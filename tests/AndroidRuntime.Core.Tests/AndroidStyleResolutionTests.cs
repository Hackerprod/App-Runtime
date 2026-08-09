using AndroidRuntime.Core.Apk;
using AndroidRuntime.Core.Ui;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Style-chain resolution and visual-fidelity inflation: the AppCompat
/// Widget.AppCompat.Button.Colored family (teal accent background + white text)
/// and Widget.AppCompat.Button.Borderless.Colored family (accent text, no
/// background), plus gravity centering.
/// </summary>
public sealed class AndroidStyleResolutionTests
{
    [Fact]
    public void Style_chain_resolves_child_before_parent_attribute()
    {
        var styles = new Dictionary<uint, AndroidResourceStyle>
        {
            [0x7f100002] = AndroidResourceStyle.ForTest(0x7f100002, 0x01030258, [new(0x01010098, AndroidResourceValue.Color(0xff112233))], "Base.Widget.AppCompat.Button"),
            [0x7f100001] = AndroidResourceStyle.ForTest(0x7f100001, 0x7f100002, [new(0x010100d4, AndroidResourceValue.Color(0xff445566))], "Widget.AppCompat.Button.Colored"),
        };
        var resolver = AndroidResourceResolver.ForTestWithStyles(new Dictionary<uint, AndroidResourceEntry>(), styles, AndroidResourceLimits.Default);

        Assert.True(resolver.TryResolveStyleAttribute(0x7f100001, 0x010100d4, out AndroidResourceValue background));
        Assert.Equal(0xff445566u, background.AsColor());
        // textColor is not defined by the child style; the parent supplies it.
        Assert.True(resolver.TryResolveStyleAttribute(0x7f100001, 0x01010098, out AndroidResourceValue textColor));
        Assert.Equal(0xff112233u, textColor.AsColor());
        // A framework parent style is not in the app table; a missing style id
        // still fails cleanly.
        Assert.False(resolver.TryResolveStyleAttribute(0x7f100099, 0x01010098, out _));
        Assert.Null(resolver.TryGetStyle(0x01030258));
    }

    [Fact]
    public void Style_color_resolution_follows_references_and_rejects_theme_attributes()
    {
        var entries = new Dictionary<uint, AndroidResourceEntry>
        {
            [0x7f050001] = AndroidResourceEntry.ForTest("app", "color", "teal", AndroidResourceValue.Color(0xff008577)),
            [0x7f050002] = AndroidResourceEntry.ForTest("app", "color", "accent_ref", AndroidResourceValue.Reference(0x7f050001)),
        };
        var styles = new Dictionary<uint, AndroidResourceStyle>
        {
            [0x7f100001] = AndroidResourceStyle.ForTest(0x7f100001, 0, [new(0x01010098, AndroidResourceValue.Reference(0x7f050002))], "Widget.AppCompat.Button.Colored"),
            [0x7f100002] = AndroidResourceStyle.ForTest(0x7f100002, 0, [new(0x01010098, AndroidResourceValue.FromBinary(0x02, 0x01010435, [], "TEST"))], "Theme.AppCompat"),
        };
        var resolver = AndroidResourceResolver.ForTestWithStyles(entries, styles, AndroidResourceLimits.Default);

        Assert.True(resolver.TryResolveStyleAttribute(0x7f100001, 0x01010098, out AndroidResourceValue accentRef));
        Assert.Equal(0xff008577u, resolver.TryResolveStyleColor(accentRef));

        // Theme-attribute references (TYPE_ATTRIBUTE, e.g. ?attr/colorAccent)
        // need a theme context: bounded resolution returns null rather than
        // guessing, so the inflater can apply its documented fallback.
        Assert.True(resolver.TryResolveStyleAttribute(0x7f100002, 0x01010098, out AndroidResourceValue themeAttr));
        Assert.Equal(AndroidResourceValueKind.Attribute, themeAttr.Kind);
        Assert.Null(resolver.TryResolveStyleColor(themeAttr));
    }

    private static AndroidXmlAttribute Attr(string? ns, string name, AndroidResourceValue value) => new(ns, name, 0, null, value);
    private static AndroidXmlAttribute AndroidAttr(string name, AndroidResourceValue value) => Attr("http://schemas.android.com/apk/res/android", name, value);

    private static AndroidXmlDocument ButtonDocument(uint styleId, string text)
    {
        var root = new AndroidXmlElement(null, "LinearLayout", 1, [AndroidAttr("layout_width", AndroidResourceValue.FromBinary(0x10, unchecked((uint)-1), [], "TEST")), AndroidAttr("layout_height", AndroidResourceValue.FromBinary(0x10, unchecked((uint)-1), [], "TEST"))]);
        var button = new AndroidXmlElement(null, "Button", 2,
        [
            AndroidAttr("layout_width", AndroidResourceValue.FromBinary(0x05, 0x0000c801, [], "TEST")),
            AndroidAttr("layout_height", AndroidResourceValue.FromBinary(0x05, 0x00003c01, [], "TEST")),
            AndroidAttr("text", AndroidResourceValue.String(text)),
            // Real Android's `style` attribute is not android-namespaced.
            Attr(null, "style", AndroidResourceValue.Reference(styleId)),
        ]);
        root.MutableChildren.Add(button);
        return new AndroidXmlDocument(root, []);
    }

    [Fact]
    public void Inflater_applies_colored_button_teal_background_and_white_text()
    {
        var styles = new Dictionary<uint, AndroidResourceStyle>
        {
            // Base.Widget.AppCompat.Button.Colored: background → accent drawable ref
            // (unresolvable by the bounded reader), textAppearance → TextAppearance style.
            [0x7f1000d2] = AndroidResourceStyle.ForTest(0x7f1000d2, 0x7f1000ce, [new(0x010100d4, AndroidResourceValue.Reference(0x7f07002f)), new(0x01010034, AndroidResourceValue.Reference(0x7f1001bd))], "Base.Widget.AppCompat.Button.Colored"),
            [0x7f1000ce] = AndroidResourceStyle.ForTest(0x7f1000ce, 0x01030258, [], "Base.Widget.AppCompat.Button"),
            [0x7f1002f4] = AndroidResourceStyle.ForTest(0x7f1002f4, 0x7f1000d2, [], "Widget.AppCompat.Button.Colored"),
            // TextAppearance.AppCompat.Widget.Button.Colored → textColor → white selector ref.
            [0x7f1001bd] = AndroidResourceStyle.ForTest(0x7f1001bd, 0x7f10003c, [], "TextAppearance.AppCompat.Widget.Button.Colored"),
            [0x7f10003c] = AndroidResourceStyle.ForTest(0x7f10003c, 0x01030258, [new(0x01010098, AndroidResourceValue.Reference(0x7f050003))], "Base.TextAppearance.AppCompat.Widget.Button.Colored"),
        };
        var resolver = AndroidResourceResolver.ForTestWithStyles(new Dictionary<uint, AndroidResourceEntry>(), styles, AndroidResourceLimits.Default);
        var inflater = new AndroidLayoutInflater(resolver, new AndroidUiLimits());

        AndroidViewNode root = inflater.Inflate(ButtonDocument(0x7f1002f4, "Connect"));
        var button = Assert.IsType<AndroidButtonNode>(Assert.Single(root.Children));

        // The background reference (drawable selector) cannot resolve to a color;
        // the framework chain falls back to the verified Material accent teal.
        Assert.Equal(new AndroidColor(255, 0x00, 0x85, 0x77), button.BackgroundColor);
        // White text over the accent (abc_btn_colored_text_material).
        Assert.Equal(new AndroidColor(255, 255, 255, 255), button.TextColor);
    }

    [Fact]
    public void Inflater_applies_borderless_colored_link_text_without_background()
    {
        var styles = new Dictionary<uint, AndroidResourceStyle>
        {
            [0x7f1000d0] = AndroidResourceStyle.ForTest(0x7f1000d0, 0x0103025a, [new(0x01010098, AndroidResourceValue.Reference(0x7f050002))], "Base.Widget.AppCompat.Button.Borderless.Colored"),
            [0x7f1002f2] = AndroidResourceStyle.ForTest(0x7f1002f2, 0x7f1000d0, [], "Widget.AppCompat.Button.Borderless.Colored"),
        };
        var resolver = AndroidResourceResolver.ForTestWithStyles(new Dictionary<uint, AndroidResourceEntry>(), styles, AndroidResourceLimits.Default);
        var inflater = new AndroidLayoutInflater(resolver, new AndroidUiLimits());

        AndroidViewNode root = inflater.Inflate(ButtonDocument(0x7f1002f2, "Conceder Permisos"));
        var button = Assert.IsType<AndroidButtonNode>(Assert.Single(root.Children));

        Assert.Null(button.BackgroundColor); // transparent, not the Button default gray
        Assert.Equal(new AndroidColor(255, 0x00, 0x85, 0x77), button.TextColor); // accent teal
    }

    [Fact]
    public void Inflater_uses_element_background_and_textColor_over_style()
    {
        var styles = new Dictionary<uint, AndroidResourceStyle>
        {
            [0x7f100001] = AndroidResourceStyle.ForTest(0x7f100001, 0x01030258, [new(0x010100d4, AndroidResourceValue.Color(0xff112233))], "Widget.AppCompat.Button.Colored"),
        };
        var resolver = AndroidResourceResolver.ForTestWithStyles(new Dictionary<uint, AndroidResourceEntry>(), styles, AndroidResourceLimits.Default);
        var inflater = new AndroidLayoutInflater(resolver, new AndroidUiLimits());
        var rootEl = new AndroidXmlElement(null, "LinearLayout", 1, [AndroidAttr("layout_width", AndroidResourceValue.FromBinary(0x10, unchecked((uint)-1), [], "TEST")), AndroidAttr("layout_height", AndroidResourceValue.FromBinary(0x10, unchecked((uint)-1), [], "TEST"))]);
        var button = new AndroidXmlElement(null, "Button", 2,
        [
            AndroidAttr("layout_width", AndroidResourceValue.FromBinary(0x05, 0x0000c801, [], "TEST")),
            AndroidAttr("layout_height", AndroidResourceValue.FromBinary(0x05, 0x00003c01, [], "TEST")),
            AndroidAttr("background", AndroidResourceValue.Color(0xffaabbcc)),
            AndroidAttr("textColor", AndroidResourceValue.Color(0xff010203)),
            AndroidAttr("text", AndroidResourceValue.String("Tap")),
            Attr(null, "style", AndroidResourceValue.Reference(0x7f100001)),
        ]);
        rootEl.MutableChildren.Add(button);

        AndroidViewNode root = inflater.Inflate(new AndroidXmlDocument(rootEl, []));
        var node = Assert.IsType<AndroidButtonNode>(Assert.Single(root.Children));

        Assert.Equal(new AndroidColor(255, 0xaa, 0xbb, 0xcc), node.BackgroundColor);
        Assert.Equal(new AndroidColor(255, 0x01, 0x02, 0x03), node.TextColor);
    }

    [Fact]
    public void Inflater_ignores_missing_and_framework_styles()
    {
        var resolver = AndroidResourceResolver.ForTestWithStyles(new Dictionary<uint, AndroidResourceEntry>(), new Dictionary<uint, AndroidResourceStyle>(), AndroidResourceLimits.Default);
        var inflater = new AndroidLayoutInflater(resolver, new AndroidUiLimits());

        AndroidViewNode root = inflater.Inflate(ButtonDocument(0x7f10ffff, "NoStyle"));
        var button = Assert.IsType<AndroidButtonNode>(Assert.Single(root.Children));
        // No style resolution: Button keeps its default gray background and
        // default dark text; nothing throws.
        Assert.Equal(new AndroidColor(255, 224, 224, 224), button.BackgroundColor);
        Assert.Equal(new AndroidColor(255, 32, 32, 32), button.TextColor);
    }

    [Fact]
    public void Inflater_preserves_button_center_gravity_when_attribute_is_absent()
    {
        // Regression: ApplyCommon must NOT clobber the Button's CENTER default
        // (0x11) with ReadInteger's 0 fallback when the XML lacks android:gravity.
        var resolver = AndroidResourceResolver.ForTestWithStyles(new Dictionary<uint, AndroidResourceEntry>(), new Dictionary<uint, AndroidResourceStyle>(), AndroidResourceLimits.Default);
        var inflater = new AndroidLayoutInflater(resolver, new AndroidUiLimits());

        AndroidViewNode root = inflater.Inflate(ButtonDocument(0x7f10ffff, "Connect"));
        var button = Assert.IsType<AndroidButtonNode>(Assert.Single(root.Children));

        Assert.Equal(0x11, button.Gravity); // CENTER preserved, not zeroed
        var renderer = new RecordingAndroidRenderBackend();
        using var host = new AndroidSceneHost(button, new DeterministicAndroidTextMeasurer(), renderer, new AndroidUiLimits());
        host.SetViewport(360, 732, 1f); host.Render();

        // 200x60 button at x=80: centered text must start at (80 + (200 - textWidth)/2),
        // i.e. the draw rect is INSET from the button's left edge, not flush at 80.
        AndroidDrawTextCommand text = Assert.Single(host.Render().DisplayList.Commands.OfType<AndroidDrawTextCommand>());
        Assert.True(text.Rect.X > 80f, $"expected text inset from button left edge (80), got x={text.Rect.X}");
        Assert.True(text.Rect.X < 200f, $"expected text in button left half, got x={text.Rect.X}");
    }

    [Fact]
    public void Inflater_explicit_gravity_attribute_overrides_button_default()
    {
        var resolver = AndroidResourceResolver.ForTestWithStyles(new Dictionary<uint, AndroidResourceEntry>(), new Dictionary<uint, AndroidResourceStyle>(), AndroidResourceLimits.Default);
        var inflater = new AndroidLayoutInflater(resolver, new AndroidUiLimits());
        var rootEl = new AndroidXmlElement(null, "LinearLayout", 1, [AndroidAttr("layout_width", AndroidResourceValue.FromBinary(0x10, unchecked((uint)-1), [], "TEST")), AndroidAttr("layout_height", AndroidResourceValue.FromBinary(0x10, unchecked((uint)-1), [], "TEST"))]);
        var button = new AndroidXmlElement(null, "Button", 2,
        [
            AndroidAttr("layout_width", AndroidResourceValue.FromBinary(0x05, 0x0000c801, [], "TEST")),
            AndroidAttr("layout_height", AndroidResourceValue.FromBinary(0x05, 0x00003c01, [], "TEST")),
            AndroidAttr("gravity", AndroidResourceValue.FromBinary(0x10, 0x30, [], "TEST")), // TOP (0x30)
            AndroidAttr("text", AndroidResourceValue.String("Top")),
            Attr(null, "style", AndroidResourceValue.Reference(0x7f10ffff)),
        ]);
        rootEl.MutableChildren.Add(button);

        AndroidViewNode root = inflater.Inflate(new AndroidXmlDocument(rootEl, []));
        var node = Assert.IsType<AndroidButtonNode>(Assert.Single(root.Children));
        Assert.Equal(0x30, node.Gravity); // explicit attribute wins over the default
    }
}
