using Trophic.TrophyFormat.Timestamps;

namespace Trophic.TrophyFormat.Tests;

public class Ps3TimestampTests
{
    [Fact]
    public void FromMicroseconds_ZeroReturnsMinValue()
    {
        var result = Ps3Timestamp.FromMicroseconds(0);
        Assert.Equal(DateTime.MinValue, result);
    }

    [Fact]
    public void FromMicroseconds_NegativeReturnsMinValue()
    {
        var result = Ps3Timestamp.FromMicroseconds(-1);
        Assert.Equal(DateTime.MinValue, result);
    }

    [Fact]
    public void FromMicroseconds_KnownDate()
    {
        // 2015-01-01 00:00:00 UTC = 635556672000000000 ticks = 63555667200000000 microseconds
        var expected = new DateTime(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        long microseconds = expected.Ticks / 10;
        var result = Ps3Timestamp.FromMicroseconds(microseconds);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ToMicroseconds_RoundTripsWithFromMicroseconds()
    {
        var original = new DateTime(2020, 6, 15, 14, 30, 0, DateTimeKind.Utc);
        long microseconds = Ps3Timestamp.ToMicroseconds(original);
        var roundTripped = Ps3Timestamp.FromMicroseconds(microseconds);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void ToMicroseconds_LocalTimeConvertsToUtc()
    {
        var utcTime = new DateTime(2020, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var localTime = utcTime.ToLocalTime();
        long fromUtc = Ps3Timestamp.ToMicroseconds(utcTime);
        long fromLocal = Ps3Timestamp.ToMicroseconds(localTime);
        Assert.Equal(fromUtc, fromLocal);
    }

    [Fact]
    public void FromBytes16_ValidData()
    {
        var expected = new DateTime(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        long microseconds = expected.Ticks / 10;

        var data = new byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(data, microseconds);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(8), microseconds);

        var result = Ps3Timestamp.FromBytes16(data);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FromBytes16_TooShortThrows()
    {
        Assert.Throws<ArgumentException>(() => Ps3Timestamp.FromBytes16(new byte[8]));
    }

    [Fact]
    public void ToBytes16_RoundTripsWithFromBytes16()
    {
        var original = new DateTime(2022, 3, 14, 9, 0, 0, DateTimeKind.Utc);
        var data = new byte[16];
        Ps3Timestamp.ToBytes16(original, data);
        var result = Ps3Timestamp.FromBytes16(data);
        Assert.Equal(original, result);
    }

    [Fact]
    public void ToBytes16_WritesDuplicatedValue()
    {
        var dt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var data = new byte[16];
        Ps3Timestamp.ToBytes16(dt, data);

        // Both halves should be identical
        Assert.Equal(data[..8], data[8..16]);
    }

    [Fact]
    public void ToBytes16_TooShortThrows()
    {
        Assert.Throws<ArgumentException>(() => Ps3Timestamp.ToBytes16(DateTime.UtcNow, new byte[8]));
    }

    [Fact]
    public void FromBytes8_ValidData()
    {
        var expected = new DateTime(2018, 7, 4, 12, 0, 0, DateTimeKind.Utc);
        long microseconds = expected.Ticks / 10;
        var data = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(data, microseconds);

        var result = Ps3Timestamp.FromBytes8(data);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FromBytes8_TooShortThrows()
    {
        Assert.Throws<ArgumentException>(() => Ps3Timestamp.FromBytes8(new byte[4]));
    }
}
