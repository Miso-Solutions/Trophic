using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Trophic.Core.Services;

/// <summary>
/// Downloads the latest release ZIP from GitHub, extracts it, and generates a batch script
/// that swaps the old app files with the new ones after the app exits.
/// </summary>
public sealed class AutoUpdateService
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/Miso-Solutions/Trophic/releases/latest";

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AllowAutoRedirect = true
    })
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    static AutoUpdateService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Trophic/1.0");
    }

    /// <summary>
    /// Downloads the latest release, extracts it, creates an update script, and returns the script path.
    /// The caller should run the script and then exit the app.
    /// </summary>
    public async Task<string> DownloadAndPrepareUpdateAsync(
        string currentAppDir,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        // 1. Get the latest release info
        using var apiRequest = new HttpRequestMessage(HttpMethod.Get, ReleasesApiUrl);
        apiRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var apiResponse = await Http.SendAsync(apiRequest, ct);
        apiResponse.EnsureSuccessStatusCode();

        var apiJson = await apiResponse.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(apiJson);
        var root = doc.RootElement;

        // Find the ZIP asset
        string? downloadUrl = null;
        string? assetName = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                downloadUrl = asset.GetProperty("browser_download_url").GetString();
                assetName = name;
                break;
            }
        }

        if (string.IsNullOrEmpty(downloadUrl) || string.IsNullOrEmpty(assetName))
            throw new Exception("No ZIP asset found in the latest release.");

        // 2. Download the ZIP
        var updateDir = Path.Combine(Path.GetTempPath(), "trophic-update");
        if (Directory.Exists(updateDir))
            Directory.Delete(updateDir, true);
        Directory.CreateDirectory(updateDir);

        var zipPath = Path.Combine(updateDir, assetName);

        using var dlRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        using var dlResponse = await Http.SendAsync(dlRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        dlResponse.EnsureSuccessStatusCode();

        var totalBytes = dlResponse.Content.Headers.ContentLength ?? -1;
        var bytesRead = 0L;

        await using var contentStream = await dlResponse.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        int read;
        while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            bytesRead += read;
            if (totalBytes > 0)
                progress?.Report((double)bytesRead / totalBytes * 0.8); // 80% for download
        }
        fileStream.Close();

        // 3. Extract the ZIP
        var extractDir = Path.Combine(updateDir, "extracted");
        ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
        progress?.Report(0.9);

        // Clean up ZIP
        try { File.Delete(zipPath); } catch { }

        // 4. Create the update batch script
        var scriptPath = Path.Combine(updateDir, "update.bat");
        var appExePath = Path.Combine(currentAppDir, "Trophic.exe");

        // The script:
        // - Waits for the app to exit (polls for the process)
        // - Copies new files over old ones (xcopy /E /Y)
        // - Restarts the app
        // - Cleans up the update temp dir
        var script = $"""
            @echo off
            echo Updating Trophic...
            echo Waiting for application to close...
            :waitloop
            tasklist /FI "PID eq %1" 2>NUL | find /I "Trophic.exe" >NUL
            if not errorlevel 1 (
                timeout /t 1 /nobreak >NUL
                goto waitloop
            )
            echo Applying update...
            xcopy /E /Y /Q "{extractDir}\*" "{currentAppDir}\"
            echo Update complete. Restarting...
            start "" "{appExePath}"
            rmdir /S /Q "{updateDir}"
            exit
            """;

        await File.WriteAllTextAsync(scriptPath, script, ct);
        progress?.Report(1.0);

        return scriptPath;
    }
}
