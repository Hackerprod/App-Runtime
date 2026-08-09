using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for the class_def static_values encoded-array parsing (DexReader) and
/// its application at class-load time (DexInterpreter.EnsureClassInitialized,
/// BEFORE any &lt;clinit&gt;, per the JLS order). AGP emits R$* resource-id fields
/// this way instead of a &lt;clinit&gt; sput sequence; without it they silently
/// default to 0 — the exact ARSC_NOT_FOUND: 0x00000000 root cause on the
/// SKYNET-ApkInstaller debug APK. Includes compact (non-full-width) VALUE_FLOAT
/// and VALUE_CHAR encodings to cover the spec's right-zero-extension and
/// unsigned-char rules (regression guard for that class of bug).
/// </summary>
public sealed class StaticValuesTests
{
    [Fact]
    public void Dex_reader_parses_static_values_as_field_initializers()
    {
        byte[] dexBytes = BuildStaticValuesDex(("value", "I", IntValue(42)));
        var dex = DexReader.Parse(dexBytes);
        var cls = dex.FindClass("LC;");
        Assert.NotNull(cls);
        Assert.Single(cls.StaticFieldValues);
        Assert.Equal("value", cls.StaticFieldValues[0].Key.Name);
        Assert.Equal("I", cls.StaticFieldValues[0].Key.Type);
        Assert.Equal(42, cls.StaticFieldValues[0].Value);
    }

    [Fact]
    public void Ensure_class_initialized_applies_static_values_before_clinit()
    {
        byte[] dexBytes = BuildStaticValuesDex(("value", "I", IntValue(0x7f09001d)));
        var dex = DexReader.Parse(dexBytes);
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());
        // read() does sget LC;.value -> the static_values must populate it even
        // though the class has NO <clinit> at all (AGP's R-class shape).
        Assert.Equal(0x7f09001d, interpreter.InvokeStaticExact("LC;", "read", "()I"));
    }

    [Fact]
    public void Compact_float_uses_the_spec_right_zero_extension()
    {
        // 1.0f = 0x3F800000. Compact size=2 stores the TOP 2 bytes 0x3F80,
        // little-endian [0x80, 0x3F]; the decoder must re-assemble 0x3F80 then
        // left-shift (4-2)*8 = 16 bits to restore 0x3F800000. Without the shift
        // the bits decode as a denormal ~9e-42 (wrong by ~40 orders of magnitude).
        byte[] dexBytes = BuildStaticValuesDex(("value", "F", [0x30, 0x80, 0x3F]));
        var cls = DexReader.Parse(dexBytes).FindClass("LC;");
        Assert.Equal(1.0f, Assert.IsType<float>(cls!.StaticFieldValues[0].Value));
    }

    [Fact]
    public void Compact_char_is_zero_extended_not_sign_extended()
    {
        // char 0x00FF compact size=1: byte 0xFF. VALUE_CHAR is UNSIGNED —
        // zero-extend to 0x00FF (255). Sign-extending would give -1 (0xFFFFFFFF)
        // which truncates to 0xFFFF (65535).
        byte[] dexBytes = BuildStaticValuesDex(("ch", "C", [0x03, 0xFF]));
        var cls = DexReader.Parse(dexBytes).FindClass("LC;");
        Assert.Equal(255, Assert.IsType<int>(cls!.StaticFieldValues[0].Value));
    }

    [Fact]
    public void Compact_double_uses_the_spec_right_zero_extension()
    {
        // 1.0 = 0x3FF0000000000000. Compact size=4 stores the TOP 4 bytes
        // 0x3FF00000, little-endian [0x00, 0x00, 0xF0, 0x3F]; decoder shifts left
        // (8-4)*8 = 32 bits to restore the full double.
        byte[] dexBytes = BuildStaticValuesDex(("value", "D", [0x71, 0x00, 0x00, 0xF0, 0x3F]));
        var cls = DexReader.Parse(dexBytes).FindClass("LC;");
        Assert.Equal(1.0, Assert.IsType<double>(cls!.StaticFieldValues[0].Value));
    }

    private static byte[] IntValue(int value)
    {
        var bytes = new byte[5];
        bytes[0] = 0x64; // VALUE_INT, arg 3
        for (int i = 0; i < 4; i++) bytes[1 + i] = (byte)(value >> (i * 8));
        return bytes;
    }

    /// <summary>Builds a minimal DEX with class LC; and one static field per entry,
    /// initialized via static_values (encoded_array), plus a static read()I method
    /// that sgets field 0. No &lt;clinit&gt; — exactly the AGP R-class shape. Layout is
    /// computed dynamically: header 0x70, string_ids, type_ids, proto_ids, field_ids,
    /// method_ids, class_defs, data (string_data, code_item, class_data, encoded_array).</summary>
    private static byte[] BuildStaticValuesDex(params (string FieldName, string FieldType, byte[] EncodedValue)[] fields)
    {
        var strings = new List<string> { "LC;", "I" }; // "I" always present (proto shorty)
        foreach (var (name, type, _) in fields)
        {
            if (!strings.Contains(type)) strings.Add(type);
            strings.Add(name);
        }
        strings.Add("read");
        var types = new List<string> { "LC;", "I" }; // "I" always present (proto return)
        foreach (var (_, type, _) in fields)
            if (!types.Contains(type)) types.Add(type);

        int stringIdsOff = 0x70;
        int stringIdsSize = strings.Count;
        int typeIdsOff = stringIdsOff + 4 * stringIdsSize;
        int typeIdsSize = types.Count;
        int protoIdsOff = typeIdsOff + 4 * typeIdsSize;
        int fieldIdsOff = protoIdsOff + 12;
        int fieldIdsSize = fields.Length;
        int methodIdsOff = fieldIdsOff + 8 * fieldIdsSize;
        int classDefsOff = methodIdsOff + 8;
        int dataOff = classDefsOff + 32;

        // string_data
        var stringData = new List<byte>();
        var stringOffsets = new int[strings.Count];
        for (int i = 0; i < strings.Count; i++)
        {
            stringOffsets[i] = stringData.Count;
            WriteUleb(stringData, strings[i].Length);
            foreach (byte b in System.Text.Encoding.UTF8.GetBytes(strings[i])) stringData.Add(b);
            stringData.Add(0);
        }
        while (stringData.Count % 4 != 0) stringData.Add(0);

        int codeItemOff = dataOff + stringData.Count;
        ushort[] instructions = [0x0060, 0x0000, 0x000f]; // sget v0, field 0; return v0
        int codeItemLen = 16 + instructions.Length * 2;

        var classData = new List<byte>();
        WriteUleb(classData, (uint)fields.Length); WriteUleb(classData, 0); WriteUleb(classData, 1); WriteUleb(classData, 0);
        for (int i = 0; i < fields.Length; i++) { WriteUleb(classData, (uint)(i == 0 ? 0 : 1)); WriteUleb(classData, 0x9); }
        WriteUleb(classData, 0); WriteUleb(classData, 0x8);
        WriteUleb(classData, (uint)codeItemOff);
        int classDataOff = codeItemOff + codeItemLen;
        while ((classDataOff + classData.Count) % 4 != 0) classData.Add(0);

        var encodedArray = new List<byte>();
        WriteUleb(encodedArray, (uint)fields.Length);
        foreach (var (_, _, encodedValue) in fields) encodedArray.AddRange(encodedValue);
        int staticValuesOff = classDataOff + classData.Count;

        var data = new List<byte>(stringData);
        WriteU16(data, 1); WriteU16(data, 0); WriteU16(data, 0); WriteU16(data, 0);
        WriteU32(data, 0);
        WriteU32(data, (uint)instructions.Length);
        foreach (ushort unit in instructions) WriteU16(data, unit);
        data.AddRange(classData);
        data.AddRange(encodedArray);

        int fileSize = dataOff + data.Count;
        byte[] dex = new byte[fileSize];
        data.CopyTo(dex, dataOff);
        for (int i = 0; i < strings.Count; i++) WriteU32(dex, stringIdsOff + i * 4, (uint)(dataOff + stringOffsets[i]));
        for (int i = 0; i < types.Count; i++) WriteU32(dex, typeIdsOff + i * 4, (uint)strings.IndexOf(types[i]));
        WriteU32(dex, protoIdsOff, (uint)strings.IndexOf("I"));     // shorty
        WriteU32(dex, protoIdsOff + 4, (uint)types.IndexOf("I"));   // return I
        WriteU32(dex, protoIdsOff + 8, 0);                          // no params
        int fieldNameStart = strings.IndexOf("LC;") + 1;
        for (int i = 0; i < fields.Length; i++)
        {
            WriteU16(dex, fieldIdsOff + i * 8, 0);
            WriteU16(dex, fieldIdsOff + i * 8 + 2, (ushort)types.IndexOf(fields[i].FieldType));
            WriteU32(dex, fieldIdsOff + i * 8 + 4, (uint)strings.IndexOf(fields[i].FieldName));
        }
        WriteU16(dex, methodIdsOff, 0);
        WriteU16(dex, methodIdsOff + 2, 0);
        WriteU32(dex, methodIdsOff + 4, (uint)strings.IndexOf("read"));
        WriteU32(dex, classDefsOff, 0);                 // class 0
        WriteU32(dex, classDefsOff + 4, 0);             // access_flags
        WriteU32(dex, classDefsOff + 8, 0xFFFFFFFF);    // superclass NO_INDEX
        WriteU32(dex, classDefsOff + 12, 0);            // interfaces_off
        WriteU32(dex, classDefsOff + 16, 0xFFFFFFFF);   // source_file_idx
        WriteU32(dex, classDefsOff + 20, 0);            // annotations_off
        WriteU32(dex, classDefsOff + 24, (uint)classDataOff);
        WriteU32(dex, classDefsOff + 28, (uint)staticValuesOff);
        "dex\n035\0"u8.CopyTo(dex.AsSpan(0, 8));
        WriteU32(dex, 32, (uint)fileSize);
        WriteU32(dex, 36, 0x70);
        WriteU32(dex, 40, 0x12345678);
        WriteU32(dex, 56, (uint)stringIdsSize); WriteU32(dex, 60, (uint)stringIdsOff);
        WriteU32(dex, 64, (uint)typeIdsSize); WriteU32(dex, 68, (uint)typeIdsOff);
        WriteU32(dex, 72, 1); WriteU32(dex, 76, (uint)protoIdsOff);
        WriteU32(dex, 80, (uint)fieldIdsSize); WriteU32(dex, 84, (uint)fieldIdsOff);
        WriteU32(dex, 88, 1); WriteU32(dex, 92, (uint)methodIdsOff);
        WriteU32(dex, 96, 1); WriteU32(dex, 100, (uint)classDefsOff);
        WriteU32(dex, 104, (uint)data.Count); WriteU32(dex, 108, (uint)dataOff);
        return dex;
    }

    private static void WriteUleb(List<byte> target, int value) => WriteUleb(target, (uint)value);
    private static void WriteUleb(List<byte> target, uint value)
    {
        do
        {
            int b = (int)(value & 0x7f);
            value >>= 7;
            if (value != 0) b |= 0x80;
            target.Add((byte)b);
        } while (value != 0);
    }

    private static void WriteU16(byte[] target, int offset, int value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteU32(byte[] target, int offset, uint value)
    {
        for (int i = 0; i < 4; i++) target[offset + i] = (byte)(value >> (i * 8));
    }

    private static void WriteU16(List<byte> target, int value)
    {
        target.Add((byte)value);
        target.Add((byte)(value >> 8));
    }

    private static void WriteU32(List<byte> target, uint value)
    {
        for (int i = 0; i < 4; i++) target.Add((byte)(value >> (i * 8)));
    }
}
