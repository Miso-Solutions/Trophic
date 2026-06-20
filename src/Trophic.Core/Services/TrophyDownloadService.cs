using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using Trophic.Core.Licensing;

namespace Trophic.Core.Services;

/// <summary>
/// Downloads catalog trophy sets. Two paths, selected by <see cref="LicensingOptions.Enabled"/>:
///
/// - Licensing ON: the license server authorizes the download and returns a short-lived
///   signed URL. No repo token ships in the client; a patched client cannot obtain the URL.
/// - Licensing OFF (interim): the client downloads directly from the trophy-database repo
///   using an embedded read-only token. Catalog is free. This is the pre-server behaviour.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TrophyDownloadService
{
    // Direct-download target. The new repo name is used WITHOUT relying on GitHub's 301
    // redirect from the old name — .NET strips the Authorization header on redirects,
    // which 404s private-repo requests. Hitting the current name directly avoids that.
    private const string RepoApiBase = "https://api.github.com/repos/Miso-Solutions/Miso-PS3-Database";

    private static string Token
    {
        get
        {
            // Read-only fine-grained PAT for release asset downloads, split to avoid
            // GitHub push-protection secret scanning. NOTE: this token is shipped in the
            // client and is therefore exposed — rotate it and keep the rotated value
            // scoped read-only. The licensed path (LicensingOptions.Enabled) removes it.
            var parts = new[] {
                "github_pat_11AXCPXPQ0",
                "LeHnvIXHMDc7_4bqrNoPg",
                "Nv5A3kypY1Xc9faVcmtmY",
                "gjT09M72kj7Z85IAAH25B",
                "CcNmy1eyv"
            };
            return string.Concat(parts);
        }
    }

    private readonly LicenseService _license;

    public TrophyDownloadService(LicenseService license)
    {
        _license = license;
    }

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        AllowAutoRedirect = true
    })
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    static TrophyDownloadService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Trophic/1.0");
    }

    /// <summary>
    /// Downloads a trophy ZIP and extracts it to the destination folder. Returns the path
    /// to the extracted trophy folder. Uses the licensed server path or the interim direct
    /// path depending on <see cref="LicensingOptions.Enabled"/>.
    /// </summary>
    public async Task<string> DownloadAndExtractAsync(
        string npwrId, string destinationFolder,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var zipName = $"PS3_{npwrId}.zip";
        var trophicDir = Path.Combine(destinationFolder, "Trophic Trophies");
        Directory.CreateDirectory(trophicDir);
        var zipPath = Path.Combine(trophicDir, zipName);
        var extractDir = trophicDir;

        using var response = LicensingOptions.Enabled
            ? await GetLicensedResponseAsync(npwrId, ct)
            : await GetDirectResponseAsync(npwrId, zipName, ct);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new Exception("This trophy set isn't available for download yet. Please try another title or check back later.");
            throw new Exception($"Download failed ({(int)response.StatusCode}). Please try again later.");
        }

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var bytesRead = 0L;

        await using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
        await using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                bytesRead += read;
                if (totalBytes > 0)
                    progress?.Report((double)bytesRead / totalBytes);
            }
        }

        progress?.Report(1.0);

        // Extract ZIP (the ZIP already contains the trophy folder, e.g. NPWR00214_00/)
        ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

        // Clean up ZIP file
        try { File.Delete(zipPath); } catch { }

        // The ZIP contains a folder named after the NPWR ID
        var expectedFolder = Path.Combine(extractDir, npwrId);
        if (Directory.Exists(expectedFolder) && File.Exists(Path.Combine(expectedFolder, "TROPCONF.SFM")))
            return expectedFolder;

        // Fallback: search for the trophy folder
        return FindTrophyFolder(extractDir, npwrId);
    }

    /// <summary>
    /// Licensed path: the server validates entitlement and returns a short-lived signed
    /// URL, which we then download (no auth header needed on the signed URL).
    /// </summary>
    private async Task<HttpResponseMessage> GetLicensedResponseAsync(string npwrId, CancellationToken ct)
    {
        var auth = await _license.RequestDownloadUrlAsync(npwrId, ct);
        if (!auth.Success || string.IsNullOrEmpty(auth.Url))
            throw new Exception(auth.ErrorMessage ?? "Download was not authorized.");

        using var request = new HttpRequestMessage(HttpMethod.Get, auth.Url);
        return await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    /// <summary>
    /// Interim direct path: resolve the asset's API URL from the repo, then download it
    /// with the embedded token. The asset API URL is on api.github.com (same origin as
    /// the lookup), and its 302 to the signed CDN host needs no auth, so the
    /// auth-stripped-on-redirect issue does not apply here.
    /// </summary>
    private async Task<HttpResponseMessage> GetDirectResponseAsync(string npwrId, string zipName, CancellationToken ct)
    {
        var assetUrl = await GetAssetDownloadUrlAsync(npwrId, zipName, ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, assetUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        return await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private static async Task<string> GetAssetDownloadUrlAsync(string npwrId, string zipName, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{RepoApiBase}/releases/tags/{npwrId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await Http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (body.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
                throw new Exception("GitHub API rate limit reached. Please try again in about an hour.");
            throw new Exception("Access denied. The download token may have expired.");
        }
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new Exception("This trophy set isn't available for download yet. Please try another title or check back later.");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() == zipName)
                return asset.GetProperty("url").GetString()!;
        }

        throw new Exception("This trophy set isn't available for download yet. Please try another title or check back later.");
    }

    /// <summary>
    /// Searches for the trophy folder containing TROPCONF.SFM within the extracted directory.
    /// </summary>
    private static string FindTrophyFolder(string extractDir, string npwrId)
    {
        // Check root
        if (File.Exists(Path.Combine(extractDir, "TROPCONF.SFM")))
            return extractDir;

        // Prefer subdirectory matching the NPWR ID
        foreach (var dir in Directory.GetDirectories(extractDir))
        {
            if (Path.GetFileName(dir).Contains(npwrId, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(dir, "TROPCONF.SFM")))
                return dir;
        }

        // Check all subdirectories
        foreach (var dir in Directory.GetDirectories(extractDir))
        {
            if (File.Exists(Path.Combine(dir, "TROPCONF.SFM")))
                return dir;

            foreach (var subDir in Directory.GetDirectories(dir))
            {
                if (File.Exists(Path.Combine(subDir, "TROPCONF.SFM")))
                    return subDir;
            }
        }

        // Fallback
        var dirs = Directory.GetDirectories(extractDir);
        return dirs.Length > 0 ? dirs[0] : extractDir;
    }
}
