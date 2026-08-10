#nullable enable
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Microsoft.Win32;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.WindowsHost;

/// <summary>
/// HKCU-scoped .apkr shell file-type registration (docs\installer-launcher-
/// design.md): .apkr -> "&lt;this exe&gt;" --launch-file "%1". HKCU means no
/// admin/UAC — same no-elevation principle as the install location. Also sets a
/// DefaultIcon so .apkr files show a real launcher icon in Explorer (converted
/// from the extracted app icon; per-app Explorer icons would need a shell
/// extension — out of scope).
/// </summary>
public static class FileAssociation
{
    public const string Extension = ".apkr";
    private const string ProgId = "AndroidRuntime.apkr";

    /// <summary>Registers the .apkr association for the current executable
    /// (HKCU). Idempotent; returns true when it wrote the keys.</summary>
    public static bool Register(string? executablePath = null)
    {
        string exe = executablePath ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to resolve the host executable path.");

        bool wrote = false;
        using (RegistryKey ext = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + Extension))
        {
            string? current = ext.GetValue(null) as string;
            if (current != ProgId)
            {
                ext.SetValue(null, ProgId);
                wrote = true;
            }
        }
        using (RegistryKey progId = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId))
        {
            string? current = progId.GetValue(null) as string;
            if (current != "Android Runtime APK launcher")
            {
                progId.SetValue(null, "Android Runtime APK launcher");
                wrote = true;
            }
            // DefaultIcon: a real launcher ICO so .apkr files don't show the
            // generic exe icon in Explorer.
            string? iconPath = EnsureLauncherIcon();
            if (iconPath is not null)
            {
                string? currentIcon = progId.GetValue("DefaultIcon") as string;
                if (currentIcon != iconPath)
                {
                    progId.SetValue("DefaultIcon", iconPath);
                    wrote = true;
                }
            }
            using RegistryKey command = progId.CreateSubKey(@"shell\open\command");
            string commandLine = $"\"{exe}\" --launch-file \"%1\"";
            string? currentCommand = command.GetValue(null) as string;
            if (currentCommand != commandLine)
            {
                command.SetValue(null, commandLine);
                wrote = true;
            }
        }
        // Always refresh the shell (cheap, idempotent) — the association and its
        // icon may be cached in Explorer even when no key changed.
        NotifyShellAssociationChanged();
        return wrote;
    }

    /// <summary>Tells Explorer to refresh the file-type association + icons
    /// (otherwise the new DefaultIcon/command stay cached and don't show).</summary>
    private static void NotifyShellAssociationChanged()
    {
        try
        {
            const uint SHCNE_ASSOCCHANGED = 0x08000000;
            const uint SHCNF_IDLIST = 0x0000;
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception) { }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    /// <summary>Converts the first installed app's extracted icon.png to a
    /// shared launcher.ico (best-effort; per-app Explorer icons would need a
    /// shell extension). Returns the .ico path or null when no icon exists.</summary>
    private static string? EnsureLauncherIcon()
    {
        try
        {
            string? sourcePng = null;
            foreach (AndroidInstaller.InstalledAppInfo app in AndroidInstaller.GetInstalledApps())
            {
                if (app.IconPath is string icon && File.Exists(icon))
                {
                    sourcePng = icon;
                    break;
                }
            }
            if (sourcePng is null)
                return null;
            string icoPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AndroidRuntime", "launcher.ico");
            if (File.Exists(icoPath) && File.GetLastWriteTimeUtc(sourcePng) <= File.GetLastWriteTimeUtc(icoPath))
                return icoPath;
            using var bitmap = new Bitmap(sourcePng);
            IntPtr hIcon = bitmap.GetHicon();
            try
            {
                using var icon = Icon.FromHandle(hIcon);
                using var stream = new FileStream(icoPath, FileMode.Create, FileAccess.Write);
                icon.Save(stream);
                return icoPath;
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
