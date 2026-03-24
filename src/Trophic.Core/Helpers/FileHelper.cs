namespace Trophic.Core.Helpers;

public static class FileHelper
{
    /// <summary>
    /// Copies a trophy directory to a temp location for editing.
    /// </summary>
    public static string CopyTrophyDirToTemp(string sourcePath)
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "Trophic");
        string tempDir = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
        string destDir = Path.Combine(tempDir, Path.GetFileName(sourcePath));

        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourcePath))
        {
            string destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        return destDir;
    }

    /// <summary>
    /// File extensions that contain trophy data and need to be saved back.
    /// Icons (PNG) and other static assets are unchanged and skipped to avoid
    /// conflicts with other processes that may have them open.
    /// </summary>
    private static readonly HashSet<string> SaveableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".DAT", ".SFO", ".PFD", ".EDAT"
    };

    public static void CopyTempBackToSource(string tempPath, string sourcePath)
    {
        foreach (var file in Directory.GetFiles(tempPath))
        {
            var ext = Path.GetExtension(file);
            if (!SaveableExtensions.Contains(ext))
                continue;

            string destFile = Path.Combine(sourcePath, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }
    }

    /// <summary>
    /// Deletes a temp directory and its parent container.
    /// </summary>
    public static void DeleteTempDirectory(string tempPath)
    {
        try
        {
            var parent = Directory.GetParent(tempPath);
            if (parent != null && parent.FullName.Contains("Trophic"))
            {
                Directory.Delete(parent.FullName, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup — files may be locked by another process
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup — insufficient permissions
        }
    }
}
