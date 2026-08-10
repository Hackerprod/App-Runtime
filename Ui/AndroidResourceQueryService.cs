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
    /// style (including framework 0x01xxxxxx styles, not in the app table).
    /// Each attribute carries its name (framework attrs resolved from the
    /// bounded map; app attrs from the resource table) AND its name_id, so the
    /// native can both apply by name and match ?attr/&lt;id&gt; by id.
    ///
    /// DRAWABLE ids: a drawable resource is structurally a bag (AOSP parses
    /// drawable XML into a Drawable whose solid/selector default color is what
    /// paints). When the id is not a style entry, resolve it to its file path,
    /// parse the binary AXML (same reader as layouts), and return the drawable's
    /// effective default color as a single-attribute bag (android:color). This
    /// lets ViewRuntime's resolve_drawable_solid consume drawables through the
    /// SAME resolve_style channel — no raw bytes cross for drawables; fetch_file
    /// stays reserved for images/fonts.</summary>
    public AndroidResourceStyleLink? ResolveStyle(uint styleId)
    {
        AndroidResourceStyle? style = _resolver.TryGetStyle(styleId);
        if (style is not null)
        {
            var attributes = style.Attributes
                .Select(attribute => new AndroidInflateAttribute(null, AttributeName(_resolver, attribute.AttributeId), attribute.AttributeId, AndroidInflateSerializer.FromValue(attribute.Value)))
                .ToArray();
            return new AndroidResourceStyleLink(styleId, style.Parent, attributes);
        }
        return ResolveDrawableBag(styleId);
    }

    /// <summary>Drawable fallback: resolve the id to its file path, parse the
    /// binary AXML, and extract the effective default color as a bag of one
    /// android:color attribute. Default-state semantics match AOSP ColorStateList:
    /// prefer an item with NO state spec (skips android:state_enabled=false etc);
    /// fall back to the first literal color only when no stateless item exists.
    /// Returns null when the id is neither a style nor a file-backed drawable.</summary>
    private AndroidResourceStyleLink? ResolveDrawableBag(uint styleId)
    {
        string? path = ResourceFilePath(styleId);
        if (path is null || !_apk.ResourceFiles.TryGetValue(path, out byte[]? raw)) return null;
        try
        {
            AndroidXmlDocument document = AndroidBinaryXmlReader.Parse(raw);
            AndroidInflateAttribute? color = FindDrawableColor(document.Root);
            if (color is null) return null;
            return new AndroidResourceStyleLink(styleId, 0, [color]);
        }
        catch (InvalidDataException) { return null; }
    }

    private string? ResourceFilePath(uint resourceId)
    {
        try
        {
            AndroidResourceValue value = _resolver.Resolve(resourceId);
            return value.Kind == AndroidResourceValueKind.String ? value.AsString() : null;
        }
        catch (KeyNotFoundException) { return null; }
    }

    /// <summary>Walks a drawable/color XML tree for its effective default color,
    /// matching AOSP semantics:
    /// — shape drawable: a &lt;solid android:color&gt; inside a &lt;shape&gt;, preferring
    ///   the one in a STATELESS &lt;item&gt; (selector default) over a state-specified
    ///   item, and preferring a &lt;solid&gt; fill over a &lt;ripple&gt;/&lt;item&gt;
    ///   top-level color (the ripple color is touch feedback, not background).
    /// — ColorStateList: a stateless &lt;item android:color&gt; (the default state),
    ///   skipping state-specified items (e.g. android:state_enabled=false).
    /// Returns null when no color is present.</summary>
    private static AndroidInflateAttribute? FindDrawableColor(AndroidXmlElement element)
    {
        AndroidInflateAttribute? solidDefault = null;
        AndroidInflateAttribute? solidAny = null;
        AndroidInflateAttribute? itemDefault = null;
        AndroidInflateAttribute? itemFallback = null;
        Walk(element, nearestItemHasState: null, ref solidDefault, ref solidAny, ref itemDefault, ref itemFallback);
        // Priority: default-state solid fill (the real background) >
        // ColorStateList default item > any solid > any item.
        return solidDefault ?? itemDefault ?? solidAny ?? itemFallback;
    }

    private static void Walk(AndroidXmlElement element, bool? nearestItemHasState, ref AndroidInflateAttribute? solidDefault, ref AndroidInflateAttribute? solidAny, ref AndroidInflateAttribute? itemDefault, ref AndroidInflateAttribute? itemFallback)
    {
        // Track the NEAREST ancestor <item> and whether it has a state spec.
        bool? currentItemState = nearestItemHasState;
        if (element.Name == "item")
            currentItemState = HasStateSpecifiers(element);

        // A <solid> inside a <shape>: the painted fill. Default = inside a
        // stateless <item> (the selector's default branch).
        if (element.Name == "solid" &&
            TryColorAttribute(element, out AndroidInflateAttribute? solidColor) &&
            solidColor is not null)
        {
            if (currentItemState == false && solidDefault is null) solidDefault = solidColor;
            solidAny ??= solidColor;
        }
        // A direct <item android:color>: the stateless item is the default.
        else if (element.Name == "item" &&
                 TryColorAttribute(element, out AndroidInflateAttribute? itemColor) &&
                 itemColor is not null)
        {
            if (currentItemState == false && itemDefault is null) itemDefault = itemColor;
            itemFallback ??= itemColor;
        }

        foreach (AndroidXmlElement child in element.Children)
            Walk(child, currentItemState, ref solidDefault, ref solidAny, ref itemDefault, ref itemFallback);
    }

    private static bool HasStateSpecifiers(AndroidXmlElement element) =>
        element.Attributes.Any(attribute => attribute.Name.StartsWith("state_", StringComparison.Ordinal));

    private static bool TryColorAttribute(AndroidXmlElement element, out AndroidInflateAttribute? color)
    {
        AndroidXmlAttribute? attribute = element.Attributes.FirstOrDefault(item => item.Name == "color");
        if (attribute is null) { color = null; return false; }
        color = new AndroidInflateAttribute(attribute.NamespaceUri, attribute.Name, attribute.ResourceId, AndroidInflateSerializer.FromValue(attribute.Value));
        return true;
    }

    /// <summary>Resolves an attribute resource id to its short name. App attrs
    /// (0x7f03xxxx) come from the resource table; framework attrs (0x01xxxxxx)
    /// are not in the app table, so they use the bounded map of the view
    /// attributes this project's native inflater applies. Unknown ids return
    /// null (the native matches those by name_id only).</summary>
    private static string? AttributeName(AndroidResourceResolver resolver, uint attributeId)
    {
        if ((attributeId >> 24) == 0x7f)
        {
            try { return resolver.GetResourceName(attributeId).Name; }
            catch (KeyNotFoundException) { return null; }
        }
        return FrameworkAttributeNames.TryGetValue(attributeId, out string? name) ? name : null;
    }

    private static readonly Dictionary<uint, string> FrameworkAttributeNames = new()
    {
        [0x01010034] = "textAppearance",
        [0x01010098] = "textColor",
        [0x01010095] = "textSize",
        [0x0101014f] = "text",
        [0x010100d4] = "background",
        [0x010100dc] = "visibility",
        [0x010100af] = "gravity",
        [0x010100c4] = "orientation",
        [0x010100d5] = "padding",
        [0x010100f4] = "layout_width",
        [0x010100f5] = "layout_height",
        [0x010100f6] = "layout_marginLeft",
        [0x010100f7] = "layout_marginTop",
        [0x010100f8] = "layout_marginRight",
        [0x010100f9] = "layout_marginBottom",
        [0x010100fa] = "layout_margin",
        [0x010100f3] = "layout_gravity",
        [0x010101e0] = "layout_weight",
        [0x010101ea] = "weightSum",
        [0x01010119] = "src",
        [0x01010048] = "contentDescription",
        [0x0101013f] = "minWidth",
        [0x01010140] = "minHeight",
        [0x010100b1] = "singleLine",
        [0x0101001e] = "enabled",
        [0x010100aa] = "checked",
        [0x0101011b] = "scaleType",
        [0x010100d6] = "paddingLeft",
        [0x010100d7] = "paddingTop",
        [0x010100d8] = "paddingRight",
        [0x010100d9] = "paddingBottom",
        [0x01010150] = "hint",
        [0x01010002] = "style",
        [0x01010036] = "textColorHighlight",
        [0x01010038] = "textColorHint",
        [0x01010054] = "windowBackground",
        [0x0101009a] = "textColorLink",
        [0x010103b1] = "textAlignment",
    };

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
