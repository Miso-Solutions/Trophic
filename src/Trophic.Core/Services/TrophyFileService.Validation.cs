using Trophic.TrophyFormat.Parsers;

namespace Trophic.Core.Services;

public sealed partial class TrophyFileService
{
    /// <summary>
    /// Rejects a corrupt trophy folder up front. TROPCONF defines the trophy set and
    /// TROPUSR must carry exactly one time-info slot per defined trophy. A mismatch means
    /// the files are inconsistent — surfacing it here gives a clear error instead of an
    /// index-out-of-range crash on the first unlock.
    /// </summary>
    private static void ValidateFolderIntegrity(TropConfParser config, TropUsrParser usr)
    {
        if (config.Count != usr.TrophyTimeInfos.Count)
            throw new InvalidOperationException(
                $"[{ErrorCodes.FmtCountMismatch}] Corrupt trophy folder: TROPCONF defines {config.Count} " +
                $"trophies but TROPUSR contains {usr.TrophyTimeInfos.Count}. The files do not match.");
    }
}
