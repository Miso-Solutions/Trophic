using System.Text;

namespace Trophic.TrophyFormat.Binary;

public static class SpanExtensions
{
    public static string ReadUtf8String(this ReadOnlySpan<byte> span, int offset, int length)
    {
        var slice = span.Slice(offset, length);
        int nullIndex = slice.IndexOf((byte)0);
        int len = nullIndex >= 0 ? nullIndex : length;
        return Encoding.UTF8.GetString(slice.Slice(0, len));
    }
}
