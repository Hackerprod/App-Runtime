using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

public sealed class AndroidPowerServiceTests
{
    [Fact]
    public void Power_services_are_stable_governed_and_use_exact_sentinels()
    {
        var power = new FakePower(new HostPowerSnapshot(true, true, 73, null, 4_500_000, -120_000, -110_000, 2, true));
        using var state = State(35, new AndroidCapabilityPolicy([AndroidCapability.PowerRead]), power);
        var registry = AndroidApiBindings.CreateBuilder(state, new ConsoleAndroidLogSink()).Build();
        var battery = Invoke(registry, state, "Landroid/content/Context;", "getSystemService", "(Ljava/lang/String;)Ljava/lang/Object;", state.ApplicationContext, "batterymanager");
        var batteryAgain = Invoke(registry, state, "Landroid/content/Context;", "getSystemService", "(Ljava/lang/String;)Ljava/lang/Object;", state.ApplicationContext, "batterymanager");
        var manager = Assert.IsType<DexObject>(battery);
        Assert.Same(manager, batteryAgain);
        Assert.Equal(73, Invoke(registry, state, "Landroid/os/BatteryManager;", "getIntProperty", "(I)I", manager, 4));
        Assert.Equal(int.MinValue, Invoke(registry, state, "Landroid/os/BatteryManager;", "getIntProperty", "(I)I", manager, 99));
        Assert.Equal(long.MinValue, Invoke(registry, state, "Landroid/os/BatteryManager;", "getLongProperty", "(I)J", manager, 5));
        Assert.Equal(1, Invoke(registry, state, "Landroid/os/BatteryManager;", "isCharging", "()Z", manager));
        var powerManager = Assert.IsType<DexObject>(Invoke(registry, state, "Landroid/content/Context;", "getSystemService", "(Ljava/lang/String;)Ljava/lang/Object;", state.ApplicationContext, "power"));
        Assert.Equal(1, Invoke(registry, state, "Landroid/os/PowerManager;", "isPowerSaveMode", "()Z", powerManager));
    }

    [Fact]
    public void Legacy_target_uses_zero_and_denied_or_foreign_receivers_fail_closed()
    {
        var power = new FakePower(new HostPowerSnapshot(true, false, null, null, null, null, null, null, false));
        using var legacy = State(27, new AndroidCapabilityPolicy([AndroidCapability.PowerRead]), power);
        var registry = AndroidApiBindings.CreateBuilder(legacy, new ConsoleAndroidLogSink()).Build();
        var battery = Assert.IsType<DexObject>(Invoke(registry, legacy, "Landroid/content/Context;", "getSystemService", "(Ljava/lang/String;)Ljava/lang/Object;", legacy.ApplicationContext, "batterymanager"));
        Assert.Equal(0, Invoke(registry, legacy, "Landroid/os/BatteryManager;", "getIntProperty", "(I)I", battery, 4));

        using var denied = State(35, AndroidCapabilityPolicy.DenyAll, power);
        var deniedRegistry = AndroidApiBindings.CreateBuilder(denied, new ConsoleAndroidLogSink()).Build();
        Assert.Throws<AndroidApiSecurityException>(() => Invoke(deniedRegistry, denied, "Landroid/content/Context;", "getSystemService", "(Ljava/lang/String;)Ljava/lang/Object;", denied.ApplicationContext, "power"));
        Assert.Throws<AndroidApiBindingException>(() => Invoke(deniedRegistry, denied, "Landroid/os/BatteryManager;", "isCharging", "()Z", battery));
    }

    [Fact]
    public void Real_dex_calls_both_power_services_and_returns_a_wide_result()
    {
        byte[] apkBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "RuntimeProbe.apk"));
        var loaded = AndroidRuntime.Core.Apk.ApkLoader.Load(new MemoryStream(apkBytes));
        var manifest = AndroidRuntime.Core.Apk.AndroidManifestReader.Parse(loaded.AndroidManifestXml);
        using var state = new AndroidFrameworkState("real-power", manifest.PackageName, "Lorg/example/runtimeprobe/MainActivity;", new ActivityWindowPeers(), targetSdkVersion: manifest.TargetSdkVersion, capabilityPolicy: new AndroidCapabilityPolicy([AndroidCapability.PowerRead]), power: new FakePower(new HostPowerSnapshot(true, true, 75, 5_000_000_000L, null, null, null, null, true)));
        var interpreter = new DexInterpreter(DexReader.Parse(loaded.ClassesDexFiles[0]), AndroidApiBindings.CreateBuilder(state, new ConsoleAndroidLogSink()).Build());
        Assert.Equal(5_000_000_078L, interpreter.InvokeStaticExact("Lorg/example/runtimeprobe/PowerProbe;", "sample", "(Landroid/content/Context;)J", state.ApplicationContext));
    }

    private static AndroidFrameworkState State(int targetSdk, IAndroidCapabilityPolicy policy, IAndroidPower power) =>
        new("power", "org.example", "Lorg/example/MainActivity;", new ActivityWindowPeers(), targetSdkVersion: targetSdk, capabilityPolicy: policy, power: power);

    private static object? Invoke(AndroidApiRegistry registry, AndroidFrameworkState state, string owner, string name, string descriptor, params object?[] args) =>
        registry.Invoke(new AndroidApiSessionContext(state.SessionId, state.PackageName, state.ActivityDescriptor, CancellationToken.None, () => true), new AndroidApiCallSite("Ltest/Probe;->run()V", 0, new(owner, name, descriptor), new(owner, name, descriptor), AndroidInvokeKind.Virtual), args!);

    private sealed class FakePower(HostPowerSnapshot snapshot) : IAndroidPower
    {
        public bool IsAvailable => true;
        public HostPowerSnapshot GetSnapshot(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return snapshot; }
    }
}
