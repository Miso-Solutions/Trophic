using System.Windows;
using Trophic.Core.Interfaces;
using Trophic.Core.Models;
using Trophic.Views;

namespace Trophic.Infrastructure;

public sealed class DialogService : IDialogService
{
    private readonly ISettingsService _settings;

    public DialogService(ISettingsService settings)
    {
        _settings = settings;
    }

    public string? BrowseFolder(string? description = null)
    {
        var dialog = new FileBrowserDialog
        {
            Mode = BrowseMode.Folder,
            DialogTitle = description ?? "Select Folder",
            InitialDirectory = GetLastBrowseDir(),
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true) return null;

        _settings.LastBrowseDirectory = System.IO.Path.GetDirectoryName(dialog.SelectedPath);
        _settings.Save();
        return dialog.SelectedPath;
    }

    public string? BrowseFile(string filter, string? title = null, string? formatHint = null)
    {
        var dialog = new FileBrowserDialog
        {
            Mode = BrowseMode.File,
            DialogTitle = title ?? "Select File",
            FileFilter = filter,
            FormatHint = formatHint,
            InitialDirectory = GetLastBrowseDir(),
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true) return null;

        _settings.LastBrowseDirectory = System.IO.Path.GetDirectoryName(dialog.SelectedPath);
        _settings.Save();
        return dialog.SelectedPath;
    }

    private string? GetLastBrowseDir()
    {
        var dir = _settings.LastBrowseDirectory;
        return dir != null && System.IO.Directory.Exists(dir) ? dir : null;
    }

    public bool Confirm(string message, string title = "Confirm")
    {
        if (GetOverlay() is { } overlay)
            return overlay.ShowConfirm(message, title);

        return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    public void ShowError(string message, string title = "Error")
    {
        if (GetOverlay() is { } overlay)
        {
            overlay.ShowAlert(message, title, showCopy: true);
            return;
        }

        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public void ShowInfo(string message, string title = "Info")
    {
        if (GetOverlay() is { } overlay)
        {
            overlay.ShowAlert(message, title);
            return;
        }

        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public string? PromptText(string message, string title = "Input", string defaultValue = "")
    {
        if (GetOverlay() is { } overlay)
            return overlay.ShowPrompt(message, title, defaultValue);

        return null;
    }

    private static Controls.ConfirmationDialogOverlay? GetOverlay() =>
        Application.Current.MainWindow is MainWindow mainWindow
            ? mainWindow.FindName("ConfirmDialog") as Controls.ConfirmationDialogOverlay
            : null;

    public DateTime? ShowDateTimePicker(DateTime? initial = null, DateTime? min = null, DateTime? max = null, DateTime? now = null)
    {
        var dialog = new Views.DateTimePickerDialog
        {
            SelectedDateTime = initial ?? now ?? DateTime.Now,
            Owner = Application.Current.MainWindow
        };

        return dialog.ShowDialog() == true ? dialog.SelectedDateTime : null;
    }

    public ImportSettings? ShowImportDialog()
    {
        var dialog = new Views.ImportUrlDialog
        {
            Owner = Application.Current.MainWindow
        };

        return dialog.ShowDialog() == true ? dialog.Settings : null;
    }
}
