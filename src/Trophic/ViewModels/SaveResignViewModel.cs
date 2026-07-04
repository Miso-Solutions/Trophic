using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Trophic.Core.Interfaces;
using Trophic.Core.Services;

namespace Trophic.ViewModels;

public sealed partial class SaveResignViewModel : ObservableObject
{
    private readonly SaveResignService _service;
    private readonly TrophyResignService _trophyService;
    private readonly IProfileService _profiles;
    private readonly ConsoleKeysService _consoleKeys;
    private readonly GamesConfService? _gamesConf;

    public SaveResignViewModel(SaveResignService service, TrophyResignService trophyService,
        IProfileService profiles, ConsoleKeysService consoleKeys, GamesConfService? gamesConf = null)
    {
        _service = service;
        _trophyService = trophyService;
        _profiles = profiles;
        _consoleKeys = consoleKeys;
        _gamesConf = gamesConf;
        Queue = new ObservableCollection<SaveResignQueueItem>();
        RefreshProfiles();
        RefreshKeysStatus();
        RefreshGamesConfStatus();
    }

    [ObservableProperty]
    private string _gamesConfStatus = "not loaded";

    [ObservableProperty]
    private bool _outputAsZip;

    public void RefreshGamesConfStatus()
    {
        if (_gamesConf is { IsLoaded: true })
            GamesConfStatus = $"{_gamesConf.GameCount} games";
        else
            GamesConfStatus = "not loaded (drop games.conf next to Trophic.exe)";
    }

    public ObservableCollection<SaveResignQueueItem> Queue { get; }
    public ObservableCollection<string> AvailableProfiles { get; } = new();

    [ObservableProperty]
    private string? _selectedProfile;

    [ObservableProperty]
    private string? _customProfileSfoPath;

    [ObservableProperty]
    private string? _outputFolder;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _consoleKeysConfigured;

    public void RefreshProfiles()
    {
        AvailableProfiles.Clear();
        foreach (var name in _profiles.GetProfileNames())
            AvailableProfiles.Add(name);
        if (SelectedProfile == null && AvailableProfiles.Count > 0)
            SelectedProfile = AvailableProfiles[0];
    }

    public void RefreshKeysStatus()
    {
        ConsoleKeysConfigured = _consoleKeys.GetKeys().IsConfigured;
    }

    [RelayCommand]
    private void AddSaves()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select save zip files or PARAM.PFD inside save folders",
            Filter = "Save archives or PARAM.PFD|*.zip;PARAM.PFD|All files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        foreach (var path in dlg.FileNames)
        {
            string source = path.EndsWith("PARAM.PFD", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(path)! : path;
            EnqueueIfNew(source);
        }
    }

    [RelayCommand]
    private void AddSaveFolders()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Select one or more save folders",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        foreach (var folder in dlg.FolderNames)
            EnqueueIfNew(folder);
    }

    private void EnqueueIfNew(string source)
    {
        foreach (var existing in Queue)
            if (string.Equals(existing.SourcePath, source, StringComparison.OrdinalIgnoreCase))
                return;
        Queue.Add(new SaveResignQueueItem(source));
    }

    [RelayCommand]
    private void BrowseCustomProfile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select target PARAM.SFO",
            Filter = "PARAM.SFO|PARAM.SFO;*.SFO|All files|*.*"
        };
        if (dlg.ShowDialog() == true)
            CustomProfileSfoPath = dlg.FileName;
    }

    [RelayCommand]
    private void ClearCustomProfile()
    {
        CustomProfileSfoPath = null;
    }

    [RelayCommand]
    private void BrowseOutputFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Pick destination folder for resigned saves" };
        if (dlg.ShowDialog() == true)
            OutputFolder = dlg.FolderName;
    }

    [RelayCommand]
    private void RemoveSelected(SaveResignQueueItem? item)
    {
        if (item != null) Queue.Remove(item);
    }

    [RelayCommand]
    private void ClearQueue()
    {
        Queue.Clear();
    }

    [RelayCommand]
    private void ReloadGamesConf()
    {
        _gamesConf?.Reload();
        RefreshGamesConfStatus();
    }

    private CancellationTokenSource? _batchCts;
    private Task? _batchTask;

    /// <summary>
    /// Awaits the in-flight batch (if any). Caller is expected to call CancelBatch first
    /// when shutting down so the loop exits at the next safe point. Returns immediately
    /// if no batch is running.
    /// </summary>
    public Task WaitForBatchAsync()
        => _batchTask ?? Task.CompletedTask;

    [RelayCommand]
    private async Task StartBatch()
    {
        if (IsRunning) return;
        if (Queue.Count == 0) { StatusMessage = "Queue is empty."; return; }
        if (string.IsNullOrEmpty(OutputFolder)) { StatusMessage = "Pick an output folder first."; return; }

        var profileSfoPath = ResolveTargetProfileSfoPath();
        if (profileSfoPath == null) { StatusMessage = "Pick a target profile or browse to a PARAM.SFO."; return; }

        // Trophy items resign via TrophyResignService (no Console Keys required); generic
        // saves go through SaveResignService (requires Console Keys).
        bool anyGenericSave = Queue.Any(q => !q.IsTrophy);
        if (anyGenericSave && !_consoleKeys.GetKeys().IsConfigured)
        {
            StatusMessage = "Console keys required for non-trophy saves. Open Menu → Console Keys, or queue only trophy folders.";
            return;
        }

        _batchCts = new CancellationTokenSource();
        IsRunning = true;
        StatusMessage = "Resigning...";
        _batchTask = RunBatchCore(profileSfoPath);
        try { await _batchTask; }
        finally { _batchTask = null; }
    }

    private async Task RunBatchCore(string profileSfoPath)
    {
        // _batchCts is assigned by the caller (StartBatch) before this method is invoked.
        var cts = _batchCts!;
        int ok = 0, fail = 0;

        try
        {
            foreach (var item in Queue)
            {
                if (cts.IsCancellationRequested)
                {
                    item.Status = "Cancelled";
                    continue;
                }
                item.Status = "Running";

                SaveResignResult result;
                if (item.IsTrophy)
                    result = await _trophyService.ResignAsync(item.SourcePath, profileSfoPath, OutputFolder!, OutputAsZip, cts.Token);
                else
                    result = await _service.ResignAsync(item.SourcePath, profileSfoPath, OutputFolder!, OutputAsZip, cts.Token);

                if (result.Success)
                {
                    item.Status = "Done";
                    item.OutputPath = result.OutputPath;
                    ok++;
                }
                else
                {
                    item.Status = "Failed";
                    item.OutputPath = result.ErrorMessage;
                    fail++;
                }
            }
        }
        finally
        {
            IsRunning = false;
            StatusMessage = cts.IsCancellationRequested
                ? $"Cancelled — {ok} succeeded, {fail} failed before stop."
                : $"Done — {ok} succeeded, {fail} failed.";
            cts.Dispose();
            _batchCts = null;
        }
    }

    public void CancelBatch() => _batchCts?.Cancel();

    private string? ResolveTargetProfileSfoPath()
    {
        if (!string.IsNullOrEmpty(CustomProfileSfoPath) && File.Exists(CustomProfileSfoPath))
            return CustomProfileSfoPath;
        if (!string.IsNullOrEmpty(SelectedProfile))
            return _profiles.GetProfilePath(SelectedProfile);
        return null;
    }
}

public sealed partial class SaveResignQueueItem : ObservableObject
{
    public SaveResignQueueItem(string sourcePath)
    {
        SourcePath = sourcePath;
        Name = Path.GetFileName(sourcePath);
        IsTrophy = DetectTrophy(sourcePath);
        InputType = IsTrophy ? "Trophy"
            : Directory.Exists(sourcePath) ? "Folder"
            : sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? "Zip"
            : "Unknown";
        Status = "Queued";
    }

    public string SourcePath { get; }
    public string Name { get; }
    public string InputType { get; }
    public bool IsTrophy { get; }

    [ObservableProperty] private string _status;
    [ObservableProperty] private string? _outputPath;

    private static bool DetectTrophy(string sourcePath)
    {
        if (Directory.Exists(sourcePath))
            return TrophyResignService.IsTrophyFolder(sourcePath)
                || Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories)
                       .Any(TrophyResignService.IsTrophyFolder);

        if (sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(sourcePath))
        {
            try
            {
                using var archive = System.IO.Compression.ZipFile.OpenRead(sourcePath);
                return archive.Entries.Any(e =>
                    e.Name.Equals("TROPCONF.SFM", StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }
        return false;
    }
}
