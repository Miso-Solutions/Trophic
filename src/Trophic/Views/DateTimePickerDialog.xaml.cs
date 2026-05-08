using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Trophic.Views;

public partial class DateTimePickerDialog : Window
{
    public DateTime SelectedDateTime { get; set; } = DateTime.Now;
    public DateTime Now { get; set; } = DateTime.Now;
    private DateTime? _previousDate;

    public DateTimePickerDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FutureDateConverter.ReferenceToday = Now.Date;
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
        if (selected == null || selected.Value.Date <= Now.Date)
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
        DatePickerControl.SelectedDate = Now.Date;
        HourBox.Text = Now.Hour.ToString();
        MinuteBox.Text = Now.Minute.ToString();
        SecondBox.Text = Now.Second.ToString();
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

    /// Set by DateTimePickerDialog to the display-timezone's current date so the
    /// calendar grey-out matches the user-selected timezone, not the system clock.
    public static DateTime ReferenceToday { get; set; } = DateTime.Today;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 1 && values[0] is DateTime date)
            return date.Date > ReferenceToday;
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
