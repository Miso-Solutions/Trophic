using Trophic.TrophyFormat.Timestamps;

namespace Trophic.TrophyFormat.Models;

/// <summary>
/// The first Type 4 block in TROPTRNS.DAT: initialization time (160 bytes).
/// </summary>
public sealed class TrnsInitTime
{
    public const int Size = 160;

    public DateTime InitTime { get; set; }
    public byte[] RawData { get; set; } = new byte[Size];

    public static TrnsInitTime ReadFrom(ReadOnlySpan<byte> data)
    {
        return new TrnsInitTime
        {
            RawData = data.Slice(0, Size).ToArray(),
            InitTime = Ps3Timestamp.FromBytes16(data.Slice(0x20))
        };
    }

    /// <summary>
    /// Pure RawData copy — InitTime is never modified after parsing.
    /// </summary>
    public void WriteTo(Span<byte> dest)
    {
        RawData.AsSpan().CopyTo(dest);
    }
}
