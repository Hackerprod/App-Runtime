#nullable enable
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace AndroidRuntime.Core.Dex;

public enum DexDiagnosticKind { Malformed, Unsupported }
public enum DexDiagnosticCode { InvalidMagic, InvalidVersion, InvalidHeader, InvalidFileSize, InvalidEndian, InvalidChecksum, InvalidSignature, InvalidMap, ParseFailure, InvalidInstruction, InvalidRegister, InvalidPoolIndex, InvalidBranch, InvalidMoveResult, BudgetExceeded }
public sealed record DexVerificationDiagnostic(DexDiagnosticKind Kind, DexDiagnosticCode Code, int Offset, string Message)
{
    public override string ToString() => $"{Kind}/{Code} at 0x{Offset:x}: {Message}";
}
public sealed record DexVerifierLimits(int MaxMethods = 100_000, int MaxInstructions = 10_000_000, int MaxFileBytes = 64 * 1024 * 1024)
{
    internal void Validate() { if (MaxMethods <= 0 || MaxInstructions <= 0 || MaxFileBytes < 0x70) throw new ArgumentOutOfRangeException(nameof(DexVerifierLimits)); }
}
public sealed record DexVerificationResult(bool IsValid, IReadOnlyList<DexVerificationDiagnostic> Diagnostics, int MethodsVerified, int InstructionsVerified);

/// <summary>Fail-closed structural verification for the bounded interpreter. This is not the ART type verifier.</summary>
public static class DexVerifier
{
    private const uint EndianConstant = 0x12345678;
    public static IReadOnlyList<byte> SupportedOpcodeMetadata => DexOpcodeTable.SupportedOpcodes;
    public static DexVerificationResult Verify(byte[] data, DexVerifierLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        limits ??= new(); limits.Validate();
        DexVerificationDiagnostic? header = ValidateHeader(data, limits);
        if (header is not null) return Invalid(header);
        DexFile dex;
        try { dex = DexReader.Parse(data); }
        catch (DexUnsupportedInstructionException error)
        { return Invalid(new(DexDiagnosticKind.Unsupported, DexDiagnosticCode.InvalidInstruction, 0, error.Message)); }
        catch (Exception error) when (error is FormatException or IndexOutOfRangeException or OverflowException or ArgumentOutOfRangeException)
        { return Invalid(new(DexDiagnosticKind.Malformed, DexDiagnosticCode.ParseFailure, 0, error.Message)); }

        int methods = 0, instructions = 0;
        foreach (DexClass cls in dex.Classes)
        foreach (DexEncodedMethod method in cls.AllMethods())
        {
            if (method.Code is null) continue;
            if (++methods > limits.MaxMethods) return Invalid(new(DexDiagnosticKind.Malformed, DexDiagnosticCode.BudgetExceeded, 0, "Method verification budget exceeded."), methods - 1, instructions);
            ushort[] words = method.Code.Instructions;
            var boundaries = new HashSet<int>();
            for (int pc = 0; pc < words.Length;)
            {
                boundaries.Add(pc);
                if (++instructions > limits.MaxInstructions) return Invalid(new(DexDiagnosticKind.Malformed, DexDiagnosticCode.BudgetExceeded, pc, "Instruction verification budget exceeded."), methods, instructions - 1);
                int width;
                try { width = DexReader.InstructionWidth(words, pc); }
                catch (Exception error) when (error is FormatException or OverflowException) { return Invalid(new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidInstruction, pc, error.Message), methods, instructions); }
                if (width <= 0 || pc > words.Length - width) return Invalid(new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidInstruction, pc, "Instruction extends beyond code_item."), methods, instructions);
                DexVerificationDiagnostic? instruction = ValidateInstruction(method, dex, pc);
                if (instruction is not null) return Invalid(instruction, methods, instructions);
                pc += width;
            }
            foreach (int pc in boundaries)
            {
                DexVerificationDiagnostic? flow = ValidateFlow(method, dex, pc, boundaries);
                if (flow is not null) return Invalid(flow, methods, instructions);
            }
        }
        return new(true, Array.Empty<DexVerificationDiagnostic>(), methods, instructions);
    }

    private static DexVerificationDiagnostic? ValidateHeader(byte[] data, DexVerifierLimits limits)
    {
        if (data.Length < 0x70 || data.Length > limits.MaxFileBytes) return new(DexDiagnosticKind.Malformed, data.Length > limits.MaxFileBytes ? DexDiagnosticCode.BudgetExceeded : DexDiagnosticCode.InvalidHeader, 0, "DEX file size is outside the configured bounds.");
        if (!data.AsSpan(0, 4).SequenceEqual("dex\n"u8)) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidMagic, 0, "DEX magic is invalid.");
        if (data[7] != 0 || data[4] is < (byte)'0' or > (byte)'9' || data[5] is < (byte)'0' or > (byte)'9' || data[6] is < (byte)'0' or > (byte)'9') return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidVersion, 4, "DEX version bytes are invalid.");
        int version = (data[4] - '0') * 100 + (data[5] - '0') * 10 + data[6] - '0';
        if (version is < 35 or > 41) return new(DexDiagnosticKind.Unsupported, DexDiagnosticCode.InvalidVersion, 4, $"DEX version {version:000} is valid-looking but unsupported.");
        if (U32(data, 32) != data.Length) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidFileSize, 32, "Header file_size does not match input length.");
        if (U32(data, 36) != 0x70) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidHeader, 36, "DEX header_size must be 0x70.");
        if (U32(data, 40) != EndianConstant) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidEndian, 40, "Only standard little-endian DEX is supported.");
        Span<byte> signature = stackalloc byte[20]; SHA1.HashData(data.AsSpan(32), signature);
        if (!data.AsSpan(12, 20).SequenceEqual(signature)) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidSignature, 12, "DEX SHA-1 signature does not match bytes 32..end.");
        if (U32(data, 8) != Adler32(data.AsSpan(12))) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidChecksum, 8, "DEX Adler-32 checksum does not match bytes 12..end.");
        foreach ((int sizeOffset, int itemSize, string name) in new[] { (56, 4, "string_ids"), (64, 4, "type_ids"), (72, 12, "proto_ids"), (80, 8, "field_ids"), (88, 8, "method_ids"), (96, 32, "class_defs") })
        {
            uint size = U32(data, sizeOffset), offset = U32(data, sizeOffset + 4);
            if ((size == 0) != (offset == 0) || offset > data.Length || (long)size * itemSize > data.Length - (long)offset)
                return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidHeader, sizeOffset, name + " range is inconsistent or outside the file.");
        }
        uint dataSize = U32(data, 104), dataOff = U32(data, 108);
        if (dataOff > data.Length || dataSize > data.Length - (long)dataOff) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidHeader, 104, "data section exceeds file bounds.");
        uint map = U32(data, 52); if (map == 0 || map > data.Length - 4) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidMap, 52, "map_off is missing or outside the file.");
        uint count = U32(data, (int)map); if (count > 65_536 || map + 4L + count * 12L > data.Length) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidMap, (int)map, "map_list exceeds file bounds or quota.");
        var seenTypes = new HashSet<ushort>(); uint previousOffset = 0;
        for (int index = 0; index < count; index++)
        {
            int item = checked((int)map + 4 + index * 12); ushort type = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(item, 2)); ushort unused = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(item + 2, 2)); uint size = U32(data, item + 4), offset = U32(data, item + 8);
            if (!KnownMapType(type) || unused != 0 || size == 0 || !seenTypes.Add(type)) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidMap, item, "map_item has an unknown/duplicate type, nonzero unused field, or zero size.");
            if (offset >= data.Length || index > 0 && offset <= previousOffset) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidMap, item + 8, "map_item offsets must be strictly increasing and inside the file.");
            int fixedSize = FixedMapItemSize(type); if (fixedSize != 0 && (long)size * fixedSize > data.Length - (long)offset) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidMap, item + 4, "map_item fixed-size range exceeds the file.");
            if (type == 0x0000 && (size != 1 || offset != 0) || type == 0x1000 && (size != 1 || offset != map)) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidMap, item, "header/map_list map_item identity is inconsistent.");
            if (type is >= 0x0001 and <= 0x0006)
            {
                int headerSizeOffset = 56 + (type - 1) * 8;
                if (size != U32(data, headerSizeOffset) || offset != U32(data, headerSizeOffset + 4)) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidMap, item, "map_item does not match its header section identity.");
            }
            if (type >= 0x1000 && offset < dataOff) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidMap, item + 8, "data map_item begins before data_off.");
            previousOffset = offset;
        }
        if (!seenTypes.Contains(0x0000) || !seenTypes.Contains(0x1000)) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidMap, (int)map, "map_list must contain header and map_list identities.");
        for (ushort type = 1; type <= 6; type++) if (U32(data, 56 + (type - 1) * 8) != 0 && !seenTypes.Contains(type)) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidMap, (int)map, "map_list omits a non-empty header section.");
        return null;
    }

    private static bool KnownMapType(ushort type) => type is <= 0x0008 or >= 0x1000 and <= 0x1003 or >= 0x2000 and <= 0x2006;
    private static int FixedMapItemSize(ushort type) => type switch { 0x0000 => 0x70, 0x0001 => 4, 0x0002 => 4, 0x0003 => 12, 0x0004 => 8, 0x0005 => 8, 0x0006 => 32, 0x0007 => 4, 0x0008 => 8, 0x1000 => 12, _ => 0 };

    private static DexVerificationDiagnostic? ValidateInstruction(DexEncodedMethod method, DexFile dex, int pc)
    {
        ushort[] words = method.Code.Instructions; int op = words[pc] & 0xff; int a = words[pc] >> 8;
        if (!DexOpcodeTable.TryGetFormat(words[pc], out DexInstructionFormat format))
            return new(DexDiagnosticKind.Unsupported, DexDiagnosticCode.InvalidInstruction, pc, $"Opcode 0x{op:x2} has no verifier metadata and is unsupported.");
        DexVerificationDiagnostic? registers = ValidateRegisters(method.Code.RegistersSize, words, pc, op, format);
        if (registers is not null) return registers;
        if (op is 0x0a or 0x0b or 0x0c)
        {
            if (pc == 0) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidMoveResult, pc, "move-result must immediately follow invoke/fill-new-array.");
            int previous = PreviousPc(words, pc); int previousOp = words[previous] & 0xff;
            if (previousOp is not (>= 0x6e and <= 0x72) and not (>= 0x74 and <= 0x78) and not 0x24 and not 0x25)
                return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidMoveResult, pc, "move-result must immediately follow a result-producing instruction.");
            if (previousOp is 0x24 or 0x25)
            {
                if (op != 0x0c) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidMoveResult, pc, "fill-new-array requires move-result-object.");
            }
            else
            {
                string result = dex.Methods[words[previous + 1]].Proto.ReturnType;
                int expected = result switch { "V" => -1, "J" or "D" => 0x0b, _ when result.StartsWith("L", StringComparison.Ordinal) || result.StartsWith("[", StringComparison.Ordinal) => 0x0c, _ => 0x0a };
                if (op != expected) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidMoveResult, pc, $"move-result kind does not match invoke return descriptor {result}.");
            }
        }
        if (op == 0x1a && words[pc + 1] >= dex.Strings.Count || op == 0x1b && ((uint)words[pc + 1] | (uint)words[pc + 2] << 16) >= dex.Strings.Count) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidPoolIndex, pc, "String index is out of range.");
        if (op is 0x1c or 0x1f or 0x20 or 0x22 or 0x23 or 0x24 or 0x25 && words[pc + 1] >= dex.TypeDescriptors.Count) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidPoolIndex, pc, "Type index is out of range.");
        if (op is >= 0x52 and <= 0x6d && words[pc + 1] >= dex.Fields.Count) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidPoolIndex, pc, "Field index is out of range.");
        if ((op is >= 0x6e and <= 0x72 || op is >= 0x74 and <= 0x78) && words[pc + 1] >= dex.Methods.Count) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidPoolIndex, pc, "Method index is out of range.");
        return null;
    }

    private static DexVerificationDiagnostic? ValidateRegisters(int count, ushort[] words, int pc, int op, DexInstructionFormat format)
    {
        bool Register(int value) => (uint)value < count;
        bool Pair(int value) => value >= 0 && value < count - 1;
        int first = words[pc], hi = first >> 8, a4 = (first >> 8) & 0xf, b4 = (first >> 12) & 0xf;
        int a = -1, b = -1, c = -1;
        bool valid = format switch
        {
            DexInstructionFormat.Payload or DexInstructionFormat.F10x or DexInstructionFormat.F10t or DexInstructionFormat.F20t or DexInstructionFormat.F30t => true,
            DexInstructionFormat.F11x or DexInstructionFormat.F21 or DexInstructionFormat.F31 or DexInstructionFormat.F51 or DexInstructionFormat.F21t => Register(a = hi),
            DexInstructionFormat.F11n => Register(a = a4),
            DexInstructionFormat.F12x => Register(a = a4) && Register(b = b4),
            DexInstructionFormat.F22x => Register(a = hi) && Register(b = words[pc + 1]),
            DexInstructionFormat.F32x => Register(a = words[pc + 1]) && Register(b = words[pc + 2]),
            DexInstructionFormat.F22c or DexInstructionFormat.F22t or DexInstructionFormat.F22s => Register(a = a4) && Register(b = b4),
            DexInstructionFormat.F23x => Register(a = hi) && Register(b = words[pc + 1] & 0xff) && Register(c = words[pc + 1] >> 8),
            DexInstructionFormat.F22b => Register(a = hi) && Register(b = words[pc + 1] & 0xff),
            DexInstructionFormat.F35c => Validate35cRegisters(words, pc, count),
            DexInstructionFormat.F3rc => hi <= count && words[pc + 2] <= count - hi,
            _ => false
        };
        if (!valid) return InvalidRegister(pc, "Register operand exceeds registers_size.");

        bool wideValid = op switch
        {
            0x04 or 0x7d or 0x7e or 0x80 or 0x86 or >= 0xbb and <= 0xc2 or >= 0xcb and <= 0xcf => Pair(a) && Pair(b),
            0x05 => Pair(a) && Pair(b),
            0x06 => Pair(a) && Pair(b),
            0x0b or 0x10 or >= 0x16 and <= 0x19 or 0x61 or 0x68 => Pair(a),
            0x2f or 0x30 or 0x31 => Pair(b) && Pair(c),
            0x45 or 0x4c or 0x53 or 0x5a => Pair(a),
            0x7f or 0x83 or 0x88 or 0x89 => Pair(a),
            0x81 => Pair(a),
            0x84 or 0x85 or 0x8a or 0x8c => Pair(b),
            0x8b => Pair(a) && Pair(b),
            >= 0x9b and <= 0xa2 or >= 0xab and <= 0xaf => Pair(a) && Pair(b) && Pair(c),
            >= 0xa3 and <= 0xa5 => Pair(a) && Pair(b),
            >= 0xc3 and <= 0xc5 => Pair(a),
            _ => true
        };
        return wideValid ? null : InvalidRegister(pc, "Wide register operand exceeds the register pair boundary.");
    }

    private static bool Validate35cRegisters(ushort[] words, int pc, int count)
    {
        int argumentWords = (words[pc] >> 12) & 0xf;
        if (argumentWords > 5) return false;
        int second = words[pc + 2];
        Span<int> registers = stackalloc int[5] { second & 0xf, (second >> 4) & 0xf, (second >> 8) & 0xf, (second >> 12) & 0xf, (words[pc] >> 8) & 0xf };
        for (int index = 0; index < argumentWords; index++) if ((uint)registers[index] >= count) return false;
        return true;
    }

    private static DexVerificationDiagnostic InvalidRegister(int pc, string message) => new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidRegister, pc, message);

    private static DexVerificationDiagnostic? ValidateFlow(DexEncodedMethod method, DexFile dex, int pc, HashSet<int> boundaries)
    {
        ushort[] words = method.Code.Instructions; int op = words[pc] & 0xff; int? target = op switch
        {
            0x28 => pc + unchecked((sbyte)(words[pc] >> 8)),
            0x29 => pc + unchecked((short)words[pc + 1]),
            0x2a => pc + unchecked((int)((uint)words[pc + 1] | (uint)words[pc + 2] << 16)),
            0x26 or 0x2b or 0x2c => pc + unchecked((int)((uint)words[pc + 1] | (uint)words[pc + 2] << 16)),
            >= 0x32 and <= 0x3d => pc + unchecked((short)words[pc + 1]),
            _ => null
        };
        if (target is int branch && !boundaries.Contains(branch)) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidBranch, pc, "Branch target is outside the method or not instruction-aligned.");
        // Branch-to-payload instructions must land specifically on a payload of the
        // matching kind (0x0100 packed-switch-payload for 0x2b, 0x0200
        // sparse-switch-payload for 0x2c, 0x0300 fill-array-data-payload for 0x26) —
        // "target is a valid boundary" is not enough, pointing at ordinary code or at
        // the wrong payload shape is malformed and must fail closed here rather than
        // be misread by the interpreter later.
        if (op is 0x26 or 0x2b or 0x2c)
        {
            int expectedIdent = op switch { 0x2b => 0x0100, 0x2c => 0x0200, _ => 0x0300 };
            if (target is not int payload || words[payload] != expectedIdent)
                return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidBranch, pc, "Branch target is not the matching payload kind.");
        }
        if (op is >= 0x6e and <= 0x72 or >= 0x74 and <= 0x78)
        {
            DexMethodRef called = dex.Methods[words[pc + 1]];
            int expected = called.Proto.ParameterTypes.Sum(WordCount) + (op is 0x71 or 0x77 ? 0 : 1);
            int actual = op <= 0x72 ? (words[pc] >> 12) & 0xf : words[pc] >> 8;
            if (actual != expected) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidInstruction, pc, $"Invoke word count {actual} does not match descriptor word count {expected}.");
            if (actual > method.Code.OutsSize) return new(DexDiagnosticKind.Malformed, DexDiagnosticCode.InvalidInstruction, pc, "Invoke word count exceeds outs_size.");
        }
        return null;
    }

    private static int WordCount(string descriptor) => descriptor is "J" or "D" ? 2 : 1;

    private static int PreviousPc(ushort[] words, int target) { int pc = 0, previous = -1; while (pc < target) { previous = pc; pc += DexReader.InstructionWidth(words, pc); } return pc == target ? previous : -1; }
    private static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    private static uint Adler32(ReadOnlySpan<byte> data) { const uint modulus = 65521; uint a = 1, b = 0; foreach (byte value in data) { a = (a + value) % modulus; b = (b + a) % modulus; } return b << 16 | a; }
    private static DexVerificationResult Invalid(DexVerificationDiagnostic diagnostic, int methods = 0, int instructions = 0) => new(false, new[] { diagnostic }, methods, instructions);
}
