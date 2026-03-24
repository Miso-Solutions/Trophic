namespace Trophic.TrophyFormat.Crypto;

/// <summary>
/// Hardcoded PS3 trophy encryption keys used for PFD operations.
/// </summary>
public static class Ps3TrophyKeys
{
    /// <summary>AES-128 key for PFD header encryption and entry key decryption.</summary>
    public static readonly byte[] SysconManagerKey = Convert.FromHexString("D413B89663E1FE9F75143D3BB4565274");

    /// <summary>HMAC-SHA1 key for PFD version 4 real key derivation.</summary>
    public static readonly byte[] KeygenKey = Convert.FromHexString("6B1ACEA246B745FD8F93763B920594CD53483B82");

    /// <summary>Per-file HMAC key for TROPTRNS.DAT.</summary>
    public static readonly byte[] TroptrnsDatKey = Convert.FromHexString("91EE81555ACC1C4FB5AAE5462CFE1C62A4AF36A5");

    /// <summary>Per-file HMAC key for TROPSYS.DAT.</summary>
    public static readonly byte[] TropsysDatKey = Convert.FromHexString("B080C40FF358643689281736A6BF15892CFEA436");

    /// <summary>Per-file HMAC key for TROPUSR.DAT.</summary>
    public static readonly byte[] TropusrDatKey = Convert.FromHexString("8711EFF406913F0937F115FAB23DE1A9897A789A");

    /// <summary>Per-file HMAC key for TROPCONF.SFM.</summary>
    public static readonly byte[] TropconfSfmKey = Convert.FromHexString("E2ED33C71C444EEBC1E23D635AD8E82F4ECA4E94");

    /// <summary>Per-file HMAC key for PARAM.SFO (trophy).</summary>
    public static readonly byte[] TrophyParamSfoKey = Convert.FromHexString("5D5B647917024E9BB8D330486B996E795D7F4392");

    /// <summary>
    /// Returns the per-file HMAC key for the given file name.
    /// </summary>
    public static byte[]? GetFileKey(string fileName)
    {
        return fileName.ToUpperInvariant() switch
        {
            "TROPTRNS.DAT" => TroptrnsDatKey,
            "TROPSYS.DAT" => TropsysDatKey,
            "TROPUSR.DAT" => TropusrDatKey,
            "TROPCONF.SFM" => TropconfSfmKey,
            "PARAM.SFO" => TrophyParamSfoKey,
            _ => null
        };
    }
}
