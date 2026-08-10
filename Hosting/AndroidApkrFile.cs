#nullable enable
using System.IO;
using System.Text;

namespace AndroidRuntime.Core.Hosting;

/// <summary>
/// The .apkr launcher file format (docs\installer-launcher-design.md): a plain,
/// human-readable UTF-8 key=value pointer carrying the installed app's metadata
/// — deliberately NOT a Windows .lnk (custom-metadata ownership + no COM
/// interop; the project avoids native shell tooling throughout). Explorer
/// double-click works because the runtime registers .apkr -> WindowsHost
/// --launch-file "%1" (HKCU-scoped, no admin).
/// </summary>
public sealed class AndroidApkrFile
{
    public const string Extension = "apkr";
    private const string FormatMarker = "AndroidRuntimeLauncher=1";

    private AndroidApkrFile(string package, string displayName, string installedPath, string? iconPath)
    {
        Package = package;
        DisplayName = displayName;
        InstalledPath = installedPath;
        IconPath = iconPath;
    }

    public string Package { get; }
    public string DisplayName { get; }
    public string InstalledPath { get; }
    /// <summary>Icon reference — the design doc marks real icon extraction as an
    /// open question; omitted from the file until a real decode path exists.</summary>
    public string? IconPath { get; }

    public static AndroidApkrFile Create(string package, string displayName, string installedPath, string? iconPath = null) =>
        new(package, displayName, installedPath, iconPath);

    /// <summary>Serializes to the plain text format and writes it to the target
    /// directory. The FILE NAME is the real display name (e.g. "SKYNET APK
    /// Installer.apkr") so Explorer shows the app's name, not the package; the
    /// Package value inside the file is the identity.</summary>
    public string Write(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, AndroidInstaller.LauncherFileName(DisplayName, Package));
        var sb = new StringBuilder();
        sb.AppendLine(FormatMarker);
        sb.AppendLine("Package=" + Package);
        sb.AppendLine("DisplayName=" + DisplayName);
        sb.AppendLine("InstalledPath=" + InstalledPath);
        if (IconPath is not null)
            sb.AppendLine("IconPath=" + IconPath);
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }

    /// <summary>Parses an .apkr file. Returns false (out null) when the file is
    /// missing, malformed, or not an AndroidRuntime launcher file.</summary>
    public static bool TryRead(string path, out AndroidApkrFile? launcher)
    {
        launcher = null;
        try
        {
            if (!File.Exists(path))
                return false;
            string? marker = null, package = null, displayName = null, installedPath = null, iconPath = null;
            foreach (string rawLine in File.ReadLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;
                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                string key = line[..eq].Trim();
                string value = line[(eq + 1)..].Trim();
                switch (key)
                {
                    case "AndroidRuntimeLauncher": marker = value; break;
                    case "Package": package = value; break;
                    case "DisplayName": displayName = value; break;
                    case "InstalledPath": installedPath = value; break;
                    case "IconPath": iconPath = value; break;
                }
            }
            if (marker != "1" || string.IsNullOrWhiteSpace(package) || string.IsNullOrWhiteSpace(installedPath))
                return false;
            launcher = new AndroidApkrFile(package!, displayName ?? package!, installedPath!, iconPath);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
