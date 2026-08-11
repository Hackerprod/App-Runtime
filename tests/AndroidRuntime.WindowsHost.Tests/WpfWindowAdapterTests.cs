using AndroidRuntime.WindowsHost;
using AndroidRuntime.Core;
using AndroidRuntime.Core.Hosting;
using AndroidRuntime.Core.Apk;
using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace AndroidRuntime.WindowsHost.Tests;

[Collection("WPF adapter")]
public sealed class WpfWindowAdapterTests
{
    // The former UiProbe_renders_in_child_hwnd_and_native_pointer_updates_scene_and_toast
    // test is REMOVED: it asserted on the Phase-1 C# view hierarchy (semantic
    // snapshots, RenderUiAsync callbacks, local hit-testing). Phase 2 delegates
    // all view behavior to ViewRuntime; those surfaces no longer exist here.

    [Fact]
    public void Factory_dispose_timeout_can_be_retried_without_orphaning_dispatcher()
    {
        var entered = new ManualResetEventSlim(); var release = new ManualResetEventSlim();
        var factory = new WpfActivityWindowFactory(TimeSpan.FromMilliseconds(50));
        factory.BlockDispatcherForTest(entered, release); Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
        Assert.Throws<TimeoutException>(factory.Dispose); Assert.True(factory.IsDispatcherThreadAlive);
        release.Set(); factory.Dispose(); factory.Dispose(); Assert.False(factory.IsDispatcherThreadAlive);
    }
    [Fact]
    public void Windows_power_snapshot_is_honest_and_bounded()
    {
        var adapter = new WindowsPowerAdapter();
        Assert.True(adapter.IsAvailable);
        HostPowerSnapshot snapshot = adapter.GetSnapshot(CancellationToken.None);
        Assert.True(snapshot.CapacityPercent is null or >= 0 and <= 100);
        if (!snapshot.HasBattery) Assert.Null(snapshot.CapacityPercent);
    }
    [Fact]
    public async Task Clipboard_adapter_contract_uses_STA_backend_focus_gate_and_no_global_clipboard()
    {
        var backend = new FakeClipboardBackend(); bool focused = true;
        using var adapter = new WindowsClipboardAdapter(backend, () => focused);
        await adapter.SetTextAsync("isolated", default);
        Assert.Equal("isolated", await adapter.GetTextAsync(default));
        Assert.Equal(ApartmentState.STA, backend.Apartment);
        focused = false;
        Assert.Null(await adapter.GetTextAsync(default));
        focused = true;
        await adapter.ClearAsync(default);
        Assert.Null(await adapter.GetTextAsync(default));
    }

    [Fact]
    public void Clipboard_adapter_immediate_repeated_dispose_terminates_STA_thread()
    {
        var adapter = new WindowsClipboardAdapter(new FakeClipboardBackend(), () => true);
        adapter.Dispose();
        adapter.Dispose();
        Assert.False(adapter.IsDispatcherThreadAlive);
    }

    [Fact]
    public void ServicesProbe_host_harness_uses_non_global_clipboard_and_correlated_service_traces()
    {
        var backend = new FakeClipboardBackend();
        using var clipboard = new WindowsClipboardAdapter(backend, () => true);
        LoadedApk apk = ApkLoader.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ServicesProbe.apk"));
        AndroidManifest manifest = AndroidManifestReader.Parse(apk.AndroidManifestXml);
        DexFile dex = DexReader.Parse(apk.ClassesDexFiles[0]);
        var state = new AndroidFrameworkState("windows-services", manifest.PackageName, "Lorg/example/runtimeprobe/ServicesProbe;", new ActivityWindowPeers(), declaredPermissions: manifest.UsesPermissions, capabilityPolicy: new AndroidCapabilityPolicy([AndroidCapability.ClipboardRead, AndroidCapability.ClipboardWrite, AndroidCapability.NetworkState]), clipboard: clipboard, connectivity: new FakeConnectivity());
        var trace = new AndroidApiTraceBuffer(256);
        var session = new AndroidApiSessionContext("windows-services", manifest.PackageName, state.ActivityDescriptor, default, () => true, trace);
        var interpreter = new DexInterpreter(dex, AndroidApiBindings.CreateBuilder(state, new QuietLog()).Build(), apiSession: session);

        Assert.Equal(4, interpreter.InvokeStaticExact("Lorg/example/runtimeprobe/ServicesProbe;", "clipboard", "(Landroid/content/Context;)I", state.ApplicationContext));
        Assert.Equal(15, interpreter.InvokeStaticExact("Lorg/example/runtimeprobe/ServicesProbe;", "connectivity", "(Landroid/content/Context;)I", state.ApplicationContext));
        Assert.Null(backend.Text);
        var events = trace.Snapshot();
        Assert.Contains(events, item => item.Invocation.ResolvedApi.MethodName == "getSystemService" && item.Kind == AndroidApiEventKind.Completed);
        Assert.All(events, item => Assert.Equal("windows-services", item.Invocation.SessionId));
    }

    [Fact]
    public void Coarse_connectivity_adapter_never_fabricates_validation_or_metering()
    {
        var snapshot = new WindowsConnectivityAdapter().GetSnapshot(default);
        Assert.False(snapshot.Validated);
        Assert.Null(snapshot.Metered);
        Assert.True(snapshot.Revision > 0);
        Assert.NotEqual(Guid.Empty, snapshot.Token);
    }

    [Fact]
    public void Duplicate_capability_grant_is_rejected_before_launch()
    {
        Assert.Equal(2, Program.Main([FixturePath(), "--grant-network-state", "--grant-network-state"]));
    }
    [Fact]
    public void Windows_clock_clamps_each_source_monotonically_and_reports_missing_uptime_capability()
    {
        var uptime = new Queue<(bool Success, ulong Ticks100ns)>([(true, 20_000), (true, 10_000)]);
        var elapsed = new Queue<ulong>([20, 10]);
        var nanos = new Queue<long>([200, 100]);
        var clock = new WindowsAndroidClock(() => uptime.Dequeue(), () => elapsed.Dequeue(), () => nanos.Dequeue());

        Assert.Equal(2, clock.UptimeMillis());
        Assert.Equal(2, clock.UptimeMillis());
        Assert.Equal(20, clock.ElapsedRealtime());
        Assert.Equal(20, clock.ElapsedRealtime());
        Assert.Equal(200, clock.ElapsedRealtimeNanos());
        Assert.Equal(200, clock.ElapsedRealtimeNanos());

        var unavailable = new WindowsAndroidClock(() => (false, 0), () => 0, () => 0);
        Assert.Throws<InvalidOperationException>(() => unavailable.UptimeMillis());
    }

    [Fact]
    public void Windows_clock_default_sources_are_available_and_non_decreasing()
    {
        var clock = new WindowsAndroidClock();
        long uptime = clock.UptimeMillis();
        long elapsed = clock.ElapsedRealtime();
        long nanos = clock.ElapsedRealtimeNanos();

        Assert.InRange(uptime, 0, elapsed + 60_000);
        Assert.True(clock.UptimeMillis() >= uptime);
        Assert.True(clock.ElapsedRealtime() >= elapsed);
        Assert.True(clock.ElapsedRealtimeNanos() >= nanos);
    }

    [Fact]
    public void Case_alias_trace_path_is_rejected_and_apk_is_preserved()
    {
        string directory = CreateTemporaryDirectory();
        string apk = Path.Combine(directory, "runtimeprobe.apk");
        File.Copy(FixturePath(), apk);
        byte[] originalHash = SHA256.HashData(File.ReadAllBytes(apk));
        try
        {
            int exitCode = Program.Main([apk, "--trace", apk.ToUpperInvariant()]);

            Assert.NotEqual(0, exitCode);
            Assert.Equal(originalHash, SHA256.HashData(File.ReadAllBytes(apk)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Hardlink_alias_trace_path_is_rejected_and_apk_hash_is_preserved()
    {
        string directory = CreateTemporaryDirectory();
        string apk = Path.Combine(directory, "input.apk");
        string trace = Path.Combine(directory, "trace.jsonl");
        File.Copy(FixturePath(), apk);
        if (!CreateHardLink(trace, apk, nint.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        byte[] originalHash = SHA256.HashData(File.ReadAllBytes(apk));
        try
        {
            int exitCode = Program.Main([apk, "--auto-close-ms", "100", "--trace", trace]);

            Assert.NotEqual(0, exitCode);
            Assert.Equal(originalHash, SHA256.HashData(File.ReadAllBytes(apk)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Distinct_trace_path_can_be_locked_without_changing_apk()
    {
        string directory = CreateTemporaryDirectory();
        string apk = Path.Combine(directory, "input.apk");
        string trace = Path.Combine(directory, "trace.jsonl");
        File.Copy(FixturePath(), apk);
        byte[] original = File.ReadAllBytes(apk);
        try
        {
            using var lease = TraceOutputLease.Open(apk, trace);

            Assert.Equal(original, File.ReadAllBytes(apk));
            Assert.True(File.Exists(trace));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Active_host_uses_leased_apk_stream_and_blocks_path_rebinding()
    {
        string directory = CreateTemporaryDirectory();
        string apk = Path.Combine(directory, "input.apk");
        string trace = Path.Combine(directory, "trace.jsonl");
        string replacement = Path.Combine(directory, "replacement.apk");
        File.Copy(FixturePath(), apk);
        File.WriteAllBytes(replacement, [1, 2, 3]);
        try
        {
            using var lease = TraceOutputLease.Open(apk, trace);
            var runtime = new AndroidAppRuntime();
            await using var hosted = await runtime.LaunchSessionAsync(
                lease.ApkStream,
                new AndroidRuntimeServices(new InMemoryActivityWindowFactory(), new ConsoleAndroidLogSink()));

            Assert.Equal(123, hosted.Session.Activity.InstanceFields["lifecycleState"]);
            Exception? rebindError = Record.Exception(() => File.Move(replacement, apk, overwrite: true));
            Assert.True(rebindError is IOException or UnauthorizedAccessException, rebindError?.ToString());
            Assert.Equal(SHA256.HashData(File.ReadAllBytes(FixturePath())), SHA256.HashData(File.ReadAllBytes(apk)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Real_hwnd_accepts_worker_title_change_and_dispatcher_stops_after_close()
    {
        using var factory = new WpfActivityWindowFactory();
        using var window = factory.Create("test", "org.example", "Lexample/Main;", CancellationToken.None);

        Assert.NotEqual(nint.Zero, window.Handle);
        await Task.Run(() => window.SetTitle("Worker title", CancellationToken.None));
        Assert.Equal("Worker title", window.Title);

        window.Close();
        window.Close();
        factory.Dispose();
        Assert.False(factory.IsDispatcherThreadAlive);
    }

    [Fact]
    public void Real_activity_surface_is_input_hit_testable()
    {
        using var factory = new WpfActivityWindowFactory();
        using var window = Assert.IsType<WpfActivityWindow>(
            factory.Create("test", "org.example", "Lexample/Main;", CancellationToken.None));

        window.Show(CancellationToken.None);
        nint surface = window.SurfaceHandle;
        Assert.NotEqual(nint.Zero, surface);
        Assert.NotEqual(0, GetWindowLong(surface, GwlStyle) & SsNotify);
        Assert.True(GetWindowRect(surface, out NativeRect rect));

        int x = rect.Left + ((rect.Right - rect.Left) / 2);
        int y = rect.Top + ((rect.Bottom - rect.Top) / 2);
        Assert.Equal((nint)HtClient, SendMessage(surface, WmNcHitTest, nint.Zero, MakeLParam(x, y)));
    }


    private static string FixturePath() => Path.Combine(AppContext.BaseDirectory, "Fixtures", "RuntimeProbe.apk");

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeClipboardBackend : IWindowsClipboardBackend
    {
        private string? _text;
        internal string? Text => _text;
        internal ApartmentState Apartment { get; private set; }
        public void SetText(string text) { Apartment = Thread.CurrentThread.GetApartmentState(); _text = text; }
        public string? GetText() { Apartment = Thread.CurrentThread.GetApartmentState(); return _text; }
        public void Clear() { Apartment = Thread.CurrentThread.GetApartmentState(); _text = null; }
    }
    private sealed class FakeConnectivity : IAndroidConnectivity
    {
        private readonly Guid _token = Guid.NewGuid();
        public bool IsAvailable => true;
        public AndroidConnectivitySnapshot GetSnapshot(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return new(1, DateTimeOffset.UtcNow, _token, true, true, false, true, true, true, AndroidNetworkTransport.Wifi); }
    }
    private sealed class QuietLog : IAndroidLogSink { public int Info(AndroidLogEntry entry) => 1; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newFileName, string existingFileName, nint securityAttributes);

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hwnd, int message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(nint hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

    private static nint MakeLParam(int low, int high) => (nint)((high << 16) | (low & 0xFFFF));

    private const int GwlStyle = -16;
    private const int SsNotify = 0x0100;
    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

}

[CollectionDefinition("WPF adapter", DisableParallelization = true)]
public sealed class WpfAdapterCollection;
