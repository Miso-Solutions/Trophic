using Trophic.Core.Interfaces;

namespace Trophic.Core.Services;

public sealed class ProfileService : IProfileService
{
    private readonly string _profilesDir;

    public ProfileService()
    {
        _profilesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles");
        Directory.CreateDirectory(_profilesDir);
    }

    public IReadOnlyList<string> GetProfileNames()
    {
        if (!Directory.Exists(_profilesDir))
            return Array.Empty<string>();

        return Directory.GetFiles(_profilesDir, "*.SFO")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n != null)
            .Cast<string>()
            .ToList();
    }

    public void SaveProfile(string name, string sourceSfoPath)
    {
        ValidateProfileName(name);

        if (!File.Exists(sourceSfoPath))
            throw new FileNotFoundException($"[{ErrorCodes.FileSfoNotFound}] SFO file not found: {sourceSfoPath}");

        string destPath = Path.Combine(_profilesDir, $"{name}.SFO");
        File.Copy(sourceSfoPath, destPath, overwrite: true);
    }

    public void DeleteProfile(string name)
    {
        ValidateProfileName(name);

        string path = Path.Combine(_profilesDir, $"{name}.SFO");
        if (File.Exists(path))
            File.Delete(path);
    }

    public string? GetProfilePath(string name)
    {
        ValidateProfileName(name);

        string path = Path.Combine(_profilesDir, $"{name}.SFO");
        return File.Exists(path) ? path : null;
    }

    private static void ValidateProfileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Profile name cannot be empty.");

        // Prevent path traversal: strip to filename only, reject path separators
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains("..") ||
            name != Path.GetFileNameWithoutExtension(name))
        {
            throw new ArgumentException($"Invalid profile name: {name}");
        }
    }
}
