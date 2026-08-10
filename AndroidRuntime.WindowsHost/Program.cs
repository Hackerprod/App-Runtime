#nullable enable
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using AndroidRuntime.Core;
using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.WindowsHost;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            return RunAsync(args).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Launch cancelled.");
            return 3;
        }
        catch (ArgumentException error)
        {
            Console.Error.WriteLine(error.Message);
            return 2;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        var options = Parse(args);
        using var traceOutput = TraceOutputLease.Open(options.ApkPath, options.TracePath);
        using var capabilityAudit = options.CapabilityAuditPath is string auditPath ? new FileAndroidCapabilityAuditSink(auditPath) : null;
        using var factory = new WpfActivityWindowFactory();
        using var clipboard = new WindowsClipboardAdapter();
        var connectivity = new WindowsConnectivityAdapter();
        var logs = new ConsoleAndroidLogSink();
        var runtime = new AndroidAppRuntime();
        await using var hosted = await runtime.LaunchSessionAsync(
            traceOutput.ApkStream,
            new AndroidRuntimeServices(factory, logs, traceCapacity: 4096, clock: new WindowsAndroidClock(), capabilityPolicy: new AndroidCapabilityPolicy(options.Grants), clipboard: clipboard, connectivity: connectivity, power: new WindowsPowerAdapter(), viewBridgeFactory: ViewRuntimeAndroidViewBridge.TryCreate, capabilityAudit: capabilityAudit));
        Console.WriteLine($"READY hwnd={hosted.Window.Handle} title={hosted.Window.Title}");

        if (options.CaptureFramePath is string capturePath)
        {
            // Wait for at least one real rendered frame, then write it as a BMP.
            WindowsFrameCapture capture = await WaitForRenderedFrameAsync(hosted.Window, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            BmpFrameWriter.Write(capturePath, capture);
            Console.WriteLine($"CAPTURED {capturePath} {capture.Width}x{capture.Height} rev={capture.Revision} sha={capture.Sha256}");
        }

        if (options.AutoCloseMilliseconds is int delay)
        {
            await Task.Delay(delay).ConfigureAwait(false);
        }
        else
        {
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            hosted.Window.Closed += (_, _) => closed.TrySetResult();
            if (!hosted.Window.IsClosed)
                await closed.Task.ConfigureAwait(false);
        }

        if (traceOutput.Stream is not null)
            WriteTrace(traceOutput.Stream, hosted.Trace);
        return 0;
    }

    private static async Task<WindowsFrameCapture> WaitForRenderedFrameAsync(IActivityWindow window, TimeSpan timeout)
    {
        // The real WPF window is a WpfActivityWindow (the factory only creates those);
        // poll its surface until a frame with a non-trivial revision has rendered.
        if (window is not WpfActivityWindow wpf)
            throw new ArgumentException("Capture requires the WpfActivityWindow host.", nameof(window));
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            WindowsFrameCapture capture = wpf.CaptureSurface();
            if (capture.Revision > 0) return capture;
            await Task.Delay(100).ConfigureAwait(false);
        }
        throw new TimeoutException($"No rendered frame within {timeout.TotalSeconds:0} seconds.");
    }

    private static HostOptions Parse(string[] args)
    {
        if (args.Length == 0)
            throw new ArgumentException("Usage: AndroidRuntime.WindowsHost <apk> [--auto-close-ms <100..600000>] [--trace <path>] [--capture-frame <path.bmp>] [--capability-audit <path>] [--grant-clipboard-read] [--grant-clipboard-write] [--grant-network-state] [--grant-power] [--grant-file-read] [--grant-file-write] [--grant-bluetooth-scan] [--grant-bluetooth-connect] [--grant-camera] [--grant-network-connect] [--grant-location-coarse] [--grant-location-fine] [--grant-microphone]");
        string apkPath = Path.GetFullPath(args[0]);
        if (!File.Exists(apkPath) || !string.Equals(Path.GetExtension(apkPath), ".apk", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("APK path does not exist or is not an .apk file: " + apkPath);
        int? autoClose = null;
        string? tracePath = null;
        string? captureFramePath = null;
        string? capabilityAuditPath = null;
        var grants = new HashSet<AndroidCapability>();
        for (int index = 1; index < args.Length; index++)
        {
            if (args[index] == "--auto-close-ms" && index + 1 < args.Length &&
                int.TryParse(args[++index], out int milliseconds) && milliseconds is >= 100 and <= 600000)
            {
                autoClose = milliseconds;
                continue;
            }
            if (args[index] == "--trace" && index + 1 < args.Length)
            {
                tracePath = Path.GetFullPath(args[++index]);
                continue;
            }
            if (args[index] == "--capture-frame" && index + 1 < args.Length)
            {
                captureFramePath = Path.GetFullPath(args[++index]);
                if (!string.Equals(Path.GetExtension(captureFramePath), ".bmp", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("--capture-frame requires a .bmp path (BGRA32 top-down BMP, no extra dependency).");
                continue;
            }
            if (args[index] == "--capability-audit" && index + 1 < args.Length)
            {
                capabilityAuditPath = Path.GetFullPath(args[++index]);
                continue;
            }
            AndroidCapability? grant = args[index] switch
            {
                "--grant-clipboard-read" => AndroidCapability.ClipboardRead,
                "--grant-clipboard-write" => AndroidCapability.ClipboardWrite,
                "--grant-network-state" => AndroidCapability.NetworkState,
                "--grant-power" => AndroidCapability.PowerRead,
                "--grant-file-read" => AndroidCapability.FileRead,
                "--grant-file-write" => AndroidCapability.FileWrite,
                "--grant-bluetooth-scan" => AndroidCapability.BluetoothScan,
                "--grant-bluetooth-connect" => AndroidCapability.BluetoothConnect,
                "--grant-camera" => AndroidCapability.Camera,
                "--grant-network-connect" => AndroidCapability.NetworkConnect,
                "--grant-location-coarse" => AndroidCapability.LocationCoarse,
                "--grant-location-fine" => AndroidCapability.LocationFine,
                "--grant-microphone" => AndroidCapability.Microphone,
                _ => null
            };
            if (grant.HasValue)
            {
                if (!grants.Add(grant.Value)) throw new ArgumentException("Duplicate host grant: " + args[index]);
                continue;
            }
            throw new ArgumentException("Invalid host option near: " + args[index]);
        }
        if (tracePath is not null && PathsAlias(apkPath, tracePath))
            throw new ArgumentException("Trace output path must not alias the input APK path.");
        if (captureFramePath is not null && PathsAlias(apkPath, captureFramePath))
            throw new ArgumentException("Capture output path must not alias the input APK path.");
        if (capabilityAuditPath is not null && (PathsAlias(apkPath, capabilityAuditPath) || (tracePath is not null && PathsAlias(tracePath, capabilityAuditPath))))
            throw new ArgumentException("Capability audit output path must not alias the input APK or trace path.");
        return new HostOptions(apkPath, autoClose, tracePath, captureFramePath, capabilityAuditPath, grants.ToArray());
    }

    private static bool PathsAlias(string left, string right)
    {
        string normalizedLeft = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
        string normalizedRight = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteTrace(FileStream output, AndroidApiTraceBuffer trace)
    {
        var lines = trace.Snapshot().Select(item => JsonSerializer.Serialize(new
        {
            kind = item.Kind.ToString(),
            sequence = item.Invocation.Sequence,
            invocationId = item.Invocation.InvocationId,
            caller = item.Invocation.CallerMethod,
            dexPc = item.Invocation.DexPc,
            requested = item.Invocation.RequestedApi.ToString(),
            resolved = item.Invocation.ResolvedApi.ToString(),
            invokeKind = item.Invocation.InvokeKind.ToString(),
            arguments = item.Invocation.ArgumentSummaries,
            thread = item.Invocation.ManagedThreadId,
            isMainLane = item.Invocation.IsMainLane,
            session = item.Invocation.SessionId,
            packageName = item.Invocation.PackageName,
            activity = item.Invocation.ActivityDescriptor,
            errorType = item.ErrorType
        }));
        output.Position = 0;
        output.SetLength(0);
        using var writer = new StreamWriter(output, new System.Text.UTF8Encoding(false), leaveOpen: true);
        foreach (string line in lines)
            writer.WriteLine(line);
        writer.Flush();
        output.Flush(flushToDisk: true);
    }

    private sealed record HostOptions(string ApkPath, int? AutoCloseMilliseconds, string? TracePath, string? CaptureFramePath, string? CapabilityAuditPath, IReadOnlyCollection<AndroidCapability> Grants);
}

internal sealed class TraceOutputLease : IDisposable
{
    private TraceOutputLease(FileStream apkStream, FileStream? stream)
    {
        ApkStream = apkStream;
        Stream = stream;
    }

    internal FileStream ApkStream { get; }
    internal FileStream? Stream { get; }

    internal static TraceOutputLease Open(string apkPath, string? tracePath)
    {
        if (tracePath is not null && PathsEqual(apkPath, tracePath))
            throw new ArgumentException("Trace output path must not alias the input APK path.");

        FileStream? input = null;
        FileStream? output = null;
        try
        {
            input = new FileStream(apkPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (tracePath is null)
                return new TraceOutputLease(input, null);

            string? directory = Path.GetDirectoryName(tracePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            if (File.Exists(tracePath))
            {
                using var existingOutput = new FileStream(tracePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (FileIdentity.From(input.SafeFileHandle) == FileIdentity.From(existingOutput.SafeFileHandle))
                    throw new ArgumentException("Trace output resolves to the same Windows file as the input APK.");
            }

            output = new FileStream(tracePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            if (FileIdentity.From(input.SafeFileHandle) == FileIdentity.From(output.SafeFileHandle))
                throw new ArgumentException("Trace output resolves to the same Windows file as the input APK.");
            return new TraceOutputLease(input, output);
        }
        catch
        {
            output?.Dispose();
            input?.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Stream?.Dispose();
        ApkStream.Dispose();
    }

    private static bool PathsEqual(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        StringComparison.OrdinalIgnoreCase);

    private readonly record struct FileIdentity(uint VolumeSerialNumber, uint FileIndexHigh, uint FileIndexLow)
    {
        internal static FileIdentity From(SafeFileHandle handle)
        {
            if (!GetFileInformationByHandle(handle, out var information))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to verify file identity safely.");
            return new FileIdentity(
                information.VolumeSerialNumber,
                information.FileIndexHigh,
                information.FileIndexLow);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        internal uint FileAttributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle fileHandle,
        out ByHandleFileInformation fileInformation);
}
