using System.Globalization;
using System.Windows.Data;

namespace Trophic.Converters;

/// Renders a TimeZoneInfo using its CURRENT effective offset (GetUtcOffset at now),
/// not the static standard-time DisplayName which never reflects active DST.
public sealed class TimeZoneLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not TimeZoneInfo tz) return string.Empty;

        var offset = tz.GetUtcOffset(DateTime.UtcNow);
        var sign = offset.Ticks >= 0 ? "+" : "-";
        var abs = offset.Duration();
        var offsetText = $"(UTC{sign}{abs.Hours:00}:{abs.Minutes:00})";

        var name = StripLeadingOffset(tz.DisplayName);
        return $"{offsetText} {name}";
    }

    private static string StripLeadingOffset(string displayName)
    {
        if (displayName.StartsWith("(UTC", StringComparison.Ordinal))
        {
            var close = displayName.IndexOf(')');
            if (close > 0 && close + 1 < displayName.Length)
                return displayName[(close + 1)..].TrimStart();
        }
        return displayName;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
