using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;
using AndroidRuntime.Core.Ui;

namespace AndroidRuntime.Core.Tests;

public sealed class InstallerLauncherTests
{
    [Fact]
    public void Apkr_file_round_trips_plain_text_metadata()
    {
        string directory = Path.Combine(Path.GetTempPath(), "apkr-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            AndroidApkrFile launcher = AndroidApkrFile.Create("com.example.app", "com.example.app", @"C:\LocalAppData\AndroidRuntime\Apps\com.example.app");
            string path = launcher.Write(directory);

            Assert.True(File.Exists(path));
            string text = File.ReadAllText(path);
            Assert.StartsWith("AndroidRuntimeLauncher=1", text, StringComparison.Ordinal);
            Assert.Contains("Package=com.example.app", text, StringComparison.Ordinal);
            Assert.Contains("DisplayName=com.example.app", text, StringComparison.Ordinal);
            Assert.Contains("InstalledPath=C:\\LocalAppData\\AndroidRuntime\\Apps\\com.example.app", text, StringComparison.Ordinal);

            Assert.True(AndroidApkrFile.TryRead(path, out AndroidApkrFile? parsed));
            Assert.Equal("com.example.app", parsed!.Package);
            Assert.Equal("com.example.app", parsed.DisplayName);
            Assert.Equal(@"C:\LocalAppData\AndroidRuntime\Apps\com.example.app", parsed.InstalledPath);
            Assert.Null(parsed.IconPath);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Apkr_reader_rejects_non_launcher_files()
    {
        string path = Path.Combine(Path.GetTempPath(), "apkr-bad-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(path, "NotALauncher=1\nPackage=x\n");
            Assert.False(AndroidApkrFile.TryRead(path, out _));
            Assert.False(AndroidApkrFile.TryRead(path + "-missing", out _));
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public void Installer_install_uninstall_list_round_trip()
    {
        // Use the RuntimeProbe fixture APK (a real APK with a package name).
        string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "RuntimeProbe.apk");
        string appsRoot = Path.Combine(Path.GetTempPath(), "android-apps-test-" + Guid.NewGuid().ToString("N"));
        string launcherDir = Path.Combine(Path.GetTempPath(), "android-launchers-test-" + Guid.NewGuid().ToString("N"));
        string? previousRoot = Environment.GetEnvironmentVariable("ANDROID_RUNTIME_APPS_ROOT");
        Environment.SetEnvironmentVariable("ANDROID_RUNTIME_APPS_ROOT", appsRoot);
        try
        {
            InstalledApp app = AndroidInstaller.Install(fixture, launcherDir);

            Assert.True(File.Exists(app.InstalledApkPath));
            Assert.True(File.Exists(app.LauncherFilePath));
            Assert.True(AndroidApkrFile.TryRead(app.LauncherFilePath, out AndroidApkrFile? launcher));
            Assert.Equal(app.Package, launcher!.Package);
            Assert.Contains(app.Package, AndroidInstaller.ListInstalled());
            Assert.Equal(app.InstalledApkPath, AndroidInstaller.ResolveApk(app.Package));

            AndroidInstaller.Uninstall(app.Package);
            Assert.DoesNotContain(app.Package, AndroidInstaller.ListInstalled());
            Assert.Null(AndroidInstaller.ResolveApk(app.Package));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANDROID_RUNTIME_APPS_ROOT", previousRoot);
            try { Directory.Delete(appsRoot, recursive: true); } catch (IOException) { }
            try { Directory.Delete(launcherDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Display_state_reconciles_configuration_with_the_reference_device()
    {
        // docs\installer-launcher-design.md: the single display-state source must
        // report the reference device shape (360x732dp @ 3x = 1080x2196px,
        // 480dpi) — NOT the old hardcoded 720x1280 baseline.
        using var state = new AndroidFrameworkState("display", "org.example", "Lorg/example/MainActivity;", new ActivityWindowPeers());
        AndroidDisplayState display = state.DisplayState;

        Assert.Equal(360, display.ScreenWidthDp);
        Assert.Equal(732, display.ScreenHeightDp);
        Assert.Equal(1080, display.ScreenWidthPx);
        Assert.Equal(2196, display.ScreenHeightPx);
        Assert.Equal(480, display.DensityDpi);
        Assert.Equal(1, display.Orientation);

        // The guest Configuration facade reads the same source.
        var registry = AndroidApiBindings.CreateBuilder(state, new QuietLog()).Build();
        var session = new AndroidApiSessionContext(state.SessionId, state.PackageName, state.ActivityDescriptor, CancellationToken.None, () => true);
        var resources = new DexObject("Landroid/content/res/Resources;");
        var api = new AndroidApiMethodId("Landroid/content/res/Resources;", "getConfiguration", "()Landroid/content/res/Configuration;");
        var configuration = (DexObject)registry.Invoke(session, new AndroidApiCallSite("Ltest/Probe;->run()V", 0, api, api, AndroidInvokeKind.Virtual), new object[] { resources });

        Assert.Equal(360, configuration.InstanceFields["Landroid/content/res/Configuration;->screenWidthDp:I"]);
        Assert.Equal(732, configuration.InstanceFields["Landroid/content/res/Configuration;->screenHeightDp:I"]);
        Assert.Equal(480, configuration.InstanceFields["Landroid/content/res/Configuration;->densityDpi:I"]);
    }

    private sealed class QuietLog : IAndroidLogSink { public int Info(AndroidLogEntry entry) => 1; }
}
