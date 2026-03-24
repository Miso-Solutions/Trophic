namespace Trophic.Core;

public static class ErrorCodes
{
    // File I/O
    public const string FileTroptrnsNotFound = "E-FILE-001";
    public const string FileParamPfdNotFound = "E-FILE-002";
    public const string FileSfoNotFound = "E-FILE-003";
    public const string FileSaveFailed = "E-FILE-004";
    public const string FileOpenFailed = "E-FILE-005";
    public const string FileProfileNotFound = "E-FILE-006";

    // Format Validation
    public const string FmtDecryptionInvalid = "E-FMT-001";
    public const string FmtInvalidTrophyFile = "E-FMT-002";

    // Trophy State
    public const string TrophyAlreadyEarned = "E-TROPHY-001";
    public const string TrophyAlreadySynced = "E-TROPHY-002";
    public const string TrophyNotFound = "E-TROPHY-003";
    public const string TrophySyncTime = "E-TROPHY-004";
    public const string TrophyPlatinumFailed = "E-TROPHY-005";
    public const string TrophyUnlockFailed = "E-TROPHY-006";

    // Network / Scraping
    public const string NetImportFailed = "E-NET-001";
    public const string NetNoTimestamps = "E-NET-002";

}
