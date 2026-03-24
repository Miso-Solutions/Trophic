using Trophic.Core.Models;

namespace Trophic.Core.Interfaces;

public interface IWebScraperService : IAsyncDisposable
{
    Task<IReadOnlyList<ScrapedTimestamp>> ScrapeTimestampsAsync(string profileGameUrl, CancellationToken ct = default);
    Task InitializeAsync();
}
