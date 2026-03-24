using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Trophic.TrophyFormat.Crypto;

/// <summary>
/// Native C# implementation of PS3 PFD (Protected File Data) operations.
/// Replaces the external pfdtool.exe for trophy file encryption, decryption, and hash updates.
/// </summary>
public static class PfdFileHandler
{
    /// <summary>
    /// Parses a PARAM.PFD file, decrypting the signature block and deriving the real hash key.
    /// </summary>
    public static PfdFile Parse(byte[] rawData)
    {
        // Caller passes data read from File.ReadAllBytes — safe to mutate directly
        byte[] data = rawData;

        // Decrypt signature block: AES-128-CBC(SysconManagerKey, IV=headerKey, data=signature)
        byte[] headerKey = new byte[16];
        Array.Copy(data, PfdConstants.HeaderKeyOffset, headerKey, 0, 16);

        byte[] sigBlock = new byte[PfdConstants.SignatureSize];
        Array.Copy(data, PfdConstants.SignatureOffset, sigBlock, 0, PfdConstants.SignatureSize);
        sigBlock = DecryptAesCbc(Ps3TrophyKeys.SysconManagerKey, headerKey, sigBlock);
        Array.Copy(sigBlock, 0, data, PfdConstants.SignatureOffset, PfdConstants.SignatureSize);

        var pfd = new PfdFile(data);

        // Extract signature fields from decrypted block
        Array.Copy(data, PfdConstants.SignatureOffset, pfd.BottomHash, 0, 20);
        Array.Copy(data, PfdConstants.SignatureOffset + 20, pfd.TopHash, 0, 20);
        Array.Copy(data, PfdConstants.SignatureOffset + 40, pfd.HashKey, 0, 20);

        // Derive real hash key
        if (pfd.Version == PfdConstants.VersionV4)
        {
            using var hmac = new HMACSHA1(Ps3TrophyKeys.KeygenKey);
            pfd.RealHashKey = hmac.ComputeHash(pfd.HashKey);
        }
        else
        {
            Array.Copy(pfd.HashKey, pfd.RealHashKey, 20);
        }

        // Detect trophy folder
        for (int i = 0; i < (int)pfd.NumUsed; i++)
        {
            var name = pfd.Entries[i].FileName.ToUpperInvariant();
            if (name is "TROPSYS.DAT" or "TROPCONF.SFM" or "TROPUSR.DAT" or "TROPTRNS.DAT")
            {
                pfd.IsTrophy = true;
                break;
            }
        }

        return pfd;
    }

    /// <summary>
    /// Serializes the PFD back to 32,768 bytes, re-encrypting the signature block.
    /// </summary>
    public static byte[] Serialize(PfdFile pfd)
    {
        pfd.WriteBack();
        byte[] output = (byte[])pfd.RawData.Clone();

        // Encrypt the signature block back
        byte[] headerKey = new byte[16];
        Array.Copy(output, PfdConstants.HeaderKeyOffset, headerKey, 0, 16);

        byte[] sigBlock = new byte[PfdConstants.SignatureSize];
        Array.Copy(output, PfdConstants.SignatureOffset, sigBlock, 0, PfdConstants.SignatureSize);
        sigBlock = EncryptAesCbc(Ps3TrophyKeys.SysconManagerKey, headerKey, sigBlock);
        Array.Copy(sigBlock, 0, output, PfdConstants.SignatureOffset, PfdConstants.SignatureSize);

        return output;
    }

    /// <summary>
    /// Decrypts a file (e.g., TROPTRNS.DAT) using the entry key from the PFD.
    /// </summary>
    public static void DecryptFile(PfdFile pfd, string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var found = pfd.FindEntry(fileName);
        if (found == null)
            throw new FileNotFoundException($"Entry '{fileName}' not found in PFD");

        var entry = found.Value.entry;
        byte[] entryKey = GetEntryKey(pfd, entry);

        ulong fileSize = entry.FileSize;
        ulong alignedSize = AlignTo16(fileSize);

        byte[] encrypted = File.ReadAllBytes(filePath);
        if ((ulong)encrypted.Length < alignedSize)
        {
            // Pad to aligned size
            Array.Resize(ref encrypted, (int)alignedSize);
        }

        DecryptData(entryKey, encrypted, (int)alignedSize);

        // Write only the actual file size (trimming padding)
        using var fs = File.Create(filePath);
        fs.Write(encrypted, 0, (int)fileSize);
    }

    /// <summary>
    /// Encrypts a file (e.g., TROPTRNS.DAT) using the entry key from the PFD.
    /// Updates the entry's file_size field.
    /// </summary>
    public static void EncryptFile(PfdFile pfd, string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var found = pfd.FindEntry(fileName);
        if (found == null)
            throw new FileNotFoundException($"Entry '{fileName}' not found in PFD");

        var entry = found.Value.entry;
        byte[] entryKey = GetEntryKey(pfd, entry);

        byte[] plaintext = File.ReadAllBytes(filePath);
        ulong fileSize = (ulong)plaintext.Length;
        ulong alignedSize = AlignTo16(fileSize);

        // Pad to aligned size
        byte[] padded = new byte[alignedSize];
        Array.Copy(plaintext, padded, plaintext.Length);

        EncryptData(entryKey, padded, (int)alignedSize);

        // Update entry file size
        entry.FileSize = fileSize;

        File.WriteAllBytes(filePath, padded);
    }

    /// <summary>
    /// Recomputes all HMAC-SHA1 hashes in the PFD: per-file hashes, Y-table, bottom hash, top hash.
    /// </summary>
    public static void UpdateAllHashes(PfdFile pfd, string directoryPath)
    {
        // 1. Update per-file HMAC hashes for each entry
        for (int i = 0; i < (int)pfd.NumUsed; i++)
        {
            var entry = pfd.Entries[i];
            UpdateFileHashes(pfd, entry, directoryPath);
        }

        // 1b. Sync modified fields back to RawBytes so Y-table hashing reads updated data
        for (int i = 0; i < (int)pfd.NumReserved; i++)
        {
            pfd.Entries[i].WriteTo(pfd.Entries[i].RawBytes, 0);
        }

        // Use a single HMACSHA1 instance for all realKey-based operations
        using var realKeyHmac = new HMACSHA1(pfd.RealHashKey);

        // 2. Compute default hash for unused Y-table slots
        byte[] defaultHash = realKeyHmac.ComputeHash(Array.Empty<byte>());
        realKeyHmac.Initialize();

        for (int i = 0; i < (int)pfd.Capacity; i++)
        {
            if (pfd.HashTableEntries[i] >= pfd.Capacity)
                Array.Copy(defaultHash, pfd.EntrySignatures[i], 20);
        }

        // 3. Compute Y-table entry hashes (entry signature) for used entries
        for (int i = 0; i < (int)pfd.NumUsed; i++)
        {
            var entry = pfd.Entries[i];
            int htIndex = pfd.CalculateHashTableIndex(entry.FileName);

            byte[] entryHash = CalculateEntryHash(pfd, entry);
            Array.Copy(entryHash, pfd.EntrySignatures[htIndex], 20);
        }

        // 4. Compute bottom hash = HMAC-SHA1(realKey, all Y-table bytes)
        byte[] yTableBytes = new byte[(int)pfd.Capacity * PfdConstants.HashSize];
        for (int i = 0; i < (int)pfd.Capacity; i++)
            Array.Copy(pfd.EntrySignatures[i], 0, yTableBytes, i * PfdConstants.HashSize, PfdConstants.HashSize);

        pfd.BottomHash = realKeyHmac.ComputeHash(yTableBytes);
        realKeyHmac.Initialize();

        // 5. Compute top hash = HMAC-SHA1(realKey, hash_table_header + x_table)
        int hashTableSize = PfdConstants.HashTableHeaderSize + (int)pfd.Capacity * PfdConstants.EntryIndexSize;
        byte[] hashTableBytes = new byte[hashTableSize];
        Array.Copy(pfd.RawData, PfdConstants.HashTableOffset, hashTableBytes, 0, hashTableSize);

        pfd.TopHash = realKeyHmac.ComputeHash(hashTableBytes);
    }

    #region Per-file hash computation

    /// <summary>
    /// Updates the 4 per-file HMAC-SHA1 hashes for a single entry.
    /// For trophy files: hash 0 (FILE), hash 2 (DHK_CID2), hash 3 (AID_UID). Hash 1 (CID) is skipped.
    /// For PARAM.SFO: all 4 hashes in trophy mode use specific keys.
    /// </summary>
    private static void UpdateFileHashes(PfdFile pfd, PfdEntry entry, string directoryPath)
    {
        string filePath = Path.Combine(directoryPath, entry.FileName);
        if (!File.Exists(filePath))
            return;

        byte[] fileData = File.ReadAllBytes(filePath);
        bool isParamSfo = entry.FileName.Equals("PARAM.SFO", StringComparison.OrdinalIgnoreCase);

        for (int hashIndex = 0; hashIndex < 4; hashIndex++)
        {
            // For non-PARAM.SFO files, only hash 0 (FILE) is computed
            if (!isParamSfo && hashIndex != 0)
                continue;

            // For trophy mode, hash 1 (FILE_CID) is skipped
            if (pfd.IsTrophy && hashIndex == 1)
                continue;

            byte[]? hashKey = GetEntryHashKey(pfd, entry, hashIndex);
            if (hashKey == null)
                continue;

            using var hmac = new HMACSHA1(hashKey);
            entry.FileHashes[hashIndex] = hmac.ComputeHash(fileData);
        }
    }

    /// <summary>
    /// Returns the HMAC key for a specific file and hash index.
    /// Hash indices: 0=FILE, 1=FILE_CID, 2=FILE_DHK_CID2, 3=FILE_AID_UID
    /// </summary>
    private static byte[]? GetEntryHashKey(PfdFile pfd, PfdEntry entry, int hashIndex)
    {
        if (entry.FileName.Equals("PARAM.SFO", StringComparison.OrdinalIgnoreCase))
            return GetParamSfoHashKey(pfd, hashIndex);

        // For all other files, only hash index 0 (FILE) is used
        if (hashIndex != 0)
            return null;

        return Ps3TrophyKeys.GetFileKey(entry.FileName);
    }

    /// <summary>
    /// Returns the HMAC key for PARAM.SFO at a given hash index.
    /// Trophy mode uses different keys than savegame mode.
    /// </summary>
    private static byte[]? GetParamSfoHashKey(PfdFile pfd, int hashIndex)
    {
        return hashIndex switch
        {
            0 => Ps3TrophyKeys.TrophyParamSfoKey,
            // hash 1 (CID) is skipped for trophies
            // hashes 2,3 require console-specific IDPS/UserId — skip to preserve originals
            _ => null
        };
    }

    #endregion

    #region Y-table (entry signature) computation

    /// <summary>
    /// Computes the Y-table hash for a given entry by walking its hash chain.
    /// HMAC-SHA1(realKey, concat(entry.filename + entry.data) for each entry in chain)
    /// </summary>
    private static byte[] CalculateEntryHash(PfdFile pfd, PfdEntry entry)
    {
        int htIndex = pfd.CalculateHashTableIndex(entry.FileName);
        ulong currentIdx = pfd.HashTableEntries[htIndex];

        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, pfd.RealHashKey);

        while (currentIdx < pfd.NumReserved)
        {
            var e = pfd.Entries[currentIdx];
            // Hash: filename (65 bytes) + entry data (192 bytes = key + hashes + padding + filesize)
            // This is bytes [8..8+65] and [80..80+192] of the raw entry, totalling 257 bytes
            hmac.AppendData(e.RawBytes.AsSpan(8, PfdConstants.EntryNameSize));
            hmac.AppendData(e.RawBytes.AsSpan(80, PfdConstants.EntryDataSize));
            currentIdx = e.AdditionalIndex;
        }

        return hmac.GetHashAndReset();
    }

    #endregion

    #region Entry key decryption

    /// <summary>
    /// Decrypts the per-file entry key.
    /// IV = the file's hash key (first 16 bytes of the per-file HMAC key).
    /// Key = SysconManagerKey.
    /// </summary>
    private static byte[] GetEntryKey(PfdFile pfd, PfdEntry entry)
    {
        byte[]? fileKey = GetEntryHashKey(pfd, entry, 0);
        if (fileKey == null)
            throw new InvalidOperationException($"No file key for entry: {entry.FileName}");

        // IV is the first 16 bytes of the file's hash key, padded to 16 if shorter
        byte[] iv = new byte[16];
        Array.Copy(fileKey, iv, Math.Min(fileKey.Length, 16));

        byte[] decryptedKey = DecryptAesCbc(Ps3TrophyKeys.SysconManagerKey, iv, entry.Key);
        return decryptedKey;
    }

    #endregion

    #region File data encryption/decryption (AES-ECB counter mode)

    /// <summary>
    /// Decrypts file data using AES-ECB counter mode.
    /// For each 16-byte block i:
    ///   1. counter = BE64(i) || 0x0000000000000000
    ///   2. counter_enc = AES-ECB-Encrypt(key, counter)
    ///   3. block = AES-ECB-Decrypt(key, block)
    ///   4. block = block XOR counter_enc
    /// </summary>
    private static void DecryptData(byte[] key, byte[] data, int size)
    {
        using var aes = Aes.Create();
        aes.Key = key[..16];
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        using var encryptor = aes.CreateEncryptor();
        using var decryptor = aes.CreateDecryptor();

        int numBlocks = size / 16;
        byte[] counter = new byte[16];
        byte[] counterEnc = new byte[16];
        byte[] blockBuf = new byte[16];

        for (int i = 0; i < numBlocks; i++)
        {
            int offset = i * 16;

            // Build counter: big-endian u64(i) + 8 zero bytes (PS3 native byte order)
            BinaryPrimitives.WriteUInt64BigEndian(counter, (ulong)i);
            Array.Clear(counter, 8, 8);

            // Encrypt counter
            encryptor.TransformBlock(counter, 0, 16, counterEnc, 0);

            // Decrypt block
            Array.Copy(data, offset, blockBuf, 0, 16);
            decryptor.TransformBlock(blockBuf, 0, 16, blockBuf, 0);

            // XOR with encrypted counter
            for (int j = 0; j < 16; j++)
                data[offset + j] = (byte)(blockBuf[j] ^ counterEnc[j]);
        }
    }

    /// <summary>
    /// Encrypts file data using AES-ECB counter mode (reverse of decrypt).
    /// For each 16-byte block i:
    ///   1. counter = BE64(i) || 0x0000000000000000
    ///   2. counter_enc = AES-ECB-Encrypt(key, counter)
    ///   3. block = block XOR counter_enc
    ///   4. block = AES-ECB-Encrypt(key, block)
    /// </summary>
    private static void EncryptData(byte[] key, byte[] data, int size)
    {
        using var aes = Aes.Create();
        aes.Key = key[..16];
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        using var encryptor1 = aes.CreateEncryptor(); // for counter
        using var encryptor2 = aes.CreateEncryptor(); // for block

        int numBlocks = size / 16;
        byte[] counter = new byte[16];
        byte[] counterEnc = new byte[16];
        byte[] blockBuf = new byte[16];

        for (int i = 0; i < numBlocks; i++)
        {
            int offset = i * 16;

            // Build counter: big-endian u64(i) + 8 zero bytes (PS3 native byte order)
            BinaryPrimitives.WriteUInt64BigEndian(counter, (ulong)i);
            Array.Clear(counter, 8, 8);

            // Encrypt counter
            encryptor1.TransformBlock(counter, 0, 16, counterEnc, 0);

            // XOR block with encrypted counter
            Array.Copy(data, offset, blockBuf, 0, 16);
            for (int j = 0; j < 16; j++)
                blockBuf[j] ^= counterEnc[j];

            // Encrypt block
            encryptor2.TransformBlock(blockBuf, 0, 16, blockBuf, 0);
            Array.Copy(blockBuf, 0, data, offset, 16);
        }
    }

    #endregion

    #region AES helpers

    private static byte[] DecryptAesCbc(byte[] key, byte[] iv, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var dec = aes.CreateDecryptor();
        return dec.TransformFinalBlock(data, 0, data.Length);
    }

    private static byte[] EncryptAesCbc(byte[] key, byte[] iv, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var enc = aes.CreateEncryptor();
        return enc.TransformFinalBlock(data, 0, data.Length);
    }

    private static ulong AlignTo16(ulong value)
    {
        return (value + 15) & ~15UL;
    }

    #endregion
}
