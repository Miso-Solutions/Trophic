using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Trophic.Core.Models;

namespace Trophic.Converters;

public sealed class TrophyStatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush NotEarnedBrush = new(Color.FromArgb(0, 0, 0, 0)); // Transparent
    private static readonly SolidColorBrush EarnedBrush = new(Color.FromArgb(26, 26, 97, 93)); // Pine Teal tint
    private static readonly SolidColorBrush SyncedBrush = new(Color.FromArgb(26, 208, 160, 84)); // Ginger Honey tint

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is TrophyRowStatus status ? status switch
        {
            TrophyRowStatus.Earned => EarnedBrush,
            TrophyRowStatus.Synced => SyncedBrush,
            _ => NotEarnedBrush
        } : NotEarnedBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
