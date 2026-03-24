using System.Globalization;
using System.Windows.Data;

namespace Trophic.Converters;

public sealed class FractionToWidthConverter : IMultiValueConverter
{
    public double MaxWidth { get; set; } = 80;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is int earned && values[1] is int total && total > 0)
            return Math.Max(0, Math.Min(MaxWidth, (double)earned / total * MaxWidth));

        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
