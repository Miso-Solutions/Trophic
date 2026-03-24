using Trophic.Core.Helpers;

namespace Trophic.Core.Tests;

public class FileHelperTests : IDisposable
{
    private readonly string _testDir;

    public FileHelperTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "TrophicTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); }
        catch { }
    }

    [Fact]
    public void CopyTrophyDirToTemp_CopiesAllFiles()
    {
        // Arrange: create test files
        File.WriteAllText(Path.Combine(_testDir, "TROPTRNS.DAT"), "test1");
        File.WriteAllText(Path.Combine(_testDir, "TROPUSR.DAT"), "test2");
        File.WriteAllText(Path.Combine(_testDir, "TROP000.PNG"), "icon");

        // Act
        var tempPath = FileHelper.CopyTrophyDirToTemp(_testDir);

        try
        {
            // Assert
            Assert.True(Directory.Exists(tempPath));
            Assert.True(File.Exists(Path.Combine(tempPath, "TROPTRNS.DAT")));
            Assert.True(File.Exists(Path.Combine(tempPath, "TROPUSR.DAT")));
            Assert.True(File.Exists(Path.Combine(tempPath, "TROP000.PNG")));
            Assert.Equal("test1", File.ReadAllText(Path.Combine(tempPath, "TROPTRNS.DAT")));
        }
        finally
        {
            FileHelper.DeleteTempDirectory(tempPath);
        }
    }

    [Fact]
    public void CopyTempBackToSource_OnlyCopiesSaveableExtensions()
    {
        // Arrange: create temp dir with mixed files
        var tempDir = Path.Combine(Path.GetTempPath(), "Trophic", Guid.NewGuid().ToString("N"), "test");
        Directory.CreateDirectory(tempDir);

        var destDir = Path.Combine(_testDir, "dest");
        Directory.CreateDirectory(destDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "TROPTRNS.DAT"), "modified");
            File.WriteAllText(Path.Combine(tempDir, "PARAM.SFO"), "sfo");
            File.WriteAllText(Path.Combine(tempDir, "PARAM.PFD"), "pfd");
            File.WriteAllText(Path.Combine(tempDir, "TROP000.PNG"), "icon_should_not_copy");

            // Act
            FileHelper.CopyTempBackToSource(tempDir, destDir);

            // Assert: DAT, SFO, PFD are copied; PNG is not
            Assert.True(File.Exists(Path.Combine(destDir, "TROPTRNS.DAT")));
            Assert.True(File.Exists(Path.Combine(destDir, "PARAM.SFO")));
            Assert.True(File.Exists(Path.Combine(destDir, "PARAM.PFD")));
            Assert.False(File.Exists(Path.Combine(destDir, "TROP000.PNG")));
        }
        finally
        {
            FileHelper.DeleteTempDirectory(tempDir);
        }
    }

    [Fact]
    public void DeleteTempDirectory_CleansTrophicParent()
    {
        var parent = Path.Combine(Path.GetTempPath(), "Trophic", Guid.NewGuid().ToString("N"));
        var child = Path.Combine(parent, "trophies");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(child, "test.dat"), "data");

        FileHelper.DeleteTempDirectory(child);

        Assert.False(Directory.Exists(parent));
    }

    [Fact]
    public void DeleteTempDirectory_DoesNotThrowOnMissingDir()
    {
        // Should not throw even for non-existent paths
        FileHelper.DeleteTempDirectory(Path.Combine(Path.GetTempPath(), "Trophic", "nonexistent", "path"));
    }
}
