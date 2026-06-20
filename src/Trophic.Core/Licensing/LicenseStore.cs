using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Trophic.Core.Licensing;

/// <summary>
/// Persists the license key, the latest server assertion, and the last successful
/// server-verification time. The blob is encrypted at rest with Windows DPAPI
/// (CurrentUser scope) so it cannot be decrypted on a different machine or user —
/// a second layer of defence on top of the server-side device binding.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LicenseStore
{
    private readonly string _path;

    // Serializes file access. The startup background refresh and user-triggered
    // reads/writes can otherwise hit license.bin concurrently, causing a locked-file
    // exception (spurious "not licensed") or a torn write.
    private readonly object _gate = new();

    public LicenseStore(string baseDirectory)
    {
        _path = Path.Combine(baseDirectory, "license.bin");
    }

    public LicenseRecord? Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path)) return null;
                var protectedBytes = File.ReadAllBytes(_path);
                var plain = ProtectedData.Unprotect(protectedBytes, OptionalEntropy, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(plain);
                return JsonSerializer.Deserialize<LicenseRecord>(json);
            }
            catch
            {
                // Corrupt, tampered, or moved to another machine — treat as unlicensed.
                return null;
            }
        }
    }

    public void Save(LicenseRecord record)
    {
        var json = JsonSerializer.Serialize(record);
        var plain = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(plain, OptionalEntropy, DataProtectionScope.CurrentUser);
        lock (_gate)
        {
            // Write to a temp file then move into place so a crash mid-write can't
            // leave a torn license.bin.
            var tmp = _path + ".tmp";
            File.WriteAllBytes(tmp, protectedBytes);
            File.Move(tmp, _path, overwrite: true);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            try { if (File.Exists(_path)) File.Delete(_path); }
            catch { /* best-effort */ }
        }
    }

    // Static app-specific entropy mixed into DPAPI. Not a secret (it ships in the
    // binary) but ties the blob to this application's storage format.
    private static byte[] OptionalEntropy { get; } = Encoding.UTF8.GetBytes("trophic-license-store-v1");
}

/// <summary>
/// On-disk record. The assertion is the server-signed JWT; LastVerifiedAtUnix is
/// the last time the server confirmed the license was still active.
/// </summary>
public sealed record LicenseRecord(string LicenseKey, string Assertion, long LastVerifiedAtUnix);
