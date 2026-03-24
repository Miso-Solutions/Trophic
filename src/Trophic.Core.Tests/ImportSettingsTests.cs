using Trophic.Core.Models;

namespace Trophic.Core.Tests;

public class ImportSettingsTests
{
    [Fact]
    public void ApplyTo_AsIs_ReturnsOriginalTimestamp()
    {
        var settings = new ImportSettings { Mode = ImportMode.AsIs };
        long ts = 1423756800; // 2015-02-12 12:00:00 UTC

        Assert.Equal(ts, settings.ApplyTo(ts));
    }

    [Fact]
    public void ApplyTo_Offset_AddsCorrectly()
    {
        var settings = new ImportSettings
        {
            Mode = ImportMode.Offset,
            OffsetYears = 1,
            OffsetDays = 5,
            OffsetHours = 3
        };

        // Use a known timestamp and verify the offset is applied
        var baseDt = new DateTime(2015, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        long ts = new DateTimeOffset(baseDt).ToUnixTimeSeconds();
        long result = settings.ApplyTo(ts);
        var resultDt = DateTimeOffset.FromUnixTimeSeconds(result).UtcDateTime;

        Assert.Equal(2016, resultDt.Year);
        Assert.Equal(6, resultDt.Month);
        Assert.Equal(20, resultDt.Day);   // 15 + 5
        Assert.Equal(13, resultDt.Hour);  // 10 + 3
    }

    [Fact]
    public void ApplyTo_FixedValues_OverridesSpecifiedFields()
    {
        var settings = new ImportSettings
        {
            Mode = ImportMode.FixedValues,
            OverrideYear = true,
            FixedYear = 2020,
            OverrideHour = true,
            FixedHour = 10
        };

        long ts = 1423756800; // 2015-02-12 12:00:00 UTC
        long result = settings.ApplyTo(ts);
        var resultDt = DateTimeOffset.FromUnixTimeSeconds(result).UtcDateTime;

        Assert.Equal(2020, resultDt.Year);
        Assert.Equal(2, resultDt.Month); // Unchanged
        Assert.Equal(10, resultDt.Hour); // Overridden
    }

    [Fact]
    public void ApplyTo_FixedValues_ClampsDayToMaxForMonth()
    {
        var settings = new ImportSettings
        {
            Mode = ImportMode.FixedValues,
            OverrideMonth = true,
            FixedMonth = 2,
            OverrideDay = true,
            FixedDay = 31 // February doesn't have 31 days
        };

        long ts = 1423756800; // 2015-02-12
        long result = settings.ApplyTo(ts);
        var resultDt = DateTimeOffset.FromUnixTimeSeconds(result).UtcDateTime;

        Assert.Equal(28, resultDt.Day); // Clamped to Feb max
    }

    [Fact]
    public void ApplyStartTime_ShiftsAllTimestamps()
    {
        var settings = new ImportSettings
        {
            Mode = ImportMode.StartTime,
            StartTimeUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var timestamps = new List<ScrapedTimestamp>
        {
            new(0, 1000),
            new(1, 2000),
            new(2, 3000)
        };

        var result = settings.ApplyStartTime(timestamps);
        long targetUnix = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        long delta = targetUnix - 1000; // earliest is 1000

        Assert.Equal(targetUnix, result[0].UnixTimestamp);
        Assert.Equal(2000 + delta, result[1].UnixTimestamp);
        Assert.Equal(3000 + delta, result[2].UnixTimestamp);
    }

    [Fact]
    public void ApplyStartTime_NullStartTime_ReturnsOriginal()
    {
        var settings = new ImportSettings { Mode = ImportMode.StartTime, StartTimeUtc = null };
        var timestamps = new List<ScrapedTimestamp> { new(0, 1000) };

        var result = settings.ApplyStartTime(timestamps);
        Assert.Equal(1000, result[0].UnixTimestamp);
    }

    [Fact]
    public void ApplyStartTime_PreservesZeroTimestamps()
    {
        var settings = new ImportSettings
        {
            Mode = ImportMode.StartTime,
            StartTimeUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var timestamps = new List<ScrapedTimestamp>
        {
            new(0, 0),      // Unearned — should stay 0
            new(1, 1000),
            new(2, 2000)
        };

        var result = settings.ApplyStartTime(timestamps);
        Assert.Equal(0, result[0].UnixTimestamp);
    }
}
