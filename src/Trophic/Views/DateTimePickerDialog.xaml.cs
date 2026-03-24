using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Trophic.Views;

public partial class DateTimePickerDialog : Window
{
    public DateTime SelectedDateTime { get; set; } = DateTime.Now;
    private DateTime? _previousDate;

    public DateTimePickerDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _previousDate = SelectedDateTime.Date;
        DatePickerControl.SelectedDate = SelectedDateTime.Date;
        HourBox.Text = SelectedDateTime.Hour.ToString();
        MinuteBox.Text = SelectedDateTime.Minute.ToString();
        SecondBox.Text = SelectedDateTime.Second.ToString();

        DatePickerControl.SelectedDateChanged += DatePicker_SelectedDateChanged;
    }

    private void DatePicker_SelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selected = DatePickerControl.SelectedDate;
        if (selected == null || selected.Value.Date <= DateTime.Today)
        {
            _previousDate = selected;
            if (FutureWarning != null) FutureWarning.Visibility = Visibility.Collapsed;
            return;
        }

        _previousDate = selected;
        if (FutureWarning != null) FutureWarning.Visibility = Visibility.Visible;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var date = DatePickerControl.SelectedDate ?? DateTime.Today;
        int.TryParse(HourBox.Text, out int hour);
        int.TryParse(MinuteBox.Text, out int minute);
        int.TryParse(SecondBox.Text, out int second);

        hour = Math.Clamp(hour, 0, 23);
        minute = Math.Clamp(minute, 0, 59);
        second = Math.Clamp(second, 0, 59);

        SelectedDateTime = new DateTime(date.Year, date.Month, date.Day, hour, minute, second);
        DialogResult = true;
    }

    private void TodayButton_Click(object sender, RoutedEventArgs e)
    {
        var now = DateTime.Now;
        DatePickerControl.SelectedDate = now.Date;
        HourBox.Text = now.Hour.ToString();
        MinuteBox.Text = now.Minute.ToString();
        SecondBox.Text = now.Second.ToString();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

/// <summary>
/// Returns true when the bound date (CalendarDayButton.DataContext) is after today.
/// Used to grey out future dates in the calendar popup.
/// </summary>
public sealed class FutureDateConverter : IMultiValueConverter
{
    public static readonly FutureDateConverter Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 1 && values[0] is DateTime date)
            return date.Date > DateTime.Today;
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
