using System.Buffers.Binary;
using Trophic.TrophyFormat.Timestamps;

namespace Trophic.TrophyFormat.Models;

/// <summary>
/// TROPUSR.DAT Type 7 block (80 bytes): trophy counts and last sync time.
/// All property setters patch RawData so WriteTo is a pure copy.
/// </summary>
public sealed class UsrUnknownType7
{
    public const int Size = 80;

    public byte[] RawData { get; set; } = new byte[Size];

    private int _trophyCount;
    public int TrophyCount
    {
        get => _trophyCount;
        set
        {
            _trophyCount = value;
            BinaryPrimitives.WriteInt32BigEndian(RawData.AsSpan(), value);
        }
    }

    public int SyncTrophyCount { get; set; }
    public int Unknown3 { get; set; }
    public int Unknown4 { get; set; }
    public DateTime LastSyncTime { get; set; }

    public static UsrUnknownType7 ReadFrom(ReadOnlySpan<byte> data)
    {
        return new UsrUnknownType7
        {
            RawData = data.Slice(0, Size).ToArray(),
            TrophyCount = BinaryPrimitives.ReadInt32BigEndian(data),
            SyncTrophyCount = BinaryPrimitives.ReadInt32BigEndian(data.Slice(4)),
            Unknown3 = BinaryPrimitives.ReadInt32BigEndian(data.Slice(8)),
            Unknown4 = BinaryPrimitives.ReadInt32BigEndian(data.Slice(12)),
            LastSyncTime = Ps3Timestamp.FromBytes8(data.Slice(0x10))
        };
    }

    /// <summary>
    /// Pure RawData copy — all mutations already patch RawData via property setters.
    /// </summary>
    public void WriteTo(Span<byte> dest)
    {
        RawData.AsSpan().CopyTo(dest);
    }
}
