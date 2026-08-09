#nullable enable
using System.Buffers.Binary;
using System.Text;

namespace AndroidRuntime.Core.Apk;

internal readonly record struct AndroidChunk(ushort Type, int HeaderSize, int Size, int Offset)
{
    public int End => checked(Offset + Size);
}

internal static class AndroidBinaryFormat
{
    internal const ushort StringPoolType = 0x0001;
    private const uint Utf8Flag = 0x00000100;

    internal static AndroidChunk ReadChunk(ReadOnlySpan<byte> data, int offset, int enclosingEnd, string diagnosticPrefix)
    {
        if (offset < 0 || enclosingEnd < offset || enclosingEnd > data.Length || offset > enclosingEnd - 8)
            throw Invalid(diagnosticPrefix, "chunk header is truncated");
        ushort type = U16(data, offset, diagnosticPrefix);
        int headerSize = U16(data, offset + 2, diagnosticPrefix);
        uint sizeValue = U32(data, offset + 4, diagnosticPrefix);
        if (sizeValue > int.MaxValue) throw Invalid(diagnosticPrefix, "chunk size exceeds the supported range");
        int size = (int)sizeValue;
        if (headerSize < 8 || size < headerSize || size > enclosingEnd - offset)
            throw Invalid(diagnosticPrefix, "chunk header bounds are invalid");
        return new AndroidChunk(type, headerSize, size, offset);
    }

    internal static string[] ReadStringPool(ReadOnlySpan<byte> data, AndroidChunk chunk, string diagnosticPrefix, int maximumStrings = 65536, int maximumDecodedBytes = 16 * 1024 * 1024)
    {
        if (chunk.Type != StringPoolType || chunk.HeaderSize < 28)
            throw Invalid(diagnosticPrefix, "string pool header is invalid");
        int stringCount = CheckedInt(U32(data, chunk.Offset + 8, diagnosticPrefix), diagnosticPrefix, "string count");
        int styleCount = CheckedInt(U32(data, chunk.Offset + 12, diagnosticPrefix), diagnosticPrefix, "style count");
        uint flags = U32(data, chunk.Offset + 16, diagnosticPrefix);
        int stringsStart = CheckedInt(U32(data, chunk.Offset + 20, diagnosticPrefix), diagnosticPrefix, "strings start");
        uint stylesStartValue = U32(data, chunk.Offset + 24, diagnosticPrefix);
        int stylesStart = stylesStartValue == 0 ? chunk.Size : CheckedInt(stylesStartValue, diagnosticPrefix, "styles start");
        if (stringCount > maximumStrings) throw Invalid(diagnosticPrefix, $"string count exceeds quota {maximumStrings}");
        int offsetsEnd;
        try { offsetsEnd = checked(chunk.HeaderSize + checked((stringCount + styleCount) * 4)); }
        catch (OverflowException) { throw Invalid(diagnosticPrefix, "string pool offset table overflows"); }
        if (offsetsEnd > chunk.Size || stringsStart < offsetsEnd || stringsStart > stylesStart || stylesStart > chunk.Size)
            throw Invalid(diagnosticPrefix, "string pool regions overlap or exceed chunk bounds");

        var result = new string[stringCount];
        int decodedBytes = 0;
        bool utf8 = (flags & Utf8Flag) != 0;
        int table = chunk.Offset + chunk.HeaderSize;
        int stringsBase = chunk.Offset + stringsStart;
        int stringsLimit = chunk.Offset + stylesStart;
        for (int index = 0; index < stringCount; index++)
        {
            int relative = CheckedInt(U32(data, table + index * 4, diagnosticPrefix), diagnosticPrefix, "string offset");
            int valueOffset;
            try { valueOffset = checked(stringsBase + relative); }
            catch (OverflowException) { throw Invalid(diagnosticPrefix, "string offset overflows"); }
            if (valueOffset < stringsBase || valueOffset >= stringsLimit) throw Invalid(diagnosticPrefix, "string offset is outside string data");
            result[index] = utf8 ? ReadUtf8(data, valueOffset, stringsLimit, diagnosticPrefix) : ReadUtf16(data, valueOffset, stringsLimit, diagnosticPrefix);
            decodedBytes = checked(decodedBytes + Encoding.UTF8.GetByteCount(result[index]));
            if (decodedBytes > maximumDecodedBytes) throw Invalid(diagnosticPrefix, $"decoded strings exceed byte quota {maximumDecodedBytes}");
        }
        return result;
    }

    internal static ushort U16(ReadOnlySpan<byte> data, int offset, string prefix)
    {
        if ((uint)offset > (uint)(data.Length - 2)) throw Invalid(prefix, "16-bit value is truncated");
        return BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
    }

    internal static uint U32(ReadOnlySpan<byte> data, int offset, string prefix)
    {
        if ((uint)offset > (uint)(data.Length - 4)) throw Invalid(prefix, "32-bit value is truncated");
        return BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    }

    internal static InvalidDataException Invalid(string prefix, string message, Exception? inner = null) =>
        new($"{prefix}: {message}", inner);

    private static int CheckedInt(uint value, string prefix, string field)
    {
        if (value > int.MaxValue) throw Invalid(prefix, field + " exceeds the supported range");
        return (int)value;
    }

    private static string ReadUtf8(ReadOnlySpan<byte> data, int offset, int limit, string prefix)
    {
        int utf16Length = ReadLength8(data, ref offset, limit, prefix);
        int byteLength = ReadLength8(data, ref offset, limit, prefix);
        int end;
        try { end = checked(offset + byteLength); } catch (OverflowException) { throw Invalid(prefix, "UTF-8 string length overflows"); }
        if (end >= limit || data[end] != 0) throw Invalid(prefix, "UTF-8 string is truncated or unterminated");

        // Android's ARSC string pools use Modified UTF-8 (CESU-8), not conformant
        // RFC 3629 UTF-8: supplementary code points are stored as two standalone
        // 3-byte sequences (one per UTF-16 surrogate half, lead bytes 0xED..0xEF)
        // and U+0000 as the overlong 0xC0 0x80. .NET's strict UTF-8 decoder rejects
        // the surrogate halves, so decode each code unit with the Modified UTF-8
        // formula and keep surrogate halves as raw chars: two consecutive chars form
        // the correct code point in a C# string, and the utf16Length check below
        // stays exact because Android counts surrogate halves separately too.
        // Valid 4-byte real UTF-8 (some non-Android tooling emits it) is also
        // accepted and expanded to its surrogate pair. Genuinely malformed bytes
        // (truncated sequences, invalid continuation bytes, stray/unknown leads)
        // still fail closed instead of producing replacement characters.
        var builder = new StringBuilder(utf16Length);
        int cursor = offset;
        while (cursor < end)
        {
            byte b0 = data[cursor];
            if ((b0 & 0x80) == 0)
            {
                builder.Append((char)b0);
                cursor += 1;
            }
            else if ((b0 & 0xE0) == 0xC0)
            {
                int cp = DecodeContinuations(data, cursor, end, 1, prefix, b0 & 0x1F);
                builder.Append((char)cp);
                cursor += 2;
            }
            else if ((b0 & 0xF0) == 0xE0)
            {
                int cp = DecodeContinuations(data, cursor, end, 2, prefix, b0 & 0x0F);
                builder.Append((char)cp);
                cursor += 3;
            }
            else if ((b0 & 0xF8) == 0xF0)
            {
                int cp = DecodeContinuations(data, cursor, end, 3, prefix, b0 & 0x07);
                if (cp < 0x10000 || cp > 0x10FFFF)
                    throw Invalid(prefix, "UTF-8 string contains an invalid supplementary code point");
                builder.Append((char)(0xD800 + ((cp - 0x10000) >> 10)));
                builder.Append((char)(0xDC00 + ((cp - 0x10000) & 0x3FF)));
                cursor += 4;
            }
            else
            {
                throw Invalid(prefix, "UTF-8 string contains an invalid lead byte");
            }
        }

        string value = builder.ToString();
        if (value.Length != utf16Length) throw Invalid(prefix, "UTF-8 length prefix does not match text");
        return value;
    }

    /// <summary>Decodes the continuation bytes of one Modified-UTF-8 sequence starting at a validated lead byte.</summary>
    private static int DecodeContinuations(ReadOnlySpan<byte> data, int start, int end, int count, string prefix, int initial)
    {
        if (start + count >= end) throw Invalid(prefix, "UTF-8 string contains a truncated sequence");
        int cp = initial;
        for (int i = 1; i <= count; i++)
        {
            byte next = data[start + i];
            if ((next & 0xC0) != 0x80) throw Invalid(prefix, "UTF-8 string contains an invalid continuation byte");
            cp = (cp << 6) | (next & 0x3F);
        }
        return cp;
    }

    private static string ReadUtf16(ReadOnlySpan<byte> data, int offset, int limit, string prefix)
    {
        int length = ReadLength16(data, ref offset, limit, prefix);
        int end;
        try { end = checked(offset + checked(length * 2)); } catch (OverflowException) { throw Invalid(prefix, "UTF-16 string length overflows"); }
        if (end > limit - 2 || U16(data, end, prefix) != 0) throw Invalid(prefix, "UTF-16 string is truncated or unterminated");
        try { return new UnicodeEncoding(false, false, true).GetString(data[offset..end]); }
        catch (DecoderFallbackException error) { throw Invalid(prefix, "UTF-16 string is invalid", error); }
    }

    private static int ReadLength8(ReadOnlySpan<byte> data, ref int offset, int limit, string prefix)
    {
        if (offset >= limit) throw Invalid(prefix, "UTF-8 length is truncated");
        int first = data[offset++];
        if ((first & 0x80) == 0) return first;
        if (offset >= limit) throw Invalid(prefix, "UTF-8 length is truncated");
        return ((first & 0x7f) << 8) | data[offset++];
    }

    private static int ReadLength16(ReadOnlySpan<byte> data, ref int offset, int limit, string prefix)
    {
        if (offset > limit - 2) throw Invalid(prefix, "UTF-16 length is truncated");
        int first = U16(data, offset, prefix); offset += 2;
        if ((first & 0x8000) == 0) return first;
        if (offset > limit - 2) throw Invalid(prefix, "UTF-16 length is truncated");
        int second = U16(data, offset, prefix); offset += 2;
        return ((first & 0x7fff) << 16) | second;
    }
}
