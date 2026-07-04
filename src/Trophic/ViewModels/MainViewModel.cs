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
    private readonly Core.Services.UpdateCheckService _updateChecker;
    private readonly Core.Services.AutoUpdateService _autoUpdater;
    private readonly Core.Services.DiagnosticLogger _logger;
    private readonly Core.Services.BackupService _backupService;
    private readonly Core.Licensing.LicenseService _licenseService;
    private string? _catalogDownloadFolder;

    public static string AppVersion => System.Reflection.Assembly.GetExecutingAssembly()
        .GetName().Version?.ToString(3) ?? "1.0.0";

    private static string BaseTitle => $"{Properties.Strings.AppTitle} v{AppVersion}";

    public MainViewModel(
        ITrophyFileService trophyFileService,
        IDialogService dialogService,
        IWebScraperService webScraper,
        IProfileService profileService,
        ISettingsService settingsService,
        RecentFilesService recentFiles,
        Core.Services.TrophyDownloadService downloadService,
        Core.Services.UpdateCheckService updateChecker,
        Core.Services.AutoUpdateService autoUpdater,
        Core.Services.DiagnosticLogger logger,
        Core.Services.BackupService backupService,
        Core.Licensing.LicenseService licenseService)
    {
        _trophyFileService = trophyFileService;
        _dialogService = dialogService;
        _webScraper = webScraper;
        _profileService = profileService;
        _settingsService = settingsService;
        _recentFiles = recentFiles;
        _downloadService = downloadService;
        _updateChecker = updateChecker;
        _autoUpdater = autoUpdater;
        _logger = logger;
        _backupService = backupService;
        _licenseService = licenseService;

        Trophies = new ObservableCollection<TrophyRowViewModel>();
        RecentFiles = new ObservableCollection<RecentFileEntry>(_recentFiles.GetEntries());
        RefreshProfiles();
        ApplyTimeZone();
        ApplyLanguage();
        _logger.Info($"Trophic v{AppVersion} started");

        // Check for updates in the background (non-blocking)
        _ = CheckForUpdateAsync();
    }

    // --- Observable Properties ---

    [ObservableProperty] private string _windowTitle = $"{Properties.Strings.AppTitle}";
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

    public ReadOnlyCollection<TimeZoneInfo> AvailableTimeZones { get; } = TimeZoneInfo.GetSystemTimeZones();

    partial void OnSelectedTimeZoneChanged(TimeZoneInfo value) => ApplyTimeZone();

    private void ApplyTimeZone()
    {
        _trophyFileService.DisplayTimeZone = SelectedTimeZone;
        RefreshTrophyList();
    }

    /// <summary>
    /// Returns the current moment in the selected display timezone.
    /// Native DST rules are applied automatically.
    /// </summary>
    private DateTime NowInDisplayTimeZone()
    {
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SelectedTimeZone);
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
        return TimeZoneInfo.ConvertTimeFromUtc(utc, SelectedTimeZone);
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
