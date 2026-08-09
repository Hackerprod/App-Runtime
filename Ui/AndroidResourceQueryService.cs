#nullable enable
using AndroidRuntime.Core.Apk;

namespace AndroidRuntime.Core.Ui;

/// <summary>
/// Phase-2 resource-query service: the ViewRuntime → App Runtime callback
/// channel, backed entirely by the existing (already real, tested) ARSC/AXML
/// format parsing. ViewRuntime calls these three operations to resolve raw
/// resource data; this side does NO interpretation (no style application, no
/// theme resolution, no density math — ViewRuntime owns what a resolved value
/// MEANS for view behavior).
/// </summary>
public sealed class AndroidResourceQueryService
{
    private readonly AndroidResourceResolver _resolver;
    private readonly LoadedApk _apk;

    public AndroidResourceQueryService(AndroidResourceResolver resolver, LoadedApk apk)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _apk = apk ?? throw new ArgumentNullException(nameof(apk));
    }

    /// <summary>resolve_resource(resource_id) -> raw typed value. A plain
    /// reference lookup: follows the reference chain exactly like real
    /// Resources.getValue would, returning the raw typed value untouched.</summary>
    public AndroidRawValue ResolveResource(uint resourceId)
    {
        AndroidResourceValue value = _resolver.Resolve(resourceId);
        return AndroidInflateSerializer.FromValue(value);
    }

    /// <summary>resolve_style(style_id) -> raw attr bag + parent style id.
    /// Returns ONE link of the style chain at a time; ViewRuntime walks the
    /// parent chain itself. Returns null when the style id is not a parsed
    /// style (including framework 0x01xxxxxx styles, not in the app table).</summary>
    public AndroidResourceStyleLink? ResolveStyle(uint styleId)
    {
        AndroidResourceStyle? style = _resolver.TryGetStyle(styleId);
        if (style is null) return null;
        var attributes = style.Attributes
            .Select(attribute => new AndroidInflateAttribute(null, null, attribute.AttributeId, AndroidInflateSerializer.FromValue(attribute.Value)))
            .ToArray();
        return new AndroidResourceStyleLink(styleId, style.Parent, attributes);
    }

    /// <summary>fetch_file(path) -> raw bytes for a resource file (bitmap/font).
    /// Returns null when the path is not present in the APK's resource files.</summary>
    public byte[]? FetchFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _apk.ResourceFiles.TryGetValue(path, out byte[]? bytes) ? bytes : null;
    }

    /// <summary>Access to the underlying resolver for the inflate-tree theme root
    /// and other queries ViewRuntime issues directly through the callbacks.</summary>
    public AndroidResourceResolver Resolver => _resolver;
}

public sealed record AndroidResourceStyleLink(uint StyleId, uint ParentStyleId, IReadOnlyList<AndroidInflateAttribute> Attributes);
