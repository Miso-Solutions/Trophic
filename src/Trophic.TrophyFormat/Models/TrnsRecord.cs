using System.Buffers.Binary;
using Trophic.TrophyFormat.Enums;
using Trophic.TrophyFormat.Timestamps;

namespace Trophic.TrophyFormat.Models;

/// <summary>
/// A trophy record from TROPTRNS.DAT Type 4 block (160 bytes).
/// All property setters patch RawData so WriteTo is a pure copy.
/// </summary>
public sealed class TrnsRecord
{
    public const int Size = 160;

    public byte[] RawData { get; set; } = new byte[Size];

    private int _sequenceNumber;
    public int SequenceNumber
    {
        get => _sequenceNumber;
        set
        {
            _sequenceNumber = value;
            BinaryPrimitives.WriteInt32BigEndian(RawData.AsSpan(), value);
        }
    }

    public bool IsExist { get; set; }
    public bool IsSynced { get; set; }

    private int _trophyId;
    public int TrophyId
    {
        get => _trophyId;
        set
        {
            _trophyId = value;
            BinaryPrimitives.WriteInt32BigEndian(RawData.AsSpan(0x20), value);
        }
    }

    private TrophyType _trophyType;
    public TrophyType TrophyType
    {
        get => _trophyType;
        set
        {
            _trophyType = value;
            BinaryPrimitives.WriteInt32BigEndian(RawData.AsSpan(0x24), (int)value);
        }
    }

    private DateTime _getTime;
    public DateTime GetTime
    {
        get => _getTime;
        set
        {
            _getTime = value;
            Ps3Timestamp.ToBytes16(value, RawData.AsSpan(0x30));
        }
    }

    public static TrnsRecord ReadFrom(ReadOnlySpan<byte> data)
    {
        return new TrnsRecord
        {
            RawData = data.Slice(0, Size).ToArray(),
            SequenceNumber = BinaryPrimitives.ReadInt32BigEndian(data),
            IsExist = data[7] == 2,
            IsSynced = data[11] != 0,
            TrophyId = BinaryPrimitives.ReadInt32BigEndian(data.Slice(0x20)),
            TrophyType = (TrophyType)BinaryPrimitives.ReadInt32BigEndian(data.Slice(0x24)),
            GetTime = Ps3Timestamp.FromBytes16(data.Slice(0x30))
        };
    }

    /// <summary>
    /// Pure RawData copy — all mutations already patch RawData via property setters.
    /// </summary>
    public void WriteTo(Span<byte> dest)
    {
        RawData.AsSpan().CopyTo(dest);
    }

    public static TrnsRecord Create(int id, TrophyType type, DateTime dateTime, int sequenceNumber)
    {
        var record = new TrnsRecord
        {
            RawData = new byte[Size],
            SequenceNumber = sequenceNumber,
            IsExist = true,
            IsSynced = false,
            TrophyId = id,
            TrophyType = type,
            GetTime = dateTime
        };

        // Set flags in RawData for new records
        record.RawData[7] = 2;  // IsExist = true (byte value 2)
        // IsSynced stays 0 (not synced)

        // Set _unknowInt2 to 0x00100000 as per original code
        BinaryPrimitives.WriteInt32BigEndian(record.RawData.AsSpan(0x28), 0x00100000);

        return record;
    }
}
