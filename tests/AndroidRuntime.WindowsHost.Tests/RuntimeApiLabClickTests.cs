using AndroidRuntime.Core;
using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;
using AndroidRuntime.WindowsHost;
using System.IO;

namespace AndroidRuntime.WindowsHost.Tests;

/// <summary>
/// Real click dispatch end-to-end against the project owner's own test APK
/// (.tmp\Apks\RuntimeApiLab-debug.apk — buttons wired with programmatic
/// lambdas that call real guest methods like pingGoogle / createStorageFile).
/// The test launches the real session with the real ViewRuntime bridge, finds
/// each button by its real resource id, performs a real PerformClick through
/// the bridge (programmatic listener lookup + real DEX execution of the guest
/// onClick), and proves the guest handler actually ran by observing NEW
/// interpreter API activity in the session trace whose caller is a guest frame.
///
/// NOTE on coordinate hit-testing: the mouse-click path (HitTest -> id ->
/// PerformClick) is NOT exercisable on this APK yet because ViewRuntime lays
/// out only the root ScrollView — its children measure with zero bounds, so
/// hit-test returns the root (id 0) at every pixel and the rendered frame is
/// black. That is a ViewRuntime-side measure/layout fidelity gap (this side
/// serializes correct ids + structure; verified). Dispatch itself is proven
/// through the identical bridge path below.
/// </summary>
[Collection("WPF adapter")]
public sealed class RuntimeApiLabClickTests
{
    // Real button resource ids from RuntimeApiLab's layout (verified by
    // serializing activity_main: Button nodes at 0x7F030002/01/00/04/03/05).
    private static readonly int[] ButtonIds = [0x7F030002, 0x7F030001, 0x7F030000, 0x7F030004, 0x7F030003, 0x7F030005];

    [Fact]
    public async Task Real_click_dispatches_to_a_real_guest_onclick_handler()
    {
        string apkPath = FindRuntimeApiLabApk();
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

        var dispatched = new List<string>();
        foreach (int buttonId in ButtonIds)
        {
            DexObject? view = hosted.ViewBridge.FindViewById(buttonId);
            if (view is null) continue;
            var before = TraceTerminalSet(hosted.Trace);
            bool consumed = false;
            try
            {
                consumed = hosted.ViewBridge.PerformClick(view);
            }
            catch (Exception)
            {
                // A guest onClick may hit an honest unimplemented-API boundary
                // (e.g. getExternalFilesDir) — that is EXPECTED for this unit:
                // the dispatch ran real guest DEX; the trace below proves it.
            }
            IReadOnlyList<AndroidApiTraceEvent> after = await WaitForNewTerminalAsync(hosted.Trace, before, TimeSpan.FromSeconds(10));
            var newTerminals = after
                .Where(item => item.Kind != AndroidApiEventKind.Requested && !before.Contains(TerminalKey(item)))
                .ToList();
            foreach (AndroidApiTraceEvent item in newTerminals)
            {
                dispatched.Add(
                    $"buttonId=0x{buttonId:X8} consumed={consumed} kind={item.Kind} caller={item.Invocation.CallerMethod} api={item.Invocation.RequestedApi} err={item.ErrorType}");
            }
        }

        // At least one button must have performed a REAL click whose guest
        // onClick executed real guest DEX (new API activity from an app frame).
        Assert.NotEmpty(dispatched);
        Assert.All(dispatched, line =>
        {
            Assert.True(
                line.Contains("runtimeapitest", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("MainActivity", StringComparison.OrdinalIgnoreCase),
                "Dispatch evidence caller is not an app guest frame: " + line);
        });

        // Persist the evidence for the handoff report.
        string outDir = Path.Combine(RepoRoot(), "artifacts", "click-dispatch");
        Directory.CreateDirectory(outDir);
        File.WriteAllLines(Path.Combine(outDir, "click-dispatch-evidence.txt"), dispatched);
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

    private static string FindRuntimeApiLabApk()
    {
        string candidate = Path.Combine(RepoRoot(), ".tmp", "Apks", "RuntimeApiLab-debug.apk");
        Assert.True(File.Exists(candidate), "RuntimeApiLab test APK must exist at " + candidate);
        return candidate;
    }

    private static string RepoRoot()
    {
        string? directory = new DirectoryInfo(AppContext.BaseDirectory).Parent?.FullName;
        while (directory is not null && !File.Exists(Path.Combine(directory, "AndroidRuntime.sln")))
            directory = Directory.GetParent(directory)?.FullName;
        return directory ?? string.Empty;
    }
}
