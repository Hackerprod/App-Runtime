using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Apk;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

public sealed class AndroidSystemServiceTests
{
    private const string Owner = "Lorg/example/runtimeprobe/ServicesProbe;";

    [Fact]
    public void Real_services_probe_catches_default_denials_and_unknown_is_null()
    {
        var harness = Harness("ServicesProbe.apk", AndroidCapabilityPolicy.DenyAll, new FakeClipboard(), new FakeConnectivity(Snapshot()));
        Assert.Equal(1, harness.Invoke("deniedClipboard", "(Landroid/content/Context;)I"));
        Assert.Equal(2, harness.Invoke("deniedConnectivity", "(Landroid/content/Context;)I"));
        Assert.Equal(3, harness.Invoke("unknown", "(Landroid/content/Context;)I"));
        Assert.Equal(21, Harness("ServicesProbe.apk", new AndroidCapabilityPolicy([AndroidCapability.ClipboardRead]), new FakeClipboard(), new UnavailableAndroidConnectivity()).Invoke("deniedWrite", "(Landroid/content/Context;)I"));
        Assert.Equal(22, Harness("ServicesProbe.apk", new AndroidCapabilityPolicy([AndroidCapability.ClipboardWrite]), new FakeClipboard(), new UnavailableAndroidConnectivity()).Invoke("deniedRead", "(Landroid/content/Context;)I"));
    }

    [Fact]
    public void Clipboard_roundtrip_focus_clear_stability_and_session_isolation_are_bounded()
    {
        var grants = new AndroidCapabilityPolicy([AndroidCapability.ClipboardRead, AndroidCapability.ClipboardWrite]);
        var clipboard = new FakeClipboard();
        var first = Harness("ServicesProbe.apk", grants, clipboard, new UnavailableAndroidConnectivity());
        Assert.Equal(4, first.Invoke("clipboard", "(Landroid/content/Context;)I"));
        Assert.Null(clipboard.Text);
        clipboard.Focused = false;
        Assert.Equal(0, first.Invoke("hasClipboard", "(Landroid/content/Context;)I"));
        var secondClipboard = new FakeClipboard { Text = "other" };
        var second = Harness("ServicesProbe.apk", grants, secondClipboard, new UnavailableAndroidConnectivity());
        Assert.Equal(1, second.Invoke("hasClipboard", "(Landroid/content/Context;)I"));
        Assert.Null(clipboard.Text);
    }

    [Fact]
    public void Connectivity_permission_capabilities_offline_and_stale_tokens_are_exact()
    {
        var grants = new AndroidCapabilityPolicy([AndroidCapability.NetworkState]);
        var online = Harness("ServicesProbe.apk", grants, new UnavailableAndroidClipboard(), new FakeConnectivity(Snapshot()));
        Assert.Equal(15, online.Invoke("connectivity", "(Landroid/content/Context;)I"));
        var offline = Harness("ServicesProbe.apk", grants, new UnavailableAndroidClipboard(), new FakeConnectivity(Snapshot() with { Online = false }));
        Assert.Equal(5, offline.Invoke("connectivity", "(Landroid/content/Context;)I"));
        var stale = new FakeConnectivity(Snapshot()) { SwitchAfterFirst = true };
        Assert.Equal(6, Harness("ServicesProbe.apk", grants, new UnavailableAndroidClipboard(), stale).Invoke("stale", "(Landroid/content/Context;)I"));
        Assert.Equal(2, Harness("ServicesProbeMissingPermission.apk", grants, new UnavailableAndroidClipboard(), new FakeConnectivity(Snapshot())).Invoke("deniedConnectivity", "(Landroid/content/Context;)I"));
    }

    [Fact]
    public void Adapter_absence_returns_null_and_audit_never_contains_clipboard_text()
    {
        var audit = new CapturingAudit();
        var harness = Harness("ServicesProbe.apk", new AndroidCapabilityPolicy([AndroidCapability.ClipboardRead]), new UnavailableAndroidClipboard(), new UnavailableAndroidConnectivity(), audit);
        Assert.Equal(20, harness.Invoke("clipboardUnavailable", "(Landroid/content/Context;)I"));
        Assert.DoesNotContain(audit.Entries, entry => entry.ToString()!.Contains("guest-text", StringComparison.Ordinal));
    }

    [Fact]
    public void Clip_peer_quota_is_configurable_and_non_catchable()
    {
        LoadedApk apk = ApkLoader.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ServicesProbe.apk")); var manifest = AndroidManifestReader.Parse(apk.AndroidManifestXml); DexFile dex = DexReader.Parse(apk.ClassesDexFiles[0]);
        var state = new AndroidFrameworkState("quota", manifest.PackageName, Owner, new ActivityWindowPeers(), declaredPermissions: manifest.UsesPermissions, capabilityPolicy: new AndroidCapabilityPolicy([AndroidCapability.ClipboardRead]), clipboard: new FakeClipboard(), serviceLimits: new AndroidServiceLimits(MaxClipData: 1));
        var interpreter = new DexInterpreter(dex, AndroidApiBindings.CreateBuilder(state, new QuietLog()).Build());
        Assert.Throws<AndroidPeerQuotaExceededException>(() => interpreter.InvokeStaticExact(Owner, "clipQuota", "()I"));
    }

    [Fact]
    public void Declared_permissions_are_defensive_immutable_snapshots()
    {
        var source = new List<string> { "android.permission.ACCESS_NETWORK_STATE" };
        var state = new AndroidFrameworkState("immutable", "pkg", Owner, new ActivityWindowPeers(), declaredPermissions: source);
        source.Clear();
        Assert.Contains("android.permission.ACCESS_NETWORK_STATE", state.DeclaredPermissions);
        Assert.False(state.DeclaredPermissions is ICollection<string> mutable && !mutable.IsReadOnly);
    }

    [Fact]
    public void Service_receivers_and_peers_cannot_cross_session_boundaries()
    {
        var grants = new AndroidCapabilityPolicy([AndroidCapability.ClipboardRead, AndroidCapability.ClipboardWrite, AndroidCapability.NetworkState]);
        var first = DirectHarness("first", grants, new FakeClipboard(), new FakeConnectivity(Snapshot()));
        var second = DirectHarness("second", grants, new FakeClipboard(), new FakeConnectivity(Snapshot()));
        DexObject manager = (DexObject)first.Invoke("Landroid/content/Context;", "getSystemService", "(Ljava/lang/String;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, first.State.ApplicationContext, "clipboard");
        DexObject secondManager = (DexObject)second.Invoke("Landroid/content/Context;", "getSystemService", "(Ljava/lang/String;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, second.State.ApplicationContext, "clipboard");
        DexObject clip = (DexObject)first.Invoke("Landroid/content/ClipData;", "newPlainText", "(Ljava/lang/CharSequence;Ljava/lang/CharSequence;)Landroid/content/ClipData;", AndroidInvokeKind.Static, null!, "secret");

        Assert.Throws<AndroidApiBindingException>(() => second.Invoke("Landroid/content/ClipboardManager;", "setPrimaryClip", "(Landroid/content/ClipData;)V", AndroidInvokeKind.Virtual, manager, clip));
        Assert.Throws<AndroidApiBindingException>(() => first.Invoke("Landroid/content/ClipboardManager;", "setPrimaryClip", "(Landroid/content/ClipData;)V", AndroidInvokeKind.Virtual, secondManager, clip));
        Assert.Throws<AndroidApiBindingException>(() => second.Invoke("Landroid/content/ClipboardManager;", "setPrimaryClip", "(Landroid/content/ClipData;)V", AndroidInvokeKind.Virtual, secondManager, clip));
        Assert.Throws<AndroidApiBindingException>(() => first.Invoke("Landroid/content/Context;", "getSystemService", "(Ljava/lang/String;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, new DexObject("Landroid/content/Context;"), "clipboard"));

        DexObject firstConnectivity = (DexObject)first.Invoke("Landroid/content/Context;", "getSystemService", "(Ljava/lang/String;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, first.State.ApplicationContext, "connectivity");
        DexObject secondConnectivity = (DexObject)second.Invoke("Landroid/content/Context;", "getSystemService", "(Ljava/lang/String;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, second.State.ApplicationContext, "connectivity");
        DexObject network = (DexObject)first.Invoke("Landroid/net/ConnectivityManager;", "getActiveNetwork", "()Landroid/net/Network;", AndroidInvokeKind.Virtual, firstConnectivity);
        Assert.Throws<AndroidApiBindingException>(() => second.Invoke("Landroid/net/ConnectivityManager;", "getNetworkCapabilities", "(Landroid/net/Network;)Landroid/net/NetworkCapabilities;", AndroidInvokeKind.Virtual, secondConnectivity, network));
    }

    [Fact]
    public void Connectivity_receives_the_invocation_cancellation_token()
    {
        using var cancellation = new CancellationTokenSource();
        var connectivity = new FakeConnectivity(Snapshot());
        var direct = DirectHarness("cancel", new AndroidCapabilityPolicy([AndroidCapability.NetworkState]), new FakeClipboard(), connectivity, cancellation.Token);
        DexObject manager = (DexObject)direct.Invoke("Landroid/content/Context;", "getSystemService", "(Ljava/lang/String;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, direct.State.ApplicationContext, "connectivity");
        direct.Invoke("Landroid/net/ConnectivityManager;", "getActiveNetwork", "()Landroid/net/Network;", AndroidInvokeKind.Virtual, manager);
        Assert.Equal(cancellation.Token, connectivity.LastToken);

        var clipboard = new FakeClipboard();
        var clipboardHarness = DirectHarness("clipboard-token", new AndroidCapabilityPolicy([AndroidCapability.ClipboardWrite]), clipboard, new UnavailableAndroidConnectivity(), cancellation.Token);
        DexObject clipboardManager = (DexObject)clipboardHarness.Invoke("Landroid/content/Context;", "getSystemService", "(Ljava/lang/String;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, clipboardHarness.State.ApplicationContext, "clipboard");
        DexObject clip = (DexObject)clipboardHarness.Invoke("Landroid/content/ClipData;", "newPlainText", "(Ljava/lang/CharSequence;Ljava/lang/CharSequence;)Landroid/content/ClipData;", AndroidInvokeKind.Static, null!, "value");
        clipboardHarness.Invoke("Landroid/content/ClipboardManager;", "setPrimaryClip", "(Landroid/content/ClipData;)V", AndroidInvokeKind.Virtual, clipboardManager, clip);
        Assert.Equal(cancellation.Token, clipboard.LastToken);
    }

    private static TestHarness Harness(string fixture, IAndroidCapabilityPolicy policy, IAndroidClipboard clipboard, IAndroidConnectivity connectivity, IAndroidServiceAuditSink? audit = null)
    {
        LoadedApk apk = ApkLoader.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)); var manifest = AndroidManifestReader.Parse(apk.AndroidManifestXml); DexFile dex = DexReader.Parse(apk.ClassesDexFiles[0]);
        var state = new AndroidFrameworkState("services", manifest.PackageName, Owner, new ActivityWindowPeers(), declaredPermissions: manifest.UsesPermissions, capabilityPolicy: policy, clipboard: clipboard, connectivity: connectivity, serviceAudit: audit);
        var interpreter = new DexInterpreter(dex, AndroidApiBindings.CreateBuilder(state, new QuietLog()).Build()); return new(interpreter, state.ApplicationContext);
    }
    private static DirectServiceHarness DirectHarness(string sessionId, IAndroidCapabilityPolicy policy, IAndroidClipboard clipboard, IAndroidConnectivity connectivity, CancellationToken token = default)
    {
        var state = new AndroidFrameworkState(sessionId, "org.example.runtimeprobe", Owner, new ActivityWindowPeers(), declaredPermissions: ["android.permission.ACCESS_NETWORK_STATE"], capabilityPolicy: policy, clipboard: clipboard, connectivity: connectivity);
        var registry = AndroidApiBindings.CreateBuilder(state, new QuietLog()).Build();
        var session = new AndroidApiSessionContext(sessionId, state.PackageName, Owner, token, () => true);
        return new(state, registry, session);
    }
    private sealed record DirectServiceHarness(AndroidFrameworkState State, AndroidApiRegistry Registry, AndroidApiSessionContext Session)
    {
        internal object Invoke(string owner, string name, string descriptor, AndroidInvokeKind kind, params object[] args)
        {
            var api = new AndroidApiMethodId(owner, name, descriptor);
            return Registry.Invoke(Session, new(Owner + "->test()V", 0, api, api, kind), args);
        }
    }
    private static AndroidConnectivitySnapshot Snapshot() => new(1, DateTimeOffset.UtcNow, Guid.NewGuid(), true, true, false, true, true, true, AndroidNetworkTransport.Wifi);
    private sealed record TestHarness(DexInterpreter Interpreter, DexObject Context) { internal object Invoke(string name, string descriptor) => Interpreter.InvokeStaticExact(Owner, name, descriptor, Context); }
    private sealed class FakeClipboard : IAndroidClipboard { public bool IsAvailable => true; public bool IsReadFocused => Focused; public bool Focused { get; set; } = true; public string? Text { get; set; } public CancellationToken LastToken { get; private set; } public ValueTask SetTextAsync(string text, CancellationToken token) { LastToken = token; token.ThrowIfCancellationRequested(); Text = text; return ValueTask.CompletedTask; } public ValueTask<string?> GetTextAsync(CancellationToken token) { LastToken = token; token.ThrowIfCancellationRequested(); return ValueTask.FromResult(Text); } public ValueTask ClearAsync(CancellationToken token) { LastToken = token; token.ThrowIfCancellationRequested(); Text = null; return ValueTask.CompletedTask; } }
    private sealed class FakeConnectivity(AndroidConnectivitySnapshot snapshot) : IAndroidConnectivity { private int _calls; public bool IsAvailable => true; public bool SwitchAfterFirst { get; set; } public CancellationToken LastToken { get; private set; } public AndroidConnectivitySnapshot GetSnapshot(CancellationToken token) { LastToken = token; token.ThrowIfCancellationRequested(); int call = Interlocked.Increment(ref _calls); return SwitchAfterFirst && call > 1 ? snapshot with { Revision = snapshot.Revision + 1, Token = Guid.NewGuid() } : snapshot; } }
    private sealed class CapturingAudit : IAndroidServiceAuditSink { public List<AndroidServiceAuditEntry> Entries { get; } = []; public void Record(AndroidServiceAuditEntry entry) => Entries.Add(entry); }
    private sealed class QuietLog : IAndroidLogSink { public int Info(AndroidLogEntry entry) => 1; }
}
