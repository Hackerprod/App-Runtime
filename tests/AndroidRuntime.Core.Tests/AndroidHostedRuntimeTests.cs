using System.Collections.Concurrent;
using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;
using AndroidRuntime.Core.Apk;
using AndroidRuntime.Core.Ui;

namespace AndroidRuntime.Core.Tests;

public sealed class AndroidHostedRuntimeTests
{
    [Fact]
    public void Real_exception_probe_executes_typed_handlers_unwind_rethrow_and_guest_failures()
    {
        LoadedApk apk = ApkLoader.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ExceptionProbe.apk"));
        DexFile dex = DexReader.Parse(apk.ClassesDexFiles[0]);
        var trace = new AndroidApiTraceBuffer(256);
        var context = new AndroidApiSessionContext("exception-session", "org.example.runtimeprobe", "Lorg/example/runtimeprobe/ExceptionProbe;", default, () => true, trace);
        var interpreter = new DexInterpreter(dex, AndroidApiBindings.CreateBuilder(new ActivityWindowPeers(), new FakeLogSink()).Build(), apiSession: context);
        const string owner = "Lorg/example/runtimeprobe/ExceptionProbe;";

        Assert.Equal(5, interpreter.InvokeStaticExact(owner, "exactCatch", "()I"));
        Assert.Equal(2, interpreter.InvokeStaticExact(owner, "superCatch", "()I"));
        Assert.Equal(13, interpreter.InvokeStaticExact(owner, "handlerOrder", "()I"));
        Assert.Equal(3, interpreter.InvokeStaticExact(owner, "errorVsException", "()I"));
        Assert.Equal(4, interpreter.InvokeStaticExact(owner, "throwableCatch", "()I"));
        Assert.Equal(5, interpreter.InvokeStaticExact(owner, "unwindThreeFrames", "()I"));
        Assert.Equal(4, interpreter.InvokeStaticExact(owner, "catchRethrow", "()I"));
        Assert.Equal(15, interpreter.InvokeStaticExact(owner, "rethrowIdentity", "()I"));
        Assert.Equal(16, interpreter.InvokeStaticExact(owner, "throwableToString", "()I"));
        Assert.Equal(7, interpreter.InvokeStaticExact(owner, "throwNull", "()I"));
        Assert.Equal(8, interpreter.InvokeStaticExact(owner, "divideInt", "(I)I", 0));
        Assert.Equal(9, interpreter.InvokeStaticExact(owner, "divideLong", "(J)I", 0L));
        Assert.Equal(10, interpreter.InvokeStaticExact(owner, "nullReceiver", "()I"));
        Assert.Equal(11, interpreter.InvokeStaticExact(owner, "arrayBounds", "(I)I", 2));
        Assert.Equal(12, interpreter.InvokeStaticExact(owner, "classCast", "()I"));
        Assert.Equal(17, interpreter.InvokeStaticExact(owner, "negativeArray", "(I)I", -1));
        Assert.Equal(18, interpreter.InvokeStaticExact(owner, "nullField", "()I"));
        Assert.Equal(19, interpreter.InvokeStaticExact(owner, "nullArray", "()I"));
        Assert.Throws<UncaughtAndroidGuestException>(() => interpreter.InvokeStaticExact(owner, "catchAllFinally", "()I"));
        UncaughtAndroidGuestException uncaught = Assert.Throws<UncaughtAndroidGuestException>(() => interpreter.InvokeStaticExact(owner, "uncaught", "()V"));
        Assert.Equal("Lorg/example/runtimeprobe/ExceptionProbe$CustomException;", uncaught.TypeDescriptor);
        Assert.Equal("sanitized", uncaught.GuestMessage);
        Assert.DoesNotContain("D:\\", uncaught.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(trace.Snapshot(), item => item.Kind == AndroidApiEventKind.GuestThrew && item.Invocation.SessionId == "exception-session");

        var lifecycle = new AndroidActivitySession(interpreter, new DexObject(owner));
        Assert.Throws<UncaughtAndroidGuestException>(lifecycle.Create);
        Assert.Equal(AndroidActivityState.Faulted, lifecycle.State);
    }

    [Theory]
    [InlineData("WideProbe.apk")]
    [InlineData("WideClockProbe.apk")]
    public async Task Real_wide_and_clock_probes_execute_long_double_arrays_fields_and_bindings(string fixture)
    {
        var hosted = await new AndroidAppRuntime().LaunchSessionAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture),
            new AndroidRuntimeServices(new FakeWindowFactory(), new FakeLogSink(), clock: new ProbeClock()));
        const long seed = 0x1122334455667788L;
        long expected = unchecked((seed + 5L) + ((seed + 5L) << 3) + 7L);
        Assert.Equal(seed, hosted.Session.Activity.InstanceFields["instanceWide"]);
        Assert.Equal(expected, hosted.Session.Activity.InstanceFields["wideValue"]);
        Assert.Equal(expected + 7_000_000_000L, hosted.Session.Activity.InstanceFields["bundleLong"]);
        Assert.Equal(1.75d, hosted.Session.Activity.InstanceFields["doubleValue"]);
        Assert.Equal(3, hosted.Session.Activity.InstanceFields["wideChecks"]);
        Assert.Equal(123_456_789L, hosted.Session.Activity.InstanceFields["constant32"]);
        Assert.Equal(6_000_000_006L, hosted.Session.Activity.InstanceFields["clockValue"]);
        await hosted.DisposeAsync();
    }
    [Fact]
    public async Task Real_apk_sets_window_title_logs_once_and_reaches_resumed_on_one_lane()
    {
        var windows = new FakeWindowFactory();
        var logs = new FakeLogSink();
        var bridge = new FakeViewBridge();
        var runtime = new AndroidAppRuntime();

        await using var hosted = await runtime.LaunchSessionAsync(
            FixturePath(),
            new AndroidRuntimeServices(windows, logs, traceCapacity: 128, viewBridgeFactory: (_, _, _) => bridge));

        Assert.Equal(AndroidActivityState.Resumed, hosted.Session.State);
        Assert.Equal(123, hosted.Session.Activity.InstanceFields["lifecycleState"]);
        Assert.Equal(1, hosted.Session.Activity.InstanceFields["createCount"]);
        Assert.Equal(1, hosted.Session.Activity.InstanceFields["startCount"]);
        Assert.Equal(1, hosted.Session.Activity.InstanceFields["resumeCount"]);
        var window = Assert.IsType<FakeWindow>(hosted.Window);
        Assert.Equal("RuntimeProbe DEX", window.Title);
        Assert.Equal(1, window.TitleSetCount);
        Assert.Equal(1, window.ShowCount);
        Assert.Equal("value=41true!", hosted.Session.Activity.InstanceFields["builtText"]);
        Assert.Equal("org.example.runtimeprobe", hosted.Session.Activity.InstanceFields["observedPackage"]);
        Assert.Equal("MainActivity", hosted.Session.Activity.InstanceFields["observedLocalClass"]);
        Assert.Equal("RuntimeProbe DEX", hosted.Session.Activity.InstanceFields["observedTitle"]);
        Assert.Equal("bundle", hosted.Session.Activity.InstanceFields["bundleText"]);
        Assert.Equal(7, hosted.Session.Activity.InstanceFields["bundleNumber"]);
        Assert.Equal(1, hosted.Session.Activity.InstanceFields["bundleFlag"]);
        Assert.Equal("android.intent.action.MAIN", hosted.Session.Activity.InstanceFields["intentAction"]);
        Assert.Equal(9, hosted.Session.Activity.InstanceFields["intentNumber"]);
        Assert.Equal(unchecked((int)0xffff8007), hosted.Session.Activity.InstanceFields["colorValue"]);
        Assert.Equal(6, logs.Entries.Count);
        Assert.All(logs.Entries, log => { Assert.Equal("RuntimeProbe", log.Tag); Assert.True(log.Result > 0); Assert.Equal(hosted.SessionId, log.SessionId); });
        Assert.Equal("value=41true!", bridge.ToastMadeText);
        Assert.Equal(1, bridge.ToastShowCount);

        var apiEvents = hosted.Trace.Snapshot()
            .Where(item => item.Invocation.ResolvedApi.MethodName is "setTitle" or "i")
            .ToArray();
        Assert.Equal(
            [AndroidApiEventKind.Requested, AndroidApiEventKind.Completed, AndroidApiEventKind.Requested, AndroidApiEventKind.Completed],
            apiEvents.Select(item => item.Kind));
        Assert.All(apiEvents, item => Assert.True(item.Invocation.IsMainLane));
        Assert.True(apiEvents.Zip(apiEvents.Skip(1), (left, right) => left.Invocation.Sequence <= right.Invocation.Sequence).All(value => value));
        var requested = apiEvents.Where(item => item.Kind == AndroidApiEventKind.Requested).ToArray();
        Assert.Equal(["setTitle", "i"], requested.Select(item => item.Invocation.ResolvedApi.MethodName));
        Assert.Equal("Lorg/example/runtimeprobe/MainActivity;", requested[0].Invocation.RequestedApi.ClassDescriptor);
        Assert.Equal("Landroid/app/Activity;", requested[0].Invocation.ResolvedApi.ClassDescriptor);
        Assert.Equal(AndroidInvokeKind.Virtual, requested[0].Invocation.InvokeKind);
        Assert.Equal(AndroidInvokeKind.Static, requested[1].Invocation.InvokeKind);
        Assert.Contains(hosted.Trace.Snapshot(), item => item.Kind == AndroidApiEventKind.Completed &&
            item.Invocation.RequestedApi.ClassDescriptor == "Lorg/example/runtimeprobe/MainActivity;" &&
            item.Invocation.ResolvedApi == new AndroidApiMethodId("Landroid/content/Context;", "getPackageName", "()Ljava/lang/String;"));
        Assert.Contains(hosted.Trace.Snapshot(), item => item.Kind == AndroidApiEventKind.Completed &&
            item.Invocation.RequestedApi.ClassDescriptor == "Landroid/os/Bundle;" &&
            item.Invocation.ResolvedApi.ClassDescriptor == "Landroid/os/BaseBundle;" &&
            item.Invocation.ResolvedApi.MethodName == "putString");
    }

    [Fact]
    public async Task Concurrent_sessions_keep_windows_and_trace_identity_isolated()
    {
        var runtime = new AndroidAppRuntime();
        var firstWindows = new FakeWindowFactory();
        var secondWindows = new FakeWindowFactory();
        var firstTask = runtime.LaunchSessionAsync(FixturePath(), new AndroidRuntimeServices(firstWindows, new FakeLogSink()));
        var secondTask = runtime.LaunchSessionAsync(FixturePath(), new AndroidRuntimeServices(secondWindows, new FakeLogSink()));

        await using var first = await firstTask;
        await using var second = await secondTask;

        Assert.NotSame(first.Window, second.Window);
        Assert.NotSame(first.Session.Activity.InstanceFields["applicationContext"], second.Session.Activity.InstanceFields["applicationContext"]);
        Assert.Equal("RuntimeProbe DEX", first.Window.Title);
        Assert.Equal("RuntimeProbe DEX", second.Window.Title);
        Assert.Single(first.Trace.Snapshot().Select(item => item.Invocation.SessionId).Distinct());
        Assert.Single(second.Trace.Snapshot().Select(item => item.Invocation.SessionId).Distinct());
        Assert.NotEqual(
            first.Trace.Snapshot()[0].Invocation.SessionId,
            second.Trace.Snapshot()[0].Invocation.SessionId);
    }

    [Fact]
    public async Task Shared_log_sink_attributes_entries_to_the_exact_session()
    {
        var logs = new FakeLogSink();
        var runtime = new AndroidAppRuntime();
        var firstTask = runtime.LaunchSessionAsync(FixturePath(), new AndroidRuntimeServices(new FakeWindowFactory(), logs));
        var secondTask = runtime.LaunchSessionAsync(FixturePath(), new AndroidRuntimeServices(new FakeWindowFactory(), logs));

        await using var first = await firstTask;
        await using var second = await secondTask;

        var entries = logs.Entries.ToArray();
        Assert.Equal(12, entries.Length);
        Assert.Equal(2, entries.Select(entry => entry.SessionId).Distinct().Count());
        Assert.Contains(entries, entry => entry.SessionId == first.SessionId);
        Assert.Contains(entries, entry => entry.SessionId == second.SessionId);
        Assert.All(entries, entry =>
        {
            Assert.Equal("org.example.runtimeprobe", entry.PackageName);
            Assert.Equal("Lorg/example/runtimeprobe/MainActivity;", entry.ActivityDescriptor);
            Assert.NotEqual(Guid.Empty, entry.InvocationId);
        });
    }

    [Fact]
    public async Task Closing_window_stops_execution_lane_and_dispose_remains_idempotent()
    {
        var runtime = new AndroidAppRuntime();
        var hosted = await runtime.LaunchSessionAsync(
            FixturePath(),
            new AndroidRuntimeServices(new FakeWindowFactory(), new FakeLogSink()));

        hosted.Window.Close();
        await hosted.Termination.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(hosted.IsTerminated);
        Assert.Equal(AndroidActivityState.Destroyed, hosted.Session.State);
        Assert.Equal(1, hosted.Session.Activity.InstanceFields["pauseCount"]);
        Assert.Equal(1, hosted.Session.Activity.InstanceFields["stopCount"]);
        Assert.Equal(1, hosted.Session.Activity.InstanceFields["destroyCount"]);
        await hosted.DisposeAsync();
        await hosted.DisposeAsync();
    }


    [Fact]
    public async Task Dispose_and_close_are_idempotent_and_title_after_close_is_unavailable()
    {
        var window = new FakeWindow();
        var peers = new ActivityWindowPeers();
        var activity = new DexObject("Lexample/MainActivity;");
        peers.Associate(activity, window);
        var registry = AndroidApiBindings.CreateBuilder(peers, new FakeLogSink()).Build();
        var trace = new AndroidApiTraceBuffer(16);
        var session = new AndroidApiSessionContext("s", "p", activity.TypeDescriptor, default, () => true, trace);
        window.Close();

        Assert.Throws<AndroidApiUnavailableException>(() => registry.Invoke(
            session,
            new AndroidApiCallSite(
                "Lexample/MainActivity;->onCreate(Landroid/os/Bundle;)V",
                4,
                AndroidApiBindings.SetTitle,
                AndroidApiBindings.SetTitle,
                AndroidInvokeKind.Virtual),
            [activity, "late title"]));
        Assert.Equal(AndroidApiEventKind.Failed, trace.Snapshot()[^1].Kind);

        var runtime = new AndroidAppRuntime();
        var hosted = await runtime.LaunchSessionAsync(
            FixturePath(),
            new AndroidRuntimeServices(new FakeWindowFactory(), new FakeLogSink()));
        await hosted.DisposeAsync();
        await hosted.DisposeAsync();
        Assert.True(hosted.Window.IsClosed);
        Assert.Equal(new AndroidPeerCounts(), hosted.PeerCounts);
    }

    [Fact]
    public void Log_sink_failure_is_traced_and_wrapped()
    {
        var trace = new AndroidApiTraceBuffer(8);
        var registry = AndroidApiBindings.CreateBuilder(new ActivityWindowPeers(), new ThrowingLogSink()).Build();
        var session = new AndroidApiSessionContext("s", "p", "La;", default, () => true, trace);

        var error = Assert.Throws<AndroidApiBindingException>(() => registry.Invoke(
            session,
            new AndroidApiCallSite("La;->call()V", 1, AndroidApiBindings.LogInfo, AndroidApiBindings.LogInfo, AndroidInvokeKind.Static),
            ["tag", "message"]));

        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Equal(AndroidApiEventKind.Failed, trace.Snapshot()[^1].Kind);
    }

    private static string FixturePath() => Path.Combine(AppContext.BaseDirectory, "Fixtures", "RuntimeProbe.apk");

    private sealed class FakeWindowFactory : IActivityWindowFactory
    {
        public IActivityWindow Create(string sessionId, string packageName, string activityDescriptor, CancellationToken cancellationToken) =>
            new FakeWindow();
    }

    private sealed class FakeWindow : IActivityWindow
    {
        private int _closed;
        public event EventHandler? Closed;
        public nint Handle => 1;
        public string Title { get; private set; } = string.Empty;
        public int TitleSetCount { get; private set; }
        public int ShowCount { get; private set; }
        public bool IsClosed => Volatile.Read(ref _closed) != 0;

        public void SetTitle(string? title, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsClosed) throw new InvalidOperationException("closed");
            Title = title ?? string.Empty;
            TitleSetCount++;
        }

        public void Show(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsClosed) throw new InvalidOperationException("closed");
            ShowCount++;
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            Closed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose() => Close();
    }

    private sealed class FakeLogSink : IAndroidLogSink
    {
        public ConcurrentQueue<(string SessionId, string PackageName, string ActivityDescriptor, Guid InvocationId, string? Tag, string Message, int Result)> Entries { get; } = new();

        public int Info(AndroidLogEntry entry)
        {
            int result = Math.Max(1, (entry.Tag?.Length ?? 0) + entry.Message.Length);
            Entries.Enqueue((entry.SessionId, entry.PackageName, entry.ActivityDescriptor, entry.Invocation.InvocationId, entry.Tag, entry.Message, result));
            return result;
        }
    }
    private sealed class ProbeClock : IAndroidClock
    {
        public long UptimeMillis() => 1_000_000_001L;
        public long ElapsedRealtime() => 2_000_000_002L;
        public long ElapsedRealtimeNanos() => 3_000_000_003L;
    }

    private sealed class ThrowingLogSink : IAndroidLogSink
    {
        public int Info(AndroidLogEntry entry) => throw new InvalidOperationException("sink failed");
    }

    private sealed class FakeViewBridge : IAndroidViewBridge
    {
        public string? ToastMadeText { get; private set; }
        public int ToastShowCount { get; private set; }
        public bool IsAvailable => true;
        public event Action? FrameRequested { add { } remove { } }
        public void DisposeBridge() { }
        public void AttachSession(DexInterpreter interpreter, DexObject activity, Func<Func<object?>, object?> dispatchToLane) { }
        public void SetContentView(int layoutResourceId) { }
        public DexObject Inflate(int layoutResourceId) => new("Landroid/view/View;");
        public DexObject? FindViewById(int id, DexObject? receiver = null) => null;
        public int GetId(DexObject view) => 0;
        public void SetEnabled(DexObject view, bool enabled) { }
        public bool IsEnabled(DexObject view) => true;
        public void SetVisibility(DexObject view, int visibility) { }
        public int GetVisibility(DexObject view) => 0;
        public void SetPressed(DexObject view, bool pressed) { }
        public void SetHovered(DexObject view, bool hovered) { }
        public void SetScrollOffset(DexObject view, float x, float y) { }
        public void SetOnClickListener(DexObject view, DexObject? listener) { }
        public bool PerformClick(DexObject view) => false;
        public void SetText(DexObject view, string? text) { }
        public string GetText(DexObject view) => string.Empty;
        public bool IsLaidOut(DexObject view) => true;
        public int GetPaddingLeft(DexObject view) => 0;
        public int GetPaddingTop(DexObject view) => 0;
        public int GetPaddingRight(DexObject view) => 0;
        public int GetPaddingBottom(DexObject view) => 0;
        public DexObject ObtainStyledAttributes() => new("Landroid/content/res/TypedArray;");
        public int TypedArrayGetIndexCount() => 0;
        public bool TypedArrayHasValue(int index) => false;
        public string? TypedArrayGetString(int index) => null;
        public int TypedArrayGetColor(int index, int defaultValue) => defaultValue;
        public DexObject? TypedArrayGetColorStateList(int index) => null;
        public float TypedArrayGetDimension(int index, float defaultValue) => defaultValue;
        public int TypedArrayGetInt(int index, int defaultValue) => defaultValue;
        public int TypedArrayGetResourceId(int index, int defaultValue) => defaultValue;
        public bool TypedArrayGetBoolean(int index, bool defaultValue) => defaultValue;
        public float TypedArrayGetFloat(int index, float defaultValue) => defaultValue;
        public int TypedArrayGetDimensionPixelSize(int index, int defaultValue) => defaultValue;
        public int TypedArrayGetDimensionPixelOffset(int index, int defaultValue) => defaultValue;
        public int TypedArrayGetIndex(int index) => 0;
        public bool TypedArrayGetValue(int index) => false;
        public byte[]? RenderFrame(int pixelWidth, int pixelHeight, float density) => null;
        public int? HitTest(float pixelX, float pixelY) => null;
        public void ToastMakeText(string? text, int duration) => ToastMadeText = text;
        public void ToastSetText(string? text) => ToastMadeText = text;
        public void ToastSetDuration(int duration) { }
        public int ToastGetDuration() => 0;
        public void ToastShow() => ToastShowCount++;
        public void ToastCancel() { }
        public bool ToastIsActive() => false;
        public void ToastRender() { }
        public void DispatchTouch(int action, float x, float y) { }
        public void DispatchKey(int action, int keyCode) { }
        public int GesturePoll() => 0;
        public bool GestureActive => false;
    }
}
