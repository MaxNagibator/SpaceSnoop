using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Unicode;

namespace SpaceSnoop.Core.Domain;

internal static class Wtf8
{
    private const int MaxStackDecodeBytes = 512;

    public static int GetMaxByteCount(int charCount)
    {
        return charCount * 3;
    }

    public static int GetByteCount(ReadOnlySpan<char> source)
    {
        return source.IsEmpty ? 0 : Encoding.UTF8.GetByteCount(source);
    }

    public static int FromUtf16(ReadOnlySpan<char> source, Span<byte> destination)
    {
        if (source.IsEmpty)
        {
            return 0;
        }

        var status = Utf8.FromUtf16(source, destination, out _, out var written, replaceInvalidSequences: false);

        if (status == OperationStatus.Done)
        {
            return written;
        }

        if (status == OperationStatus.DestinationTooSmall)
        {
            throw new ArgumentException("Destination buffer is too small.", nameof(destination));
        }

        return EncodeWtf8(source, destination);
    }

    public static string GetString(ReadOnlySpan<byte> wtf8)
    {
        if (wtf8.IsEmpty)
        {
            return string.Empty;
        }

        if (Utf8.IsValid(wtf8))
        {
            return Encoding.UTF8.GetString(wtf8);
        }

        if (wtf8.Length <= MaxStackDecodeBytes)
        {
            Span<char> chars = stackalloc char[MaxStackDecodeBytes];
            var count = ToUtf16(wtf8, chars);
            return new string(chars[..count]);
        }

        var rented = ArrayPool<char>.Shared.Rent(wtf8.Length);

        try
        {
            var count = ToUtf16(wtf8, rented);
            return new string(rented.AsSpan(0, count));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    public static bool Equals(ReadOnlySpan<byte> wtf8, ReadOnlySpan<char> value, StringComparison comparison)
    {
        if ((comparison == StringComparison.Ordinal || comparison == StringComparison.OrdinalIgnoreCase) && Ascii.IsValid(wtf8))
        {
            return comparison == StringComparison.Ordinal
                ? Ascii.Equals(value, wtf8)
                : Ascii.EqualsIgnoreCase(value, wtf8);
        }

        if (wtf8.Length <= MaxStackDecodeBytes)
        {
            Span<char> chars = stackalloc char[MaxStackDecodeBytes];
            var count = ToUtf16(wtf8, chars);
            return chars[..count].Equals(value, comparison);
        }

        var rented = ArrayPool<char>.Shared.Rent(wtf8.Length);

        try
        {
            var count = ToUtf16(wtf8, rented);
            return rented.AsSpan(0, count).Equals(value, comparison);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    internal static int ToUtf16(ReadOnlySpan<byte> wtf8, Span<char> destination)
    {
        if (wtf8.IsEmpty)
        {
            return 0;
        }

        var status = Utf8.ToUtf16(wtf8, destination, out _, out var written, replaceInvalidSequences: false);

        if (status == OperationStatus.Done)
        {
            return written;
        }

        if (status == OperationStatus.DestinationTooSmall)
        {
            throw new ArgumentException("Destination buffer is too small.", nameof(destination));
        }

        return DecodeWtf8(wtf8, destination);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int EncodeWtf8(ReadOnlySpan<char> source, Span<byte> destination)
    {
        var pos = 0;

        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];

            if (c < 0x80)
            {
                destination[pos++] = (byte)c;
            }
            else if (c < 0x800)
            {
                destination[pos++] = (byte)(0xC0 | (c >> 6));
                destination[pos++] = (byte)(0x80 | (c & 0x3F));
            }
            else if (char.IsHighSurrogate(c) && (i + 1) < source.Length && char.IsLowSurrogate(source[i + 1]))
            {
                var cp = char.ConvertToUtf32(c, source[++i]);
                destination[pos++] = (byte)(0xF0 | (cp >> 18));
                destination[pos++] = (byte)(0x80 | ((cp >> 12) & 0x3F));
                destination[pos++] = (byte)(0x80 | ((cp >> 6) & 0x3F));
                destination[pos++] = (byte)(0x80 | (cp & 0x3F));
            }
            else
            {
                destination[pos++] = (byte)(0xE0 | (c >> 12));
                destination[pos++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                destination[pos++] = (byte)(0x80 | (c & 0x3F));
            }
        }

        return pos;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int DecodeWtf8(ReadOnlySpan<byte> wtf8, Span<char> destination)
    {
        var pos = 0;
        var i = 0;

        while (i < wtf8.Length)
        {
            var b0 = wtf8[i];

            if (b0 < 0x80)
            {
                destination[pos++] = (char)b0;
                i++;
                continue;
            }

            if ((b0 & 0xE0) == 0xC0)
            {
                if ((i + 1) < wtf8.Length && b0 >= 0xC2 && (wtf8[i + 1] & 0xC0) == 0x80)
                {
                    destination[pos++] = (char)(((b0 & 0x1F) << 6) | (wtf8[i + 1] & 0x3F));
                    i += 2;
                    continue;
                }

                destination[pos++] = '\uFFFD';
                i++;
                continue;
            }

            if ((b0 & 0xF0) == 0xE0)
            {
                if ((i + 2) < wtf8.Length)
                {
                    var b1 = wtf8[i + 1];
                    var b2 = wtf8[i + 2];

                    if ((b1 & 0xC0) == 0x80 && (b2 & 0xC0) == 0x80 && (b0 != 0xE0 || b1 >= 0xA0))
                    {
                        destination[pos++] = (char)(((b0 & 0x0F) << 12) | ((b1 & 0x3F) << 6) | (b2 & 0x3F));
                        i += 3;
                        continue;
                    }
                }

                destination[pos++] = '\uFFFD';
                i++;
                continue;
            }

            if ((b0 & 0xF8) == 0xF0)
            {
                if ((i + 3) < wtf8.Length)
                {
                    var b1 = wtf8[i + 1];
                    var b2 = wtf8[i + 2];
                    var b3 = wtf8[i + 3];

                    if ((b1 & 0xC0) == 0x80
                        && (b2 & 0xC0) == 0x80
                        && (b3 & 0xC0) == 0x80
                        && !(b0 == 0xF0 && b1 < 0x90)
                        && !(b0 == 0xF4 && b1 > 0x8F)
                        && b0 <= 0xF4)
                    {
                        var cp = ((b0 & 0x07) << 18) | ((b1 & 0x3F) << 12) | ((b2 & 0x3F) << 6) | (b3 & 0x3F);
                        cp -= 0x10000;
                        destination[pos++] = (char)(0xD800 + (cp >> 10));
                        destination[pos++] = (char)(0xDC00 + (cp & 0x3FF));
                        i += 4;
                        continue;
                    }
                }

                destination[pos++] = '\uFFFD';
                i++;
                continue;
            }

            destination[pos++] = '\uFFFD';
            i++;
        }

        return pos;
    }
}
