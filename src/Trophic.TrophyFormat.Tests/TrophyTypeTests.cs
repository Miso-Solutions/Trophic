using Trophic.TrophyFormat.Enums;

namespace Trophic.TrophyFormat.Tests;

public class TrophyTypeTests
{
    [Theory]
    [InlineData(TrophyType.Platinum, "P")]
    [InlineData(TrophyType.Gold, "G")]
    [InlineData(TrophyType.Silver, "S")]
    [InlineData(TrophyType.Bronze, "B")]
    public void ToCode_ReturnsExpected(TrophyType type, string expected)
    {
        Assert.Equal(expected, type.ToCode());
    }

    [Theory]
    [InlineData("P", TrophyType.Platinum)]
    [InlineData("G", TrophyType.Gold)]
    [InlineData("S", TrophyType.Silver)]
    [InlineData("B", TrophyType.Bronze)]
    [InlineData("p", TrophyType.Platinum)]
    [InlineData("g", TrophyType.Gold)]
    public void FromCode_ReturnsExpected(string code, TrophyType expected)
    {
        Assert.Equal(expected, TrophyTypeExtensions.FromCode(code));
    }

    [Theory]
    [InlineData("X")]
    [InlineData("")]
    [InlineData("ZZ")]
    public void FromCode_InvalidThrows(string code)
    {
        Assert.Throws<ArgumentException>(() => TrophyTypeExtensions.FromCode(code));
    }

    [Theory]
    [InlineData(TrophyType.Platinum, 180)]
    [InlineData(TrophyType.Gold, 90)]
    [InlineData(TrophyType.Silver, 30)]
    [InlineData(TrophyType.Bronze, 15)]
    public void GradePoints_ReturnsExpected(TrophyType type, int expected)
    {
        Assert.Equal(expected, type.GradePoints());
    }

    [Fact]
    public void RoundTrip_CodeToTypeAndBack()
    {
        foreach (var type in new[] { TrophyType.Platinum, TrophyType.Gold, TrophyType.Silver, TrophyType.Bronze })
        {
            var code = type.ToCode();
            var result = TrophyTypeExtensions.FromCode(code);
            Assert.Equal(type, result);
        }
    }
}
