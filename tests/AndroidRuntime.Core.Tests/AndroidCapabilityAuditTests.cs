using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Apk;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

public sealed class AndroidCapabilityAuditTests
{
    private const string Owner = "Lorg/example/runtimeprobe/ServicesProbe;";

    [Fact]
    public void Funnel_records_allowed_and_denied_attempts_with_exact_fields()
    {
        var audit = new CapturingAudit();
        using var state = new AndroidFrameworkState("audit-session", "org.example", Owner, new ActivityWindowPeers(),
            capabilityPolicy: new AndroidCapabilityPolicy([AndroidCapability.NetworkState]), capabilityAudit: audit);

        Assert.True(state.IsCapabilityAllowed(new("audit-session", "org.example", AndroidCapability.NetworkState, "lookup")));
        Assert.False(state.IsCapabilityAllowed(new("audit-session", "org.example", AndroidCapability.PowerRead, "getIntProperty")));

        Assert.Equal(2, audit.Entries.Count);
        AndroidCapabilityAuditEntry allowed = audit.Entries[0];
        Assert.Equal("audit-session", allowed.SessionId);
        Assert.Equal("org.example", allowed.PackageName);
        Assert.Equal(AndroidCapability.NetworkState, allowed.Capability);
        Assert.Equal("lookup", allowed.Operation);
        Assert.True(allowed.Allowed);
        Assert.True(allowed.TimestampMillis > 0);
        AndroidCapabilityAuditEntry denied = audit.Entries[1];
        Assert.Equal(AndroidCapability.PowerRead, denied.Capability);
        Assert.Equal("getIntProperty", denied.Operation);
        Assert.False(denied.Allowed);
    }

    [Fact]
    public void Real_guest_denial_records_the_capability_attempt_before_the_exception()
    {
        var audit = new CapturingAudit();
        var harness = Harness("ServicesProbe.apk", AndroidCapabilityPolicy.DenyAll, audit);

        // deniedConnectivity catches the guest SecurityException and returns 2;
        // the attempted NetworkState lookup must still be in the audit trail.
        Assert.Equal(2, harness.Invoke("deniedConnectivity", "(Landroid/content/Context;)I"));
        Assert.Contains(audit.Entries, entry => entry.Capability == AndroidCapability.NetworkState && !entry.Allowed && entry.Operation == "lookup");
    }

    [Fact]
    public void Allowed_guest_path_records_allowed_attempts()
    {
        var audit = new CapturingAudit();
        var harness = Harness("ServicesProbe.apk", new AndroidCapabilityPolicy([AndroidCapability.NetworkState]), audit);

        Assert.Equal(15, harness.Invoke("connectivity", "(Landroid/content/Context;)I"));
        Assert.Contains(audit.Entries, entry => entry.Capability == AndroidCapability.NetworkState && entry.Allowed);
    }

    [Theory]
    [InlineData(AndroidCapability.FileRead, "android.permission.READ_EXTERNAL_STORAGE")]
    [InlineData(AndroidCapability.FileWrite, "android.permission.WRITE_EXTERNAL_STORAGE")]
    [InlineData(AndroidCapability.BluetoothScan, "android.permission.BLUETOOTH_SCAN")]
    [InlineData(AndroidCapability.BluetoothConnect, "android.permission.BLUETOOTH_CONNECT")]
    [InlineData(AndroidCapability.Camera, "android.permission.CAMERA")]
    [InlineData(AndroidCapability.NetworkConnect, "android.permission.INTERNET")]
    [InlineData(AndroidCapability.LocationCoarse, "android.permission.ACCESS_COARSE_LOCATION")]
    [InlineData(AndroidCapability.LocationFine, "android.permission.ACCESS_FINE_LOCATION")]
    [InlineData(AndroidCapability.Microphone, "android.permission.RECORD_AUDIO")]
    [InlineData(AndroidCapability.NetworkState, "android.permission.ACCESS_NETWORK_STATE")]
    public void Taxonomy_maps_capabilities_to_real_android_permissions(AndroidCapability capability, string permission)
        => Assert.Equal(permission, AndroidCapabilityInfo.ToAndroidPermission(capability));

    [Theory]
    [InlineData(AndroidCapability.ClipboardRead)]
    [InlineData(AndroidCapability.ClipboardWrite)]
    [InlineData(AndroidCapability.PowerRead)]
    public void Host_only_capabilities_have_no_direct_permission(AndroidCapability capability)
        => Assert.Null(AndroidCapabilityInfo.ToAndroidPermission(capability));

    [Theory]
    [InlineData("android.permission.BLUETOOTH_CONNECT", AndroidCapability.BluetoothConnect)]
    [InlineData("android.permission.BLUETOOTH_SCAN", AndroidCapability.BluetoothScan)]
    [InlineData("android.permission.RECORD_AUDIO", AndroidCapability.Microphone)]
    [InlineData("android.permission.CAMERA", AndroidCapability.Camera)]
    [InlineData("android.permission.INTERNET", AndroidCapability.NetworkConnect)]
    [InlineData("android.permission.ACCESS_FINE_LOCATION", AndroidCapability.LocationFine)]
    [InlineData("android.permission.READ_EXTERNAL_STORAGE", AndroidCapability.FileRead)]
    public void Permission_strings_map_to_their_capabilities(string permission, AndroidCapability expected)
    {
        Assert.True(AndroidCapabilityInfo.TryFromAndroidPermission(permission, out AndroidCapability actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("android.permission.UNKNOWN_THING")]
    [InlineData("")]
    [InlineData("not.a.permission")]
    public void Unknown_permission_strings_do_not_map(string permission)
        => Assert.False(AndroidCapabilityInfo.TryFromAndroidPermission(permission, out _));

    [Fact]
    public void Check_self_permission_is_a_query_never_throws_and_audits()
    {
        var audit = new CapturingAudit();
        using var state = new AndroidFrameworkState("audit", "org.example", Owner, new ActivityWindowPeers(),
            capabilityPolicy: new AndroidCapabilityPolicy([AndroidCapability.BluetoothConnect, AndroidCapability.Microphone]),
            capabilityAudit: audit);

        // Granted when the mapped capability is allowed by the policy.
        Assert.Equal(0, state.CheckSelfPermission("android.permission.BLUETOOTH_CONNECT"));
        Assert.Equal(0, state.CheckSelfPermission("android.permission.RECORD_AUDIO"));
        // Denied when the capability is not granted.
        Assert.Equal(-1, state.CheckSelfPermission("android.permission.BLUETOOTH_SCAN"));
        Assert.Equal(-1, state.CheckSelfPermission("android.permission.CAMERA"));
        // Unknown permission: DENIED, never an exception (query contract).
        Assert.Equal(-1, state.CheckSelfPermission("android.permission.DOES_NOT_EXIST"));

        // Every mapped query flowed through the audit funnel; the unmapped
        // permission never reaches the funnel (no capability to record).
        Assert.Contains(audit.Entries, entry => entry.Capability == AndroidCapability.BluetoothConnect && entry.Allowed && entry.Operation == "checkSelfPermission");
        Assert.Contains(audit.Entries, entry => entry.Capability == AndroidCapability.Camera && !entry.Allowed && entry.Operation == "checkSelfPermission");
        Assert.Equal(4, audit.Entries.Count);
    }

    [Fact]
    public void Module_registry_covers_full_taxonomy_exactly_once()
    {
        var capabilities = AndroidCapabilityModules.All.SelectMany(module => module.Capabilities).ToList();
        Assert.Equal(13, capabilities.Count);
        Assert.Equal(capabilities.Count, capabilities.Distinct().Count());
        Assert.Contains(AndroidCapability.ClipboardRead, capabilities);
        Assert.Contains(AndroidCapability.PowerRead, capabilities);
        Assert.Contains(AndroidCapability.FileRead, capabilities);
        Assert.Contains(AndroidCapability.BluetoothConnect, capabilities);
        Assert.Contains(AndroidCapability.Camera, capabilities);
        Assert.Contains(AndroidCapability.NetworkConnect, capabilities);
        Assert.Contains(AndroidCapability.LocationFine, capabilities);
        Assert.Contains(AndroidCapability.Microphone, capabilities);
        foreach (AndroidCapability capability in capabilities)
            Assert.Contains(capability, AndroidCapabilityModules.ModuleFor(capability).Capabilities);
    }

    [Fact]
    public void Modules_are_independently_toggleable_by_policy()
    {
        Assert.True(AndroidCapabilityModules.Files.IsEnabled(new AndroidCapabilityPolicy([AndroidCapability.FileRead])));
        Assert.False(AndroidCapabilityModules.Files.IsEnabled(AndroidCapabilityPolicy.DenyAll));
        Assert.True(AndroidCapabilityModules.Location.IsEnabled(new AndroidCapabilityPolicy([AndroidCapability.LocationFine])));
        Assert.False(AndroidCapabilityModules.Camera.IsEnabled(new AndroidCapabilityPolicy([AndroidCapability.LocationFine])));
    }

    [Fact]
    public void Registration_time_gating_only_skips_gated_modules()
    {
        var state = new AndroidFrameworkState("modules", "org.example", Owner, new ActivityWindowPeers());
        var builder = new AndroidApiRegistryBuilder();
        bool gatedCalled = false;
        var gated = new TestModule("gated", [AndroidCapability.FileRead], gateRegistration: true, () => gatedCalled = true);

        AndroidCapabilityModules.RegisterModule(builder, state, gated);
        Assert.False(gatedCalled); // DenyAll: gated module registration is skipped.

        using var grantedState = new AndroidFrameworkState("modules", "org.example", Owner, new ActivityWindowPeers(),
            capabilityPolicy: new AndroidCapabilityPolicy([AndroidCapability.FileRead]));
        AndroidCapabilityModules.RegisterModule(builder, grantedState, gated);
        Assert.True(gatedCalled);

        bool ungatedCalled = false;
        var ungated = new TestModule("ungated", [AndroidCapability.Camera], gateRegistration: false, () => ungatedCalled = true);
        AndroidCapabilityModules.RegisterModule(builder, state, ungated);
        Assert.True(ungatedCalled); // Call-time gated modules always register (deny surfaces as SecurityException, not NotImplemented).
    }

    [Fact]
    public void File_sink_flushes_each_record_immediately_and_survives_without_dispose()
    {
        string directory = Path.Combine(Path.GetTempPath(), "capability-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "audit.jsonl");
        string afterSecond;
        try
        {
            using (var sink = new FileAndroidCapabilityAuditSink(path))
            {
                sink.Record(new(1_000, "s1", "org.example", AndroidCapability.PowerRead, "lookup", Allowed: false));

                // No Dispose and no explicit close: the record must already be on
                // disk — this is the crash-survival property (the trace file only
                // gets written after a clean close and ends up 0 bytes on a crash).
                // A live reader must share writes (like a tail-following tool);
                // File.ReadAllText itself refuses to open alongside a writer.
                Assert.True(File.Exists(path));
                string first = ReadWhileOpen(path);
                Assert.Contains("PowerRead", first);
                Assert.Contains("\"allowed\":false", first);
                Assert.Contains("\"operation\":\"lookup\"", first);

                sink.Record(new(2_000, "s1", "org.example", AndroidCapability.NetworkState, "lookup", Allowed: true));
                afterSecond = ReadWhileOpen(path);
                Assert.Equal(2, afterSecond.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
            }
            Assert.Equal(afterSecond, File.ReadAllText(path));
        }
        finally
        {
            TryDeleteFile(path);
            TryDeleteDirectory(directory);
        }
    }

    private static void TryDeleteFile(string path)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try { if (File.Exists(path)) File.Delete(path); return; }
            catch (IOException) { Thread.Sleep(50); }
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try { Directory.Delete(directory, recursive: true); return; }
            catch (IOException) { Thread.Sleep(50); }
        }
    }

    private static string ReadWhileOpen(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false), detectEncodingFromByteOrderMarks: false);
        return reader.ReadToEnd();
    }

    private static TestHarness Harness(string fixture, IAndroidCapabilityPolicy policy, IAndroidCapabilityAuditSink audit)
    {
        LoadedApk apk = ApkLoader.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        var manifest = AndroidManifestReader.Parse(apk.AndroidManifestXml);
        DexFile dex = DexReader.Parse(apk.ClassesDexFiles[0]);
        var state = new AndroidFrameworkState("audit", manifest.PackageName, Owner, new ActivityWindowPeers(),
            declaredPermissions: manifest.UsesPermissions, capabilityPolicy: policy, clipboard: new FakeClipboard(), connectivity: new FakeConnectivity(Snapshot()), capabilityAudit: audit);
        var interpreter = new DexInterpreter(dex, AndroidApiBindings.CreateBuilder(state, new QuietLog()).Build());
        return new(interpreter, state.ApplicationContext);
    }

    private sealed record TestHarness(DexInterpreter Interpreter, DexObject Context)
    {
        internal object Invoke(string name, string descriptor) => Interpreter.InvokeStaticExact(Owner, name, descriptor, Context);
    }

    private sealed class CapturingAudit : IAndroidCapabilityAuditSink
    {
        public List<AndroidCapabilityAuditEntry> Entries { get; } = [];
        public void Record(AndroidCapabilityAuditEntry entry) => Entries.Add(entry);
    }

    private sealed class FakeClipboard : IAndroidClipboard
    {
        public bool IsAvailable => true;
        public bool IsReadFocused => true;
        public ValueTask SetTextAsync(string text, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<string?> GetTextAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);
        public ValueTask ClearAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class FakeConnectivity : IAndroidConnectivity
    {
        private readonly AndroidConnectivitySnapshot _snapshot;
        internal FakeConnectivity(AndroidConnectivitySnapshot snapshot) => _snapshot = snapshot;
        public bool IsAvailable => true;
        public AndroidConnectivitySnapshot GetSnapshot(CancellationToken cancellationToken) => _snapshot;
    }

    private static AndroidConnectivitySnapshot Snapshot() => new(1, DateTimeOffset.UtcNow, Guid.NewGuid(), true, true, false, true, true, true, AndroidNetworkTransport.Wifi);

    private sealed class TestModule(string name, AndroidCapability[] capabilities, bool gateRegistration, Action onRegister)
        : AndroidCapabilityModule(name, capabilities, gateRegistration)
    {
        public override void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state) => onRegister();
    }

    private sealed class QuietLog : IAndroidLogSink { public int Info(AndroidLogEntry entry) => 1; }
}
