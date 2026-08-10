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
    /// binary AXML, and extract the FULL drawable bag: the effective default
    /// color as an android:color attribute PLUS one attribute per recognized
    /// state item (android:state_pressed="true" -> attr named state_pressed;
    /// android:state_hovered="true" -> attr named state_hovered) so ViewRuntime
    /// can drive real selectors (pressed/hovered feedback) instead of only the
    /// flattened default color. Default-state semantics match AOSP
    /// ColorStateList: prefer an item with NO state spec (skips
    /// android:state_enabled=false etc); fall back to the first literal color
    /// only when no stateless item exists. Returns null when the id is neither
    /// a style nor a file-backed drawable.</summary>
    private AndroidResourceStyleLink? ResolveDrawableBag(uint styleId)
    {
        string? path = ResourceFilePath(styleId);
        if (path is null || !_apk.ResourceFiles.TryGetValue(path, out byte[]? raw)) return null;
        try
        {
            AndroidXmlDocument document = AndroidBinaryXmlReader.Parse(raw);
            AndroidInflateAttribute[]? bag = FindDrawableBag(document.Root);
            if (bag is null) return null;
            return new AndroidResourceStyleLink(styleId, 0, bag);
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

    /// <summary>Walks a drawable/color XML tree and returns the FULL drawable
    /// bag (null when no color is present anywhere):
    /// — the effective default color as an attribute named <c>color</c>, matching
    ///   AOSP semantics: shape drawable -> a &lt;solid android:color&gt; inside a
    ///   &lt;shape&gt;, preferring the one in a STATELESS &lt;item&gt; (selector default)
    ///   over a state-specified item, and preferring a &lt;solid&gt; fill over a
    ///   &lt;ripple&gt;/&lt;item&gt; top-level color (the ripple color is touch feedback,
    ///   not background); ColorStateList -> a stateless &lt;item android:color&gt;,
    ///   skipping state-specified items (e.g. android:state_enabled=false);
    /// — a &lt;gradient android:startColor&gt; (GradientDrawable) exposed as an
    ///   attribute named <c>startColor</c> with the same default-vs-state rules
    ///   (ViewRuntime uses it as the fallback when the bag has no color/solid);
    /// — a &lt;gradient android:endColor&gt; exposed as <c>endColor</c> (sibling of
    ///   startColor, same rules), a &lt;gradient android:angle&gt; exposed as
    ///   <c>angle</c> and a &lt;gradient android:type&gt; (linear/radial/sweep)
    ///   exposed as <c>type</c>;
    /// — a &lt;stroke android:width/android:color&gt; exposed as <c>strokeWidth</c> /
    ///   <c>strokeColor</c> (a stroke paints a border even without a fill);
    /// — the &lt;shape android:shape&gt; (rectangle/oval/line/ring) exposed as
    ///   <c>shape</c>;
    /// — a &lt;corners android:radius&gt; exposed as an attribute named
    ///   <c>radius</c> (a raw dimension) with the same default-vs-state rules;
    /// — PLUS one attribute per item/solid with a recognized state specifier:
    ///   <c>android:state_pressed="true"</c> -> attr named <c>state_pressed</c>,
    ///   <c>android:state_hovered="true"</c> -> attr named <c>state_hovered</c>
    ///   (the first recognized specifier names the item; other states and
    ///   false-valued specifiers are not modeled and stay out of the bag).
    /// Internal so tests can construct selector trees directly.</summary>
    internal static AndroidInflateAttribute[]? FindDrawableBag(AndroidXmlElement element)
    {
        var builder = new DrawableBagBuilder();
        Walk(element, nearestItemHasState: null, nearestItemState: null, builder);
        // Priority: default-state solid fill (the real background) >
        // ColorStateList default item > any solid > any item > gradient
        // startColor (GradientDrawable fallback).
        AndroidInflateAttribute? color = builder.SolidDefault ?? builder.ItemDefault ?? builder.SolidAny ?? builder.ItemFallback;
        AndroidInflateAttribute? startColor = builder.GradientStartDefault ?? builder.GradientStartAny;
        AndroidInflateAttribute? endColor = builder.GradientEndDefault ?? builder.GradientEndAny;
        AndroidInflateAttribute? angle = builder.GradientAngleDefault ?? builder.GradientAngleAny;
        AndroidInflateAttribute? gradientType = builder.GradientTypeDefault ?? builder.GradientTypeAny;
        AndroidInflateAttribute? radius = builder.CornersDefault ?? builder.CornersAny;
        AndroidInflateAttribute? strokeWidth = builder.StrokeWidthDefault ?? builder.StrokeWidthAny;
        AndroidInflateAttribute? strokeColor = builder.StrokeColorDefault ?? builder.StrokeColorAny;
        AndroidInflateAttribute? shape = builder.ShapeDefault ?? builder.ShapeAny;
        // radius/angle/type/shape alone are not a paintable bag (a shape with
        // corners but no fill draws nothing) — the bag stays null until some
        // color is present. A stroke DOES paint (a border), so it counts.
        if (color is null && startColor is null && strokeColor is null && builder.StateColors.Count == 0) return null;
        var bag = new List<AndroidInflateAttribute>(builder.StateColors.Count + 9);
        if (color is not null) bag.Add(color);
        if (startColor is not null) bag.Add(startColor);
        if (endColor is not null) bag.Add(endColor);
        if (angle is not null) bag.Add(angle);
        if (gradientType is not null) bag.Add(gradientType);
        if (radius is not null) bag.Add(radius);
        if (strokeWidth is not null) bag.Add(strokeWidth);
        if (strokeColor is not null) bag.Add(strokeColor);
        if (shape is not null) bag.Add(shape);
        bag.AddRange(builder.StateColors);
        return bag.ToArray();
    }

    /// <summary>Accumulates the default-vs-state candidates across the walk.
    /// Every recognized element branch feeds the same pair of slots: the
    /// DEFAULT value (inside a stateless item) and the ANY fallback (the first
    /// value regardless of state), resolved as Default ?? Any at the end.</summary>
    private sealed class DrawableBagBuilder
    {
        public AndroidInflateAttribute? SolidDefault, SolidAny;
        public AndroidInflateAttribute? ItemDefault, ItemFallback;
        public AndroidInflateAttribute? GradientStartDefault, GradientStartAny;
        public AndroidInflateAttribute? GradientEndDefault, GradientEndAny;
        public AndroidInflateAttribute? GradientAngleDefault, GradientAngleAny;
        public AndroidInflateAttribute? GradientTypeDefault, GradientTypeAny;
        public AndroidInflateAttribute? CornersDefault, CornersAny;
        public AndroidInflateAttribute? StrokeWidthDefault, StrokeWidthAny;
        public AndroidInflateAttribute? StrokeColorDefault, StrokeColorAny;
        public AndroidInflateAttribute? ShapeDefault, ShapeAny;
        public List<AndroidInflateAttribute> StateColors { get; } = new();
    }

    private static void Walk(AndroidXmlElement element, bool? nearestItemHasState, AndroidXmlAttribute? nearestItemState, DrawableBagBuilder builder)
    {
        // Track the NEAREST ancestor <item>: whether it has any state spec
        // (for default-vs-fallback priority) and its recognized state attribute
        // (for selector bag entries; a <solid> inside a state <item> inherits it).
        bool? currentItemState = nearestItemHasState;
        AndroidXmlAttribute? currentItemStateAttr = nearestItemState;
        if (element.Name == "item")
        {
            currentItemState = HasStateSpecifiers(element);
            currentItemStateAttr = RecognizedStateAttribute(element);
        }

        // A <solid> inside a <shape>: the painted fill. Default = inside a
        // stateless <item> (the selector's default branch).
        if (element.Name == "solid" &&
            TryColorAttribute(element, out AndroidInflateAttribute? solidColor) &&
            solidColor is not null)
        {
            AddBagValue(currentItemState, solidColor, ref builder.SolidDefault, ref builder.SolidAny);
            if (currentItemStateAttr is not null) builder.StateColors.Add(NamedStateColor(solidColor, currentItemStateAttr));
        }
        // A <gradient android:startColor>: GradientDrawable without a <solid>.
        // Exposed as "startColor" with the same default-vs-state rules, plus
        // its sibling endColor/angle/type (the full GradientDrawable config).
        else if (element.Name == "gradient")
        {
            if (TryNamedAttribute(element, "startColor", out AndroidInflateAttribute? gradientColor) && gradientColor is not null)
            {
                AddBagValue(currentItemState, gradientColor, ref builder.GradientStartDefault, ref builder.GradientStartAny);
                if (currentItemStateAttr is not null) builder.StateColors.Add(NamedStateColor(gradientColor, currentItemStateAttr));
            }
            if (TryNamedAttribute(element, "endColor", out AndroidInflateAttribute? gradientEnd) && gradientEnd is not null)
                AddBagValue(currentItemState, gradientEnd, ref builder.GradientEndDefault, ref builder.GradientEndAny);
            if (TryNamedAttribute(element, "angle", out AndroidInflateAttribute? gradientAngle) && gradientAngle is not null)
                AddBagValue(currentItemState, gradientAngle, ref builder.GradientAngleDefault, ref builder.GradientAngleAny);
            if (TryNamedAttribute(element, "type", out AndroidInflateAttribute? gradientType) && gradientType is not null)
                AddBagValue(currentItemState, gradientType, ref builder.GradientTypeDefault, ref builder.GradientTypeAny);
        }
        // A direct <item android:color>: the stateless item is the default.
        else if (element.Name == "item" &&
                 TryColorAttribute(element, out AndroidInflateAttribute? itemColor) &&
                 itemColor is not null)
        {
            if (currentItemState == false && builder.ItemDefault is null) builder.ItemDefault = itemColor;
            builder.ItemFallback ??= itemColor;
            if (currentItemStateAttr is not null) builder.StateColors.Add(NamedStateColor(itemColor, currentItemStateAttr));
        }
        // <corners android:radius>: rounded-corner geometry, a sibling of
        // <solid> inside <shape>. Exposed as "radius" (a raw dimension — the
        // generic TryNamedAttribute handles it) with the same default-vs-state
        // rules as the color branches.
        else if (element.Name == "corners" &&
                 TryNamedAttribute(element, "radius", out AndroidInflateAttribute? cornerRadius) &&
                 cornerRadius is not null)
        {
            AddBagValue(currentItemState, cornerRadius, ref builder.CornersDefault, ref builder.CornersAny);
            if (currentItemStateAttr is not null) builder.StateColors.Add(NamedStateColor(cornerRadius, currentItemStateAttr));
        }
        // <stroke android:width/android:color>: border geometry + paint, a
        // sibling of <solid> inside <shape>. Exposed as "strokeWidth" (a raw
        // dimension) and "strokeColor" (renamed so it cannot collide with the
        // fill's "color" attr) with the same default-vs-state rules.
        else if (element.Name == "stroke")
        {
            if (TryNamedAttribute(element, "width", out AndroidInflateAttribute? strokeWidth) && strokeWidth is not null)
                AddBagValue(currentItemState, strokeWidth with { Name = "strokeWidth" }, ref builder.StrokeWidthDefault, ref builder.StrokeWidthAny);
            if (TryColorAttribute(element, out AndroidInflateAttribute? strokeColor) && strokeColor is not null)
                AddBagValue(currentItemState, strokeColor with { Name = "strokeColor" }, ref builder.StrokeColorDefault, ref builder.StrokeColorAny);
        }
        // <shape android:shape>: rectangle/oval/line/ring — the drawable's
        // geometry. Exposed as "shape" (a raw int enum).
        else if (element.Name == "shape" &&
                 TryNamedAttribute(element, "shape", out AndroidInflateAttribute? shapeAttr) &&
                 shapeAttr is not null)
        {
            AddBagValue(currentItemState, shapeAttr, ref builder.ShapeDefault, ref builder.ShapeAny);
        }

        foreach (AndroidXmlElement child in element.Children)
            Walk(child, currentItemState, currentItemStateAttr, builder);
    }

    /// <summary>Shared default-vs-state capture: the value inside a stateless
    /// item becomes the DEFAULT candidate; the first value seen (any state)
    /// becomes the ANY fallback. Resolution is Default ?? Any at the end.</summary>
    private static void AddBagValue(bool? currentItemState, AndroidInflateAttribute value, ref AndroidInflateAttribute? defaultValue, ref AndroidInflateAttribute? anyValue)
    {
        if (currentItemState == false && defaultValue is null) defaultValue = value;
        anyValue ??= value;
    }

    /// <summary>Repackages a color attribute as the selector state entry: same
    /// color value, but named after the recognized state (state_pressed /
    /// state_hovered) and carrying that state attribute's own resource id /
    /// namespace so the native can match by name or by id.</summary>
    private static AndroidInflateAttribute NamedStateColor(AndroidInflateAttribute color, AndroidXmlAttribute stateAttribute) =>
        color with { Name = stateAttribute.Name, ResourceId = stateAttribute.ResourceId, NamespaceUri = stateAttribute.NamespaceUri };

    /// <summary>First recognized state specifier of an item, or null. Order:
    /// state_pressed, then state_hovered (the first recognized specifier wins —
    /// no multi-state combinations are modeled). A specifier counts only when
    /// its literal value is true (android:state_pressed="true"); references and
    /// false values are not modeled.</summary>
    private static AndroidXmlAttribute? RecognizedStateAttribute(AndroidXmlElement element)
    {
        foreach (AndroidXmlAttribute attribute in element.Attributes)
        {
            if (attribute.Name == "state_pressed" && IsTrue(attribute.Value)) return attribute;
            if (attribute.Name == "state_hovered" && IsTrue(attribute.Value)) return attribute;
        }
        return null;
    }

    private static bool IsTrue(AndroidResourceValue value) =>
        value.Kind == AndroidResourceValueKind.Boolean && value.Data != 0;

    private static bool HasStateSpecifiers(AndroidXmlElement element) =>
        element.Attributes.Any(attribute => attribute.Name.StartsWith("state_", StringComparison.Ordinal));

    private static bool TryColorAttribute(AndroidXmlElement element, out AndroidInflateAttribute? color) =>
        TryNamedAttribute(element, "color", out color);

    private static bool TryNamedAttribute(AndroidXmlElement element, string name, out AndroidInflateAttribute? attribute)
    {
        AndroidXmlAttribute? found = element.Attributes.FirstOrDefault(item => item.Name == name);
        if (found is null) { attribute = null; return false; }
        attribute = new AndroidInflateAttribute(found.NamespaceUri, found.Name, found.ResourceId, AndroidInflateSerializer.FromValue(found.Value));
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
