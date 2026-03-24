namespace Trophic.Core.Tests;

public class ErrorCodesTests
{
    [Fact]
    public void FileErrorCodes_HaveExpectedPrefix()
    {
        Assert.StartsWith("E-FILE-", ErrorCodes.FileTroptrnsNotFound);
        Assert.StartsWith("E-FILE-", ErrorCodes.FileParamPfdNotFound);
        Assert.StartsWith("E-FILE-", ErrorCodes.FileSfoNotFound);
        Assert.StartsWith("E-FILE-", ErrorCodes.FileSaveFailed);
        Assert.StartsWith("E-FILE-", ErrorCodes.FileOpenFailed);
        Assert.StartsWith("E-FILE-", ErrorCodes.FileProfileNotFound);
    }

    [Fact]
    public void FormatErrorCodes_HaveExpectedPrefix()
    {
        Assert.StartsWith("E-FMT-", ErrorCodes.FmtDecryptionInvalid);
        Assert.StartsWith("E-FMT-", ErrorCodes.FmtInvalidTrophyFile);
    }

    [Fact]
    public void TrophyErrorCodes_HaveExpectedPrefix()
    {
        Assert.StartsWith("E-TROPHY-", ErrorCodes.TrophyAlreadyEarned);
        Assert.StartsWith("E-TROPHY-", ErrorCodes.TrophyAlreadySynced);
        Assert.StartsWith("E-TROPHY-", ErrorCodes.TrophyNotFound);
        Assert.StartsWith("E-TROPHY-", ErrorCodes.TrophySyncTime);
        Assert.StartsWith("E-TROPHY-", ErrorCodes.TrophyPlatinumFailed);
        Assert.StartsWith("E-TROPHY-", ErrorCodes.TrophyUnlockFailed);
    }

    [Fact]
    public void NetworkErrorCodes_HaveExpectedPrefix()
    {
        Assert.StartsWith("E-NET-", ErrorCodes.NetImportFailed);
        Assert.StartsWith("E-NET-", ErrorCodes.NetNoTimestamps);
    }

    [Fact]
    public void AllErrorCodes_AreDistinct()
    {
        var codes = new[]
        {
            ErrorCodes.FileTroptrnsNotFound,
            ErrorCodes.FileParamPfdNotFound,
            ErrorCodes.FileSfoNotFound,
            ErrorCodes.FileSaveFailed,
            ErrorCodes.FileOpenFailed,
            ErrorCodes.FileProfileNotFound,
            ErrorCodes.FmtDecryptionInvalid,
            ErrorCodes.FmtInvalidTrophyFile,
            ErrorCodes.TrophyAlreadyEarned,
            ErrorCodes.TrophyAlreadySynced,
            ErrorCodes.TrophyNotFound,
            ErrorCodes.TrophySyncTime,
            ErrorCodes.TrophyPlatinumFailed,
            ErrorCodes.TrophyUnlockFailed,
            ErrorCodes.NetImportFailed,
            ErrorCodes.NetNoTimestamps
        };

        Assert.Equal(codes.Length, codes.Distinct().Count());
    }
}
