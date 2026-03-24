using System.Buffers.Binary;
using Trophic.TrophyFormat.Enums;

namespace Trophic.TrophyFormat.Models;

/// <summary>
/// TROPUSR.DAT Type 4 block: trophy type definition (80 bytes per trophy).
/// </summary>
public sealed class UsrTrophyType
{
    public const int Size = 80;

    public int SequenceNumber { get; set; }
    public TrophyType Type { get; set; }
    public byte[] RawData { get; set; } = new byte[Size];

    public static UsrTrophyType ReadFrom(ReadOnlySpan<byte> data)
    {
        return new UsrTrophyType
        {
            RawData = data.Slice(0, Size).ToArray(),
            SequenceNumber = BinaryPrimitives.ReadInt32BigEndian(data),
            Type = (TrophyType)BinaryPrimitives.ReadInt32BigEndian(data.Slice(4))
        };
    }

    public void WriteTo(Span<byte> dest)
    {
        RawData.AsSpan().CopyTo(dest);
        BinaryPrimitives.WriteInt32BigEndian(dest, SequenceNumber);
        BinaryPrimitives.WriteInt32BigEndian(dest.Slice(4), (int)Type);
    }
}
