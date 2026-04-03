namespace Trophic.Core.Models;

public sealed record CatalogEntry(
    string Id,
    string Name,
    string Region,
    string Platform,
    string? OriginalName = null);
