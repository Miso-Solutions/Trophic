using System.Text.Json;
using Trophic.Core.Models;

namespace Trophic.Core.Services;

/// <summary>
/// Persists the user's PS3 Console ID, User ID, and Authentication ID needed for
/// generic save resigning. Stored as hex strings in a dedicated JSON file alongside
/// the app so they can be deleted independently of other settings.
/// </summary>
public sealed class ConsoleKeysService
{
    private const string FileName = "console-keys.json";
    private readonly string _path;
    private ConsoleKeys _keys = new();

    public ConsoleKeysService()
    {
        _path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
        Load();
    }

    public ConsoleKeys GetKeys() => _keys;

    public void SetKeys(ConsoleKeys keys)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        Save();
    }

    public void Clear()
    {
        _keys = new ConsoleKeys();
        if (File.Exists(_path)) File.Delete(_path);
    }

    /// <summary>
    /// Extracts the 8-byte authentication ID from a PS3 act.dat file.
    /// Layout (verified against Apollo Save Tool's create_actdat in rifrap.c):
    ///   bytes 0..3:  version       (u32 BE, typically 0x00010001)
    ///   bytes 4..7:  licenseType   (u32 BE, typically 0x00000002)
    ///   bytes 8..15: accountId     (u64 BE) — this is the authentication ID
    /// </summary>
    public static byte[] ExtractAuthenticationIdFromActDat(string actDatPath)
    {
        var bytes = File.ReadAllBytes(actDatPath);
        if (bytes.Length < 16)
            throw new InvalidDataException("act.dat is too small to contain an authentication ID (need >= 16 bytes).");
        var result = new byte[8];
        Array.Copy(bytes, 8, result, 0, 8);
        return result;
    }

    /// <summary>
    /// Extracts Authentication ID and User ID from a save folder's PARAM.SFO.
    /// IDPS cannot be derived from saves. Returns the extracted values without
    /// mutating persisted state; the caller decides when (or whether) to call SetKeys.
    /// </summary>
    public SaveExtractionResult ExtractFromSaveFolder(string saveFolderPath)
    {
        try
        {
            var sfoPath = FindParamSfo(saveFolderPath);
            if (sfoPath == null)
                return SaveExtractionResult.Fail("PARAM.SFO not found inside the selected folder.");

            byte[]? authId = null;
            byte[]? userId = null;

            // ACCOUNT_ID is a 16-char ASCII hex string at the top level
            var accountIdAscii = ParamSfoReader.ReadBytes(sfoPath, "ACCOUNT_ID");
            if (accountIdAscii != null && accountIdAscii.Length >= 16)
            {
                var hexString = System.Text.Encoding.ASCII.GetString(accountIdAscii, 0, 16).Trim('\0', ' ');
                if (hexString.Length == 16 && AllHex(hexString))
                {
                    try { authId = Convert.FromHexString(hexString); } catch { /* ignored */ }
                }
            }

            // PARAMS binary struct: user_id at offset 0x18 (u32 little-endian on disk,
            // verified against PS3-created saves: bytes "01 00 00 00" = user_id 1).
            var paramsBytes = ParamSfoReader.ReadBytes(sfoPath, "PARAMS");
            if (paramsBytes != null && paramsBytes.Length >= 0x1C)
            {
                uint userIdInt = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(paramsBytes.AsSpan(0x18));
                if (userIdInt > 0 && userIdInt < 100_000_000)
                {
                    var ascii = userIdInt.ToString("D8");
                    userId = System.Text.Encoding.ASCII.GetBytes(ascii);
                }
            }

            if (authId == null && userId == null)
                return SaveExtractionResult.Fail("No Authentication ID or User ID found in the save.");

            return SaveExtractionResult.Ok(authId, userId);
        }
        catch (Exception ex)
        {
            return SaveExtractionResult.Fail(ex.Message);
        }
    }

    private static string? FindParamSfo(string folder)
    {
        if (File.Exists(Path.Combine(folder, "PARAM.SFO")))
            return Path.Combine(folder, "PARAM.SFO");
        foreach (var sub in Directory.EnumerateFiles(folder, "PARAM.SFO", SearchOption.AllDirectories))
            return sub;
        return null;
    }

    private static bool AllHex(string s)
    {
        foreach (var c in s)
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        return true;
    }

    /// <summary>
    /// Reads an IDPS file (raw 16-byte console identifier dump).
    /// </summary>
    public static byte[] ReadIdpsFile(string idpsPath)
    {
        var bytes = File.ReadAllBytes(idpsPath);
        if (bytes.Length < 16)
            throw new InvalidDataException("IDPS file must contain at least 16 bytes.");
        var result = new byte[16];
        Array.Copy(bytes, 0, result, 0, 16);
        return result;
    }

    /// <summary>
    /// Parses a hex string ("AABBCC..." or "AA BB CC..." or with 0x prefixes) into bytes.
    /// Throws on invalid input or unexpected length.
    /// </summary>
    public static byte[] ParseHex(string input, int expectedLength)
    {
        var cleaned = new string(input.Where(c =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')).ToArray());
        if (cleaned.Length != expectedLength * 2)
            throw new FormatException($"Expected {expectedLength * 2} hex characters, got {cleaned.Length}.");
        var bytes = new byte[expectedLength];
        for (int i = 0; i < expectedLength; i++)
            bytes[i] = Convert.ToByte(cleaned.Substring(i * 2, 2), 16);
        return bytes;
    }

    private void Save()
    {
        var dto = new ConsoleKeysDto
        {
            ConsoleIdHex = Convert.ToHexString(_keys.ConsoleId),
            UserIdHex = Convert.ToHexString(_keys.UserId),
            AuthenticationIdHex = Convert.ToHexString(_keys.AuthenticationId),
        };
        File.WriteAllText(_path, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var json = File.ReadAllText(_path);
            var dto = JsonSerializer.Deserialize<ConsoleKeysDto>(json);
            if (dto == null) return;
            _keys = new ConsoleKeys
            {
                ConsoleId = TryParseHex(dto.ConsoleIdHex, 16),
                UserId = TryParseHex(dto.UserIdHex, 8),
                AuthenticationId = TryParseHex(dto.AuthenticationIdHex, 8),
            };
        }
        catch
        {
            _keys = new ConsoleKeys();
        }
    }

    private static byte[] TryParseHex(string? hex, int length)
    {
        if (string.IsNullOrWhiteSpace(hex)) return new byte[length];
        try { return ParseHex(hex, length); }
        catch { return new byte[length]; }
    }

    private sealed class ConsoleKeysDto
    {
        public string ConsoleIdHex { get; set; } = string.Empty;
        public string UserIdHex { get; set; } = string.Empty;
        public string AuthenticationIdHex { get; set; } = string.Empty;
    }
}

public sealed record SaveExtractionResult(bool Success, byte[]? AuthenticationId, byte[]? UserId, string? ErrorMessage)
{
    public static SaveExtractionResult Ok(byte[]? authId, byte[]? userId) => new(true, authId, userId, null);
    public static SaveExtractionResult Fail(string error) => new(false, null, null, error);
}
