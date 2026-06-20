using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Trophic.Core.Licensing;

/// <summary>
/// Verifies RS256-signed license assertions offline using the embedded public key.
/// Custom JWT parsing keeps the dependency surface minimal; only the RS256 / RSA-PKCS1
/// path is supported, which is what the license server emits.
/// </summary>
public static class AssertionVerifier
{
    private const string ExpectedIssuer = "trophic-license-server";
    private const string ExpectedAudience = "trophic-client";
    private const string PublicKeyResource = "Trophic.Core.Licensing.license-public-key.pem";

    private static RSA? _cachedKey;
    // RSA instances are not guaranteed safe for concurrent use, and Verify can be
    // called from the startup background refresh and the UI thread at the same time.
    private static readonly object _keyGate = new();

    /// <summary>
    /// Verifies the cryptographic signature, algorithm, issuer, audience, and required
    /// claims of a compact-JWS assertion and returns the authentic claims. Returns null
    /// only when the assertion is structurally invalid or the signature does not verify.
    /// Expiry is intentionally NOT enforced here — the caller applies expiry and offline
    /// grace policy against the returned <see cref="LicenseAssertion.AssertionExpiresAtUnix"/>.
    /// </summary>
    public static LicenseAssertion? Verify(string jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt)) return null;

        var parts = jwt.Split('.');
        if (parts.Length != 3) return null;

        try
        {
            var headerJson = DecodeSegment(parts[0]);
            using var headerDoc = JsonDocument.Parse(headerJson);
            var alg = headerDoc.RootElement.TryGetProperty("alg", out var algEl) ? algEl.GetString() : null;
            if (alg != "RS256") return null;

            var signingInput = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
            var signature = Base64UrlDecode(parts[2]);

            // Serialize key creation and the verify itself — RSA is not concurrency-safe.
            bool valid;
            lock (_keyGate)
            {
                var rsa = GetPublicKey();
                if (rsa == null) return null;
                valid = rsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            if (!valid) return null;

            var payloadJson = DecodeSegment(parts[1]);
            using var payloadDoc = JsonDocument.Parse(payloadJson);
            var root = payloadDoc.RootElement;

            if (GetString(root, "iss") != ExpectedIssuer) return null;
            if (GetString(root, "aud") != ExpectedAudience) return null;

            long exp = GetInt64(root, "exp") ?? 0;
            long iat = GetInt64(root, "iat") ?? 0;
            if (exp <= 0) return null;

            var licenseKey = GetString(root, "licenseKey");
            var plan = GetString(root, "plan");
            if (string.IsNullOrEmpty(licenseKey) || string.IsNullOrEmpty(plan)) return null;

            long? licenseExpires = GetInt64(root, "licenseExpiresAt");
            var deviceId = GetString(root, "deviceId");

            return new LicenseAssertion(licenseKey!, plan!, licenseExpires, deviceId, iat, exp);
        }
        catch
        {
            return null;
        }
    }

    private static RSA? GetPublicKey()
    {
        if (_cachedKey != null) return _cachedKey;
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(PublicKeyResource);
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            var pem = reader.ReadToEnd();
            var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            _cachedKey = rsa;
            return rsa;
        }
        catch
        {
            return null;
        }
    }

    private static string DecodeSegment(string segment) => Encoding.UTF8.GetString(Base64UrlDecode(segment));

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

    private static string? GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static long? GetInt64(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetInt64(out var v) ? v : null,
            _ => null
        };
    }
}
