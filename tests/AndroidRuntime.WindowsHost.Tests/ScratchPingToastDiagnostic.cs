using AndroidRuntime.Core;
using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;
using AndroidRuntime.WindowsHost;
using System.IO;

namespace AndroidRuntime.WindowsHost.Tests;

/// <summary>TEMP diagnostic: click Ping Google with the real services and dump
/// the FULL flow trace (executor worker, Runtime.exec, the toast path) to find
/// why the result toast doesn't appear.</summary>
[Collection("WPF adapter")]
public sealed class ScratchPingToastDiagnostic
{
    private const int BtnPing = 0x7F030002;

    [Fact]
    public async Task Dump_the_ping_flow_trace()
    {
        string apkPath = Path.Combine(RepoRoot(), ".tmp", "Apks", "RuntimeApiLab-debug.apk");
        using var factory = new WpfActivityWindowFactory();
        using var clipboard = new WindowsClipboardAdapter();
        using var audioRecorder = new WindowsAudioRecorder();
        var services = new AndroidRuntimeServices(
            factory, new ConsoleAndroidLogSink(), traceCapacity: 16384, clock: new WindowsAndroidClock(),
            clipboard: clipboard, connectivity: new WindowsConnectivityAdapter(), power: new WindowsPowerAdapter(),
            capabilityPolicy: new AndroidCapabilityPolicy(new AndroidCapability[]
            {
                AndroidCapability.ClipboardRead, AndroidCapability.ClipboardWrite, AndroidCapability.NetworkState,
                AndroidCapability.PowerRead, AndroidCapability.FileRead, AndroidCapability.FileWrite,
                AndroidCapability.BluetoothScan, AndroidCapability.BluetoothConnect, AndroidCapability.Camera,
                AndroidCapability.NetworkConnect, AndroidCapability.LocationCoarse, AndroidCapability.LocationFine,
                AndroidCapability.Microphone
            }),
            viewBridgeFactory: ViewRuntimeAndroidViewBridge.TryCreate,
            audioRecorder: audioRecorder);
        var runtime = new AndroidAppRuntime();
        var logPath = Path.Combine(RepoRoot(), "artifacts", "ping-toast", "trace.txt");
        File.WriteAllText(logPath, "launching\n");
        await using (var hosted = await runtime.LaunchSessionAsync(apkPath, services))
        {
            File.AppendAllText(logPath, "launched\n");
            DexObject? view = hosted.ViewBridge.FindViewById(BtnPing);
            File.AppendAllText(logPath, $"pingView={view is not null}\n");
            Assert.NotNull(view);
            try { hosted.ViewBridge.PerformClick(view); File.AppendAllText(logPath, "click returned\n"); }
            catch (Exception ex) { File.AppendAllText(logPath, $"click threw {ex.GetType().Name}: {ex.Message}\n"); }
            await Task.Delay(3000);
            File.AppendAllText(logPath, "waited 3s\n");

            var events = hosted.Trace.Snapshot();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== ping flow events ===");
            foreach (var e in events)
            {
                string api = e.Invocation.RequestedApi.ToString();
                if (e.Invocation.CallerMethod.Contains("pingGoogle", StringComparison.Ordinal) ||
                    api.Contains("Toast", StringComparison.Ordinal) ||
                    api.Contains("Runtime", StringComparison.Ordinal) ||
                    api.Contains("Handler", StringComparison.Ordinal) ||
                    api.Contains("Executor", StringComparison.Ordinal) ||
                    api.Contains("ProcessBuilder", StringComparison.Ordinal) ||
                    api.Contains("exec", StringComparison.Ordinal) ||
                    api.Contains("setStatus", StringComparison.Ordinal) ||
                    api.Contains("setText", StringComparison.Ordinal))
                {
                    sb.AppendLine($"kind={e.Kind} api={api} caller={e.Invocation.CallerMethod} err={e.ErrorType}");
                }
            }
            sb.AppendLine("=== all unimplemented ===");
            foreach (var e in events.Where(x => x.Kind == AndroidApiEventKind.Unimplemented))
                sb.AppendLine($"  {e.Invocation.RequestedApi} caller={e.Invocation.CallerMethod}");
            File.AppendAllText(logPath, sb.ToString());
            File.AppendAllText(logPath, "trace dumped\n");
        }
        File.AppendAllText(logPath, "disposed\n");
    }

    private static string RepoRoot()
    {
        string? directory = new DirectoryInfo(AppContext.BaseDirectory).Parent?.FullName;
        while (directory is not null && !File.Exists(Path.Combine(directory, "AndroidRuntime.sln")))
            directory = Directory.GetParent(directory)?.FullName;
        return directory ?? string.Empty;
    }
}
