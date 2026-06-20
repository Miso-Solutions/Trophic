using System.Windows;
using System.Windows.Media;
using Trophic.Core.Licensing;

namespace Trophic.Views;

public partial class LicenseDialog : Window
{
    private readonly LicenseService _license;

    public LicenseDialog(LicenseService license)
    {
        InitializeComponent();
        _license = license;
        Loaded += (_, _) => RefreshStatus();
    }

    private void RefreshStatus()
    {
        var status = _license.GetStatus();
        switch (status.State)
        {
            case LicenseState.Active:
                StatusText.Text = "Active";
                StatusText.Foreground = Brush("TextPrimaryBrush");
                StatusDetail.Text = DescribePlan(status);
                DeactivateButton.Visibility = Visibility.Visible;
                KeyBox.Text = status.LicenseKey ?? string.Empty;
                break;
            case LicenseState.Expired:
                StatusText.Text = "Expired";
                StatusText.Foreground = Brush("DestructiveBrush");
                StatusDetail.Text = "This license has expired. Enter a renewed key, or contact support to extend it.";
                DeactivateButton.Visibility = Visibility.Visible;
                break;
            case LicenseState.GracePeriodLapsed:
                StatusText.Text = "Needs reverification";
                StatusText.Foreground = Brush("DestructiveBrush");
                StatusDetail.Text = "Could not reach the license server within the offline grace period. Connect to the internet and reopen, or re-activate.";
                DeactivateButton.Visibility = Visibility.Visible;
                break;
            default:
                StatusText.Text = "Not activated";
                StatusText.Foreground = Brush("TextSecondaryBrush");
                StatusDetail.Text = "Enter a license key to unlock Catalog downloads.";
                DeactivateButton.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private static string DescribePlan(LicenseStatus status)
    {
        var plan = status.Plan ?? "license";
        if (status.LicenseExpiresAt is { } exp)
            return $"Plan: {plan}. Valid until {exp.ToLocalTime():yyyy-MM-dd}.";
        return $"Plan: {plan}. No expiry.";
    }

    private async void ActivateButton_Click(object sender, RoutedEventArgs e)
    {
        var key = KeyBox.Text?.Trim() ?? string.Empty;
        if (key.Length == 0)
        {
            ShowMessage("Enter a license key.", isError: true);
            return;
        }

        SetBusy(true);
        ShowMessage("Activating…", isError: false);
        try
        {
            var result = await _license.ActivateAsync(key);
            if (result.Success)
            {
                ShowMessage("Activated. Catalog downloads are unlocked.", isError: false);
                RefreshStatus();
            }
            else
            {
                ShowMessage(result.ErrorMessage ?? "Activation failed.", isError: true);
            }
        }
        catch (Exception ex)
        {
            // async void: never let an exception escape unhandled.
            ShowMessage("Activation error: " + ex.Message, isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void DeactivateButton_Click(object sender, RoutedEventArgs e)
    {
        _license.Deactivate();
        KeyBox.Text = string.Empty;
        ShowMessage("Deactivated on this device.", isError: false);
        RefreshStatus();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        // DialogResult reflects whether the user is entitled when the dialog closes,
        // so the catalog paywall can proceed or abort accordingly.
        DialogResult = _license.IsEntitled();
    }

    private void SetBusy(bool busy)
    {
        ActivateButton.IsEnabled = !busy;
        DeactivateButton.IsEnabled = !busy;
        KeyBox.IsEnabled = !busy;
    }

    private void ShowMessage(string message, bool isError)
    {
        MessageText.Text = message;
        MessageText.Foreground = Brush(isError ? "DestructiveBrush" : "TextSecondaryBrush");
        MessageText.Visibility = Visibility.Visible;
    }

    private static Brush Brush(string key)
        => (Brush)Application.Current.Resources[key];
}
