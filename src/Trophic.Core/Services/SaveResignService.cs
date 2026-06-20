using System.IO.Compression;
using Trophic.Core.Interfaces;
using Trophic.Core.Models;

namespace Trophic.Core.Services;

/// <summary>
/// Resigns a single PS3 save folder or zip to a target profile.
/// Patches the 16-byte account ID inside PARAM.SFO, then re-runs PFD hash
/// regeneration using the user's Console ID / User ID / Authentication ID so
/// the resulting save is valid for the target account on the user's console.
/// </summary>
public sealed class SaveResignService
{
    private const int SfoProfileIdLength = 16;

    private readonly IPfdToolService _pfdTool;
    private readonly ConsoleKeysService _consoleKeysService;
    private readonly GamesConfService? _gamesConf;

    public SaveResignService(IPfdToolService pfdTool, ConsoleKeysService consoleKeysService,
        GamesConfService? gamesConf = null)
    {
        _pfdTool = pfdTool;
        _consoleKeysService = consoleKeysService;
        _gamesConf = gamesConf;
    }

    public async Task<SaveResignResult> ResignAsync(string sourcePath, string targetProfileSfoPath,
        string outputFolder, bool outputAsZip = false, CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            return SaveResignResult.Fail($"Source not found: {sourcePath}");

        var keys = _consoleKeysService.GetKeys();
        if (!keys.IsConfigured)
            return SaveResignResult.Fail("Console keys not configured. Open Menu → Console Keys.");

        if (!File.Exists(targetProfileSfoPath))
            return SaveResignResult.Fail($"Target profile PARAM.SFO not found: {targetProfileSfoPath}");

        // For zip inputs strip the .zip extension; for folder inputs preserve the name as-is
        // so folders containing dots (e.g. "Foo.Bar") don't get silently truncated.
        var saveName = Directory.Exists(sourcePath)
            ? Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : Path.GetFileNameWithoutExtension(sourcePath);
        var workingDir = CreateTempWorkingDir();
        var saveDir = Path.Combine(workingDir, "save");

        try
        {
            // Stage the input into the working directory
            if (Directory.Exists(sourcePath))
                CopyDirectory(sourcePath, saveDir);
            else if (sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                ExtractZipPreservingTopFolder(sourcePath, saveDir);
            else
                return SaveResignResult.Fail($"Input must be a folder or .zip: {sourcePath}");

            // Save folders are sometimes nested inside the zip; locate PARAM.PFD.
            var pfdDirs = FindAllPfdDirectories(saveDir);
            if (pfdDirs.Count == 0)
                return SaveResignResult.Fail("PARAM.PFD not found in the source save.");
            if (pfdDirs.Count > 1)
                return SaveResignResult.Fail(
                    $"Source contains {pfdDirs.Count} save folders. Provide a single save folder per queue entry.");
            var pfdDir = pfdDirs[0];

            var sfoPath = Path.Combine(pfdDir, "PARAM.SFO");
            if (!File.Exists(sfoPath))
                return SaveResignResult.Fail("PARAM.SFO not found alongside PARAM.PFD.");

            // Extract target account ID and patch PARAM.SFO at both required locations:
            //   1. The top-level ACCOUNT_ID SFO key (variable file offset)
            //   2. The 16-byte account_id field inside the PARAMS struct (offset 0x30)
            // Also patch the PARAMS user_id_1 / user_id_2 fields so the save claims the
            // resigning console's local user. Apollo writes both; mismatched values are
            // rejected by PSN.
            var targetAccountId = ExtractProfileAccountId(targetProfileSfoPath);
            var userIdInt = ParseUserIdToUint(keys.UserId);
            PatchSaveSfo(sfoPath, targetAccountId, userIdInt);

            // Look up the per-game disc_hash_key + secure_file_id table when games.conf is loaded.
            // PS3 save PARAM.SFOs do not carry a TITLE_ID key; the game ID lives in
            // SAVEDATA_DIRECTORY (e.g. "BLUS30068" or "BLES00269-DATA-A"). First 9 chars
            // are the canonical game ID.
            byte[]? discHashKey = null;
            Func<string, byte[]?>? secureFileIdLookup = null;
            if (_gamesConf is { IsLoaded: true })
            {
                var titleId = ExtractTitleId(sfoPath, pfdDir);
                if (!string.IsNullOrEmpty(titleId))
                {
                    discHashKey = _gamesConf.GetDiscHashKey(titleId);
                    var capturedTitleId = titleId;
                    secureFileIdLookup = fname => _gamesConf.GetSecureFileId(capturedTitleId, fname);
                }
            }

            // Recompute all PFD hashes using the configured console keys + per-game keys.
            await _pfdTool.UpdatePfdAsync(pfdDir, keys.ConsoleId, keys.UserId, keys.AuthenticationId,
                discHashKey, secureFileIdLookup, ct);

            // Determine output destination: outputFolder / saveName / <pfd-files>
            // Or, when zipping, outputFolder / saveName.zip containing the save folder.
            if (outputAsZip)
            {
                var zipPath = ResolveCollisionFreeOutputZip(outputFolder, saveName);
                Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
                // Stage under a top-level folder matching the original save name so the
                // archive layout matches what PS3 file managers expect.
                var stage = Path.Combine(Path.GetTempPath(), "Trophic.Resign.Zip." + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(stage);
                try
                {
                    CopyDirectory(pfdDir, Path.Combine(stage, saveName));
                    ZipFile.CreateFromDirectory(stage, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
                }
                finally { TryCleanup(stage); }
                return SaveResignResult.Ok(zipPath);
            }

            var destSaveDir = ResolveCollisionFreeOutputDir(outputFolder, saveName);
            CopyDirectory(pfdDir, destSaveDir);
            return SaveResignResult.Ok(destSaveDir);
        }
        catch (Exception ex)
        {
            return SaveResignResult.Fail(ex.Message);
        }
        finally
        {
            TryCleanup(workingDir);
        }
    }

    private static string CreateTempWorkingDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "Trophic.Resign." + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var dest = Path.Combine(destDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private static void ExtractZipPreservingTopFolder(string zipPath, string destDir)
    {
        Directory.CreateDirectory(destDir);
        ZipFile.ExtractToDirectory(zipPath, destDir, overwriteFiles: true);
    }

    /// <summary>
    /// Returns every folder beneath <paramref name="root"/> that contains a PARAM.PFD,
    /// sorted by nesting depth (shallowest first) for deterministic ordering. The
    /// caller treats more than one match as an ambiguity and surfaces an error.
    /// </summary>
    private static List<string> FindAllPfdDirectories(string root)
    {
        var matches = new List<string>();
        if (File.Exists(Path.Combine(root, "PARAM.PFD")))
            matches.Add(root);
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            if (File.Exists(Path.Combine(dir, "PARAM.PFD")))
                matches.Add(dir);
        matches.Sort((a, b) =>
        {
            int depthA = a.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);
            int depthB = b.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);
            int byDepth = depthA.CompareTo(depthB);
            return byDepth != 0 ? byDepth : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        });
        return matches;
    }

    private static string? ExtractTitleId(string sfoPath, string pfdDir)
    {
        var dirField = ParamSfoReader.ReadString(sfoPath, "SAVEDATA_DIRECTORY");
        if (!string.IsNullOrEmpty(dirField) && dirField.Length >= 9)
            return dirField[..9];

        // Fallback: many saves live in a folder named <GAMEID> or <GAMEID>-<suffix>
        var folderName = Path.GetFileName(pfdDir);
        if (!string.IsNullOrEmpty(folderName) && folderName.Length >= 9)
            return folderName[..9];

        return null;
    }

    private static byte[] ExtractProfileAccountId(string profileSfoPath)
    {
        var bytes = File.ReadAllBytes(profileSfoPath);
        if (bytes.Length < 0x14 || bytes[0] != 0x00 || bytes[1] != (byte)'P' || bytes[2] != (byte)'S' || bytes[3] != (byte)'F')
            throw new InvalidDataException("Profile PARAM.SFO has invalid magic (expected \\0PSF).");

        var (off, len) = ParamSfoReader.FindFieldOffset(bytes, "ACCOUNT_ID");
        if (off < 0 || len < SfoProfileIdLength)
            throw new InvalidDataException("Profile PARAM.SFO does not contain a valid ACCOUNT_ID field.");

        var result = new byte[SfoProfileIdLength];
        Array.Copy(bytes, off, result, 0, SfoProfileIdLength);
        return result;
    }

    /// <summary>
    /// Generic-save SFO patcher. Writes the 16-byte account id (ASCII hex form) into:
    ///   - the top-level ACCOUNT_ID field, located by parsing the SFO index table
    ///   - the embedded account_id slot at PARAMS struct offset 0x30
    /// </summary>
    private static uint ParseUserIdToUint(byte[] userIdBytes)
    {
        if (userIdBytes == null || userIdBytes.Length != 8) return 0;
        var ascii = System.Text.Encoding.ASCII.GetString(userIdBytes);
        if (ascii.All(c => c >= '0' && c <= '9') &&
            uint.TryParse(ascii, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var n))
            return n;
        return 0;
    }

    /// <summary>
    /// Single-pass PARAM.SFO patcher. Writes the 16-byte account id into the top-level
    /// ACCOUNT_ID field and the PARAMS-embedded slot at offset 0x30, and (when non-zero)
    /// updates PARAMS user_id_1 / user_id_2 at offsets 0x18 / 0x2C in little-endian.
    /// </summary>
    private static void PatchSaveSfo(string sfoPath, byte[] accountId, uint userId)
    {
        if (accountId.Length != 16)
            throw new ArgumentException("Account ID must be 16 bytes (ASCII hex form).", nameof(accountId));

        var bytes = File.ReadAllBytes(sfoPath);

        // 1. Top-level ACCOUNT_ID field — clear the allocated slot then write the 16 bytes
        var (accountOff, accountLen) = ParamSfoReader.FindFieldOffset(bytes, "ACCOUNT_ID");
        if (accountOff >= 0 && accountLen >= 16)
        {
            Array.Clear(bytes, accountOff, accountLen);
            Array.Copy(accountId, 0, bytes, accountOff, 16);
        }

        // 2. Embedded account_id and user_id fields inside the PARAMS struct
        var (paramsOff, paramsLen) = ParamSfoReader.FindFieldOffset(bytes, "PARAMS");
        if (paramsOff >= 0)
        {
            if (paramsLen >= 0x40)
                Array.Copy(accountId, 0, bytes, paramsOff + 0x30, 16);
            if (userId > 0 && paramsLen >= 0x30)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(paramsOff + 0x18), userId);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(paramsOff + 0x2C), userId);
            }
        }

        File.WriteAllBytes(sfoPath, bytes);
    }

    private static string ResolveCollisionFreeOutputDir(string outputFolder, string saveName)
    {
        Directory.CreateDirectory(outputFolder);
        var candidate = Path.Combine(outputFolder, saveName);
        if (!Directory.Exists(candidate)) return candidate;
        for (int i = 1; i < 1000; i++)
        {
            var withSuffix = Path.Combine(outputFolder, $"{saveName}_{i}");
            if (!Directory.Exists(withSuffix)) return withSuffix;
        }
        throw new IOException("Could not allocate a unique output folder name.");
    }

    private static string ResolveCollisionFreeOutputZip(string outputFolder, string saveName)
    {
        Directory.CreateDirectory(outputFolder);
        var candidate = Path.Combine(outputFolder, saveName + ".zip");
        if (!File.Exists(candidate)) return candidate;
        for (int i = 1; i < 1000; i++)
        {
            var withSuffix = Path.Combine(outputFolder, $"{saveName}_{i}.zip");
            if (!File.Exists(withSuffix)) return withSuffix;
        }
        throw new IOException("Could not allocate a unique output zip name.");
    }

    private static void TryCleanup(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}

public sealed record SaveResignResult(bool Success, string? OutputPath, string? ErrorMessage)
{
    public static SaveResignResult Ok(string outputPath) => new(true, outputPath, null);
    public static SaveResignResult Fail(string error) => new(false, null, error);
}
