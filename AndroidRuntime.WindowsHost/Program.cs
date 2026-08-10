#nullable enable
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using AetherUI.Primitives;
using AetherUI.Runtime;
using AetherUI.Windows.Hosting;
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
        // Installer + launcher commands (docs\installer-launcher-design.md).
        if (args.Length == 0)
            return RunLauncher();
        switch (args[0])
        {
            case "--install":
                return RunInstall(args);
            case "--launch":
                return await RunLaunchAsync(args).ConfigureAwait(false);
            case "--launch-file":
                return await RunLaunchFileAsync(args).ConfigureAwait(false);
            case "--list-installed":
                return RunListInstalled();
            case "--uninstall":
                return RunUninstall(args);
            case "--register-file-association":
                return RunRegisterAssociation();
        }
        var options = Parse(args);
        return await LaunchAsync(options.ApkPath, options).ConfigureAwait(false);
    }

    private static int RunInstall(string[] args)
    {
        // --install <apk> [--launcher-dir <dir>]
        if (args.Length < 2)
            throw new ArgumentException("Usage: --install <apk> [--launcher-dir <dir>]");
        string? launcherDir = null;
        for (int index = 2; index < args.Length; index++)
            if (args[index] == "--launcher-dir" && index + 1 < args.Length)
                launcherDir = args[++index];
        InstalledApp app = AndroidInstaller.Install(args[1], launcherDir);
        FileAssociation.Register(); // after install so the DefaultIcon finds the extracted icon
        Console.WriteLine($"INSTALLED package={app.Package} apk={app.InstalledApkPath} launcher={app.LauncherFilePath}");
        return 0;
    }

    private static int RunListInstalled()
    {
        IReadOnlyList<string> packages = AndroidInstaller.ListInstalled();
        if (packages.Count == 0)
        {
            Console.WriteLine("INSTALLED_APPS 0");
            return 0;
        }
        Console.WriteLine($"INSTALLED_APPS {packages.Count}");
        foreach (string package in packages)
            Console.WriteLine(package);
        return 0;
    }

    private static int RunUninstall(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("Usage: --uninstall <package>");
        AndroidInstaller.Uninstall(args[1]);
        Console.WriteLine($"UNINSTALLED {args[1]}");
        return 0;
    }

    private static int RunRegisterAssociation()
    {
        bool wrote = FileAssociation.Register();
        Console.WriteLine(wrote ? "REGISTERED .apkr association" : "REGISTERED .apkr association (already current)");
        return 0;
    }

    private static int RunLauncher()
    {
        FileAssociation.Register();
        // Primary UI: the AetherUI launcher (installed-apps view). Falls back to
        // the WPF window only when the AetherUI native core is unavailable.
        try
        {
            return WindowsUiApplication.Run<AndroidRuntime.WindowsHost.Views.LauncherView>(new UiHostOptions
            {
                Title = "Android Runtime",
                Width = 900,
                Height = 680,
                MinimumWidth = 520,
                MinimumHeight = 460,
                ClearColor = new ColorRgba(0.965f, 0.973f, 0.984f, 1f),
                // AetherUI's own Windows image loader, wrapped to strip the
                // quotes the engine leaves on url('...') background-image
                // sources before they reach the resolver (verified: the raw
                // quoted URI is rejected by policy, the clean one decodes).
                // AetherUI's own Windows image loader decodes file:// icon
                // paths through the engine's native pipeline. The engine-side
                // fix for its HTML-encoded url('...') quoting (tracked by the
                // AetherUI maintainer) makes this the only loader needed.
                ImageResolver = new ImageResourceResolver(new AetherUI.Windows.Resources.FileImageResourceLoader()),
            });
        }
        catch (DllNotFoundException)
        {
            // AetherUI native core missing — degrade to the WPF fallback window.
        }
        catch (BadImageFormatException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
        var window = new LauncherWindow();
        window.ShowDialog();
        return 0;
    }

    private static async Task<int> RunLaunchAsync(string[] args)
    {
        // --launch <package> [options]
        if (args.Length < 2)
            throw new ArgumentException("Usage: --launch <package> [options]");
        string package = args[1];
        string? apk = AndroidInstaller.ResolveApk(package);
        if (apk is null)
        {
            Console.Error.WriteLine("App is not installed: " + package);
            return 2;
        }
        var options = Parse(RebaseArgs(apk, args, skip: 2));
        return await LaunchAsync(apk, options).ConfigureAwait(false);
    }

    private static async Task<int> RunLaunchFileAsync(string[] args)
    {
        // --launch-file <file.apkr> [options]
        if (args.Length < 2)
            throw new ArgumentException("Usage: --launch-file <file.apkr> [options]");
        if (!AndroidApkrFile.TryRead(args[1], out AndroidApkrFile? launcher) || launcher is null)
        {
            Console.Error.WriteLine("Invalid launcher file: " + args[1]);
            return 2;
        }
        string? apk = AndroidInstaller.ResolveApk(launcher.Package);
        if (apk is null)
        {
            Console.Error.WriteLine("App is not installed: " + launcher.Package);
            return 2;
        }
        var options = Parse(RebaseArgs(apk, args, skip: 2));
        return await LaunchAsync(apk, options).ConfigureAwait(false);
    }

    /// <summary>Rebuilds the argument list with the resolved apk first so the
    /// existing option parser is reused unchanged: [apk, ...remaining].</summary>
    private static string[] RebaseArgs(string apkPath, string[] args, int skip)
    {
        var result = new string[1 + (args.Length - skip)];
        result[0] = apkPath;
        Array.Copy(args, skip, result, 1, args.Length - skip);
        return result;
    }

    private static async Task<int> LaunchAsync(string apkPath, HostOptions options)
    {
        using var traceOutput = TraceOutputLease.Open(apkPath, options.TracePath);
        using var capabilityAudit = options.CapabilityAuditPath is string auditPath ? new FileAndroidCapabilityAuditSink(auditPath) : null;
        using var audioRecorder = new WindowsAudioRecorder();
        using var factory = new WpfActivityWindowFactory();
        using var clipboard = new WindowsClipboardAdapter();
        var connectivity = new WindowsConnectivityAdapter();
        var logs = new ConsoleAndroidLogSink();
        var runtime = new AndroidAppRuntime();
        await using var hosted = await runtime.LaunchSessionAsync(
            traceOutput.ApkStream,
            new AndroidRuntimeServices(factory, logs, traceCapacity: 4096, clock: new WindowsAndroidClock(), capabilityPolicy: new AndroidCapabilityPolicy(options.Grants), clipboard: clipboard, connectivity: connectivity, power: new WindowsPowerAdapter(), viewBridgeFactory: ViewRuntimeAndroidViewBridge.TryCreate, capabilityAudit: capabilityAudit, audioRecorder: audioRecorder));
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
