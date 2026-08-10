using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

public sealed class AndroidMediaRecorderTests
{
    [Fact]
    public void Media_recorder_ctor_registers_and_validates_the_receiver()
    {
        using var state = new AndroidFrameworkState("media", "org.example", "Lorg/example/MainActivity;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new QuietLog()).Build();
        var session = new AndroidApiSessionContext(state.SessionId, state.PackageName, state.ActivityDescriptor, CancellationToken.None, () => true);
        var api = new AndroidApiMethodId("Landroid/media/MediaRecorder;", "<init>", "()V");

        var recorder = new DexObject("Landroid/media/MediaRecorder;");
        object result = registry.Invoke(session, new AndroidApiCallSite("Ltest/Probe;->run()V", 0, api, api, AndroidInvokeKind.Direct), new object[] { recorder });

        Assert.Null(result);
        // A null receiver fails the registry's call-shape validation before the
        // binding runs (instance API receiver is null).
        Assert.Throws<AndroidApiNullReferenceException>(() => registry.Invoke(session, new AndroidApiCallSite("Ltest/Probe;->run()V", 0, api, api, AndroidInvokeKind.Direct), new object[] { null! }));
    }

    private sealed class QuietLog : IAndroidLogSink { public int Info(AndroidLogEntry entry) => 1; }
}
