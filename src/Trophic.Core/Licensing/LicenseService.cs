using System.Runtime.Versioning;

namespace Trophic.Core.Licensing;

/// <summary>
/// Coordinates local license storage, offline assertion verification, and server
/// activation/refresh. Entitlement rules:
///
/// 1. Assertion signature must verify against the embedded public key.
/// 2. The license itself must not be past its <c>licenseExpiresAt</c> (if set).
/// 3. The assertion is valid until its own <c>exp</c> (server-controlled, ~14 days).
///    A revoked or expired license stops minting fresh assertions, so the client
///    loses entitlement within that window — this is the revocation propagation delay.
/// 4. Past the assertion <c>exp</c>, a short offline grace window keeps a previously
///    valid license working through brief outages; beyond that, the Catalog locks
///    until the client can refresh against the server again.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LicenseService
{
    // Extra days a previously valid (but now assertion-expired) license keeps working
    // while offline, before the Catalog hard-locks pending a successful refresh.
    private const int OfflineGraceDays = 7;

    private readonly LicenseStore _store;
    private readonly LicenseServerClient _server;
    private readonly Func<DateTimeOffset> _clock;

    public LicenseService(string baseDirectory, LicenseServerClient? server = null, Func<DateTimeOffset>? clock = null)
    {
        _store = new LicenseStore(baseDirectory);
        _server = server ?? new LicenseServerClient();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Current entitlement derived purely from local state (no network). Safe to call
    /// frequently (e.g. to gate a button).
    /// </summary>
    public LicenseStatus GetStatus()
    {
        var record = _store.Load();
        if (record == null) return LicenseStatus.NotLicensed;

        var assertion = AssertionVerifier.Verify(record.Assertion);
        if (assertion == null) return LicenseStatus.NotLicensed;

        var now = _clock();
        var nowUnix = now.ToUnixTimeSeconds();

        DateTimeOffset? licenseExpiresAt = assertion.LicenseExpiresAtUnix is { } lx
            ? DateTimeOffset.FromUnixTimeSeconds(lx) : null;
        var lastVerified = DateTimeOffset.FromUnixTimeSeconds(record.LastVerifiedAtUnix);

        // License itself expired (server would refuse a refresh until extended).
        if (assertion.LicenseExpiresAtUnix is { } lexp && nowUnix > lexp)
            return new LicenseStatus(LicenseState.Expired, assertion.Plan, assertion.LicenseKey, licenseExpiresAt, lastVerified);

        // Assertion still fresh — fully entitled.
        if (nowUnix <= assertion.AssertionExpiresAtUnix)
            return new LicenseStatus(LicenseState.Active, assertion.Plan, assertion.LicenseKey, licenseExpiresAt, lastVerified);

        // Assertion expired: allow a bounded offline grace past the assertion exp.
        long graceLimit = assertion.AssertionExpiresAtUnix + (long)OfflineGraceDays * 86400;
        if (nowUnix <= graceLimit)
            return new LicenseStatus(LicenseState.Active, assertion.Plan, assertion.LicenseKey, licenseExpiresAt, lastVerified);

        return new LicenseStatus(LicenseState.GracePeriodLapsed, assertion.Plan, assertion.LicenseKey, licenseExpiresAt, lastVerified);
    }

    public bool IsEntitled() => GetStatus().IsEntitled;

    /// <summary>
    /// Activates a key against the server, binding this device, and persists the
    /// returned assertion. Used by the "Enter license" dialog.
    /// </summary>
    public async Task<LicenseOperationResult> ActivateAsync(string licenseKey, CancellationToken ct = default)
    {
        var key = NormalizeKey(licenseKey);
        if (string.IsNullOrWhiteSpace(key))
            return LicenseOperationResult.Fail("Enter a license key.");

        var response = await _server.ActivateAsync(key, DeviceIdentity.Get(), ct);
        return PersistFromResponse(key, response);
    }

    /// <summary>
    /// Re-validates the stored license against the server and refreshes the cached
    /// assertion. Intended to run quietly at startup / on a schedule. On a network
    /// failure the existing local status is preserved (offline grace applies).
    /// </summary>
    public async Task<LicenseOperationResult> RefreshAsync(CancellationToken ct = default)
    {
        var record = _store.Load();
        if (record == null) return LicenseOperationResult.Fail("No license to refresh.");

        var response = await _server.RefreshAsync(record.LicenseKey, DeviceIdentity.Get(), ct);

        if (response.Success)
            return PersistFromResponse(record.LicenseKey, response);

        // Only DEFINITIVE rejections clear the local license. Network failures and
        // transient server errors (5xx, unexpected codes) must preserve it so a server
        // hiccup never wipes a paying user's activation — offline grace covers the gap.
        if (IsDefinitiveRejection(response.ErrorCode))
        {
            _store.Clear();
            return LicenseOperationResult.Fail(response.ErrorMessage ?? "License is no longer valid.");
        }

        // Preserve current entitlement; the offline grace window decides whether it counts.
        return LicenseOperationResult.Ok(GetStatus());
    }

    public void Deactivate() => _store.Clear();

    /// <summary>
    /// Asks the server for a short-lived signed download URL for a catalog trophy set.
    /// The server re-checks entitlement, so this is the authoritative paywall — a
    /// patched client cannot obtain the URL without an active, device-matched license.
    /// </summary>
    public async Task<DownloadAuthorization> RequestDownloadUrlAsync(string npwrId, CancellationToken ct = default)
    {
        var record = _store.Load();
        if (record == null)
            return DownloadAuthorization.Fail("No license. Activate a license to download from the Catalog.");

        var response = await _server.GetDownloadUrlAsync(record.LicenseKey, DeviceIdentity.Get(), npwrId, ct);
        if (response.Success && response.Url is { Length: > 0 })
            return DownloadAuthorization.Ok(response.Url);

        // A hard rejection from the server (revoked/expired/device) clears local state
        // so the UI reflects the loss of entitlement on next check.
        if (IsDefinitiveRejection(response.ErrorCode))
            _store.Clear();

        return DownloadAuthorization.Fail(response.ErrorMessage ?? "Could not authorize the download.");
    }

    private LicenseOperationResult PersistFromResponse(string key, ServerResponse response)
    {
        if (!response.Success)
            return LicenseOperationResult.Fail(response.ErrorMessage ?? "Activation failed.");

        var assertion = AssertionVerifier.Verify(response.Jwt!);
        if (assertion == null)
            return LicenseOperationResult.Fail("The server returned an assertion that failed verification.");

        // Defence in depth: the assertion's bound device must match this machine.
        var deviceId = DeviceIdentity.Get();
        if (!string.IsNullOrEmpty(assertion.DeviceId) && assertion.DeviceId != deviceId)
            return LicenseOperationResult.Fail("The license assertion is bound to a different device.");

        try
        {
            _store.Save(new LicenseRecord(key, response.Jwt!, _clock().ToUnixTimeSeconds()));
        }
        catch (Exception ex)
        {
            // Disk full, permissions, antivirus lock, etc. Surface as a friendly failure
            // instead of letting it escape into an async-void UI handler and crash the app.
            return LicenseOperationResult.Fail("Could not save the license locally: " + ex.Message);
        }

        return LicenseOperationResult.Ok(GetStatus());
    }

    // Server error codes that mean the license is genuinely no longer usable, as
    // opposed to transient failures (network, 5xx) that must not clear local state.
    private static bool IsDefinitiveRejection(string? code)
        => code is "revoked" or "expired" or "device_mismatch" or "unknown_key";

    private static string NormalizeKey(string raw)
        => raw.Trim().ToUpperInvariant().Replace(" ", string.Empty);
}
