#nullable enable
using AndroidRuntime.Core.Apk;

namespace AndroidRuntime.Core.Ui;

/// <summary>Fail-closed inflater for the deliberately bounded platform View subset.</summary>
public sealed class AndroidLayoutInflater
{
    private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";
    private readonly AndroidResourceResolver _resources;
    private readonly AndroidUiLimits _limits;

    public AndroidLayoutInflater(AndroidResourceResolver resources, AndroidUiLimits? limits = null)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _limits = limits ?? new AndroidUiLimits();
        _limits.Validate();
    }

    public int CreatedViewCount { get; private set; }

    public AndroidViewNode Inflate(AndroidXmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        int count = 0;
        try
        {
            AndroidViewNode result = InflateElement(document.Root, 1, ref count);
            CreatedViewCount = count;
            return result;
        }
        catch
        {
            CreatedViewCount = 0;
            throw;
        }
    }

    private AndroidViewNode InflateElement(AndroidXmlElement element, int depth, ref int count)
    {
        if (depth > _limits.MaxViewDepth || ++count > _limits.MaxViewCount)
            throw new AndroidUiQuotaExceededException("View inflation quota exceeded.");
        // <include layout="@layout/...">: inflates the referenced layout INLINE
        // (real Android merges the included root into this position). The layout
        // attribute is an un-namespaced resource reference (android: prefixed or
        // bare), so read it directly rather than through the android namespace.
        if (element.Name == "include")
        {
            AndroidXmlAttribute? includeAttribute = element.Attributes.FirstOrDefault(item => item.Name == "layout");
            if (includeAttribute is null || includeAttribute.Value.Kind != AndroidResourceValueKind.Reference)
                throw new InvalidDataException($"UI_INVALID_INCLUDE: include without layout reference at line {element.LineNumber}");
            AndroidXmlDocument included = _resources.LoadLayout(includeAttribute.Value.AsReference());
            return InflateElement(included.Root, depth + 1, ref count);
        }
        int id = ReadId(element);
        // <merge>: children inflate into the PARENT container (real Android
        // merges them; the merge tag itself produces no view). Modeled as an
        // anonymous LinearLayout whose children are the merge's children.
        if (element.Name == "merge")
        {
            var mergeHost = new AndroidLinearLayoutNode(0);
            foreach (AndroidXmlElement child in element.Children)
                mergeHost.Add(InflateElement(child, depth + 1, ref count));
            return mergeHost;
        }
        if (element.Name is "androidx.appcompat.widget.ContentFrameLayout" or "android.widget.FrameLayout")
        {
            // appcompat's scaffold ContentFrameLayout is registered under its own
            // id AND android.R.id.content (0x01020002) — real Android aliases the
            // same view; applyFixedSizeWindow looks it up by the framework id.
            var frame = new AndroidLinearLayoutNode(id);
            if (id != 0)
                frame.AliasIds.Add(0x01020002);
            frame.GuestDescriptor = "L" + element.Name.Replace('.', '/') + ";";
            ApplyCommon(frame, element);
            foreach (AndroidXmlElement child in element.Children) frame.Add(InflateElement(child, depth + 1, ref count));
            return frame;
        }
        AndroidViewNode node = element.Name switch
        {
            "LinearLayout" or "android.widget.LinearLayout" => new AndroidLinearLayoutNode(id),
            // androidx container classes are layout-equivalent to LinearLayout:
            // appcompat's own window scaffolding (FitWindowsFrameLayout etc.)
            // inflates as a bounded container. ContentFrameLayout registers under
            // its own descriptor so appcompat's findViewById(R.id.content) cast
            // resolves; its OnAttachListener surface is bound as a no-op.
            "androidx.appcompat.widget.FitWindowsFrameLayout" or "androidx.appcompat.widget.FitWindowsLinearLayout" or "android.widget.FrameLayout" or "FrameLayout" or "androidx.appcompat.widget.ContentFrameLayout" or "androidx.appcompat.widget.ViewStubCompat" or "androidx.cardview.widget.CardView" => new AndroidLinearLayoutNode(id) { GuestDescriptor = "L" + (element.Name.Contains('.') ? element.Name : "android.widget." + element.Name).Replace('.', '/') + ";" },
            "TextView" or "android.widget.TextView" or "EditText" or "android.widget.EditText" or "androidx.appcompat.widget.AppCompatEditText" => new AndroidTextViewNode(id),
            "Button" or "android.widget.Button" or "androidx.appcompat.widget.AppCompatButton" => new AndroidButtonNode(id),
            "ImageView" or "android.widget.ImageView" => new AndroidImageViewNode(id),
            // ProgressBar renders as a static box (no indeterminate animation in
            // the bounded render subset); the view exists, finds by id, and
            // accepts setVisibility so app flows keep working.
            "ProgressBar" or "android.widget.ProgressBar" => new AndroidImageViewNode(id),
            _ => throw new NotSupportedException($"UI_UNKNOWN_CLASS: {element.Name} at line {element.LineNumber}")
        };
        ApplyCommon(node, element);
        if (node is AndroidLinearLayoutNode linear)
        {
            linear.Orientation = ReadInteger(element, "orientation", 0) switch
            {
                0 => AndroidOrientation.Horizontal,
                1 => AndroidOrientation.Vertical,
                int value => throw new InvalidDataException($"UI_INVALID_ATTRIBUTE: orientation={value}")
            };
            linear.PaddingDp = ReadDimension(element, "padding", 0, AndroidDimensionUnit.Dp);
            foreach (AndroidXmlElement child in element.Children) linear.Add(InflateElement(child, depth + 1, ref count));
        }
        else if (element.Children.Count != 0)
        {
            throw new InvalidDataException($"UI_INVALID_TREE: {element.Name} cannot contain child views");
        }
        if (node is AndroidTextViewNode text)
        {
            text.Text = ReadString(element, "text") ?? string.Empty;
            text.TextSizeSp = ReadDimension(element, "textSize", 16, AndroidDimensionUnit.Sp);
            text.TextColor = ReadColor(element, "textColor", new AndroidColor(255, 32, 32, 32));
        }
        return node;
    }

    private void ApplyCommon(AndroidViewNode node, AndroidXmlElement element)
    {
        node.LayoutWidth = ReadLayoutDimension(element, "layout_width");
        node.LayoutHeight = ReadLayoutDimension(element, "layout_height");
        node.ContentDescription = ReadString(element, "contentDescription");
        node.XmlOnClick = ReadString(element, "onClick");
        if (TryValue(element, "background", out AndroidResourceValue background))
        {
            // Colors render; drawable resources (shapes, selectors, bitmaps) are
            // outside the bounded render subset and are ignored rather than
            // crashing the APK (documented visual subset).
            if (background.Kind != AndroidResourceValueKind.Color) return;
            node.BackgroundColor = Color(background.AsColor());
        }
    }

    private int ReadId(AndroidXmlElement element)
    {
        AndroidXmlAttribute? attribute = Attribute(element, "id");
        if (attribute is null) return 0;
        if (attribute.Value.Kind != AndroidResourceValueKind.Reference) throw new InvalidDataException("UI_INVALID_ATTRIBUTE: id must be a resource reference");
        return unchecked((int)attribute.Value.AsReference());
    }

    private AndroidLayoutDimension ReadLayoutDimension(AndroidXmlElement element, string name)
    {
        if (!TryValue(element, name, out AndroidResourceValue value)) throw new InvalidDataException("UI_REQUIRED_ATTRIBUTE: " + name);
        if (value.Kind == AndroidResourceValueKind.Integer)
            return value.AsInteger() switch
            {
                -1 => AndroidLayoutDimension.MatchParent,
                -2 => AndroidLayoutDimension.WrapContent,
                int other => throw new InvalidDataException($"UI_INVALID_ATTRIBUTE: {name}={other}")
            };
        if (value.Kind == AndroidResourceValueKind.Dimension)
        {
            AndroidDimension dimension = value.AsDimension();
            return dimension.Unit is AndroidDimensionUnit.Dp or AndroidDimensionUnit.Px
                ? AndroidLayoutDimension.Exact(dimension.Value)
                : throw new NotSupportedException($"UI_UNSUPPORTED_ATTRIBUTE: {name} unit {dimension.Unit}");
        }
        throw new InvalidDataException($"UI_INVALID_ATTRIBUTE: {name} is {value.Kind}");
    }

    private string? ReadString(AndroidXmlElement element, string name)
    {
        if (!TryValue(element, name, out AndroidResourceValue value)) return null;
        return value.Kind == AndroidResourceValueKind.String ? value.AsString() : throw new InvalidDataException($"UI_INVALID_ATTRIBUTE: {name} is {value.Kind}");
    }

    private int ReadInteger(AndroidXmlElement element, string name, int fallback)
    {
        if (!TryValue(element, name, out AndroidResourceValue value)) return fallback;
        return value.Kind == AndroidResourceValueKind.Integer ? value.AsInteger() : throw new InvalidDataException($"UI_INVALID_ATTRIBUTE: {name} is {value.Kind}");
    }

    private float ReadDimension(AndroidXmlElement element, string name, float fallback, AndroidDimensionUnit expected)
    {
        if (!TryValue(element, name, out AndroidResourceValue value)) return fallback;
        if (value.Kind != AndroidResourceValueKind.Dimension) throw new InvalidDataException($"UI_INVALID_ATTRIBUTE: {name} is {value.Kind}");
        AndroidDimension dimension = value.AsDimension();
        if (dimension.Unit != expected) throw new NotSupportedException($"UI_UNSUPPORTED_ATTRIBUTE: {name} requires {expected}, got {dimension.Unit}");
        return dimension.Value;
    }

    private AndroidColor ReadColor(AndroidXmlElement element, string name, AndroidColor fallback)
    {
        if (!TryValue(element, name, out AndroidResourceValue value)) return fallback;
        return value.Kind == AndroidResourceValueKind.Color ? Color(value.AsColor()) : throw new InvalidDataException($"UI_INVALID_ATTRIBUTE: {name} is {value.Kind}");
    }

    private bool TryValue(AndroidXmlElement element, string name, out AndroidResourceValue value)
    {
        AndroidXmlAttribute? attribute = Attribute(element, name);
        if (attribute is null) { value = default; return false; }
        if (attribute.Value.Kind != AndroidResourceValueKind.Reference) { value = attribute.Value; return true; }
        uint reference = attribute.Value.AsReference();
        // Framework attribute references (0x01xxxxxx, android.R.attr.*) are not in
        // the app's resource table; treat them as absent so inflation continues
        // with defaults rather than failing the whole layout.
        if ((reference >> 24) == 0x01) { value = default; return false; }
        try { value = _resources.Resolve(reference); return true; }
        catch (KeyNotFoundException) { value = default; return false; }
    }

    private static AndroidXmlAttribute? Attribute(AndroidXmlElement element, string name) =>
        element.Attributes.FirstOrDefault(item => item.Name == name && item.NamespaceUri == AndroidNamespace);

    private static AndroidColor Color(uint argb) => new((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}
