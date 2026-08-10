using AndroidRuntime.Core.Apk;
using AndroidRuntime.Core.Ui;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Full drawable-bag extraction for resolve_style: the effective default color
/// (attr named "color") PLUS one attr per recognized selector state item
/// (state_pressed / state_hovered) so ViewRuntime can drive real selectors.
/// Trees are built synthetically through the internal element ctor — the walk
/// logic is pure, no APK fixture needed.
/// </summary>
public sealed class AndroidDrawableBagTests
{
    private const string AndroidNs = "http://schemas.android.com/apk/res/android";
    // Real framework attribute ids used by the fixture (only names are asserted).
    private const uint ColorId = 0x0101001a;
    private const uint StatePressedId = 0x010100e3;
    private const uint StateHoveredId = 0x0101036e;
    private const uint StateEnabledId = 0x0101009e;

    [Fact]
    public void Selector_with_pressed_and_default_returns_color_plus_state_pressed()
    {
        var root = new AndroidXmlElement(null, "selector", 1, []);
        root.MutableChildren.Add(Item(2, [State("state_pressed", true), Color("color", 0xFFFF0000)]));
        root.MutableChildren.Add(Item(3, [Color("color", 0xFF00FF00)]));

        AndroidInflateAttribute[]? bag = AndroidResourceQueryService.FindDrawableBag(root);

        Assert.NotNull(bag);
        Assert.Equal(2, bag!.Length);
        Assert.Equal("color", bag[0].Name);
        Assert.Equal(0xFF00FF00u, bag[0].Value.Data); // stateless item is the default
        Assert.Equal("state_pressed", bag[1].Name);
        Assert.Equal(0xFFFF0000u, bag[1].Value.Data);
        Assert.Equal(StatePressedId, bag[1].ResourceId);
    }

    [Fact]
    public void Selector_with_pressed_hovered_and_default_returns_three_attrs()
    {
        var root = new AndroidXmlElement(null, "selector", 1, []);
        root.MutableChildren.Add(Item(2, [State("state_pressed", true), Color("color", 0xFFFF0000)]));
        root.MutableChildren.Add(Item(3, [State("state_hovered", true), Color("color", 0xFF0000FF)]));
        root.MutableChildren.Add(Item(4, [Color("color", 0xFF00FF00)]));

        AndroidInflateAttribute[]? bag = AndroidResourceQueryService.FindDrawableBag(root);

        Assert.Equal(3, bag!.Length);
        Assert.Equal("color", bag[0].Name);
        Assert.Equal("state_pressed", bag[1].Name);
        Assert.Equal(StatePressedId, bag[1].ResourceId);
        Assert.Equal("state_hovered", bag[2].Name);
        Assert.Equal(StateHoveredId, bag[2].ResourceId);
    }

    [Fact]
    public void Pressed_shape_solid_inside_item_is_captured_as_state_entry()
    {
        var root = new AndroidXmlElement(null, "selector", 1, []);
        var pressedItem = new AndroidXmlElement(AndroidNs, "item", 2, [State("state_pressed", true)]);
        var shape = new AndroidXmlElement(AndroidNs, "shape", 3, []);
        shape.MutableChildren.Add(new AndroidXmlElement(AndroidNs, "solid", 4, [Color("color", 0xFFFF0000)]));
        pressedItem.MutableChildren.Add(shape);
        root.MutableChildren.Add(pressedItem);
        root.MutableChildren.Add(Item(5, [Color("color", 0xFF00FF00)]));

        AndroidInflateAttribute[]? bag = AndroidResourceQueryService.FindDrawableBag(root);

        Assert.Equal(2, bag!.Length);
        Assert.Equal("color", bag[0].Name);
        Assert.Equal("state_pressed", bag[1].Name);
        Assert.Equal(0xFFFF0000u, bag[1].Value.Data);
        // The state entry carries the ITEM's state attr id (not the color id) —
        // a <solid> inside a state <item> inherits the item's state attribute.
        Assert.Equal(StatePressedId, bag[1].ResourceId);
    }

    [Fact]
    public void Non_pressed_hovered_states_stay_out_of_the_bag()
    {
        var root = new AndroidXmlElement(null, "selector", 1, []);
        root.MutableChildren.Add(Item(2, [State("state_enabled", false), Color("color", 0xFF111111)]));
        root.MutableChildren.Add(Item(3, [State("state_pressed", false), Color("color", 0xFF222222)]));
        root.MutableChildren.Add(Item(4, [Color("color", 0xFF00FF00)]));

        AndroidInflateAttribute[]? bag = AndroidResourceQueryService.FindDrawableBag(root);

        // Only the stateless default survives; false/other state items are not
        // modeled (but still served as the fallback default when no stateless
        // item exists — here the stateless item wins the priority).
        Assert.NotNull(bag);
        Assert.Single(bag);
        Assert.Equal("color", bag[0].Name);
        Assert.Equal(0xFF00FF00u, bag[0].Value.Data);
    }

    [Fact]
    public void Plain_shape_without_items_is_a_single_color_bag()
    {
        var root = new AndroidXmlElement(AndroidNs, "shape", 1, []);
        root.MutableChildren.Add(new AndroidXmlElement(AndroidNs, "solid", 2, [Color("color", 0xFF336699)]));

        AndroidInflateAttribute[]? bag = AndroidResourceQueryService.FindDrawableBag(root);

        Assert.NotNull(bag);
        Assert.Single(bag);
        Assert.Equal("color", bag[0].Name);
        Assert.Equal(0xFF336699u, bag[0].Value.Data);
    }

    [Fact]
    public void No_color_anywhere_returns_null()
    {
        var root = new AndroidXmlElement(null, "selector", 1, []);
        root.MutableChildren.Add(Item(2, [State("state_pressed", true)])); // state but no color

        Assert.Null(AndroidResourceQueryService.FindDrawableBag(root));
    }

    [Fact]
    public void Gradient_shape_without_solid_exposes_start_color()
    {
        var root = new AndroidXmlElement(AndroidNs, "shape", 1, []);
        var gradient = new AndroidXmlElement(AndroidNs, "gradient", 2,
        [
            new(AndroidNs, "startColor", 0x0101019D, null, AndroidResourceValue.Color(0xFF0B1020)),
            new(AndroidNs, "endColor", 0x0101019E, null, AndroidResourceValue.Color(0xFF111A33))
        ]);
        root.MutableChildren.Add(gradient);

        AndroidInflateAttribute[]? bag = AndroidResourceQueryService.FindDrawableBag(root);

        Assert.NotNull(bag);
        Assert.Equal(2, bag!.Length);
        Assert.Equal("startColor", bag[0].Name);
        Assert.Equal(0xFF0B1020u, bag[0].Value.Data);
        Assert.Equal(0x0101019Du, bag[0].ResourceId);
        // endColor is now a sibling attr (the fixture has it; it was silently
        // dropped before the bag extension).
        Assert.Equal("endColor", bag[1].Name);
        Assert.Equal(0xFF111A33u, bag[1].Value.Data);
    }

    [Fact]
    public void Solid_and_gradient_emit_color_then_start_color()
    {
        var root = new AndroidXmlElement(AndroidNs, "shape", 1, []);
        root.MutableChildren.Add(new AndroidXmlElement(AndroidNs, "solid", 2, [Color("color", 0xFF336699)]));
        root.MutableChildren.Add(new AndroidXmlElement(AndroidNs, "gradient", 3,
        [
            new(AndroidNs, "startColor", 0x0101019D, null, AndroidResourceValue.Color(0xFF0B1020))
        ]));

        AndroidInflateAttribute[]? bag = AndroidResourceQueryService.FindDrawableBag(root);

        Assert.NotNull(bag);
        Assert.Equal(2, bag!.Length);
        Assert.Equal("color", bag[0].Name);
        Assert.Equal("startColor", bag[1].Name);
        Assert.Equal(0xFF0B1020u, bag[1].Value.Data);
    }

    [Fact]
    public void Gradient_inside_state_item_follows_state_symmetry()
    {
        var root = new AndroidXmlElement(null, "selector", 1, []);
        var pressedItem = new AndroidXmlElement(AndroidNs, "item", 2, [State("state_pressed", true)]);
        var shape = new AndroidXmlElement(AndroidNs, "shape", 3, []);
        shape.MutableChildren.Add(new AndroidXmlElement(AndroidNs, "gradient", 4,
        [
            new(AndroidNs, "startColor", 0x0101019D, null, AndroidResourceValue.Color(0xFFFF0000))
        ]));
        pressedItem.MutableChildren.Add(shape);
        root.MutableChildren.Add(pressedItem);
        root.MutableChildren.Add(Item(5, [Color("color", 0xFF00FF00)]));

        AndroidInflateAttribute[]? bag = AndroidResourceQueryService.FindDrawableBag(root);

        Assert.NotNull(bag);
        // Mirrors the solid symmetry exactly: the state gradient also lands in
        // the generic startColor fallback (gradientAny), like solidAny does for
        // solids — the state entry is what ViewRuntime actually applies when
        // pressed, and the stateless item wins the "color" default.
        Assert.Equal(3, bag!.Length);
        Assert.Equal("color", bag[0].Name); // stateless item default
        Assert.Equal("startColor", bag[1].Name); // any-fallback (same gradient)
        Assert.Equal("state_pressed", bag[2].Name); // gradient inside the pressed item
        Assert.Equal(0xFFFF0000u, bag[2].Value.Data);
        Assert.Equal(StatePressedId, bag[2].ResourceId);
    }

    [Fact]
    public void Shape_with_solid_and_corners_exposes_color_then_radius()
    {
        var root = new AndroidXmlElement(AndroidNs, "shape", 1, []);
        root.MutableChildren.Add(new AndroidXmlElement(AndroidNs, "solid", 2, [Color("color", 0xFF336699)]));
        root.MutableChildren.Add(new AndroidXmlElement(AndroidNs, "corners", 3,
        [
            new(AndroidNs, "radius", 0x010101A8, null, AndroidResourceValue.Dimension(0x00001201)) // 18dp encoded
        ]));

        AndroidInflateAttribute[]? bag = AndroidResourceQueryService.FindDrawableBag(root);

        Assert.NotNull(bag);
        Assert.Equal(2, bag!.Length);
        Assert.Equal("color", bag[0].Name);
        Assert.Equal("radius", bag[1].Name);
        Assert.Equal(AndroidRawValueKind.Dimension, bag[1].Value.Kind);
        Assert.True(Math.Abs(bag[1].Value.FloatValue - 18f) < 0.001f, $"decoded={bag[1].Value.FloatValue}");
        Assert.Equal(1, bag[1].Value.Unit); // android_dimen_unit_t: dp
        Assert.Equal(0x010101A8u, bag[1].ResourceId);
    }

    [Fact]
    public void Corners_without_any_fill_keeps_the_bag_null()
    {
        var root = new AndroidXmlElement(AndroidNs, "shape", 1, []);
        root.MutableChildren.Add(new AndroidXmlElement(AndroidNs, "corners", 2,
        [
            new(AndroidNs, "radius", 0x010101A8, null, AndroidResourceValue.Dimension(0x00001201))
        ]));

        // A shape with corners but no fill paints nothing — no color bag.
        Assert.Null(AndroidResourceQueryService.FindDrawableBag(root));
    }

    [Fact]
    public void Corners_inside_state_item_follows_state_symmetry()
    {
        var root = new AndroidXmlElement(null, "selector", 1, []);
        var pressedItem = new AndroidXmlElement(AndroidNs, "item", 2, [State("state_pressed", true)]);
        var shape = new AndroidXmlElement(AndroidNs, "shape", 3, []);
        shape.MutableChildren.Add(new AndroidXmlElement(AndroidNs, "corners", 4,
        [
            new(AndroidNs, "radius", 0x010101A8, null, AndroidResourceValue.Dimension(0x00002001))
        ]));
        pressedItem.MutableChildren.Add(shape);
        root.MutableChildren.Add(pressedItem);
        root.MutableChildren.Add(Item(5, [Color("color", 0xFF00FF00)]));

        AndroidInflateAttribute[]? bag = AndroidResourceQueryService.FindDrawableBag(root);

        Assert.NotNull(bag);
        Assert.Equal(3, bag!.Length); // color + radius-fallback + state_pressed
        Assert.Equal("state_pressed", bag[2].Name);
        Assert.Equal(AndroidRawValueKind.Dimension, bag[2].Value.Kind);
        Assert.Equal(StatePressedId, bag[2].ResourceId);
    }

    [Fact]
    public void Gradient_with_end_color_angle_and_type_exposes_all_attrs()
    {
        var root = new AndroidXmlElement(AndroidNs, "shape", 1, []);
        root.MutableChildren.Add(new AndroidXmlElement(AndroidNs, "gradient", 2,
        [
            new(AndroidNs, "startColor", 0x0101019D, null, AndroidResourceValue.Color(0xFF0B1020)),
            new(AndroidNs, "endColor", 0x0101019E, null, AndroidResourceValue.Color(0xFF111A33)),
            new(AndroidNs, "angle", 0x01010041, null, AndroidResourceValue.FromBinary(0x10, 0, [], "TEST")),
            new(AndroidNs, "type", 0x0101003A, null, AndroidResourceValue.FromBinary(0x10, 2, [], "TEST")) // sweep
        ]));

        AndroidInflateAttribute[]? bag = AndroidResourceQueryService.FindDrawableBag(root);

        Assert.NotNull(bag);
        Assert.Equal(4, bag!.Length);
        Assert.Equal("startColor", bag[0].Name);
        Assert.Equal("endColor", bag[1].Name);
        Assert.Equal(0xFF111A33u, bag[1].Value.Data);
        Assert.Equal("angle", bag[2].Name);
        Assert.Equal(AndroidRawValueKind.IntDecimal, bag[2].Value.Kind);
        Assert.Equal(0u, bag[2].Value.Data);
        Assert.Equal("type", bag[3].Name);
        Assert.Equal(2u, bag[3].Value.Data);
    }

    [Fact]
    public void Shape_with_stroke_exposes_stroke_width_and_color()
    {
        var root = new AndroidXmlElement(AndroidNs, "shape", 1, []);
        root.MutableChildren.Add(new AndroidXmlElement(AndroidNs, "solid", 2, [Color("color", 0xFF336699)]));
        root.MutableChildren.Add(new AndroidXmlElement(AndroidNs, "stroke", 3,
        [
            new(AndroidNs, "width", 0x0101013D, null, AndroidResourceValue.Dimension(0x00000101)), // 1dp
            new(AndroidNs, "color", 0x0101019C, null, AndroidResourceValue.Color(0xFF2A2F3F))
        ]));

        AndroidInflateAttribute[]? bag = AndroidResourceQueryService.FindDrawableBag(root);

        Assert.NotNull(bag);
        Assert.Equal(3, bag!.Length);
        Assert.Equal("color", bag[0].Name);
        Assert.Equal("strokeWidth", bag[1].Name);
        Assert.Equal(AndroidRawValueKind.Dimension, bag[1].Value.Kind);
        Assert.True(Math.Abs(bag[1].Value.FloatValue - 1f) < 0.001f);
        Assert.Equal(1, bag[1].Value.Unit); // dp
        Assert.Equal("strokeColor", bag[2].Name);
        Assert.Equal(0xFF2A2F3Fu, bag[2].Value.Data);
    }

    [Fact]
    public void Shape_shape_attribute_is_exposed()
    {
        var root = new AndroidXmlElement(AndroidNs, "shape", 1,
        [
            new(AndroidNs, "shape", 0x0101007c, null, AndroidResourceValue.FromBinary(0x10, 1, [], "TEST")) // oval
        ]);
        root.MutableChildren.Add(new AndroidXmlElement(AndroidNs, "solid", 2, [Color("color", 0xFF336699)]));

        AndroidInflateAttribute[]? bag = AndroidResourceQueryService.FindDrawableBag(root);

        Assert.NotNull(bag);
        Assert.Equal(2, bag!.Length);
        Assert.Equal("color", bag[0].Name);
        Assert.Equal("shape", bag[1].Name);
        Assert.Equal(1u, bag[1].Value.Data);
        Assert.Equal(0x0101007cu, bag[1].ResourceId);
    }

    [Fact]
    public void Stroke_without_any_fill_is_still_paintable()
    {
        var root = new AndroidXmlElement(AndroidNs, "shape", 1, []);
        root.MutableChildren.Add(new AndroidXmlElement(AndroidNs, "stroke", 2,
        [
            new(AndroidNs, "width", 0x0101013D, null, AndroidResourceValue.Dimension(0x00000101)),
            new(AndroidNs, "color", 0x0101019C, null, AndroidResourceValue.Color(0xFF2A2F3F))
        ]));

        // A stroke paints a border even with no fill — the bag must survive.
        AndroidInflateAttribute[]? bag = AndroidResourceQueryService.FindDrawableBag(root);

        Assert.NotNull(bag);
        Assert.Equal(2, bag!.Length);
        Assert.Equal("strokeWidth", bag[0].Name);
        Assert.Equal("strokeColor", bag[1].Name);
    }

    private static AndroidXmlElement Item(int line, AndroidXmlAttribute[] attributes) =>
        new(AndroidNs, "item", line, attributes);

    private static AndroidXmlAttribute State(string name, bool value) =>
        new(AndroidNs, name, name switch
        {
            "state_pressed" => StatePressedId,
            "state_hovered" => StateHoveredId,
            _ => StateEnabledId
        }, value ? "true" : "false", AndroidResourceValue.Boolean(value));

    private static AndroidXmlAttribute Color(string name, uint argb) =>
        new(AndroidNs, name, ColorId, null, AndroidResourceValue.Color(argb));
}
