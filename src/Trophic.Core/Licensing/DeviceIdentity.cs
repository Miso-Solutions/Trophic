using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Trophic.Core.Licensing;

/// <summary>
/// Produces a stable, opaque per-machine identifier used to bind a license to a
/// device. Derived from the Windows cryptographic MachineGuid so it survives app
/// reinstalls and is not a copyable file. The raw MachineGuid is never sent; only
/// a salted SHA-256 hash leaves the machine.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DeviceIdentity
{
    private const string Salt = "trophic-device-binding-v1";

    private static string? _cached;

    public static string Get()
    {
        if (_cached != null) return _cached;
        var machineGuid = ReadMachineGuid() ?? FallbackId();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Salt + ":" + machineGuid));
        // 32 hex chars is plenty of entropy for a binding id and keeps payloads small.
        _cached = Convert.ToHexString(bytes, 0, 16);
        return _cached;
    }

    private static string? ReadMachineGuid()
    {
        try
        {
            using var key = RegistryKey
                .OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default)
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch
        {
            return null;
        }
    }

    // Only reached if the registry read fails (e.g. locked-down environment).
    // A machine-name-based fallback is weaker but keeps activation functional.
    private static string FallbackId()
    {
        try { return "host:" + Environment.MachineName; }
        catch { return "host:unknown"; }
    }
}
