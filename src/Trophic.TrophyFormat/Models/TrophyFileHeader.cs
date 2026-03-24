using System.Buffers.Binary;
using Trophic.TrophyFormat.Exceptions;

namespace Trophic.TrophyFormat.Models;

/// <summary>
/// Shared 48-byte header for TROPTRNS.DAT and TROPUSR.DAT.
/// Magic (8 bytes) + TypeRecordCount (4 bytes) + Padding (36 bytes)
/// </summary>
public sealed class TrophyFileHeader
{
    public const int Size = 48;
    public const ulong ExpectedMagic = 0x818F54AD00010000;

    public ulong Magic { get; set; }
    public int TypeRecordCount { get; set; }

    public static TrophyFileHeader ReadFrom(ReadOnlySpan<byte> data, string? sourceFile = null)
    {
        if (data.Length < Size)
            throw new InvalidTrophyFileException($"Header too short: {data.Length} bytes, expected {Size}");

        var header = new TrophyFileHeader
        {
            Magic = BinaryPrimitives.ReadUInt64BigEndian(data),
            TypeRecordCount = BinaryPrimitives.ReadInt32BigEndian(data.Slice(8))
        };

        if (header.Magic != ExpectedMagic)
        {
            var hexDump = Convert.ToHexString(data.Slice(0, Math.Min(16, data.Length)));
            var file = sourceFile != null ? $" in {Path.GetFileName(sourceFile)}" : "";
            throw new InvalidTrophyFileException(
                $"Invalid trophy file magic{file}: 0x{header.Magic:X16}, expected 0x{ExpectedMagic:X16}. First 16 bytes: {hexDump}");
        }

        return header;
    }

    public void WriteTo(Span<byte> dest)
    {
        if (dest.Length < Size)
            throw new ArgumentException($"Destination too short: {dest.Length}, need {Size}");

        dest.Slice(0, Size).Clear();
        BinaryPrimitives.WriteUInt64BigEndian(dest, Magic);
        BinaryPrimitives.WriteInt32BigEndian(dest.Slice(8), TypeRecordCount);
    }
}
