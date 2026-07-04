using Trophic.Core.Helpers;
using Trophic.Core.Models;

namespace Trophic.Core.Services;

/// <summary>
/// Batch trophy operations: instant platinum/unlock, clear-all, and applying scraped
/// or imported timestamps. Split from the main TrophyFileService for file-size clarity.
/// </summary>
public sealed partial class TrophyFileService
{
    public void InstantPlatinum(DateTime startTime, DateTime endTime)
    {
        if (_state == null) return;

        var config = _state.Config;
        var usr = _state.UserState;
        int total = config.Count;

        // Generate timestamps in order
        var times = new DateTime[total];
        for (int i = 0; i < total; i++)
        {
            times[i] = RandomHelper.NextDateTime(startTime, endTime);
        }
        Array.Sort(times);

        // Platinum goes last (if exists)
        int platIndex = -1;
        if (config.HasPlatinum)
            platIndex = 0;

        for (int i = 0; i < total; i++)
        {
            if (i == platIndex) continue; // Skip platinum for now

            var timeInfo = usr.TrophyTimeInfos[i];
            if (timeInfo.IsSynced)
                continue;

            try
            {
                if (timeInfo.IsEarned)
                    ChangeTrophyTime(i, times[i]);
                else
                    UnlockTrophy(i, times[i]);
            }
            catch (Exception)
            {
                // Intentional: batch operations skip individual failures
                // (e.g., synced trophies, format mismatches) to process remaining trophies
            }
        }

        // Unlock platinum last with the latest time
        if (platIndex >= 0)
        {
            var timeInfo = usr.TrophyTimeInfos[platIndex];
            if (!timeInfo.IsSynced)
            {
                try
                {
                    if (timeInfo.IsEarned)
                        ChangeTrophyTime(platIndex, endTime);
                    else
                        UnlockTrophy(platIndex, endTime);
                }
                catch (Exception) { /* Batch: skip individual failures */ }
            }
        }

        _hasUnsavedChanges = true;
    }

    public void InstantUnlock(DateTime timestamp)
    {
        if (_state == null) return;

        var config = _state.Config;
        var usr = _state.UserState;

        for (int i = 0; i < config.Count; i++)
        {
            var timeInfo = usr.TrophyTimeInfos[i];

            if (timeInfo.IsSynced)
                continue;

            if (timeInfo.IsEarned)
            {
                // Overwrite timestamp for already-earned non-synced trophies
                try
                {
                    var utcTime = ConvertDisplayToUtc(timestamp);
                    _state.UserState.ChangeTrophyTime(i, utcTime);
                    if (!_state.IsRpcs3)
                    {
                        try { _state.Transactions.ChangeTime(i, utcTime); }
                        catch (Exception) { /* Expected: record may not exist in TROPTRNS */ }
                    }
                }
                catch (Exception) { /* Batch: skip individual failures */ }
                continue;
            }

            try { UnlockTrophy(i, timestamp); }
            catch (Exception) { /* Batch: skip individual failures */ }
        }

        _hasUnsavedChanges = true;
    }

    public void ClearAllTrophies(bool includeSynced = false)
    {
        if (_state == null) return;

        var usr = _state.UserState;

        for (int i = usr.TrophyTimeInfos.Count - 1; i >= 0; i--)
        {
            var timeInfo = usr.TrophyTimeInfos[i];

            bool isEarned = timeInfo.IsEarned || usr.ListInfo.IsTrophyUnlocked(i);

            if (!isEarned) continue;
            if (!includeSynced && timeInfo.IsSynced) continue;

            try { LockTrophy(i, force: includeSynced); }
            catch (Exception) { /* Batch: skip individual failures */ }
        }

        _hasUnsavedChanges = true;
    }

    public ApplyTimestampsResult ApplyScrapedTimestamps(IReadOnlyList<ScrapedTimestamp> timestamps)
    {
        if (_state == null) return new ApplyTimestampsResult(0, 0, 0, 0, null);

        int applied = 0, outOfRange = 0, synced = 0, failed = 0;
        string? firstError = null;

        foreach (var ts in timestamps)
        {
            if (ts.TrophyId < 0 || ts.TrophyId >= _state.UserState.TrophyTimeInfos.Count)
            {
                outOfRange++;
                continue;
            }

            if (ts.UnixTimestamp == 0) { failed++; continue; }

            var utcDateTime = DateTimeOffset.FromUnixTimeSeconds(ts.UnixTimestamp).UtcDateTime;

            var timeInfo = _state.UserState.TrophyTimeInfos[ts.TrophyId];
            if (timeInfo.IsSynced)
            {
                synced++;
                continue;
            }

            // Scraped timestamps are correct UTC. Convert to the face value the user
            // sees on the website (system timezone), then route through UnlockTrophy/
            // ChangeTrophyTime which converts from the selected display timezone to UTC.
            // This ensures the website's face value appears in whatever timezone is selected.
            var faceValue = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc),
                TimeZoneInfo.Local);

            try
            {
                if (timeInfo.IsEarned)
                    ChangeTrophyTime(ts.TrophyId, faceValue);
                else
                    UnlockTrophy(ts.TrophyId, faceValue);
                applied++;
            }
            catch (Exception ex)
            {
                failed++;
                firstError ??= ex.Message;
            }
        }

        _hasUnsavedChanges = true;
        return new ApplyTimestampsResult(applied, outOfRange, synced, failed, firstError);
    }
}
