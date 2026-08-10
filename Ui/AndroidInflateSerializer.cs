#nullable enable
using AndroidRuntime.Core.Apk;

namespace AndroidRuntime.Core.Ui;

/// <summary>
/// The serialized view tree App Runtime hands to ViewRuntime for inflation —
/// the agreed Phase-2 bridge shape. This is NOT raw AXML bytes: the binary-AXML
/// parsing stays here (already real, tested), and this side serializes the
/// parsed element tree so ViewRuntime can build its own real view objects.
///
/// Every attribute value is RAW and UNRESOLVED (a mirror of android.util.
/// TypedValue): references stay references, dimensions keep their unit
/// (dp/sp/px/etc) — NEVER pre-converted to pixels. ViewRuntime applies density
/// exactly once on its side, which is the fix for the confirmed 2x-size bug.
/// </summary>
public sealed record AndroidInflateTree(
    IReadOnlyList<AndroidInflateNode> Nodes,
    int ApplicationThemeStyleId)
{
}

public sealed record AndroidInflateNode(
    int Index,
    int ParentIndex,
    string ClassName,
    string? NamespaceUri,
    int ResourceId,
    string? XmlOnClick,
    IReadOnlyList<AndroidInflateAttribute> Attributes)
{
}

public sealed record AndroidInflateAttribute(
    string? NamespaceUri,
    string? Name,
    uint ResourceId,
    AndroidRawValue Value)
{
}

/// <summary>RAW typed value mirroring android.util.TypedValue (RES_VALUE kinds).
/// Dimensions carry their DECODED float value + raw unit (dp/sp/px/etc) — never
/// a converted pixel number. Float carries the decoded value; int kinds carry
/// the raw integer. Reference/Attribute carry the resource id in Data.</summary>
public readonly record struct AndroidRawValue(
    AndroidRawValueKind Kind,
    uint Data,
    string? String,
    float FloatValue = 0f,
    int Unit = 0)
{
    public static AndroidRawValue Null { get; } = new(AndroidRawValueKind.Null, 0, null);
}

public enum AndroidRawValueKind
{
    /// <summary>Enum VALUES match ViewRuntime's android_raw_value_kind_t
    /// (android.h) exactly — this is a cross-ABI contract, order matters.</summary>
    String = 0,
    Reference = 1,
    Attribute = 2,
    Dimension = 3,
    Float = 4,
    IntBoolean = 5,
    IntDecimal = 6,
    IntHex = 7,
    IntColor = 8,
    Null = 9
}

/// <summary>
/// Serializes a parsed AXML element tree (AndroidXmlDocument) into the agreed
/// flat node-array bridge shape. The tree walk is format-agnostic data
/// conversion: element names, namespaced attributes, and their RAW values are
/// copied across with no visual interpretation (no style application, no
/// density conversion, no defaults).
/// </summary>
public static class AndroidInflateSerializer
{
    /// <summary>Serializes the layout tree; applicationThemeStyleId is the
    /// manifest's &lt;application android:theme&gt; style id (theme == style chain,
    /// resolved by ViewRuntime through resolve_style).</summary>
    public static AndroidInflateTree Serialize(AndroidXmlDocument document, int applicationThemeStyleId)
    {
        ArgumentNullException.ThrowIfNull(document);
        var nodes = new List<AndroidInflateNode>();
        Walk(document.Root, -1, nodes);
        return new AndroidInflateTree(nodes.AsReadOnly(), applicationThemeStyleId);
    }

    private static void Walk(AndroidXmlElement element, int parentIndex, List<AndroidInflateNode> nodes)
    {
        int index = nodes.Count;
        var attributes = element.Attributes
            .Select(attribute => new AndroidInflateAttribute(
                attribute.NamespaceUri,
                attribute.Name,
                attribute.ResourceId,
                FromValue(attribute.Value)))
            .ToArray();
        // The android:id reference becomes the node's resource id (native
        // android_ui_find_view_by_id / hit-test lookups key on it). Real
        // Android: id is a reference whose raw value IS the id resource id.
        int resourceId = 0;
        foreach (AndroidInflateAttribute attribute in attributes)
        {
            if (attribute.Name != "id") continue;
            if (attribute.Value.Kind == AndroidRawValueKind.Reference)
                resourceId = unchecked((int)attribute.Value.Data);
            break;
        }
        // android:onClick="methodName" names an Activity method directly (the
        // older declarative click pattern — no setOnClickListener call). This
        // side captures it at inflate time so click dispatch can look it up
        // later without asking ViewRuntime anything.
        string? xmlOnClick = TryGetXmlOnClick(attributes);
        nodes.Add(new AndroidInflateNode(
            index,
            parentIndex,
            element.Name,
            element.NamespaceUri,
            resourceId,
            xmlOnClick,
            attributes));
        foreach (AndroidXmlElement child in element.Children)
            Walk(child, index, nodes);
    }

    /// <summary>Extracts the android:onClick attribute's method name from a
    /// node's raw attribute list, or null when absent/not a string. Pure and
    /// testable without binary AXML.</summary>
    public static string? TryGetXmlOnClick(IReadOnlyList<AndroidInflateAttribute> attributes)
    {
        foreach (AndroidInflateAttribute attribute in attributes)
        {
            if (attribute.Name != "onClick") continue;
            if (!string.Equals(attribute.NamespaceUri, AndroidNamespaceUri, StringComparison.Ordinal)) continue;
            if (attribute.Value.Kind == AndroidRawValueKind.String && attribute.Value.String is not null)
                return attribute.Value.String;
            break;
        }
        return null;
    }

    /// <summary>http://schemas.android.com/apk/res/android — the standard Android
    /// framework namespace for android: attributes.</summary>
    public const string AndroidNamespaceUri = "http://schemas.android.com/apk/res/android";

    /// <summary>Maps an AndroidResourceValue (the parsed AXML typed value) to the
    /// raw android_raw_value_t mirror (kind values match android.h exactly).
    /// Dimensions are DECODED into float_value + unit (the native ABI reads
    /// v.float_value / v.unit directly — passing the raw encoded bits would be
    /// wrong). References keep their id in Data. Fractions have no native mirror
    /// and map to the closest typed kind (int decimal) rather than inventing a
    /// new ABI kind.</summary>
    public static AndroidRawValue FromValue(AndroidResourceValue value) => value.Kind switch
    {
        AndroidResourceValueKind.Reference => new(AndroidRawValueKind.Reference, value.Data, null),
        AndroidResourceValueKind.Attribute => new(AndroidRawValueKind.Attribute, value.Data, null),
        AndroidResourceValueKind.String => new(AndroidRawValueKind.String, 0, value.AsString()),
        AndroidResourceValueKind.Float => new(AndroidRawValueKind.Float, value.Data, null, FloatValue: BitConverter.Int32BitsToSingle(unchecked((int)value.Data))),
        AndroidResourceValueKind.Dimension => FromDimension(value.AsDimension()),
        AndroidResourceValueKind.Fraction => new(AndroidRawValueKind.IntDecimal, value.Data, null),
        AndroidResourceValueKind.Integer => new(AndroidRawValueKind.IntDecimal, value.Data, null),
        AndroidResourceValueKind.Boolean => new(AndroidRawValueKind.IntBoolean, value.Data, null),
        AndroidResourceValueKind.Color => new(AndroidRawValueKind.IntColor, value.Data, null),
        _ => new(AndroidRawValueKind.Null, 0, null)
    };

    /// <summary>Decoded dimension: float_value = the raw number, unit = the raw
    /// android_dimen_unit_t (px=0, dip=1, sp=2, pt=3, in=4, mm=5). ViewRuntime
    /// applies density exactly once from these — this side never converts.</summary>
    private static AndroidRawValue FromDimension(AndroidDimension dimension) =>
        new(AndroidRawValueKind.Dimension, 0, null, FloatValue: dimension.Value, Unit: (int)dimension.Unit);
}
