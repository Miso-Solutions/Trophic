using System.Buffers.Binary;
using System.Text;

namespace Trophic.TrophyFormat.Crypto;

/// <summary>
/// Raw PFD file layout constants and data structures.
/// PARAM.PFD is a 32,768-byte fixed-size protected file database.
/// Multi-byte integers are stored in big-endian format (PS3 native).
/// </summary>
public static class PfdConstants
{
    public const int FileSize = 32768;       // 0x8000
    public const ulong Magic = 0x0000000050464442; // 4 zero bytes + "PFDB" as BE u64
    public const ulong VersionV3 = 3;
    public const ulong VersionV4 = 4;

    // Section offsets
    public const int HeaderOffset = 0;       // 16 bytes: magic(8) + version(8)
    public const int HeaderKeyOffset = 16;   // 16 bytes: AES IV for signature decryption
    public const int SignatureOffset = 32;   // 64 bytes: encrypted signature block
    public const int HashTableOffset = 96;   // 24+ bytes: capacity(8) + reserved(8) + used(8) + entries[]

    // Sizes
    public const int HeaderSize = 16;
    public const int HeaderKeySize = 16;
    public const int SignatureSize = 64;
    public const int HashTableHeaderSize = 24;
    public const int EntryIndexSize = 8;
    public const int EntrySize = 272;
    public const int HashSize = 20;
    public const int EntryKeySize = 64;
    public const int EntryNameSize = 65;
    public const int EntryDataSize = 192;       // key(64) + hashes(80) + padding(40) + filesize(8)
    public const int FileAlignment = 16;
}

/// <summary>
/// Parsed PFD file with all sections accessible.
/// </summary>
public sealed class PfdFile
{
    public byte[] RawData { get; }

    // Header
    public ulong Version { get; }

    // Signature (decrypted)
    public byte[] BottomHash { get; set; } = new byte[20];
    public byte[] TopHash { get; set; } = new byte[20];
    public byte[] HashKey { get; set; } = new byte[20];

    // Hash table
    public ulong Capacity { get; }
    public ulong NumReserved { get; }
    public ulong NumUsed { get; }
    public ulong[] HashTableEntries { get; }

    // Entry table
    public PfdEntry[] Entries { get; }

    // Entry signature table (Y-table)
    public byte[][] EntrySignatures { get; }

    // Derived
    public byte[] RealHashKey { get; set; } = new byte[20];
    public bool IsTrophy { get; set; }

    // Computed offsets
    public int EntryTableOffset { get; }
    public int EntrySignatureTableOffset { get; }

    public PfdFile(byte[] data)
    {
        if (data.Length != PfdConstants.FileSize)
            throw new ArgumentException($"PFD file must be exactly {PfdConstants.FileSize} bytes, got {data.Length}");

        RawData = data;
        var span = data.AsSpan();

        // Parse header (big-endian)
        ulong magic = BinaryPrimitives.ReadUInt64BigEndian(span);
        Version = BinaryPrimitives.ReadUInt64BigEndian(span.Slice(8));

        if (magic != PfdConstants.Magic)
            throw new InvalidDataException($"Invalid PFD magic: 0x{magic:X16}, expected 0x{PfdConstants.Magic:X16}");
        if (Version != PfdConstants.VersionV3 && Version != PfdConstants.VersionV4)
            throw new InvalidDataException($"Unsupported PFD version: {Version}");

        // Parse hash table header (big-endian)
        int htOff = PfdConstants.HashTableOffset;
        Capacity = BinaryPrimitives.ReadUInt64BigEndian(span.Slice(htOff));
        NumReserved = BinaryPrimitives.ReadUInt64BigEndian(span.Slice(htOff + 8));
        NumUsed = BinaryPrimitives.ReadUInt64BigEndian(span.Slice(htOff + 16));

        // Parse X-table entries (big-endian)
        HashTableEntries = new ulong[Capacity];
        int xOff = htOff + PfdConstants.HashTableHeaderSize;
        for (int i = 0; i < (int)Capacity; i++)
        {
            HashTableEntries[i] = BinaryPrimitives.ReadUInt64BigEndian(span.Slice(xOff + i * 8));
        }

        // Entry table offset
        EntryTableOffset = xOff + (int)Capacity * PfdConstants.EntryIndexSize;

        // Parse entries
        Entries = new PfdEntry[NumReserved];
        for (int i = 0; i < (int)NumReserved; i++)
        {
            int off = EntryTableOffset + i * PfdConstants.EntrySize;
            Entries[i] = PfdEntry.Parse(data, off);
        }

        // Entry signature table (Y-table) offset
        EntrySignatureTableOffset = EntryTableOffset + (int)NumReserved * PfdConstants.EntrySize;

        // Parse Y-table (raw bytes, no endian conversion needed for hashes)
        EntrySignatures = new byte[Capacity][];
        for (int i = 0; i < (int)Capacity; i++)
        {
            EntrySignatures[i] = new byte[PfdConstants.HashSize];
            Array.Copy(data, EntrySignatureTableOffset + i * PfdConstants.HashSize, EntrySignatures[i], 0, PfdConstants.HashSize);
        }
    }

    /// <summary>
    /// Calculates the X-table index for a given filename.
    /// </summary>
    public int CalculateHashTableIndex(string fileName)
    {
        ulong hash = 0;
        foreach (char c in fileName)
            hash = (hash << 5) - hash + (byte)c;
        return (int)(hash % Capacity);
    }

    /// <summary>
    /// Finds a file entry by name, walking the hash chain.
    /// </summary>
    public (PfdEntry entry, int entryIndex)? FindEntry(string fileName)
    {
        int htIndex = CalculateHashTableIndex(fileName);
        ulong currentIdx = HashTableEntries[htIndex];

        while (currentIdx < NumReserved)
        {
            var entry = Entries[currentIdx];
            if (string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                return (entry, (int)currentIdx);
            currentIdx = entry.AdditionalIndex;
        }

        return null;
    }

    /// <summary>
    /// Writes the current state back to RawData for export.
    /// </summary>
    public void WriteBack()
    {
        // Write entries back to raw data
        for (int i = 0; i < (int)NumReserved; i++)
        {
            int off = EntryTableOffset + i * PfdConstants.EntrySize;
            Entries[i].WriteTo(RawData, off);
        }

        // Write Y-table back
        for (int i = 0; i < (int)Capacity; i++)
        {
            Array.Copy(EntrySignatures[i], 0, RawData, EntrySignatureTableOffset + i * PfdConstants.HashSize, PfdConstants.HashSize);
        }

        // Write signature back (will be encrypted before export)
        Array.Copy(BottomHash, 0, RawData, PfdConstants.SignatureOffset, 20);
        Array.Copy(TopHash, 0, RawData, PfdConstants.SignatureOffset + 20, 20);
        Array.Copy(HashKey, 0, RawData, PfdConstants.SignatureOffset + 40, 20);
    }
}

/// <summary>
/// A single protected file entry (272 bytes).
/// </summary>
public sealed class PfdEntry
{
    public ulong AdditionalIndex { get; set; }
    public string FileName { get; set; } = "";
    public byte[] Key { get; set; } = new byte[PfdConstants.EntryKeySize];
    public byte[][] FileHashes { get; set; } = new byte[4][];
    public ulong FileSize { get; set; }

    // Raw 272 bytes for exact reconstruction
    public byte[] RawBytes { get; set; } = new byte[PfdConstants.EntrySize];

    public static PfdEntry Parse(byte[] data, int offset)
    {
        var entry = new PfdEntry();
        Array.Copy(data, offset, entry.RawBytes, 0, PfdConstants.EntrySize);

        entry.AdditionalIndex = BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(offset));

        // Filename: 65 bytes at offset+8
        int nameEnd = Array.IndexOf(data, (byte)0, offset + 8, PfdConstants.EntryNameSize);
        int nameLen = nameEnd >= 0 ? nameEnd - (offset + 8) : PfdConstants.EntryNameSize;
        entry.FileName = Encoding.ASCII.GetString(data, offset + 8, nameLen);

        // Key: 64 bytes at offset+80 (0x50)
        entry.Key = new byte[PfdConstants.EntryKeySize];
        Array.Copy(data, offset + 80, entry.Key, 0, PfdConstants.EntryKeySize);

        // File hashes: 4 x 20 bytes at offset+144 (0x90)
        entry.FileHashes = new byte[4][];
        for (int i = 0; i < 4; i++)
        {
            entry.FileHashes[i] = new byte[PfdConstants.HashSize];
            Array.Copy(data, offset + 144 + i * PfdConstants.HashSize, entry.FileHashes[i], 0, PfdConstants.HashSize);
        }

        // File size: 8 bytes at offset+264 (0x108), big-endian
        entry.FileSize = BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(offset + 264));

        return entry;
    }

    /// <summary>
    /// Writes this entry back to a byte array at the given offset.
    /// </summary>
    public void WriteTo(byte[] data, int offset)
    {
        // Start from RawBytes to preserve padding
        Array.Copy(RawBytes, 0, data, offset, PfdConstants.EntrySize);

        // Overwrite mutable fields (big-endian)
        BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(offset), AdditionalIndex);

        // File hashes at offset+144
        for (int i = 0; i < 4; i++)
            Array.Copy(FileHashes[i], 0, data, offset + 144 + i * PfdConstants.HashSize, PfdConstants.HashSize);

        // File size at offset+264
        BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(offset + 264), FileSize);
    }
}
