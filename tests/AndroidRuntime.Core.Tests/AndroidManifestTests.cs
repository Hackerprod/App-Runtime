using AndroidRuntime.Core.Apk;
using System.Buffers.Binary;

namespace AndroidRuntime.Core.Tests;

public sealed class AndroidManifestTests
{
    [Fact]
    public void Real_binary_manifest_resolves_launcher_activity_without_hardcoding()
    {
        var apk = ApkLoader.Load(FixturePath());

        var manifest = AndroidManifestReader.Parse(apk.AndroidManifestXml);

        Assert.Equal("org.example.runtimeprobe", manifest.PackageName);
        Assert.Equal("org.example.runtimeprobe.MainActivity", manifest.LauncherActivityName);
        Assert.Equal("Lorg/example/runtimeprobe/MainActivity;", manifest.LauncherActivityDescriptor);
        Assert.Contains(manifest.Activities, activity => activity.Name == "org.example.runtimeprobe.MainActivity");
        Assert.Equal(["android.permission.ACCESS_NETWORK_STATE"], manifest.UsesPermissions);
        Assert.True(manifest.TargetSdkVersion > 0);
    }

    [Fact]
    public void Missing_permission_fixture_has_no_declared_permissions()
    {
        var manifest = AndroidManifestReader.Parse(ApkLoader.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ServicesProbeMissingPermission.apk")).AndroidManifestXml);
        Assert.Empty(manifest.UsesPermissions);
    }

    [Fact]
    public void Target_sdk_defaults_to_min_sdk_when_target_is_absent()
    {
        byte[] bytes = ApkLoader.Load(FixturePath()).AndroidManifestXml.ToArray();
        Assert.True(ReplaceEncodedText(bytes, "targetSdkVersion", "unusedSdkVersion"));
        var usesSdk = ReadChunks(bytes).First(chunk => chunk.Type == 0x0102 && chunk.Name == "uses-sdk");
        ushort count = ReadU16(bytes, usesSdk.Offset + 28); int attributes = usesSdk.Offset + 16 + ReadU16(bytes, usesSdk.Offset + 24);
        bool changed = false;
        for (int index = 0; index < count; index++)
        {
            int value = attributes + index * 20 + 16;
            if (ReadU32(bytes, value) == 21) { BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(value), 35); changed = true; break; }
        }
        Assert.True(changed);
        Assert.Equal(35, AndroidManifestReader.Parse(bytes).TargetSdkVersion);
    }

    [Fact]
    public void Manifest_permissions_are_exposed_as_an_immutable_snapshot()
    {
        var permissions = AndroidManifestReader.Parse(ApkLoader.Load(FixturePath()).AndroidManifestXml).UsesPermissions;
        var collection = Assert.IsAssignableFrom<ICollection<string>>(permissions);
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(collection.Clear);
        Assert.Equal(["android.permission.ACCESS_NETWORK_STATE"], permissions);
    }

    [Fact]
    public void Uses_permissions_are_deduplicated_and_only_direct_manifest_children_count()
    {
        byte[] source = ApkLoader.Load(FixturePath()).AndroidManifestXml;
        byte[] duplicated = RewriteChunks(source, chunks =>
        {
            var pair = ElementPair(chunks, "uses-permission"); int end = chunks.IndexOf(pair.End); chunks.InsertRange(end + 1, [pair.Start, pair.End]); return chunks;
        });
        Assert.Single(AndroidManifestReader.Parse(duplicated).UsesPermissions);

        byte[] nested = RewriteChunks(source, chunks =>
        {
            var pair = ElementPair(chunks, "uses-permission"); chunks.Remove(pair.Start); chunks.Remove(pair.End); int application = chunks.FindIndex(chunk => chunk.Type == 0x0102 && chunk.Name == "application"); chunks.InsertRange(application + 1, [pair.Start, pair.End]); return chunks;
        });
        Assert.Empty(AndroidManifestReader.Parse(nested).UsesPermissions);
    }

    [Theory]
    [InlineData(".MainActivity", "Lorg/example/runtimeprobe/MainActivity;")]
    [InlineData("MainActivity", "Lorg/example/runtimeprobe/MainActivity;")]
    [InlineData("other.example.MainActivity", "Lother/example/MainActivity;")]
    public void Activity_names_are_resolved_like_android(string declaredName, string expectedDescriptor)
    {
        Assert.Equal(expectedDescriptor, AndroidManifestReader.ToDexDescriptor("org.example.runtimeprobe", declaredName));
    }

    [Fact]
    public void Truncated_binary_manifest_is_rejected()
    {
        var bytes = ApkLoader.Load(FixturePath()).AndroidManifestXml;

        for (int length = 0; length < bytes.Length; length++)
        {
            var error = Assert.Throws<InvalidDataException>(() => AndroidManifestReader.Parse(bytes.AsSpan(0, length)));
            Assert.Contains("chunk", error.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Malformed_binary_manifest_header_is_rejected()
    {
        var bytes = ApkLoader.Load(FixturePath()).AndroidManifestXml.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 4);

        var error = Assert.Throws<InvalidDataException>(() => AndroidManifestReader.Parse(bytes));

        Assert.Contains("chunk header", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manifest_without_main_launcher_pair_is_rejected()
    {
        var bytes = ApkLoader.Load(FixturePath()).AndroidManifestXml.ToArray();
        Assert.True(ReplaceEncodedText(bytes, "LAUNCHER", "NOTHINGX"), "Fixture should contain the launcher category.");

        var error = Assert.Throws<InvalidDataException>(() => AndroidManifestReader.Parse(bytes));

        Assert.Contains("launcher", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Utf8_string_pool_manifest_is_supported_alongside_the_real_utf16_fixture()
    {
        var utf16Manifest = ApkLoader.Load(FixturePath()).AndroidManifestXml;

        var manifest = AndroidManifestReader.Parse(RebuildStringPool(utf16Manifest, utf8: true, value => value));

        Assert.Equal("Lorg/example/runtimeprobe/MainActivity;", manifest.LauncherActivityDescriptor);
    }

    [Fact]
    public void Intent_filter_nested_indirectly_under_activity_is_not_a_launcher()
    {
        var bytes = RewriteChunks(ApkLoader.Load(FixturePath()).AndroidManifestXml, chunks =>
        {
            var wrapperStart = chunks.First(chunk => chunk.Type == 0x0102 && chunk.Name == "uses-sdk");
            var wrapperEnd = chunks.First(chunk => chunk.Type == 0x0103 && chunk.Name == "uses-sdk");
            int filterStart = chunks.FindIndex(chunk => chunk.Type == 0x0102 && chunk.Name == "intent-filter");
            int filterEnd = chunks.FindIndex(filterStart, chunk => chunk.Type == 0x0103 && chunk.Name == "intent-filter");
            chunks.Insert(filterEnd + 1, wrapperEnd);
            chunks.Insert(filterStart, wrapperStart);
            return chunks;
        });

        var error = Assert.Throws<InvalidDataException>(() => AndroidManifestReader.Parse(bytes));

        Assert.Contains("launcher", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Main_and_launcher_in_sibling_filters_do_not_form_a_launcher()
    {
        var bytes = RewriteChunks(ApkLoader.Load(FixturePath()).AndroidManifestXml, chunks =>
        {
            int filterStart = chunks.FindIndex(chunk => chunk.Type == 0x0102 && chunk.Name == "intent-filter");
            int filterEnd = chunks.FindIndex(filterStart, chunk => chunk.Type == 0x0103 && chunk.Name == "intent-filter");
            var filterStartChunk = chunks[filterStart];
            var filterEndChunk = chunks[filterEnd];
            var action = ElementPair(chunks, "action");
            var category = ElementPair(chunks, "category");
            chunks.RemoveRange(filterStart, filterEnd - filterStart + 1);
            chunks.InsertRange(filterStart,
            [
                filterStartChunk, action.Start, action.End, filterEndChunk,
                filterStartChunk, category.Start, category.End, filterEndChunk
            ]);
            return chunks;
        });

        var error = Assert.Throws<InvalidDataException>(() => AndroidManifestReader.Parse(bytes));

        Assert.Contains("same intent-filter", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resource_map_after_xml_nodes_is_rejected()
    {
        var bytes = RewriteChunks(ApkLoader.Load(FixturePath()).AndroidManifestXml, chunks =>
        {
            int resourceMap = chunks.FindIndex(chunk => chunk.Type == 0x0180);
            var moved = chunks[resourceMap];
            chunks.RemoveAt(resourceMap);
            int usesSdkEnd = chunks.FindIndex(chunk => chunk.Type == 0x0103 && chunk.Name == "uses-sdk");
            chunks.Insert(usesSdkEnd + 1, moved);
            return chunks;
        });

        var error = Assert.Throws<InvalidDataException>(() => AndroidManifestReader.Parse(bytes));

        Assert.Contains("resource map", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void String_pool_rejects_styles_start_when_style_count_is_zero()
    {
        var bytes = ApkLoader.Load(FixturePath()).AndroidManifestXml.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8 + 24), ReadU32(bytes, 8 + 4));

        var error = Assert.Throws<InvalidDataException>(() => AndroidManifestReader.Parse(bytes));

        Assert.Contains("styles", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Start_element_rejects_special_attribute_index_outside_attribute_count()
    {
        var bytes = ApkLoader.Load(FixturePath()).AndroidManifestXml.ToArray();
        int manifestOffset = ReadChunks(bytes).First(chunk => chunk.Type == 0x0102 && chunk.Name == "manifest").Offset;
        ushort attributeCount = ReadU16(bytes, manifestOffset + 28);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(manifestOffset + 30), checked((ushort)(attributeCount + 1)));

        var error = Assert.Throws<InvalidDataException>(() => AndroidManifestReader.Parse(bytes));

        Assert.Contains("attribute index", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Utf8_pool_accepts_multibyte_and_supplementary_text()
    {
        var bytes = ApkLoader.Load(FixturePath()).AndroidManifestXml;
        var converted = RebuildStringPool(bytes, utf8: true, value => value == "RuntimeProbe" ? "Rúntime🚀" : value);

        Assert.Equal("Lorg/example/runtimeprobe/MainActivity;", AndroidManifestReader.Parse(converted).LauncherActivityDescriptor);
    }

    [Fact]
    public void Utf16_pool_accepts_supplementary_text()
    {
        var bytes = ApkLoader.Load(FixturePath()).AndroidManifestXml;
        var converted = RebuildStringPool(bytes, utf8: false, value => value == "RuntimeProbe" ? "Rocket🚀" : value);

        Assert.Equal("Lorg/example/runtimeprobe/MainActivity;", AndroidManifestReader.Parse(converted).LauncherActivityDescriptor);
    }

    private static string FixturePath() => Path.Combine(AppContext.BaseDirectory, "Fixtures", "RuntimeProbe.apk");

    private static bool ReplaceEncodedText(byte[] bytes, string oldValue, string newValue)
    {
        foreach (var encoding in new[] { System.Text.Encoding.UTF8, System.Text.Encoding.Unicode })
        {
            byte[] oldBytes = encoding.GetBytes(oldValue);
            byte[] newBytes = encoding.GetBytes(newValue);
            for (int offset = 0; offset <= bytes.Length - oldBytes.Length; offset++)
            {
                if (!bytes.AsSpan(offset, oldBytes.Length).SequenceEqual(oldBytes))
                    continue;
                newBytes.CopyTo(bytes, offset);
                return true;
            }
        }

        return false;
    }

    private static byte[] RebuildStringPool(byte[] xml, bool utf8, Func<string, string> transform)
    {
        const int poolOffset = 8;
        Assert.Equal((ushort)0x0001, ReadU16(xml, poolOffset));
        int headerSize = ReadU16(xml, poolOffset + 2);
        int oldPoolSize = checked((int)ReadU32(xml, poolOffset + 4));
        int stringCount = checked((int)ReadU32(xml, poolOffset + 8));
        Assert.Equal(0u, ReadU32(xml, poolOffset + 12));
        uint flags = ReadU32(xml, poolOffset + 16);
        Assert.Equal(0u, flags & 0x100u);
        int stringsStart = checked((int)ReadU32(xml, poolOffset + 20));
        var strings = new string[stringCount];
        for (int index = 0; index < stringCount; index++)
        {
            int relativeOffset = checked((int)ReadU32(xml, poolOffset + headerSize + index * 4));
            int cursor = poolOffset + stringsStart + relativeOffset;
            int utf16Length = ReadUtf16Length(xml, ref cursor);
            strings[index] = transform(System.Text.Encoding.Unicode.GetString(xml, cursor, utf16Length * 2));
        }

        using var stringData = new MemoryStream();
        using var stringWriter = new BinaryWriter(stringData, utf8 ? System.Text.Encoding.UTF8 : System.Text.Encoding.Unicode, leaveOpen: true);
        var offsets = new int[stringCount];
        for (int index = 0; index < strings.Length; index++)
        {
            offsets[index] = checked((int)stringData.Position);
            if (utf8)
            {
                byte[] encoded = System.Text.Encoding.UTF8.GetBytes(strings[index]);
                WriteUtf8Length(stringWriter, strings[index].Length);
                WriteUtf8Length(stringWriter, encoded.Length);
                stringWriter.Write(encoded);
                stringWriter.Write((byte)0);
            }
            else
            {
                WriteUtf16Length(stringWriter, strings[index].Length);
                stringWriter.Write(System.Text.Encoding.Unicode.GetBytes(strings[index]));
                stringWriter.Write((ushort)0);
            }
        }
        while (stringData.Length % 4 != 0)
            stringWriter.Write((byte)0);

        int newStringsStart = 28 + stringCount * 4;
        int newPoolSize = checked(newStringsStart + (int)stringData.Length);
        using var pool = new MemoryStream();
        using (var writer = new BinaryWriter(pool, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0x0001);
            writer.Write((ushort)28);
            writer.Write((uint)newPoolSize);
            writer.Write((uint)stringCount);
            writer.Write(0u);
            writer.Write(utf8 ? flags | 0x100u : flags & ~0x100u);
            writer.Write((uint)newStringsStart);
            writer.Write(0u);
            foreach (int offset in offsets)
                writer.Write((uint)offset);
            writer.Write(stringData.ToArray());
        }

        var converted = new byte[checked(xml.Length - oldPoolSize + newPoolSize)];
        xml.AsSpan(0, poolOffset).CopyTo(converted);
        pool.ToArray().CopyTo(converted, poolOffset);
        xml.AsSpan(poolOffset + oldPoolSize).CopyTo(converted.AsSpan(poolOffset + newPoolSize));
        BinaryPrimitives.WriteUInt32LittleEndian(converted.AsSpan(4), (uint)converted.Length);
        return converted;
    }

    private static int ReadUtf16Length(byte[] bytes, ref int offset)
    {
        int first = ReadU16(bytes, offset);
        offset += 2;
        if ((first & 0x8000) == 0)
            return first;
        int second = ReadU16(bytes, offset);
        offset += 2;
        return ((first & 0x7fff) << 16) | second;
    }

    private static void WriteUtf8Length(BinaryWriter writer, int value)
    {
        Assert.InRange(value, 0, 0x7fff);
        if (value > 0x7f)
            writer.Write((byte)((value >> 8) | 0x80));
        writer.Write((byte)value);
    }

    private static void WriteUtf16Length(BinaryWriter writer, int value)
    {
        Assert.InRange(value, 0, 0x7fffffff);
        if (value > 0x7fff)
        {
            writer.Write((ushort)((value >> 16) | 0x8000));
            writer.Write((ushort)value);
            return;
        }
        writer.Write((ushort)value);
    }

    private static (BinaryXmlChunk Start, BinaryXmlChunk End) ElementPair(List<BinaryXmlChunk> chunks, string name)
    {
        var start = chunks.First(chunk => chunk.Type == 0x0102 && chunk.Name == name);
        var end = chunks.First(chunk => chunk.Type == 0x0103 && chunk.Name == name);
        return (start, end);
    }

    private static byte[] RewriteChunks(byte[] xml, Func<List<BinaryXmlChunk>, List<BinaryXmlChunk>> rewrite)
    {
        var chunks = rewrite(ReadChunks(xml));
        int totalSize = checked(8 + chunks.Sum(chunk => chunk.Bytes.Length));
        var rewritten = new byte[totalSize];
        xml.AsSpan(0, 8).CopyTo(rewritten);
        BinaryPrimitives.WriteUInt32LittleEndian(rewritten.AsSpan(4), (uint)totalSize);
        int offset = 8;
        foreach (var chunk in chunks)
        {
            chunk.Bytes.CopyTo(rewritten, offset);
            offset += chunk.Bytes.Length;
        }
        return rewritten;
    }

    private static List<BinaryXmlChunk> ReadChunks(byte[] xml)
    {
        string[] strings = ReadFixtureStringPool(xml);
        var chunks = new List<BinaryXmlChunk>();
        for (int offset = 8; offset < xml.Length;)
        {
            ushort type = ReadU16(xml, offset);
            int size = checked((int)ReadU32(xml, offset + 4));
            string? name = type is 0x0102 or 0x0103
                ? strings[checked((int)ReadU32(xml, offset + 20))]
                : null;
            chunks.Add(new BinaryXmlChunk(type, name, xml.AsSpan(offset, size).ToArray(), offset));
            offset += size;
        }
        return chunks;
    }

    private static string[] ReadFixtureStringPool(byte[] xml)
    {
        const int poolOffset = 8;
        int headerSize = ReadU16(xml, poolOffset + 2);
        int stringCount = checked((int)ReadU32(xml, poolOffset + 8));
        int stringsStart = checked((int)ReadU32(xml, poolOffset + 20));
        var strings = new string[stringCount];
        for (int index = 0; index < stringCount; index++)
        {
            int relativeOffset = checked((int)ReadU32(xml, poolOffset + headerSize + index * 4));
            int cursor = poolOffset + stringsStart + relativeOffset;
            int utf16Length = ReadUtf16Length(xml, ref cursor);
            strings[index] = System.Text.Encoding.Unicode.GetString(xml, cursor, utf16Length * 2);
        }
        return strings;
    }

    private static ushort ReadU16(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));

    private static uint ReadU32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private sealed record BinaryXmlChunk(ushort Type, string? Name, byte[] Bytes, int Offset);
}
