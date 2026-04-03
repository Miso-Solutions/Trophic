using System.Net.Http;
using System.Text.Json;

namespace Trophic.Core.Services;

/// <summary>
/// Checks for new versions by querying GitHub Releases on the Trophic repo.
/// </summary>
public sealed class UpdateCheckService
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/Miso-Solutions/Trophic/releases/latest";

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    static UpdateCheckService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Trophic/1.0");
        Http.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    /// <summary>
    /// Returns the latest version tag from GitHub, or null if the check fails.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(string currentVersion, CancellationToken ct = default)
    {
        try
        {
            var json = await Http.GetStringAsync(ReleasesApiUrl, ct);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString();
            if (string.IsNullOrEmpty(tagName)) return null;

            // Strip leading 'v' for comparison
            var latestVersion = tagName.TrimStart('v');
            var current = currentVersion.TrimStart('v');

            if (!Version.TryParse(latestVersion, out var latest) ||
                !Version.TryParse(current, out var curr))
                return null;

            if (latest <= curr) return null;

            var htmlUrl = root.GetProperty("html_url").GetString() ?? "";
            var body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";

            return new UpdateInfo(latestVersion, htmlUrl, body);
        }
        catch
        {
            // Non-critical — update check failure should never block the app
            return null;
        }
    }
}

public sealed record UpdateInfo(string Version, string Url, string ReleaseNotes);
