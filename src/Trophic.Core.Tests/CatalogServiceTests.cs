using Trophic.Core.Services;

namespace Trophic.Core.Tests;

public class CatalogServiceTests
{
    private static CatalogService CreateWithTempCatalog(string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"trophic-test-{Guid.NewGuid()}");
        var dataDir = Path.Combine(dir, "data");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, "ps3_catalog.json"), json);
        return new CatalogService(dir);
    }

    [Fact]
    public void Entries_LoadsValidCatalog()
    {
        var catalog = CreateWithTempCatalog("""
        [
            {"id":"NPWR00001_00","name":"Test Game","region":"WW","platform":"PS3"},
            {"id":"NPWR00002_00","name":"Another Game","region":"EUR","platform":"PS3"}
        ]
        """);
        Assert.Equal(2, catalog.Entries.Count);
        Assert.Equal("NPWR00001_00", catalog.Entries[0].Id);
        Assert.Equal("Test Game", catalog.Entries[0].Name);
    }

    [Fact]
    public void Entries_ReturnsEmptyForMissingFile()
    {
        var catalog = new CatalogService(Path.Combine(Path.GetTempPath(), "nonexistent"));
        Assert.Empty(catalog.Entries);
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsAll()
    {
        var catalog = CreateWithTempCatalog("""
        [
            {"id":"NPWR00001_00","name":"Game A","region":"WW","platform":"PS3"},
            {"id":"NPWR00002_00","name":"Game B","region":"EUR","platform":"PS3"}
        ]
        """);
        Assert.Equal(2, catalog.Search("").Count);
        Assert.Equal(2, catalog.Search("  ").Count);
    }

    [Fact]
    public void Search_ByNpwrId_FindsMatch()
    {
        var catalog = CreateWithTempCatalog("""
        [
            {"id":"NPWR00001_00","name":"Killzone 2","region":"WW","platform":"PS3"},
            {"id":"NPWR00153_00","name":"Another","region":"WW","platform":"PS3"}
        ]
        """);
        var results = catalog.Search("NPWR00153");
        Assert.Single(results);
        Assert.Equal("Another", results[0].Name);
    }

    [Fact]
    public void Search_ByName_CaseInsensitive()
    {
        var catalog = CreateWithTempCatalog("""
        [
            {"id":"NPWR00001_00","name":"Grand Theft Auto IV","region":"WW","platform":"PS3"},
            {"id":"NPWR00002_00","name":"Killzone 2","region":"WW","platform":"PS3"}
        ]
        """);
        var results = catalog.Search("grand theft");
        Assert.Single(results);
        Assert.Equal("Grand Theft Auto IV", results[0].Name);
    }

    [Fact]
    public void Search_ByOriginalName_FindsMatch()
    {
        var catalog = CreateWithTempCatalog("""
        [
            {"id":"NPWR00508_00","name":"Yakuza 3","region":"JPN","platform":"PS3","originalName":"龍が如く3"}
        ]
        """);
        var results = catalog.Search("龍が如く");
        Assert.Single(results);
        Assert.Equal("Yakuza 3", results[0].Name);
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmpty()
    {
        var catalog = CreateWithTempCatalog("""
        [
            {"id":"NPWR00001_00","name":"Test","region":"WW","platform":"PS3"}
        ]
        """);
        Assert.Empty(catalog.Search("nonexistent"));
    }

    [Fact]
    public void CatalogEntry_OriginalName_IsOptional()
    {
        var catalog = CreateWithTempCatalog("""
        [
            {"id":"NPWR00001_00","name":"Test","region":"WW","platform":"PS3"}
        ]
        """);
        Assert.Null(catalog.Entries[0].OriginalName);
    }
}
