using Trophic.Core.Services;

namespace Trophic.Core.Tests;

public class ProfileServiceTests
{
    private readonly ProfileService _service = new();

    [Theory]
    [InlineData("MyProfile")]
    [InlineData("User_123")]
    [InlineData("test")]
    public void GetProfilePath_ValidName_DoesNotThrow(string name)
    {
        // Should not throw — path may not exist, but validation passes
        var result = _service.GetProfilePath(name);
        // Returns null because the profile doesn't exist on disk
        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SaveProfile_EmptyName_Throws(string name)
    {
        Assert.ThrowsAny<ArgumentException>(() => _service.SaveProfile(name, "dummy.sfo"));
    }

    [Theory]
    [InlineData("..\\..\\evil")]
    [InlineData("../etc/passwd")]
    [InlineData("foo/bar")]
    [InlineData("foo\\bar")]
    [InlineData("test..test")]
    public void SaveProfile_PathTraversal_Throws(string name)
    {
        Assert.ThrowsAny<ArgumentException>(() => _service.SaveProfile(name, "dummy.sfo"));
    }

    [Theory]
    [InlineData("file.exe")]
    [InlineData("name.txt")]
    public void SaveProfile_NameWithExtension_Throws(string name)
    {
        // Names with extensions fail because Path.GetFileNameWithoutExtension strips the extension
        Assert.ThrowsAny<ArgumentException>(() => _service.SaveProfile(name, "dummy.sfo"));
    }

    [Fact]
    public void DeleteProfile_EmptyName_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => _service.DeleteProfile(""));
    }

    [Fact]
    public void DeleteProfile_PathTraversal_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => _service.DeleteProfile("..\\..\\evil"));
    }

    [Fact]
    public void GetProfileNames_ReturnsListWithoutError()
    {
        var names = _service.GetProfileNames();
        Assert.NotNull(names);
    }
}
