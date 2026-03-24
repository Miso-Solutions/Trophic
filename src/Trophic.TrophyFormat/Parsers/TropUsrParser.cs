using System.Buffers.Binary;
using Trophic.TrophyFormat.Binary;
using Trophic.TrophyFormat.Enums;
using Trophic.TrophyFormat.Exceptions;
using Trophic.TrophyFormat.Models;
using Trophic.TrophyFormat.Timestamps;

namespace Trophic.TrophyFormat.Parsers;

/// <summary>
/// Parses and mutates TROPUSR.DAT (trophy user state).
/// Sequential block-based big-endian binary format.
/// </summary>
public sealed class TropUsrParser
{
    private const string FileName = "TROPUSR.DAT";
    private const int BlockHeaderSize = 16;

    private readonly string _filePath;
    private readonly bool _isRpcs3;

    // Parsed data
    private TrophyFileHeader _header = null!;
    private List<TypeRecord> _typeRecords = new();
    private string _accountId = string.Empty;
    private string _trophyId = string.Empty;
    private int _allTrophyNumber;
    private readonly List<UsrTrophyType> _trophyTypes = new();
    private UsrTrophyListInfo _listInfo = new();
    private readonly List<UsrTrophyTimeInfo> _timeInfos = new();
    private UsrUnknownType7 _type7 = new();
    private byte[] _type8Hash = new byte[20];

    // Raw file data for re-serialization
    private byte[] _rawFileData = Array.Empty<byte>();

    // Block positions for re-writing
    private readonly List<(int type, long offset, int dataSize)> _blockPositions = new();

    public string AccountId => _accountId;

    /// <summary>
    /// Patches the account ID in the raw file data and updates the in-memory value.
    /// </summary>
    public void SetAccountId(byte[] newId16)
    {
        var pos = _blockPositions.FirstOrDefault(b => b.type == 2);
        if (pos.dataSize >= 32)
        {
            newId16.AsSpan(0, 16).CopyTo(_rawFileData.AsSpan((int)pos.offset + BlockHeaderSize + 16, 16));
            _accountId = System.Text.Encoding.UTF8.GetString(newId16).TrimEnd('\0');
        }
    }

    public string TrophyId => _trophyId;
    public int AllTrophyNumber => _allTrophyNumber;
    public IReadOnlyList<UsrTrophyType> TrophyTypes => _trophyTypes;
    public UsrTrophyListInfo ListInfo => _listInfo;
    public IReadOnlyList<UsrTrophyTimeInfo> TrophyTimeInfos => _timeInfos;
    public UsrUnknownType7 Type7 => _type7;

    public DateTime LastSyncTime => _type7.LastSyncTime;
    public DateTime LastTrophyTime => _listInfo.ListLastGetTrophyTime;

    public TropUsrParser(string directoryPath, bool isRpcs3Format)
    {
        _filePath = Path.Combine(directoryPath, FileName);
        _isRpcs3 = isRpcs3Format;

        if (!File.Exists(_filePath))
            throw new FileNotFoundException($"Trophy user file not found: {_filePath}");

        Parse();
    }

    private void Parse()
    {
        _rawFileData = File.ReadAllBytes(_filePath);
        ReadOnlySpan<byte> data = _rawFileData.AsSpan();

        _header = TrophyFileHeader.ReadFrom(data, _filePath);

        // Read type records
        int offset = TrophyFileHeader.Size;
        for (int i = 0; i < _header.TypeRecordCount; i++)
        {
            _typeRecords.Add(TypeRecord.ReadFrom(data.Slice(offset)));
            offset += TypeRecord.Size;
        }

        // Read blocks sequentially
        while (offset + BlockHeaderSize <= _rawFileData.Length)
        {
            int blockType = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset));
            int blockSize = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset + 4));
            int sequenceNumber = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset + 8));

            if (blockSize <= 0 || offset + BlockHeaderSize + blockSize > _rawFileData.Length)
                break;

            _blockPositions.Add((blockType, offset, blockSize));

            var blockData = data.Slice(offset + BlockHeaderSize, blockSize);

            switch (blockType)
            {
                case 1:
                    // Unknown, skip
                    break;

                case 2:
                    // Account ID at blockdata[16..31]
                    if (blockSize >= 32)
                        _accountId = blockData.Slice(16, 16).ReadUtf8String(0, 16);
                    break;

                case 3:
                    // Trophy ID + achievement counts
                    if (blockSize >= 32)
                    {
                        _trophyId = blockData.ReadUtf8String(0, 16);
                        _allTrophyNumber = BinaryPrimitives.ReadInt32BigEndian(blockData.Slice(24));
                    }
                    break;

                case 4:
                    // Trophy type (one per trophy, 80 bytes)
                    if (blockSize >= UsrTrophyType.Size)
                        _trophyTypes.Add(UsrTrophyType.ReadFrom(blockData));
                    break;

                case 5:
                    // Trophy list info (208 bytes)
                    if (blockSize >= UsrTrophyListInfo.Size)
                        _listInfo = UsrTrophyListInfo.ReadFrom(blockData);
                    break;

                case 6:
                    // Trophy time info (one per trophy, 96 bytes)
                    if (blockSize >= UsrTrophyTimeInfo.Size)
                        _timeInfos.Add(UsrTrophyTimeInfo.ReadFrom(blockData));
                    break;

                case 7:
                    // Unknown type 7 (80 bytes)
                    if (blockSize >= UsrUnknownType7.Size)
                        _type7 = UsrUnknownType7.ReadFrom(blockData);
                    break;

                case 8:
                    // Hash (first 20 bytes)
                    if (blockSize >= 20)
                        blockData.Slice(0, 20).CopyTo(_type8Hash);
                    break;

                case 9:
                case 10:
                    // Unknown/padding, skip
                    break;
            }

            offset += BlockHeaderSize + blockSize;
        }
    }

    /// <summary>
    /// Unlock a trophy by ID with the given timestamp.
    /// Updates TrophyTimeInfo, TrophyListInfo, and UnknownType7.
    /// </summary>
    public void UnlockTrophy(int trophyId, DateTime dateTime)
    {
        if (trophyId < 0 || trophyId >= _timeInfos.Count)
            throw new TrophyNotFoundException(trophyId);

        var timeInfo = _timeInfos[trophyId];

        if (timeInfo.IsEarned)
            throw new TrophyAlreadyEarnedException(trophyId);

        if (timeInfo.IsSynced)
            throw new TrophyAlreadySyncException(trophyId);

        // Unlock the trophy
        timeInfo.Unlock(dateTime);

        // Update list info
        _listInfo.GetTrophyNumber++;
        _listInfo.SetTrophyUnlocked(trophyId, true);

        // Update last trophy time if this is more recent
        if (dateTime > _listInfo.ListLastGetTrophyTime)
            _listInfo.ListLastGetTrophyTime = dateTime;

        // Update type 7
        _type7.TrophyCount++;
    }

    /// <summary>
    /// Lock (un-earn) a trophy by ID.
    /// </summary>
    public void LockTrophy(int trophyId, bool force = false)
    {
        if (trophyId < 0 || trophyId >= _timeInfos.Count)
            throw new TrophyNotFoundException(trophyId);

        var timeInfo = _timeInfos[trophyId];

        if (!timeInfo.IsEarned && !_listInfo.IsTrophyUnlocked(trophyId))
            return;

        if (timeInfo.IsSynced && !force)
            throw new TrophyAlreadySyncException(trophyId);

        if (timeInfo.IsEarned)
        {
            timeInfo.Lock();
            _type7.TrophyCount--;
        }

        if (_listInfo.IsTrophyUnlocked(trophyId))
        {
            _listInfo.GetTrophyNumber--;
            _listInfo.SetTrophyUnlocked(trophyId, false);
        }

        // Recalculate last trophy time
        var max = DateTime.MinValue;
        foreach (var t in _timeInfos)
            if (t.IsEarned && t.GetTime > max) max = t.GetTime;
        _listInfo.ListLastGetTrophyTime = max;
    }

    /// <summary>
    /// Change the timestamp of an earned trophy.
    /// </summary>
    public void ChangeTrophyTime(int trophyId, DateTime newTime)
    {
        if (trophyId < 0 || trophyId >= _timeInfos.Count)
            throw new TrophyNotFoundException(trophyId);

        var timeInfo = _timeInfos[trophyId];

        if (!timeInfo.IsEarned)
            throw new TrophyNotFoundException(trophyId);

        if (timeInfo.IsSynced)
            throw new TrophyAlreadySyncException(trophyId);

        timeInfo.GetTime = newTime;
    }

    /// <summary>
    /// Save changes back to the file.
    /// WriteTo is a pure RawData copy — property setters already patched RawData.
    /// </summary>
    public void Save()
    {
        var data = _rawFileData.AsSpan();

        int timeInfoIndex = 0;

        foreach (var (blockType, blockOffset, blockSize) in _blockPositions)
        {
            var blockData = data.Slice((int)blockOffset + BlockHeaderSize, blockSize);

            switch (blockType)
            {
                case 5:
                    _listInfo.WriteTo(blockData);
                    break;

                case 6:
                    if (timeInfoIndex < _timeInfos.Count)
                    {
                        _timeInfos[timeInfoIndex].WriteTo(blockData);
                        timeInfoIndex++;
                    }
                    break;

                case 7:
                    _type7.WriteTo(blockData);
                    break;
            }
        }

        File.WriteAllBytes(_filePath, _rawFileData);
    }
}
