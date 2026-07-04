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
/// Import (web/text-file) and per-trophy edit/batch commands.
/// </summary>
public sealed partial class MainViewModel
{
    [RelayCommand]
    private async Task ImportTimestampsAsync()
    {
        if (!_trophyFileService.IsOpen) return;

        var settings = _dialogService.ShowImportDialog();
        if (settings == null) return;

        IsBusy = true;
        IsLoadingTrophies = true;
        BusyMessage = Properties.Strings.BusyInitBrowser;

        try
        {
            await _webScraper.InitializeAsync();
            BusyMessage = Properties.Strings.BusyFetching;
            var timestamps = await _webScraper.ScrapeTimestampsAsync(settings.Url);

            if (timestamps.Count == 0)
            {
                _dialogService.ShowError(
                    $"[{ErrorCodes.NetNoTimestamps}] {Properties.Strings.NoTimestampsFound}",
                    Properties.Strings.NoDataFound);
                return;
            }

            // Apply transformations if configured
            IReadOnlyList<ScrapedTimestamp> applied;
            if (settings.Mode == ImportMode.StartTime)
            {
                applied = settings.ApplyStartTime(timestamps);
            }
            else if (settings.Mode != ImportMode.AsIs)
            {
                applied = timestamps.Select(ts => new ScrapedTimestamp(ts.TrophyId, settings.ApplyTo(ts.UnixTimestamp))).ToList();
            }
            else
            {
                applied = timestamps.ToList();
            }

            var result = _trophyFileService.ApplyScrapedTimestamps(applied);
            RefreshTrophyList();

            if (result.Applied > 0)
            {
                ShowToastNotification(string.Format(Properties.Strings.ImportedTimestamps, result.Applied));
            }
            else
            {
                // Nothing was actually stamped — report why instead of the misleading
                // scraped count (previously it always showed "imported N" even when 0 applied).
                var reason = result.Synced > 0
                    ? "all matching trophies are already synced with PSN and cannot be changed."
                    : result.OutOfRange > 0
                        ? "the imported trophies did not line up with this game's trophy list (wrong page or game?)."
                        : result.FirstError is { Length: > 0 } err
                            ? $"none could be applied. First error: {err}"
                            : "none could be applied to the open trophy file.";
                _dialogService.ShowError(
                    $"Found {timestamps.Count} timestamp(s) but applied 0 — {reason}",
                    Properties.Strings.NoDataFound);
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"[{ErrorCodes.NetImportFailed}] {Properties.Strings.ErrorImportFailed}\n{ex.Message}");
        }
        finally
        {
            IsLoadingTrophies = false;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportFromFileAsync()
    {
        if (!_trophyFileService.IsOpen) return;

        var filePath = _dialogService.BrowseFile(
            "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            Properties.Strings.SelectTrophyImportFile,
            Properties.Strings.ImportFormatHint);
        if (filePath == null) return;

        await ImportFromFilePathAsync(filePath);
    }

    public async Task ImportFromFilePathAsync(string filePath)
    {
        if (!_trophyFileService.IsOpen) return;

        try
        {
            var lines = await File.ReadAllLinesAsync(filePath);
            var trophyList = _trophyFileService.GetTrophyList();
            var nameToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var loosenedToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var earnedById = new Dictionary<int, bool>();
            var syncedById = new Dictionary<int, bool>();
            foreach (var t in trophyList)
            {
                nameToId[NormalizeTrophyName(t.Name)] = t.Id;
                loosenedToId[LoosenTrophyName(t.Name)] = t.Id;
                earnedById[t.Id] = t.IsEarned;
                syncedById[t.Id] = t.IsSynced;
            }

            var skippedSynced = new List<string>();

            int appliedCount = 0;
            var unmatchedNames = new List<string>();

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                    continue;

                // Support both pipe and tab delimiters: "Trophy Name | 2015-02-18 14:03:52"
                var separatorIndex = line.LastIndexOf('|');
                if (separatorIndex < 0) separatorIndex = line.LastIndexOf('\t');
                if (separatorIndex < 0)
                {
                    unmatchedNames.Add($"Bad format: {line}");
                    continue;
                }

                var name = line[..separatorIndex].Trim();
                var dateStr = line[(separatorIndex + 1)..].Trim();

                if (!nameToId.TryGetValue(NormalizeTrophyName(name), out int trophyId) &&
                    !loosenedToId.TryGetValue(LoosenTrophyName(name), out trophyId))
                {
                    unmatchedNames.Add(name);
                    continue;
                }

                if (!DateTime.TryParse(dateStr, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dateTime))
                {
                    unmatchedNames.Add($"Bad date for '{name}': {dateStr}");
                    continue;
                }

                // Skip synced trophies — modifying them desyncs TROPUSR/TROPTRNS
                // and causes PSN to reject the upload with 0x80022D00.
                if (syncedById.TryGetValue(trophyId, out bool isSynced) && isSynced)
                {
                    skippedSynced.Add(name);
                    continue;
                }

                // Text imports: the typed wall-clock time is in the selected display timezone.
                // Route directly through UnlockTrophy/ChangeTrophyTime which apply DisplayTimeZone -> UTC.
                try
                {
                    if (earnedById.TryGetValue(trophyId, out bool isEarned) && isEarned)
                        _trophyFileService.ChangeTrophyTime(trophyId, dateTime);
                    else
                        _trophyFileService.UnlockTrophy(trophyId, dateTime);
                    appliedCount++;
                }
                catch (Exception ex)
                {
                    unmatchedNames.Add($"Failed '{name}': {ex.Message}");
                }
            }

            if (appliedCount == 0)
            {
                var msg = Properties.Strings.NoValidTimestampsInFile;
                if (unmatchedNames.Count > 0)
                    msg += $"\n\nUnmatched entries ({unmatchedNames.Count}):\n" + string.Join("\n", unmatchedNames.Take(10));
                _dialogService.ShowError(msg, Properties.Strings.NoDataFound);
                return;
            }

            RefreshTrophyList();

            var toast = string.Format(Properties.Strings.ImportedFromFile, appliedCount);
            if (unmatchedNames.Count > 0)
                toast += string.Format(Properties.Strings.UnmatchedSuffix, unmatchedNames.Count);
            ShowToastNotification(toast);

            if (unmatchedNames.Count > 0 || skippedSynced.Count > 0)
            {
                var body = string.Format(Properties.Strings.ImportCompleteMessage, appliedCount, unmatchedNames.Count);
                if (unmatchedNames.Count > 0)
                    body += "\n" + string.Join("\n", unmatchedNames.Take(20));
                if (skippedSynced.Count > 0)
                    body += $"\n\nSkipped {skippedSynced.Count} already-synced trophies (modifying them would break PSN sync):\n" +
                            string.Join("\n", skippedSynced.Take(20));
                _dialogService.ShowInfo(body, Properties.Strings.ImportComplete);
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"[{ErrorCodes.NetImportFailed}] {Properties.Strings.ErrorFileImportFailed}\n{ex.Message}");
        }
    }

    /// Normalizes typographic punctuation that web sources (PSNProfiles, Exophase, etc.)
    /// emit but PS3 trophy files store as ASCII. Lets text-file import match either form.
    private static string NormalizeTrophyName(string name)
    {
        var normalized = name
            .Replace('‘', '\'').Replace('’', '\'')      // curly single quotes
            .Replace('“', '"').Replace('”', '"')      // curly double quotes
            .Replace('–', '-').Replace('—', '-')      // en/em dash
            .Replace("…", "...")                            // horizontal ellipsis
            .Replace(' ', ' ')                          // NBSP -> space
            .Replace('​', ' ').Replace('‌', ' ').Replace('‍', ' ') // zero-width chars
            .Replace('﻿', ' ');                         // BOM
        return System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ").Trim();
    }

    /// Last-resort match key: alphanumerics only, lowercased. Defeats spacing, punctuation,
    /// and case differences that a strict comparison would miss.
    private static string LoosenTrophyName(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    [RelayCommand]
    private void ToggleTrophy(TrophyRowViewModel? trophy)
    {
        if (trophy == null || !_trophyFileService.IsOpen) return;

        try
        {
            if (trophy.IsEarned)
            {
                _trophyFileService.LockTrophy(trophy.Id);
            }
            else
            {
                // Generate random time and convert to display timezone context
                var localRandom = Core.Helpers.RandomHelper.NextDateTime(RandomStartTime, RandomEndTime);
                var displayTime = ConvertLocalToDisplayTz(localRandom);
                _trophyFileService.UnlockTrophy(trophy.Id, displayTime);
            }
            RefreshTrophyList();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"[{GetErrorCode(ex)}] {ex.Message}");
        }
    }

    [RelayCommand]
    private void EditTimestamp(TrophyRowViewModel? trophy)
    {
        if (trophy == null || trophy.IsSynced) return;

        var newTime = _dialogService.ShowDateTimePicker(trophy.EarnedTime ?? NowInDisplayTimeZone(), now: NowInDisplayTimeZone());
        if (newTime == null) return;

        try
        {
            if (!trophy.IsEarned)
            {
                try
                {
                    _trophyFileService.UnlockTrophy(trophy.Id, newTime.Value);
                }
                catch (TrophyFormat.Exceptions.TrophyAlreadyEarnedException)
                {
                    // State mismatch — trophy is still marked earned internally; update time instead
                    _trophyFileService.ChangeTrophyTime(trophy.Id, newTime.Value);
                }
            }
            else
            {
                _trophyFileService.ChangeTrophyTime(trophy.Id, newTime.Value);
            }
            RefreshTrophyList();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"[{GetErrorCode(ex)}] {ex.Message}");
        }
    }

    [RelayCommand]
    private void CopyTrophyName(TrophyRowViewModel? trophy)
    {
        if (trophy == null) return;
        try
        {
            System.Windows.Clipboard.SetText(trophy.Name);
            ShowToastNotification(string.Format(Properties.Strings.CopiedName, trophy.Name), ToastType.Info);
        }
        catch (Exception) { }
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void ClearAllFilters()
    {
        FilterEarned = false;
        FilterNotEarned = false;
        FilterSynced = false;
        FilterPlatinum = false;
        FilterGold = false;
        FilterSilver = false;
        FilterBronze = false;
        SearchText = string.Empty;
    }

    [RelayCommand]
    private void BatchUnlock()
    {
        if (SelectedItems == null || !_trophyFileService.IsOpen) return;
        var trophies = SelectedItems.Cast<TrophyRowViewModel>().Where(t => !t.IsEarned && !t.IsSynced).ToList();
        if (trophies.Count == 0) return;

        foreach (var trophy in trophies)
        {
            var localRandom = Core.Helpers.RandomHelper.NextDateTime(RandomStartTime, RandomEndTime);
            var displayTime = ConvertLocalToDisplayTz(localRandom);
            _trophyFileService.UnlockTrophy(trophy.Id, displayTime);
        }
        RefreshTrophyList();
        ShowToastNotification(string.Format(Properties.Strings.UnlockedCount, trophies.Count));
    }

    [RelayCommand]
    private void BatchLock()
    {
        if (SelectedItems == null || !_trophyFileService.IsOpen) return;
        var trophies = SelectedItems.Cast<TrophyRowViewModel>().Where(t => t.IsEarned && !t.IsSynced).ToList();
        if (trophies.Count == 0) return;

        foreach (var trophy in trophies)
            _trophyFileService.LockTrophy(trophy.Id);

        RefreshTrophyList();
        ShowToastNotification(string.Format(Properties.Strings.LockedCount, trophies.Count));
    }

    [RelayCommand]
    private void BatchClear()
    {
        if (SelectedItems == null || !_trophyFileService.IsOpen) return;
        var trophies = SelectedItems.Cast<TrophyRowViewModel>().Where(t => t.IsEarned && !t.IsSynced).ToList();
        if (trophies.Count == 0) return;

        if (!_dialogService.Confirm(string.Format(Properties.Strings.ConfirmClearBatch, trophies.Count)))
            return;

        foreach (var trophy in trophies)
            _trophyFileService.LockTrophy(trophy.Id);

        RefreshTrophyList();
        ShowToastNotification(string.Format(Properties.Strings.ClearedCount, trophies.Count));
    }

    [RelayCommand]
    private void ToggleShortcutsHelp() => ShowShortcutsHelp = !ShowShortcutsHelp;

}
