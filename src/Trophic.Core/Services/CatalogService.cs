using System.Text.Json;
using Trophic.Core.Models;

namespace Trophic.Core.Services;

public sealed class CatalogService
{
    private IReadOnlyList<CatalogEntry>? _entries;
    private readonly string _catalogPath;

    public CatalogService(string basePath)
    {
        _catalogPath = Path.Combine(basePath, "data", "ps3_catalog.json");
    }

    public IReadOnlyList<CatalogEntry> Entries => _entries ??= LoadCatalog();

    public IReadOnlyList<CatalogEntry> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Entries;

        var q = query.Trim();
        return Entries.Where(e =>
            e.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            (e.OriginalName != null && e.OriginalName.Contains(q, StringComparison.OrdinalIgnoreCase))
        ).ToList();
    }

    private IReadOnlyList<CatalogEntry> LoadCatalog()
    {
        if (!File.Exists(_catalogPath))
            return Array.Empty<CatalogEntry>();

        var json = File.ReadAllText(_catalogPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<List<CatalogEntry>>(json, options)
               ?? new List<CatalogEntry>();
    }
}
