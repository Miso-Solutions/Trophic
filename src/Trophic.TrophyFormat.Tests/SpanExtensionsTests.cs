using System.Text;
using Trophic.TrophyFormat.Binary;

namespace Trophic.TrophyFormat.Tests;

public class SpanExtensionsTests
{
    [Fact]
    public void ReadUtf8String_BasicAscii()
    {
        var data = Encoding.UTF8.GetBytes("Hello World");
        var result = ((ReadOnlySpan<byte>)data).ReadUtf8String(0, data.Length);
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void ReadUtf8String_NullTerminated()
    {
        var data = new byte[] { (byte)'A', (byte)'B', (byte)'C', 0, (byte)'X', (byte)'Y' };
        var result = ((ReadOnlySpan<byte>)data).ReadUtf8String(0, 6);
        Assert.Equal("ABC", result);
    }

    [Fact]
    public void ReadUtf8String_WithOffset()
    {
        var data = new byte[] { 0xFF, 0xFF, (byte)'H', (byte)'i', 0 };
        var result = ((ReadOnlySpan<byte>)data).ReadUtf8String(2, 3);
        Assert.Equal("Hi", result);
    }

    [Fact]
    public void ReadUtf8String_AllNullBytes()
    {
        var data = new byte[8];
        var result = ((ReadOnlySpan<byte>)data).ReadUtf8String(0, 8);
        Assert.Equal("", result);
    }

    [Fact]
    public void ReadUtf8String_NoNullTerminator()
    {
        var data = Encoding.UTF8.GetBytes("ABCDEF");
        var result = ((ReadOnlySpan<byte>)data).ReadUtf8String(0, 6);
        Assert.Equal("ABCDEF", result);
    }

    [Fact]
    public void ReadUtf8String_Utf8MultiByte()
    {
        var text = "caf\u00e9"; // café with é as 2-byte UTF-8
        var data = new byte[16];
        var bytes = Encoding.UTF8.GetBytes(text);
        Array.Copy(bytes, data, bytes.Length);
        // null terminated since data is zeroed
        var result = ((ReadOnlySpan<byte>)data).ReadUtf8String(0, 16);
        Assert.Equal(text, result);
    }
}
