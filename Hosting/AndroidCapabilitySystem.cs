#nullable enable
using System.Text;
using System.Text.Json;
using AndroidRuntime.Core.ApiLayer;

namespace AndroidRuntime.Core.Hosting;

/// <summary>Maps <see cref="AndroidCapability"/> values to real Android permission
/// names where a direct equivalent exists. The taxonomy deliberately mirrors
/// Android's own dangerous-permission groups instead of an invented scheme, so
/// the mapping stays obvious and verifiable. Values without a direct permission
/// equivalent (host-only gates such as clipboard or power) return null.</summary>
public static class AndroidCapabilityInfo
{
    public static string? ToAndroidPermission(AndroidCapability capability) => capability switch
    {
        AndroidCapability.NetworkState => "android.permission.ACCESS_NETWORK_STATE",
        AndroidCapability.FileRead => "android.permission.READ_EXTERNAL_STORAGE",
        AndroidCapability.FileWrite => "android.permission.WRITE_EXTERNAL_STORAGE",
        AndroidCapability.BluetoothScan => "android.permission.BLUETOOTH_SCAN",
        AndroidCapability.BluetoothConnect => "android.permission.BLUETOOTH_CONNECT",
        AndroidCapability.Camera => "android.permission.CAMERA",
        AndroidCapability.NetworkConnect => "android.permission.INTERNET",
        AndroidCapability.LocationCoarse => "android.permission.ACCESS_COARSE_LOCATION",
        AndroidCapability.LocationFine => "android.permission.ACCESS_FINE_LOCATION",
        AndroidCapability.Microphone => "android.permission.RECORD_AUDIO",
        _ => null
    };
}

/// <summary>One structured capability-attempt record, produced for EVERY
/// <see cref="IAndroidCapabilityPolicy.IsAllowed(AndroidCapabilityRequest)"/>
/// call (allowed or denied). Written/flushed independently of whether the
/// subsequent guest exception is caught, so a crash immediately after a denial
/// cannot lose the record — the exact failure mode of the invocation-level
/// trace file on an uncaught guest exception.</summary>
public sealed record AndroidCapabilityAuditEntry(
    long TimestampMillis,
    string SessionId,
    string PackageName,
    AndroidCapability Capability,
    string Operation,
    bool Allowed);

public interface IAndroidCapabilityAuditSink
{
    void Record(AndroidCapabilityAuditEntry entry);
}

public sealed class NullAndroidCapabilityAuditSink : IAndroidCapabilityAuditSink
{
    public void Record(AndroidCapabilityAuditEntry entry) { }
}

/// <summary>Append-only, per-record-flushed audit sink. Every record is written
/// and flushed to disk synchronously (FileStream.Flush(flushToDisk: true)) so
/// the record survives an immediate process crash without any finalization
/// path. Non-authoritative and non-throwing, same contract as trace sinks: a
/// failing audit file never breaks the guest run.</summary>
public sealed class FileAndroidCapabilityAuditSink : IAndroidCapabilityAuditSink, IDisposable
{
    private readonly object _gate = new();
    private readonly FileStream _stream;
    private readonly StreamWriter _writer;
    private bool _disposed;

    public FileAndroidCapabilityAuditSink(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        // FileShare.ReadWrite: the file stays inspectable (readable) while the
        // process is alive — including mid-crash — which is the entire point of
        // this sink. A reader opening with FileShare.Read alone would fail
        // against an open write handle.
        _stream = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(_stream, new UTF8Encoding(false));
    }

    public void Record(AndroidCapabilityAuditEntry entry)
    {
        try
        {
            lock (_gate)
            {
                if (_disposed) return;
                _writer.WriteLine(JsonSerializer.Serialize(new
                {
                    timestampMillis = entry.TimestampMillis,
                    sessionId = entry.SessionId,
                    packageName = entry.PackageName,
                    capability = entry.Capability.ToString(),
                    operation = entry.Operation,
                    allowed = entry.Allowed
                }));
                _writer.Flush();
                _stream.Flush(flushToDisk: true);
            }
        }
        catch
        {
            // Non-authoritative: an audit failure must never break the guest run.
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            try { _writer.Dispose(); } catch { }
        }
    }
}

/// <summary>Pairs a set of <see cref="AndroidCapability"/> values with the same
/// registration function shape every existing *Bindings.cs file uses
/// (<c>Register(AndroidApiRegistryBuilder, AndroidFrameworkState)</c>). Each
/// domain (Files, Bluetooth, Camera, Network I/O, Location, Microphone, plus
/// the existing clipboard/network-state/power) is an independently toggleable
/// module: a capability grant via the host policy enables it.</summary>
public interface IAndroidCapabilityModule
{
    string Name { get; }
    IReadOnlyCollection<AndroidCapability> Capabilities { get; }

    /// <summary>False (default) = call-time gating: bindings register
    /// unconditionally and each binding calls the capability gate before doing
    /// real work — the pattern every existing binding in this codebase uses
    /// (register always, deny at call time with a catchable guest
    /// SecurityException). True = registration-time gating: Register is skipped
    /// entirely unless at least one capability could be granted. Existing
    /// domains must stay call-time gated so a denied capability still produces
    /// its SecurityException instead of a NotImplemented boundary.</summary>
    bool GateRegistration { get; }

    bool IsEnabled(IAndroidCapabilityPolicy policy);
    void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state);
}

public abstract class AndroidCapabilityModule : IAndroidCapabilityModule
{
    protected AndroidCapabilityModule(string name, IReadOnlyCollection<AndroidCapability> capabilities, bool gateRegistration = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (capabilities.Count == 0)
            throw new ArgumentException("A capability module must declare at least one capability.", nameof(capabilities));
        Name = name;
        Capabilities = Array.AsReadOnly(capabilities.Distinct().ToArray());
        GateRegistration = gateRegistration;
    }

    public string Name { get; }
    public IReadOnlyCollection<AndroidCapability> Capabilities { get; }
    public bool GateRegistration { get; }

    public bool IsEnabled(IAndroidCapabilityPolicy policy) =>
        policy is not null && Capabilities.Any(capability => policy.IsAllowed(new(string.Empty, string.Empty, capability, "module-enabled")));

    /// <summary>Default no-op: this unit ships the capability/audit
    /// infrastructure only. Real API bindings for each domain are separate,
    /// probe-first units that override this and register only their own
    /// bindings here (honoring <see cref="GateRegistration"/>).</summary>
    public virtual void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state) { }
}

/// <summary>Built-in module registry covering the full capability taxonomy.
/// <see cref="RegisterAll"/> is invoked at the end of
/// <c>AndroidApiBindings.CreateBuilder</c> so every real registry build
/// exercises the module seam; gated modules skip registration entirely.</summary>
public static class AndroidCapabilityModules
{
    public static IAndroidCapabilityModule Clipboard { get; } = new DeclarativeModule("clipboard", [AndroidCapability.ClipboardRead, AndroidCapability.ClipboardWrite]);
    public static IAndroidCapabilityModule NetworkState { get; } = new DeclarativeModule("network-state", [AndroidCapability.NetworkState]);
    public static IAndroidCapabilityModule Power { get; } = new DeclarativeModule("power", [AndroidCapability.PowerRead]);
    public static IAndroidCapabilityModule Files { get; } = new DeclarativeModule("files", [AndroidCapability.FileRead, AndroidCapability.FileWrite]);
    public static IAndroidCapabilityModule Bluetooth { get; } = new DeclarativeModule("bluetooth", [AndroidCapability.BluetoothScan, AndroidCapability.BluetoothConnect]);
    public static IAndroidCapabilityModule Camera { get; } = new DeclarativeModule("camera", [AndroidCapability.Camera]);
    public static IAndroidCapabilityModule NetworkIo { get; } = new DeclarativeModule("network-io", [AndroidCapability.NetworkConnect]);
    public static IAndroidCapabilityModule Location { get; } = new DeclarativeModule("location", [AndroidCapability.LocationCoarse, AndroidCapability.LocationFine]);
    public static IAndroidCapabilityModule Microphone { get; } = new DeclarativeModule("microphone", [AndroidCapability.Microphone]);

    public static IReadOnlyList<IAndroidCapabilityModule> All { get; } =
        [Clipboard, NetworkState, Power, Files, Bluetooth, Camera, NetworkIo, Location, Microphone];

    public static IAndroidCapabilityModule ModuleFor(AndroidCapability capability) =>
        All.FirstOrDefault(module => module.Capabilities.Contains(capability))
        ?? throw new ArgumentException("No capability module declares " + capability + ".", nameof(capability));

    public static void RegisterAll(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(state);
        foreach (IAndroidCapabilityModule module in All)
            RegisterModule(builder, state, module);
    }

    public static void RegisterModule(AndroidApiRegistryBuilder builder, AndroidFrameworkState state, IAndroidCapabilityModule module)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(module);
        if (module.GateRegistration && !module.IsEnabled(state.CapabilityPolicy))
            return;
        module.Register(builder, state);
    }

    /// <summary>Concrete module for domains that only declare their capability
    /// set (the call-time-gated pattern) and have no bindings yet — this unit
    /// ships capability/audit infrastructure only; real API bindings are future
    /// probe-first units that override Register.</summary>
    private sealed class DeclarativeModule(string name, AndroidCapability[] capabilities)
        : AndroidCapabilityModule(name, capabilities);
}
