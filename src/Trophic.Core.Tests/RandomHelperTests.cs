using Trophic.Core.Helpers;

namespace Trophic.Core.Tests;

public class RandomHelperTests
{
    [Fact]
    public void NextDateTime_ResultWithinBounds()
    {
        var min = new DateTime(2020, 1, 1);
        var max = new DateTime(2025, 12, 31);

        for (int i = 0; i < 100; i++)
        {
            var result = RandomHelper.NextDateTime(min, max);
            Assert.InRange(result, min, max);
        }
    }

    [Fact]
    public void NextDateTime_MinEqualsMax_ReturnsMin()
    {
        var dt = new DateTime(2023, 6, 15, 12, 0, 0);
        var result = RandomHelper.NextDateTime(dt, dt);
        Assert.Equal(dt, result);
    }

    [Fact]
    public void NextDateTime_MinGreaterThanMax_ReturnsMin()
    {
        var min = new DateTime(2025, 1, 1);
        var max = new DateTime(2020, 1, 1);
        var result = RandomHelper.NextDateTime(min, max);
        Assert.Equal(min, result);
    }

    [Fact]
    public void NextTimeSpan_ResultWithinBounds()
    {
        for (int i = 0; i < 100; i++)
        {
            var result = RandomHelper.NextTimeSpan(5, 30);
            Assert.True(result.TotalMinutes >= 5);
            Assert.True(result.TotalMinutes < 31); // max minutes + up to 59 seconds
        }
    }

    [Fact]
    public void NextTimeSpan_MinEqualsMax_ReturnsFallback()
    {
        var result = RandomHelper.NextTimeSpan(10, 10);
        Assert.Equal(10, result.TotalMinutes);
    }
}
