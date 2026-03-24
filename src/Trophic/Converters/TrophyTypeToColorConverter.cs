using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Trophic.Converters;

public sealed class TrophyTypeToColorConverter : IValueConverter
{
    // V7 — warm metallic backgrounds
    private static readonly SolidColorBrush PlatinumBg = new(Color.FromRgb(184, 204, 223));  // #B8CCDF warm blue platinum
    private static readonly SolidColorBrush GoldBg = new(Color.FromRgb(232, 208, 128));      // #E8D080 warm gold
    private static readonly SolidColorBrush SilverBg = new(Color.FromRgb(192, 196, 202));    // #C0C4CA warm silver
    private static readonly SolidColorBrush BronzeBg = new(Color.FromRgb(212, 172, 128));    // #D4AC80 warm bronze
    private static readonly SolidColorBrush DefaultBg = new(Color.FromRgb(208, 210, 213));   // #D0D2D5 warm neutral

    // Dark text colors matched to each metal — all 6:1+ contrast on their background
    private static readonly SolidColorBrush PlatinumFg = new(Color.FromRgb(14, 37, 64));     // #0E2540 → 7.5:1
    private static readonly SolidColorBrush GoldFg = new(Color.FromRgb(56, 44, 0));          // #382C00 → 7.2:1
    private static readonly SolidColorBrush SilverFg = new(Color.FromRgb(29, 37, 48));       // #1D2530 → 6.5:1
    private static readonly SolidColorBrush BronzeFg = new(Color.FromRgb(56, 29, 0));        // #381D00 → 6.0:1
    private static readonly SolidColorBrush DefaultFg = new(Color.FromRgb(29, 34, 40));      // #1D2228 → 7.2:1

    static TrophyTypeToColorConverter()
    {
        PlatinumBg.Freeze(); PlatinumFg.Freeze();
        GoldBg.Freeze(); GoldFg.Freeze();
        SilverBg.Freeze(); SilverFg.Freeze();
        BronzeBg.Freeze(); BronzeFg.Freeze();
        DefaultBg.Freeze(); DefaultFg.Freeze();
    }

    /// <summary>
    /// parameter=null or "Background" → badge background brush.
    /// parameter="Foreground" → badge text brush.
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isForeground = parameter is string s && s == "Foreground";
        return value is string code ? code switch
        {
            "P" => isForeground ? PlatinumFg : PlatinumBg,
            "G" => isForeground ? GoldFg : GoldBg,
            "S" => isForeground ? SilverFg : SilverBg,
            "B" => isForeground ? BronzeFg : BronzeBg,
            _ => isForeground ? DefaultFg : DefaultBg
        } : isForeground ? DefaultFg : DefaultBg;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
