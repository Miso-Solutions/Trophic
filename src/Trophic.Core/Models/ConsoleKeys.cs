namespace Trophic.Core.Models;

/// <summary>
/// User-supplied per-console/per-account key material needed for PS3 save resigning.
/// Each value is unique to a PS3 console and PSN account; the user provides them once.
/// </summary>
public sealed class ConsoleKeys
{
    /// 16-byte console identifier (IDPS). Unique per PS3 hardware unit.
    public byte[] ConsoleId { get; set; } = new byte[16];

    /// 8-byte local user identifier on the PS3.
    public byte[] UserId { get; set; } = new byte[8];

    /// 8-byte PSN account authentication ID (from act.dat).
    public byte[] AuthenticationId { get; set; } = new byte[8];

    /// True when all three values are non-zero (a usable key set).
    public bool IsConfigured =>
        HasNonZero(ConsoleId) && HasNonZero(UserId) && HasNonZero(AuthenticationId);

    private static bool HasNonZero(byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
            if (bytes[i] != 0) return true;
        return false;
    }
}
