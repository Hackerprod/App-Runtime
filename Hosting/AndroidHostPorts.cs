#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Apk;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Ui;

namespace AndroidRuntime.Core.Hosting;

public interface IActivityWindow : IDisposable
{
    event EventHandler? Closed;
    nint Handle { get; }
    string Title { get; }
    bool IsClosed { get; }
    void SetTitle(string? title, CancellationToken cancellationToken);
    void Show(CancellationToken cancellationToken);
    void Close();
}

public interface IDeferredActivityWindowClose
{
    event EventHandler? CloseRequested;
}

/// <summary>Optional native surface port. Attach only installs asynchronous lane requests.</summary>
public interface IAndroidUiSurfaceHost
{
    void Attach(AndroidHostedActivitySession session);
    void Detach(AndroidHostedActivitySession session);
}

public interface IActivityWindowFactory
{
    IActivityWindow Create(
        string sessionId,
        string packageName,
        string activityDescriptor,
        CancellationToken cancellationToken);
}

public interface IAndroidLogSink
{
    int Info(AndroidLogEntry entry);
}

public interface IAndroidClock
{
    long UptimeMillis();
    long ElapsedRealtime();
    long ElapsedRealtimeNanos();
}

/// <summary>Wall-clock port: "what time is it right now" as epoch milliseconds
/// (UTC). Deliberately separate from <see cref="IAndroidClock"/>, which is
/// monotonic/uptime-only and cannot answer current-time questions. Real Android
/// answers this via System.currentTimeMillis()/new Date(); this port gives the
/// host a testable seam (inject a fixed value in tests, never assert against
/// real "now").</summary>
public interface IAndroidWallClock
{
    long NowMillis();
}

/// <summary>Default wall-clock implementation: real UTC epoch milliseconds via
/// the CLR clock. Honest default — wall time genuinely is "now"; the port
/// exists for host/test injection, not to hide the real time.</summary>
public sealed class UtcAndroidWallClock : IAndroidWallClock
{
    public long NowMillis() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

/// <summary>Capability taxonomy mirroring Android's real permission groups.
/// The first four values are the original host gates (extend, never rename);
/// the rest are the modular taxonomy added by the modular capability system
/// (Files, Bluetooth, Camera, real Network I/O, Location, Microphone). Map to
/// real Android permission names via <see cref="AndroidCapabilityInfo"/>.</summary>
public enum AndroidCapability
{
    ClipboardRead,
    ClipboardWrite,
    NetworkState,
    PowerRead,
    FileRead,
    FileWrite,
    BluetoothScan,
    BluetoothConnect,
    Camera,
    NetworkConnect,
    LocationCoarse,
    LocationFine,
    Microphone
}
public sealed record AndroidCapabilityRequest(string SessionId, string PackageName, AndroidCapability Capability, string Operation);
public interface IAndroidCapabilityPolicy { bool IsAllowed(AndroidCapabilityRequest request); }
public sealed class AndroidCapabilityPolicy : IAndroidCapabilityPolicy
{
    private readonly HashSet<AndroidCapability> _grants;
    public AndroidCapabilityPolicy(IEnumerable<AndroidCapability>? grants = null) => _grants = grants?.ToHashSet() ?? [];
    public bool IsAllowed(AndroidCapabilityRequest request) => _grants.Contains(request.Capability);
    public static AndroidCapabilityPolicy DenyAll { get; } = new();
}
public sealed record AndroidServiceAuditEntry(string SessionId, string PackageName, string Service, string Operation, bool Allowed, int Size, long DurationMilliseconds, string? ErrorType = null);
public interface IAndroidServiceAuditSink { void Record(AndroidServiceAuditEntry entry); }
public sealed class NullAndroidServiceAuditSink : IAndroidServiceAuditSink { public void Record(AndroidServiceAuditEntry entry) { } }

public interface IAndroidClipboard
{
    bool IsAvailable { get; }
    bool IsReadFocused { get; }
    ValueTask SetTextAsync(string text, CancellationToken cancellationToken);
    ValueTask<string?> GetTextAsync(CancellationToken cancellationToken);
    ValueTask ClearAsync(CancellationToken cancellationToken);
}

[Flags] public enum AndroidNetworkTransport { None = 0, Cellular = 1, Wifi = 2, Ethernet = 4, Vpn = 8 }
public sealed record AndroidConnectivitySnapshot(long Revision, DateTimeOffset Timestamp, Guid Token, bool Online, bool Validated, bool? Metered, bool NotRestricted, bool NotVpn, bool NotRoaming, AndroidNetworkTransport Transports);
public interface IAndroidConnectivity
{
    bool IsAvailable { get; }
    AndroidConnectivitySnapshot GetSnapshot(CancellationToken cancellationToken);
}

public sealed record HostPowerSnapshot(
    bool HasBattery,
    bool? IsCharging,
    int? CapacityPercent,
    long? EnergyCounterNWh,
    int? ChargeCounterUAh,
    int? CurrentNowUa,
    int? CurrentAverageUa,
    int? Status,
    bool PowerSaveMode);

public interface IAndroidPower
{
    bool IsAvailable { get; }
    HostPowerSnapshot GetSnapshot(CancellationToken cancellationToken);
}

public sealed class UnavailableAndroidPower : IAndroidPower
{
    public bool IsAvailable => false;
    public HostPowerSnapshot GetSnapshot(CancellationToken cancellationToken) => throw new InvalidOperationException("Power adapter is unavailable.");
}

public sealed class UnavailableAndroidClipboard : IAndroidClipboard
{
    public bool IsAvailable => false; public bool IsReadFocused => false;
    public ValueTask SetTextAsync(string text, CancellationToken cancellationToken) => ValueTask.FromException(new InvalidOperationException("Clipboard adapter is unavailable."));
    public ValueTask<string?> GetTextAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);
    public ValueTask ClearAsync(CancellationToken cancellationToken) => ValueTask.FromException(new InvalidOperationException("Clipboard adapter is unavailable."));
}
public sealed class UnavailableAndroidConnectivity : IAndroidConnectivity
{
    public bool IsAvailable => false;
    public AndroidConnectivitySnapshot GetSnapshot(CancellationToken cancellationToken) => throw new InvalidOperationException("Connectivity adapter is unavailable.");
}

/// <summary>App-private file sandbox port (real Android app-specific
/// directories): getCacheDir / getExternalFilesDir return File objects pointing
/// INSIDE this sandbox — never arbitrary filesystem access. Scoped-storage
/// semantics: app-specific directories need NO runtime permission, so these
/// bindings are deliberately ungated (the FileRead/FileWrite capabilities gate
/// shared-storage access, a separate future surface).</summary>
public interface IAndroidFileSandbox
{
    string GetCacheDirectory(string packageName);
    string GetFilesDirectory(string packageName, string? type);
}

/// <summary>Real audio capture port (MediaRecorder backing): captures the host
/// microphone and encodes to AAC in an MP4 container at the requested sample
/// rate/bitrate — the project owner's approved scope (real apps like WhatsApp
/// need real audio, an empty stub is not acceptable). The host provides the
/// implementation; Core never fabricates audio. The Microphone capability gates
/// start() (real Android checks RECORD_AUDIO at start, not construction).</summary>
public interface IAndroidAudioRecorder
{
    /// <summary>Starts real microphone capture into outputPath (AAC/MP4).
    /// Throws when the microphone is unavailable or encoding cannot start.</summary>
    void Start(string outputPath, int sampleRate, int bitRate);

    /// <summary>Stops capture and finalizes the output file. No-op when not
    /// recording.</summary>
    void Stop();
}

/// <summary>Fail-closed default: no audio backend attached — start throws
/// (never a fabricated silent file).</summary>
public sealed class UnavailableAndroidAudioRecorder : IAndroidAudioRecorder
{
    public void Start(string outputPath, int sampleRate, int bitRate) =>
        throw new InvalidOperationException("Audio recorder backend is unavailable.");
    public void Stop() { }
}

/// <summary>Default sandbox: %LOCALAPPDATA%\AndroidRuntime\&lt;package&gt;\cache
/// (Context.getCacheDir) and ...\files[/&lt;type&gt;] (Context.getExternalFilesDir),
/// mirroring Android/data/&lt;package&gt;/... layout. Directories are created on
/// demand (real Android pre-creates them).</summary>
public sealed class LocalAppDataFileSandbox : IAndroidFileSandbox
{
    private readonly string _root;

    public LocalAppDataFileSandbox(string? rootOverride = null)
    {
        _root = rootOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AndroidRuntime");
    }

    public string GetCacheDirectory(string packageName) => EnsureDirectory(Path.Combine(_root, packageName, "cache"));

    public string GetFilesDirectory(string packageName, string? type) =>
        string.IsNullOrWhiteSpace(type)
            ? EnsureDirectory(Path.Combine(_root, packageName, "files"))
            : EnsureDirectory(Path.Combine(_root, packageName, "files", type));

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
public sealed record AndroidServiceLimits(int MaxClipData = 64, int MaxTextLength = 65_536, int MaxOperations = 512)
{
    public void Validate() { if (MaxClipData <= 0 || MaxTextLength <= 0 || MaxOperations <= 0) throw new ArgumentOutOfRangeException(nameof(AndroidServiceLimits)); }
    public static AndroidServiceLimits Default { get; } = new();
}

public sealed class StopwatchAndroidClock : IAndroidClock
{
    private readonly long _start = Stopwatch.GetTimestamp();
    private long ElapsedNanos() => checked((long)(((Stopwatch.GetTimestamp() - _start) * 1_000_000_000d) / Stopwatch.Frequency));
    public long UptimeMillis() => ElapsedNanos() / 1_000_000;
    public long ElapsedRealtime() => ElapsedNanos() / 1_000_000;
    public long ElapsedRealtimeNanos() => ElapsedNanos();
}

public sealed record AndroidLogEntry(
    string SessionId,
    string PackageName,
    string ActivityDescriptor,
    string? Tag,
    string Message,
    AndroidApiInvocation Invocation,
    int Priority = 4,
    string Level = "I");

public sealed class ActivityWindowPeers
{
    private readonly ConcurrentDictionary<DexObject, IActivityWindow> _windows =
        new(ReferenceEqualityComparer.Instance);

    public void Associate(DexObject activity, IActivityWindow window)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(window);
        if (!_windows.TryAdd(activity, window))
            throw new InvalidOperationException("Activity already has an associated window peer: " + activity.TypeDescriptor);
    }

    public bool TryGet(DexObject activity, out IActivityWindow window) =>
        _windows.TryGetValue(activity, out window!);

    public void Remove(DexObject activity) => _windows.TryRemove(activity, out _);
}

public sealed class AndroidRuntimeServices
{
    public AndroidRuntimeServices(
        IActivityWindowFactory windowFactory,
        IAndroidLogSink logSink,
        int traceCapacity = 1024,
        IAndroidApiTraceSink? additionalTraceSink = null,
        int minimumLogPriority = 2,
        AndroidPeerLimits? peerLimits = null,
        IAndroidClock? clock = null,
        IAndroidWallClock? wallClock = null,
        IAndroidCapabilityPolicy? capabilityPolicy = null,
        IAndroidClipboard? clipboard = null,
        IAndroidConnectivity? connectivity = null,
        IAndroidServiceAuditSink? serviceAudit = null,
        AndroidServiceLimits? serviceLimits = null,
        IAndroidPower? power = null,
        Func<AndroidResourceResolver, AndroidResourceQueryService, int, IAndroidViewBridge?>? viewBridgeFactory = null,
        IAndroidCapabilityAuditSink? capabilityAudit = null,
        IAndroidFileSandbox? fileSandbox = null,
        IAndroidAudioRecorder? audioRecorder = null)
    {
        WindowFactory = windowFactory ?? throw new ArgumentNullException(nameof(windowFactory));
        LogSink = logSink ?? throw new ArgumentNullException(nameof(logSink));
        if (traceCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(traceCapacity));
        TraceCapacity = traceCapacity;
        AdditionalTraceSink = additionalTraceSink;
        if (minimumLogPriority is < 2 or > 7)
            throw new ArgumentOutOfRangeException(nameof(minimumLogPriority));
        MinimumLogPriority = minimumLogPriority;
        PeerLimits = peerLimits ?? AndroidPeerLimits.Default;
        PeerLimits.Validate();
        Clock = clock ?? new StopwatchAndroidClock();
        WallClock = wallClock ?? new UtcAndroidWallClock();
        CapabilityPolicy = capabilityPolicy ?? AndroidCapabilityPolicy.DenyAll;
        Clipboard = clipboard ?? new UnavailableAndroidClipboard();
        Connectivity = connectivity ?? new UnavailableAndroidConnectivity();
        ServiceAudit = serviceAudit ?? new NullAndroidServiceAuditSink();
        ServiceLimits = serviceLimits ?? AndroidServiceLimits.Default; ServiceLimits.Validate();
        Power = power ?? new UnavailableAndroidPower();
        ViewBridgeFactory = viewBridgeFactory;
        CapabilityAudit = capabilityAudit ?? new NullAndroidCapabilityAuditSink();
        FileSandbox = fileSandbox ?? new LocalAppDataFileSandbox();
        AudioRecorder = audioRecorder ?? new UnavailableAndroidAudioRecorder();
    }

    public IActivityWindowFactory WindowFactory { get; }
    public IAndroidLogSink LogSink { get; }
    public int TraceCapacity { get; }
    public IAndroidApiTraceSink? AdditionalTraceSink { get; }
    public int MinimumLogPriority { get; }
    public AndroidPeerLimits PeerLimits { get; }
    public IAndroidClock Clock { get; }
    public IAndroidWallClock WallClock { get; }
    public IAndroidCapabilityPolicy CapabilityPolicy { get; }
    public IAndroidClipboard Clipboard { get; }
    public IAndroidConnectivity Connectivity { get; }
    public IAndroidServiceAuditSink ServiceAudit { get; }
    public AndroidServiceLimits ServiceLimits { get; }
    public IAndroidPower Power { get; }
    /// <summary>Phase-2 view bridge factory provided by the host (ViewRuntime-
    /// backed). Called with the per-session resolver + resource-query service +
    /// the manifest's application theme style id once the APK is loaded; null
    /// means the framework state falls back to the unavailable bridge (no
    /// local visual behavior).</summary>
    public Func<AndroidResourceResolver, AndroidResourceQueryService, int, IAndroidViewBridge?>? ViewBridgeFactory { get; }

    /// <summary>Structured capability-attempt audit sink. Records one entry per
    /// <see cref="IAndroidCapabilityPolicy.IsAllowed(AndroidCapabilityRequest)"/>
    /// call (allowed or denied) via the framework-state funnel. A file-backed
    /// sink (e.g. <see cref="FileAndroidCapabilityAuditSink"/>) survives a crash
    /// immediately after a denial — the trace file does not.</summary>
    public IAndroidCapabilityAuditSink CapabilityAudit { get; }

    /// <summary>App-private file sandbox backing Context.getCacheDir /
    /// getExternalFilesDir (scoped-storage semantics: ungated).</summary>
    public IAndroidFileSandbox FileSandbox { get; }

    /// <summary>Real audio capture backing MediaRecorder (AAC/MP4 from the host
    /// microphone). Defaults to the fail-closed unavailable backend.</summary>
    public IAndroidAudioRecorder AudioRecorder { get; }

    public static AndroidRuntimeServices CreateHeadless() =>
        new(new InMemoryActivityWindowFactory(), new ConsoleAndroidLogSink());
}

public sealed class InMemoryActivityWindowFactory : IActivityWindowFactory
{
    public IActivityWindow Create(string sessionId, string packageName, string activityDescriptor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new InMemoryActivityWindow();
    }
}

public sealed class InMemoryActivityWindow : IActivityWindow
{
    private int _closed;
    public event EventHandler? Closed;
    public nint Handle => 0;
    public string Title { get; private set; } = string.Empty;
    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    public void SetTitle(string? title, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsClosed) throw new InvalidOperationException("Window is closed.");
        Title = title ?? string.Empty;
    }

    public void Show(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsClosed) throw new InvalidOperationException("Window is closed.");
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Close();
}

public sealed class ConsoleAndroidLogSink : IAndroidLogSink
{
    public int Info(AndroidLogEntry entry)
    {
        string line = $"[{entry.SessionId}] {entry.Level}/{entry.Tag}: {entry.Message}";
        Console.Error.WriteLine(line);
        return Math.Max(1, line.Length);
    }
}
