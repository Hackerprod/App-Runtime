using AndroidRuntime.Core.Apk;

namespace AndroidRuntime.Core.Tests;

public sealed class AndroidResourceTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "UiProbe.apk");

    [Fact]
    public void Signed_fixture_resolves_compiled_layout_and_typed_values_without_fixture_ids()
    {
        LoadedApk apk = ApkLoader.Load(FixturePath);
        var resources = AndroidResourceResolver.Create(apk);

        uint layoutId = resources.GetIdentifier("layout", "main");
        AndroidXmlDocument layout = resources.LoadLayout("main");

        Assert.NotEqual(0u, layoutId);
        Assert.Equal("LinearLayout", layout.Root.Name);
        Assert.Equal(["TextView", "Button"], layout.Root.Children.Select(child => child.Name));

        AndroidXmlElement label = layout.Root.Children[0];
        AndroidXmlAttribute id = Assert.Single(label.Attributes, attribute => attribute.Name == "id");
        Assert.Equal("label", resources.GetResourceName(id.Value.AsReference()).Name);
        Assert.Equal("Ready", resources.ResolveAttribute(label, "text").AsString());
        Assert.Equal(0xff336699u, resources.ResolveAttribute(label, "textColor").AsColor());
        AndroidResourceValue textSize = resources.ResolveAttribute(label, "textSize");
        Assert.True(Math.Abs(textSize.AsDimension().Value - 24f) < 0.001f, $"encoded=0x{textSize.Data:x8}, decoded={textSize.AsDimension().Value}");
        Assert.Equal(AndroidDimensionUnit.Sp, resources.ResolveAttribute(label, "textSize").AsDimension().Unit);
        Assert.Equal("Tap", resources.ResolveAttribute(layout.Root.Children[1], "text").AsString());
    }

    [Fact]
    public void Binary_xml_reader_rejects_truncation_with_stable_diagnostic()
    {
        LoadedApk apk = ApkLoader.Load(FixturePath);
        byte[] layout = apk.ResourceFiles.Single(pair => pair.Key.StartsWith("res/layout/main", StringComparison.Ordinal)).Value;

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => AndroidBinaryXmlReader.Parse(layout[..^1]));

        Assert.StartsWith("AXML_INVALID:", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resource_table_rejects_truncation_and_skips_unsupported_complex_entries()
    {
        LoadedApk apk = ApkLoader.Load(FixturePath);
        byte[] table = apk.ResourcesArsc!;
        InvalidDataException truncated = Assert.Throws<InvalidDataException>(() => AndroidResourceTable.Parse(table[..^1]));
        Assert.StartsWith("ARSC_INVALID:", truncated.Message, StringComparison.Ordinal);

        byte[] complex = table.ToArray();
        int entryOffset = FindFirstSimpleEntry(complex);
        complex[entryOffset + 2] |= 0x01; // FLAG_COMPLEX

        // The table must still parse: one complex entry no longer aborts the whole
        // resource table. Only that one specific id disappears.
        AndroidResourceTable original = AndroidResourceTable.Parse(table);
        AndroidResourceTable parsed = AndroidResourceTable.Parse(complex);
        uint[] missing = original.Entries.Keys.Except(parsed.Entries.Keys).ToArray();
        Assert.Single(missing);

        // Lookup of the skipped complex id fails cleanly only when asked.
        var resolver = AndroidResourceResolver.ForTest(parsed.Entries, AndroidResourceLimits.Default);
        Assert.StartsWith("ARSC_NOT_FOUND:", Assert.Throws<KeyNotFoundException>(() => resolver.GetResourceName(missing[0])).Message, StringComparison.Ordinal);

        // Regression: a different, non-complex resource in the same table still
        // resolves correctly even though another entry in the table is complex.
        AndroidResourceEntry survivor = parsed.Entries.Values.First(entry => entry.Value.Kind == AndroidResourceValueKind.String);
        Assert.Equal(survivor.Value.AsString(), resolver.Resolve(survivor.Id).AsString());
        Assert.Same(survivor, parsed.Entries[survivor.Id]);
    }

    [Fact]
    public void Resolver_rejects_reference_cycles_and_depth_quota()
    {
        var entries = new Dictionary<uint, AndroidResourceEntry>
        {
            [0x7f010000] = AndroidResourceEntry.ForTest("app", "string", "a", AndroidResourceValue.Reference(0x7f010001)),
            [0x7f010001] = AndroidResourceEntry.ForTest("app", "string", "b", AndroidResourceValue.Reference(0x7f010000)),
        };
        var resolver = AndroidResourceResolver.ForTest(entries, new AndroidResourceLimits(maxReferenceDepth: 8));

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => resolver.Resolve(0x7f010000));

        Assert.Equal("ARSC_REFERENCE_CYCLE: 0x7f010000 -> 0x7f010001 -> 0x7f010000", error.Message);
    }

    [Fact]
    public void Resolver_enforces_reference_depth_count_and_string_byte_quotas()
    {
        var entries = new Dictionary<uint, AndroidResourceEntry>
        {
            [0x7f010000] = AndroidResourceEntry.ForTest("app", "string", "a", AndroidResourceValue.Reference(0x7f010001)),
            [0x7f010001] = AndroidResourceEntry.ForTest("app", "string", "b", AndroidResourceValue.Reference(0x7f010002)),
            [0x7f010002] = AndroidResourceEntry.ForTest("app", "string", "c", AndroidResourceValue.String("bounded")),
        };

        var depth = AndroidResourceResolver.ForTest(entries, new AndroidResourceLimits(maxReferenceDepth: 2));
        Assert.StartsWith("ARSC_REFERENCE_DEPTH:", Assert.Throws<InvalidDataException>(() => depth.Resolve(0x7f010000)).Message, StringComparison.Ordinal);
        var count = AndroidResourceResolver.ForTest(entries, new AndroidResourceLimits(maxReferenceDepth: 8, maxResolvedValues: 2));
        Assert.StartsWith("ARSC_REFERENCE_COUNT:", Assert.Throws<InvalidDataException>(() => count.Resolve(0x7f010000)).Message, StringComparison.Ordinal);
        var bytes = AndroidResourceResolver.ForTest(entries, new AndroidResourceLimits(maxResolvedStringBytes: 3));
        Assert.StartsWith("ARSC_REFERENCE_BYTES:", Assert.Throws<InvalidDataException>(() => bytes.Resolve(0x7f010002)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Configuration_choice_prefers_exact_density_then_default_deterministically()
    {
        AndroidResourceEntry defaultEntry = AndroidResourceEntry.ForTest("app", "dimen", "size", AndroidResourceValue.Dimension(0), density: 0, sourceOrder: 20);
        AndroidResourceEntry xhdpi = AndroidResourceEntry.ForTest("app", "dimen", "size", AndroidResourceValue.Dimension(0), density: 320, sourceOrder: 10);
        AndroidResourceEntry xxhdpi = AndroidResourceEntry.ForTest("app", "dimen", "size", AndroidResourceValue.Dimension(0), density: 480, sourceOrder: 5);

        Assert.Same(xhdpi, AndroidResourceTable.SelectConfiguration([defaultEntry, xxhdpi, xhdpi], 320));
        Assert.Same(defaultEntry, AndroidResourceTable.SelectConfiguration([xxhdpi, defaultEntry], 240));
    }

    [Fact]
    public void Fraction_values_decode_both_units_with_aosp_complex_formula()
    {
        // 24.0% of base: radix 0 (k=0), mantissa 24, unit Fraction(0).
        AndroidFraction baseFraction = AndroidResourceValue.Fraction(0x00001800).AsFraction();
        Assert.Equal(AndroidFractionUnit.Fraction, baseFraction.Unit);
        Assert.True(Math.Abs(baseFraction.Value - 24f) < 0.001f, $"decoded={baseFraction.Value}");

        // 0.5% of parent: radix 1 (k=7), mantissa round(0.5*2^7)=64, unit FractionParent(1).
        AndroidFraction parentFraction = AndroidResourceValue.Fraction(0x00004011).AsFraction();
        Assert.Equal(AndroidFractionUnit.FractionParent, parentFraction.Unit);
        Assert.True(Math.Abs(parentFraction.Value - 0.5f) < 0.001f, $"decoded={parentFraction.Value}");

        // Negative fraction: signed mantissa -64 at radix 1.
        AndroidFraction negative = AndroidResourceValue.Fraction(0xFFFFC010).AsFraction();
        Assert.Equal(AndroidFractionUnit.Fraction, negative.Unit);
        Assert.True(Math.Abs(negative.Value - (-0.5f)) < 0.001f, $"decoded={negative.Value}");

        // 2.0% of base: radix 2 (k=15), mantissa round(2.0*2^15)=65536.
        AndroidFraction wide = AndroidResourceValue.Fraction(0x01000020).AsFraction();
        Assert.Equal(AndroidFractionUnit.Fraction, wide.Unit);
        Assert.True(Math.Abs(wide.Value - 2.0f) < 0.001f, $"decoded={wide.Value}");
    }

    [Fact]
    public void FromBinary_wires_type_fraction_to_the_fraction_kind()
    {
        AndroidResourceValue value = AndroidResourceValue.FromBinary(0x06, 0x00004011, [], "TEST");

        Assert.Equal(AndroidResourceValueKind.Fraction, value.Kind);
        Assert.True(Math.Abs(value.AsFraction().Value - 0.5f) < 0.001f);
    }

    [Fact]
    public void Fraction_unit_byte_over_one_throws_unsupported()
    {
        var error = Assert.Throws<NotSupportedException>(() => AndroidResourceValue.Fraction(0x00001802));

        Assert.StartsWith("ARSC_UNSUPPORTED: fraction unit 2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dimension_radix_one_and_two_decode_with_aosp_multipliers()
    {
        // 0.5dp at radix 1 (k=7): mantissa round(0.5*2^7)=64 -> 64/2^7 = 0.5.
        AndroidDimension half = AndroidResourceValue.Dimension(0x00004011).AsDimension();
        Assert.Equal(AndroidDimensionUnit.Dp, half.Unit);
        Assert.True(Math.Abs(half.Value - 0.5f) < 0.001f, $"decoded={half.Value}");

        // 1.0dp at radix 2 (k=15): mantissa round(1.0*2^15)=32768 -> 32768/2^15 = 1.0.
        AndroidDimension one = AndroidResourceValue.Dimension(0x00800021).AsDimension();
        Assert.Equal(AndroidDimensionUnit.Dp, one.Unit);
        Assert.True(Math.Abs(one.Value - 1.0f) < 0.001f, $"decoded={one.Value}");
    }

    private static int FindFirstSimpleEntry(byte[] table)
    {
        for (int i = 0; i <= table.Length - 16; i += 4)
        {
            if (BitConverter.ToUInt16(table, i) == 8 && BitConverter.ToUInt16(table, i + 2) == 0 &&
                BitConverter.ToUInt16(table, i + 8) == 8 && table[i + 10] == 0)
                return i;
        }
        throw new InvalidDataException("Fixture contains no simple resource entry.");
    }
}
