using System.Buffers.Binary;
using System.Text;

namespace Trophic.Core.Services;

/// <summary>
/// Minimal PARAM.SFO reader for extracting well-known string keys (e.g. TITLE_ID).
/// Format reference: Sony PSF binary container.
///   off 0x00: magic 0x00 'P' 'S' 'F'
///   off 0x04: version
///   off 0x08: key table offset
///   off 0x0C: data table offset
///   off 0x10: number of entries
///   off 0x14..: entry index table (16 bytes each)
///       0x00 u16 key_name_offset (relative to key table)
///       0x02 u8  data_format
///       0x03 u8  reserved
///       0x04 u32 data_used_size
///       0x08 u32 data_max_size
///       0x0C u32 data_offset (relative to data table)
/// </summary>
public static class ParamSfoReader
{
    /// <summary>
    /// Reads a binary value for a key. Returns the raw bytes (length = data_used_size).
    /// </summary>
    public static byte[]? ReadBytes(string sfoPath, string keyName)
    {
        if (!File.Exists(sfoPath)) return null;
        var bytes = File.ReadAllBytes(sfoPath);
        if (!IsValid(bytes)) return null;

        int keyTableOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x08));
        int dataTableOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x0C));
        int entryCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x10));

        for (int i = 0; i < entryCount; i++)
        {
            int entryOff = 0x14 + i * 16;
            if (entryOff + 16 > bytes.Length) return null;
            ushort keyOff = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entryOff));
            uint dataUsed = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entryOff + 4));
            uint dataOff = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entryOff + 12));

            int keyAbs = keyTableOffset + keyOff;
            int keyEnd = keyAbs;
            while (keyEnd < bytes.Length && bytes[keyEnd] != 0) keyEnd++;
            var key = Encoding.UTF8.GetString(bytes, keyAbs, keyEnd - keyAbs);
            if (key != keyName) continue;

            int dataAbs = dataTableOffset + (int)dataOff;
            int dataLen = (int)dataUsed;
            if (dataAbs < 0 || dataLen <= 0 || dataAbs + dataLen > bytes.Length) return null;
            return bytes.AsSpan(dataAbs, dataLen).ToArray();
        }
        return null;
    }

    private static bool IsValid(byte[] bytes) =>
        bytes.Length >= 0x14 && bytes[0] == 0x00 && bytes[1] == (byte)'P' && bytes[2] == (byte)'S' && bytes[3] == (byte)'F';

    /// <summary>
    /// Locates a named SFO field within an already-loaded SFO byte buffer.
    /// Returns the absolute file offset of the field's data area and its allocated length,
    /// or (-1, 0) when the field is not present or the SFO header is malformed.
    /// </summary>
    public static (int DataOffset, int DataLength) FindFieldOffset(byte[] sfoBytes, string keyName)
    {
        if (!IsValid(sfoBytes)) return (-1, 0);

        int keyTableOffset = BinaryPrimitives.ReadInt32LittleEndian(sfoBytes.AsSpan(0x08));
        int dataTableOffset = BinaryPrimitives.ReadInt32LittleEndian(sfoBytes.AsSpan(0x0C));
        int entryCount = BinaryPrimitives.ReadInt32LittleEndian(sfoBytes.AsSpan(0x10));
        if (entryCount <= 0 || entryCount > 1024) return (-1, 0);

        for (int i = 0; i < entryCount; i++)
        {
            int entryOff = 0x14 + i * 16;
            if (entryOff + 16 > sfoBytes.Length) return (-1, 0);

            ushort keyOff = BinaryPrimitives.ReadUInt16LittleEndian(sfoBytes.AsSpan(entryOff));
            uint dataMax = BinaryPrimitives.ReadUInt32LittleEndian(sfoBytes.AsSpan(entryOff + 8));
            uint dataOff = BinaryPrimitives.ReadUInt32LittleEndian(sfoBytes.AsSpan(entryOff + 12));

            int keyAbs = keyTableOffset + keyOff;
            if (keyAbs >= sfoBytes.Length) continue;
            int keyEnd = keyAbs;
            while (keyEnd < sfoBytes.Length && sfoBytes[keyEnd] != 0) keyEnd++;
            var key = Encoding.UTF8.GetString(sfoBytes, keyAbs, keyEnd - keyAbs);
            if (key != keyName) continue;

            int dataAbs = dataTableOffset + (int)dataOff;
            int dataLen = (int)dataMax;
            if (dataAbs < 0 || dataLen <= 0 || dataAbs + dataLen > sfoBytes.Length) return (-1, 0);
            return (dataAbs, dataLen);
        }
        return (-1, 0);
    }

    public static string? ReadString(string sfoPath, string keyName)
    {
        if (!File.Exists(sfoPath)) return null;
        var bytes = File.ReadAllBytes(sfoPath);
        if (bytes.Length < 0x14) return null;
        if (!(bytes[0] == 0x00 && bytes[1] == (byte)'P' && bytes[2] == (byte)'S' && bytes[3] == (byte)'F'))
            return null;

        int keyTableOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x08));
        int dataTableOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x0C));
        int entryCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x10));
        if (entryCount <= 0 || entryCount > 1024) return null;

        for (int i = 0; i < entryCount; i++)
        {
            int entryOff = 0x14 + i * 16;
            if (entryOff + 16 > bytes.Length) return null;

            ushort keyOff = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entryOff));
            uint dataUsed = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entryOff + 4));
            uint dataOff = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entryOff + 12));

            int keyAbs = keyTableOffset + keyOff;
            if (keyAbs >= bytes.Length) continue;
            int keyEnd = keyAbs;
            while (keyEnd < bytes.Length && bytes[keyEnd] != 0) keyEnd++;
            var key = Encoding.UTF8.GetString(bytes, keyAbs, keyEnd - keyAbs);

            if (key != keyName) continue;

            int dataAbs = dataTableOffset + (int)dataOff;
            int dataLen = (int)dataUsed;
            if (dataAbs < 0 || dataLen <= 0 || dataAbs + dataLen > bytes.Length) return null;
            while (dataLen > 0 && bytes[dataAbs + dataLen - 1] == 0) dataLen--;
            return Encoding.UTF8.GetString(bytes, dataAbs, dataLen);
        }
        return null;
    }
}
