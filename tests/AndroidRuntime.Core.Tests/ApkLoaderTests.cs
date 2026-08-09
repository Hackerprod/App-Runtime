using System.IO.Compression;
using AndroidRuntime.Core.Apk;

namespace AndroidRuntime.Core.Tests;

public sealed class ApkLoaderTests
{
    [Fact]
    public void Load_exposes_bounded_resource_table_and_res_entries_as_immutable_snapshots()
    {
        using var apk = CreateApk(("classes.dex", [1]), ("AndroidManifest.xml", [2]), ("resources.arsc", [3, 4]), ("res/layout/main.xml", [5, 6, 7]));
        LoadedApk loaded = ApkLoader.Load(apk, new ApkLoadLimits(maxResourceTableBytes: 16, maxResourceFileBytes: 16, maxResourceTotalBytes: 32, maxResourceEntries: 4));
        Assert.Equal([3, 4], loaded.ResourcesArsc); Assert.Equal([5, 6, 7], loaded.ResourceFiles["res/layout/main.xml"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, byte[]>)loaded.ResourceFiles).Clear());
    }

    [Fact]
    public void Load_rejects_resource_entry_count_size_and_compression_ratio_quotas()
    {
        using var tooMany = CreateApk(("classes.dex", [1]), ("AndroidManifest.xml", [2]), ("res/a", [1]), ("res/b", [2]));
        Assert.Throws<InvalidDataException>(() => ApkLoader.Load(tooMany, new ApkLoadLimits(maxResourceEntries: 1)));
        using var compressed = CreateApk(("classes.dex", [1]), ("AndroidManifest.xml", [2]), ("res/raw/bomb.bin", new byte[4096]));
        Assert.Throws<InvalidDataException>(() => ApkLoader.Load(compressed, new ApkLoadLimits(maxResourceFileBytes: 8192, maxResourceTotalBytes: 8192, maxResourceCompressionRatio: 2)));
    }
    [Fact]
    public void Load_reads_the_only_classes_dex()
    {
        byte[] expected = [0x64, 0x65, 0x78, 0x0A];
        byte[] manifest = [0x03, 0x00, 0x08, 0x00, 0x08, 0x00, 0x00, 0x00];
        using var apk = CreateApk(("classes.dex", expected), ("AndroidManifest.xml", manifest));

        var loaded = ApkLoader.Load(apk);

        Assert.Equal(expected, Assert.Single(loaded.ClassesDexFiles));
        Assert.Equal(manifest, loaded.AndroidManifestXml);
    }

    [Fact]
    public void Load_rejects_an_apk_without_classes_dex()
    {
        using var apk = CreateApk(("AndroidManifest.xml", [0x01]));

        var error = Assert.Throws<InvalidDataException>(() => ApkLoader.Load(apk));

        Assert.Contains("classes.dex", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_collects_multidex_entries_in_numeric_order_not_zip_order()
    {
        using var apk = CreateApk(
            ("classes.dex", [0x01]),
            ("classes3.dex", [0x03]),
            ("classes2.dex", [0x02]),
            ("AndroidManifest.xml", [0x03]));

        var loaded = ApkLoader.Load(apk);

        Assert.Equal([[0x01], [0x02], [0x03]], loaded.ClassesDexFiles);
    }

    [Fact]
    public void Load_sorts_classes10_after_classes9_numerically_not_alphabetically()
    {
        using var apk = CreateApk(
            ("classes.dex", [0x01]),
            ("classes9.dex", [0x09]),
            ("classes10.dex", [0x0A]),
            ("classes2.dex", [0x02]),
            ("classes8.dex", [0x08]),
            ("classes3.dex", [0x03]),
            ("classes7.dex", [0x07]),
            ("classes4.dex", [0x04]),
            ("classes6.dex", [0x06]),
            ("classes5.dex", [0x05]),
            ("AndroidManifest.xml", [0x03]));

        var loaded = ApkLoader.Load(apk);

        Assert.Equal(
            [[0x01], [0x02], [0x03], [0x04], [0x05], [0x06], [0x07], [0x08], [0x09], [0x0A]],
            loaded.ClassesDexFiles);
    }

    [Fact]
    public void Load_rejects_multidex_sets_over_the_secondary_count_quota()
    {
        using var apk = CreateApk(
            ("classes.dex", [0x01]),
            ("classes2.dex", [0x02]),
            ("classes3.dex", [0x03]),
            ("AndroidManifest.xml", [0x03]));

        var error = Assert.Throws<InvalidDataException>(() => ApkLoader.Load(apk, new ApkLoadLimits(maxSecondaryDexFiles: 1)));

        Assert.Contains("secondary DEX", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_applies_the_dex_byte_quota_to_each_secondary_entry()
    {
        using var apk = CreateApk(
            ("classes.dex", [0x01]),
            ("classes2.dex", new byte[65]),
            ("AndroidManifest.xml", [0x03, 0x00, 0x08, 0x00, 0x08, 0x00, 0x00, 0x00]));

        var error = Assert.Throws<InvalidDataException>(() => ApkLoader.Load(apk, new ApkLoadLimits(maxClassesDexBytes: 64)));

        Assert.Contains("classes2.dex", error.Message, StringComparison.Ordinal);
        Assert.Contains("64", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_rejects_non_contiguous_secondary_dex_numbering()
    {
        using var apk = CreateApk(
            ("classes.dex", [0x01]),
            ("classes2.dex", [0x02]),
            ("classes4.dex", [0x04]),
            ("AndroidManifest.xml", [0x03]));

        var error = Assert.Throws<InvalidDataException>(() => ApkLoader.Load(apk));

        Assert.Contains("classes3.dex", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_rejects_duplicate_dex_entry_names()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var entry = archive.CreateEntry("classes.dex").Open()) entry.Write([0x01]);
            using (var entry = archive.CreateEntry("classes2.dex").Open()) entry.Write([0x02]);
            using (var entry = archive.CreateEntry("classes2.dex").Open()) entry.Write([0x03]);
            using (var entry = archive.CreateEntry("AndroidManifest.xml").Open()) entry.Write([0x04]);
        }
        stream.Position = 0;

        var error = Assert.Throws<InvalidDataException>(() => ApkLoader.Load(stream));

        Assert.Contains("classes2.dex", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_rejects_an_apk_without_android_manifest()
    {
        using var apk = CreateApk(("classes.dex", [0x01]));

        var error = Assert.Throws<InvalidDataException>(() => ApkLoader.Load(apk));

        Assert.Contains("AndroidManifest.xml", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_rejects_a_compressed_dex_entry_over_the_configured_uncompressed_quota()
    {
        using var apk = CreateApk(
            ("classes.dex", new byte[65]),
            ("AndroidManifest.xml", [0x03, 0x00, 0x08, 0x00, 0x08, 0x00, 0x00, 0x00]));
        using (var archive = new ZipArchive(apk, ZipArchiveMode.Read, leaveOpen: true))
        {
            var dexEntry = archive.GetEntry("classes.dex")!;
            Assert.True(dexEntry.CompressedLength < dexEntry.Length);
        }
        apk.Position = 0;
        var limits = new ApkLoadLimits(maxClassesDexBytes: 64, maxAndroidManifestBytes: 64);

        var error = Assert.Throws<InvalidDataException>(() => ApkLoader.Load(apk, limits));

        Assert.Contains("classes.dex", error.Message, StringComparison.Ordinal);
        Assert.Contains("64", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_rejects_a_manifest_entry_over_the_configured_uncompressed_quota()
    {
        using var apk = CreateApk(("classes.dex", [0x01]), ("AndroidManifest.xml", new byte[33]));
        var limits = new ApkLoadLimits(maxClassesDexBytes: 64, maxAndroidManifestBytes: 32);

        var error = Assert.Throws<InvalidDataException>(() => ApkLoader.Load(apk, limits));

        Assert.Contains("AndroidManifest.xml", error.Message, StringComparison.Ordinal);
        Assert.Contains("32", error.Message, StringComparison.Ordinal);
    }

    private static MemoryStream CreateApk(params (string Name, byte[] Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
                using var output = entry.Open();
                output.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
