using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trophic.Core;
using Trophic.Core.Interfaces;
using Trophic.Core.Models;
using Trophic.Services;
using Trophic.TrophyFormat.Exceptions;

namespace Trophic.ViewModels;

/// <summary>
/// Update checks, backup import/export, language selection, recent files, and drag-and-drop.
/// </summary>
public sealed partial class MainViewModel
{
    // --- Update Check ---

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        var update = await _updateChecker.CheckForUpdateAsync(AppVersion);
        if (update == null)
        {
            ShowToastNotification(Properties.Strings.UpdateUpToDate, ToastType.Success);
            return;
        }

        var message = string.Format(Properties.Strings.UpdateAvailable, update.Version);
        if (!_dialogService.Confirm($"{message}\n\n{Properties.Strings.UpdateDownloadPrompt}"))
            return;

        IsBusy = true;
        BusyMessage = Properties.Strings.UpdateDownloading;
        try
        {
            var progress = new Progress<double>(p =>
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    BusyMessage = $"{Properties.Strings.UpdateDownloading} ({(int)(p * 100)}%)";
                });
            });

            var appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var scriptPath = await _autoUpdater.DownloadAndPrepareUpdateAsync(appDir, progress);
            _logger.Info($"Update downloaded. Launching update script: {scriptPath}");

            IsBusy = false;

            // Launch the update script with our PID so it knows when we've exited
            var currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{scriptPath}\" {currentPid}",
                UseShellExecute = false,
                CreateNoWindow = true
            });

            // Exit the app so the script can replace files
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            IsBusy = false;
            _logger.Error("Auto-update failed", ex);
            _dialogService.ShowError($"{Properties.Strings.UpdateFailed}\n{ex.Message}");
        }
    }

    // --- Backup / Export / Import ---

    [RelayCommand]
    private void ExportBackup()
    {
        var path = _dialogService.BrowseFolder(Properties.Strings.BackupSelectFolder);
        if (path == null) return;

        try
        {
            var zipPath = Path.Combine(path, $"Trophic-Backup-{DateTime.Now:yyyy-MM-dd}.zip");
            _backupService.ExportBackup(AppDomain.CurrentDomain.BaseDirectory, zipPath);
            _logger.Info($"Backup exported to: {zipPath}");
            ShowToastNotification(Properties.Strings.BackupExported, ToastType.Success);
        }
        catch (Exception ex)
        {
            _logger.Error("Backup export failed", ex);
            _dialogService.ShowError($"{Properties.Strings.BackupFailed}\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void ImportBackup()
    {
        var file = _dialogService.BrowseFile("Trophic Backup (*.zip)|*.zip", Properties.Strings.BackupSelectFile);
        if (file == null) return;

        if (!_dialogService.Confirm(Properties.Strings.BackupImportConfirm))
            return;

        try
        {
            _backupService.ImportBackup(AppDomain.CurrentDomain.BaseDirectory, file);
            _logger.Info($"Backup imported from: {file}");
            ShowToastNotification(Properties.Strings.BackupImported, ToastType.Success);
        }
        catch (Exception ex)
        {
            _logger.Error("Backup import failed", ex);
            _dialogService.ShowError($"{Properties.Strings.BackupFailed}\n{ex.Message}");
        }
    }

    // Language entries sorted alphabetically by display name, English always first
    public sealed class LanguageEntry
    {
        public string Name { get; init; } = "";
        public string Culture { get; init; } = "";
    }

    public static readonly LanguageEntry[] AvailableLanguages = [
        new() { Name = "English", Culture = "en" },
        new() { Name = "العربية", Culture = "ar" },
        new() { Name = "Deutsch", Culture = "de" },
        new() { Name = "Español", Culture = "es" },
        new() { Name = "Français", Culture = "fr" },
        new() { Name = "עברית", Culture = "he" },
        new() { Name = "日本語", Culture = "ja" },
        new() { Name = "한국어", Culture = "ko" },
        new() { Name = "Português", Culture = "pt-BR" },
        new() { Name = "Русский", Culture = "ru" },
        new() { Name = "简体中文", Culture = "zh-Hans" },
        new() { Name = "繁體中文", Culture = "zh-Hant" },
    ];

    [ObservableProperty] private FlowDirection _appFlowDirection = FlowDirection.LeftToRight;
    [ObservableProperty] private string _currentLanguageName = "English";

    private void ApplyLanguage()
    {
        var code = _settingsService.LanguageCode;
        var entry = Array.Find(AvailableLanguages, e => e.Culture == code) ?? AvailableLanguages[0];
        var culture = new System.Globalization.CultureInfo(entry.Culture);
        Properties.Strings.Culture = culture;
        CurrentLanguageName = entry.Name;
        WindowTitle = BaseTitle;
        AppFlowDirection = culture.TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    [RelayCommand]
    private void SelectLanguage(LanguageEntry? entry)
    {
        if (entry == null || entry.Culture == _settingsService.LanguageCode) return;

        _settingsService.LanguageCode = entry.Culture;
        _settingsService.Save();

        // WPF x:Static bindings resolve once at parse time — restart to apply new language
        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (exePath != null)
        {
            using var proc = System.Diagnostics.Process.Start(exePath);
        }
        Application.Current.Shutdown();
    }

    [RelayCommand]
    private async Task OpenRecentAsync(RecentFileEntry? entry)
    {
        if (entry == null) return;
        if (!Directory.Exists(entry.Path))
        {
            _recentFiles.RemoveEntry(entry.Path);
            RefreshRecentFiles();
            ShowToastNotification(Properties.Strings.FolderNoLongerExists, ToastType.Error);
            return;
        }
        await OpenTrophyFolderAsync(entry.Path);
    }

    [RelayCommand]
    private void RemoveRecent(RecentFileEntry? entry)
    {
        if (entry == null) return;
        _recentFiles.RemoveEntry(entry.Path);
        RefreshRecentFiles();
    }

    [RelayCommand]
    private void ClearTimestamp(TrophyRowViewModel? trophy)
    {
        if (trophy == null || !trophy.IsEarned) return;

        if (trophy.IsSynced)
        {
            _dialogService.ShowError(Properties.Strings.SyncedCannotClear);
            return;
        }

        try
        {
            _trophyFileService.LockTrophy(trophy.Id);
            RefreshTrophyList();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"[{GetErrorCode(ex)}] {ex.Message}");
        }
    }

    partial void OnIsRpcs3FormatChanged(bool value)
    {
        _trophyFileService.IsRpcs3Format = value;
    }

    // --- Drag and Drop ---

    public void HandleDrop(string[] paths)
    {
        if (paths.Length == 0) return;

        var path = paths[0];
        if (Directory.Exists(path))
        {
            _ = OpenTrophyFolderAsync(path);
        }
        else if (File.Exists(path) && path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            _ = ImportFromFilePathAsync(path);
        }
    }

    // --- Private helpers ---

    private static string GetErrorCode(Exception ex) => ex switch
    {
        TrophyAlreadyEarnedException => ErrorCodes.TrophyAlreadyEarned,
        TrophyAlreadySyncException => ErrorCodes.TrophyAlreadySynced,
        TrophyNotFoundException => ErrorCodes.TrophyNotFound,
        TrophySyncTimeException => ErrorCodes.TrophySyncTime,
        InvalidTrophyFileException => ErrorCodes.FmtInvalidTrophyFile,
        FileNotFoundException => ErrorCodes.FileOpenFailed,
        TimeoutException => "E-SYS-TIMEOUT",
        _ => "E-UNKNOWN"
    };

}
