using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Apk;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

public sealed class ActivityLifecycleSessionTests
{
    private const string ActivityDescriptor = "Landroid/app/Activity;";
    private const string LeafDescriptor = "Lexample/LeafActivity;";
    private const string BaseDescriptor = "Lexample/BaseActivity;";

    [Fact]
    public void Out_of_order_and_duplicate_transitions_fail_before_callback_bytecode()
    {
        var runtime = new AndroidAppRuntime();
        var session = runtime.CreateSession(FixturePath());

        var outOfOrder = Assert.Throws<InvalidOperationException>(() => session.Start());
        Assert.Contains("Constructed", outOfOrder.Message, StringComparison.Ordinal);
        Assert.Equal(AndroidActivityState.Constructed, session.State);
        Assert.Empty(session.Activity.InstanceFields);

        session.Create();
        var snapshot = session.Activity.InstanceFields.ToDictionary(pair => pair.Key, pair => pair.Value);
        var duplicate = Assert.Throws<InvalidOperationException>(() => session.Create());

        Assert.Contains("Created", duplicate.Message, StringComparison.Ordinal);
        Assert.Equal(AndroidActivityState.Created, session.State);
        Assert.Equal(snapshot, session.Activity.InstanceFields);
    }

    [Fact]
    public void Missing_overrides_fall_back_to_exact_activity_stubs()
    {
        var dex = DexWith(new DexClass { Descriptor = LeafDescriptor, SuperclassDescriptor = ActivityDescriptor });
        var session = Session(dex, AndroidApiRegistry.CreateActivityLifecycleRegistry());

        session.Create();
        session.Start();
        session.Resume();

        Assert.Equal(AndroidActivityState.Resumed, session.State);

        session.Pause();
        session.Stop();
        session.Destroy();
        Assert.Equal(AndroidActivityState.Destroyed, session.State);
    }

    [Fact]
    public void Reverse_lifecycle_enforces_order_and_partial_termination_suffixes()
    {
        var created = Session(DexWith(new DexClass { Descriptor = LeafDescriptor, SuperclassDescriptor = ActivityDescriptor }), AndroidApiRegistry.CreateActivityLifecycleRegistry());
        created.Create();
        created.Terminate();
        Assert.Equal(AndroidActivityState.Destroyed, created.State);

        var resumed = Session(DexWith(new DexClass { Descriptor = LeafDescriptor, SuperclassDescriptor = ActivityDescriptor }), AndroidApiRegistry.CreateActivityLifecycleRegistry());
        resumed.Create(); resumed.Start(); resumed.Resume();
        Assert.Throws<InvalidOperationException>(resumed.Stop);
        resumed.Terminate();
        resumed.Terminate();
        Assert.Equal(AndroidActivityState.Destroyed, resumed.State);
    }

    [Theory]
    [InlineData("onCreate", AndroidActivityState.Created, 0, 0, 0, 0, 0)]
    [InlineData("onStart", AndroidActivityState.Started, 1, 0, 0, 0, 1)]
    [InlineData("onResume", AndroidActivityState.Resumed, 1, 1, 1, 1, 1)]
    public void Finish_during_forward_callback_stops_immediately_and_selects_exact_reverse_suffix(string finishAt, AndroidActivityState checkpoint, int expectedStart, int expectedResume, int expectedShow, int expectedPause, int expectedStop)
    {
        using var state = new AndroidFrameworkState("finish", "example", LeafDescriptor, new ActivityWindowPeers());
        int starts = 0, resumes = 0, pauses = 0, stops = 0, destroys = 0;
        object Callback(string name) { if (name == "onStart") starts++; if (name == "onResume") resumes++; if (name == finishAt) state.RequestFinish(); return null!; }
        var registry = new AndroidApiRegistryBuilder()
            .Register(ActivityDescriptor, "onCreate", "(Landroid/os/Bundle;)V", (_, _) => Callback("onCreate"))
            .Register(ActivityDescriptor, "onStart", "()V", (_, _) => Callback("onStart"))
            .Register(ActivityDescriptor, "onResume", "()V", (_, _) => Callback("onResume"))
            .Register(ActivityDescriptor, "onPause", "()V", (_, _) => { pauses++; return null!; })
            .Register(ActivityDescriptor, "onStop", "()V", (_, _) => { stops++; return null!; })
            .Register(ActivityDescriptor, "onDestroy", "()V", (_, _) => { destroys++; return null!; })
            .Build();
        var session = Session(DexWith(new DexClass { Descriptor = LeafDescriptor, SuperclassDescriptor = ActivityDescriptor }), registry);
        var window = new CountingWindow();

        Assert.False(AndroidLifecycleCoordinator.RunForward(session, state, window, CancellationToken.None));
        Assert.Equal(checkpoint, session.State); Assert.Equal(expectedStart, starts); Assert.Equal(expectedResume, resumes); Assert.Equal(expectedShow, window.ShowCount);
        session.Terminate(); Assert.Equal(AndroidActivityState.Destroyed, session.State);
        Assert.Equal(expectedPause, pauses); Assert.Equal(expectedStop, stops); Assert.Equal(1, destroys);
    }

    [Fact]
    public void Lifecycle_lookup_finds_an_exact_override_in_a_dex_superclass()
    {
        var baseClass = new DexClass { Descriptor = BaseDescriptor, SuperclassDescriptor = ActivityDescriptor };
        baseClass.VirtualMethods.Add(InstanceFieldSetter(BaseDescriptor, "onStart", "()V", 7));
        var leafClass = new DexClass { Descriptor = LeafDescriptor, SuperclassDescriptor = BaseDescriptor };
        var dex = DexWith(baseClass, leafClass);
        dex.Fields.Add(new DexFieldRef { ClassDescriptor = BaseDescriptor, Name = "marker", Type = "I" });
        var session = Session(dex, AndroidApiRegistry.CreateActivityLifecycleRegistry());

        session.Create();
        session.Start();

        Assert.Equal(AndroidActivityState.Started, session.State);
        Assert.Equal(7, session.Activity.InstanceFields["marker"]);
    }

    [Fact]
    public void Missing_final_stub_faults_with_the_complete_method_identity()
    {
        var dex = DexWith(new DexClass { Descriptor = LeafDescriptor, SuperclassDescriptor = ActivityDescriptor });
        var registry = new AndroidApiRegistryBuilder()
            .Register(ActivityDescriptor, "onCreate", "(Landroid/os/Bundle;)V", (_, _) => null!)
            .Register(ActivityDescriptor, "onStart", "(I)V", (_, _) => null!)
            .Build();
        var session = Session(dex, registry);
        session.Create();

        var error = Assert.Throws<MissingMethodException>(() => session.Start());

        Assert.Contains("Landroid/app/Activity;->onStart()V", error.Message, StringComparison.Ordinal);
        Assert.Equal(AndroidActivityState.Faulted, session.State);
    }

    [Fact]
    public void Failed_callback_faults_session_and_blocks_later_callbacks()
    {
        var cls = new DexClass { Descriptor = LeafDescriptor, SuperclassDescriptor = ActivityDescriptor };
        cls.VirtualMethods.Add(new DexEncodedMethod
        {
            Method = MethodRef(LeafDescriptor, "onStart", "()V"),
            Code = new DexCodeItem { RegistersSize = 1, InsSize = 1, Instructions = [0x00ff] }
        });
        var dex = DexWith(cls);
        int resumeCalls = 0;
        var registry = new AndroidApiRegistryBuilder()
            .Register(ActivityDescriptor, "onCreate", "(Landroid/os/Bundle;)V", (_, _) => null!)
            .Register(ActivityDescriptor, "onResume", "()V", (_, _) => { resumeCalls++; return null!; })
            .Build();
        var session = Session(dex, registry);
        session.Create();

        Assert.Throws<NotImplementedException>(() => session.Start());
        Assert.Equal(AndroidActivityState.Faulted, session.State);
        var blocked = Assert.Throws<InvalidOperationException>(() => session.Resume());
        Assert.Contains("Faulted", blocked.Message, StringComparison.Ordinal);
        Assert.Equal(0, resumeCalls);
    }

    [Fact]
    public void Unregistered_real_api_call_is_traced_and_faults_without_fake_semantics()
    {
        var cls = new DexClass { Descriptor = LeafDescriptor, SuperclassDescriptor = ActivityDescriptor };
        cls.VirtualMethods.Add(new DexEncodedMethod
        {
            Method = MethodRef(LeafDescriptor, "onStart", "()V"),
            Code = new DexCodeItem
            {
                RegistersSize = 1,
                InsSize = 1,
                Instructions = [0x0071, 0x0000, 0x0000, 0x000e]
            }
        });
        var dex = DexWith(cls);
        var missingApi = MethodRef("Landroid/view/View;", "invalidate", "()V");
        dex.Methods.Add(missingApi);
        var trace = new AndroidApiTraceBuffer(16);
        var apiContext = new AndroidApiSessionContext("missing", "example", LeafDescriptor, default, () => true, trace);
        var session = new AndroidActivitySession(
            new DexInterpreter(dex, AndroidApiRegistry.CreateActivityLifecycleRegistry(), apiSession: apiContext),
            new DexObject(LeafDescriptor));
        session.Create();

        var error = Assert.Throws<AndroidApiNotImplementedException>(() => session.Start());

        Assert.Equal("Landroid/view/View;->invalidate()V", error.Api.ToString());
        Assert.Equal(AndroidActivityState.Faulted, session.State);
        Assert.Contains(trace.Snapshot(), item =>
            item.Kind == AndroidApiEventKind.Unimplemented && item.Invocation.ResolvedApi == error.Api);
    }

    [Fact]
    public void Real_apk_with_unimplemented_api_faults_and_records_demand()
    {
        var apk = ApkLoader.Load(UnimplementedFixturePath());
        var manifest = AndroidManifestReader.Parse(apk.AndroidManifestXml);
        var dex = DexReader.Parse(apk.ClassesDexFiles[0]);
        var peers = new ActivityWindowPeers();
        var trace = new AndroidApiTraceBuffer(32);
        var context = new AndroidApiSessionContext("real-missing", manifest.PackageName, manifest.LauncherActivityDescriptor, default, () => true, trace);
        var frameworkState = new AndroidFrameworkState("real-missing", manifest.PackageName, manifest.LauncherActivityDescriptor, peers);
        var registry = AndroidApiBindings.CreateBuilder(frameworkState, new PositiveLogSink()).Build();
        var interpreter = new DexInterpreter(dex, registry, apiSession: context);
        var activity = interpreter.ConstructInstance(manifest.LauncherActivityDescriptor);
        peers.Associate(activity, new InMemoryActivityWindow());
        frameworkState.AttachActivity(activity);
        var session = new AndroidActivitySession(interpreter, activity);

        var error = Assert.Throws<AndroidApiNotImplementedException>(() => session.Create());

        Assert.Equal("Landroid/util/Log;->w(Ljava/lang/String;Ljava/lang/String;Ljava/lang/Throwable;)I", error.Api.ToString());
        Assert.Equal(AndroidActivityState.Faulted, session.State);
        Assert.Contains(trace.Snapshot(), item => item.Kind == AndroidApiEventKind.Unimplemented && item.Invocation.InvocationId == error.Invocation.InvocationId);
    }

    private static AndroidActivitySession Session(DexFile dex, AndroidApiRegistry registry) =>
        new(new DexInterpreter(dex, registry), new DexObject(LeafDescriptor));

    private static DexFile DexWith(params DexClass[] classes)
    {
        var dex = new DexFile();
        dex.Classes.AddRange(classes);
        dex.BuildIndexes();
        return dex;
    }

    private static DexEncodedMethod InstanceFieldSetter(string classDescriptor, string name, string descriptor, int value) => new()
    {
        Method = MethodRef(classDescriptor, name, descriptor),
        Code = new DexCodeItem
        {
            RegistersSize = 2,
            InsSize = 1,
            Instructions = [(ushort)(0x0012 | (value << 12)), 0x1059, 0x0000, 0x000e]
        }
    };

    private static DexMethodRef MethodRef(string classDescriptor, string name, string descriptor) => new()
    {
        ClassDescriptor = classDescriptor,
        Name = name,
        Proto = new DexProto
        {
            Shorty = "V",
            ReturnType = "V",
            ParameterTypes = []
        }
    };

    private static string FixturePath() => Path.Combine(AppContext.BaseDirectory, "Fixtures", "RuntimeProbe.apk");
    private static string UnimplementedFixturePath() => Path.Combine(AppContext.BaseDirectory, "Fixtures", "UnimplementedApiProbe.apk");

    private sealed class CountingWindow : IActivityWindow
    {
        public event EventHandler? Closed;
        public nint Handle => 0;
        public string Title => string.Empty;
        public bool IsClosed { get; private set; }
        public int ShowCount { get; private set; }
        public void SetTitle(string? title, CancellationToken cancellationToken) { }
        public void Show(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); ShowCount++; }
        public void Close() { if (IsClosed) return; IsClosed = true; Closed?.Invoke(this, EventArgs.Empty); }
        public void Dispose() => Close();
    }

    private sealed class PositiveLogSink : IAndroidLogSink
    {
        public int Info(AndroidLogEntry entry) => 1;
    }
}
