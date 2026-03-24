namespace Trophic.Core.Helpers;

public static class RandomHelper
{
    public static DateTime NextDateTime(DateTime min, DateTime max)
    {
        long range = max.Ticks - min.Ticks;
        if (range <= 0) return min;
        long randomTicks = Random.Shared.NextInt64(0, range);
        return new DateTime(min.Ticks + randomTicks, min.Kind);
    }

    public static TimeSpan NextTimeSpan(int minMinutes, int maxMinutes)
    {
        if (maxMinutes <= minMinutes) return TimeSpan.FromMinutes(minMinutes);
        int minutes = Random.Shared.Next(minMinutes, maxMinutes);
        int seconds = Random.Shared.Next(0, 60);
        return new TimeSpan(0, minutes, seconds);
    }
}
