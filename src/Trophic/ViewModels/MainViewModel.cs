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

public enum ToastType { Success, Error, Info }

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ITrophyFileService _trophyFileService;
    private readonly IDialogService _dialogService;
    private readonly IWebScraperService _webScraper;
    private readonly IProfileService _profileService;
    private readonly ISettingsService _settingsService;
    private readonly RecentFilesService _recentFiles;
    private readonly Core.Services.TrophyDownloadService _downloadService;
    private string? _catalogDownloadFolder;

    public MainViewModel(
        ITrophyFileService trophyFileService,
        IDialogService dialogService,
        IWebScraperService webScraper,
        IProfileService profileService,
        ISettingsService settingsService,
        RecentFilesService recentFiles,
        Core.Services.TrophyDownloadService downloadService)
    {
        _trophyFileService = trophyFileService;
        _dialogService = dialogService;
        _webScraper = webScraper;
        _profileService = profileService;
        _settingsService = settingsService;
        _recentFiles = recentFiles;
        _downloadService = downloadService;

        Trophies = new ObservableCollection<TrophyRowViewModel>();
        RecentFiles = new ObservableCollection<RecentFileEntry>(_recentFiles.GetEntries());
        RefreshProfiles();
        ApplyTimeZone();
        ApplyLanguage();
    }

    // --- Observable Properties ---

    [ObservableProperty] private string _windowTitle = Properties.Strings.AppTitle;
    [ObservableProperty] private bool _isFileOpen;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyMessage = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EarnedStatusText))]
    [NotifyPropertyChangedFor(nameof(TrophiesStatusText))]
    private int _earnedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EarnedStatusText))]
    [NotifyPropertyChangedFor(nameof(TrophiesStatusText))]
    [NotifyPropertyChangedFor(nameof(FilteredStatusText))]
    private int _totalCount;
    [ObservableProperty] private int _earnedGrade;
    [ObservableProperty] private int _totalGrade;
    [ObservableProperty] private double _completionPercent;
    [ObservableProperty] private bool _isRpcs3Format;
    [ObservableProperty] private DateTime _randomStartTime = new(2015, 1, 1);
    [ObservableProperty] private DateTime _randomEndTime = DateTime.Now;

    [ObservableProperty] private bool _hasPlatinum;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowColumnHeaders))]
    private bool _hasNoTrophies = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowColumnHeaders))]
    private bool _isLoadingTrophies;

    public bool ShowEmptyState => HasNoTrophies && !IsLoadingTrophies;
    public bool ShowColumnHeaders => !HasNoTrophies || IsLoadingTrophies;

    [ObservableProperty] private bool _saveSucceeded;
    [ObservableProperty] private bool _showToast;
    [ObservableProperty] private string _toastMessage = string.Empty;
    [ObservableProperty] private ToastType _toastType = ToastType.Success;
    [ObservableProperty] private string _loadedFolderPath = string.Empty;
    [ObservableProperty] private int _platinumCount;
    [ObservableProperty] private int _goldCount;
    [ObservableProperty] private int _silverCount;
    [ObservableProperty] private int _bronzeCount;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredStatusText))]
    private int _filteredCount;

    // Filter chips
    [ObservableProperty] private bool _filterEarned;
    [ObservableProperty] private bool _filterNotEarned;
    [ObservableProperty] private bool _filterSynced;
    [ObservableProperty] private bool _filterPlatinum;
    [ObservableProperty] private bool _filterGold;
    [ObservableProperty] private bool _filterSilver;
    [ObservableProperty] private bool _filterBronze;

    public bool HasActiveFilters => FilterEarned || FilterNotEarned || FilterSynced ||
        FilterPlatinum || FilterGold || FilterSilver || FilterBronze;

    // Localized status bar text
    public string EarnedStatusText => string.Format(Properties.Strings.EarnedFormat, EarnedCount, TotalCount);
    public string UserStatusText => string.Format(Properties.Strings.UserFormat, AccountId);
    public string TrophiesStatusText => string.Format(Properties.Strings.TrophiesFormat, EarnedCount, TotalCount);
    public string FilteredStatusText => string.Format(Properties.Strings.FilteredFormat, FilteredCount, TotalCount);

    partial void OnFilterEarnedChanged(bool value) => ApplyFilters();
    partial void OnFilterNotEarnedChanged(bool value) => ApplyFilters();
    partial void OnFilterSyncedChanged(bool value) => ApplyFilters();
    partial void OnFilterPlatinumChanged(bool value) => ApplyFilters();
    partial void OnFilterGoldChanged(bool value) => ApplyFilters();
    partial void OnFilterSilverChanged(bool value) => ApplyFilters();
    partial void OnFilterBronzeChanged(bool value) => ApplyFilters();

    // Multi-select
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private string _selectionStatusText = string.Empty;
    public System.Collections.IList? SelectedItems { get; set; }

    public bool HasMultipleSelected => SelectedCount > 1;

    partial void OnSelectedCountChanged(int value)
    {
        SelectionStatusText = value > 1 ? string.Format(Properties.Strings.SelectedCount, value) : string.Empty;
        OnPropertyChanged(nameof(HasMultipleSelected));
    }

    // Help overlay
    [ObservableProperty] private bool _showShortcutsHelp;

    // Recent files
    public ObservableCollection<RecentFileEntry> RecentFiles { get; }
    public bool HasRecentFiles => RecentFiles.Count > 0;

    private void RefreshRecentFiles()
    {
        RecentFiles.Clear();
        foreach (var entry in _recentFiles.GetEntries())
            RecentFiles.Add(entry);
        OnPropertyChanged(nameof(HasRecentFiles));
    }

    [ObservableProperty] private bool _hasUnsavedChanges;
    [ObservableProperty] private bool _showHiddenTrophies;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UserStatusText))]
    private string _accountId = string.Empty;
    [ObservableProperty] private TimeZoneInfo _selectedTimeZone = TimeZoneInfo.Local;
    [ObservableProperty] private bool _useDaylightSaving = true;
    private TimeZoneInfo? _cachedEffectiveTimeZone;

    public ReadOnlyCollection<TimeZoneInfo> AvailableTimeZones { get; } = TimeZoneInfo.GetSystemTimeZones();

    public bool CanApplyDst => SelectedTimeZone.SupportsDaylightSavingTime;

    partial void OnSelectedTimeZoneChanged(TimeZoneInfo value)
    {
        OnPropertyChanged(nameof(CanApplyDst));
        ApplyTimeZone();
    }
    partial void OnUseDaylightSavingChanged(bool value) => ApplyTimeZone();

    private void ApplyTimeZone()
    {
        _cachedEffectiveTimeZone = null;
        _trophyFileService.DisplayTimeZone = GetEffectiveTimeZone();
        RefreshTrophyList();
    }

    private TimeZoneInfo GetEffectiveTimeZone()
    {
        if (_cachedEffectiveTimeZone != null)
            return _cachedEffectiveTimeZone;

        var tz = SelectedTimeZone;
        if (!tz.SupportsDaylightSavingTime)
        {
            _cachedEffectiveTimeZone = tz;
            return tz;
        }

        // Use fixed-offset timezones so the DST toggle always has a visible effect
        // regardless of whether a given trophy was earned in summer or winter.
        TimeZoneInfo result;
        if (UseDaylightSaving)
        {
            var rules = tz.GetAdjustmentRules();
            var dstDelta = rules.Length > 0 ? rules.Max(r => r.DaylightDelta) : TimeSpan.Zero;
            result = TimeZoneInfo.CreateCustomTimeZone(
                tz.Id + "_DST",
                tz.BaseUtcOffset + dstDelta,
                tz.DisplayName + " (DST)",
                tz.DaylightName);
        }
        else
        {
            result = TimeZoneInfo.CreateCustomTimeZone(
                tz.Id + "_NoDST",
                tz.BaseUtcOffset,
                tz.DisplayName + " (No DST)",
                tz.StandardName);
        }

        _cachedEffectiveTimeZone = result;
        return result;
    }

    /// <summary>
    /// Returns the current moment in the selected display timezone.
    /// </summary>
    private DateTime NowInDisplayTimeZone()
    {
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, GetEffectiveTimeZone());
    }

    /// <summary>
    /// Converts a local DateTime to the equivalent time in the display timezone.
    /// Used when generating random times from local-time UI bounds.
    /// </summary>
    private DateTime ConvertLocalToDisplayTz(DateTime localTime)
    {
        var utc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localTime, DateTimeKind.Local),
            TimeZoneInfo.Local);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, GetEffectiveTimeZone());
    }

    public bool IsSaveButtonEnabled => HasUnsavedChanges || SaveSucceeded || HasProfileChange;

    partial void OnHasUnsavedChangesChanged(bool value) => OnPropertyChanged(nameof(IsSaveButtonEnabled));
    partial void OnSaveSucceededChanged(bool value) => OnPropertyChanged(nameof(IsSaveButtonEnabled));

    partial void OnSearchTextChanged(string value) => ApplyFilters();

    private void ApplyFilters()
    {
        OnPropertyChanged(nameof(HasActiveFilters));

        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(Trophies);
        if (view == null) return;

        bool anyStatusFilter = FilterEarned || FilterNotEarned || FilterSynced;
        bool anyTypeFilter = FilterPlatinum || FilterGold || FilterSilver || FilterBronze;
        bool hasSearch = !string.IsNullOrWhiteSpace(SearchText);

        if (!anyStatusFilter && !anyTypeFilter && !hasSearch)
        {
            view.Filter = null;
            FilteredCount = Trophies.Count;
            return;
        }

        var search = SearchText?.Trim() ?? string.Empty;
        view.Filter = obj =>
        {
            if (obj is not TrophyRowViewModel t) return false;

            if (hasSearch && !t.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                return false;

            if (anyStatusFilter)
            {
                bool matchesStatus = false;
                if (FilterEarned && t.IsEarned && !t.IsSynced) matchesStatus = true;
                if (FilterNotEarned && !t.IsEarned) matchesStatus = true;
                if (FilterSynced && t.IsSynced) matchesStatus = true;
                if (!matchesStatus) return false;
            }

            if (anyTypeFilter)
            {
                bool matchesType = false;
                if (FilterPlatinum && t.TypeCode == "P") matchesType = true;
                if (FilterGold && t.TypeCode == "G") matchesType = true;
                if (FilterSilver && t.TypeCode == "S") matchesType = true;
                if (FilterBronze && t.TypeCode == "B") matchesType = true;
                if (!matchesType) return false;
            }

            return true;
        };
        FilteredCount = view.Cast<object>().Count();
    }

    public ObservableCollection<TrophyRowViewModel> Trophies { get; }
    public ObservableCollection<string> ProfileNames { get; } = new();

    [ObservableProperty] private string? _selectedProfile;
    private string? _lastSavedProfile;

    public bool HasProfileChange => IsFileOpen && SelectedProfile != _lastSavedProfile;

    partial void OnSelectedProfileChanged(string? value) => OnPropertyChanged(nameof(IsSaveButtonEnabled));

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
            WindowTitle = string.IsNullOrEmpty(gameName) ? Properties.Strings.AppTitle : $"{Properties.Strings.AppTitle} | {gameName}";
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
        WindowTitle = Properties.Strings.AppTitle;
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
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
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

            _trophyFileService.ApplyScrapedTimestamps(applied);
            RefreshTrophyList();
            ShowToastNotification(string.Format(Properties.Strings.ImportedTimestamps, timestamps.Count));
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
            foreach (var t in trophyList)
                nameToId[t.Name] = t.Id;

            var timestamps = new List<ScrapedTimestamp>();
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

                if (!nameToId.TryGetValue(name, out int trophyId))
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

                var unix = new DateTimeOffset(dateTime, TimeSpan.Zero).ToUnixTimeSeconds();
                timestamps.Add(new ScrapedTimestamp(trophyId, unix));
            }

            if (timestamps.Count == 0)
            {
                var msg = Properties.Strings.NoValidTimestampsInFile;
                if (unmatchedNames.Count > 0)
                    msg += $"\n\nUnmatched entries ({unmatchedNames.Count}):\n" + string.Join("\n", unmatchedNames.Take(10));
                _dialogService.ShowError(msg, Properties.Strings.NoDataFound);
                return;
            }

            _trophyFileService.ApplyScrapedTimestamps(timestamps);
            RefreshTrophyList();

            var toast = string.Format(Properties.Strings.ImportedFromFile, timestamps.Count);
            if (unmatchedNames.Count > 0)
                toast += string.Format(Properties.Strings.UnmatchedSuffix, unmatchedNames.Count);
            ShowToastNotification(toast);

            if (unmatchedNames.Count > 0)
            {
                _dialogService.ShowInfo(
                    string.Format(Properties.Strings.ImportCompleteMessage, timestamps.Count, unmatchedNames.Count) + "\n" +
                    string.Join("\n", unmatchedNames.Take(20)),
                    Properties.Strings.ImportComplete);
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"[{ErrorCodes.NetImportFailed}] {Properties.Strings.ErrorFileImportFailed}\n{ex.Message}");
        }
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
        WindowTitle = Properties.Strings.AppTitle;
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
            System.Diagnostics.Process.Start(exePath);
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

    private async Task ResetSaveSucceededAsync()
    {
        await Task.Delay(2000);
        SaveSucceeded = false;
    }

    private readonly Queue<(string Message, ToastType Type)> _toastQueue = new();
    private bool _isToastAnimating;

    private void ShowToastNotification(string message, ToastType type = ToastType.Success)
    {
        _toastQueue.Enqueue((message, type));
        if (!_isToastAnimating)
            ProcessNextToast();
    }

    private void ProcessNextToast()
    {
        if (!_toastQueue.TryDequeue(out var next))
        {
            _isToastAnimating = false;
            return;
        }
        _isToastAnimating = true;
        ToastMessage = next.Message;
        ToastType = next.Type;
        ShowToast = false;
        ShowToast = true;
        _ = ResetToastAsync();
    }

    private async Task ResetToastAsync()
    {
        var delay = ToastType == ToastType.Error ? 4000 : 2500;
        await Task.Delay(delay);
        ShowToast = false;
        await Task.Delay(350);
        ProcessNextToast();
    }

    private void RefreshTrophyList()
    {
        var newList = _trophyFileService.GetTrophyList();

        if (Trophies.Count == newList.Count)
        {
            // Update existing items in-place to preserve scroll position and avoid full re-render
            for (int i = 0; i < newList.Count; i++)
            {
                var src = newList[i];
                var dst = Trophies[i];
                dst.IsEarned = src.IsEarned;
                dst.IsSynced = src.IsSynced;
                dst.EarnedTime = src.EarnedTime;
            }
        }
        else
        {
            Trophies.Clear();
            foreach (var trophy in newList)
                Trophies.Add(trophy);
        }

        HasNoTrophies = Trophies.Count == 0;
        HasUnsavedChanges = _trophyFileService.HasUnsavedChanges;
        HasPlatinum = _trophyFileService.CurrentState?.Config.HasPlatinum ?? false;
        AccountId = _trophyFileService.CurrentState?.UserState.AccountId ?? string.Empty;

        var (earned, total, earnedGrade, totalGrade) = _trophyFileService.GetCompletionStats();
        UpdateStats(earned, total, earnedGrade, totalGrade);

        PlatinumCount = Trophies.Count(t => t.TypeCode == "P" && t.IsEarned);
        GoldCount = Trophies.Count(t => t.TypeCode == "G" && t.IsEarned);
        SilverCount = Trophies.Count(t => t.TypeCode == "S" && t.IsEarned);
        BronzeCount = Trophies.Count(t => t.TypeCode == "B" && t.IsEarned);

        ApplyFilters();
    }

    private void UpdateStats(int earned, int total, int earnedGrade, int totalGrade)
    {
        EarnedCount = earned;
        TotalCount = total;
        EarnedGrade = earnedGrade;
        TotalGrade = totalGrade;
        CompletionPercent = total > 0 ? (double)earned / total * 100 : 0;
    }

    private void RefreshProfiles()
    {
        ProfileNames.Clear();
        foreach (var name in _profileService.GetProfileNames())
        {
            ProfileNames.Add(name);
        }
    }
}
