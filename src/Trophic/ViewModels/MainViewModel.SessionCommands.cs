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
/// Session and profile commands: open (folder/catalog), save, close, exit, profile add/remove, and instant trophy operations.
/// </summary>
public sealed partial class MainViewModel
{
    // --- Commands ---

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var folder = _dialogService.BrowseFolder(Properties.Strings.SelectTrophyFolder);
        if (folder == null) return;

        await OpenTrophyFolderAsync(folder);
    }

    [RelayCommand]
    private async Task OpenFromCatalogAsync()
    {
        var entry = _dialogService.ShowCatalogDialog();
        if (entry == null) return;

        // Soft paywall: browsing the catalog is free, downloading requires a license.
        // Only enforced once catalog licensing is switched on (after the license server
        // is deployed). Until then the Catalog is free (interim direct-download).
        if (Core.Licensing.LicensingOptions.Enabled && !_licenseService.IsEntitled())
        {
            // A valid license whose offline grace lapsed recovers with a single server
            // round-trip — try that before prompting for a key, so a still-valid user
            // isn't asked to re-enter anything just because they were offline a while.
            try { await _licenseService.RefreshAsync(); }
            catch { /* offline or transient — fall through to the dialog */ }

            if (!_licenseService.IsEntitled())
            {
                var entitled = _dialogService.ShowLicenseDialog();
                if (!entitled) return;
            }
        }

        // Pick download folder (reuse within session)
        if (_catalogDownloadFolder == null || !System.IO.Directory.Exists(_catalogDownloadFolder))
        {
            var folder = _dialogService.BrowseFolder(Properties.Strings.CatalogSelectFolder);
            if (folder == null) return;
            _catalogDownloadFolder = folder;
        }

        IsBusy = true;
        BusyMessage = string.Format(Properties.Strings.CatalogDownloading, entry.Name);
        try
        {
            var progress = new Progress<double>(p =>
            {
                // Update on UI thread
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    BusyMessage = string.Format(Properties.Strings.CatalogDownloading, entry.Name) +
                                  $" ({(int)(p * 100)}%)";
                });
            });

            var trophyFolder = await _downloadService.DownloadAndExtractAsync(
                entry.Id, _catalogDownloadFolder, progress);

            IsBusy = false;
            await OpenTrophyFolderAsync(trophyFolder);
        }
        catch (Exception ex)
        {
            IsBusy = false;
            _dialogService.ShowError(string.Format(Properties.Strings.CatalogDownloadFailed, ex.Message));
        }
    }

    public async Task OpenTrophyFolderAsync(string folderPath)
    {
        IsBusy = true;
        IsLoadingTrophies = true;
        BusyMessage = Properties.Strings.BusyDecrypting;

        try
        {
            _trophyFileService.IsRpcs3Format = IsRpcs3Format;
            await _trophyFileService.OpenAsync(folderPath);
            RefreshTrophyList();
            IsFileOpen = true;
            _lastSavedProfile = SelectedProfile;
            var gameName = _trophyFileService.CurrentState?.Config.TitleName ?? "";
            WindowTitle = string.IsNullOrEmpty(gameName) ? BaseTitle : $"{BaseTitle} | {gameName}";
            LoadedFolderPath = folderPath;
            _recentFiles.AddEntry(folderPath, WindowTitle);
            RefreshRecentFiles();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"[{ErrorCodes.FileOpenFailed}] {Properties.Strings.ErrorOpenFailed}\n{ex.Message}");
        }
        finally
        {
            IsLoadingTrophies = false;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!_trophyFileService.IsOpen) return;

        IsBusy = true;
        BusyMessage = Properties.Strings.BusyEncrypting;

        try
        {
            await _trophyFileService.SaveAsync(SelectedProfile);
            HasUnsavedChanges = false;
            _lastSavedProfile = SelectedProfile;
            SaveSucceeded = true;
            AccountId = _trophyFileService.CurrentState?.UserState.AccountId ?? string.Empty;
            _ = ResetSaveSucceededAsync();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"[{ErrorCodes.FileSaveFailed}] {Properties.Strings.ErrorSaveFailed}\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CloseFile()
    {
        TryCloseFile();
    }

    private bool TryCloseFile()
    {
        if (_trophyFileService.HasUnsavedChanges || HasProfileChange)
        {
            if (!_dialogService.Confirm(Properties.Strings.UnsavedChangesConfirm))
                return false;
        }

        _trophyFileService.Close();
        Trophies.Clear();
        HasNoTrophies = true;
        HasUnsavedChanges = false;
        _lastSavedProfile = null;
        HasPlatinum = false;
        ShowHiddenTrophies = false;
        IsFileOpen = false;
        WindowTitle = BaseTitle;
        LoadedFolderPath = string.Empty;
        FilterEarned = false; FilterNotEarned = false; FilterSynced = false;
        FilterPlatinum = false; FilterGold = false; FilterSilver = false; FilterBronze = false;
        SearchText = string.Empty;
        SelectedCount = 0;
        PlatinumCount = GoldCount = SilverCount = BronzeCount = 0;
        UpdateStats(0, 0, 0, 0);
        return true;
    }

    [RelayCommand]
    private void Exit()
    {
        if (TryCloseFile())
            Application.Current.Shutdown();
    }

    [RelayCommand]
    private void AddProfile()
    {
        var file = _dialogService.BrowseFile("PARAM.SFO Files (*.SFO)|*.SFO|All Files (*.*)|*.*", Properties.Strings.SelectProfileSFO);
        if (file == null) return;

        var name = _dialogService.PromptText(Properties.Strings.EnterProfileName, Properties.Strings.AddProfile, Properties.Strings.DefaultProfileName);
        if (string.IsNullOrWhiteSpace(name)) return;

        // Sanitize: remove characters invalid for file names
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            _profileService.SaveProfile(name, file);
            RefreshProfiles();
            SelectedProfile = name;
            ShowToastNotification(string.Format(Properties.Strings.ProfileAdded, name));
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"{Properties.Strings.ErrorAddProfile}\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void RemoveProfile()
    {
        if (string.IsNullOrEmpty(SelectedProfile)) return;

        if (!_dialogService.Confirm(string.Format(Properties.Strings.RemoveProfileConfirm, SelectedProfile)))
            return;

        try
        {
            _profileService.DeleteProfile(SelectedProfile);
            SelectedProfile = null;
            RefreshProfiles();
            ShowToastNotification(Properties.Strings.ProfileRemoved);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"{Properties.Strings.ErrorRemoveProfile}\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void Donate()
    {
        var url = "https://ko-fi.com/misosolutions";
        using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

    }

    [RelayCommand]
    private void Refresh()
    {
        if (_trophyFileService.IsOpen)
            RefreshTrophyList();
    }

    [RelayCommand]
    private void InstantPlatinum()
    {
        if (!_trophyFileService.IsOpen) return;

        try
        {
            _trophyFileService.InstantPlatinum(ConvertLocalToDisplayTz(RandomStartTime), ConvertLocalToDisplayTz(RandomEndTime));
            RefreshTrophyList();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"[{ErrorCodes.TrophyPlatinumFailed}] {Properties.Strings.ErrorPlatinumFailed}\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearTrophies()
    {
        if (!_trophyFileService.IsOpen) return;

        if (!_dialogService.Confirm(Properties.Strings.ConfirmClearNonSynced))
            return;

        _trophyFileService.ClearAllTrophies(includeSynced: false);
        RefreshTrophyList();
    }

    [RelayCommand]
    private void ClearAllIncludingSynced()
    {
        if (!_trophyFileService.IsOpen) return;

        if (!_dialogService.Confirm(Properties.Strings.ConfirmClearIncludingSynced))
            return;

        _trophyFileService.ClearAllTrophies(includeSynced: true);
        RefreshTrophyList();
    }

    [RelayCommand]
    private void InstantUnlock()
    {
        if (!_trophyFileService.IsOpen) return;

        if (!_dialogService.Confirm(Properties.Strings.ConfirmUnlockAll))
            return;

        try
        {
            _trophyFileService.InstantUnlock(NowInDisplayTimeZone());
            RefreshTrophyList();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"[{ErrorCodes.TrophyUnlockFailed}] {Properties.Strings.ErrorUnlockFailed}\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void InstantUnlockAtTimestamp()
    {
        if (!_trophyFileService.IsOpen) return;

        var time = _dialogService.ShowDateTimePicker(NowInDisplayTimeZone(), now: NowInDisplayTimeZone());
        if (time == null) return;

        try
        {
            _trophyFileService.InstantUnlock(time.Value);
            RefreshTrophyList();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"[{ErrorCodes.TrophyUnlockFailed}] {Properties.Strings.ErrorUnlockFailed}\n{ex.Message}");
        }
    }

}
