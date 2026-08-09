using AndroidRuntime.Core.Apk;
using AndroidRuntime.Core;

namespace AndroidRuntime.Core.Tests;

public sealed class RuntimeProbeIntegrationTests
{
    [Fact]
    public void Signed_real_apk_reaches_resumed_with_each_callback_exactly_once()
    {
        string apkPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "RuntimeProbe.apk");
        var runtime = new AndroidAppRuntime();

        var session = runtime.LaunchSession(apkPath);

        Assert.Equal(AndroidActivityState.Resumed, session.State);
        Assert.Equal("Lorg/example/runtimeprobe/MainActivity;", session.Activity.TypeDescriptor);
        Assert.Equal(123, session.Activity.InstanceFields["lifecycleState"]);
        Assert.Equal(1, session.Activity.InstanceFields["createCount"]);
        Assert.Equal(1, session.Activity.InstanceFields["startCount"]);
        Assert.Equal(1, session.Activity.InstanceFields["resumeCount"]);
        var compatibilityActivity = runtime.Launch(apkPath);
        Assert.Equal(123, compatibilityActivity.InstanceFields["lifecycleState"]);
    }
}
