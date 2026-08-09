using System.Text;
using AndroidRuntime.Core.Apk;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Focused tests for the ARSC string pool Modified-UTF-8/CESU-8 decoder. Android's
/// ARSC UTF-8 pools store supplementary code points as two standalone 3-byte
/// sequences (one per UTF-16 surrogate half, lead byte 0xED..0xEF) and U+0000 as
/// 0xC0 0x80; a strict RFC 3629 decoder rejects those surrogate halves. These tests
/// prove the decoder round-trips CESU-8, still accepts real 4-byte UTF-8, and keeps
/// failing closed on genuinely malformed input.
/// </summary>
public sealed class AndroidBinaryFormatUtf8Tests
{
    private const string Prefix = "TEST_POOL";

    [Fact]
    public void Cesu8_surrogate_pair_round_trips_to_the_original_code_point()
    {
        // U+1F600 (grinning face) as CESU-8: high surrogate ED A0 BD + low surrogate ED B8 80.
        var pool = BuildPool((Utf16Length: 2, Payload: [0xED, 0xA0, 0xBD, 0xED, 0xB8, 0x80]));

        string value = ReadPool(pool)[0];

        Assert.Equal(2, value.Length);
        Assert.Equal(0x1F600, char.ConvertToUtf32(value, 0));
        Assert.Equal("\U0001F600", value);
    }

    [Fact]
    public void Cesu8_pairs_interleave_with_plain_bmp_and_ascii_text()
    {
        // "A😀B": 'A' (0x41), CESU-8 pair for U+1F600, 'B' (0x42). utf16Length counts
        // surrogate halves separately: 1 + 2 + 1 = 4.
        var pool = BuildPool((Utf16Length: 4, Payload: [0x41, 0xED, 0xA0, 0xBD, 0xED, 0xB8, 0x80, 0x42]));

        string value = ReadPool(pool)[0];

        Assert.Equal(4, value.Length);
        Assert.Equal("A\U0001F600B", value);
        Assert.Equal(0x1F600, char.ConvertToUtf32(value, 1));
    }

    [Fact]
    public void Modified_utf8_nul_sequence_decodes_to_a_nul_char()
    {
        var pool = BuildPool((Utf16Length: 1, Payload: [0xC0, 0x80]));

        Assert.Equal("\0", ReadPool(pool)[0]);
    }

    [Fact]
    public void Lead_byte_ed_with_second_byte_in_bmp_range_decodes_as_plain_bmp_char()
    {
        // 0xED 0x9F 0xBF decodes to U+D7FF — a valid BMP code point BELOW the surrogate
        // range; the generic 3-byte formula must not treat it as a surrogate half.
        var pool = BuildPool((Utf16Length: 1, Payload: [0xED, 0x9F, 0xBF]));

        Assert.Equal("\uD7FF", ReadPool(pool)[0]);
    }

    [Fact]
    public void Real_four_byte_utf8_supplementary_is_still_accepted()
    {
        // U+1F680 (rocket) as conformant 4-byte UTF-8: F0 9F 9A 80.
        var pool = BuildPool((Utf16Length: 2, Payload: [0xF0, 0x9F, 0x9A, 0x80]));

        string value = ReadPool(pool)[0];

        Assert.Equal(2, value.Length);
        Assert.Equal(0x1F680, char.ConvertToUtf32(value, 0));
    }

    [Fact]
    public void Plain_utf8_multibyte_text_still_decodes()
    {
        // "Rúntime" with 'ú' as C3 BA.
        byte[] payload = [0x52, 0xC3, 0xBA, 0x6E, 0x74, 0x69, 0x6D, 0x65];
        var pool = BuildPool((Utf16Length: 7, Payload: payload));

        Assert.Equal("Rúntime", ReadPool(pool)[0]);
    }

    [Fact]
    public void Truncated_sequence_fails_closed()
    {
        // 3-byte lead followed by only one continuation byte.
        var pool = BuildPool((Utf16Length: 1, Payload: [0xE0, 0xA0]));

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ReadPool(pool));

        Assert.StartsWith(Prefix + ":", error.Message, StringComparison.Ordinal);
        Assert.Contains("truncated", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Invalid_continuation_byte_fails_closed()
    {
        // Continuation byte must be 0x80-0xBF; 0x41 is not.
        var pool = BuildPool((Utf16Length: 1, Payload: [0xE0, 0x41, 0x80]));

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ReadPool(pool));

        Assert.Contains("continuation", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0x80)] // stray continuation byte as lead
    [InlineData(0xF8)] // 5-byte lead: not valid UTF-8 at all
    [InlineData(0xFF)]
    public void Invalid_lead_byte_fails_closed(int lead)
    {
        var pool = BuildPool((Utf16Length: 1, Payload: [(byte)lead]));

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ReadPool(pool));

        Assert.Contains("lead byte", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_four_byte_supplementary_fails_closed()
    {
        // F7 80 80 80 decodes beyond U+10FFFF — not a valid Unicode code point.
        var pool = BuildPool((Utf16Length: 2, Payload: [0xF7, 0x80, 0x80, 0x80]));

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ReadPool(pool));

        Assert.Contains("supplementary", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unterminated_string_fails_closed()
    {
        // No NUL terminator and no padding: the string region ends exactly at the pool
        // limit, so the terminator check must fail closed.
        var pool = BuildUnterminatedPool((Utf16Length: 4, Payload: [0x41, 0x42, 0x43, 0x44]));

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ReadPool(pool));

        Assert.Contains("unterminated", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Utf16_length_prefix_mismatch_fails_closed()
    {
        // Payload decodes to "ab" (Length 2) but the prefix claims 3 code units.
        var pool = BuildPool((Utf16Length: 3, Payload: [0x61, 0x62]));

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ReadPool(pool));

        Assert.Contains("length prefix", error.Message, StringComparison.Ordinal);
    }

    private static string[] ReadPool(byte[] pool) =>
        AndroidBinaryFormat.ReadStringPool(pool, new AndroidChunk(0x0001, 28, pool.Length, 0), Prefix);

    private static byte[] BuildPool(params (int Utf16Length, byte[] Payload)[] strings) =>
        BuildPoolCore(appendNull: true, strings);

    private static byte[] BuildUnterminatedPool(params (int Utf16Length, byte[] Payload)[] strings) =>
        BuildPoolCore(appendNull: false, strings);

    private static byte[] BuildPoolCore(bool appendNull, params (int Utf16Length, byte[] Payload)[] strings)
    {
        int count = strings.Length;
        int headerSize = 28;
        int stringsStart = headerSize + count * 4;
        using var data = new MemoryStream();
        using var writer = new BinaryWriter(data, Encoding.UTF8, leaveOpen: true);
        var offsets = new int[count];
        for (int i = 0; i < count; i++)
        {
            offsets[i] = (int)data.Position;
            WriteLen8(writer, strings[i].Utf16Length);
            WriteLen8(writer, strings[i].Payload.Length);
            writer.Write(strings[i].Payload);
            if (appendNull) writer.Write((byte)0);
        }
        if (appendNull)
            while (data.Length % 4 != 0) writer.Write((byte)0);

        int size = stringsStart + (int)data.Length;
        using var pool = new MemoryStream();
        using (var poolWriter = new BinaryWriter(pool, Encoding.UTF8, leaveOpen: true))
        {
            poolWriter.Write((ushort)0x0001);
            poolWriter.Write((ushort)headerSize);
            poolWriter.Write((uint)size);
            poolWriter.Write((uint)count);
            poolWriter.Write(0u);
            poolWriter.Write(0x100u); // UTF8_FLAG
            poolWriter.Write((uint)stringsStart);
            poolWriter.Write(0u);
            foreach (int offset in offsets) poolWriter.Write((uint)offset);
            poolWriter.Write(data.ToArray());
        }
        return pool.ToArray();
    }

    private static void WriteLen8(BinaryWriter writer, int value)
    {
        if (value < 0x80) writer.Write((byte)value);
        else { writer.Write((byte)((value >> 8) | 0x80)); writer.Write((byte)(value & 0xFF)); }
    }
}
