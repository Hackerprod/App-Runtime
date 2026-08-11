using System.IO;
using AndroidRuntime.Core;
using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;
using AndroidRuntime.WindowsHost;
using Xunit.Abstractions;

namespace AndroidRuntime.WindowsHost.Tests;

/// <summary>
/// AndroidInputManager end-to-end: a pointer DOWN then UP on the same view is a
/// real TAP that dispatches the guest onClick (real DEX), exactly like Android
/// touch semantics. This exercises the same bridge path as RuntimeApiLabClickTests
/// but through the InputManager (the single input entry point).
/// </summary>
[Collection("WPF adapter")]
public sealed class AndroidInputManagerTapTests
{
    private readonly ITestOutputHelper _output;
    public AndroidInputManagerTapTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Pointer_down_up_on_button_dispatches_guest_onclick()
    {
        string apkPath = Path.Combine(RepoRoot(), ".tmp", "Apks", "RuntimeApiLab-debug.apk");
        using var factory = new WpfActivityWindowFactory();
        using var clipboard = new WindowsClipboardAdapter();
        var services = new AndroidRuntimeServices(
            factory,
            new ConsoleAndroidLogSink(),
            traceCapacity: 8192,
            clock: new WindowsAndroidClock(),
            clipboard: clipboard,
            connectivity: new WindowsConnectivityAdapter(),
            power: new WindowsPowerAdapter(),
            viewBridgeFactory: ViewRuntimeAndroidViewBridge.TryCreate);
        var runtime = new AndroidAppRuntime();
        await using var hosted = await runtime.LaunchSessionAsync(apkPath, services);

        var window = Assert.IsType<WpfActivityWindow>(hosted.Window);
        await WaitForFrameAsync(window, TimeSpan.FromSeconds(20));

        // Find the first button's hit-test center on the real surface.
        int buttonId = 0x7F030002;
        int? centerX = null, centerY = null;
        var capture = window.CaptureSurface();
        for (int y = 0; y < capture.Height && centerY is null; y += 2)
            for (int x = 0; x < capture.Width && centerX is null; x += 2)
                if (hosted.ViewBridge.HitTest(x, y) == buttonId) { centerX = x; centerY = y; break; }
        Assert.NotNull(centerX);
        _output.WriteLine($"button 0x{buttonId:X8} hit at ({centerX},{centerY}) surface {capture.Width}x{capture.Height}");

        // Inject a real tap through the surface (which routes to InputManager).
        var baseline = TraceTerminalSet(hosted.Trace);
        window.InjectPointerClick(centerX!.Value, centerY!.Value);
        var after = await WaitForNewTerminalAsync(hosted.Trace, baseline, TimeSpan.FromSeconds(10));
        var newTerminals = after
            .Where(item => item.Kind != AndroidApiEventKind.Requested && !baseline.Contains(TerminalKey(item)))
            .ToList();
        _output.WriteLine($"new terminal events after tap: {newTerminals.Count}");
        foreach (var item in newTerminals)
            _output.WriteLine($"  kind={item.Kind} caller={item.Invocation.CallerMethod} api={item.Invocation.RequestedApi} err={item.ErrorType}");

        // A real guest onClick ran: caller must be an app guest frame.
        Assert.NotEmpty(newTerminals);
        Assert.All(newTerminals, item =>
            Assert.True(
                (item.Invocation.CallerMethod?.Contains("runtimeapitest", StringComparison.OrdinalIgnoreCase) ?? false) ||
                (item.Invocation.CallerMethod?.Contains("MainActivity", StringComparison.OrdinalIgnoreCase) ?? false),
                "tap dispatch evidence caller is not an app guest frame: " + item.Invocation.CallerMethod));
    }

    private static async Task WaitForFrameAsync(WpfActivityWindow window, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var capture = window.CaptureSurface();
            if (capture.Revision > 0) return;
            await Task.Delay(100);
        }
        throw new TimeoutException("RuntimeApiLab did not render a frame within " + timeout.TotalSeconds + "s.");
    }

    private static async Task<IReadOnlyList<AndroidApiTraceEvent>> WaitForNewTerminalAsync(
        AndroidApiTraceBuffer trace, HashSet<string> baseline, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = trace.Snapshot();
            if (snapshot.Any(item => item.Kind != AndroidApiEventKind.Requested && !baseline.Contains(TerminalKey(item))))
                return snapshot;
            await Task.Delay(100);
        }
        return trace.Snapshot();
    }

    private static HashSet<string> TraceTerminalSet(AndroidApiTraceBuffer trace) =>
        trace.Snapshot()
            .Where(item => item.Kind != AndroidApiEventKind.Requested)
            .Select(TerminalKey)
            .ToHashSet(StringComparer.Ordinal);

    private static string TerminalKey(AndroidApiTraceEvent item) =>
        item.Kind + "|" + item.Invocation.CallerMethod + "|" + item.Invocation.RequestedApi + "|" + item.ErrorType;

    private static string RepoRoot()
    {
        string? directory = new DirectoryInfo(AppContext.BaseDirectory).Parent?.FullName;
        while (directory is not null && !File.Exists(Path.Combine(directory, "AndroidRuntime.sln")))
            directory = Directory.GetParent(directory)?.FullName;
        return directory ?? string.Empty;
    }
}
