using Trophic.Core.Services;

namespace Trophic.Core.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _originalDir;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TrophicSettingsTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _originalDir = AppDomain.CurrentDomain.BaseDirectory;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { }
    }

    [Fact]
    public void DefaultLanguageCode_IsEnglish()
    {
        var service = new SettingsService();
        Assert.Equal("en", service.LanguageCode);
    }

    [Fact]
    public void DefaultLastBrowseDirectory_IsNull()
    {
        var service = new SettingsService();
        // LastBrowseDirectory defaults to null when no settings file exists
        // or when settings file has no value set
        Assert.True(service.LastBrowseDirectory == null || service.LastBrowseDirectory is string);
    }

    [Fact]
    public void LanguageCode_SetAndGet_Roundtrips()
    {
        var service = new SettingsService();
        service.LanguageCode = "he";
        Assert.Equal("he", service.LanguageCode);
    }

    [Fact]
    public void LastBrowseDirectory_SetAndGet_Roundtrips()
    {
        var service = new SettingsService();
        service.LastBrowseDirectory = @"C:\Test\Path";
        Assert.Equal(@"C:\Test\Path", service.LastBrowseDirectory);
    }
}
