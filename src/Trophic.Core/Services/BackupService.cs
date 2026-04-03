using System.IO.Compression;

namespace Trophic.Core.Services;

/// <summary>
/// Exports and imports user data (settings, profiles, recent files) as a ZIP backup.
/// </summary>
public sealed class BackupService
{
    private static readonly string[] BackupFiles = ["settings.json", "recent.json"];
    private const string ProfilesDir = "profiles";

    /// <summary>
    /// Creates a ZIP backup of user data (settings, recent files, profiles).
    /// </summary>
    public void ExportBackup(string basePath, string destinationZipPath)
    {
        if (File.Exists(destinationZipPath))
            File.Delete(destinationZipPath);

        using var zip = ZipFile.Open(destinationZipPath, ZipArchiveMode.Create);

        // Add settings and recent files
        foreach (var file in BackupFiles)
        {
            var fullPath = Path.Combine(basePath, file);
            if (File.Exists(fullPath))
                zip.CreateEntryFromFile(fullPath, file);
        }

        // Add profiles
        var profilesPath = Path.Combine(basePath, ProfilesDir);
        if (Directory.Exists(profilesPath))
        {
            foreach (var profile in Directory.GetFiles(profilesPath))
            {
                var entryName = Path.Combine(ProfilesDir, Path.GetFileName(profile));
                zip.CreateEntryFromFile(profile, entryName);
            }
        }
    }

    /// <summary>
    /// Restores user data from a ZIP backup. Overwrites existing files.
    /// </summary>
    public void ImportBackup(string basePath, string sourceZipPath)
    {
        using var zip = ZipFile.OpenRead(sourceZipPath);

        foreach (var entry in zip.Entries)
        {
            // Security: prevent path traversal
            var destinationPath = Path.GetFullPath(Path.Combine(basePath, entry.FullName));
            if (!destinationPath.StartsWith(Path.GetFullPath(basePath), StringComparison.OrdinalIgnoreCase))
                continue;

            // Create directory if needed
            var dir = Path.GetDirectoryName(destinationPath);
            if (dir != null) Directory.CreateDirectory(dir);

            // Skip directory entries
            if (string.IsNullOrEmpty(entry.Name)) continue;

            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }
}
