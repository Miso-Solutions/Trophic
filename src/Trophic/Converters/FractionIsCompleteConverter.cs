using System.Globalization;
using System.Windows.Data;

namespace Trophic.Converters;

public sealed class FractionIsCompleteConverter : IMultiValueConverter
{
    private static readonly object BoxedTrue = true;
    private static readonly object BoxedFalse = false;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is int earned && values[1] is int total)
            return total > 0 && earned >= total ? BoxedTrue : BoxedFalse;
        return BoxedFalse;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
