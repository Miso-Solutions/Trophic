namespace Trophic.TrophyFormat.Crypto;

/// <summary>
/// Per-file HMAC hash key derivation for PS3 PFD files in both trophy and savegame modes.
/// Ported from bucanero/pfd_sfo_tools (pfd.c, pfd_generate_hash_key_for_* and pfd_get_entry_hash_key).
/// </summary>
public static class PfdSaveCrypto
{
    public const int HashKeySize = 20;
    public const int SecureFileIdSize = 16;
    public const int ConsoleIdSize = 16;
    public const int UserIdSize = 8;
    public const int AuthenticationIdSize = 8;

    public const int HashFile = 0;
    public const int HashFileCid = 1;
    public const int HashFileDhkCid2 = 2;
    public const int HashFileAidUid = 3;

    /// <summary>
    /// Derives the 20-byte HMAC key used for hashing/encrypting a generic save file
    /// (one not in the well-known trophy/SFO list). The save's PARAM.SFO embeds a
    /// 16-byte secure_file_id; this function mixes it with constant bytes.
    /// </summary>
    public static byte[] GenerateHashKeyForSecureFileId(ReadOnlySpan<byte> secureFileId)
    {
        if (secureFileId.Length < SecureFileIdSize)
            throw new ArgumentException($"Secure file ID requires {SecureFileIdSize} bytes", nameof(secureFileId));

        var hashKey = new byte[HashKeySize];
        int j = 0;
        for (int i = 0; i < HashKeySize; i++)
        {
            switch (i)
            {
                case 1: hashKey[i] = 11; break;
                case 2: hashKey[i] = 15; break;
                case 5: hashKey[i] = 14; break;
                case 8: hashKey[i] = 10; break;
                default:
                    hashKey[i] = secureFileId[j++];
                    break;
            }
        }
        return hashKey;
    }

    /// <summary>
    /// Derives the 20-byte HMAC key for PARAM.SFO in savegame (non-trophy) mode.
    /// </summary>
    public static byte[] GenerateSavegameParamSfoHashKey(int hashIndex, ReadOnlySpan<byte> consoleId,
        ReadOnlySpan<byte> discHashKey, ReadOnlySpan<byte> authenticationId)
    {
        var hashKey = new byte[HashKeySize];
        switch (hashIndex)
        {
            case HashFile:
                Ps3SaveKeys.SavegameParamSfoKey.AsSpan(0, HashKeySize).CopyTo(hashKey);
                break;
            case HashFileCid:
                consoleId.Slice(0, ConsoleIdSize).CopyTo(hashKey);
                break;
            case HashFileDhkCid2:
                discHashKey.Slice(0, 16).CopyTo(hashKey);
                break;
            case HashFileAidUid:
                authenticationId.Slice(0, AuthenticationIdSize).CopyTo(hashKey);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(hashIndex));
        }
        return hashKey;
    }

    /// <summary>
    /// Derives the 20-byte HMAC key for PARAM.SFO in trophy mode.
    /// Hashes 2 and 3 mix console_id and user_id with constant bytes at fixed positions.
    /// </summary>
    public static byte[] GenerateTrophyParamSfoHashKey(int hashIndex, ReadOnlySpan<byte> consoleId, ReadOnlySpan<byte> userId)
    {
        var hashKey = new byte[HashKeySize];
        switch (hashIndex)
        {
            case HashFile:
                Ps3TrophyKeys.TrophyParamSfoKey.AsSpan(0, HashKeySize).CopyTo(hashKey);
                break;

            case HashFileDhkCid2:
            {
                int j = 0;
                for (int i = 0; i < HashKeySize; i++)
                {
                    switch (i)
                    {
                        case 4: hashKey[i] = 11; break;
                        case 8: hashKey[i] = 10; break;
                        case 9: hashKey[i] = 14; break;
                        case 10: hashKey[i] = 15; break;
                        default:
                            hashKey[i] = consoleId[j++];
                            break;
                    }
                }
                break;
            }

            case HashFileAidUid:
            {
                int j = 0;
                for (int i = 0; i < HashKeySize; i++)
                {
                    switch (i)
                    {
                        case 3: hashKey[i] = 11; break;
                        case 7: hashKey[i] = 14; break;
                        default:
                            hashKey[i] = userId[j++ % UserIdSize];
                            break;
                    }
                }
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(hashIndex), "Trophy mode supports hash indices 0, 2, 3 only.");
        }
        return hashKey;
    }

    /// <summary>
    /// Returns the per-file HMAC key for an entry given its filename and the requested hash index.
    /// Returns null when the index is not applicable for that file (caller skips it).
    /// </summary>
    public static byte[]? GetEntryHashKey(string fileName, bool isTrophyMode, int hashIndex,
        ReadOnlySpan<byte> consoleId, ReadOnlySpan<byte> userId, ReadOnlySpan<byte> authenticationId,
        ReadOnlySpan<byte> discHashKey, Func<string, byte[]?>? secureFileIdLookup)
    {
        var upper = fileName.ToUpperInvariant();

        if (upper == "PARAM.SFO")
        {
            if (isTrophyMode)
            {
                if (hashIndex is HashFile or HashFileDhkCid2 or HashFileAidUid)
                    return GenerateTrophyParamSfoHashKey(hashIndex, consoleId, userId);
                return null;
            }
            return GenerateSavegameParamSfoHashKey(hashIndex, consoleId, discHashKey, authenticationId);
        }

        // Trophy-system file names use fixed per-file keys (only hash 0).
        var fixedKey = Ps3TrophyKeys.GetFileKey(upper);
        if (fixedKey != null)
            return hashIndex == HashFile ? fixedKey : null;

        // Anything else: must look up the file's secure_file_id from PARAM.SFO.
        if (secureFileIdLookup == null) return null;
        var secureId = secureFileIdLookup(fileName);
        if (secureId == null || secureId.Length < SecureFileIdSize) return null;
        return GenerateHashKeyForSecureFileId(secureId);
    }
}
