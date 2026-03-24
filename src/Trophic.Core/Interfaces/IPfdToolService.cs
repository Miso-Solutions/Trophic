namespace Trophic.Core.Interfaces;

public interface IPfdToolService
{
    Task DecryptTrophyAsync(string directoryPath, CancellationToken ct = default);
    Task EncryptTrophyAsync(string directoryPath, CancellationToken ct = default);
    Task UpdatePfdAsync(string directoryPath, CancellationToken ct = default);
    bool IsAvailable();
}
