using System.Buffers.Binary;
using Trophic.TrophyFormat.Enums;
using Trophic.TrophyFormat.Timestamps;

namespace Trophic.TrophyFormat.Models;

/// <summary>
/// TROPUSR.DAT Type 6 block: per-trophy time info (96 bytes).
/// All property setters patch RawData so WriteTo is a pure copy.
/// </summary>
public sealed class UsrTrophyTimeInfo
{
    public const int Size = 96;

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

    private bool _isEarned;
    public bool IsEarned
    {
        get => _isEarned;
        set
        {
            _isEarned = value;
            RawData[4] = 0; RawData[5] = 0; RawData[6] = 0;
            RawData[7] = value ? (byte)1 : (byte)0;
        }
    }

    private TrophySyncState _syncState;
    public TrophySyncState SyncState
    {
        get => _syncState;
        set
        {
            _syncState = value;
            BinaryPrimitives.WriteInt32BigEndian(RawData.AsSpan(8), (int)value);
        }
    }

    private DateTime _getTime;
    public DateTime GetTime
    {
        get => _getTime;
        set
        {
            _getTime = value;
            Ps3Timestamp.ToBytes16(value, RawData.AsSpan(0x10));
        }
    }

    public bool IsSynced => SyncState.HasFlag(TrophySyncState.Synced);

    public static UsrTrophyTimeInfo ReadFrom(ReadOnlySpan<byte> data)
    {
        return new UsrTrophyTimeInfo
        {
            RawData = data.Slice(0, Size).ToArray(),
            SequenceNumber = BinaryPrimitives.ReadInt32BigEndian(data),
            IsEarned = data[7] != 0,
            SyncState = (TrophySyncState)BinaryPrimitives.ReadInt32BigEndian(data.Slice(8)),
            GetTime = Ps3Timestamp.FromBytes16(data.Slice(0x10))
        };
    }

    /// <summary>
    /// Pure RawData copy — all mutations already patch RawData via property setters.
    /// </summary>
    public void WriteTo(Span<byte> dest)
    {
        RawData.AsSpan().CopyTo(dest);
    }

    public void Unlock(DateTime time)
    {
        IsEarned = true;
        SyncState = TrophySyncState.NotSynced;
        GetTime = time;
    }

    public void Lock()
    {
        IsEarned = false;
        SyncState = TrophySyncState.None;
        GetTime = DateTime.MinValue;
    }
}
