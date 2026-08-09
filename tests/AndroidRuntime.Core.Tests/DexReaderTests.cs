using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.Tests;

public sealed class DexReaderTests
{
    [Fact]
    public void Parse_rejects_a_truncated_dex_with_a_format_error()
    {
        byte[] truncated = new byte[0x70];
        "dex\n035\0"u8.CopyTo(truncated);
        BitConverter.GetBytes(1u).CopyTo(truncated, 56);
        BitConverter.GetBytes(0x70u).CopyTo(truncated, 60);

        var error = Assert.Throws<FormatException>(() => DexReader.Parse(truncated));

        Assert.Contains("trunc", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
