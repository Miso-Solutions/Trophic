using System.IO.Compression;
using Trophic.Core.Interfaces;

namespace Trophic.Core.Services;

/// <summary>
/// Resigns a single PS3 trophy folder (or zipped trophy folder) to a target profile
/// without requiring any edit. Reuses <see cref="TrophyFileService"/>'s open / resign /
/// save flow, which already handles PARAM.SFO account-id patching, TROPUSR / TROPTRNS
/// type-2 account-id rewrites, encryption, and PFD hash regeneration.
///
/// Console keys are optional. When configured, the per-user PARAM.SFO hashes 2/3 are
/// recomputed. When not, the source values are preserved (matching pre-keys behaviour).
/// </summary>
public sealed class TrophyResignService
{
    private const string TrophyMarkerFile = "TROPCONF.SFM";

    private readonly IPfdToolService _pfdTool;
    private readonly ConsoleKeysService _consoleKeysService;

    public TrophyResignService(IPfdToolService pfdTool, ConsoleKeysService consoleKeysService)
    {
        _pfdTool = pfdTool;
        _consoleKeysService = consoleKeysService;
    }

    /// <summary>
    /// Filename prefix used when staging a custom profile SFO into the profiles folder.
    /// Exposed so <see cref="ProfileService"/> can filter these transient files out of
    /// the user-visible dropdown.
    /// </summary>
    public const string TempProfilePrefix = "_trophic_resign_";

    /// <summary>
    /// True when <paramref name="folderPath"/> contains <c>TROPCONF.SFM</c>, the
    /// canonical marker for a PS3 trophy folder.
    /// </summary>
    public static bool IsTrophyFolder(string folderPath)
        => Directory.Exists(folderPath) && File.Exists(Path.Combine(folderPath, TrophyMarkerFile));

    /// <summary>
    /// Best-effort removal of any orphan temp profiles left by a crashed prior run.
    /// Call once at application startup.
    /// </summary>
    public static void CleanupOrphanTempProfiles()
    {
        try
        {
            var profilesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles");
            if (!Directory.Exists(profilesDir)) return;
            foreach (var file in Directory.EnumerateFiles(profilesDir, TempProfilePrefix + "*.SFO"))
            {
                try { File.Delete(file); } catch { /* best-effort */ }
            }
        }
        catch { /* best-effort */ }
    }

    public async Task<SaveResignResult> ResignAsync(string sourcePath, string profileSfoPath,
        string outputFolder, bool outputAsZip = false, CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            return SaveResignResult.Fail($"Source not found: {sourcePath}");
        if (string.IsNullOrWhiteSpace(profileSfoPath) || !File.Exists(profileSfoPath))
            return SaveResignResult.Fail("Target profile PARAM.SFO not found.");

        // TrophyFileService.ResignToProfile loads profiles/<name>.SFO. To support a custom
        // profile path outside that folder, temporarily stage the file there under a unique
        // name, then delete it once we're done.
        var profilesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles");
        Directory.CreateDirectory(profilesDir);
        // Append the separator so "<base>\profiles_other\foo.SFO" doesn't false-match the
        // "<base>\profiles" prefix.
        var profileDirNorm = Path.GetFullPath(profilesDir)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var profileFullPath = Path.GetFullPath(profileSfoPath);
        string profileName;
        string? tempProfilePath = null;

        var saveName = Directory.Exists(sourcePath)
            ? Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : Path.GetFileNameWithoutExtension(sourcePath);

        var workingDir = CreateTempWorkingDir();

        try
        {
            // Stage the profile inside the try so cleanup runs even if anything before
            // the main pipeline throws.
            if (profileFullPath.StartsWith(profileDirNorm, StringComparison.OrdinalIgnoreCase))
            {
                profileName = Path.GetFileNameWithoutExtension(profileSfoPath);
            }
            else
            {
                profileName = TempProfilePrefix + Guid.NewGuid().ToString("N")[..8];
                tempProfilePath = Path.Combine(profilesDir, profileName + ".SFO");
                File.Copy(profileSfoPath, tempProfilePath, overwrite: true);
            }

            // 1. Stage input into a working directory
            var stagedRoot = Path.Combine(workingDir, "input");
            if (Directory.Exists(sourcePath))
                CopyDirectory(sourcePath, stagedRoot);
            else if (sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                ZipFile.ExtractToDirectory(sourcePath, stagedRoot, overwriteFiles: true);
            else
                return SaveResignResult.Fail("Input must be a folder or .zip.");

            // 2. Locate the trophy folder (root or any sub-folder containing TROPCONF.SFM)
            var trophyFolders = FindAllTrophyFolders(stagedRoot);
            if (trophyFolders.Count == 0)
                return SaveResignResult.Fail("No trophy folder (TROPCONF.SFM) found in the source.");
            if (trophyFolders.Count > 1)
                return SaveResignResult.Fail($"Source contains {trophyFolders.Count} trophy folders. Provide one trophy folder per queue entry.");
            var stagedTrophyDir = trophyFolders[0];

            // 3. Resolve a collision-free destination and copy the trophy folder there
            //    so TrophyFileService.SaveAsync writes back to the destination on completion.
            var destFolder = ResolveCollisionFreeOutputDir(outputFolder, saveName);
            CopyDirectory(stagedTrophyDir, destFolder);

            // 4. Open + resign + save via TrophyFileService — it owns the full pipeline
            //    (PARAM.SFO patch, TROPUSR/TROPTRNS account-id rewrites, encrypt, hash regen).
            var trophyFile = new TrophyFileService(_pfdTool, _consoleKeysService);
            try
            {
                await trophyFile.OpenAsync(destFolder);
                await trophyFile.SaveAsync(profileName);
            }
            finally
            {
                trophyFile.Close();
            }

            // 5. If requested, zip the destination and remove the folder so the final
            //    artifact is just the zip file.
            if (outputAsZip)
            {
                var zipPath = ResolveCollisionFreeOutputZip(outputFolder, saveName);
                var zipStage = Path.Combine(workingDir, "zip");
                Directory.CreateDirectory(zipStage);
                CopyDirectory(destFolder, Path.Combine(zipStage, saveName));
                ZipFile.CreateFromDirectory(zipStage, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
                TryCleanup(destFolder);
                return SaveResignResult.Ok(zipPath);
            }

            return SaveResignResult.Ok(destFolder);
        }
        catch (Exception ex)
        {
            return SaveResignResult.Fail(ex.Message);
        }
        finally
        {
            TryCleanup(workingDir);
            if (tempProfilePath != null)
            {
                try { File.Delete(tempProfilePath); } catch { /* best-effort */ }
            }
        }
    }

    private static string CreateTempWorkingDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "Trophic.TrophyResign." + Guid.NewGuid().ToString("N")[..8]);
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

    private static List<string> FindAllTrophyFolders(string root)
    {
        var matches = new List<string>();
        if (File.Exists(Path.Combine(root, TrophyMarkerFile)))
            matches.Add(root);
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            if (File.Exists(Path.Combine(dir, TrophyMarkerFile)))
                matches.Add(dir);
        return matches;
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
        catch { /* best-effort */ }
    }
}
