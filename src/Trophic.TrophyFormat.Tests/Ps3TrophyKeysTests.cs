using Trophic.TrophyFormat.Crypto;

namespace Trophic.TrophyFormat.Tests;

public class Ps3TrophyKeysTests
{
    [Fact]
    public void SysconManagerKey_Is16Bytes()
    {
        Assert.Equal(16, Ps3TrophyKeys.SysconManagerKey.Length);
    }

    [Fact]
    public void KeygenKey_Is20Bytes()
    {
        Assert.Equal(20, Ps3TrophyKeys.KeygenKey.Length);
    }

    [Theory]
    [InlineData("TROPTRNS.DAT")]
    [InlineData("TROPSYS.DAT")]
    [InlineData("TROPUSR.DAT")]
    [InlineData("TROPCONF.SFM")]
    [InlineData("PARAM.SFO")]
    public void GetFileKey_KnownFilesReturn20ByteKey(string fileName)
    {
        var key = Ps3TrophyKeys.GetFileKey(fileName);
        Assert.NotNull(key);
        Assert.Equal(20, key!.Length);
    }

    [Theory]
    [InlineData("troptrns.dat")]
    [InlineData("Troptrns.DAT")]
    public void GetFileKey_CaseInsensitive(string fileName)
    {
        var key = Ps3TrophyKeys.GetFileKey(fileName);
        Assert.NotNull(key);
    }

    [Theory]
    [InlineData("UNKNOWN.DAT")]
    [InlineData("")]
    [InlineData("ICON0.PNG")]
    public void GetFileKey_UnknownFileReturnsNull(string fileName)
    {
        Assert.Null(Ps3TrophyKeys.GetFileKey(fileName));
    }

    [Fact]
    public void GetFileKey_EachFileHasDistinctKey()
    {
        var files = new[] { "TROPTRNS.DAT", "TROPSYS.DAT", "TROPUSR.DAT", "TROPCONF.SFM", "PARAM.SFO" };
        var keys = files.Select(f => Ps3TrophyKeys.GetFileKey(f)!).ToList();

        // All keys should be distinct
        for (int i = 0; i < keys.Count; i++)
        {
            for (int j = i + 1; j < keys.Count; j++)
            {
                Assert.False(keys[i].SequenceEqual(keys[j]),
                    $"Keys for {files[i]} and {files[j]} should be distinct");
            }
        }
    }
}
