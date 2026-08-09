#nullable enable
using System.Diagnostics;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.ApiLayer;

internal sealed class AndroidSystemServiceRegistry : IDisposable
{
    internal const string ClipboardName = "clipboard";
    internal const string ConnectivityName = "connectivity";
    internal const string BatteryName = "batterymanager";
    internal const string PowerName = "power";
    private readonly AndroidFrameworkState _state;
    private readonly Dictionary<DexObject, ClipDataPeer> _clips = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DexObject, ClipItemPeer> _items = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DexObject, NetworkPeer> _networks = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DexObject, AndroidConnectivitySnapshot> _capabilities = new(ReferenceEqualityComparer.Instance);
    private int _operations;
    internal AndroidSystemServiceRegistry(AndroidFrameworkState state)
    {
        _state = state;
        ClipboardManager = new DexObject("Landroid/content/ClipboardManager;");
        ConnectivityManager = new DexObject("Landroid/net/ConnectivityManager;");
        BatteryManager = new DexObject("Landroid/os/BatteryManager;");
        PowerManager = new DexObject("Landroid/os/PowerManager;");
    }
    internal DexObject ClipboardManager { get; }
    internal DexObject ConnectivityManager { get; }
    internal DexObject BatteryManager { get; }
    internal DexObject PowerManager { get; }
    internal DexObject? GetService(string name)
    {
        return name switch
        {
            ClipboardName when !_state.Clipboard.IsAvailable => null,
            ClipboardName => DemandClipboardLookup() ? ClipboardManager : null,
            ConnectivityName when !_state.Connectivity.IsAvailable => null,
            ConnectivityName => DemandNetworkLookup() ? ConnectivityManager : null,
            BatteryName when !_state.Power.IsAvailable => null,
            BatteryName => DemandLookup(AndroidCapability.PowerRead, BatteryName) ? BatteryManager : null,
            PowerName when !_state.Power.IsAvailable => null,
            PowerName => DemandLookup(AndroidCapability.PowerRead, PowerName) ? PowerManager : null,
            _ => null
        };
    }
    private bool DemandNetworkLookup()
    {
        if (!_state.DeclaredPermissions.Contains("android.permission.ACCESS_NETWORK_STATE", StringComparer.Ordinal)) Denied(ConnectivityName, "lookup", AndroidCapability.NetworkState);
        return DemandLookup(AndroidCapability.NetworkState, ConnectivityName);
    }
    private bool DemandClipboardLookup()
    {
        bool allowed = _state.CapabilityPolicy.IsAllowed(new(_state.SessionId, _state.PackageName, AndroidCapability.ClipboardRead, "lookup")) ||
                       _state.CapabilityPolicy.IsAllowed(new(_state.SessionId, _state.PackageName, AndroidCapability.ClipboardWrite, "lookup"));
        if (!allowed) Denied(ClipboardName, "lookup", AndroidCapability.ClipboardRead);
        Audit(ClipboardName, "lookup", true, 0, 0, null); return true;
    }
    private bool DemandLookup(AndroidCapability capability, string service)
    {
        if (!_state.CapabilityPolicy.IsAllowed(new(_state.SessionId, _state.PackageName, capability, "lookup"))) Denied(service, "lookup", capability);
        Audit(service, "lookup", true, 0, 0, null); return true;
    }
    internal void Demand(AndroidCapability capability, string service, string operation)
    {
        if (Interlocked.Increment(ref _operations) > _state.ServiceLimits.MaxOperations) throw new AndroidPeerQuotaExceededException("service operation", _state.ServiceLimits.MaxOperations);
        if (!_state.CapabilityPolicy.IsAllowed(new(_state.SessionId, _state.PackageName, capability, operation))) Denied(service, operation, capability);
    }
    private void Denied(string service, string operation, AndroidCapability capability)
    {
        Audit(service, operation, false, 0, 0, nameof(AndroidApiSecurityException));
        throw new AndroidApiSecurityException($"Capability denied for {service}.{operation}: {capability}.");
    }
    internal void Audit(string service, string operation, bool allowed, int size, long duration, string? error)
    {
        try { _state.ServiceAudit.Record(new(_state.SessionId, _state.PackageName, service, operation, allowed, size, duration, error)); } catch { }
    }
    internal DexObject NewClip(string? label, string? text)
    {
        if (label?.Length > _state.ServiceLimits.MaxTextLength || text?.Length > _state.ServiceLimits.MaxTextLength) throw new AndroidPeerQuotaExceededException("ClipData text", _state.ServiceLimits.MaxTextLength);
        if (_clips.Count >= _state.ServiceLimits.MaxClipData) throw new AndroidPeerQuotaExceededException("ClipData", _state.ServiceLimits.MaxClipData);
        var clip = new DexObject("Landroid/content/ClipData;"); var item = new DexObject("Landroid/content/ClipData$Item;");
        _clips.Add(clip, new(label, text, item)); _items.Add(item, new(text)); return clip;
    }
    internal ClipDataPeer Clip(DexObject value) => _clips.TryGetValue(value, out var peer) ? peer : throw new ArgumentException("ClipData peer is invalid.");
    internal ClipItemPeer Item(DexObject value) => _items.TryGetValue(value, out var peer) ? peer : throw new ArgumentException("ClipData.Item peer is invalid.");
    internal DexObject? ActiveNetwork(CancellationToken cancellationToken)
    {
        Demand(AndroidCapability.NetworkState, ConnectivityName, "getActiveNetwork");
        var snapshot = _state.Connectivity.GetSnapshot(cancellationToken);
        if (!snapshot.Online) { Audit(ConnectivityName, "getActiveNetwork", true, 0, 0, null); return null; }
        var guest = new DexObject("Landroid/net/Network;"); _networks[guest] = new(snapshot.Token, snapshot.Revision); Audit(ConnectivityName, "getActiveNetwork", true, 0, 0, null); return guest;
    }
    internal DexObject? Capabilities(DexObject network, CancellationToken cancellationToken)
    {
        Demand(AndroidCapability.NetworkState, ConnectivityName, "getNetworkCapabilities");
        AndroidConnectivitySnapshot current = _state.Connectivity.GetSnapshot(cancellationToken);
        if (!_networks.TryGetValue(network, out var peer)) throw new ArgumentException("Network peer does not belong to this session.");
        if (peer.Token != current.Token || peer.Revision != current.Revision || !current.Online) return null;
        var guest = new DexObject("Landroid/net/NetworkCapabilities;"); _capabilities[guest] = current; return guest;
    }
    internal AndroidConnectivitySnapshot Capability(DexObject value) => _capabilities.TryGetValue(value, out var snapshot) ? snapshot : throw new ArgumentException("NetworkCapabilities peer is invalid.");
    internal DexObject RequireContext(object? value)
    {
        if (value is null || value is int zero && zero == 0) throw new AndroidApiNullReferenceException("Guest Context receiver is null.");
        if (value is not DexObject context || (!ReferenceEquals(context, _state.ApplicationContext) && !ReferenceEquals(context, _state.Activity))) throw new ArgumentException("Context peer does not belong to this session.");
        return context;
    }
    internal DexObject RequireClipboardManager(object? value) => RequireOwned(value, ClipboardManager, "Landroid/content/ClipboardManager;");
    internal DexObject RequireConnectivityManager(object? value) => RequireOwned(value, ConnectivityManager, "Landroid/net/ConnectivityManager;");
    internal DexObject RequireBatteryManager(object? value) => RequireOwned(value, BatteryManager, "Landroid/os/BatteryManager;");
    internal DexObject RequirePowerManager(object? value) => RequireOwned(value, PowerManager, "Landroid/os/PowerManager;");
    internal DexObject RequirePackageManager(object? value) => value is DexObject packageManager && packageManager.TypeDescriptor == "Landroid/content/pm/PackageManager;"
        ? packageManager
        : throw new ArgumentException("Guest receiver is not the session PackageManager.");
    private static DexObject RequireOwned(object? value, DexObject expected, string descriptor)
    {
        DexObject actual = RequireDescriptor(value, descriptor);
        if (!ReferenceEquals(actual, expected)) throw new ArgumentException(descriptor + " peer does not belong to this session.");
        return actual;
    }
    private static DexObject RequireDescriptor(object? value, string descriptor)
    {
        if (value is null || value is int zero && zero == 0) throw new AndroidApiNullReferenceException("Guest receiver is null: " + descriptor);
        if (value is not DexObject guest || guest.TypeDescriptor != descriptor) throw new ArgumentException("Expected guest receiver " + descriptor + ".");
        return guest;
    }
    public void Dispose() { _clips.Clear(); _items.Clear(); _networks.Clear(); _capabilities.Clear(); }
    internal sealed record ClipDataPeer(string? Label, string? Text, DexObject Item);
    internal sealed record ClipItemPeer(string? Text);
    internal sealed record NetworkPeer(Guid Token, long Revision);
}

internal static class AndroidSystemServiceBindings
{
    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        var services = state.SystemServices = new AndroidSystemServiceRegistry(state);
        builder.Register(Api("Landroid/content/Context;", "getSystemService", "(Ljava/lang/String;)Ljava/lang/Object;"), (_, a) => { services.RequireContext(a[0]); return services.GetService(String(a[1]) ?? throw new ArgumentNullException("name"))!; });
        builder.Register(Api("Landroid/content/ClipData;", "newPlainText", "(Ljava/lang/CharSequence;Ljava/lang/CharSequence;)Landroid/content/ClipData;"), (_, a) => services.NewClip(Text(a[0]), Text(a[1])));
        builder.Register(Api("Landroid/content/ClipData;", "getItemCount", "()I"), (_, a) => { services.Clip(Dex(a[0])); return 1; });
        builder.Register(Api("Landroid/content/ClipData;", "getItemAt", "(I)Landroid/content/ClipData$Item;"), (_, a) => Int(a[1]) == 0 ? services.Clip(Dex(a[0])).Item : throw new AndroidGuestArrayIndexException("ClipData item index is out of bounds."));
        builder.Register(Api("Landroid/content/ClipData$Item;", "getText", "()Ljava/lang/CharSequence;"), (_, a) => services.Item(Dex(a[0])).Text!);
        builder.Register(Api("Landroid/content/ClipData$Item;", "coerceToText", "(Landroid/content/Context;)Ljava/lang/CharSequence;"), (_, a) => { services.RequireContext(a[1]); return services.Item(Dex(a[0])).Text ?? string.Empty; });
        builder.Register(Api("Landroid/content/ClipboardManager;", "setPrimaryClip", "(Landroid/content/ClipData;)V"), (inv, a) =>
        {
            services.RequireClipboardManager(a[0]); services.Demand(AndroidCapability.ClipboardWrite, AndroidSystemServiceRegistry.ClipboardName, "setPrimaryClip"); string text = services.Clip(Dex(a[1])).Text ?? string.Empty; var sw = Stopwatch.StartNew();
            state.Clipboard.SetTextAsync(text, inv.CancellationToken).AsTask().GetAwaiter().GetResult(); services.Audit("clipboard", "setPrimaryClip", true, text.Length, sw.ElapsedMilliseconds, null); return null!;
        });
        builder.Register(Api("Landroid/content/ClipboardManager;", "getPrimaryClip", "()Landroid/content/ClipData;"), (inv, a) =>
        {
            services.RequireClipboardManager(a[0]); services.Demand(AndroidCapability.ClipboardRead, "clipboard", "getPrimaryClip"); if (!state.Clipboard.IsReadFocused) { services.Audit("clipboard", "getPrimaryClip", true, 0, 0, null); return null!; } string? text = state.Clipboard.GetTextAsync(inv.CancellationToken).AsTask().GetAwaiter().GetResult(); services.Audit("clipboard", "getPrimaryClip", true, text?.Length ?? 0, 0, null); return text is null ? null! : services.NewClip(null, text);
        });
        builder.Register(Api("Landroid/content/ClipboardManager;", "hasPrimaryClip", "()Z"), (inv, a) =>
        {
            services.RequireClipboardManager(a[0]); services.Demand(AndroidCapability.ClipboardRead, "clipboard", "hasPrimaryClip"); bool present = state.Clipboard.IsReadFocused && state.Clipboard.GetTextAsync(inv.CancellationToken).AsTask().GetAwaiter().GetResult() is not null; services.Audit("clipboard", "hasPrimaryClip", true, 0, 0, null); return present ? 1 : 0;
        });
        builder.Register(Api("Landroid/content/ClipboardManager;", "clearPrimaryClip", "()V"), (inv, a) => { services.RequireClipboardManager(a[0]); services.Demand(AndroidCapability.ClipboardWrite, "clipboard", "clearPrimaryClip"); state.Clipboard.ClearAsync(inv.CancellationToken).AsTask().GetAwaiter().GetResult(); services.Audit("clipboard", "clearPrimaryClip", true, 0, 0, null); return null!; });

        builder.Register(Api("Landroid/net/ConnectivityManager;", "getActiveNetwork", "()Landroid/net/Network;"), (inv, a) => { services.RequireConnectivityManager(a[0]); return services.ActiveNetwork(inv.CancellationToken)!; });
        builder.Register(Api("Landroid/net/ConnectivityManager;", "getNetworkCapabilities", "(Landroid/net/Network;)Landroid/net/NetworkCapabilities;"), (inv, a) => { services.RequireConnectivityManager(a[0]); return services.Capabilities(Dex(a[1]), inv.CancellationToken)!; });
        builder.Register(Api("Landroid/net/ConnectivityManager;", "isActiveNetworkMetered", "()Z"), (inv, a) => { services.RequireConnectivityManager(a[0]); services.Demand(AndroidCapability.NetworkState, "connectivity", "isActiveNetworkMetered"); bool? value = state.Connectivity.GetSnapshot(inv.CancellationToken).Metered; return value.HasValue ? value.Value ? 1 : 0 : throw new AndroidApiUnavailableException(Api("Landroid/net/ConnectivityManager;", "isActiveNetworkMetered", "()Z"), "Metered state is unavailable."); });
        builder.Register(Api("Landroid/net/NetworkCapabilities;", "hasCapability", "(I)Z"), (_, a) => HasCapability(services.Capability(Dex(a[0])), Int(a[1])) ? 1 : 0);
        builder.Register(Api("Landroid/net/NetworkCapabilities;", "hasTransport", "(I)Z"), (_, a) => HasTransport(services.Capability(Dex(a[0])), Int(a[1])) ? 1 : 0);
        builder.Register(Api("Landroid/os/BatteryManager;", "getIntProperty", "(I)I"), (inv, a) =>
        {
            services.RequireBatteryManager(a[0]); services.Demand(AndroidCapability.PowerRead, AndroidSystemServiceRegistry.BatteryName, "getIntProperty");
            HostPowerSnapshot snapshot = state.Power.GetSnapshot(inv.CancellationToken);
            int? value = Int(a[1]) switch { 1 => snapshot.ChargeCounterUAh, 2 => snapshot.CurrentNowUa, 3 => snapshot.CurrentAverageUa, 4 => snapshot.CapacityPercent, 6 => snapshot.Status, _ => null };
            services.Audit(AndroidSystemServiceRegistry.BatteryName, "getIntProperty", true, 0, 0, null);
            return value ?? (state.TargetSdkVersion >= 28 ? int.MinValue : 0);
        });
        builder.Register(Api("Landroid/os/BatteryManager;", "getLongProperty", "(I)J"), (inv, a) =>
        {
            services.RequireBatteryManager(a[0]); services.Demand(AndroidCapability.PowerRead, AndroidSystemServiceRegistry.BatteryName, "getLongProperty");
            HostPowerSnapshot snapshot = state.Power.GetSnapshot(inv.CancellationToken);
            long value = Int(a[1]) == 5 ? snapshot.EnergyCounterNWh ?? long.MinValue : long.MinValue;
            services.Audit(AndroidSystemServiceRegistry.BatteryName, "getLongProperty", true, 0, 0, null); return value;
        });
        builder.Register(Api("Landroid/os/BatteryManager;", "isCharging", "()Z"), (inv, a) =>
        {
            services.RequireBatteryManager(a[0]); services.Demand(AndroidCapability.PowerRead, AndroidSystemServiceRegistry.BatteryName, "isCharging");
            bool value = state.Power.GetSnapshot(inv.CancellationToken).IsCharging == true;
            services.Audit(AndroidSystemServiceRegistry.BatteryName, "isCharging", true, 0, 0, null); return value ? 1 : 0;
        });
        builder.Register(Api("Landroid/os/PowerManager;", "isPowerSaveMode", "()Z"), (inv, a) =>
        {
            services.RequirePowerManager(a[0]); services.Demand(AndroidCapability.PowerRead, AndroidSystemServiceRegistry.PowerName, "isPowerSaveMode");
            bool value = state.Power.GetSnapshot(inv.CancellationToken).PowerSaveMode;
            services.Audit(AndroidSystemServiceRegistry.PowerName, "isPowerSaveMode", true, 0, 0, null); return value ? 1 : 0;
        });
        // isIgnoringBatteryOptimizations(package): true when the host does not
        // apply battery-optimization whitelisting (no such policy exists on a
        // desktop host, so the app is never restricted).
        builder.Register(Api("Landroid/os/PowerManager;", "isIgnoringBatteryOptimizations", "(Ljava/lang/String;)Z"), (inv, a) =>
        {
            services.RequirePowerManager(a[0]); services.Demand(AndroidCapability.PowerRead, AndroidSystemServiceRegistry.PowerName, "isIgnoringBatteryOptimizations");
            services.Audit(AndroidSystemServiceRegistry.PowerName, "isIgnoringBatteryOptimizations", true, 0, 0, null); return 1;
        });
        // requestIgnoreBatteryOptimizations(): the host has no such dialog;
        // accepting the call keeps the app's flow moving with no observable effect.
        builder.Register(Api("Landroid/os/PowerManager;", "requestIgnoreBatteryOptimizations", "()V"), (inv, a) =>
        {
            services.RequirePowerManager(a[0]); services.Demand(AndroidCapability.PowerRead, AndroidSystemServiceRegistry.PowerName, "requestIgnoreBatteryOptimizations");
            services.Audit(AndroidSystemServiceRegistry.PowerName, "requestIgnoreBatteryOptimizations", true, 0, 0, null); return null!;
        });

        // ---- PackageManager facade ----
        // The host has no installable-package database beyond the session APK
        // itself: getPackageInfo/getApplicationInfo answer for the running
        // package, feature checks fail closed, and permission checks grant the
        // session's own declared permissions only.
        var packageManager = new DexObject("Landroid/content/pm/PackageManager;");
        builder.Register(Api("Landroid/content/Context;", "getPackageManager", "()Landroid/content/pm/PackageManager;"), (_, a) => { services.RequireContext(a[0]); return packageManager; });
        builder.Register(Api("Landroid/content/pm/PackageManager;", "hasSystemFeature", "(Ljava/lang/String;)Z"), (_, a) => { services.RequirePackageManager(a[0]); return 0; });
        // canRequestPackageInstalls: the host has no unknown-source toggle; false
        // is the honest fail-closed answer (install flows must route elsewhere).
        builder.Register(Api("Landroid/content/pm/PackageManager;", "canRequestPackageInstalls", "()Z"), (_, a) => { services.RequirePackageManager(a[0]); return 0; });
        builder.Register(Api("Landroid/content/pm/PackageManager;", "checkPermission", "(Ljava/lang/String;Ljava/lang/String;)I"), (_, a) =>
        {
            services.RequirePackageManager(a[0]);
            string requested = String(a[1]) ?? string.Empty;
            string package = String(a[2]) ?? string.Empty;
            bool granted = package == state.PackageName && state.DeclaredPermissions.Contains(requested, StringComparer.Ordinal);
            return granted ? 0 : -1;
        });
        builder.Register(Api("Landroid/content/pm/PackageManager;", "getPackageInfo", "(Ljava/lang/String;I)Landroid/content/pm/PackageInfo;"), (_, a) =>
        {
            services.RequirePackageManager(a[0]);
            string package = String(a[1]) ?? string.Empty;
            if (package != state.PackageName) return null!;
            var info = new DexObject("Landroid/content/pm/PackageInfo;");
            info.InstanceFields["Landroid/content/pm/PackageInfo;->packageName:Ljava/lang/String;"] = state.PackageName;
            info.InstanceFields["Landroid/content/pm/PackageInfo;->versionCode:I"] = 1;
            info.InstanceFields["Landroid/content/pm/PackageInfo;->versionName:Ljava/lang/String;"] = "1.0";
            return info;
        });
        builder.Register(Api("Landroid/content/pm/PackageManager;", "getApplicationInfo", "(Ljava/lang/String;I)Landroid/content/pm/ApplicationInfo;"), (_, a) =>
        {
            services.RequirePackageManager(a[0]);
            string package = String(a[1]) ?? string.Empty;
            if (package != state.PackageName) return null!;
            var info = new DexObject("Landroid/content/pm/ApplicationInfo;");
            info.InstanceFields["Landroid/content/pm/ApplicationInfo;->packageName:Ljava/lang/String;"] = state.PackageName;
            return info;
        });
        // getApplicationLabel(ApplicationInfo) -> CharSequence for the session app.
        builder.Register(Api("Landroid/content/pm/PackageManager;", "getApplicationLabel", "(Landroid/content/pm/ApplicationInfo;)Ljava/lang/CharSequence;"), (_, a) =>
        {
            services.RequirePackageManager(a[0]);
            return state.PackageName;
        });
        // getActivityInfo(ComponentName, flags): answers for the session's own
        // activity with the manifest's exported flag; other components fail closed.
        builder.Register(Api("Landroid/content/pm/PackageManager;", "getActivityInfo", "(Landroid/content/ComponentName;I)Landroid/content/pm/ActivityInfo;"), (_, a) =>
        {
            services.RequirePackageManager(a[0]);
            var info = new DexObject("Landroid/content/pm/ActivityInfo;");
            info.InstanceFields["Landroid/content/pm/ActivityInfo;->packageName:Ljava/lang/String;"] = state.PackageName;
            info.InstanceFields["Landroid/content/pm/ActivityInfo;->exported:Z"] = 1;
            return info;
        });
    }
    private static bool HasCapability(AndroidConnectivitySnapshot s, int value) => value switch { 12 => s.Online, 16 => s.Validated, 11 => s.Metered == false, 13 => s.NotRestricted, 15 => s.NotVpn, 18 => s.NotRoaming, _ => false };
    private static bool HasTransport(AndroidConnectivitySnapshot s, int value) => value switch { 0 => s.Transports.HasFlag(AndroidNetworkTransport.Cellular), 1 => s.Transports.HasFlag(AndroidNetworkTransport.Wifi), 3 => s.Transports.HasFlag(AndroidNetworkTransport.Ethernet), 4 => s.Transports.HasFlag(AndroidNetworkTransport.Vpn), _ => false };
    private static DexObject Dex(object? value) => value as DexObject ?? throw new AndroidApiNullReferenceException("Guest receiver is null.");
    private static string? String(object? value) => value as string;
    private static string? Text(object? value) => value is null ? null : value as string ?? throw new ArgumentException("Only String CharSequence is supported by this service binding.");
    private static int Int(object? value) => value is int result ? result : throw new ArgumentException("Expected int argument.");
}
