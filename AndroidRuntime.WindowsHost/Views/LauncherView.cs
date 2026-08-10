using System.IO;
using AndroidRuntime.Core.Hosting;
using AetherUI.Events;
using AetherUI.Runtime;

namespace AndroidRuntime.WindowsHost.Views;

/// <summary>
/// Installed-apps launcher view (docs\installer-launcher-design.md), rendered by
/// AetherUI's HTML/CSS/C# engine. The .view.html + .view.css companion files are
/// compiled by the AetherUI.Generator into the BuildRenderTree half of this
/// partial; the markup controls (<template a:for / a:if>) and {{ }} bindings are
/// the Modern syntax. Apps show like Android: the real icon (extracted PNG) with
/// the real display name (manifest label) underneath, not the package name.
/// </summary>
public sealed partial class LauncherView : UiComponent
{
    private IReadOnlyList<AndroidInstaller.InstalledAppInfo> InstalledApps { get; set; } = [];
    private string ApkPath { get; set; } = string.Empty;
    private string Status { get; set; } = string.Empty;
    private string HoveredPackage { get; set; } = string.Empty;
    /// <summary>Per-app icon rules emitted as a &lt;style&gt; block (the engine
    /// processes &lt;style&gt; elements in the rendered markup; each icon div is
    /// keyed by id, so the background-image URL is dynamic per app).</summary>
    private string IconStyles { get; set; } = string.Empty;

    /// <summary>UiComponent has NO lifecycle hook (no OnInitialized/OnMount —
    /// base type is System.Object), so the initial state must be computed in
    /// the constructor: without this, IconStyles stays empty until the first
    /// Refresh *event* and no icon renders on launch.</summary>
    public LauncherView()
    {
        RefreshState();
    }

    private void RefreshApps(UiEvent e) => Refresh();

    private void Refresh()
    {
        RefreshState();
        StateHasChanged();
    }

    /// <summary>Recomputes InstalledApps/Status/IconStyles without notifying
    /// the renderer (shared by the constructor and Refresh).</summary>
    private void RefreshState()
    {
        InstalledApps = AndroidInstaller.GetInstalledApps();
        Status = InstalledApps.Count == 0
            ? "No apps installed yet."
            : InstalledApps.Count + (InstalledApps.Count == 1 ? " app installed." : " apps installed.");
        var styles = new System.Text.StringBuilder();
        foreach (AndroidInstaller.InstalledAppInfo app in InstalledApps)
        {
            if (app.IconPath is string icon)
                styles.Append("#").Append(IconId(app.Package)).Append(" { background: url('file:///")
                    .Append(icon.Replace('\\', '/'))
                    .Append("') center / cover no-repeat; }\n");
        }
        IconStyles = styles.ToString();
    }

    private void OnApkPathInput(UiEvent e)
    {
        ApkPath = e.Value ?? string.Empty;
        StateHasChanged();
    }

    /// <summary>Opens a native file explorer to pick an .apk and installs it.
    /// Used by both the navbar "Install APK" menu and the empty-state card.</summary>
    private void OpenInstaller(UiEvent e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select an APK to install",
            Filter = "Android packages (*.apk)|*.apk|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            InstalledApp app = AndroidInstaller.Install(dialog.FileName);
            FileAssociation.Register();
            Status = "Installed " + app.DisplayName + ".";
        }
        catch (Exception error)
        {
            Status = "Install failed: " + error.Message;
        }
        Refresh();
    }

    private void OpenApp(UiEvent e)
    {
        string? package = PackageFrom(e);
        if (package is null) return;
        string exe = Environment.ProcessPath ?? string.Empty;
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false };
            startInfo.ArgumentList.Add("--launch");
            startInfo.ArgumentList.Add(package);
            // The launcher is the user-facing surface: apps open with the full
            // capability grant set (like a real Android launcher where install-
            // time permissions are already granted). The raw CLI path keeps the
            // deny-by-default model for validation.
            foreach (string grant in AllGrants)
                startInfo.ArgumentList.Add(grant);
            System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start the app host.");
            // The app window can open BEHIND the launcher — bring it to front.
            _ = Task.Run(async () =>
            {
                for (int attempt = 0; attempt < 25; attempt++)
                {
                    await Task.Delay(200);
                    if (process.HasExited) break;
                    if (process.MainWindowHandle != 0)
                    {
                        WindowsHostNative.SetForegroundWindow(process.MainWindowHandle);
                        break;
                    }
                }
            });
        }
        catch (Exception error)
        {
            Status = "Launch failed: " + error.Message;
            StateHasChanged();
        }
    }

    private static readonly string[] AllGrants =
    [
        "--grant-clipboard-read", "--grant-clipboard-write", "--grant-network-state", "--grant-power",
        "--grant-file-read", "--grant-file-write", "--grant-bluetooth-scan", "--grant-bluetooth-connect",
        "--grant-camera", "--grant-network-connect", "--grant-location-coarse", "--grant-location-fine",
        "--grant-microphone"
    ];

    private void UninstallApp(UiEvent e)
    {
        string? package = PackageFrom(e);
        if (package is null) return;
        AndroidInstaller.Uninstall(package);
        Status = "Uninstalled " + package + ".";
        Refresh();
    }

    /// <summary>Hover state per tile: shows the small uninstall "×" on the
    /// hovered app, like Android's launcher long-press affordance.</summary>
    private void TileOver(UiEvent e) => TileHover(e, over: true);

    private void TileOut(UiEvent e) => TileHover(e, over: false);

    private void TileHover(UiEvent e, bool over)
    {
        string? id = e.CurrentTarget?.Id ?? e.Target?.Id;
        if (id is null || !id.StartsWith("tile-", StringComparison.Ordinal)) return;
        HoveredPackage = over ? id[5..] : string.Empty;
        StateHasChanged();
    }

    private string UninstallClass(string package) =>
        string.Equals(package, HoveredPackage, StringComparison.Ordinal) ? "tile-uninstall visible" : "tile-uninstall";

    /// <summary>Per-item buttons carry id="open-&lt;package&gt;" / "uninstall-&lt;package&gt;"
    /// and tiles carry id="tile-&lt;package&gt;". The CLICK lands on the deepest
    /// element under the cursor (a child span/icon without an id), so the
    /// handler reads CurrentTarget — the element the handler is bound to —
    /// first, then falls back to the raw Target.</summary>
    private static string? PackageFrom(UiEvent e)
    {
        string? id = e.CurrentTarget?.Id ?? e.Target?.Id;
        if (id is null) return null;
        int dash = id.IndexOf('-');
        return dash >= 0 && dash + 1 < id.Length ? id[(dash + 1)..] : null;
    }

    private static string TileId(string package) => "tile-" + package;
    private static string OpenId(string package) => "open-" + package;
    private static string UninstallId(string package) => "uninstall-" + package;
    private static string IconId(string package) => "icon-" + SanitizeId(package);

    /// <summary>CSS id selectors cannot contain dots (they parse as classes) —
    /// packages do, so ids are sanitized; the click handlers read the RAW id
    /// (buttons carry the unsanitized package) so recovery is exact.</summary>
    private static string SanitizeId(string package) =>
        new string(package.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_').ToArray());
}
