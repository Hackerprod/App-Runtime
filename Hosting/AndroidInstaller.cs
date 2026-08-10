#nullable enable
using System.IO;
using AndroidRuntime.Core.Apk;

namespace AndroidRuntime.Core.Hosting;

/// <summary>
/// Installer + launcher model (docs\installer-launcher-design.md): an APK is
/// installed ONCE into %LocalAppData%\AndroidRuntime\Apps\&lt;package&gt; and the
/// runtime always launches from that installed state — never re-parsing a raw
/// .apk path on every launch. Mirrors real Android's install-then-run model and
/// per-app data-directory isolation (/data/data/&lt;package&gt;). Per-user,
/// no-admin-needed: not %ProgramFiles% (UAC friction).
/// </summary>
public static class AndroidInstaller
{
    /// <summary>%LocalAppData%\AndroidRuntime\Apps — the installed-apps root.
    /// Overridable via ANDROID_RUNTIME_APPS_ROOT (used by tests to keep the
    /// real install root clean).</summary>
    public static string InstalledRoot =>
        Environment.GetEnvironmentVariable("ANDROID_RUNTIME_APPS_ROOT") is { Length: > 0 } root
            ? Path.GetFullPath(root)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AndroidRuntime", "Apps");

    /// <summary>%LocalAppData%\AndroidRuntime\Apps\&lt;package&gt; — one directory per
    /// installed app holding the launch .apk (and later the icon).</summary>
    public static string AppDirectory(string packageName) => Path.Combine(InstalledRoot, packageName);

    /// <summary>The installed .apk file the runtime always loads from.</summary>
    public static string InstalledApkPath(string packageName) => Path.Combine(AppDirectory(packageName), packageName + ".apk");

    /// <summary>Installs an .apk once: parses its package name, copies the apk
    /// into the per-app directory, extracts the launcher icon (plain PNG
    /// mipmaps; adaptive XML icons are skipped for now — the design doc's icon
    /// open question), resolves the real display label, and writes the .apkr
    /// launcher file to the desktop (or the requested directory). Re-installing
    /// over an existing package replaces the apk in place (update-in-place).
    /// Returns the install result; throws on malformed APKs.</summary>
    public static InstalledApp Install(string apkPath, string? launcherDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apkPath);
        string fullApk = Path.GetFullPath(apkPath);
        if (!File.Exists(fullApk))
            throw new FileNotFoundException("APK path does not exist: " + fullApk);

        LoadedApk apk = ApkLoader.Load(fullApk);
        AndroidManifest manifest = AndroidManifestReader.Parse(apk.AndroidManifestXml);
        if (string.IsNullOrWhiteSpace(manifest.PackageName))
            throw new InvalidDataException("APK manifest has no package name.");

        string package = manifest.PackageName;
        string appDir = AppDirectory(package);
        Directory.CreateDirectory(appDir);
        string installedApk = InstalledApkPath(package);
        File.Copy(fullApk, installedApk, overwrite: true);

        // Real display name: the manifest's android:label (literal or a
        // string-resource reference resolved through the resource table).
        string displayName = ResolveDisplayName(apk, manifest) ?? package;

        // Real launcher icon: resolve android:icon to its file; only plain PNG
        // mipmaps are extracted (adaptive XML icons need a compositor — the
        // design doc's open question).
        string? iconPath = ExtractIcon(apk, manifest, appDir);

        AndroidApkrFile launcher = AndroidApkrFile.Create(package, displayName, appDir, iconPath);
        string launcherPath = launcher.Write(launcherDirectory ?? DesktopDirectory());
        return new InstalledApp(package, displayName, installedApk, launcherPath, iconPath);
    }

    /// <summary>File name used for the .apkr launcher: the REAL display name
    /// (e.g. "SKYNET APK Installer.apkr"), so Explorer shows the app's name —
    /// not the package. Collisions get a numeric suffix; the package stays
    /// inside the file for identity.</summary>
    internal static string LauncherFileName(string displayName, string package)
    {
        string baseName = SanitizeFileName(displayName);
        string candidate = baseName + "." + AndroidApkrFile.Extension;
        int suffix = 1;
        while (File.Exists(Path.Combine(DesktopDirectory(), candidate)) && !OwnsLauncherFile(candidate, package))
            candidate = baseName + " (" + suffix++ + ")." + AndroidApkrFile.Extension;
        return candidate;
    }

    private static bool OwnsLauncherFile(string fileName, string package) =>
        AndroidApkrFile.TryRead(Path.Combine(DesktopDirectory(), fileName), out AndroidApkrFile? existing)
        && existing is not null && string.Equals(existing.Package, package, StringComparison.Ordinal);

    private static string SanitizeFileName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(name) ? "App" : name.Trim();
    }

    private static string? ResolveDisplayName(LoadedApk apk, AndroidManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest.ApplicationLabel))
            return manifest.ApplicationLabel;
        if (manifest.ApplicationLabelResourceId is int labelRef)
        {
            try
            {
                AndroidResourceResolver resolver = AndroidResourceResolver.Create(apk);
                AndroidResourceValue value = resolver.Resolve((uint)labelRef);
                if (value.Kind == AndroidResourceValueKind.String)
                    return value.AsString();
            }
            catch (Exception) { }
        }
        return null;
    }

    private static string? ExtractIcon(LoadedApk apk, AndroidManifest manifest, string appDir)
    {
        if (manifest.ApplicationIconResourceId is not int iconRef)
            return null;
        try
        {
            AndroidResourceResolver resolver = AndroidResourceResolver.Create(apk);
            AndroidResourceValue value = resolver.Resolve((uint)iconRef);
            if (value.Kind != AndroidResourceValueKind.String)
                return null;
            string filePath = value.AsString();
            if (!filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                return null; // adaptive XML / webp icons are not decoded yet
            if (!apk.ResourceFiles.TryGetValue(filePath, out byte[]? bytes) || bytes is null || bytes.Length == 0)
                return null;
            string iconPath = Path.Combine(appDir, "icon.png");
            File.WriteAllBytes(iconPath, bytes);
            return iconPath;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Removes an installed app: the app directory AND its data sandbox
    /// (%LocalAppData%\AndroidRuntime\&lt;package&gt; — getCacheDir/getExternalFilesDir
    /// live there), plus the .apkr launcher file (best-effort).</summary>
    public static void Uninstall(string packageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        TryDeleteDirectory(AppDirectory(packageName));
        TryDeleteDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AndroidRuntime", packageName));
        // Best-effort: remove any .apkr launcher files pointing at this package.
        try
        {
            foreach (string file in Directory.EnumerateFiles(DesktopDirectory(), "*." + AndroidApkrFile.Extension, SearchOption.TopDirectoryOnly))
            {
                if (AndroidApkrFile.TryRead(file, out AndroidApkrFile? launcher) && launcher is not null && string.Equals(launcher.Package, packageName, StringComparison.Ordinal))
                {
                    try { File.Delete(file); } catch (IOException) { }
                }
            }
        }
        catch (IOException) { }
    }

    /// <summary>Lists installed packages (directory names under the installed
    /// root that contain a launch .apk).</summary>
    public static IReadOnlyList<string> ListInstalled()
    {
        if (!Directory.Exists(InstalledRoot))
            return [];
        return Directory.EnumerateDirectories(InstalledRoot)
            .Select(Path.GetFileName)
            .Where(name => name is not null && File.Exists(InstalledApkPath(name!)))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Resolves the installed .apk for a package; null when not installed.</summary>
    public static string? ResolveApk(string packageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        string installed = InstalledApkPath(packageName);
        return File.Exists(installed) ? installed : null;
    }

    /// <summary>Rich view model for the launcher: real display name (label) and
    /// the extracted icon path (null when the icon is adaptive/XML or absent).</summary>
    public sealed record InstalledAppInfo(string Package, string DisplayName, string? IconPath);

    /// <summary>Lists installed apps with their real display names and icons
    /// (from the per-app icon.png + the .apkr metadata when present).</summary>
    public static IReadOnlyList<InstalledAppInfo> GetInstalledApps()
    {
        if (!Directory.Exists(InstalledRoot))
            return [];
        var apps = new List<InstalledAppInfo>();
        foreach (string package in ListInstalled())
        {
            string displayName = package;
            string? iconPath = Path.Combine(AppDirectory(package), "icon.png");
            if (!File.Exists(iconPath))
                iconPath = null;
            // Prefer the .apkr's DisplayName (richer), else the package.
            foreach (string apkr in SafeDesktopApkrFiles())
            {
                if (AndroidApkrFile.TryRead(apkr, out AndroidApkrFile? launcher) && launcher is not null && string.Equals(launcher.Package, package, StringComparison.Ordinal))
                {
                    displayName = launcher.DisplayName;
                    break;
                }
            }
            apps.Add(new InstalledAppInfo(package, displayName, iconPath));
        }
        return apps;
    }

    private static IEnumerable<string> SafeDesktopApkrFiles()
    {
        try
        {
            return Directory.EnumerateFiles(DesktopDirectory(), "*." + AndroidApkrFile.Extension, SearchOption.TopDirectoryOnly);
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string DesktopDirectory() =>
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
}

/// <summary>Result of a successful install.</summary>
public sealed record InstalledApp(string Package, string DisplayName, string InstalledApkPath, string LauncherFilePath, string? IconPath = null);
