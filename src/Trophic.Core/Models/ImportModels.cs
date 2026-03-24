namespace Trophic.Core.Models;

public sealed record ScrapedTimestamp(int TrophyId, long UnixTimestamp);

public enum ImportMode { AsIs, Offset, FixedValues, StartTime }

public sealed class ImportSettings
{
    public string Url { get; set; } = "";
    public ImportMode Mode { get; set; } = ImportMode.AsIs;

    // Offset mode: add these values to each timestamp
    public int OffsetYears { get; set; }
    public int OffsetMonths { get; set; }
    public int OffsetWeeks { get; set; }
    public int OffsetDays { get; set; }
    public int OffsetHours { get; set; }
    public int OffsetMinutes { get; set; }
    public int OffsetSeconds { get; set; }

    // Start time mode: shift all timestamps so the earliest one starts at this time
    public DateTime? StartTimeUtc { get; set; }

    // Fixed values mode: override specific date parts
    public bool OverrideYear { get; set; }
    public int FixedYear { get; set; } = DateTime.Now.Year;
    public bool OverrideMonth { get; set; }
    public int FixedMonth { get; set; } = 1;
    public bool OverrideDay { get; set; }
    public int FixedDay { get; set; } = 1;
    public bool OverrideHour { get; set; }
    public int FixedHour { get; set; }
    public bool OverrideMinute { get; set; }
    public int FixedMinute { get; set; }
    public bool OverrideSecond { get; set; }
    public int FixedSecond { get; set; }

    /// <summary>
    /// Applies the offset or fixed value overrides to a Unix timestamp.
    /// </summary>
    public long ApplyTo(long unixTimestamp)
    {
        if (Mode == ImportMode.AsIs) return unixTimestamp;

        var dt = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).UtcDateTime;

        if (Mode == ImportMode.Offset)
        {
            dt = dt.AddYears(OffsetYears)
                    .AddMonths(OffsetMonths)
                    .AddDays(OffsetWeeks * 7 + OffsetDays)
                    .AddHours(OffsetHours)
                    .AddMinutes(OffsetMinutes)
                    .AddSeconds(OffsetSeconds);
        }
        else if (Mode == ImportMode.FixedValues)
        {
            int year = OverrideYear ? FixedYear : dt.Year;
            int month = OverrideMonth ? FixedMonth : dt.Month;
            int day = OverrideDay ? FixedDay : dt.Day;
            int hour = OverrideHour ? FixedHour : dt.Hour;
            int minute = OverrideMinute ? FixedMinute : dt.Minute;
            int second = OverrideSecond ? FixedSecond : dt.Second;

            // Clamp day to valid range for the target month/year
            int maxDay = DateTime.DaysInMonth(year, month);
            if (day > maxDay) day = maxDay;

            dt = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
        }

        return new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeSeconds();
    }

    /// <summary>
    /// Shifts all timestamps so the earliest one starts at StartTimeUtc,
    /// preserving relative spacing between all trophies.
    /// </summary>
    public IReadOnlyList<ScrapedTimestamp> ApplyStartTime(IReadOnlyList<ScrapedTimestamp> timestamps)
    {
        if (StartTimeUtc == null || timestamps.Count == 0)
            return timestamps;

        var positive = timestamps.Where(t => t.UnixTimestamp > 0);
        if (!positive.Any()) return timestamps;
        long earliest = positive.Min(t => t.UnixTimestamp);
        long targetUnix = new DateTimeOffset(StartTimeUtc.Value, TimeSpan.Zero).ToUnixTimeSeconds();
        long delta = targetUnix - earliest;

        return timestamps
            .Select(t => t.UnixTimestamp > 0
                ? new ScrapedTimestamp(t.TrophyId, t.UnixTimestamp + delta)
                : t)
            .ToList();
    }
}
