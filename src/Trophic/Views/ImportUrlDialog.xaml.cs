using System.Windows;
using System.Windows.Input;
using Trophic.Core.Models;

namespace Trophic.Views;

public partial class ImportUrlDialog : Window
{
    public ImportSettings? Settings { get; private set; }

    public ImportUrlDialog()
    {
        InitializeComponent();
        FixYear.Text = DateTime.Now.Year.ToString();
        StartDate.SelectedDate = DateTime.Today;
        Loaded += (_, _) => UrlBox.Focus();
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim();
        if (string.IsNullOrEmpty(url))
        {
            UrlBox.Focus();
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            UrlError.Visibility = Visibility.Visible;
            UrlBox.Focus();
            return;
        }

        var settings = new ImportSettings { Url = url };

        if (ModeOffset.IsChecked == true)
        {
            settings.Mode = ImportMode.Offset;
            settings.OffsetYears = ParseInt(OffYears.Text);
            settings.OffsetMonths = ParseInt(OffMonths.Text);
            settings.OffsetWeeks = ParseInt(OffWeeks.Text);
            settings.OffsetDays = ParseInt(OffDays.Text);
            settings.OffsetHours = ParseInt(OffHours.Text);
            settings.OffsetMinutes = ParseInt(OffMinutes.Text);
            settings.OffsetSeconds = ParseInt(OffSeconds.Text);
        }
        else if (ModeFixed.IsChecked == true)
        {
            settings.Mode = ImportMode.FixedValues;
            settings.OverrideYear = ChkYear.IsChecked == true;
            settings.FixedYear = ParseInt(FixYear.Text, DateTime.Now.Year);
            settings.OverrideMonth = ChkMonth.IsChecked == true;
            settings.FixedMonth = Math.Clamp(ParseInt(FixMonth.Text, 1), 1, 12);
            settings.OverrideDay = ChkDay.IsChecked == true;
            settings.FixedDay = Math.Clamp(ParseInt(FixDay.Text, 1), 1, 31);
            settings.OverrideHour = ChkHour.IsChecked == true;
            settings.FixedHour = Math.Clamp(ParseInt(FixHour.Text), 0, 23);
            settings.OverrideMinute = ChkMinute.IsChecked == true;
            settings.FixedMinute = Math.Clamp(ParseInt(FixMinute.Text), 0, 59);
            settings.OverrideSecond = ChkSecond.IsChecked == true;
            settings.FixedSecond = Math.Clamp(ParseInt(FixSecond.Text), 0, 59);
        }
        else if (ModeStartTime.IsChecked == true)
        {
            settings.Mode = ImportMode.StartTime;
            var date = StartDate.SelectedDate ?? DateTime.Today;
            int hour = Math.Clamp(ParseInt(StartHour.Text, 12), 0, 23);
            int minute = Math.Clamp(ParseInt(StartMinute.Text), 0, 59);
            int second = Math.Clamp(ParseInt(StartSecond.Text), 0, 59);
            var localTime = new DateTime(date.Year, date.Month, date.Day, hour, minute, second, DateTimeKind.Local);
            settings.StartTimeUtc = localTime.ToUniversalTime();
        }

        Settings = settings;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void UrlBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            ImportButton_Click(sender, new RoutedEventArgs());
    }

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (PanelAsIs == null) return; // designer guard

        PanelAsIs.Visibility = ModeAsIs.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelOffset.Visibility = ModeOffset.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelFixed.Visibility = ModeFixed.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelStartTime.Visibility = ModeStartTime.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FixedCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (FixYear == null) return; // designer guard

        FixYear.IsEnabled = ChkYear.IsChecked == true;
        FixMonth.IsEnabled = ChkMonth.IsChecked == true;
        FixDay.IsEnabled = ChkDay.IsChecked == true;
        FixHour.IsEnabled = ChkHour.IsChecked == true;
        FixMinute.IsEnabled = ChkMinute.IsChecked == true;
        FixSecond.IsEnabled = ChkSecond.IsChecked == true;
    }

    private void UrlBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (UrlError != null)
            UrlError.Visibility = Visibility.Collapsed;
    }

    private static int ParseInt(string text, int fallback = 0)
        => int.TryParse(text, out int val) ? val : fallback;
}
