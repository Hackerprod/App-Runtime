using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

public sealed class AndroidMediaRecorderTests
{
    private const string Owner = "Lorg/example/MainActivity;";
    private const string AudioSourceField = "Landroid/media/MediaRecorder;->audioSource:I";
    private const string OutputFormatField = "Landroid/media/MediaRecorder;->outputFormat:I";
    private const string AudioEncoderField = "Landroid/media/MediaRecorder;->audioEncoder:I";
    private const string BitRateField = "Landroid/media/MediaRecorder;->audioEncodingBitRate:I";
    private const string SamplingRateField = "Landroid/media/MediaRecorder;->audioSamplingRate:I";
    private const string OutputFileField = "Landroid/media/MediaRecorder;->outputFile:Ljava/lang/String;";
    private const string FilePathField = "Ljava/io/File;->path:Ljava/lang/String;";

    [Fact]
    public void Media_recorder_ctor_registers_and_validates_the_receiver()
    {
        using var state = new AndroidFrameworkState("media", "org.example", Owner, new ActivityWindowPeers());
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

    [Fact]
    public void Start_requires_the_microphone_capability_and_calls_the_recorder_port()
    {
        var recorder = new FakeAudioRecorder();
        var audit = new CapturingAudit();
        using var state = new AndroidFrameworkState("media", "org.example", Owner, new ActivityWindowPeers(),
            capabilityPolicy: new AndroidCapabilityPolicy([AndroidCapability.Microphone]),
            capabilityAudit: audit, audioRecorder: recorder);
        var registry = AndroidApiBindings.CreateBuilder(state, new QuietLog()).Build();
        var session = new AndroidApiSessionContext(state.SessionId, state.PackageName, state.ActivityDescriptor, CancellationToken.None, () => true);

        DexObject rec = ConfiguredRecorder(registry, session);

        // Denied policy -> start throws SecurityException, port NOT called.
        using var deniedState = new AndroidFrameworkState("media", "org.example", Owner, new ActivityWindowPeers(),
            capabilityPolicy: AndroidCapabilityPolicy.DenyAll, audioRecorder: recorder);
        var deniedRegistry = AndroidApiBindings.CreateBuilder(deniedState, new QuietLog()).Build();
        var deniedSession = new AndroidApiSessionContext(deniedState.SessionId, deniedState.PackageName, deniedState.ActivityDescriptor, CancellationToken.None, () => true);
        DexObject deniedRec = ConfiguredRecorder(deniedRegistry, deniedSession);
        Assert.Throws<AndroidApiSecurityException>(() => Invoke(deniedRegistry, deniedSession, "Landroid/media/MediaRecorder;", "start", "()V", AndroidInvokeKind.Virtual, deniedRec));
        Assert.Null(recorder.StartedPath);

        // Granted -> start delegates to the port with the configured values.
        Invoke(registry, session, "Landroid/media/MediaRecorder;", "start", "()V", AndroidInvokeKind.Virtual, rec);
        Assert.Equal("C:\\sandbox\\recording.mp4", recorder.StartedPath);
        Assert.Equal(44100, recorder.SampleRate);
        Assert.Equal(128000, recorder.BitRate);
        Assert.Contains(audit.Entries, e => e.Capability == AndroidCapability.Microphone && e.Allowed && e.Operation == "MediaRecorder.start");

        // stop() finalizes through the port.
        Invoke(registry, session, "Landroid/media/MediaRecorder;", "stop", "()V", AndroidInvokeKind.Virtual, rec);
        Assert.Equal(1, recorder.StopCount);
    }

    private static DexObject ConfiguredRecorder(AndroidApiRegistry registry, AndroidApiSessionContext session)
    {
        var recorder = new DexObject("Landroid/media/MediaRecorder;");
        Invoke(registry, session, "Landroid/media/MediaRecorder;", "<init>", "()V", AndroidInvokeKind.Direct, recorder);
        Invoke(registry, session, "Landroid/media/MediaRecorder;", "setAudioSource", "(I)V", AndroidInvokeKind.Virtual, recorder, 1);
        Invoke(registry, session, "Landroid/media/MediaRecorder;", "setOutputFormat", "(I)V", AndroidInvokeKind.Virtual, recorder, 2);
        Invoke(registry, session, "Landroid/media/MediaRecorder;", "setAudioEncoder", "(I)V", AndroidInvokeKind.Virtual, recorder, 3);
        Invoke(registry, session, "Landroid/media/MediaRecorder;", "setAudioEncodingBitRate", "(I)V", AndroidInvokeKind.Virtual, recorder, 128000);
        Invoke(registry, session, "Landroid/media/MediaRecorder;", "setAudioSamplingRate", "(I)V", AndroidInvokeKind.Virtual, recorder, 44100);
        Invoke(registry, session, "Landroid/media/MediaRecorder;", "setOutputFile", "(Ljava/lang/String;)V", AndroidInvokeKind.Virtual, recorder, "C:\\sandbox\\recording.mp4");
        Invoke(registry, session, "Landroid/media/MediaRecorder;", "prepare", "()V", AndroidInvokeKind.Virtual, recorder);
        return recorder;
    }

    private static void Invoke(AndroidApiRegistry registry, AndroidApiSessionContext session, string owner, string name, string descriptor, AndroidInvokeKind kind, params object[] args)
    {
        var api = new AndroidApiMethodId(owner, name, descriptor);
        registry.Invoke(session, new AndroidApiCallSite(Owner + "->test()V", 0, api, api, kind), args);
    }

    private sealed class FakeAudioRecorder : IAndroidAudioRecorder
    {
        public string? StartedPath { get; private set; }
        public int SampleRate { get; private set; }
        public int BitRate { get; private set; }
        public int StopCount { get; private set; }
        public void Start(string outputPath, int sampleRate, int bitRate) { StartedPath = outputPath; SampleRate = sampleRate; BitRate = bitRate; }
        public void Stop() => StopCount++;
    }

    private sealed class CapturingAudit : IAndroidCapabilityAuditSink
    {
        public List<AndroidCapabilityAuditEntry> Entries { get; } = [];
        public void Record(AndroidCapabilityAuditEntry entry) => Entries.Add(entry);
    }

    private sealed class QuietLog : IAndroidLogSink { public int Info(AndroidLogEntry entry) => 1; }
}
