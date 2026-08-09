#nullable enable
using System.Text;

namespace AndroidRuntime.Core.Apk;

public sealed class AndroidResourceResolver
{
    private readonly IReadOnlyDictionary<uint, AndroidResourceEntry> _entries;
    private readonly IReadOnlyDictionary<uint, AndroidResourceStyle> _styles;
    private readonly IReadOnlyDictionary<(string Type, string Name), uint> _names;
    private readonly LoadedApk? _apk;
    private readonly AndroidResourceLimits _limits;

    private AndroidResourceResolver(IReadOnlyDictionary<uint, AndroidResourceEntry> entries, IReadOnlyDictionary<uint, AndroidResourceStyle> styles, LoadedApk? apk, AndroidResourceLimits limits)
    {
        _entries = entries; _styles = styles; _apk = apk; _limits = limits;
        var names = new Dictionary<(string, string), uint>();
        foreach (var entry in entries.Values) names.TryAdd((entry.Name.Type, entry.Name.Name), entry.Id);
        _names = names;
    }

    public static AndroidResourceResolver Create(LoadedApk apk, AndroidResourceLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(apk); limits ??= AndroidResourceLimits.Default;
        if (apk.ResourcesArsc is null) throw new InvalidDataException("ARSC_INVALID: APK has no resources.arsc");
        AndroidResourceTable table = AndroidResourceTable.Parse(apk.ResourcesArsc, limits);
        return new AndroidResourceResolver(table.Entries, table.Styles, apk, limits);
    }
    internal static AndroidResourceResolver ForTest(IReadOnlyDictionary<uint, AndroidResourceEntry> entries, AndroidResourceLimits limits)
    {
        var normalized = entries.ToDictionary(pair => pair.Key, pair => new AndroidResourceEntry(pair.Key, pair.Value.Name, pair.Value.Value, pair.Value.Density, pair.Value.SourceOrder));
        return new AndroidResourceResolver(normalized, new Dictionary<uint, AndroidResourceStyle>(), null, limits);
    }
    internal static AndroidResourceResolver ForTestWithStyles(IReadOnlyDictionary<uint, AndroidResourceEntry> entries, IReadOnlyDictionary<uint, AndroidResourceStyle> styles, AndroidResourceLimits limits)
    {
        var normalized = entries.ToDictionary(pair => pair.Key, pair => new AndroidResourceEntry(pair.Key, pair.Value.Name, pair.Value.Value, pair.Value.Density, pair.Value.SourceOrder));
        return new AndroidResourceResolver(normalized, styles, null, limits);
    }

    public uint GetIdentifier(string type, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type); ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _names.TryGetValue((type, name), out uint id) ? id : throw new KeyNotFoundException($"ARSC_NOT_FOUND: {type}/{name}");
    }
    public AndroidResourceName GetResourceName(uint id) => _entries.TryGetValue(id, out var entry) ? entry.Name : throw new KeyNotFoundException($"ARSC_NOT_FOUND: 0x{id:x8}");

    public AndroidResourceValue Resolve(uint id)
    {
        var path = new List<uint>(); int values = 0, bytes = 0;
        while (true)
        {
            if (path.Count >= _limits.MaxReferenceDepth) throw new InvalidDataException($"ARSC_REFERENCE_DEPTH: exceeds {_limits.MaxReferenceDepth} at 0x{id:x8}");
            int cycle = path.IndexOf(id);
            if (cycle >= 0)
            {
                var cycleIds = path.Skip(cycle).Append(id).Select(value => $"0x{value:x8}");
                throw new InvalidDataException("ARSC_REFERENCE_CYCLE: " + string.Join(" -> ", cycleIds));
            }
            path.Add(id);
            if (++values > _limits.MaxResolvedValues) throw new InvalidDataException($"ARSC_REFERENCE_COUNT: exceeds {_limits.MaxResolvedValues}");
            if (!_entries.TryGetValue(id, out var entry)) throw new KeyNotFoundException($"ARSC_NOT_FOUND: 0x{id:x8}");
            AndroidResourceValue value = entry.Value;
            if (value.Kind == AndroidResourceValueKind.String)
            {
                bytes = checked(bytes + Encoding.UTF8.GetByteCount(value.AsString()));
                if (bytes > _limits.MaxResolvedStringBytes) throw new InvalidDataException($"ARSC_REFERENCE_BYTES: exceeds {_limits.MaxResolvedStringBytes}");
            }
            if (value.Kind != AndroidResourceValueKind.Reference) return value;
            id = value.AsReference();
        }
    }

    public AndroidResourceValue ResolveAttribute(AndroidXmlElement element, string name, string? namespaceUri = "http://schemas.android.com/apk/res/android")
    {
        AndroidXmlAttribute attribute = element.Attributes.FirstOrDefault(item => item.Name == name && item.NamespaceUri == namespaceUri)
            ?? throw new KeyNotFoundException($"AXML_ATTRIBUTE_NOT_FOUND: {name}");
        return attribute.Value.Kind == AndroidResourceValueKind.Reference ? Resolve(attribute.Value.AsReference()) : attribute.Value;
    }

    /// <summary>Looks up a style bag by id. Returns null when the id is not a
    /// parsed style (including framework 0x01xxxxxx styles, which are not part
    /// of the app's own table).</summary>
    public AndroidResourceStyle? TryGetStyle(uint id) => _styles.TryGetValue(id, out AndroidResourceStyle? style) ? style : null;

    /// <summary>
    /// Resolves a style attribute through the style's parent chain, bounded by
    /// the reference-depth limit. Real Android walks child → parent and uses the
    /// first value found; a missing style or framework-only parent chain yields
    /// false. The returned value may itself be a reference (e.g. a color selector
    /// or theme attribute) that callers resolve further.
    /// </summary>
    public bool TryResolveStyleAttribute(uint styleId, uint attributeId, out AndroidResourceValue value)
    {
        var seen = new HashSet<uint>();
        uint current = styleId;
        for (int depth = 0; depth < _limits.MaxReferenceDepth; depth++)
        {
            if (!seen.Add(current)) break;
            if (_styles.TryGetValue(current, out AndroidResourceStyle? style))
            {
                foreach (AndroidResourceStyleAttribute attribute in style.Attributes)
                {
                    if (attribute.AttributeId != attributeId) continue;
                    value = attribute.Value;
                    return true;
                }
                current = style.Parent;
            }
            else break;
        }
        value = default;
        return false;
    }

    /// <summary>Resolves a style chain value down to a concrete ARGB color when
    /// the chain terminates in one. References are followed through the entry
    /// table; framework references (0x01xxxxxx) and unresolved resources yield
    /// null so callers can apply a bounded fallback. Theme-attribute references
    /// (kind Attribute) also yield null — resolving those needs a theme context.</summary>
    public uint? TryResolveStyleColor(AndroidResourceValue value)
    {
        if (value.Kind == AndroidResourceValueKind.Color) return value.AsColor();
        if (value.Kind != AndroidResourceValueKind.Reference) return null;
        uint reference = value.AsReference();
        if ((reference >> 24) == 0x01) return null;
        try
        {
            AndroidResourceValue resolved = Resolve(reference);
            return resolved.Kind == AndroidResourceValueKind.Color ? resolved.AsColor() : null;
        }
        catch (KeyNotFoundException) { return null; }
    }

    public AndroidXmlDocument LoadLayout(string name)
    {
        if (_apk is null) throw new InvalidOperationException("Resolver has no APK resource files.");
        return LoadLayout(GetIdentifier("layout", name));
    }

    public AndroidXmlDocument LoadLayout(uint id)
    {
        if (_apk is null) throw new InvalidOperationException("Resolver has no APK resource files.");
        AndroidResourceName name = GetResourceName(id);
        if (name.Type != "layout") throw new InvalidDataException($"ARSC_INVALID: 0x{id:x8} is {name.Type}/{name.Name}, not a layout");
        AndroidResourceValue value = Resolve(id);
        if (value.Kind != AndroidResourceValueKind.String) throw new InvalidDataException($"ARSC_INVALID: layout/{name.Name} does not resolve to a resource path");
        string path = CanonicalResourcePath(value.AsString());
        if (!_apk.ResourceFiles.TryGetValue(path, out byte[]? data)) throw new InvalidDataException($"ARSC_MISSING_FILE: {path}");
        return AndroidBinaryXmlReader.Parse(data);
    }

    private static string CanonicalResourcePath(string path)
    {
        if (!path.StartsWith("res/layout", StringComparison.Ordinal) || !path.EndsWith(".xml", StringComparison.Ordinal) || path.Contains('\\') || path.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException("ARSC_INVALID_PATH: " + path);
        return path;
    }
}
