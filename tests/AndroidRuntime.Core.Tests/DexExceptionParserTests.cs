using System.Reflection;
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.Tests;

public sealed class DexExceptionParserTests
{
    [Fact]
    public void Code_item_parses_odd_padding_try_and_typed_handler()
    {
        byte[] data = CodeItem([0x0012, 0x000f, 0x000d], [(0u, (ushort)1, (ushort)1)], [1, 1, 0, 2]);

        DexCodeItem code = Parse(data);

        DexTryBlock block = Assert.Single(code.TryBlocks);
        Assert.Equal(0, block.StartAddress);
        Assert.Equal(1, block.InstructionCount);
        DexExceptionHandler handler = Assert.Single(block.Handlers);
        Assert.Equal("Ljava/lang/ArithmeticException;", handler.TypeDescriptor);
        Assert.Equal(2, handler.TargetAddress);
    }

    [Fact]
    public void Code_item_rejects_handler_offset_overlap_bad_target_and_truncated_sleb()
    {
        AssertFormat(CodeItem([0x0012, 0x000f, 0x000d], [(0u, (ushort)1, (ushort)9)], [1, 1, 0, 2]));
        AssertFormat(CodeItem([0x0012, 0x000f, 0x000d], [(0u, (ushort)2, (ushort)1), (1u, (ushort)1, (ushort)1)], [1, 1, 0, 2]));
        AssertFormat(CodeItem([0x0012, 0x000f, 0x000d], [(0u, (ushort)1, (ushort)1)], [1, 1, 0, 1]));
        AssertFormat(CodeItem([0x0012, 0x000f, 0x000d], [(0u, (ushort)1, (ushort)1)], [1, 0x80]));
    }

    [Fact]
    public void Code_item_rejects_try_and_handler_quotas()
    {
        byte[] tooManyTries = new byte[16];
        WriteU16(tooManyTries, 6, 1025);
        AssertFormat(tooManyTries);
        AssertFormat(CodeItem([0x000d], [(0u, (ushort)1, (ushort)1)], [0x81, 0x20]));
    }

    [Fact]
    public void Code_item_rejects_nonzero_padding_and_invalid_fifth_leb_bytes()
    {
        byte[] padding = CodeItem([0x0012, 0x000f, 0x000d], [(0u, (ushort)1, (ushort)1)], [1, 1, 0, 2]);
        padding[16 + 3 * 2] = 1;
        AssertFormat(padding);
        AssertFormat(CodeItem([0x000d], [(0u, (ushort)1, (ushort)1)], [0x81, 0x80, 0x80, 0x80, 0x10]));
        AssertFormat(CodeItem([0x000d], [(0u, (ushort)1, (ushort)1)], [1, 0x81, 0x80, 0x80, 0x80, 0x08]));
    }

    private static DexCodeItem Parse(byte[] data)
    {
        var dex = new DexFile();
        dex.TypeDescriptors.Add("Ljava/lang/ArithmeticException;");
        MethodInfo method = typeof(DexReader).GetMethod("ParseCodeItem", BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return (DexCodeItem)method.Invoke(null, [data, 0, dex])!; }
        catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
    }

    private static void AssertFormat(byte[] data) => Assert.Throws<FormatException>(() => Parse(data));

    private static byte[] CodeItem(ushort[] instructions, (uint Start, ushort Count, ushort HandlerOffset)[] tries, byte[] handlers)
    {
        int padding = instructions.Length % 2;
        int triesOffset = 16 + instructions.Length * 2 + padding * 2;
        byte[] data = new byte[triesOffset + tries.Length * 8 + handlers.Length];
        WriteU16(data, 0, 2); WriteU16(data, 6, (ushort)tries.Length); WriteU32(data, 12, (uint)instructions.Length);
        for (int i = 0; i < instructions.Length; i++) WriteU16(data, 16 + i * 2, instructions[i]);
        for (int i = 0; i < tries.Length; i++)
        {
            int offset = triesOffset + i * 8;
            WriteU32(data, offset, tries[i].Start); WriteU16(data, offset + 4, tries[i].Count); WriteU16(data, offset + 6, tries[i].HandlerOffset);
        }
        handlers.CopyTo(data, triesOffset + tries.Length * 8);
        return data;
    }

    private static void WriteU16(byte[] data, int offset, ushort value) => BitConverter.GetBytes(value).CopyTo(data, offset);
    private static void WriteU32(byte[] data, int offset, uint value) => BitConverter.GetBytes(value).CopyTo(data, offset);
}
