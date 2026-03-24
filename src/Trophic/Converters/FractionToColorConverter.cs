using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Trophic.Converters;

/// <summary>
/// Converts a fraction (earned/total) to a progress color.
/// Accepts 2 or 3 values: EarnedGrade, TotalGrade, [HasPlatinum].
/// At 100%: platinum color if HasPlatinum, gold otherwise.
/// </summary>
public sealed class FractionToColorConverter : IMultiValueConverter
{
    private static readonly Color PlatinumColor = Color.FromRgb(0xA0, 0xBE, 0xD8);
    private static readonly Color GoldColor = Color.FromRgb(0xD0, 0xA0, 0x54); // Ginger Honey

    private static readonly (double stop, Color color)[] BaseStops =
    [
        (0.00, Color.FromRgb(0xCC, 0x22, 0x22)), // red
        (0.25, Color.FromRgb(0xD8, 0x50, 0x0C)), // orange
        (0.50, Color.FromRgb(0xD0, 0xA0, 0x54)), // Ginger Honey
        (0.75, Color.FromRgb(0x52, 0x7D, 0x65)), // teal-shifted green
    ];

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        double fraction = 0;
        if (values.Length >= 2 && values[0] is int earned && values[1] is int total && total > 0)
            fraction = Math.Clamp((double)earned / total, 0, 1);

        bool hasPlatinum = values.Length >= 3 && values[2] is bool hp && hp;
        var finalColor = hasPlatinum ? PlatinumColor : GoldColor;

        return new SolidColorBrush(Interpolate(fraction, finalColor));
    }

    private static Color Interpolate(double t, Color finalColor)
    {
        var stops = new (double stop, Color color)[]
        {
            BaseStops[0], BaseStops[1], BaseStops[2], BaseStops[3],
            (1.00, finalColor)
        };

        for (int i = 0; i < stops.Length - 1; i++)
        {
            if (t <= stops[i + 1].stop)
            {
                double local = (t - stops[i].stop) / (stops[i + 1].stop - stops[i].stop);
                return Lerp(stops[i].color, stops[i + 1].color, local);
            }
        }

        return finalColor;
    }

    private static Color Lerp(Color a, Color b, double t)
    {
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
