using AndroidRuntime.Core.Apk;
using AndroidRuntime.Core.Dex;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace AndroidRuntime.Core.Tests;

public sealed class DexVerifierTests
{
    [Fact]
    public void Real_fixture_is_structurally_verified()
    {
        byte[] dex = ApkLoader.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "RuntimeProbe.apk")).ClassesDexFiles[0];
        DexVerificationResult result = DexVerifier.Verify(dex);
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.True(result.MethodsVerified > 0);
        Assert.True(result.InstructionsVerified > 0);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(36, 0)]
    [InlineData(40, 0)]
    public void Header_mutations_fail_with_stable_malformed_diagnostic(int offset, byte value)
    {
        byte[] dex = ApkLoader.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "RuntimeProbe.apk")).ClassesDexFiles[0];
        dex[offset] = value;
        DexVerificationResult result = DexVerifier.Verify(dex);
        Assert.False(result.IsValid);
        Assert.Equal(DexDiagnosticKind.Malformed, result.Diagnostics[0].Kind);
    }

    [Fact]
    public void Budget_exhaustion_is_fail_closed_and_deterministic()
    {
        byte[] dex = ApkLoader.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "RuntimeProbe.apk")).ClassesDexFiles[0];
        var result = DexVerifier.Verify(dex, new DexVerifierLimits(MaxMethods: 1, MaxInstructions: 1, MaxFileBytes: dex.Length));
        Assert.False(result.IsValid);
        Assert.Equal(DexDiagnosticCode.BudgetExceeded, result.Diagnostics[0].Code);
    }

    [Fact]
    public void Seeded_header_mutation_corpus_never_throws_and_is_reproducible()
    {
        byte[] original = ApkLoader.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "RuntimeProbe.apk")).ClassesDexFiles[0];
        static string[] Run(byte[] source)
        {
            var random = new Random(0x5eed); var output = new List<string>();
            for (int i = 0; i < 128; i++)
            {
                byte[] mutated = (byte[])source.Clone(); int offset = random.Next(0x70); mutated[offset] ^= (byte)(1 << random.Next(8));
                DexVerificationResult result = DexVerifier.Verify(mutated); output.Add(result.IsValid ? "valid" : result.Diagnostics[0].Code.ToString());
            }
            return output.ToArray();
        }
        Assert.Equal(Run(original), Run(original));
    }

    [Fact]
    public void Const4_out_of_range_register_is_rejected_before_execution()
    {
        byte[] original = ApkLoader.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "RuntimeProbe.apk")).ClassesDexFiles[0];
        DexVerificationResult? rejected = null;
        for (int offset = 0; offset + 1 < original.Length; offset += 2)
        {
            if (original[offset] != 0x12) continue;
            byte[] mutated = (byte[])original.Clone(); mutated[offset + 1] = (byte)((mutated[offset + 1] & 0xf0) | 0x0f);
            RecomputeIntegrity(mutated); DexVerificationResult result = DexVerifier.Verify(mutated);
            if (!result.IsValid && result.Diagnostics[0].Code == DexDiagnosticCode.InvalidRegister) { rejected = result; break; }
        }
        Assert.NotNull(rejected);
        Assert.Equal(DexDiagnosticKind.Malformed, rejected!.Diagnostics[0].Kind);
    }

    [Fact]
    public void Unknown_opcode_and_unknown_map_type_are_fail_closed()
    {
        byte[] original = ApkLoader.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "RuntimeProbe.apk")).ClassesDexFiles[0];
        uint map = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(52, 4));
        byte[] badMap = (byte[])original.Clone(); badMap[map + 4] = 0xff; badMap[map + 5] = 0xff; RecomputeIntegrity(badMap);
        Assert.Equal(DexDiagnosticCode.InvalidMap, DexVerifier.Verify(badMap).Diagnostics[0].Code);

        DexVerificationResult? unsupported = null;
        for (int offset = 0; offset < original.Length; offset += 2)
        {
            byte[] mutated = (byte[])original.Clone(); mutated[offset] = 0xff;
            RecomputeIntegrity(mutated); DexVerificationResult result = DexVerifier.Verify(mutated);
            if (!result.IsValid && result.Diagnostics[0].Kind == DexDiagnosticKind.Unsupported && result.Diagnostics[0].Code == DexDiagnosticCode.InvalidInstruction) { unsupported = result; break; }
        }
        Assert.NotNull(unsupported);
    }

    [Fact]
    public void Every_interpreter_opcode_has_explicit_verifier_metadata()
    {
        byte[] expected = Expand("00-08,0a-29,2b-2c,2d-3d,44-5f,60-6c,6e-72,74-78,7b-cf,d0-e2");
        Assert.Equal(expected, DexVerifier.SupportedOpcodeMetadata);
    }

    [Theory]
    [InlineData(8, DexDiagnosticCode.InvalidChecksum)]
    [InlineData(12, DexDiagnosticCode.InvalidSignature)]
    public void Dex_integrity_fields_are_verified(int offset, DexDiagnosticCode expected)
    {
        byte[] dex = ApkLoader.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "RuntimeProbe.apk")).ClassesDexFiles[0];
        dex[offset] ^= 1;
        Assert.Equal(expected, DexVerifier.Verify(dex).Diagnostics[0].Code);
    }

    private static byte[] Expand(string specification)
    {
        var result = new List<byte>();
        foreach (string part in specification.Split(','))
        {
            string[] bounds = part.Split('-'); int first = Convert.ToInt32(bounds[0], 16), last = bounds.Length == 1 ? first : Convert.ToInt32(bounds[1], 16);
            for (int value = first; value <= last; value++) result.Add((byte)value);
        }
        return result.ToArray();
    }

    [Fact]
    public void Switch_instructions_with_matching_payloads_verify()
    {
        // packed-switch v0, +4 -> payload at pc 4 (ident 0x0100, size 2, first_key 1).
        ushort[] packed = [0x002B, 0x0004, 0x0000, 0x000e, 0x0100, 0x0002, 0x0001, 0x0000, 0x0003, 0x0000, 0x0003, 0x0000];
        Assert.True(DexVerifier.Verify(BuildSwitchDex(packed)).IsValid);

        // sparse-switch v0, +4 -> payload at pc 4 (ident 0x0200, size 1, key 7, target 3).
        ushort[] sparse = [0x002C, 0x0004, 0x0000, 0x000e, 0x0200, 0x0001, 0x0007, 0x0000, 0x0003, 0x0000];
        Assert.True(DexVerifier.Verify(BuildSwitchDex(sparse)).IsValid);
    }

    [Fact]
    public void Switch_pointing_at_wrong_payload_kind_fails_closed()
    {
        // packed-switch offset lands on a boundary, but the payload ident is the
        // sparse one (0x0200) — the wrong shape must fail, not pass as "any boundary".
        ushort[] instructions = [0x002B, 0x0004, 0x0000, 0x000e, 0x0200, 0x0001, 0x0007, 0x0000, 0x0003, 0x0000];

        DexVerificationResult result = DexVerifier.Verify(BuildSwitchDex(instructions));

        Assert.False(result.IsValid);
        Assert.Equal(DexDiagnosticCode.InvalidBranch, result.Diagnostics[0].Code);
    }

    [Fact]
    public void Switch_offset_outside_method_fails_closed()
    {
        ushort[] instructions = [0x002B, 0x0064, 0x0000, 0x000e, 0x0100, 0x0002, 0x0001, 0x0000, 0x0003, 0x0000, 0x0003, 0x0000];

        DexVerificationResult result = DexVerifier.Verify(BuildSwitchDex(instructions));

        Assert.False(result.IsValid);
        Assert.Equal(DexDiagnosticCode.InvalidBranch, result.Diagnostics[0].Code);
    }

    [Fact]
    public void Fill_array_data_with_matching_payload_verifies()
    {
        // fill-array-data v0, +4 -> payload at pc 4 (ident 0x0300, width 4, size 1).
        ushort[] instructions = [0x0026, 0x0004, 0x0000, 0x000e, 0x0300, 0x0004, 0x0001, 0x0000, 0x0100, 0x0000];

        Assert.True(DexVerifier.Verify(BuildSwitchDex(instructions)).IsValid);
    }

    [Fact]
    public void Fill_array_data_pointing_at_wrong_payload_kind_fails_closed()
    {
        // fill-array-data offset lands on a boundary, but the payload ident is the
        // packed-switch one (0x0100, size 1 so it fits the method) — the wrong shape
        // must fail the kind check, not pass as "any boundary".
        ushort[] instructions = [0x0026, 0x0004, 0x0000, 0x000e, 0x0100, 0x0001, 0x0001, 0x0000, 0x0003, 0x0000];

        DexVerificationResult result = DexVerifier.Verify(BuildSwitchDex(instructions));

        Assert.False(result.IsValid);
        Assert.Equal(DexDiagnosticCode.InvalidBranch, result.Diagnostics[0].Code);
    }

    /// <summary>Builds a complete, integrity-correct minimal DEX with one class
    /// LTest; and one static run()V method whose code_item is the given instructions.
    /// Layout: header 0x70, string_ids (3), type_ids (2), proto_ids (1), method_ids (1),
    /// class_defs (1), then data: string_data, code_item, class_data, map_list.</summary>
    private static byte[] BuildSwitchDex(ushort[] instructions)
    {
        const int dataOff = 0xB8;
        string[] strings = ["LTest;", "run", "V"];
        var stringData = new List<byte>();
        int[] stringOffsets = new int[3];
        for (int i = 0; i < strings.Length; i++)
        {
            stringOffsets[i] = stringData.Count;
            WriteUleb(stringData, strings[i].Length);
            foreach (byte b in System.Text.Encoding.UTF8.GetBytes(strings[i])) stringData.Add(b);
            stringData.Add(0);
        }
        while (stringData.Count % 4 != 0) stringData.Add(0);

        int codeItemOff = dataOff + stringData.Count;
        int codeItemLen = 16 + instructions.Length * 2;

        var classData = new List<byte>();
        WriteUleb(classData, 0); WriteUleb(classData, 0); WriteUleb(classData, 1); WriteUleb(classData, 0);
        WriteUleb(classData, 0); WriteUleb(classData, 0x8);
        WriteUleb(classData, codeItemOff);
        int classDataOff = codeItemOff + codeItemLen;
        while ((classDataOff + classData.Count) % 4 != 0) classData.Add(0);

        int mapOff = classDataOff + classData.Count;
        var data = new List<byte>(stringData);
        WriteU16(data, 1);              // code_item registers_size
        WriteU16(data, 0);              // ins_size
        WriteU16(data, 0);              // outs_size
        WriteU16(data, 0);              // tries_size
        WriteU32(data, 0);              // debug_info_off
        WriteU32(data, (uint)instructions.Length);
        foreach (ushort unit in instructions) WriteU16(data, unit);
        data.AddRange(classData);
        WriteU32(data, 10);
        WriteMap(data, 0x0000, 1, 0);
        WriteMap(data, 0x0001, 3, 0x70);
        WriteMap(data, 0x0002, 2, 0x7C);
        WriteMap(data, 0x0003, 1, 0x84);
        WriteMap(data, 0x0005, 1, 0x90);
        WriteMap(data, 0x0006, 1, 0x98);
        WriteMap(data, 0x1001, 3, (uint)dataOff);
        WriteMap(data, 0x2001, 1, (uint)codeItemOff);
        WriteMap(data, 0x2000, 1, (uint)classDataOff);
        WriteMap(data, 0x1000, 1, (uint)mapOff);

        int fileSize = dataOff + data.Count;
        byte[] dex = new byte[fileSize];
        data.CopyTo(dex, dataOff);
        for (int i = 0; i < 3; i++) WriteU32(dex, 0x70 + i * 4, (uint)(dataOff + stringOffsets[i]));
        WriteU32(dex, 0x7C, 0); WriteU32(dex, 0x80, 1); // type_ids: LTest;, V
        WriteU32(dex, 0x84, 2); WriteU32(dex, 0x88, 1); WriteU32(dex, 0x8C, 0); // proto: shorty "V", return V, no params
        WriteU16(dex, 0x90, 0); WriteU16(dex, 0x92, 0); WriteU32(dex, 0x94, 1); // method_ids: class 0, proto 0, name "run"
        WriteU32(dex, 0x98, 0);             // class_defs: class 0
        WriteU32(dex, 0x9C, 0);             // access_flags 0
        WriteU32(dex, 0xA0, 0xFFFFFFFF);    // superclass NO_INDEX
        WriteU32(dex, 0xA4, 0);             // interfaces_off
        WriteU32(dex, 0xA8, 0xFFFFFFFF);    // source_file_idx
        WriteU32(dex, 0xAC, 0);             // annotations_off
        WriteU32(dex, 0xB0, (uint)classDataOff);
        WriteU32(dex, 0xB4, 0);             // static_values_off
        "dex\n035\0"u8.CopyTo(dex.AsSpan(0, 8));
        WriteU32(dex, 32, (uint)fileSize);
        WriteU32(dex, 36, 0x70);
        WriteU32(dex, 40, 0x12345678);
        WriteU32(dex, 52, (uint)mapOff);
        WriteU32(dex, 56, 3); WriteU32(dex, 60, 0x70);
        WriteU32(dex, 64, 2); WriteU32(dex, 68, 0x7C);
        WriteU32(dex, 72, 1); WriteU32(dex, 76, 0x84);
        WriteU32(dex, 80, 0); WriteU32(dex, 84, 0);
        WriteU32(dex, 88, 1); WriteU32(dex, 92, 0x90);
        WriteU32(dex, 96, 1); WriteU32(dex, 100, 0x98);
        WriteU32(dex, 104, (uint)(fileSize - dataOff)); WriteU32(dex, 108, (uint)dataOff);
        SHA1.HashData(dex.AsSpan(32)).CopyTo(dex.AsSpan(12, 20));
        const uint modulus = 65521; uint checksumA = 1, checksumB = 0; foreach (byte value in dex.AsSpan(12)) { checksumA = (checksumA + value) % modulus; checksumB = (checksumB + checksumA) % modulus; }
        BinaryPrimitives.WriteUInt32LittleEndian(dex.AsSpan(8, 4), checksumB << 16 | checksumA);
        return dex;
    }

    private static void WriteUleb(List<byte> data, int value)
    {
        while (value >= 0x80) { data.Add((byte)((value & 0x7f) | 0x80)); value >>= 7; }
        data.Add((byte)value);
    }
    private static void WriteU16(List<byte> data, int value) { data.Add((byte)value); data.Add((byte)(value >> 8)); }
    private static void WriteU16(byte[] data, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), (ushort)value);
    private static void WriteU32(List<byte> data, uint value) { data.Add((byte)value); data.Add((byte)(value >> 8)); data.Add((byte)(value >> 16)); data.Add((byte)(value >> 24)); }
    private static void WriteU32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
    private static void WriteMap(List<byte> data, ushort type, uint size, uint offset) { WriteU16(data, type); WriteU16(data, 0); WriteU32(data, size); WriteU32(data, offset); }

    private static void RecomputeIntegrity(byte[] dex)
    {
        SHA1.HashData(dex.AsSpan(32)).CopyTo(dex.AsSpan(12, 20));
        const uint modulus = 65521; uint a = 1, b = 0; foreach (byte value in dex.AsSpan(12)) { a = (a + value) % modulus; b = (b + a) % modulus; }
        BinaryPrimitives.WriteUInt32LittleEndian(dex.AsSpan(8, 4), b << 16 | a);
    }
}
