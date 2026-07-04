namespace Trophic.Core.Licensing;

/// <summary>
/// Master rollout switch for Catalog monetization.
///
/// OFF (default): the Catalog downloads directly from the trophy-database repo and is
/// free — the interim behaviour shipped before the license server is deployed.
///
/// ON: downloads are authorized by the license server (no client-side repo token) and
/// gated behind a valid license. Flip this on only once the server is live and the
/// client's TROPHIC_LICENSE_URL points at it.
///
/// Owner: Oren. Default: OFF. Rollout: enable after server deployment. Cleanup: once
/// licensing is permanently on, this flag and the interim direct-download path are removed.
/// Override at runtime with environment variable TROPHIC_LICENSING=on.
/// </summary>
public static class LicensingOptions
{
    public static bool Enabled =>
        string.Equals(
            Environment.GetEnvironmentVariable("TROPHIC_LICENSING"),
            "on",
            StringComparison.OrdinalIgnoreCase);
}
