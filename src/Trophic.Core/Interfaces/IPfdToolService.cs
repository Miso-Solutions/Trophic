namespace Trophic.Core.Interfaces;

public interface IPfdToolService
{
    Task DecryptTrophyAsync(string directoryPath, CancellationToken ct = default);
    Task EncryptTrophyAsync(string directoryPath, CancellationToken ct = default);
    Task UpdatePfdAsync(string directoryPath, CancellationToken ct = default);
    Task UpdatePfdAsync(string directoryPath, byte[]? consoleId, byte[]? userId, byte[]? authenticationId, CancellationToken ct = default);
    Task UpdatePfdAsync(string directoryPath, byte[]? consoleId, byte[]? userId, byte[]? authenticationId,
        byte[]? discHashKey, Func<string, byte[]?>? secureFileIdLookup, CancellationToken ct = default);
    bool IsAvailable();
}
