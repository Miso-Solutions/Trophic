using System.Reflection;
using Trophic.Core.Models;
using Trophic.Core.Services;

namespace Trophic.Core.Tests;

public class WebScraperParsingTests
{
    private static IReadOnlyList<ScrapedTimestamp> ParsePsnTimestamps(string html)
    {
        var method = typeof(WebScraperService).GetMethod(
            "ParsePsnTimestamps",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (IReadOnlyList<ScrapedTimestamp>)method!.Invoke(null, [html])!;
    }

    [Fact]
    public void ParsePsnTimestamps_ValidHtml_ExtractsTimestamps()
    {
        var html = """
            <td class="date_earned">
                <span class="sort">1423756800</span>
            </td>
            <td class="date_earned">
                <span class="sort">1423843200</span>
            </td>
            """;

        var result = ParsePsnTimestamps(html);

        Assert.Equal(2, result.Count);
        Assert.Equal(0, result[0].TrophyId);
        Assert.Equal(1423756800, result[0].UnixTimestamp);
        Assert.Equal(1, result[1].TrophyId);
        Assert.Equal(1423843200, result[1].UnixTimestamp);
    }

    [Fact]
    public void ParsePsnTimestamps_EmptyHtml_ReturnsEmpty()
    {
        var result = ParsePsnTimestamps("<html><body>No trophies here</body></html>");
        Assert.Empty(result);
    }

    [Fact]
    public void ParsePsnTimestamps_NoMatchingPattern_ReturnsEmpty()
    {
        // The regex requires digits in the sort span; non-numeric content won't match
        var html = """
            <td class="date_earned">
                <span class="sort">notanumber</span>
            </td>
            """;

        var result = ParsePsnTimestamps(html);
        Assert.Empty(result);
    }

    [Fact]
    public void ParsePsnTimestamps_SingleEntry_CorrectId()
    {
        var html = """
            <td class="date_earned">
                <span class="sort">946684800</span>
            </td>
            """;

        var result = ParsePsnTimestamps(html);
        Assert.Single(result);
        Assert.Equal(0, result[0].TrophyId);
        Assert.Equal(946684800, result[0].UnixTimestamp);
    }
}
