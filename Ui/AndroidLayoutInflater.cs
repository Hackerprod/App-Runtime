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
        int id = ReadId(element);
        AndroidViewNode node = element.Name switch
        {
            "LinearLayout" or "android.widget.LinearLayout" => new AndroidLinearLayoutNode(id),
            "TextView" or "android.widget.TextView" => new AndroidTextViewNode(id),
            "Button" or "android.widget.Button" => new AndroidButtonNode(id),
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
            if (background.Kind != AndroidResourceValueKind.Color) throw new NotSupportedException("UI_UNSUPPORTED_ATTRIBUTE: background supports colors only");
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
        value = attribute.Value.Kind == AndroidResourceValueKind.Reference ? _resources.Resolve(attribute.Value.AsReference()) : attribute.Value;
        return true;
    }

    private static AndroidXmlAttribute? Attribute(AndroidXmlElement element, string name) =>
        element.Attributes.FirstOrDefault(item => item.Name == name && item.NamespaceUri == AndroidNamespace);

    private static AndroidColor Color(uint argb) => new((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}
