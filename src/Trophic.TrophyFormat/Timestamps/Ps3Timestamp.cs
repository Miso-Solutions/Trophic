using System.Buffers.Binary;

namespace Trophic.TrophyFormat.Timestamps;

/// <summary>
/// Handles conversion between PS3 microsecond timestamps and .NET DateTime.
/// PS3 stores timestamps as big-endian int64 microseconds since the .NET epoch (0001-01-01 UTC).
/// In 16-byte timestamp fields, the same 8-byte value is written twice (duplicated).
/// </summary>
public static class Ps3Timestamp
{
    private static readonly DateTime Epoch = new(1, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Converts PS3 microseconds to a UTC DateTime.
    /// </summary>
    public static DateTime FromMicroseconds(long microseconds)
    {
        if (microseconds <= 0)
            return DateTime.MinValue;

        long ticks = microseconds * 10;
        if (ticks < 0 || ticks > DateTime.MaxValue.Ticks)
            return DateTime.MinValue;

        return new DateTime(ticks, DateTimeKind.Utc);
    }

    /// <summary>
    /// Converts a DateTime to PS3 microseconds.
    /// </summary>
    public static long ToMicroseconds(DateTime dateTime)
    {
        var utc = dateTime.Kind == DateTimeKind.Local
            ? dateTime.ToUniversalTime()
            : dateTime;
        return utc.Ticks / 10;
    }

    /// <summary>
    /// Reads a 16-byte PS3 timestamp field (8-byte BE int64, duplicated).
    /// Returns UTC DateTime.
    /// </summary>
    public static DateTime FromBytes16(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16)
            throw new ArgumentException("PS3 timestamp requires 16 bytes", nameof(data));

        long microseconds = BinaryPrimitives.ReadInt64BigEndian(data);
        return FromMicroseconds(microseconds);
    }

    /// <summary>
    /// Writes a 16-byte PS3 timestamp field (same value written twice).
    /// </summary>
    public static void ToBytes16(DateTime dateTime, Span<byte> dest)
    {
        if (dest.Length < 16)
            throw new ArgumentException("Destination requires 16 bytes", nameof(dest));

        long microseconds = ToMicroseconds(dateTime);
        BinaryPrimitives.WriteInt64BigEndian(dest, microseconds);
        BinaryPrimitives.WriteInt64BigEndian(dest.Slice(8), microseconds);
    }

    /// <summary>
    /// Reads an 8-byte PS3 timestamp (single, not duplicated).
    /// </summary>
    public static DateTime FromBytes8(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
            throw new ArgumentException("PS3 timestamp requires 8 bytes", nameof(data));

        long microseconds = BinaryPrimitives.ReadInt64BigEndian(data);
        return FromMicroseconds(microseconds);
    }

}
