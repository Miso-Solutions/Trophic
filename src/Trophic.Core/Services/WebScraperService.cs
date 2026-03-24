using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Trophic.Core.Interfaces;
using Trophic.Core.Models;

namespace Trophic.Core.Services;

/// <summary>
/// Scrapes trophy timestamps from PSN Trophy Leaders, TrueTrophies, and PSNProfiles.
/// Fast path: plain HttpClient (sub-second on a normal response).
/// Fallback: headless Chromium via Playwright for Cloudflare JS challenges.
/// TrueTrophies: headed (visible) Chromium so user can solve Cloudflare Turnstile.
/// </summary>
public sealed class WebScraperService : IWebScraperService
{
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const int SW_HIDE = 0;

    private const string ChromeUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private static void HideProcessWindows(int processId)
    {
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)processId)
                ShowWindow(hWnd, SW_HIDE);
            return true;
        }, IntPtr.Zero);
    }

    private enum ScrapeSite { PsnTrophyLeaders, TrueTrophies, PsnProfiles, Unknown }

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    static WebScraperService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            ChromeUserAgent);
        Http.DefaultRequestHeaders.Accept.ParseAdd(
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        Http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        Http.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
        Http.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
        Http.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
        Http.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
        Http.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
        Http.DefaultRequestHeaders.Add("sec-ch-ua",
            "\"Google Chrome\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"");
        Http.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
        Http.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
    }

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private bool _initialized;
    private bool _disposed;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private static readonly Regex PsnTimestampRegex = new(
        @"<td class=""date_earned"">\s+<span class=""sort"">(\d+)</span>",
        RegexOptions.Compiled);

    private static bool IsCloudflareChallenge(string html) =>
        html.Contains("challenge-form") ||
        html.Contains("cf-browser-verification") ||
        html.Contains("cf_chl_") ||
        html.Contains("Just a moment");

    private static ScrapeSite DetectSite(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var host = uri.Host.ToLowerInvariant();
            if (host.Contains("psntrophyleaders.com"))
                return ScrapeSite.PsnTrophyLeaders;
            if (host.Contains("truetrophies.com"))
                return ScrapeSite.TrueTrophies;
            if (host.Contains("psnprofiles.com"))
                return ScrapeSite.PsnProfiles;
        }
        return ScrapeSite.Unknown;
    }

    public async Task InitializeAsync()
    {
        if (_initialized || _disposed) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            // Install browser if needed — blocking CLI runs on background thread
            await Task.Run(() => Microsoft.Playwright.Program.Main(["install", "chromium"]));

            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<IReadOnlyList<ScrapedTimestamp>> ScrapeTimestampsAsync(
        string profileGameUrl, CancellationToken ct = default)
    {
        var site = DetectSite(profileGameUrl);

        return site switch
        {
            ScrapeSite.TrueTrophies => await ScrapeTrueTrophiesAsync(profileGameUrl, ct),
            ScrapeSite.PsnProfiles => await ScrapePsnProfilesAsync(profileGameUrl, ct),
            _ => await ScrapePsnTrophyLeadersAsync(profileGameUrl, ct)
        };
    }

    private async Task<IReadOnlyList<ScrapedTimestamp>> ScrapePsnTrophyLeadersAsync(
        string url, CancellationToken ct)
    {
        // Fast path: plain HTTP request, no browser overhead
        try
        {
            var html = await Http.GetStringAsync(url, ct);
            if (!IsCloudflareChallenge(html))
            {
                var fast = ParsePsnTimestamps(html);
                if (fast.Count > 0)
                    return fast;
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // HTTP failed or returned garbage — fall through to Playwright
        }

        // Slow path: Playwright handles Cloudflare JS challenges
        await InitializeAsync();

        var page = await _browser!.NewPageAsync();
        try
        {
            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60000
            });

            try
            {
                await page.WaitForSelectorAsync(".date_earned", new PageWaitForSelectorOptions
                {
                    Timeout = 30000
                });
            }
            catch (TimeoutException) { }

            var content = await page.ContentAsync();
            return ParsePsnTimestamps(content);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private async Task<IReadOnlyList<ScrapedTimestamp>> ScrapeTrueTrophiesAsync(
        string url, CancellationToken ct)
    {
        // TrueTrophies uses Cloudflare Turnstile. Use system Chrome in headless mode
        // with anti-detection flags to avoid triggering the challenge.
        await InitializeAsync();

        IBrowser? stealthBrowser = null;
        try
        {
            stealthBrowser = await _playwright!.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Channel = "chrome",
                Headless = true,
                Args = ["--disable-blink-features=AutomationControlled"]
            });

            var context = await stealthBrowser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = ChromeUserAgent
            });

            var page = await context.NewPageAsync();

            // Remove the navigator.webdriver flag that Cloudflare checks
            await page.AddInitScriptAsync(@"
                Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
            ");

            try
            {
                await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 60000
                });

                await WaitForCloudflareAsync(page, maxAttempts: 30, delayMs: 2000, ct);

                // Let page fully render
                await page.WaitForTimeoutAsync(3000);

                // Extract timestamps from TrueTrophies DOM structure:
                // ul.ach-panels > li (all trophies, li.w = won/earned)
                // Date text in span.lock.u or div.info spans: "13 July 2015 -" or "09 Sep 14"
                var results = await page.EvaluateAsync<JsonElement?>(@"() => {
                    const trophies = [];
                    const months = {
                        'jan':0,'feb':1,'mar':2,'apr':3,'may':4,'jun':5,
                        'jul':6,'aug':7,'sep':8,'oct':9,'nov':10,'dec':11,
                        'january':0,'february':1,'march':2,'april':3,'june':5,
                        'july':6,'august':7,'september':8,'october':9,'november':10,'december':11
                    };

                    const items = document.querySelectorAll('ul.ach-panels > li');
                    let id = 0;

                    for (const li of items) {
                        // li.w = won/earned trophy
                        if (!li.classList.contains('w')) { id++; continue; }

                        // Search all text in the panel for date patterns
                        const text = li.textContent || '';

                        // Patterns found on TrueTrophies:
                        // ""13 July 2015 -"" or ""09 Sep 14"" or ""23 Dec 24 at 12:07""
                        // Try full format first: DD Month YYYY
                        let dateMatch = text.match(
                            /(\d{1,2})\s+(Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\s+(\d{4})(?:\s+at\s+(\d{1,2}):(\d{2}))?/i
                        );

                        if (!dateMatch) {
                            // Try short year: DD Mon YY (e.g. ""09 Sep 14"")
                            dateMatch = text.match(
                                /(\d{1,2})\s+(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+(\d{2})(?:\s+at\s+(\d{1,2}):(\d{2}))?/i
                            );
                            if (dateMatch) {
                                // Convert 2-digit year to 4-digit
                                let yr = parseInt(dateMatch[3], 10);
                                dateMatch[3] = String(yr < 50 ? 2000 + yr : 1900 + yr);
                            }
                        }

                        if (dateMatch) {
                            const day = parseInt(dateMatch[1], 10);
                            const monthStr = dateMatch[2].toLowerCase();
                            const year = parseInt(dateMatch[3], 10);
                            const hour = dateMatch[4] ? parseInt(dateMatch[4], 10) : 12;
                            const minute = dateMatch[5] ? parseInt(dateMatch[5], 10) : 0;
                            const month = months[monthStr];

                            if (month !== undefined && year >= 2000) {
                                const d = new Date(Date.UTC(year, month, day, hour, minute, 0));
                                if (!isNaN(d.getTime())) {
                                    const ts = Math.floor(d.getTime() / 1000);
                                    if (ts > 946684800) {
                                        trophies.push({ id: id, timestamp: ts });
                                    }
                                }
                            }
                        }

                        id++;
                    }

                    return trophies;
                }");

                return ParseJsonTimestamps(results);
            }
            finally
            {
                await page.CloseAsync();
                await context.CloseAsync();
            }
        }
        finally
        {
            if (stealthBrowser != null)
                await stealthBrowser.CloseAsync();
        }
    }

    private async Task<IReadOnlyList<ScrapedTimestamp>> ScrapePsnProfilesAsync(
        string url, CancellationToken ct)
    {
        // PSNProfiles has aggressive Cloudflare Turnstile that detects Playwright-launched browsers.
        // Launch a standalone Chrome process with remote debugging, then connect via CDP.
        // This makes Chrome completely undetectable as automation.
        await InitializeAsync();

        // Validate URL scheme to prevent passing non-HTTP URIs to the browser process
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsedUri) &&
            parsedUri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException($"[{ErrorCodes.NetImportFailed}] Only HTTP/HTTPS URLs are supported.");
        }

        var debugPort = 9222 + Random.Shared.Next(1000);
        var userDataDir = Path.Combine(Path.GetTempPath(), $"ps3te-chrome-{debugPort}");
        var chromePath = FindChromeExecutable();

        System.Diagnostics.Process? chromeProcess = null;
        IBrowser? cdpBrowser = null;
        var hideCts = new CancellationTokenSource();
        CancellationTokenSource? linkedCts = null;
        Task? hideTask = null;
        try
        {
            // Launch Chrome as headed (Cloudflare detects all headless modes).
            // Continuously hide Chrome windows using a background task.
            chromeProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = chromePath,
                Arguments = $"--remote-debugging-port={debugPort} " +
                            $"--user-data-dir=\"{userDataDir}\" " +
                            "--no-first-run --no-default-browser-check " +
                            $"\"{url}\"",
                UseShellExecute = false
            });

            // Background task that aggressively hides all Chrome windows every 50ms
            var chromeId = chromeProcess?.Id ?? 0;
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, hideCts.Token);
            hideTask = Task.Run(async () =>
            {
                while (!linkedCts.Token.IsCancellationRequested)
                {
                    if (chromeId > 0) HideProcessWindows(chromeId);
                    try { await Task.Delay(50, linkedCts.Token); } catch (Exception) { break; }
                }
            }, linkedCts.Token);

            // Connect to Chrome via CDP with retry (Chrome needs a moment to start)
            for (int retry = 0; retry < 20; retry++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    cdpBrowser = await _playwright!.Chromium.ConnectOverCDPAsync($"http://localhost:{debugPort}");
                    break;
                }
                catch (Exception)
                {
                    if (retry == 19) throw;
                    await Task.Delay(500, ct);
                }
            }

            // Find the page Chrome opened
            IPage? page = null;
            foreach (var ctx in cdpBrowser!.Contexts)
            {
                foreach (var p in ctx.Pages)
                {
                    page = p;
                    break;
                }
                if (page != null) break;
            }

            if (page == null)
                throw new Exception("Could not find browser page. Please try again.");

            // Wait for Cloudflare to clear, then for trophy content to appear
            await WaitForCloudflareAsync(page, maxAttempts: 60, delayMs: 1000, ct);

            // Wait for any post-Cloudflare navigation to settle
            try
            {
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded,
                    new PageWaitForLoadStateOptions { Timeout = 10000 });
            }
            catch (Exception) { }

            // Wait for trophy table to appear
            try
            {
                await page.WaitForSelectorAsync("table.zebra", new PageWaitForSelectorOptions { Timeout = 15000 });
            }
            catch (TimeoutException) { }

            // PSNProfiles structure:
            // table.zebra contains trophy rows
            // tr.completed = earned trophies; first tr is a summary row (skip)
            // Each trophy tr has <a href="/trophy/game/N-name"> and <nobr> with date/time
            // Dates in <nobr>: "18th Feb 2015" followed by "2:03:52 AM"
            var results = await page.EvaluateAsync<JsonElement?>(@"() => {
                const trophies = [];
                const months = {
                    'jan':0,'feb':1,'mar':2,'apr':3,'may':4,'jun':5,
                    'jul':6,'aug':7,'sep':8,'oct':9,'nov':10,'dec':11
                };

                // Get all table rows
                const rows = document.querySelectorAll('table.zebra tr');
                let trophyIndex = -1;

                for (const row of rows) {
                    // Only process rows that have a trophy link
                    const trophyLink = row.querySelector('a[href*=""/trophy/""]');
                    if (!trophyLink) continue;

                    trophyIndex++;

                    // Only process earned trophies
                    if (!row.classList.contains('completed')) continue;

                    // Extract date from <nobr> elements
                    const nobrs = row.querySelectorAll('nobr');
                    let dateStr = null;
                    let timeStr = null;

                    for (const nobr of nobrs) {
                        const text = nobr.textContent.trim();
                        // Date pattern: '18th Feb 2015' or '9th Sep 2014'
                        if (/\d{1,2}(?:st|nd|rd|th)\s+\w+\s+\d{4}/i.test(text)) {
                            dateStr = text;
                        }
                        // Time pattern: '2:03:52 AM' or '10:30:00 PM'
                        else if (/\d{1,2}:\d{2}(?::\d{2})?\s*(?:AM|PM)/i.test(text)) {
                            timeStr = text;
                        }
                    }

                    if (dateStr) {
                        const dm = dateStr.match(/(\d{1,2})(?:st|nd|rd|th)\s+(\w+)\s+(\d{4})/i);
                        if (dm) {
                            const day = parseInt(dm[1], 10);
                            const month = months[dm[2].toLowerCase().substring(0,3)];
                            const year = parseInt(dm[3], 10);

                            let hour = 12, minute = 0, second = 0;
                            if (timeStr) {
                                const tm = timeStr.match(/(\d{1,2}):(\d{2})(?::(\d{2}))?\s*(AM|PM)/i);
                                if (tm) {
                                    hour = parseInt(tm[1], 10);
                                    minute = parseInt(tm[2], 10);
                                    second = tm[3] ? parseInt(tm[3], 10) : 0;
                                    if (tm[4].toUpperCase() === 'PM' && hour < 12) hour += 12;
                                    if (tm[4].toUpperCase() === 'AM' && hour === 12) hour = 0;
                                }
                            }

                            if (month !== undefined && year >= 2000) {
                                const d = new Date(Date.UTC(year, month, day, hour, minute, second));
                                if (!isNaN(d.getTime())) {
                                    const ts = Math.floor(d.getTime() / 1000);
                                    if (ts > 946684800) {
                                        trophies.push({ id: trophyIndex, timestamp: ts });
                                    }
                                }
                            }
                        }
                    }
                }

                return trophies;
            }");

            return ParseJsonTimestamps(results);
        }
        finally
        {
            hideCts.Cancel();
            if (hideTask != null)
            {
                try { await hideTask; } catch (OperationCanceledException) { }
            }

            linkedCts?.Dispose();
            hideCts.Dispose();

            if (cdpBrowser != null)
                await cdpBrowser.CloseAsync();

            if (chromeProcess != null && !chromeProcess.HasExited)
            {
                try { chromeProcess.Kill(true); } catch (Exception) { }
                chromeProcess.Dispose();
            }

            // Clean up temp user data dir
            try { if (Directory.Exists(userDataDir)) Directory.Delete(userDataDir, true); } catch (Exception) { }
        }
    }

    private static IReadOnlyList<ScrapedTimestamp> ParsePsnTimestamps(string html)
    {
        var matches = PsnTimestampRegex.Matches(html);
        var results = new List<ScrapedTimestamp>();
        int id = 0;

        foreach (Match match in matches)
        {
            if (long.TryParse(match.Groups[1].Value, out long timestamp))
                results.Add(new ScrapedTimestamp(id, timestamp));
            id++;
        }

        return results;
    }

    /// <summary>
    /// Parses the JSON array returned by in-page JavaScript evaluation into ScrapedTimestamp list.
    /// Both TrueTrophies and PSNProfiles scraping return the same {id, timestamp} format.
    /// </summary>
    private static IReadOnlyList<ScrapedTimestamp> ParseJsonTimestamps(JsonElement? results)
    {
        var timestamps = new List<ScrapedTimestamp>();
        if (results?.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in results.Value.EnumerateArray())
            {
                var id = item.GetProperty("id").GetInt32();
                var ts = item.GetProperty("timestamp").GetInt64();
                if (ts > 0)
                    timestamps.Add(new ScrapedTimestamp(id, ts));
            }
        }
        return timestamps;
    }

    /// <summary>
    /// Polls the page title until the Cloudflare "Just a moment" challenge clears.
    /// </summary>
    private static async Task WaitForCloudflareAsync(IPage page, int maxAttempts, int delayMs, CancellationToken ct)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var title = await page.TitleAsync();
                if (!title.Contains("Just a moment", StringComparison.OrdinalIgnoreCase))
                    return;
            }
            catch (Exception) { /* page may be navigating */ }
            await Task.Delay(delayMs, ct);
        }
    }

    /// <summary>
    /// Finds the Chrome executable across common install locations.
    /// </summary>
    private static string FindChromeExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Google\Chrome\Application\chrome.exe")
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        throw new Exception("Google Chrome not found. Please install Chrome to import from PSNProfiles.");
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _initialized = false;

        if (_browser != null)
        {
            await _browser.CloseAsync();
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;
    }
}
