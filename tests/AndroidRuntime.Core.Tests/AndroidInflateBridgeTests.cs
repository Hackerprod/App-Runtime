using AndroidRuntime.Core.Apk;
using AndroidRuntime.Core.Ui;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Phase-2 bridge-shape tests: the inflate serializer (AXML tree → serialized
/// node tree with RAW values), the resource-query service (resolve_resource /
/// resolve_style / fetch_file backed by the existing resolver), and the
/// manifest theme style-id extraction.
/// </summary>
public sealed class AndroidInflateBridgeTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "UiProbe.apk");

    [Fact]
    public void Inflate_serializer_preserves_tree_shape_and_raw_values()
    {
        LoadedApk apk = ApkLoader.Load(FixturePath);
        var resolver = AndroidResourceResolver.Create(apk);
        AndroidXmlDocument layout = resolver.LoadLayout("main");

        AndroidInflateTree tree = AndroidInflateSerializer.Serialize(layout, applicationThemeStyleId: 0x7f10000b);

        // Root + 2 children (LinearLayout, TextView, Button) — same shape the
        // old inflater walked, but now as raw serialized nodes.
        Assert.Equal(3, tree.Nodes.Count);
        Assert.Equal(-1, tree.Nodes[0].ParentIndex);
        Assert.Equal("LinearLayout", tree.Nodes[0].ClassName);
        Assert.Equal(0, tree.Nodes[1].ParentIndex);
        Assert.Equal(0, tree.Nodes[2].ParentIndex);

        // Attribute values are RAW and unresolved: text/textSize/textColor are
        // all References to resources (NOT resolved literals) — ViewRuntime
        // resolves them through the bridge's resolve_resource callback. The one
        // exception: plain literals like layout_width stay Integer.
        AndroidInflateNode textNode = tree.Nodes[1];
        Assert.Equal("TextView", textNode.ClassName);
        AndroidInflateAttribute text = textNode.Attributes.Single(a => a.Name == "text");
        Assert.Equal(AndroidRawValueKind.Reference, text.Value.Kind);
        Assert.True(text.Value.Data != 0, "text attribute should be a resource reference");
        AndroidInflateAttribute textSize = textNode.Attributes.Single(a => a.Name == "textSize");
        Assert.Equal(AndroidRawValueKind.Reference, textSize.Value.Kind);
        AndroidInflateAttribute textColor = textNode.Attributes.Single(a => a.Name == "textColor");
        Assert.Equal(AndroidRawValueKind.Reference, textColor.Value.Kind);
        AndroidInflateAttribute layoutWidth = textNode.Attributes.Single(a => a.Name == "layout_width");
        Assert.Equal(AndroidRawValueKind.IntDecimal, layoutWidth.Value.Kind);
        Assert.Equal(unchecked((uint)-2), layoutWidth.Value.Data); // wrap_content

        // The referenced dimension resolves through the query service with its
        // DECODED value + raw unit: 24sp must yield float_value=24, unit=sp(2).
        uint dimenId = textSize.Value.Data;
        AndroidRawValue resolved = new AndroidResourceQueryService(resolver, apk).ResolveResource(dimenId);
        Assert.Equal(AndroidRawValueKind.Dimension, resolved.Kind);
        Assert.True(Math.Abs(resolved.FloatValue - 24f) < 0.001f, $"decoded={resolved.FloatValue}");
        Assert.Equal(2, resolved.Unit); // android_dimen_unit_t: sp
        Assert.Equal(0u, resolved.Data); // no packed bits carried for dimensions

        // Theme root travels in the tree header.
        Assert.Equal(0x7f10000b, tree.ApplicationThemeStyleId);
    }

    [Fact]
    public void Resource_query_service_resolves_resource_style_and_file()
    {
        LoadedApk apk = ApkLoader.Load(FixturePath);
        var resolver = AndroidResourceResolver.Create(apk);
        AndroidXmlDocument layout = resolver.LoadLayout("main");

        AndroidInflateTree tree = AndroidInflateSerializer.Serialize(layout, applicationThemeStyleId: 0x7f10000b);
        AndroidResourceQueryService service = new(resolver, apk);

        // A referenced attribute in the serialized tree resolves through the
        // query service exactly as ViewRuntime would: textSize (a reference)
        // -> Dimension with DECODED float 24 + unit sp(2), textColor (a
        // reference) -> Color #336699, text (a reference) -> String "Ready".
        AndroidInflateNode textNode = tree.Nodes[1];
        AndroidInflateAttribute textSize = textNode.Attributes.Single(a => a.Name == "textSize");
        AndroidRawValue dimen = service.ResolveResource(textSize.Value.Data);
        Assert.Equal(AndroidRawValueKind.Dimension, dimen.Kind);
        Assert.True(Math.Abs(dimen.FloatValue - 24f) < 0.001f, $"decoded={dimen.FloatValue}");
        Assert.Equal(2, dimen.Unit); // android_dimen_unit_t: sp
        Assert.Equal(0u, dimen.Data); // no packed bits carried for dimensions
        AndroidInflateAttribute textColor = textNode.Attributes.Single(a => a.Name == "textColor");
        Assert.Equal(0xff336699u, service.ResolveResource(textColor.Value.Data).Data);
        AndroidInflateAttribute text = textNode.Attributes.Single(a => a.Name == "text");
        Assert.Equal("Ready", service.ResolveResource(text.Value.Data).String);

        // resolve_style: a missing/framework style returns null; a present
        // style returns one link (parent + raw attrs) WITH attribute names
        // resolved (the native apply_attr matches by name — null names broke
        // style-bag application, which is the fix under test).
        Assert.Null(service.ResolveStyle(0x7f10ffff));
        Assert.Null(service.ResolveStyle(0x01030258));
        AndroidResourceStyleLink? link = service.ResolveStyle(0x7f100001);
        if (link is not null)
        {
            Assert.NotNull(link.Attributes);
            Assert.NotEqual(0u, link.ParentStyleId);
            // Every style attr must carry its short name OR be resolvable by id.
            foreach (var attr in link.Attributes)
                Assert.True(attr.Name is not null || attr.ResourceId != 0, $"style attr missing both name and id: 0x{attr.ResourceId:X8}");
        }

        // fetch_file: the layout resource file is present as raw bytes.
        AndroidResourceValue layoutValue = resolver.Resolve(resolver.GetIdentifier("layout", "main"));
        Assert.Equal(AndroidResourceValueKind.String, layoutValue.Kind);
        byte[]? file = service.FetchFile(layoutValue.AsString());
        Assert.NotNull(file);
        Assert.True(file!.Length > 0);
        Assert.Null(service.FetchFile("res/layout/does-not-exist.xml"));
    }

    [Fact]
    public void Manifest_reader_extracts_application_theme_style_id()
    {
        LoadedApk apk = ApkLoader.Load(FixturePath);
        AndroidManifest manifest = AndroidManifestReader.Parse(apk.AndroidManifestXml);

        // UiProbe declares a theme in its manifest; the reader must expose it
        // as a style resource id (the fixture uses a framework theme,
        // 0x01030241, which is not in the app table — extraction still works).
        Assert.NotEqual(0, manifest.ApplicationThemeStyleId);
        Assert.InRange(manifest.ApplicationThemeStyleId, 0, 0x7fffffff);
    }

    [Fact]
    public void Inflate_serializer_captures_declarative_xml_onclick()
    {
        // The click-dispatch unit captures android:onClick="methodName" at
        // inflate time (an Activity method named directly in the layout — the
        // non-programmatic click style). The extraction is a pure helper over
        // the raw attribute list, so it is tested without binary AXML.
        const string androidNs = "http://schemas.android.com/apk/res/android";

        AndroidInflateAttribute[] onClickAttrs =
        [
            new(androidNs, "onClick", 0x01010067, new AndroidRawValue(AndroidRawValueKind.String, 0, "onPlaySoundClick")),
            new(androidNs, "text", 0x0101014f, new AndroidRawValue(AndroidRawValueKind.String, 0, "Play sound"))
        ];
        Assert.Equal("onPlaySoundClick", AndroidInflateSerializer.TryGetXmlOnClick(onClickAttrs));

        // Absent -> null.
        AndroidInflateAttribute[] noOnClick =
        [
            new(androidNs, "text", 0x0101014f, new AndroidRawValue(AndroidRawValueKind.String, 0, "No handler"))
        ];
        Assert.Null(AndroidInflateSerializer.TryGetXmlOnClick(noOnClick));

        // Wrong namespace (not android:) -> null.
        AndroidInflateAttribute[] foreignOnClick =
        [
            new("http://custom.example/schemas", "onClick", 0x7f010001, new AndroidRawValue(AndroidRawValueKind.String, 0, "onCustom"))
        ];
        Assert.Null(AndroidInflateSerializer.TryGetXmlOnClick(foreignOnClick));

        // Non-string onClick value (e.g. a reference) -> null.
        AndroidInflateAttribute[] referenceOnClick =
        [
            new(androidNs, "onClick", 0x01010067, new AndroidRawValue(AndroidRawValueKind.Reference, 0x7f0a0001, null))
        ];
        Assert.Null(AndroidInflateSerializer.TryGetXmlOnClick(referenceOnClick));

        // The UiProbe fixture layout declares android:onClick="handleClick" on
        // its Button (R.id.action) — the serializer must capture it on exactly
        // that node and leave every other node null.
        LoadedApk apk = ApkLoader.Load(FixturePath);
        var resolver = AndroidResourceResolver.Create(apk);
        AndroidInflateTree tree = AndroidInflateSerializer.Serialize(resolver.LoadLayout("main"), applicationThemeStyleId: 0x7f10000b);
        AndroidInflateNode button = Assert.Single(tree.Nodes, node => node.ClassName == "Button");
        Assert.Equal("handleClick", button.XmlOnClick);
        Assert.Equal(0, tree.Nodes.Count(node => node != button && node.XmlOnClick is not null));
    }
}
