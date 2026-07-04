using System.Windows;
using Microsoft.Win32;
using Trophic.Core.Models;
using Trophic.Core.Services;

namespace Trophic.Views;

public partial class ConsoleKeysDialog : Window
{
    private readonly ConsoleKeysService _service;

    public ConsoleKeysDialog(ConsoleKeysService service)
    {
        InitializeComponent();
        _service = service;
        Loaded += (_, _) => Populate();
    }

    private void Populate()
    {
        var keys = _service.GetKeys();
        IdpsBox.Text = Convert.ToHexString(keys.ConsoleId);
        UserIdBox.Text = DecodeUserIdToDecimal(keys.UserId);
        AuthIdBox.Text = Convert.ToHexString(keys.AuthenticationId);
    }

    private static string DecodeUserIdToDecimal(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 8 || bytes.All(b => b == 0)) return string.Empty;
        // Apollo stores user_id as 8 ASCII decimal digits ("00000001"). Reverse for the field.
        try
        {
            var ascii = System.Text.Encoding.ASCII.GetString(bytes, 0, 8);
            if (ascii.All(c => c >= '0' && c <= '9'))
                return uint.Parse(ascii, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch { /* fall through */ }
        return string.Empty;
    }

    private static byte[] EncodeUserIdFromDecimal(string text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0) return new byte[8];
        if (!uint.TryParse(trimmed, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n))
            throw new FormatException("User ID must be a decimal number (typical PS3 primary user is 1).");
        var ascii = n.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
        return System.Text.Encoding.ASCII.GetBytes(ascii);
    }

    private void BrowseIdpsButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select IDPS file",
            Filter = "IDPS files (*.hex;*.bin;*.dat)|*.hex;*.bin;*.dat|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var bytes = ConsoleKeysService.ReadIdpsFile(dlg.FileName);
            IdpsBox.Text = Convert.ToHexString(bytes);
            ClearError();
        }
        catch (Exception ex)
        {
            ShowError($"Failed to read IDPS: {ex.Message}");
        }
    }

    private void FillFromSaveFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Pick PARAM.SFO inside any save folder",
            Filter = "PARAM.SFO|PARAM.SFO|All files|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true) return;
        var folder = System.IO.Path.GetDirectoryName(dlg.FileName);
        if (folder == null) { ShowError("Could not resolve the save folder."); return; }

        var result = _service.ExtractFromSaveFolder(folder);
        if (!result.Success)
        {
            ShowError(result.ErrorMessage ?? "Extraction failed.");
            return;
        }

        // Populate the input fields only — the user clicks Save to persist.
        var filled = new System.Collections.Generic.List<string>();
        if (result.AuthenticationId != null)
        {
            AuthIdBox.Text = Convert.ToHexString(result.AuthenticationId);
            filled.Add("Authentication ID");
        }
        if (result.UserId != null)
        {
            UserIdBox.Text = DecodeUserIdToDecimal(result.UserId);
            filled.Add("User ID");
        }

        if (filled.Count > 0)
            ShowInfo($"Filled {string.Join(" and ", filled)} from save. Click Save to persist.");
        else
            ShowError("Nothing usable was found in the selected save.");
    }

    private void ShowInfo(string message)
    {
        ErrorText.Text = message;
        ErrorText.Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextSecondaryBrush"];
        ErrorText.Visibility = Visibility.Visible;
    }

    private void BrowseActDatButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select act.dat",
            Filter = "Activation data (act.dat)|act.dat|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var bytes = ConsoleKeysService.ExtractAuthenticationIdFromActDat(dlg.FileName);
            AuthIdBox.Text = Convert.ToHexString(bytes);
            ClearError();
        }
        catch (Exception ex)
        {
            ShowError($"Failed to read act.dat: {ex.Message}");
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var keys = new ConsoleKeys
            {
                ConsoleId = ConsoleKeysService.ParseHex(IdpsBox.Text, 16),
                UserId = EncodeUserIdFromDecimal(UserIdBox.Text),
                AuthenticationId = ConsoleKeysService.ParseHex(AuthIdBox.Text, 8),
            };
            _service.SetKeys(keys);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _service.Clear();
        Populate();
        ClearError();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["DestructiveBrush"];
        ErrorText.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        ErrorText.Visibility = Visibility.Collapsed;
    }
}
