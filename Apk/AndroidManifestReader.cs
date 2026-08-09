#nullable enable
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace AndroidRuntime.Core.Apk;

public sealed class AndroidManifestActivity
{
    internal AndroidManifestActivity(string name, string descriptor)
    {
        Name = name;
        Descriptor = descriptor;
    }

    public string Name { get; }
    public string Descriptor { get; }
    internal bool IsLauncher { get; set; }
}

public sealed class AndroidManifest
{
    internal AndroidManifest(string packageName, IReadOnlyList<AndroidManifestActivity> activities, AndroidManifestActivity launcher, IReadOnlyCollection<string> permissions, int targetSdkVersion, int applicationThemeStyleId)
    {
        PackageName = packageName;
        Activities = Array.AsReadOnly(activities.ToArray());
        LauncherActivityName = launcher.Name;
        LauncherActivityDescriptor = launcher.Descriptor;
        UsesPermissions = Array.AsReadOnly(permissions.Distinct(StringComparer.Ordinal).ToArray());
        TargetSdkVersion = targetSdkVersion;
        ApplicationThemeStyleId = applicationThemeStyleId;
    }

    public string PackageName { get; }
    public IReadOnlyList<AndroidManifestActivity> Activities { get; }
    public string LauncherActivityName { get; }
    public string LauncherActivityDescriptor { get; }
    public IReadOnlyCollection<string> UsesPermissions { get; }
    public int TargetSdkVersion { get; }
    /// <summary>The app's active theme style resource id from
    /// &lt;application android:theme&gt; (0 when the manifest declares none).
    /// Phase 2: ViewRuntime resolves ?attr/... by walking this style id through
    /// the bridge's resolve_style callback — the theme IS a style chain.</summary>
    public int ApplicationThemeStyleId { get; }
}

/// <summary>Strict reader for the bounded binary XML subset needed to discover an APK launcher Activity.</summary>
public static class AndroidManifestReader
{
    private const ushort XmlType = 0x0003;
    private const ushort StringPoolType = 0x0001;
    private const ushort ResourceMapType = 0x0180;
    private const ushort StartNamespaceType = 0x0100;
    private const ushort EndNamespaceType = 0x0101;
    private const ushort StartElementType = 0x0102;
    private const ushort EndElementType = 0x0103;
    private const uint NoIndex = 0xffffffff;
    private const uint Utf8Flag = 0x00000100;
    private const byte TypedString = 0x03;
    private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";
    private const string MainAction = "android.intent.action.MAIN";
    private const string LauncherCategory = "android.intent.category.LAUNCHER";

    public static AndroidManifest Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Parse((ReadOnlySpan<byte>)data);
    }

    public static AndroidManifest Parse(ReadOnlySpan<byte> data)
    {
        var root = ReadChunk(data, 0, data.Length);
        if (root.Type != XmlType || root.HeaderSize != 8)
            throw Invalid("root chunk must be RES_XML_TYPE with an 8-byte header");
        if (root.Size != data.Length)
            throw Invalid("root chunk size must match the manifest length");

        var strings = Array.Empty<string>();
        bool sawStringPool = false;
        bool sawResourceMap = false;
        bool sawXmlContent = false;
        bool sawManifest = false;
        string? packageName = null;
        var activities = new List<AndroidManifestActivity>();
        var frames = new List<ElementFrame>();
        var namespaces = new List<(uint Prefix, uint Uri)>();
        var permissions = new HashSet<string>(StringComparer.Ordinal);
        int minSdkVersion = 1;
        int? declaredTargetSdkVersion = null;
        int applicationThemeStyleId = 0;

        int offset = root.HeaderSize;
        while (offset < root.Size)
        {
            var chunk = ReadChunk(data, offset, root.Size);
            switch (chunk.Type)
            {
                case StringPoolType:
                    if (sawStringPool || offset != root.HeaderSize)
                        throw Invalid("string pool must appear exactly once as the first child chunk");
                    strings = ReadStringPool(data, chunk);
                    sawStringPool = true;
                    break;

                case ResourceMapType:
                    RequireStringPool(sawStringPool);
                    if (sawResourceMap || sawXmlContent || chunk.HeaderSize != 8 || (chunk.Size - chunk.HeaderSize) % 4 != 0)
                        throw Invalid("resource map chunk is malformed or out of order");
                    sawResourceMap = true;
                    break;

                case StartNamespaceType:
                {
                    RequireStringPool(sawStringPool);
                    sawXmlContent = true;
                    RequireNodeChunk(chunk, 24);
                    GetOptionalString(strings, U32(data, offset + 12), "namespace comment");
                    uint prefix = U32(data, offset + 16);
                    uint uri = U32(data, offset + 20);
                    GetOptionalString(strings, prefix, "namespace prefix");
                    GetString(strings, uri, "namespace URI");
                    namespaces.Add((prefix, uri));
                    break;
                }

                case EndNamespaceType:
                {
                    RequireStringPool(sawStringPool);
                    sawXmlContent = true;
                    RequireNodeChunk(chunk, 24);
                    GetOptionalString(strings, U32(data, offset + 12), "namespace comment");
                    uint prefix = U32(data, offset + 16);
                    uint uri = U32(data, offset + 20);
                    GetOptionalString(strings, prefix, "namespace prefix");
                    GetString(strings, uri, "namespace URI");
                    if (namespaces.Count == 0 || namespaces[^1] != (prefix, uri))
                        throw Invalid("namespace end chunk does not match the active namespace");
                    namespaces.RemoveAt(namespaces.Count - 1);
                    break;
                }

                case StartElementType:
                {
                    RequireStringPool(sawStringPool);
                    sawXmlContent = true;
                    RequireNodeChunk(chunk, 36);
                    GetOptionalString(strings, U32(data, offset + 12), "element comment");
                    string? elementNamespace = GetOptionalString(strings, U32(data, offset + 16), "element namespace");
                    string elementName = GetString(strings, U32(data, offset + 20), "element name");
                    ushort attributeStart = U16(data, offset + 24);
                    ushort attributeSize = U16(data, offset + 26);
                    ushort attributeCount = U16(data, offset + 28);
                    ushort idIndex = U16(data, offset + 30);
                    ushort classIndex = U16(data, offset + 32);
                    ushort styleIndex = U16(data, offset + 34);
                    if (attributeStart < 20 || attributeSize != 20)
                        throw Invalid("start-element attribute layout is unsupported or malformed");
                    if (!ValidSpecialAttributeIndex(idIndex, attributeCount) ||
                        !ValidSpecialAttributeIndex(classIndex, attributeCount) ||
                        !ValidSpecialAttributeIndex(styleIndex, attributeCount))
                        throw Invalid("start-element special attribute index is outside attributeCount");
                    int attributesOffset = CheckedAdd(offset + 16, attributeStart, "attribute offset");
                    int attributesLength = CheckedMultiply(attributeCount, attributeSize, "attribute table size");
                    int attributesEnd = CheckedAdd(attributesOffset, attributesLength, "attribute table end");
                    if (attributesOffset < offset + 36 || attributesEnd > offset + chunk.Size)
                        throw Invalid("start-element attributes exceed their chunk bounds");

                    var attributes = new List<XmlAttribute>(attributeCount);
                    for (int i = 0; i < attributeCount; i++)
                        attributes.Add(ReadAttribute(data, attributesOffset + i * attributeSize, strings));

                    if (frames.Count == 0)
                    {
                        if (sawManifest || elementName != "manifest")
                            throw Invalid("document root must be exactly one manifest element");
                        sawManifest = true;
                        packageName = FindAttribute(attributes, null, "package")
                            ?? throw Invalid("manifest package attribute is required");
                    }

                    var frame = new ElementFrame(elementName, elementNamespace);
                    if (elementName == "uses-permission" && frames.Count == 1 && frames[^1].Name == "manifest")
                    {
                        string permission = FindAttribute(attributes, AndroidNamespace, "name") ?? throw Invalid("uses-permission android:name attribute is required");
                        if (permissions.Count >= 128 && !permissions.Contains(permission)) throw Invalid("manifest exceeds the uses-permission quota");
                        permissions.Add(permission);
                    }
                    if (elementName == "uses-sdk" && frames.Count == 1 && frames[^1].Name == "manifest")
                    {
                        string? minimum = FindAttribute(attributes, AndroidNamespace, "minSdkVersion");
                        if (minimum is not null && (!int.TryParse(minimum, NumberStyles.None, CultureInfo.InvariantCulture, out minSdkVersion) || minSdkVersion <= 0))
                            throw Invalid("uses-sdk minSdkVersion must be a positive integer");
                        string? target = FindAttribute(attributes, AndroidNamespace, "targetSdkVersion");
                        if (target is not null)
                        {
                            if (!int.TryParse(target, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedTarget) || parsedTarget <= 0)
                                throw Invalid("uses-sdk targetSdkVersion must be a positive integer");
                            declaredTargetSdkVersion = parsedTarget;
                        }
                    }
                    if (elementName == "activity" && frames.Count > 0 && frames[^1].Name == "application")
                    {
                        string declaredName = FindAttribute(attributes, AndroidNamespace, "name")
                            ?? throw Invalid("activity android:name attribute is required");
                        string descriptor = ToDexDescriptor(packageName ?? throw Invalid("activity appeared before manifest package"), declaredName);
                        string resolvedName = descriptor[1..^1].Replace('/', '.');
                        activities.Add(new AndroidManifestActivity(resolvedName, descriptor));
                        frame.ActivityIndex = activities.Count - 1;
                    }
                    else if (elementName == "application" && frames.Count == 1 && frames[^1].Name == "manifest")
                    {
                        // The active theme: <application android:theme> is a typed
                        // reference to a style resource. ViewRuntime resolves
                        // ?attr/... by walking this style id (theme == style chain).
                        applicationThemeStyleId = FindReferenceAttribute(attributes, AndroidNamespace, "theme") ?? 0;
                    }
                    else if (elementName == "intent-filter" && frames.Count > 0 &&
                             frames[^1].Name == "activity" && frames[^1].ActivityIndex >= 0)
                    {
                        frame.ActivityIndex = frames[^1].ActivityIndex;
                        frame.Filter = new IntentFilterState();
                    }
                    else if ((elementName == "action" || elementName == "category") && frames.Count > 0 &&
                             frames[^1].Name == "intent-filter" && frames[^1].Filter is not null)
                    {
                        string value = FindAttribute(attributes, AndroidNamespace, "name")
                            ?? throw Invalid(elementName + " android:name attribute is required");
                        if (elementName == "action" && value == MainAction)
                            frames[^1].Filter!.HasMain = true;
                        if (elementName == "category" && value == LauncherCategory)
                            frames[^1].Filter!.HasLauncher = true;
                    }

                    if (frame.ActivityIndex < 0 && frames.Count > 0)
                        frame.ActivityIndex = frames[^1].ActivityIndex;
                    frames.Add(frame);
                    break;
                }

                case EndElementType:
                {
                    RequireStringPool(sawStringPool);
                    sawXmlContent = true;
                    RequireNodeChunk(chunk, 24);
                    GetOptionalString(strings, U32(data, offset + 12), "end-element comment");
                    string? elementNamespace = GetOptionalString(strings, U32(data, offset + 16), "end-element namespace");
                    string elementName = GetString(strings, U32(data, offset + 20), "end-element name");
                    if (frames.Count == 0 || frames[^1].Name != elementName || frames[^1].NamespaceUri != elementNamespace)
                        throw Invalid("end-element chunk does not match the open element");
                    var frame = frames[^1];
                    if (frame.Filter is { HasMain: true, HasLauncher: true })
                        activities[frame.ActivityIndex].IsLauncher = true;
                    frames.RemoveAt(frames.Count - 1);
                    break;
                }

                default:
                    throw Invalid($"unsupported binary XML chunk type 0x{chunk.Type:x4}");
            }

            offset = CheckedAdd(offset, chunk.Size, "next chunk offset");
        }

        if (!sawStringPool || !sawManifest || frames.Count != 0 || namespaces.Count != 0)
            throw Invalid("document ended with missing or unclosed chunks");
        if (string.IsNullOrWhiteSpace(packageName))
            throw Invalid("manifest package must not be empty");
        var launcher = activities.FirstOrDefault(activity => activity.IsLauncher)
            ?? throw Invalid("manifest does not declare an activity with MAIN and LAUNCHER in the same intent-filter");
        return new AndroidManifest(packageName, activities.AsReadOnly(), launcher, permissions.ToArray(), declaredTargetSdkVersion ?? minSdkVersion, applicationThemeStyleId);
    }

    public static string ToDexDescriptor(string packageName, string declaredActivityName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaredActivityName);
        string fullName = declaredActivityName.StartsWith(".", StringComparison.Ordinal)
            ? packageName + declaredActivityName
            : declaredActivityName.Contains('.', StringComparison.Ordinal)
                ? declaredActivityName
                : packageName + "." + declaredActivityName;
        if (fullName.Split('.').Any(segment => segment.Length == 0) ||
            fullName.Any(character => char.IsWhiteSpace(character) || character is '/' or ';' or '['))
            throw new InvalidDataException("Activity name contains characters that cannot form a DEX class descriptor.");
        return "L" + fullName.Replace('.', '/') + ";";
    }

    private static string[] ReadStringPool(ReadOnlySpan<byte> data, Chunk chunk)
    {
        if (chunk.HeaderSize < 28) throw Invalid("string pool header is smaller than 28 bytes");
        uint styleCount = U32(data, chunk.Offset + 12);
        uint stylesStart = U32(data, chunk.Offset + 24);
        if ((styleCount == 0) != (stylesStart == 0)) throw Invalid("stylesStart is inconsistent with styleCount");
        if (styleCount != 0) throw Invalid("styled strings are not supported by this bounded manifest reader");
        return AndroidBinaryFormat.ReadStringPool(data, new AndroidChunk(chunk.Type, chunk.HeaderSize, chunk.Size, chunk.Offset), "Binary XML chunk error");
    }

    private static string ReadUtf8String(ReadOnlySpan<byte> data, int offset, int limit)
    {
        int utf16Length = ReadLength8(data, ref offset, limit);
        int byteLength = ReadLength8(data, ref offset, limit);
        int end = CheckedAdd(offset, byteLength, "UTF-8 string end");
        if (end >= limit || data[end] != 0)
            throw Invalid("UTF-8 string is truncated or lacks a terminator");
        try
        {
            string value = new UTF8Encoding(false, true).GetString(data[offset..end]);
            if (value.Length != utf16Length)
                throw Invalid("UTF-8 string length prefix does not match decoded text");
            return value;
        }
        catch (DecoderFallbackException error)
        {
            throw Invalid("UTF-8 string contains invalid encoding", error);
        }
    }

    private static string ReadUtf16String(ReadOnlySpan<byte> data, int offset, int limit)
    {
        int utf16Length = ReadLength16(data, ref offset, limit);
        int byteLength = CheckedMultiply(utf16Length, 2, "UTF-16 byte length");
        int end = CheckedAdd(offset, byteLength, "UTF-16 string end");
        if (end > limit - 2 || U16(data, end) != 0)
            throw Invalid("UTF-16 string is truncated or lacks a terminator");
        try
        {
            string value = new UnicodeEncoding(false, false, true).GetString(data[offset..end]);
            if (value.Length != utf16Length)
                throw Invalid("UTF-16 string length prefix does not match decoded text");
            return value;
        }
        catch (DecoderFallbackException error)
        {
            throw Invalid("UTF-16 string contains invalid encoding", error);
        }
    }

    private static int ReadLength8(ReadOnlySpan<byte> data, ref int offset, int limit)
    {
        if (offset >= limit)
            throw Invalid("UTF-8 length prefix is truncated");
        int first = data[offset++];
        if ((first & 0x80) == 0)
            return first;
        if (offset >= limit)
            throw Invalid("UTF-8 length prefix is truncated");
        return ((first & 0x7f) << 8) | data[offset++];
    }

    private static int ReadLength16(ReadOnlySpan<byte> data, ref int offset, int limit)
    {
        if (offset + 2 > limit)
            throw Invalid("UTF-16 length prefix is truncated");
        int first = U16(data, offset);
        offset += 2;
        if ((first & 0x8000) == 0)
            return first;
        if (offset + 2 > limit)
            throw Invalid("UTF-16 length prefix is truncated");
        int second = U16(data, offset);
        offset += 2;
        return ((first & 0x7fff) << 16) | second;
    }

    private static XmlAttribute ReadAttribute(ReadOnlySpan<byte> data, int offset, string[] strings)
    {
        uint namespaceIndex = U32(data, offset);
        uint nameIndex = U32(data, offset + 4);
        uint rawValueIndex = U32(data, offset + 8);
        ushort typedSize = U16(data, offset + 12);
        byte zero = data[offset + 14];
        byte type = data[offset + 15];
        uint typedData = U32(data, offset + 16);
        if (typedSize != 8 || zero != 0)
            throw Invalid("attribute typed value header is malformed");
        string? namespaceUri = GetOptionalString(strings, namespaceIndex, "attribute namespace");
        string name = GetString(strings, nameIndex, "attribute name");
        string? value = rawValueIndex != NoIndex
            ? GetString(strings, rawValueIndex, "attribute raw value")
            : type == TypedString
                ? GetString(strings, typedData, "attribute typed string")
                : type == 0x12
                    ? (typedData != 0).ToString().ToLowerInvariant()
                    : type is >= 0x10 and <= 0x1f
                        ? typedData.ToString(CultureInfo.InvariantCulture)
                        : null;
        return new XmlAttribute(namespaceUri, name, value, type, typedData);
    }

    private static string? FindAttribute(IEnumerable<XmlAttribute> attributes, string? namespaceUri, string name) =>
        attributes.FirstOrDefault(attribute => attribute.NamespaceUri == namespaceUri && attribute.Name == name)?.Value;

    /// <summary>Finds an attribute by name and returns its raw typed data when the
    /// value is a TYPE_REFERENCE (0x01) resource id — used for android:theme,
    /// which carries the style resource id. Returns null when absent or not a
    /// reference.</summary>
    private static int? FindReferenceAttribute(IEnumerable<XmlAttribute> attributes, string? namespaceUri, string name)
    {
        XmlAttribute? attribute = attributes.FirstOrDefault(item => item.NamespaceUri == namespaceUri && item.Name == name);
        if (attribute is null || attribute.Type != 0x01 || attribute.TypedData > int.MaxValue)
            return null;
        return (int)attribute.TypedData;
    }

    private static Chunk ReadChunk(ReadOnlySpan<byte> data, int offset, int enclosingEnd)
    {
        AndroidChunk chunk = AndroidBinaryFormat.ReadChunk(data, offset, enclosingEnd, "Binary XML chunk error");
        return new Chunk(chunk.Type, chunk.HeaderSize, chunk.Size, chunk.Offset);
    }

    private static void RequireNodeChunk(Chunk chunk, int minimumSize)
    {
        if (chunk.HeaderSize != 16 || chunk.Size < minimumSize)
            throw Invalid("XML node chunk header or size is malformed");
    }

    private static void RequireStringPool(bool sawStringPool)
    {
        if (!sawStringPool)
            throw Invalid("XML node chunk appeared before the string pool");
    }

    private static bool ValidSpecialAttributeIndex(ushort index, ushort attributeCount) =>
        index == 0 || index <= attributeCount;

    private static string GetString(string[] strings, uint index, string role)
    {
        if (index == NoIndex || index >= strings.Length)
            throw Invalid(role + " index is outside the string pool");
        return strings[index];
    }

    private static string? GetOptionalString(string[] strings, uint index, string role) =>
        index == NoIndex ? null : GetString(strings, index, role);

    private static ushort U16(ReadOnlySpan<byte> data, int offset)
        => AndroidBinaryFormat.U16(data, offset, "Binary XML chunk error");

    private static uint U32(ReadOnlySpan<byte> data, int offset)
        => AndroidBinaryFormat.U32(data, offset, "Binary XML chunk error");

    private static int ToInt(uint value, string role)
    {
        if (value > int.MaxValue)
            throw Invalid(role + " exceeds supported bounds");
        return (int)value;
    }

    private static int CheckedAdd(int left, int right, string role)
    {
        try { return checked(left + right); }
        catch (OverflowException error) { throw Invalid(role + " overflowed", error); }
    }

    private static int CheckedMultiply(int left, int right, string role)
    {
        try { return checked(left * right); }
        catch (OverflowException error) { throw Invalid(role + " overflowed", error); }
    }

    private static InvalidDataException Invalid(string message, Exception? inner = null) =>
        new("Binary XML chunk error: " + message, inner);

    private readonly record struct Chunk(ushort Type, int HeaderSize, int Size, int Offset);
    private sealed record XmlAttribute(string? NamespaceUri, string Name, string? Value, byte Type = 0, uint TypedData = 0);
    private sealed class IntentFilterState
    {
        public bool HasMain { get; set; }
        public bool HasLauncher { get; set; }
    }

    private sealed class ElementFrame
    {
        public ElementFrame(string name, string? namespaceUri)
        {
            Name = name;
            NamespaceUri = namespaceUri;
        }

        public string Name { get; }
        public string? NamespaceUri { get; }
        public int ActivityIndex { get; set; } = -1;
        public IntentFilterState? Filter { get; set; }
    }
}
