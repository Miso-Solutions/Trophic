using Trophic.Core.Helpers;
using Trophic.Core.Interfaces;
using Trophic.Core.Models;
using Trophic.TrophyFormat.Enums;
using Trophic.TrophyFormat.Exceptions;
using Trophic.TrophyFormat.Parsers;
using Trophic.TrophyFormat.Timestamps;

namespace Trophic.Core.Services;

public sealed class TrophyFileService : ITrophyFileService
{
    private const int SfoDataOffsetPosition = 0x0C;
    private const int SfoProfileIdLength = 0x10;
    private const int SfoTrophyProfileIdOffset = 0x274;

    private readonly IPfdToolService _pfdTool;
    private TrophyFileState? _state;
    private bool _hasUnsavedChanges;

    public bool IsOpen => _state != null;
    public bool HasUnsavedChanges => _hasUnsavedChanges;
    public TrophyFileState? CurrentState => _state;

    private bool _isRpcs3Format;
    public bool IsRpcs3Format
    {
        get => _isRpcs3Format;
        set => _isRpcs3Format = value;
    }

    public TimeZoneInfo DisplayTimeZone { get; set; } = TimeZoneInfo.Local;

    public TrophyFileService(IPfdToolService pfdTool)
    {
        _pfdTool = pfdTool;
    }

    public async Task OpenAsync(string folderPath)
    {
        Close();

        // Copy to temp for safe editing
        string tempPath = FileHelper.CopyTrophyDirToTemp(folderPath);

        try
        {
            // Decrypt if not RPCS3
            if (!_isRpcs3Format)
            {
                // Verify required files exist before decryption
                var troptrnsPath = Path.Combine(tempPath, "TROPTRNS.DAT");
                var paramPfdPath = Path.Combine(tempPath, "PARAM.PFD");
                if (!File.Exists(troptrnsPath))
                    throw new FileNotFoundException($"[{ErrorCodes.FileTroptrnsNotFound}] TROPTRNS.DAT not found in: {tempPath}");
                if (!File.Exists(paramPfdPath))
                    throw new FileNotFoundException($"[{ErrorCodes.FileParamPfdNotFound}] PARAM.PFD not found in: {tempPath}. pfdtool requires this file for decryption.");

                await _pfdTool.DecryptTrophyAsync(tempPath);

                // Verify decryption actually worked by checking the magic bytes
                var header = new byte[8];
                using (var fs = File.OpenRead(troptrnsPath))
                    fs.Read(header, 0, 8);
                var magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(header);
                if (magic != TrophyFormat.Models.TrophyFileHeader.ExpectedMagic)
                {
                    var hexDump = Convert.ToHexString(header);
                    throw new InvalidOperationException(
                        $"[{ErrorCodes.FmtDecryptionInvalid}] Decryption completed but TROPTRNS.DAT magic is invalid (magic: 0x{magic:X16}, first 8 bytes: {hexDump}). " +
                        $"The PFD encryption keys may not match this trophy set.");
                }
            }

            // Parse all trophy files
            var config = new TropConfParser(tempPath, _isRpcs3Format);
            var trns = new TropTrnsParser(tempPath, _isRpcs3Format);
            var usr = new TropUsrParser(tempPath, _isRpcs3Format);

            _state = new TrophyFileState
            {
                OriginalPath = folderPath,
                TempPath = tempPath,
                Config = config,
                Transactions = trns,
                UserState = usr,
                IsRpcs3 = _isRpcs3Format
            };

            _hasUnsavedChanges = false;
        }
        catch (Exception)
        {
            FileHelper.DeleteTempDirectory(tempPath);
            throw;
        }
    }

    public async Task SaveAsync(string? profileName = null)
    {
        if (_state == null) return;

        // Resign to a different profile if specified (before Save so raw data includes new ID)
        if (!string.IsNullOrEmpty(profileName))
        {
            ResignToProfile(profileName, _state);
        }

        // Save parsed data back to temp files
        _state.Transactions.Save();
        _state.UserState.Save();

        // Encrypt first, then update PFD hashes (hashes must be over encrypted file data)
        if (!_state.IsRpcs3)
        {
            await _pfdTool.EncryptTrophyAsync(_state.TempPath);
            await _pfdTool.UpdatePfdAsync(_state.TempPath);
        }

        // Copy back to original location
        FileHelper.CopyTempBackToSource(_state.TempPath, _state.OriginalPath);
        _hasUnsavedChanges = false;
    }

    /// <summary>
    /// Reads the account/profile ID from a profile's PARAM.SFO and writes it
    /// into the trophy folder's PARAM.SFO at offset 0x274 (16 bytes).
    /// Writes the profile's account ID into the trophy folder's PARAM.SFO.
    /// </summary>
    private static void ResignToProfile(string profileName, TrophyFileState state)
    {
        var profilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles", $"{profileName}.SFO");
        if (!File.Exists(profilePath))
            throw new FileNotFoundException($"[{ErrorCodes.FileProfileNotFound}] Profile not found: {profileName}");

        var trophySfoPath = Path.Combine(state.TempPath, "PARAM.SFO");
        if (!File.Exists(trophySfoPath))
            throw new FileNotFoundException($"[{ErrorCodes.FileSfoNotFound}] PARAM.SFO not found in trophy folder");

        // Extract profile ID from the profile's PARAM.SFO
        // Structure: magic 0x00505346 ("\0PSF"), then offset 0x0C contains an int32
        // pointer to the data area. The first 16 bytes at that offset are the account/profile ID.
        byte[] profileId;
        using (var br = new BinaryReader(File.OpenRead(profilePath)))
        {
            // Validate SFO magic bytes: 0x00 0x50 0x53 0x46 ("\0PSF")
            if (br.BaseStream.Length < 0x14)
                throw new InvalidOperationException($"Profile PARAM.SFO is too small to be valid.");
            var magic = br.ReadUInt32();
            if (magic != 0x46535000) // "\0PSF" in little-endian
                throw new InvalidOperationException($"Profile PARAM.SFO has invalid magic (expected PSF format).");

            br.BaseStream.Position = SfoDataOffsetPosition;
            int dataOffset = br.ReadInt32();
            if (dataOffset < 0 || dataOffset + SfoProfileIdLength > br.BaseStream.Length)
                throw new InvalidOperationException($"Profile PARAM.SFO has invalid data offset.");
            br.BaseStream.Position = dataOffset;
            profileId = br.ReadBytes(SfoProfileIdLength);
        }

        // Write the profile ID into the trophy's PARAM.SFO at offset 0x274
        using (var bw = new BinaryWriter(File.Open(trophySfoPath, FileMode.Open, FileAccess.Write)))
        {
            bw.BaseStream.Position = SfoTrophyProfileIdOffset;
            bw.Write(profileId);
        }

        // Also patch the account ID in the in-memory DAT parsers so Save() writes it
        state.Transactions.SetAccountId(profileId);
        state.UserState.SetAccountId(profileId);
    }

    public void Close()
    {
        if (_state != null)
        {
            FileHelper.DeleteTempDirectory(_state.TempPath);
            _state = null;
            _hasUnsavedChanges = false;
        }
    }

    public void UnlockTrophy(int trophyId, DateTime timestamp)
    {
        if (_state == null) return;

        var utcTime = ConvertDisplayToUtc(timestamp);
        var config = _state.Config;
        var trophyType = config[trophyId].Type;

        _state.UserState.UnlockTrophy(trophyId, utcTime);
        _state.Transactions.PutTrophy(trophyId, trophyType, utcTime);

        _hasUnsavedChanges = true;
    }

    public void LockTrophy(int trophyId, bool force = false)
    {
        if (_state == null) return;

        _state.UserState.LockTrophy(trophyId, force);
        try { _state.Transactions.DeleteTrophyById(trophyId); }
        catch (TrophyAlreadySyncException) { }

        _hasUnsavedChanges = true;
    }

    public void ChangeTrophyTime(int trophyId, DateTime newTime)
    {
        if (_state == null) return;

        var utcTime = ConvertDisplayToUtc(newTime);

        _state.UserState.ChangeTrophyTime(trophyId, utcTime);
        _state.Transactions.ChangeTime(trophyId, utcTime);

        _hasUnsavedChanges = true;
    }

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

    public void ApplyScrapedTimestamps(IReadOnlyList<ScrapedTimestamp> timestamps)
    {
        if (_state == null) return;

        foreach (var ts in timestamps)
        {
            if (ts.TrophyId < 0 || ts.TrophyId >= _state.UserState.TrophyTimeInfos.Count)
                continue;

            if (ts.UnixTimestamp == 0) continue;

            var utcDateTime = DateTimeOffset.FromUnixTimeSeconds(ts.UnixTimestamp).UtcDateTime;

            var timeInfo = _state.UserState.TrophyTimeInfos[ts.TrophyId];
            if (timeInfo.IsSynced)
                continue;

            // Scraped timestamps are correct UTC. Convert to the face value the user
            // sees on the website (system timezone), then route through UnlockTrophy/
            // ChangeTrophyTime which converts from the selected display timezone to UTC.
            // This ensures the website's face value appears in whatever timezone is selected.
            var faceValue = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc),
                TimeZoneInfo.Local);


            if (timeInfo.IsEarned)
            {
                try { ChangeTrophyTime(ts.TrophyId, faceValue); }
                catch (Exception) { /* Batch: skip individual failures */ }
            }
            else
            {
                try { UnlockTrophy(ts.TrophyId, faceValue); }
                catch (Exception) { /* Batch: skip individual failures */ }
            }
        }

        _hasUnsavedChanges = true;
    }

    public IReadOnlyList<TrophyRowViewModel> GetTrophyList()
    {
        if (_state == null) return Array.Empty<TrophyRowViewModel>();

        var config = _state.Config;
        var usr = _state.UserState;
        var trns = _state.Transactions;

        var result = new List<TrophyRowViewModel>();
        int baseGameCount = 0;
        bool inBaseGame = true;
        var seenGroups = new HashSet<int>();

        for (int i = 0; i < config.Count; i++)
        {
            var def = config[i];
            var timeInfo = i < usr.TrophyTimeInfos.Count ? usr.TrophyTimeInfos[i] : null;
            var trnsRecord = trns[i];

            // Determine group.
            // DLC is identified by: gid > 0 (explicit group), OR
            // pid == -1 on non-platinum trophies after the base game section
            // (some older games don't use gid, only pid=-1 for DLC trophies).
            bool isDlcByPid = def.GroupId == 0 && def.ParentId == -1 && def.Type != TrophyType.Platinum;
            int effectiveGroupId = def.GroupId;

            if (isDlcByPid && !inBaseGame)
            {
                // Continue in the current DLC group
                effectiveGroupId = seenGroups.Count > 0 ? seenGroups.Max() : 1;
            }
            else if (isDlcByPid && inBaseGame)
            {
                // First DLC trophy — transition out of base game
                inBaseGame = false;
                effectiveGroupId = 1;
            }

            if (effectiveGroupId == 0 || (def.Type == TrophyType.Platinum && i == 0))
            {
                if (inBaseGame) baseGameCount++;
            }
            else
            {
                inBaseGame = false;
            }

            string groupLabel = effectiveGroupId == 0
                ? "Base Game"
                : config.GetGroupName(effectiveGroupId) ?? (isDlcByPid ? "DLC" : $"DLC {effectiveGroupId}");
            bool isFirstInDlcGroup = effectiveGroupId > 0 && seenGroups.Add(effectiveGroupId);

            // Icon path
            string? iconPath = null;
            var iconFile = Path.Combine(_state.TempPath, $"TROP{def.Id:000}.PNG");
            if (File.Exists(iconFile))
                iconPath = iconFile;

            // Determine earned status: TROPUSR is authoritative
            // Bitfield is a secondary check for inconsistent state
            bool isEarned = timeInfo?.IsEarned ?? false;
            bool isSynced = timeInfo?.IsSynced ?? false;

            if (!isEarned && usr.ListInfo.IsTrophyUnlocked(i))
                isEarned = true;

            DateTime? earnedTime = null;
            if (isEarned)
            {
                // Prefer TROPUSR timestamp, fall back to TROPTRANS
                if (timeInfo != null && timeInfo.GetTime != DateTime.MinValue)
                    earnedTime = ConvertUtcToDisplay(timeInfo.GetTime);
                else if (trnsRecord != null && trnsRecord.GetTime != DateTime.MinValue)
                    earnedTime = ConvertUtcToDisplay(trnsRecord.GetTime);
            }

            result.Add(new TrophyRowViewModel
            {
                Id = def.Id,
                Name = def.Name,
                Detail = def.Detail,
                TypeCode = def.Type.ToCode(),
                IsHidden = def.Hidden,
                GroupId = effectiveGroupId,
                GroupLabel = groupLabel,
                IsFirstInDlcGroup = isFirstInDlcGroup,
                IconPath = iconPath,
                IsEarned = isEarned,
                IsSynced = isSynced,
                EarnedTime = earnedTime
            });
        }

        return result;
    }

    public (int earned, int total, int earnedGrade, int totalGrade) GetCompletionStats()
    {
        if (_state == null) return (0, 0, 0, 0);

        var config = _state.Config;
        var usr = _state.UserState;

        int earned = 0, total = config.Count;
        int earnedGrade = 0, totalGrade = 0;

        var trns = _state.Transactions;

        for (int i = 0; i < config.Count; i++)
        {
            var grade = config[i].Type.GradePoints();
            totalGrade += grade;

            bool isEarned = i < usr.TrophyTimeInfos.Count && usr.TrophyTimeInfos[i].IsEarned;
            if (!isEarned)
                isEarned = usr.ListInfo.IsTrophyUnlocked(i);
            if (!isEarned)
                isEarned = trns[i] is { IsExist: true };

            if (isEarned)
            {
                earned++;
                earnedGrade += grade;
            }
        }

        return (earned, total, earnedGrade, totalGrade);
    }

    /// <summary>
    /// Converts a UTC DateTime to the selected display timezone.
    /// </summary>
    private DateTime ConvertUtcToDisplay(DateTime utcDateTime)
    {
        if (utcDateTime == DateTime.MinValue) return DateTime.MinValue;
        return TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc),
            DisplayTimeZone);
    }

    /// <summary>
    /// Converts a DateTime in the selected display timezone to UTC.
    /// </summary>
    private DateTime ConvertDisplayToUtc(DateTime displayDateTime)
    {
        if (displayDateTime == DateTime.MinValue) return DateTime.MinValue;
        return TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(displayDateTime, DateTimeKind.Unspecified),
            DisplayTimeZone);
    }
}
