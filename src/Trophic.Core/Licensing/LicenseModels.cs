namespace Trophic.Core.Licensing;

/// <summary>
/// High-level entitlement state derived from the cached, verified assertion.
/// </summary>
public enum LicenseState
{
    /// No key stored, or the stored assertion failed verification.
    None,

    /// A valid, unexpired assertion is present — the user is entitled.
    Active,

    /// The assertion or the license inside it has expired.
    Expired,

    /// The assertion is valid but the offline grace window has lapsed and a
    /// server refresh has not succeeded recently. Treated as not entitled until
    /// the client reaches the server again.
    GracePeriodLapsed,
}

/// <summary>
/// The claims carried by a verified server assertion.
/// </summary>
public sealed record LicenseAssertion(
    string LicenseKey,
    string Plan,
    long? LicenseExpiresAtUnix,
    string? DeviceId,
    long IssuedAtUnix,
    long AssertionExpiresAtUnix);

/// <summary>
/// The current entitlement snapshot the UI binds against.
/// </summary>
public sealed record LicenseStatus(
    LicenseState State,
    string? Plan,
    string? LicenseKey,
    DateTimeOffset? LicenseExpiresAt,
    DateTimeOffset? LastVerifiedAt)
{
    public bool IsEntitled => State == LicenseState.Active;

    public static LicenseStatus NotLicensed { get; } =
        new(LicenseState.None, null, null, null, null);
}

/// <summary>
/// Outcome of an activation or refresh attempt against the server.
/// </summary>
public sealed record LicenseOperationResult(bool Success, LicenseStatus Status, string? ErrorMessage)
{
    public static LicenseOperationResult Ok(LicenseStatus status) => new(true, status, null);
    public static LicenseOperationResult Fail(string error) => new(false, LicenseStatus.NotLicensed, error);
}

/// <summary>
/// Result of a server-authorized download request: a short-lived signed URL, or an error.
/// </summary>
public sealed record DownloadAuthorization(bool Success, string? Url, string? ErrorMessage)
{
    public static DownloadAuthorization Ok(string url) => new(true, url, null);
    public static DownloadAuthorization Fail(string error) => new(false, null, error);
}
