namespace Trophic.TrophyFormat.Enums;

/// <summary>
/// Trophy sync state flags as read via big-endian int32.
/// Original file byte patterns (mapped from LE values in reference code):
///   Synced:    file bytes 00 01 00 00 → BE int 0x10000
///   NotSynced: file bytes 00 00 10 00 → BE int 0x1000
/// </summary>
[Flags]
public enum TrophySyncState
{
    None = 0,
    Synced = 0x10000,
    NotSynced = 0x1000
}
