using Trophic.Core.Interfaces;
using Trophic.TrophyFormat.Crypto;

namespace Trophic.Core.Services;

/// <summary>
/// Native C# implementation of PFD operations for PS3 trophy files.
/// Replaces the external pfdtool.exe dependency.
/// </summary>
public sealed class NativePfdService : IPfdToolService
{
    public bool IsAvailable() => true;

    public async Task DecryptTrophyAsync(string directoryPath, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            var pfd = ParsePfd(directoryPath);

            var troptrnsPath = Path.Combine(directoryPath, "TROPTRNS.DAT");
            if (!File.Exists(troptrnsPath))
                throw new FileNotFoundException($"[{ErrorCodes.FileTroptrnsNotFound}] TROPTRNS.DAT not found in: {directoryPath}");

            PfdFileHandler.DecryptFile(pfd, troptrnsPath);
        }, ct);
    }

    public async Task EncryptTrophyAsync(string directoryPath, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            var pfd = ParsePfd(directoryPath);

            var troptrnsPath = Path.Combine(directoryPath, "TROPTRNS.DAT");
            if (!File.Exists(troptrnsPath))
                throw new FileNotFoundException($"[{ErrorCodes.FileTroptrnsNotFound}] TROPTRNS.DAT not found in: {directoryPath}");

            PfdFileHandler.EncryptFile(pfd, troptrnsPath);

            // Write PFD with updated file_size (hashes updated separately in UpdatePfdAsync)
            var pfdPath = Path.Combine(directoryPath, "PARAM.PFD");
            File.WriteAllBytes(pfdPath, PfdFileHandler.Serialize(pfd));
        }, ct);
    }

    public async Task UpdatePfdAsync(string directoryPath, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            var pfd = ParsePfd(directoryPath);
            PfdFileHandler.UpdateAllHashes(pfd, directoryPath);

            var pfdPath = Path.Combine(directoryPath, "PARAM.PFD");
            File.WriteAllBytes(pfdPath, PfdFileHandler.Serialize(pfd));
        }, ct);
    }

    private static PfdFile ParsePfd(string directoryPath)
    {
        var pfdPath = Path.Combine(directoryPath, "PARAM.PFD");
        if (!File.Exists(pfdPath))
            throw new FileNotFoundException($"[{ErrorCodes.FileParamPfdNotFound}] PARAM.PFD not found in: {directoryPath}");

        return PfdFileHandler.Parse(File.ReadAllBytes(pfdPath));
    }
}
