using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Trophic.Properties;

namespace Trophic.Controls;

public partial class ConfirmationDialogOverlay : UserControl
{
    private bool _result;
    private DispatcherFrame? _frame;

    public ConfirmationDialogOverlay()
    {
        InitializeComponent();
    }

    public bool ShowConfirm(string message, string title = "Confirm")
    {
        TitleText.Text = title;
        MessageText.Text = message;
        _result = false;
        NoButton.Visibility = Visibility.Visible;
        YesButton.Content = Strings.Yes;
        Visibility = Visibility.Visible;

        NoButton.Focus();

        _frame = new DispatcherFrame();
        Dispatcher.PushFrame(_frame);

        Visibility = Visibility.Collapsed;
        return _result;
    }

    /// <summary>
    /// Shows an OK-only alert dialog (for errors and info messages).
    /// </summary>
    public void ShowAlert(string message, string title = "Info", bool showCopy = false)
    {
        TitleText.Text = title;
        MessageText.Text = message;
        _result = false;
        NoButton.Visibility = Visibility.Collapsed;
        CopyButton.Visibility = showCopy ? Visibility.Visible : Visibility.Collapsed;
        CopyIcon.Text = "\uE8C8";
        CopyLabel.Text = Strings.Copy;
        YesButton.Content = Strings.OK;
        Visibility = Visibility.Visible;

        YesButton.Focus();

        _frame = new DispatcherFrame();
        Dispatcher.PushFrame(_frame);

        Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Shows a prompt dialog with a text input field. Returns the entered text, or null if cancelled.
    /// </summary>
    public string? ShowPrompt(string message, string title = "Input", string defaultValue = "")
    {
        TitleText.Text = title;
        MessageText.Text = message;
        _result = false;
        NoButton.Visibility = Visibility.Visible;
        NoButton.Content = Strings.Cancel;
        CopyButton.Visibility = Visibility.Collapsed;
        YesButton.Content = Strings.OK;
        InputBox.Text = defaultValue;
        InputBox.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;

        InputBox.Focus();
        InputBox.SelectAll();

        _frame = new DispatcherFrame();
        Dispatcher.PushFrame(_frame);

        var text = InputBox.Text;
        InputBox.Visibility = Visibility.Collapsed;
        NoButton.Content = Strings.No;
        Visibility = Visibility.Collapsed;

        return _result ? text : null;
    }

    private void Close(bool result)
    {
        _result = result;
        if (_frame != null)
            _frame.Continue = false;
    }

    private void YesButton_Click(object sender, RoutedEventArgs e) => Close(true);

    private void NoButton_Click(object sender, RoutedEventArgs e) => Close(false);

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText($"{TitleText.Text}\n{MessageText.Text}");
            CopyIcon.Text = "\uE73E";
            CopyLabel.Text = Strings.Copied;
        }
        catch (Exception) { }
    }

    private void OverlayRoot_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(false);
            e.Handled = true;
        }
    }
}
