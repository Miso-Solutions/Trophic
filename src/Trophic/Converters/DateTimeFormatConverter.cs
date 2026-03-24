using System.Globalization;
using System.Windows.Data;

namespace Trophic.Converters;

public sealed class DateTimeFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTime dt && dt != DateTime.MinValue)
            return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
