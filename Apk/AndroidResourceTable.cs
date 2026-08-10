#nullable enable
using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace AndroidRuntime.Core.Apk;

public enum AndroidResourceValueKind { Null, Reference, Attribute, String, Float, Dimension, Fraction, Integer, Boolean, Color }

/// <summary>A parsed ResTable_map_entry (style/theme bag): the attribute map
/// entries in source order plus the optional parent style id. Styles are kept
/// separate from simple entries because they are attribute bags, not single
/// values; a view's `style` attribute resolves through ResolveStyleChain.</summary>
public sealed class AndroidResourceStyle
{
    internal AndroidResourceStyle(uint id, uint parent, IReadOnlyList<AndroidResourceStyleAttribute> attributes, ushort density, int sourceOrder, string name)
    { Id = id; Parent = parent; Attributes = attributes; Density = density; SourceOrder = sourceOrder; Name = name; }
    public uint Id { get; }
    public uint Parent { get; }
    public IReadOnlyList<AndroidResourceStyleAttribute> Attributes { get; }
    public ushort Density { get; }
    internal int SourceOrder { get; }
    /// <summary>The style's key-pool name (e.g. "Widget.AppCompat.Button.Colored"),
    /// used by bounded fallbacks that distinguish framework style families.</summary>
    public string Name { get; }
    internal static AndroidResourceStyle ForTest(uint id, uint parent, IEnumerable<AndroidResourceStyleAttribute> attributes, string name = "", ushort density = 0, int sourceOrder = 0) =>
        new(id, parent, new ReadOnlyCollection<AndroidResourceStyleAttribute>(attributes.ToArray()), density, sourceOrder, name);
}

public readonly record struct AndroidResourceStyleAttribute(uint AttributeId, AndroidResourceValue Value);
public enum AndroidDimensionUnit { Px = 0, Dp = 1, Sp = 2, Pt = 3, In = 4, Mm = 5 }
public enum AndroidFractionUnit { Fraction = 0, FractionParent = 1 }
public readonly record struct AndroidDimension(float Value, AndroidDimensionUnit Unit);
public readonly record struct AndroidFraction(float Value, AndroidFractionUnit Unit);
public readonly record struct AndroidResourceName(string Package, string Type, string Name);

public readonly struct AndroidResourceValue
{
    private AndroidResourceValue(AndroidResourceValueKind kind, uint data, object? value) { Kind = kind; Data = data; _value = value; }
    private readonly object? _value;
    public AndroidResourceValueKind Kind { get; }
    public uint Data { get; }
    public static AndroidResourceValue Reference(uint id) => new(AndroidResourceValueKind.Reference, id, null);
    public static AndroidResourceValue String(string value) => new(AndroidResourceValueKind.String, 0, value);
    public static AndroidResourceValue Color(uint value) => new(AndroidResourceValueKind.Color, value, null);
    public static AndroidResourceValue Boolean(bool value) => new(AndroidResourceValueKind.Boolean, value ? 1u : 0u, null);
    public static AndroidResourceValue Dimension(uint encoded) => new(AndroidResourceValueKind.Dimension, encoded, DecodeDimension(encoded));
    public static AndroidResourceValue Fraction(uint encoded) => new(AndroidResourceValueKind.Fraction, encoded, DecodeFraction(encoded));
    public uint AsReference() => Kind == AndroidResourceValueKind.Reference ? Data : throw Wrong(AndroidResourceValueKind.Reference);
    public string AsString() => Kind == AndroidResourceValueKind.String ? (string)_value! : throw Wrong(AndroidResourceValueKind.String);
    public uint AsColor() => Kind == AndroidResourceValueKind.Color ? Data : throw Wrong(AndroidResourceValueKind.Color);
    public AndroidDimension AsDimension() => Kind == AndroidResourceValueKind.Dimension ? (AndroidDimension)_value! : throw Wrong(AndroidResourceValueKind.Dimension);
    public AndroidFraction AsFraction() => Kind == AndroidResourceValueKind.Fraction ? (AndroidFraction)_value! : throw Wrong(AndroidResourceValueKind.Fraction);
    public int AsInteger() => Kind == AndroidResourceValueKind.Integer ? unchecked((int)Data) : throw Wrong(AndroidResourceValueKind.Integer);
    public bool AsBoolean() => Kind == AndroidResourceValueKind.Boolean ? Data != 0 : throw Wrong(AndroidResourceValueKind.Boolean);

    internal static AndroidResourceValue FromBinary(byte type, uint data, IReadOnlyList<string> strings, string prefix) => type switch
    {
        0x00 => new(AndroidResourceValueKind.Null, data, null),
        0x01 => Reference(data),
        0x02 => new(AndroidResourceValueKind.Attribute, data, null),
        0x03 when data < strings.Count => String(strings[(int)data]),
        0x03 => throw AndroidBinaryFormat.Invalid(prefix, "string value index is outside global string pool"),
        0x04 => new(AndroidResourceValueKind.Float, data, BitConverter.Int32BitsToSingle(unchecked((int)data))),
        0x05 => Dimension(data),
        0x06 => Fraction(data),
        0x10 or 0x11 => new(AndroidResourceValueKind.Integer, data, null),
        0x12 => new(AndroidResourceValueKind.Boolean, data, null),
        >= 0x1c and <= 0x1f => Color(data),
        _ => throw new NotSupportedException($"{prefix.Replace("INVALID", "UNSUPPORTED", StringComparison.Ordinal)}: typed value 0x{type:x2} is not supported")
    };

    private static AndroidDimension DecodeDimension(uint encoded)
    {
        float value = DecodeComplexMantissa(encoded);
        int unit = (int)(encoded & 0xf);
        if (unit > 5) throw new NotSupportedException($"ARSC_UNSUPPORTED: dimension unit {unit} is not supported");
        return new AndroidDimension(value, (AndroidDimensionUnit)unit);
    }

    private static AndroidFraction DecodeFraction(uint encoded)
    {
        float value = DecodeComplexMantissa(encoded);
        int unit = (int)(encoded & 0xf);
        if (unit > 1) throw new NotSupportedException($"ARSC_UNSUPPORTED: fraction unit {unit} is not supported");
        return new AndroidFraction(value, (AndroidFractionUnit)unit);
    }

    /// <summary>
    /// AOSP complexToFloat: the shared mantissa/radix decoding for TYPE_DIMENSION and
    /// TYPE_FRACTION. The 24-bit signed mantissa occupies bits 8-31 and the low 4 bits
    /// are the unit; bits 4-5 select the radix, whose fractional precision k is
    /// {0, 7, 15, 23} (fixed-point formats 23p0 / 16p7 / 8p15 / 0p23 — integer plus
    /// fractional bits always sum to 23). The encoder stores round(value * 2^k), so the
    /// decoder divides mantissa_masked by 2^(8+k): {256, 32768, 8388608, 2147483648}.
    /// </summary>
    private static float DecodeComplexMantissa(uint encoded)
    {
        int mantissa = unchecked((int)(encoded & 0xffffff00));
        int radix = (int)((encoded >> 4) & 3);
        float multiplier = radix switch { 0 => 1f / 256f, 1 => 1f / 32768f, 2 => 1f / 8388608f, _ => 1f / 2147483648f };
        return mantissa * multiplier;
    }
    private InvalidOperationException Wrong(AndroidResourceValueKind expected) => new($"Resource value is {Kind}, not {expected}.");
}

public sealed class AndroidResourceEntry
{
    internal AndroidResourceEntry(uint id, AndroidResourceName name, AndroidResourceValue value, ushort density, int sourceOrder)
    { Id = id; Name = name; Value = value; Density = density; SourceOrder = sourceOrder; }
    public uint Id { get; }
    public AndroidResourceName Name { get; }
    public AndroidResourceValue Value { get; }
    public ushort Density { get; }
    internal int SourceOrder { get; }
    internal static AndroidResourceEntry ForTest(string package, string type, string name, AndroidResourceValue value, ushort density = 0, int sourceOrder = 0) => new(0, new(package, type, name), value, density, sourceOrder);
}

public sealed class AndroidResourceLimits
{
    public static AndroidResourceLimits Default { get; } = new();
    public AndroidResourceLimits(int maxPackages = 32, int maxEntries = 262144, int maxReferenceDepth = 32, int maxResolvedValues = 4096, int maxResolvedStringBytes = 4 * 1024 * 1024, int targetDensity = 160)
    {
        if (maxPackages <= 0 || maxEntries <= 0 || maxReferenceDepth <= 0 || maxResolvedValues <= 0 || maxResolvedStringBytes <= 0 || targetDensity <= 0) throw new ArgumentOutOfRangeException(nameof(maxPackages));
        MaxPackages = maxPackages; MaxEntries = maxEntries; MaxReferenceDepth = maxReferenceDepth; MaxResolvedValues = maxResolvedValues; MaxResolvedStringBytes = maxResolvedStringBytes; TargetDensity = targetDensity;
    }
    public int MaxPackages { get; } public int MaxEntries { get; } public int MaxReferenceDepth { get; } public int MaxResolvedValues { get; } public int MaxResolvedStringBytes { get; } public int TargetDensity { get; }
}

public sealed class AndroidResourceTable
{
    private const string Prefix = "ARSC_INVALID";
    private AndroidResourceTable(IReadOnlyDictionary<uint, AndroidResourceEntry> entries, IReadOnlyDictionary<uint, AndroidResourceStyle> styles) { Entries = entries; Styles = styles; }
    public IReadOnlyDictionary<uint, AndroidResourceEntry> Entries { get; }
    /// <summary>Parsed style/theme bags keyed by their resource id. Only
    /// well-formed complex entries are kept; a complex entry that cannot be
    /// validated stays absent and lookup fails cleanly (ARSC_NOT_FOUND).</summary>
    public IReadOnlyDictionary<uint, AndroidResourceStyle> Styles { get; }

    public static AndroidResourceTable Parse(byte[] data, AndroidResourceLimits? limits = null) { ArgumentNullException.ThrowIfNull(data); return Parse((ReadOnlySpan<byte>)data, limits); }
    public static AndroidResourceTable Parse(ReadOnlySpan<byte> data, AndroidResourceLimits? limits = null)
    {
        try { return ParseCore(data, limits); }
        catch (OverflowException error) { throw AndroidBinaryFormat.Invalid(Prefix, "integer arithmetic overflow", error); }
    }

    private static AndroidResourceTable ParseCore(ReadOnlySpan<byte> data, AndroidResourceLimits? limits)
    {
        limits ??= AndroidResourceLimits.Default;
        AndroidChunk root = Chunk(data, 0, data.Length);
        if (root.Type != 0x0002 || root.HeaderSize < 12 || root.Size != data.Length) throw Invalid("root must be a complete RES_TABLE_TYPE chunk");
        uint declaredPackages = U32(data, 8);
        if (declaredPackages > limits.MaxPackages) throw Invalid($"package count exceeds quota {limits.MaxPackages}");
        string[] globals = [];
        bool sawGlobals = false;
        int packageCount = 0;
        int totalEntryCount = 0;
        var candidates = new Dictionary<uint, List<AndroidResourceEntry>>();
        var styleCandidates = new Dictionary<uint, List<AndroidResourceStyle>>();
        for (int offset = root.HeaderSize; offset < root.End;)
        {
            AndroidChunk child = Chunk(data, offset, root.End);
            if (child.Type == 0x0001)
            {
                if (sawGlobals || packageCount != 0) throw Invalid("global string pool is duplicated or out of order");
                globals = AndroidBinaryFormat.ReadStringPool(data, child, Prefix); sawGlobals = true;
            }
            else if (child.Type == 0x0200)
            {
                if (!sawGlobals) throw Invalid("package appeared before global string pool");
                ParsePackage(data, child, globals, candidates, styleCandidates, limits, ref totalEntryCount); packageCount++;
            }
            else throw Invalid($"unsupported table child chunk 0x{child.Type:x4}");
            offset = child.End;
        }
        if (!sawGlobals || packageCount != declaredPackages) throw Invalid("declared package count does not match parsed packages");
        var selected = new Dictionary<uint, AndroidResourceEntry>();
        foreach (var pair in candidates) selected.Add(pair.Key, SelectConfiguration(pair.Value, limits.TargetDensity));
        var selectedStyles = new Dictionary<uint, AndroidResourceStyle>();
        foreach (var pair in styleCandidates) selectedStyles.Add(pair.Key, SelectStyleConfiguration(pair.Value, limits.TargetDensity));
        return new AndroidResourceTable(new ReadOnlyDictionary<uint, AndroidResourceEntry>(selected), new ReadOnlyDictionary<uint, AndroidResourceStyle>(selectedStyles));
    }

    private static void ParsePackage(ReadOnlySpan<byte> data, AndroidChunk package, string[] globals, Dictionary<uint, List<AndroidResourceEntry>> output, Dictionary<uint, List<AndroidResourceStyle>> styles, AndroidResourceLimits limits, ref int totalEntryCount)
    {
        if (package.HeaderSize < 284) throw Invalid("package header is smaller than 284 bytes");
        uint packageId = U32(data, package.Offset + 8); if (packageId is 0 or > 255) throw Invalid("package id is outside 1..255");
        string packageName = ReadPackageName(data, package.Offset + 12);
        uint typeStringsOffset = U32(data, package.Offset + 268), keyStringsOffset = U32(data, package.Offset + 276);
        string[]? types = null, keys = null;
        var typeChunks = new List<AndroidChunk>();
        var typeSpecs = new Dictionary<byte, uint>();
        for (int offset = package.Offset + package.HeaderSize; offset < package.End;)
        {
            AndroidChunk child = Chunk(data, offset, package.End);
            int relative = offset - package.Offset;
            if (child.Type == 0x0001)
            {
                string[] pool = AndroidBinaryFormat.ReadStringPool(data, child, Prefix);
                if (relative == typeStringsOffset) types = pool;
                else if (relative == keyStringsOffset) keys = pool;
                else throw Invalid("package contains an unreferenced string pool");
            }
            else if (child.Type == 0x0202)
            {
                if (child.HeaderSize < 16) throw Invalid("typeSpec header is malformed");
                byte id = data[offset + 8]; uint count = U32(data, offset + 12);
                if (id == 0 || child.HeaderSize + count * 4L > child.Size) throw Invalid("typeSpec entry count exceeds chunk");
                typeSpecs[id] = count;
            }
            else if (child.Type == 0x0201) typeChunks.Add(child);
            else if (child.Type != 0x0203) throw Invalid($"unsupported package child chunk 0x{child.Type:x4}");
            offset = child.End;
        }
        if (types is null || keys is null) throw Invalid("package string pools are missing");
        foreach (AndroidChunk typeChunk in typeChunks)
            ParseType(data, typeChunk, packageId, packageName, types, keys, globals, typeSpecs, output, styles, limits, ref totalEntryCount);
    }

    private static void ParseType(ReadOnlySpan<byte> data, AndroidChunk chunk, uint packageId, string packageName, string[] types, string[] keys, string[] globals, IReadOnlyDictionary<byte, uint> typeSpecs, Dictionary<uint, List<AndroidResourceEntry>> output, Dictionary<uint, List<AndroidResourceStyle>> styles, AndroidResourceLimits limits, ref int totalEntryCount)
    {
        if (chunk.HeaderSize < 24) throw Invalid("type header is malformed");
        byte typeId = data[chunk.Offset + 8], flags = data[chunk.Offset + 9];
        if (typeId == 0 || typeId > types.Length) throw Invalid("type id is outside type string pool");
        if (flags != 0) throw new NotSupportedException($"ARSC_UNSUPPORTED: type entry flags 0x{flags:x2} (sparse/offset16) are not supported");
        uint entryCountValue = U32(data, chunk.Offset + 12), entriesStartValue = U32(data, chunk.Offset + 16);
        if (entryCountValue > int.MaxValue || entriesStartValue > int.MaxValue) throw Invalid("type entry count or start exceeds supported range");
        int entryCount = (int)entryCountValue, entriesStart = (int)entriesStartValue;
        if (entryCount > limits.MaxEntries) throw Invalid($"type entry count exceeds quota {limits.MaxEntries}");
        if (chunk.HeaderSize + entryCount * 4L > chunk.Size || entriesStart < chunk.HeaderSize + entryCount * 4L || entriesStart > chunk.Size) throw Invalid("type offset table or entry data is outside chunk");
        int configStart = chunk.Offset + 20;
        uint configSize = U32(data, configStart); if (configSize < 4 || 20L + configSize > chunk.HeaderSize) throw Invalid("resource configuration is malformed");
        ushort density = configSize >= 16 ? U16(data, configStart + 14) : (ushort)0;
        if (HasUnsupportedQualifier(data.Slice(configStart, (int)configSize), density)) return;
        if (typeSpecs.TryGetValue(typeId, out uint specCount) && specCount != entryCountValue) throw Invalid("type and typeSpec entry counts differ");

        for (int index = 0; index < entryCount; index++)
        {
            uint relative = U32(data, chunk.Offset + chunk.HeaderSize + index * 4); if (relative == uint.MaxValue) continue;
            if (relative > int.MaxValue || relative >= chunk.Size - entriesStart) throw Invalid("resource entry offset is outside type chunk");
            int entryAt = checked(chunk.Offset + entriesStart + (int)relative);
            if (entryAt > chunk.End - 8) throw Invalid("resource entry is truncated");
            ushort entrySize = U16(data, entryAt), entryFlags = U16(data, entryAt + 2); uint keyIndex = U32(data, entryAt + 4);
            // Every non-sentinel index is real work the parser walked (offset-table
            // read plus entry header bytes), so it counts toward the global quota
            // even when the entry is skipped below; otherwise an all-complex table
            // could bypass the work bound while the loop still walks every entry.
            if (++totalEntryCount > limits.MaxEntries) throw Invalid($"resource entries exceed quota {limits.MaxEntries}");
            uint id = (packageId << 24) | ((uint)typeId << 16) | (uint)index;
            // Complex entries (ResTable_map_entry: styles/themes/bags) are parsed
            // into the style collection so a view's `style` attribute can resolve
            // its attribute map (background/textColor etc.). A malformed complex
            // entry is skipped for that one index rather than aborting the whole
            // table; its id is simply absent, and lookup fails cleanly
            // (ARSC_NOT_FOUND) only if something asks for it.
            if ((entryFlags & 1) != 0)
            {
                ParseStyleEntry(data, entryAt, entrySize, keyIndex, id, keys, globals, styles, limits, ref totalEntryCount, chunk.End, density);
                continue;
            }
            if (entrySize < 8 || keyIndex >= keys.Length) throw Invalid("resource entry header or key index is invalid");
            int valueAt = checked(entryAt + entrySize); if (valueAt > chunk.End - 8 || U16(data, valueAt) != 8 || data[valueAt + 2] != 0) throw Invalid("resource value is truncated or malformed");
            AndroidResourceValue value = AndroidResourceValue.FromBinary(data[valueAt + 3], U32(data, valueAt + 4), globals, Prefix);
            if (!output.TryGetValue(id, out var list)) output.Add(id, list = []);
            list.Add(new AndroidResourceEntry(id, new AndroidResourceName(packageName, types[typeId - 1], keys[keyIndex]), value, density, chunk.Offset));
        }
    }

    /// <summary>Parses one complex ResTable_map_entry into an AndroidResourceStyle.
    /// The entry header is 16 bytes (size/flags/key/parent/count); each following
    /// ResTable_map entry is 12 bytes (attribute name u32 + 8-byte Res_value).
    /// Fail-closed: an entry whose map crosses the chunk boundary or whose
    /// per-map work would exceed the quota is skipped for that one index.</summary>
    private static void ParseStyleEntry(ReadOnlySpan<byte> data, int entryAt, ushort entrySize, uint keyIndex, uint id, string[] keys, string[] globals, Dictionary<uint, List<AndroidResourceStyle>> styles, AndroidResourceLimits limits, ref int totalEntryCount, int chunkEnd, ushort density)
    {
        if (entrySize < 16 || keyIndex >= keys.Length || entryAt > chunkEnd - 16) return;
        uint parent = U32(data, entryAt + 8), count = U32(data, entryAt + 12);
        if (count > limits.MaxEntries) return;
        if (count > 0 && (long)entryAt + entrySize + (long)count * 12 > chunkEnd) return;
        var attributes = new List<AndroidResourceStyleAttribute>((int)count);
        int mapAt = entryAt + entrySize;
        for (int mapIndex = 0; mapIndex < count; mapIndex++)
        {
            if (++totalEntryCount > limits.MaxEntries) throw Invalid($"resource entries exceed quota {limits.MaxEntries}");
            uint attributeId = U32(data, mapAt);
            if (attributeId == 0 || mapAt > chunkEnd - 12 || U16(data, mapAt + 4) != 8 || data[mapAt + 6] != 0) return;
            AndroidResourceValue value = AndroidResourceValue.FromBinary(data[mapAt + 7], U32(data, mapAt + 8), globals, Prefix);
            attributes.Add(new AndroidResourceStyleAttribute(attributeId, value));
            mapAt += 12;
        }
        if (!styles.TryGetValue(id, out var list)) styles.Add(id, list = []);
        list.Add(new AndroidResourceStyle(id, parent, new ReadOnlyCollection<AndroidResourceStyleAttribute>(attributes), density, entryAt, keys[keyIndex]));
    }

    private static bool HasUnsupportedQualifier(ReadOnlySpan<byte> config, ushort density)
    {
        for (int i = 4; i < config.Length; i++)
        {
            if (i is 14 or 15) continue;
            if (config[i] != 0) return true;
        }
        return false;
    }
    private static (int Exact, int Default, int Distance) DensityRank(ushort density, int target) => density == target ? (0, 0, 0) : density == 0 ? (1, 0, 0) : (1, 1, Math.Abs(density - target));
    internal static AndroidResourceEntry SelectConfiguration(IEnumerable<AndroidResourceEntry> entries, int targetDensity) =>
        entries.OrderBy(entry => DensityRank(entry.Density, targetDensity)).ThenBy(entry => entry.SourceOrder).First();
    private static AndroidResourceStyle SelectStyleConfiguration(IEnumerable<AndroidResourceStyle> styles, int targetDensity) =>
        styles.OrderBy(style => DensityRank(style.Density, targetDensity)).ThenBy(style => style.SourceOrder).First();
    private static string ReadPackageName(ReadOnlySpan<byte> data, int offset)
    {
        int length = 0; while (length < 128 && U16(data, offset + length * 2) != 0) length++;
        if (length == 128) throw Invalid("package name is not terminated");
        return System.Text.Encoding.Unicode.GetString(data.Slice(offset, length * 2));
    }
    private static AndroidChunk Chunk(ReadOnlySpan<byte> data, int offset, int end) => AndroidBinaryFormat.ReadChunk(data, offset, end, Prefix);
    private static ushort U16(ReadOnlySpan<byte> data, int offset) => AndroidBinaryFormat.U16(data, offset, Prefix);
    private static uint U32(ReadOnlySpan<byte> data, int offset) => AndroidBinaryFormat.U32(data, offset, Prefix);
    private static InvalidDataException Invalid(string message) => AndroidBinaryFormat.Invalid(Prefix, message);
}
