using System.Buffers.Binary;

namespace Trophic.TrophyFormat.Models;

/// <summary>
/// 32-byte type record in the type record table following the file header.
/// </summary>
public sealed class TypeRecord
{
    public const int Size = 32;

    public int Id { get; set; }
    public int DataSize { get; set; }
    public int Unknown3 { get; set; }
    public int UsedTimes { get; set; }
    public long Offset { get; set; }
    public long Unknown6 { get; set; }

    public static TypeRecord ReadFrom(ReadOnlySpan<byte> data)
    {
        return new TypeRecord
        {
            Id = BinaryPrimitives.ReadInt32BigEndian(data),
            DataSize = BinaryPrimitives.ReadInt32BigEndian(data.Slice(4)),
            Unknown3 = BinaryPrimitives.ReadInt32BigEndian(data.Slice(8)),
            UsedTimes = BinaryPrimitives.ReadInt32BigEndian(data.Slice(12)),
            Offset = BinaryPrimitives.ReadInt64BigEndian(data.Slice(16)),
            Unknown6 = BinaryPrimitives.ReadInt64BigEndian(data.Slice(24))
        };
    }

    public void WriteTo(Span<byte> dest)
    {
        dest.Slice(0, Size).Clear();
        BinaryPrimitives.WriteInt32BigEndian(dest, Id);
        BinaryPrimitives.WriteInt32BigEndian(dest.Slice(4), DataSize);
        BinaryPrimitives.WriteInt32BigEndian(dest.Slice(8), Unknown3);
        BinaryPrimitives.WriteInt32BigEndian(dest.Slice(12), UsedTimes);
        BinaryPrimitives.WriteInt64BigEndian(dest.Slice(16), Offset);
        BinaryPrimitives.WriteInt64BigEndian(dest.Slice(24), Unknown6);
    }
}
