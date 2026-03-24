using System.Buffers.Binary;
using Trophic.TrophyFormat.Timestamps;

namespace Trophic.TrophyFormat.Models;

/// <summary>
/// TROPUSR.DAT Type 5 block: trophy list aggregate info (208 bytes).
/// All property setters patch RawData so WriteTo is a pure copy.
/// </summary>
public sealed class UsrTrophyListInfo
{
    public const int Size = 208;

    public byte[] RawData { get; set; } = new byte[Size];

    public DateTime ListCreateTime { get; set; }
    public DateTime ListLastUpdateTime { get; set; }

    private DateTime _listLastGetTrophyTime;
    public DateTime ListLastGetTrophyTime
    {
        get => _listLastGetTrophyTime;
        set
        {
            _listLastGetTrophyTime = value;
            Ps3Timestamp.ToBytes16(value, RawData.AsSpan(0x40));
        }
    }

    private int _getTrophyNumber;
    public int GetTrophyNumber
    {
        get => _getTrophyNumber;
        set
        {
            _getTrophyNumber = value;
            BinaryPrimitives.WriteInt32BigEndian(RawData.AsSpan(0x70), value);
        }
    }

    /// <summary>
    /// 128-bit achievement bitfield as 4 x uint32.
    /// Bit (1 &lt;&lt; (id % 32)) in AchievementRate[id / 32] indicates trophy 'id' is unlocked.
    /// Stored in big-endian byte order in the file.
    /// </summary>
    public uint[] AchievementRate { get; set; } = new uint[4];

    public byte[] Hash { get; set; } = new byte[16];

    public bool IsTrophyUnlocked(int trophyId)
    {
        int arrayIndex = trophyId / 32;
        int bitIndex = trophyId % 32;
        if (arrayIndex >= 4) return false;

        uint mask = (uint)(1 << bitIndex);
        return (AchievementRate[arrayIndex] & mask) != 0;
    }

    public void SetTrophyUnlocked(int trophyId, bool unlocked)
    {
        int arrayIndex = trophyId / 32;
        int bitIndex = trophyId % 32;
        if (arrayIndex >= 4) return;

        uint mask = (uint)(1 << bitIndex);
        if (unlocked)
            AchievementRate[arrayIndex] |= mask;
        else
            AchievementRate[arrayIndex] &= ~mask;

        // Patch RawData for this uint32
        BinaryPrimitives.WriteUInt32BigEndian(RawData.AsSpan(0x80 + arrayIndex * 4), AchievementRate[arrayIndex]);
    }

    public static UsrTrophyListInfo ReadFrom(ReadOnlySpan<byte> data)
    {
        var info = new UsrTrophyListInfo
        {
            RawData = data.Slice(0, Size).ToArray(),
            ListCreateTime = Ps3Timestamp.FromBytes16(data.Slice(0x10)),
            ListLastUpdateTime = Ps3Timestamp.FromBytes16(data.Slice(0x20)),
            ListLastGetTrophyTime = Ps3Timestamp.FromBytes16(data.Slice(0x40)),
            GetTrophyNumber = BinaryPrimitives.ReadInt32BigEndian(data.Slice(0x70))
        };

        // Read achievement rate (big-endian uint32 x 4 at offset 0x80)
        for (int i = 0; i < 4; i++)
        {
            info.AchievementRate[i] = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(0x80 + i * 4));
        }

        data.Slice(0xA0, 16).CopyTo(info.Hash);

        return info;
    }

    /// <summary>
    /// Pure RawData copy — all mutations already patch RawData via property setters.
    /// </summary>
    public void WriteTo(Span<byte> dest)
    {
        RawData.AsSpan().CopyTo(dest);
    }
}
