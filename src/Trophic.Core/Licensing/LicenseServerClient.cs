using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trophic.Core.Licensing;

/// <summary>
/// Thin HTTP client for the license server's /activate and /refresh endpoints.
/// The base URL is resolved at runtime so the server host can change without a
/// client rebuild (env var override, else the compiled default).
/// </summary>
public sealed class LicenseServerClient
{
    // TODO(deployment): set this to the production HTTPS endpoint before release.
    // Overridable at runtime via the TROPHIC_LICENSE_URL environment variable.
    private const string DefaultBaseUrl = "https://license.trophic.app";

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        AutomaticDecompression = System.Net.DecompressionMethods.All,
    })
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    private readonly string _baseUrl;

    public LicenseServerClient(string? baseUrlOverride = null)
    {
        _baseUrl = (baseUrlOverride
                    ?? Environment.GetEnvironmentVariable("TROPHIC_LICENSE_URL")
                    ?? DefaultBaseUrl).TrimEnd('/');
    }

    public Task<ServerResponse> ActivateAsync(string key, string deviceId, CancellationToken ct = default)
        => PostAsync("/activate", key, deviceId, ct);

    public Task<ServerResponse> RefreshAsync(string key, string deviceId, CancellationToken ct = default)
        => PostAsync("/refresh", key, deviceId, ct);

    /// <summary>
    /// Requests a short-lived signed download URL for a catalog trophy set. The server
    /// validates entitlement before returning the URL, so this is the real paywall.
    /// </summary>
    public async Task<DownloadUrlResponse> GetDownloadUrlAsync(string key, string deviceId, string npwrId, CancellationToken ct = default)
    {
        try
        {
            using var response = await Http.PostAsJsonAsync(_baseUrl + "/download-url",
                new DownloadRequestBody(key, deviceId, npwrId), ct);
            var raw = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                var ok = JsonSerializer.Deserialize<DownloadSuccessBody>(raw);
                if (ok?.Url is { Length: > 0 })
                    return new DownloadUrlResponse(true, false, ok.Url, null, null);
                return new DownloadUrlResponse(false, false, null, "invalid_server_response", "Server returned no URL.");
            }

            var err = TryParseError(raw);
            return new DownloadUrlResponse(false, false, null, err.Code, err.Message ?? MapErrorMessage(err.Code));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new DownloadUrlResponse(false, true, null, "network_error", "The license server did not respond in time.");
        }
        catch (HttpRequestException ex)
        {
            return new DownloadUrlResponse(false, true, null, "network_error", "Could not reach the license server. " + ex.Message);
        }
        catch (Exception ex)
        {
            return new DownloadUrlResponse(false, false, null, "client_error", ex.Message);
        }
    }

    private async Task<ServerResponse> PostAsync(string path, string key, string deviceId, CancellationToken ct)
    {
        try
        {
            using var response = await Http.PostAsJsonAsync(_baseUrl + path,
                new RequestBody(key, deviceId), ct);

            var raw = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                var ok = JsonSerializer.Deserialize<SuccessBody>(raw);
                if (ok?.Jwt is { Length: > 0 })
                    return ServerResponse.Ok(ok.Jwt);
                return ServerResponse.Error("invalid_server_response", "Server returned no assertion.");
            }

            var err = TryParseError(raw);
            return ServerResponse.Error(err.Code, err.Message ?? MapErrorMessage(err.Code));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ServerResponse.Network("The license server did not respond in time.");
        }
        catch (HttpRequestException ex)
        {
            return ServerResponse.Network("Could not reach the license server. " + ex.Message);
        }
        catch (Exception ex)
        {
            return ServerResponse.Error("client_error", ex.Message);
        }
    }

    private static (string Code, string? Message) TryParseError(string raw)
    {
        try
        {
            var e = JsonSerializer.Deserialize<ErrorBody>(raw);
            return (e?.Error ?? "unknown_error", e?.Message);
        }
        catch
        {
            return ("unknown_error", null);
        }
    }

    private static string MapErrorMessage(string code) => code switch
    {
        "unknown_key" => "That license key was not recognized.",
        "revoked" => "This license has been revoked. Contact support.",
        "expired" => "This license has expired.",
        "device_mismatch" => "This license is already bound to a different device. Contact support to transfer it.",
        "not_activated" => "Activate this license before downloading from the Catalog.",
        "bad_request" => "The request was malformed.",
        "bad_npwr_id" => "Invalid catalog item identifier.",
        "release_not_found" or "asset_not_found" => "That catalog item is not available for download yet.",
        _ => "Request failed (" + code + ").",
    };

    private sealed record RequestBody(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("deviceId")] string DeviceId);

    private sealed record DownloadRequestBody(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("deviceId")] string DeviceId,
        [property: JsonPropertyName("npwrId")] string NpwrId);

    private sealed class SuccessBody
    {
        [JsonPropertyName("jwt")] public string? Jwt { get; set; }
    }

    private sealed class DownloadSuccessBody
    {
        [JsonPropertyName("url")] public string? Url { get; set; }
    }

    private sealed class ErrorBody
    {
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
    }
}

/// <summary>
/// Result of a server call. Distinguishes a hard rejection (bad/revoked key) from a
/// transient network failure so the caller can fall back to the offline grace window.
/// </summary>
public sealed record ServerResponse(bool Success, bool IsNetworkFailure, string? Jwt, string? ErrorCode, string? ErrorMessage)
{
    public static ServerResponse Ok(string jwt) => new(true, false, jwt, null, null);
    public static ServerResponse Error(string code, string message) => new(false, false, null, code, message);
    public static ServerResponse Network(string message) => new(false, true, null, "network_error", message);
}

public sealed record DownloadUrlResponse(bool Success, bool IsNetworkFailure, string? Url, string? ErrorCode, string? ErrorMessage);
