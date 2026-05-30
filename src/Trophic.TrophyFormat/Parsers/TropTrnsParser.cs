using System.Buffers.Binary;
using Trophic.TrophyFormat.Binary;
using Trophic.TrophyFormat.Enums;
using Trophic.TrophyFormat.Exceptions;
using Trophic.TrophyFormat.Models;
using Trophic.TrophyFormat.Timestamps;

namespace Trophic.TrophyFormat.Parsers;

/// <summary>
/// Parses and mutates TROPTRNS.DAT (trophy transaction log).
/// Binary big-endian format with 48-byte header, type record table, and data blocks.
/// </summary>
public sealed class TropTrnsParser
{
    private const string FileName = "TROPTRNS.DAT";
    private const int BlockHeaderSize = 16;

    private readonly string _filePath;
    private readonly bool _isRpcs3;

    // Parsed data
    private TrophyFileHeader _header = null!;
    private List<TypeRecord> _typeRecords = new();
    private string _accountId = string.Empty;
    private string _trophyId = string.Empty;
    private int _allGetTrophyCount;
    private int _allSyncPsnTrophyCount;
    private TrnsInitTime? _initTime;
    private readonly List<TrnsRecord> _trophyInfos = new();
    private readonly Dictionary<int, TrnsRecord> _trophyIndex = new();

    // Raw file data for re-serialization
    private byte[] _rawFileData = Array.Empty<byte>();

    public string AccountId => _accountId;

    /// <summary>
    /// Patches the account ID in the raw file data and updates the in-memory value.
    /// </summary>
    public void SetAccountId(byte[] newId16)
    {
        var type2 = _typeRecords.FirstOrDefault(t => t.Id == 2);
        if (type2 != null)
        {
            int offset = (int)type2.Offset + BlockHeaderSize + 16;
            newId16.AsSpan(0, 16).CopyTo(_rawFileData.AsSpan(offset, 16));
            _accountId = System.Text.Encoding.UTF8.GetString(newId16).TrimEnd('\0');
        }
    }

    public string TrophyId => _trophyId;
    public int AllGetTrophyCount => _allGetTrophyCount;
    public IReadOnlyList<TrnsRecord> TrophyInfos => _trophyInfos;

    public TrnsRecord? this[int trophyId] =>
        _trophyIndex.TryGetValue(trophyId, out var record) ? record : null;

    public DateTime LastSyncTime
    {
        get
        {
            var max = DateTime.MinValue;
            foreach (var t in _trophyInfos)
                if (t.IsSynced && t.GetTime > max) max = t.GetTime;
            return max;
        }
    }

    public DateTime LastTrophyTime
    {
        get
        {
            var max = DateTime.MinValue;
            foreach (var t in _trophyInfos)
                if (t.GetTime > max) max = t.GetTime;
            return max;
        }
    }

    public TropTrnsParser(string directoryPath, bool isRpcs3Format)
    {
        _filePath = Path.Combine(directoryPath, FileName);
        _isRpcs3 = isRpcs3Format;

        if (!File.Exists(_filePath))
            throw new FileNotFoundException($"Trophy transaction file not found: {_filePath}");

        if (_isRpcs3)
            return; // RPCS3 doesn't use TROPTRNS.DAT

        Parse();
    }

    private void Parse()
    {
        _rawFileData = File.ReadAllBytes(_filePath);
        ReadOnlySpan<byte> data = _rawFileData.AsSpan();

        _header = TrophyFileHeader.ReadFrom(data, _filePath);

        // Read type records
        _typeRecords.Clear();
        int offset = TrophyFileHeader.Size;
        for (int i = 0; i < _header.TypeRecordCount; i++)
        {
            _typeRecords.Add(TypeRecord.ReadFrom(data.Slice(offset)));
            offset += TypeRecord.Size;
        }

        // Find and parse blocks by type
        var type2 = _typeRecords.FirstOrDefault(t => t.Id == 2);
        var type3 = _typeRecords.FirstOrDefault(t => t.Id == 3);
        var type4 = _typeRecords.FirstOrDefault(t => t.Id == 4);

        // Type 2: Account ID
        if (type2 != null)
        {
            int blockOffset = (int)type2.Offset + BlockHeaderSize;
            _accountId = data.Slice(blockOffset + 16, 16).ReadUtf8String(0, 16);
        }

        // Type 3: Trophy ID and counts
        if (type3 != null)
        {
            int blockOffset = (int)type3.Offset + BlockHeaderSize;
            _trophyId = data.Slice(blockOffset, 16).ReadUtf8String(0, 16);

            _allGetTrophyCount = BinaryPrimitives.ReadInt32BigEndian(data.Slice(blockOffset + 20));
            _allSyncPsnTrophyCount = BinaryPrimitives.ReadInt32BigEndian(data.Slice(blockOffset + 24));
        }

        // Type 4: Trophy infos
        if (type4 != null && _allGetTrophyCount > 0)
        {
            int blockOffset = (int)type4.Offset + BlockHeaderSize;

            // First entry is init time
            _initTime = TrnsInitTime.ReadFrom(data.Slice(blockOffset));
            blockOffset += TrnsInitTime.Size + BlockHeaderSize; // next block header

            // Remaining entries are trophy infos
            for (int i = 1; i < _allGetTrophyCount; i++)
            {
                var record = TrnsRecord.ReadFrom(data.Slice(blockOffset));
                _trophyInfos.Add(record);
                blockOffset += TrnsRecord.Size + BlockHeaderSize;
            }
        }

        RebuildIndex();
    }

    private void RebuildIndex()
    {
        _trophyIndex.Clear();
        foreach (var r in _trophyInfos)
            _trophyIndex[r.TrophyId] = r;
    }

    /// <summary>
    /// Add a trophy to the transaction log, maintaining chronological order.
    /// </summary>
    public void PutTrophy(int id, TrophyType type, DateTime dateTime)
    {
        if (_isRpcs3) return;

        // Check if already exists
        if (_trophyIndex.ContainsKey(id))
            throw new TrophyAlreadyEarnedException(id);

        // Cannot insert before a synced trophy's time
        var lastSyncedTime = LastSyncTime;

        if (dateTime < lastSyncedTime)
            throw new TrophySyncTimeException(
                $"Cannot add trophy at {dateTime} before last synced time {lastSyncedTime}");

        var record = TrnsRecord.Create(id, type, dateTime, _allGetTrophyCount);
        _allGetTrophyCount++;

        // Insert in chronological order
        int insertIndex = _trophyInfos.FindIndex(t => t.GetTime > dateTime);
        if (insertIndex < 0)
            _trophyInfos.Add(record);
        else
            _trophyInfos.Insert(insertIndex, record);

        // Reassign sequence numbers
        for (int i = 0; i < _trophyInfos.Count; i++)
            _trophyInfos[i].SequenceNumber = i + 1;

        RebuildIndex();
    }

    /// <summary>
    /// Change the timestamp of a trophy by ID.
    /// </summary>
    public void ChangeTime(int trophyId, DateTime newTime)
    {
        if (_isRpcs3) return;

        var record = this[trophyId];
        if (record == null)
            throw new TrophyNotFoundException(trophyId);
        if (record.IsSynced)
            throw new TrophyAlreadySyncException(trophyId);

        record.GetTime = newTime;

        // Re-sort chronologically
        _trophyInfos.Sort((a, b) => a.GetTime.CompareTo(b.GetTime));
        for (int i = 0; i < _trophyInfos.Count; i++)
            _trophyInfos[i].SequenceNumber = i + 1;
    }

    /// <summary>
    /// Delete a trophy by ID from the transaction log.
    /// </summary>
    public void DeleteTrophyById(int trophyId)
    {
        if (_isRpcs3) return;

        int index = _trophyInfos.FindIndex(t => t.TrophyId == trophyId);
        if (index < 0) return;

        if (_trophyInfos[index].IsSynced)
            throw new TrophyAlreadySyncException(trophyId);

        _trophyInfos.RemoveAt(index);
        _allGetTrophyCount--;

        for (int i = 0; i < _trophyInfos.Count; i++)
            _trophyInfos[i].SequenceNumber = i + 1;

        RebuildIndex();
    }

    /// <summary>
    /// Save changes back to the file.
    /// WriteTo is a pure RawData copy — property setters already patched RawData.
    /// </summary>
    public void Save()
    {
        if (_isRpcs3) return;

        var type3 = _typeRecords.FirstOrDefault(t => t.Id == 3);
        var type4 = _typeRecords.FirstOrDefault(t => t.Id == 4);

        // Resize type-4 block if the record count changed since parse.
        // Without this the new records overflow the original block, corrupting
        // downstream bytes and causing PSN to reject the file or silently drop
        // the added trophies.
        if (type4 != null)
        {
            int expectedDataSize = BlockHeaderSize + TrnsInitTime.Size
                                 + _trophyInfos.Count * (BlockHeaderSize + TrnsRecord.Size);
            if (expectedDataSize != (int)type4.DataSize)
                ResizeType4Block(type4, expectedDataSize);
        }

        var data = _rawFileData.AsSpan();

        // Patch type 3 block: update trophy counts
        if (type3 != null)
        {
            int countOffset = (int)type3.Offset + BlockHeaderSize + 20;
            BinaryPrimitives.WriteInt32BigEndian(data.Slice(countOffset), _allGetTrophyCount);
            BinaryPrimitives.WriteInt32BigEndian(data.Slice(countOffset + 4), _allSyncPsnTrophyCount);
        }

        // Patch type 4 blocks: init time + trophy records
        // blockOffset tracks BLOCK HEADER positions (type/size/seq/padding)
        if (type4 != null)
        {
            int blockOffset = (int)type4.Offset; // init time block HEADER

            // Patch init time data (at header + 16)
            if (_initTime != null)
                _initTime.WriteTo(data.Slice(blockOffset + BlockHeaderSize, TrnsInitTime.Size));

            blockOffset += BlockHeaderSize + TrnsInitTime.Size; // next block HEADER

            // Patch trophy info blocks
            for (int i = 0; i < _trophyInfos.Count; i++)
            {
                // Patch block header sequence number (at header + 8)
                BinaryPrimitives.WriteInt32BigEndian(data.Slice(blockOffset + 8), _trophyInfos[i].SequenceNumber);

                // Patch record data (at header + 16)
                _trophyInfos[i].WriteTo(data.Slice(blockOffset + BlockHeaderSize, TrnsRecord.Size));

                blockOffset += BlockHeaderSize + TrnsRecord.Size;
            }
        }

        File.WriteAllBytes(_filePath, _rawFileData);
    }

    /// <summary>
    /// Resize the type-4 data block to fit the current record count.
    /// Allocates a new raw buffer, shifts trailing bytes, updates the type-4
    /// DataSize field and any following type-record offsets, and uses the first
    /// existing record's block header as the template for newly-appended slots.
    /// </summary>
    private void ResizeType4Block(TypeRecord type4, int newDataSize)
    {
        int oldDataSize = (int)type4.DataSize;
        int delta = newDataSize - oldDataSize;
        int blockStart = (int)type4.Offset;
        int blockEndOld = blockStart + oldDataSize;
        int trailingLen = _rawFileData.Length - blockEndOld;

        // Capture a template block header for new record slots.
        // Use the first existing record's header if present; otherwise reuse the
        // init-time block header. Both live inside the type-4 region.
        byte[] templateHeader = new byte[BlockHeaderSize];
        int firstRecordHeaderOffset = blockStart + BlockHeaderSize + TrnsInitTime.Size;
        int fallbackHeaderOffset = blockStart; // init time block header
        int sourceHeader = oldDataSize >= BlockHeaderSize + TrnsInitTime.Size + BlockHeaderSize
            ? firstRecordHeaderOffset : fallbackHeaderOffset;
        _rawFileData.AsSpan(sourceHeader, BlockHeaderSize).CopyTo(templateHeader);

        // Build the new buffer.
        var newBuffer = new byte[_rawFileData.Length + delta];

        // 1. Copy bytes before the type-4 block as-is (file header + type record table + any earlier blocks).
        _rawFileData.AsSpan(0, blockStart).CopyTo(newBuffer);

        // 2. Copy the overlapping portion of the original type-4 block (preserves existing block headers
        //    and record bytes that the upcoming Save loop will repatch).
        int copyLen = Math.Min(oldDataSize, newDataSize);
        _rawFileData.AsSpan(blockStart, copyLen).CopyTo(newBuffer.AsSpan(blockStart));

        // 3. For grown blocks: stamp the template header into each newly-appended record slot.
        //    Record data bytes stay zero — the Save loop will write them via WriteTo.
        if (delta > 0)
        {
            int oldRecordCount = (oldDataSize - BlockHeaderSize - TrnsInitTime.Size)
                               / (BlockHeaderSize + TrnsRecord.Size);
            int slotStride = BlockHeaderSize + TrnsRecord.Size;
            int slotBase = blockStart + BlockHeaderSize + TrnsInitTime.Size;
            for (int i = oldRecordCount; i < _trophyInfos.Count; i++)
            {
                int slotOffset = slotBase + i * slotStride;
                templateHeader.CopyTo(newBuffer.AsSpan(slotOffset, BlockHeaderSize));
            }
        }

        // 4. Copy the trailing bytes (anything after the original type-4 block) to the shifted position.
        if (trailingLen > 0)
            _rawFileData.AsSpan(blockEndOld, trailingLen).CopyTo(newBuffer.AsSpan(blockStart + newDataSize));

        // 5. Update the type-4 DataSize in memory and in the type-record table bytes.
        type4.DataSize = newDataSize;
        int type4Index = _typeRecords.IndexOf(type4);
        int type4TableOffset = TrophyFileHeader.Size + type4Index * TypeRecord.Size;
        type4.WriteTo(newBuffer.AsSpan(type4TableOffset, TypeRecord.Size));

        // 6. Shift any type records whose Offset is after the type-4 block.
        foreach (var tr in _typeRecords)
        {
            if (tr == type4 || tr.Offset <= type4.Offset) continue;
            tr.Offset += delta;
            int idx = _typeRecords.IndexOf(tr);
            int trTableOffset = TrophyFileHeader.Size + idx * TypeRecord.Size;
            tr.WriteTo(newBuffer.AsSpan(trTableOffset, TypeRecord.Size));
        }

        _rawFileData = newBuffer;
    }
}
