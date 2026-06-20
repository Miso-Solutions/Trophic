namespace Trophic.TrophyFormat.Crypto;

/// <summary>
/// Hardcoded PS3 PFD keys for generic savegame (non-trophy) mode.
/// Derived by XOR-decoding the obfuscated key set distributed with Apollo Save Tool's
/// pfd_util.c; verified equivalent to the trophy keys already in <see cref="Ps3TrophyKeys"/>.
/// </summary>
public static class Ps3SaveKeys
{
    /// <summary>HMAC-SHA1 key for PARAM.SFO in savegame mode (PFD_ENTRY_HASH_FILE).</summary>
    public static readonly byte[] SavegameParamSfoKey = Convert.FromHexString("0C08000E090504040D010F000406020209060D03");

    /// <summary>
    /// Fallback disc hash key used when a game-specific disc_hash_key is not known.
    /// Many but not all retail saves work with this fallback.
    /// </summary>
    public static readonly byte[] FallbackDiscHashKey = Convert.FromHexString("D1C1E10B9C547E689B805DCD9710CE8D");
}
