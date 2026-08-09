#nullable enable
namespace AndroidRuntime.Core.Dex;

internal sealed class DexUnsupportedInstructionException : FormatException
{
    internal DexUnsupportedInstructionException(int opcode) : base($"Unsupported DEX opcode 0x{opcode:x2} has no decoder metadata.") { Opcode = opcode; }
    internal int Opcode { get; }
}

internal enum DexInstructionFormat { Payload, F10x, F12x, F11x, F11n, F21, F31, F51, F22x, F32x, F22c, F22t, F21t, F23x, F10t, F20t, F30t, F35c, F3rc, F22s, F22b }

internal static class DexOpcodeTable
{
    internal static bool TryGetFormat(ushort firstUnit, out DexInstructionFormat format)
    {
        int opcode = firstUnit & 0xff;
        if (opcode == 0 && firstUnit != 0) { format = DexInstructionFormat.Payload; return firstUnit >> 8 is 1 or 2 or 3; }
        format = opcode switch
        {
            0x00 or 0x0e => DexInstructionFormat.F10x,
            0x01 or 0x04 or 0x07 or >= 0x7b and <= 0x8f or >= 0xb0 and <= 0xcf => DexInstructionFormat.F12x,
            0x02 or 0x05 or 0x08 => DexInstructionFormat.F22x,
            0x03 or 0x06 => DexInstructionFormat.F32x,
            >= 0x0a and <= 0x0d or 0x0f or 0x10 or 0x11 or 0x1d or 0x1e or 0x27 => DexInstructionFormat.F11x,
            0x12 => DexInstructionFormat.F11n,
            0x13 or 0x15 or 0x16 or 0x19 or 0x1a or 0x1c or 0x1f or 0x22 or >= 0x60 and <= 0x6c => DexInstructionFormat.F21,
            0x14 or 0x17 or 0x1b or 0x26 or 0x2b or 0x2c => DexInstructionFormat.F31,
            0x18 => DexInstructionFormat.F51,
            0x20 or 0x23 or >= 0x52 and <= 0x5f => DexInstructionFormat.F22c,
            0x21 => DexInstructionFormat.F12x,
            0x28 => DexInstructionFormat.F10t,
            0x29 => DexInstructionFormat.F20t,
            >= 0x2d and <= 0x31 or >= 0x44 and <= 0x51 or >= 0x90 and <= 0xaf => DexInstructionFormat.F23x,
            >= 0x32 and <= 0x37 => DexInstructionFormat.F22t,
            >= 0x38 and <= 0x3d => DexInstructionFormat.F21t,
            >= 0x6e and <= 0x72 or 0x24 => DexInstructionFormat.F35c,
            >= 0x74 and <= 0x78 or 0x25 => DexInstructionFormat.F3rc,
            >= 0xd0 and <= 0xd7 => DexInstructionFormat.F22s,
            >= 0xd8 and <= 0xe2 => DexInstructionFormat.F22b,
            _ => default
        };
        return opcode is 0x00 or 0x01 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or
            >= 0x0a and <= 0x29 or 0x2b or 0x2c or
            >= 0x2d and <= 0x3d or >= 0x44 and <= 0x5f or >= 0x60 and <= 0x6c or >= 0x6e and <= 0x72 or >= 0x74 and <= 0x78 or
            >= 0x7b and <= 0x8f or >= 0x90 and <= 0xcf or >= 0xd0 and <= 0xe2;
    }

    internal static IReadOnlyList<byte> SupportedOpcodes { get; } = Enumerable.Range(0, 256).Where(value => TryGetFormat((ushort)value, out _)).Select(value => (byte)value).ToArray();
}
