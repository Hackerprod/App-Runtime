#nullable enable
using System.Collections.ObjectModel;

namespace AndroidRuntime.Core.Apk;

public enum AndroidXmlEventKind { StartNamespace, EndNamespace, StartElement, EndElement }

public sealed record AndroidXmlEvent(AndroidXmlEventKind Kind, int LineNumber, string? NamespaceUri, string Name, IReadOnlyList<AndroidXmlAttribute> Attributes);

public sealed class AndroidXmlDocument
{
    internal AndroidXmlDocument(AndroidXmlElement root, IReadOnlyList<AndroidXmlEvent> events) { Root = root; Events = events; }
    public AndroidXmlElement Root { get; }
    public IReadOnlyList<AndroidXmlEvent> Events { get; }
}

public sealed class AndroidXmlElement
{
    internal AndroidXmlElement(string? namespaceUri, string name, int lineNumber, IEnumerable<AndroidXmlAttribute> attributes)
    { NamespaceUri = namespaceUri; Name = name; LineNumber = lineNumber; Attributes = Array.AsReadOnly(attributes.ToArray()); }
    public string? NamespaceUri { get; }
    public string Name { get; }
    public int LineNumber { get; }
    public IReadOnlyList<AndroidXmlAttribute> Attributes { get; }
    public IReadOnlyList<AndroidXmlElement> Children => new ReadOnlyCollection<AndroidXmlElement>(_children);
    internal List<AndroidXmlElement> MutableChildren => _children;
    private readonly List<AndroidXmlElement> _children = [];
}

public sealed record AndroidXmlAttribute(string? NamespaceUri, string Name, uint ResourceId, string? RawValue, AndroidResourceValue Value);

public sealed class AndroidBinaryXmlLimits
{
    public static AndroidBinaryXmlLimits Default { get; } = new();
    public AndroidBinaryXmlLimits(int maxElements = 16384, int maxAttributes = 65536, int maxDepth = 256, int maxStrings = 65536, int maxDecodedStringBytes = 16 * 1024 * 1024)
    {
        if (maxElements <= 0 || maxAttributes <= 0 || maxDepth <= 0 || maxStrings <= 0 || maxDecodedStringBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxElements));
        MaxElements = maxElements; MaxAttributes = maxAttributes; MaxDepth = maxDepth; MaxStrings = maxStrings; MaxDecodedStringBytes = maxDecodedStringBytes;
    }
    public int MaxElements { get; } public int MaxAttributes { get; } public int MaxDepth { get; } public int MaxStrings { get; } public int MaxDecodedStringBytes { get; }
}

public static class AndroidBinaryXmlReader
{
    private const string Prefix = "AXML_INVALID";
    private const uint NoIndex = uint.MaxValue;

    public static AndroidXmlDocument Parse(byte[] data, AndroidBinaryXmlLimits? limits = null) { ArgumentNullException.ThrowIfNull(data); return Parse((ReadOnlySpan<byte>)data, limits); }
    public static AndroidXmlDocument Parse(ReadOnlySpan<byte> data, AndroidBinaryXmlLimits? limits = null)
    {
        try { return ParseCore(data, limits); }
        catch (OverflowException error) { throw AndroidBinaryFormat.Invalid(Prefix, "integer arithmetic overflow", error); }
    }

    private static AndroidXmlDocument ParseCore(ReadOnlySpan<byte> data, AndroidBinaryXmlLimits? limits)
    {
        limits ??= AndroidBinaryXmlLimits.Default;
        AndroidChunk root = AndroidBinaryFormat.ReadChunk(data, 0, data.Length, Prefix);
        if (root.Type != 0x0003 || root.HeaderSize != 8 || root.Size != data.Length) throw Invalid("root must be a complete RES_XML_TYPE chunk");
        string[] strings = [];
        uint[] resourceMap = [];
        bool sawPool = false;
        var stack = new List<AndroidXmlElement>();
        var namespaces = new List<(uint Prefix, uint Uri)>();
        var events = new List<AndroidXmlEvent>();
        AndroidXmlElement? documentRoot = null;
        int elementCount = 0, attributeTotal = 0;
        for (int offset = root.HeaderSize; offset < root.End;)
        {
            AndroidChunk chunk = AndroidBinaryFormat.ReadChunk(data, offset, root.End, Prefix);
            switch (chunk.Type)
            {
                case 0x0001:
                    if (sawPool || offset != root.HeaderSize) throw Invalid("string pool must be the first child and appear once");
                    strings = AndroidBinaryFormat.ReadStringPool(data, chunk, Prefix, limits.MaxStrings, limits.MaxDecodedStringBytes); sawPool = true; break;
                case 0x0180:
                    RequirePool(sawPool);
                    if (chunk.HeaderSize != 8 || (chunk.Size - 8) % 4 != 0) throw Invalid("resource map is malformed");
                    resourceMap = new uint[(chunk.Size - 8) / 4];
                    for (int i = 0; i < resourceMap.Length; i++) resourceMap[i] = U32(data, offset + 8 + i * 4);
                    break;
                case 0x0100:
                case 0x0101:
                {
                    RequireNode(chunk, 24); RequirePool(sawPool);
                    uint prefix = U32(data, offset + 16), uri = U32(data, offset + 20);
                    string prefixText = Optional(strings, prefix, "namespace prefix") ?? "";
                    string uriText = Required(strings, uri, "namespace URI");
                    if (chunk.Type == 0x0100) namespaces.Add((prefix, uri));
                    else { if (namespaces.Count == 0 || namespaces[^1] != (prefix, uri)) throw Invalid("namespace end does not match active namespace"); namespaces.RemoveAt(namespaces.Count - 1); }
                    events.Add(new AndroidXmlEvent(chunk.Type == 0x0100 ? AndroidXmlEventKind.StartNamespace : AndroidXmlEventKind.EndNamespace, checked((int)U32(data, offset + 8)), uriText, prefixText, Array.Empty<AndroidXmlAttribute>()));
                    break;
                }
                case 0x0102:
                {
                    RequireNode(chunk, 36); RequirePool(sawPool);
                    if (++elementCount > limits.MaxElements) throw Invalid($"element count exceeds quota {limits.MaxElements}");
                    if (stack.Count >= limits.MaxDepth) throw Invalid($"element depth exceeds quota {limits.MaxDepth}");
                    string? ns = Optional(strings, U32(data, offset + 16), "element namespace");
                    string name = Required(strings, U32(data, offset + 20), "element name");
                    ushort attributeStart = U16(data, offset + 24), attributeSize = U16(data, offset + 26), count = U16(data, offset + 28);
                    if (attributeStart < 20 || attributeSize != 20) throw Invalid("unsupported start-element attribute layout");
                    attributeTotal = checked(attributeTotal + count); if (attributeTotal > limits.MaxAttributes) throw Invalid($"attribute count exceeds quota {limits.MaxAttributes}");
                    int attributesOffset = checked(offset + 16 + attributeStart), attributesEnd = checked(attributesOffset + count * attributeSize);
                    if (attributesOffset < offset + 36 || attributesEnd > chunk.End) throw Invalid("attributes exceed start-element bounds");
                    var attributes = new AndroidXmlAttribute[count];
                    for (int i = 0; i < count; i++)
                    {
                        int at = attributesOffset + i * 20;
                        string? ans = Optional(strings, U32(data, at), "attribute namespace");
                        uint nameIndex = U32(data, at + 4);
                        string aname = Required(strings, nameIndex, "attribute name");
                        string? raw = Optional(strings, U32(data, at + 8), "attribute raw value");
                        if (U16(data, at + 12) != 8 || data[at + 14] != 0) throw Invalid("attribute typed value header is malformed");
                        var value = AndroidResourceValue.FromBinary(data[at + 15], U32(data, at + 16), strings, Prefix);
                        uint resourceId = nameIndex < resourceMap.Length ? resourceMap[nameIndex] : 0;
                        attributes[i] = new AndroidXmlAttribute(ans, aname, resourceId, raw, value);
                    }
                    int line = checked((int)U32(data, offset + 8));
                    var element = new AndroidXmlElement(ns, name, line, attributes);
                    if (stack.Count == 0) { if (documentRoot is not null) throw Invalid("document has multiple roots"); documentRoot = element; }
                    else stack[^1].MutableChildren.Add(element);
                    stack.Add(element);
                    events.Add(new AndroidXmlEvent(AndroidXmlEventKind.StartElement, line, ns, name, element.Attributes));
                    break;
                }
                case 0x0103:
                {
                    RequireNode(chunk, 24); RequirePool(sawPool);
                    string? ns = Optional(strings, U32(data, offset + 16), "element namespace"); string name = Required(strings, U32(data, offset + 20), "element name");
                    if (stack.Count == 0 || stack[^1].Name != name || stack[^1].NamespaceUri != ns) throw Invalid("end-element does not match open element");
                    stack.RemoveAt(stack.Count - 1); events.Add(new AndroidXmlEvent(AndroidXmlEventKind.EndElement, checked((int)U32(data, offset + 8)), ns, name, Array.Empty<AndroidXmlAttribute>())); break;
                }
                default: throw Invalid($"unsupported chunk type 0x{chunk.Type:x4}");
            }
            offset = chunk.End;
        }
        if (!sawPool || documentRoot is null || stack.Count != 0 || namespaces.Count != 0) throw Invalid("document ended incomplete");
        return new AndroidXmlDocument(documentRoot, Array.AsReadOnly(events.ToArray()));
    }

    public static IReadOnlyList<AndroidXmlEvent> ReadEvents(byte[] data, AndroidBinaryXmlLimits? limits = null) => Parse(data, limits).Events;
    private static void RequirePool(bool value) { if (!value) throw Invalid("node appeared before string pool"); }
    private static void RequireNode(AndroidChunk chunk, int minimum) { if (chunk.HeaderSize != 16 || chunk.Size < minimum) throw Invalid("node chunk header is malformed"); }
    private static string Required(string[] pool, uint index, string field) => Optional(pool, index, field) ?? throw Invalid(field + " cannot be null");
    private static string? Optional(string[] pool, uint index, string field) { if (index == NoIndex) return null; if (index >= pool.Length) throw Invalid(field + " index is outside string pool"); return pool[index]; }
    private static ushort U16(ReadOnlySpan<byte> data, int offset) => AndroidBinaryFormat.U16(data, offset, Prefix);
    private static uint U32(ReadOnlySpan<byte> data, int offset) => AndroidBinaryFormat.U32(data, offset, Prefix);
    private static InvalidDataException Invalid(string message) => AndroidBinaryFormat.Invalid(Prefix, message);
}
