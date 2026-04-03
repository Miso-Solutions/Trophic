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
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const int SW_HIDE = 0;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private const int CdpDebugPort = 19222;
    private const int CdpEndpointRetries = 30;
    private const int CdpRetryDelayMs = 500;
    private const int CloudflarePollRetries = 90;
    private const int CloudflarePollDelayMs = 1000;
    private const int WebSocketUrlRetries = 10;
    private const int TrophyTableRetries = 30;
    private const int TrophyTableRetryDelayMs = 500;
    private const int PageLoadTimeoutMs = 60000;
    private const int PageSettleDelayMs = 2000;
    private const int ProcessKillWaitMs = 10000;

    private const string ChromeUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    /// <summary>
    /// Moves all windows of a process off-screen. The window remains "visible" to the OS
    /// (document.visibilityState = "visible") so Cloudflare challenges still execute,
    /// but the user cannot see it.
    /// </summary>
    private static void MoveProcessWindowsOffScreen(int processId)
    {
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)processId)
                SetWindowPos(hWnd, IntPtr.Zero, -32000, -32000, 0, 0,
                    SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            return true;
        }, IntPtr.Zero);
    }

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

    /// <summary>
    /// Creates the Playwright instance only (for CDP connections to system Chrome).
    /// Does NOT install or launch bundled Chromium.
    /// </summary>
    private async Task EnsurePlaywrightAsync()
    {
        if (_playwright != null) return;

        await _initLock.WaitAsync();
        try
        {
            _playwright ??= await Playwright.CreateAsync();
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Full initialization: installs bundled Chromium and launches a headless browser.
    /// Used by PSN Trophy Leaders and TrueTrophies scrapers.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized || _disposed) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            // Install browser if needed — blocking CLI runs on background thread
            await Task.Run(() => Microsoft.Playwright.Program.Main(["install", "chromium"]));

            _playwright ??= await Playwright.CreateAsync();
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
        // Validate URL at the entry point — all scrapers require valid HTTP(S) URLs
        if (!Uri.TryCreate(profileGameUrl, UriKind.Absolute, out var validatedUri) ||
            validatedUri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException($"[{ErrorCodes.NetImportFailed}] Only HTTP/HTTPS URLs are supported.");
        }

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
                Timeout = PageLoadTimeoutMs
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
                    Timeout = PageLoadTimeoutMs
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
        // Launch a standalone Chrome process with remote debugging, then connect via raw CDP WebSocket.
        // No Playwright/node.exe needed — direct Chrome DevTools Protocol over WebSocket.

        if (Uri.TryCreate(url, UriKind.Absolute, out var parsedUri) &&
            parsedUri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException($"[{ErrorCodes.NetImportFailed}] Only HTTP/HTTPS URLs are supported.");
        }

        var debugPort = CdpDebugPort;
        var userDataDir = Path.Combine(Path.GetTempPath(), "trophic-chrome");
        var chromePath = FindBrowserExecutable();

        // Kill any zombie debug Chrome from a previous crashed session on our fixed port.
        // Leftover processes lock the temp dir and corrupt it, causing STATUS_STACK_BUFFER_OVERRUN.
        try
        {
            using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            await probe.GetStringAsync($"http://localhost:{debugPort}/json", ct);
            // Something is listening on our port — kill it via the CDP browser/close endpoint
            try { await probe.PostAsync($"http://localhost:{debugPort}/json/close", null, ct); } catch { }
            // Also try the shutdown endpoint
            try { await probe.GetStringAsync($"http://localhost:{debugPort}/shutdown", ct); } catch { }
            await Task.Delay(2000, ct);
        }
        catch { /* Nothing on our port — good */ }

        // Force-clean the temp dir
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(userDataDir))
                    Directory.Delete(userDataDir, true);
                break;
            }
            catch { await Task.Delay(500, ct); }
        }

        System.Diagnostics.Process? chromeProcess = null;
        var hideCts = new CancellationTokenSource();
        CancellationTokenSource? linkedCts = null;
        Task? hideTask = null;
        System.Net.WebSockets.ClientWebSocket? ws = null;
        int chromeId = 0;
        try
        {
            chromeProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = chromePath,
                Arguments = $"--remote-debugging-port={debugPort} " +
                            $"--user-data-dir=\"{userDataDir}\" " +
                            "--no-first-run --no-default-browser-check " +
                            "--window-position=-32000,-32000 --window-size=800,600 " +
                            $"\"{url}\"",
                UseShellExecute = false
            });

            chromeId = chromeProcess?.Id ?? 0;
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, hideCts.Token);

            // Wait for CDP endpoint to be available
            for (int retry = 0; retry < CdpEndpointRetries; retry++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await Http.GetStringAsync($"http://localhost:{debugPort}/json", ct);
                    break;
                }
                catch (Exception)
                {
                    if (retry == CdpEndpointRetries - 1) throw;
                    await Task.Delay(CdpRetryDelayMs, ct);
                }
            }

            // Wait for Cloudflare to clear by polling /json (HTTP, no WebSocket needed).
            // Browser must stay visible so Cloudflare's JS challenge can execute.
            // Match by URL to avoid picking up Edge internal pages (sync, new tab, etc).
            var targetHost = parsedUri!.Host;
            bool cloudflareCleared = false;
            for (int attempt = 0; attempt < CloudflarePollRetries && !cloudflareCleared; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var json = await Http.GetStringAsync($"http://localhost:{debugPort}/json", ct);
                    var targets = JsonSerializer.Deserialize<JsonElement>(json);
                    foreach (var target in targets.EnumerateArray())
                    {
                        if (target.TryGetProperty("type", out var type) && type.GetString() == "page" &&
                            target.TryGetProperty("url", out var targetUrl) &&
                            (targetUrl.GetString() ?? "").Contains(targetHost, StringComparison.OrdinalIgnoreCase) &&
                            target.TryGetProperty("title", out var title))
                        {
                            var titleStr = title.GetString() ?? "";
                            if (!titleStr.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) &&
                                titleStr.Length > 0)
                            {
                                cloudflareCleared = true;
                                break;
                            }
                        }
                    }
                }
                catch (Exception) { }
                if (!cloudflareCleared) await Task.Delay(CloudflarePollDelayMs, ct);
            }

            // Small delay to let page fully settle after Cloudflare redirect
            await Task.Delay(PageSettleDelayMs, ct);

            // Get fresh WebSocket URL for the target page (match by host to skip Edge internal pages)
            string? wsUrl = null;
            for (int retry = 0; retry < WebSocketUrlRetries; retry++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var json = await Http.GetStringAsync($"http://localhost:{debugPort}/json", ct);
                    var targets = JsonSerializer.Deserialize<JsonElement>(json);
                    foreach (var target in targets.EnumerateArray())
                    {
                        if (target.TryGetProperty("type", out var type) &&
                            type.GetString() == "page" &&
                            target.TryGetProperty("url", out var targetUrl) &&
                            (targetUrl.GetString() ?? "").Contains(targetHost, StringComparison.OrdinalIgnoreCase) &&
                            target.TryGetProperty("webSocketDebuggerUrl", out var wsUrlProp))
                        {
                            wsUrl = wsUrlProp.GetString();
                            break;
                        }
                    }
                    if (wsUrl != null) break;
                }
                catch (Exception)
                {
                    if (retry == WebSocketUrlRetries - 1) throw;
                }
                await Task.Delay(CdpRetryDelayMs, ct);
            }

            if (string.IsNullOrEmpty(wsUrl))
                throw new Exception("Could not find browser page for this URL. Please try again.");

            // Connect via WebSocket CDP — page is stable now
            ws = new System.Net.WebSockets.ClientWebSocket();
            await ws.ConnectAsync(new Uri(wsUrl), ct);

            int msgId = 1;

            async Task<JsonElement> CdpSendAsync(string method, object? parameters = null)
            {
                var id = msgId++;
                var cmd = new Dictionary<string, object?> { ["id"] = id, ["method"] = method };
                if (parameters != null) cmd["params"] = parameters;
                var bytes = JsonSerializer.SerializeToUtf8Bytes(cmd);
                await ws!.SendAsync(bytes, System.Net.WebSockets.WebSocketMessageType.Text, true, ct);

                var buffer = new byte[1024 * 256];
                while (true)
                {
                    using var ms = new MemoryStream();
                    System.Net.WebSockets.WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(buffer, ct);
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    var response = JsonSerializer.Deserialize<JsonElement>(ms.ToArray());
                    if (response.TryGetProperty("id", out var respId) && respId.GetInt32() == id)
                        return response;
                }
            }

            await CdpSendAsync("Runtime.enable");

            // Wait for trophy table to appear
            for (int attempt = 0; attempt < TrophyTableRetries; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var checkResult = await CdpSendAsync("Runtime.evaluate",
                        new { expression = "!!document.querySelector('table.zebra')" });
                    var found = checkResult.GetProperty("result").GetProperty("result")
                        .GetProperty("value").GetBoolean();
                    if (found) break;
                }
                catch (Exception) { }
                await Task.Delay(TrophyTableRetryDelayMs, ct);
            }

            // Extract timestamps via JS evaluation
            var extractJs = @"(() => {
                const trophies = [];
                const months = {
                    'jan':0,'feb':1,'mar':2,'apr':3,'may':4,'jun':5,
                    'jul':6,'aug':7,'sep':8,'oct':9,'nov':10,'dec':11
                };
                const rows = document.querySelectorAll('table.zebra tr');
                let trophyIndex = -1;
                for (const row of rows) {
                    const trophyLink = row.querySelector('a[href*=""/trophy/""]');
                    if (!trophyLink) continue;
                    trophyIndex++;
                    if (!row.classList.contains('completed')) continue;
                    const nobrs = row.querySelectorAll('nobr');
                    let dateStr = null, timeStr = null;
                    for (const nobr of nobrs) {
                        const text = nobr.textContent.trim();
                        if (/\d{1,2}(?:st|nd|rd|th)\s+\w+\s+\d{4}/i.test(text)) dateStr = text;
                        else if (/\d{1,2}:\d{2}(?::\d{2})?\s*(?:AM|PM)/i.test(text)) timeStr = text;
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
                                    if (ts > 946684800) trophies.push({ id: trophyIndex, timestamp: ts });
                                }
                            }
                        }
                    }
                }
                return JSON.stringify(trophies);
            })()";

            var evalResult = await CdpSendAsync("Runtime.evaluate",
                new { expression = extractJs });
            var jsonStr = evalResult.GetProperty("result").GetProperty("result")
                .GetProperty("value").GetString();

            if (string.IsNullOrEmpty(jsonStr))
                return Array.Empty<ScrapedTimestamp>();

            var parsed = JsonSerializer.Deserialize<JsonElement>(jsonStr);
            return ParseJsonTimestamps(parsed);
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

            if (ws != null)
            {
                try { ws.Dispose(); } catch (Exception) { }
            }

            // Kill all browser processes that were launched with our unique user-data-dir.
            // Edge re-parents processes so the original PID becomes stale.
            try
            {
                var kill = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"Get-CimInstance Win32_Process -Filter \\\"CommandLine LIKE '%trophic-chrome%'\\\" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                kill?.WaitForExit(ProcessKillWaitMs);
                kill?.Dispose();
            }
            catch { }

            await Task.Delay(500);

            try { if (Directory.Exists(userDataDir)) Directory.Delete(userDataDir, true); } catch { }
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
    /// Finds a Chromium-based browser executable. Prefers Edge (always present on Windows 10/11),
    /// falls back to Chrome.
    /// </summary>
    private static string FindBrowserExecutable()
    {
        // Edge preferred — Chrome 146+ crashes with STATUS_STACK_BUFFER_OVERRUN
        // when launched with --remote-debugging-port from a .NET parent process.
        // Edge uses the same Chromium engine and CDP protocol without this issue.
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"Microsoft\Edge\Application\msedge.exe"),
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

        throw new Exception("No compatible browser found. Please install Microsoft Edge or Google Chrome.");
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
