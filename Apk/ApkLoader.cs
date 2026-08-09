#nullable enable
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;

namespace AndroidRuntime.Core.Apk;

public sealed class ApkLoadLimits
{
    public const int DefaultMaxClassesDexBytes = 128 * 1024 * 1024;
    public const int DefaultMaxSecondaryDexFiles = 64;
    public const int DefaultMaxAndroidManifestBytes = 4 * 1024 * 1024;
    public const int DefaultMaxResourceTableBytes = 32 * 1024 * 1024;
    public const int DefaultMaxResourceFileBytes = 16 * 1024 * 1024;
    public const int DefaultMaxResourceTotalBytes = 128 * 1024 * 1024;

    public static ApkLoadLimits Default { get; } = new();

    public ApkLoadLimits(
        int maxClassesDexBytes = DefaultMaxClassesDexBytes,
        int maxAndroidManifestBytes = DefaultMaxAndroidManifestBytes,
        int maxResourceTableBytes = DefaultMaxResourceTableBytes,
        int maxResourceFileBytes = DefaultMaxResourceFileBytes,
        int maxResourceTotalBytes = DefaultMaxResourceTotalBytes,
        int maxResourceEntries = 4096,
        int maxResourceCompressionRatio = 200,
        int maxSecondaryDexFiles = DefaultMaxSecondaryDexFiles)
    {
        if (maxClassesDexBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxClassesDexBytes), "DEX limit must be positive.");
        if (maxAndroidManifestBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxAndroidManifestBytes), "Manifest limit must be positive.");
        MaxClassesDexBytes = maxClassesDexBytes;
        MaxAndroidManifestBytes = maxAndroidManifestBytes;
        if (maxResourceTableBytes <= 0 || maxResourceFileBytes <= 0 || maxResourceTotalBytes <= 0 || maxResourceEntries <= 0 || maxResourceCompressionRatio <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxResourceTableBytes), "Resource limits must be positive.");
        if (maxSecondaryDexFiles <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSecondaryDexFiles), "Secondary DEX count limit must be positive.");
        MaxResourceTableBytes = maxResourceTableBytes; MaxResourceFileBytes = maxResourceFileBytes; MaxResourceTotalBytes = maxResourceTotalBytes; MaxResourceEntries = maxResourceEntries; MaxResourceCompressionRatio = maxResourceCompressionRatio;
        MaxSecondaryDexFiles = maxSecondaryDexFiles;
    }

    public int MaxClassesDexBytes { get; }
    public int MaxAndroidManifestBytes { get; }
    public int MaxResourceTableBytes { get; }
    public int MaxResourceFileBytes { get; }
    public int MaxResourceTotalBytes { get; }
    public int MaxResourceEntries { get; }
    public int MaxResourceCompressionRatio { get; }
    public int MaxSecondaryDexFiles { get; }
}

public sealed class LoadedApk
{
    internal LoadedApk(IReadOnlyList<byte[]> classesDexFiles, byte[] androidManifestXml, byte[]? resourcesArsc, IDictionary<string, byte[]> resources)
    {
        ClassesDexFiles = classesDexFiles.ToArray();
        AndroidManifestXml = androidManifestXml;
        ResourcesArsc = resourcesArsc?.ToArray();
        ResourceFiles = new ReadOnlyDictionary<string, byte[]>(resources.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal));
    }

    /// <summary>Ordered DEX payloads: classes.dex first, then classes2.dex, classes3.dex, ... in numeric order.</summary>
    public IReadOnlyList<byte[]> ClassesDexFiles { get; }
    public byte[] AndroidManifestXml { get; }
    public byte[]? ResourcesArsc { get; }
    public IReadOnlyDictionary<string, byte[]> ResourceFiles { get; }
}

/// <summary>Loads the ordered multidex DEX set of an APK (classes.dex first, then numeric secondaries).</summary>
public static partial class ApkLoader
{
    public static LoadedApk Load(string path)
        => Load(path, ApkLoadLimits.Default);

    public static LoadedApk Load(string path, ApkLoadLimits limits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Load(stream, limits);
    }

    public static LoadedApk Load(Stream apkStream)
        => Load(apkStream, ApkLoadLimits.Default);

    public static LoadedApk Load(Stream apkStream, ApkLoadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(apkStream);
        ArgumentNullException.ThrowIfNull(limits);
        using var archive = new ZipArchive(apkStream, ZipArchiveMode.Read, leaveOpen: true);
        var dexEntries = archive.Entries.Where(entry => DexEntryName().IsMatch(entry.FullName)).ToArray();

        var dexNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in dexEntries)
            if (!dexNames.Add(entry.FullName))
                throw new InvalidDataException("The APK contains a duplicate DEX entry name: " + entry.FullName);

        var primaryEntries = dexEntries.Where(entry => string.Equals(entry.FullName, "classes.dex", StringComparison.Ordinal)).ToArray();
        if (primaryEntries.Length == 0)
            throw new InvalidDataException("The APK does not contain the required root entry classes.dex.");
        if (primaryEntries.Length > 1)
            throw new InvalidDataException("The APK contains more than one classes.dex entry.");

        int secondaryCount = dexEntries.Length - 1;
        if (secondaryCount > limits.MaxSecondaryDexFiles)
            throw new InvalidDataException($"The APK contains {secondaryCount} secondary DEX files, exceeding the quota of {limits.MaxSecondaryDexFiles}.");

        // Android's real classloader order is numeric (classes.dex, classes2.dex, ...),
        // never ZIP central-directory order and never alphabetical. A gap means Android
        // would stop scanning and silently ignore the higher-numbered files, so a gap is
        // rejected fail-closed instead of loading a file Android itself would skip.
        var orderedDex = dexEntries.OrderBy(entry => DexIndex(entry.FullName)).ToArray();
        for (int i = 1; i < orderedDex.Length; i++)
        {
            int expected = i + 1;
            if (DexIndex(orderedDex[i].FullName) != expected)
                throw new InvalidDataException(
                    "The APK's secondary DEX files are not contiguous: expected classes" + expected +
                    ".dex but found " + orderedDex[i].FullName + ".");
        }

        var classesDexFiles = new List<byte[]>(orderedDex.Length);
        foreach (ZipArchiveEntry entry in orderedDex)
            classesDexFiles.Add(ReadEntry(entry, limits.MaxClassesDexBytes));

        var manifestEntries = archive.Entries
            .Where(entry => string.Equals(entry.FullName, "AndroidManifest.xml", StringComparison.Ordinal))
            .ToArray();
        if (manifestEntries.Length == 0)
            throw new InvalidDataException("The APK does not contain the required root entry AndroidManifest.xml.");
        if (manifestEntries.Length > 1)
            throw new InvalidDataException("The APK contains more than one AndroidManifest.xml entry.");

        var tableEntries = archive.Entries.Where(entry => string.Equals(entry.FullName, "resources.arsc", StringComparison.Ordinal)).ToArray();
        if (tableEntries.Length > 1) throw new InvalidDataException("The APK contains more than one resources.arsc entry.");
        var resourceEntries = archive.Entries.Where(entry => entry.FullName.StartsWith("res/", StringComparison.Ordinal) && !entry.FullName.EndsWith("/", StringComparison.Ordinal)).ToArray();
        if (resourceEntries.Length > limits.MaxResourceEntries) throw new InvalidDataException($"APK resource entry count exceeds {limits.MaxResourceEntries}.");
        var resources = new Dictionary<string, byte[]>(StringComparer.Ordinal); long resourceTotal = 0;
        foreach (ZipArchiveEntry entry in resourceEntries)
        {
            ValidateResourcePath(entry.FullName); ValidateCompressionRatio(entry, limits.MaxResourceCompressionRatio);
            byte[] bytes = ReadEntry(entry, limits.MaxResourceFileBytes); resourceTotal += bytes.Length;
            if (resourceTotal > limits.MaxResourceTotalBytes) throw new InvalidDataException($"APK resources exceed the total uncompressed quota of {limits.MaxResourceTotalBytes} bytes.");
            if (!resources.TryAdd(entry.FullName, bytes)) throw new InvalidDataException("APK contains a duplicate resource path: " + entry.FullName);
        }
        byte[]? table = null;
        if (tableEntries.Length == 1) { ValidateCompressionRatio(tableEntries[0], limits.MaxResourceCompressionRatio); table = ReadEntry(tableEntries[0], limits.MaxResourceTableBytes); }
        return new LoadedApk(classesDexFiles, ReadEntry(manifestEntries[0], limits.MaxAndroidManifestBytes), table, resources);
    }

    /// <summary>Numeric position of a DEX entry: classes.dex = 1, classes2.dex = 2, classes10.dex = 10.</summary>
    private static int DexIndex(string entryName)
    {
        if (string.Equals(entryName, "classes.dex", StringComparison.Ordinal))
            return 1;
        int start = "classes".Length;
        int length = entryName.Length - "classes".Length - ".dex".Length;
        return int.Parse(entryName.AsSpan(start, length), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void ValidateResourcePath(string path)
    {
        if (path.Contains('\\') || path.Split('/').Any(segment => segment is "" or "." or "..")) throw new InvalidDataException("APK resource path is not canonical: " + path);
    }

    private static void ValidateCompressionRatio(ZipArchiveEntry entry, int maximumRatio)
    {
        if (entry.Length > 0 && (entry.CompressedLength == 0 || entry.Length > entry.CompressedLength * (long)maximumRatio))
            throw new InvalidDataException($"APK resource {entry.FullName} exceeds compression ratio {maximumRatio}:1.");
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, int maximumBytes)
    {
        if (entry.Length > maximumBytes)
            throw EntryTooLarge(entry.FullName, maximumBytes);

        using var entryStream = entry.Open();
        using var output = new MemoryStream((int)entry.Length);
        var buffer = new byte[81920];
        long copied = 0;
        while (true)
        {
            int read = entryStream.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;
            if (copied > maximumBytes - read)
                throw EntryTooLarge(entry.FullName, maximumBytes);
            output.Write(buffer, 0, read);
            copied += read;
        }

        if (copied != entry.Length)
            throw new InvalidDataException($"APK entry {entry.FullName} produced {copied} bytes but declared {entry.Length} bytes.");
        return output.ToArray();
    }

    private static InvalidDataException EntryTooLarge(string entryName, int maximumBytes) =>
        new($"APK entry {entryName} exceeds the maximum uncompressed size of {maximumBytes} bytes.");

    [GeneratedRegex(@"^classes(?:[2-9]|[1-9][0-9]+)?\.dex$", RegexOptions.CultureInvariant)]
    private static partial Regex DexEntryName();
}
