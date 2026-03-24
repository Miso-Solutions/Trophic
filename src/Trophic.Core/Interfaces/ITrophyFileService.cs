using Trophic.Core.Models;

namespace Trophic.Core.Interfaces;

public interface ITrophyFileService
{
    bool IsOpen { get; }
    bool HasUnsavedChanges { get; }
    bool IsRpcs3Format { get; set; }
    TimeZoneInfo DisplayTimeZone { get; set; }
    TrophyFileState? CurrentState { get; }

    Task OpenAsync(string folderPath);
    Task SaveAsync(string? profileName = null);
    void Close();

    void UnlockTrophy(int trophyId, DateTime timestamp);
    void LockTrophy(int trophyId, bool force = false);
    void ChangeTrophyTime(int trophyId, DateTime newTime);

    void InstantPlatinum(DateTime startTime, DateTime endTime);
    void InstantUnlock(DateTime timestamp);
    void ClearAllTrophies(bool includeSynced = false);
    void ApplyScrapedTimestamps(IReadOnlyList<ScrapedTimestamp> timestamps);

    IReadOnlyList<TrophyRowViewModel> GetTrophyList();
    (int earned, int total, int earnedGrade, int totalGrade) GetCompletionStats();
}
