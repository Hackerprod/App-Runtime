using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;
using AndroidRuntime.Core.Ui;

namespace AndroidRuntime.Core.Tests;

public sealed class AndroidFrameworkBindingTests
{
    [Fact]
    public void Object_constructor_is_a_noop_for_plain_dex_classes_that_extend_object()
    {
        var state = new AndroidFrameworkState("s", "p", "Lutil/Plain;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        Assert.True(registry.Contains(new("Ljava/lang/Object;", "<init>", "()V")));

        // A plain DEX class extending java.lang.Object whose <init> calls super()
        // (invoke-direct Object.<init>) must construct without throwing.
        var dex = new DexFile();
        dex.Methods.Add(new DexMethodRef { ClassDescriptor = "Ljava/lang/Object;", Name = "<init>", Proto = new DexProto { Shorty = "V", ReturnType = "V", ParameterTypes = [] } });
        dex.Classes.Add(new DexClass
        {
            Descriptor = "Lutil/Plain;",
            SuperclassDescriptor = "Ljava/lang/Object;",
            DirectMethods =
            {
                new DexEncodedMethod
                {
                    Method = new DexMethodRef { ClassDescriptor = "Lutil/Plain;", Name = "<init>", Proto = new DexProto { Shorty = "V", ReturnType = "V", ParameterTypes = [] } },
                    Code = new DexCodeItem { RegistersSize = 1, InsSize = 1, OutsSize = 1, Instructions = [0x1070, 0x0000, 0x0000, 0x000e] }
                }
            }
        });
        dex.BuildIndexes();
        var interpreter = new DexInterpreter(dex, registry);

        var instance = interpreter.ConstructInstance("Lutil/Plain;");

        Assert.Equal("Lutil/Plain;", instance.TypeDescriptor);
    }

    [Fact]
    public void SystemClock_and_bundle_long_preserve_values_above_32_bits()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers(), clock: new FixedClock());
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        Assert.Equal(5_000_000_001L, Invoke(registry, state, "Landroid/os/SystemClock;", "uptimeMillis", "()J", AndroidInvokeKind.Static));
        Assert.Equal(6_000_000_002L, Invoke(registry, state, "Landroid/os/SystemClock;", "elapsedRealtime", "()J", AndroidInvokeKind.Static));
        Assert.Equal(7_000_000_003L, Invoke(registry, state, "Landroid/os/SystemClock;", "elapsedRealtimeNanos", "()J", AndroidInvokeKind.Static));
        var bundle = new DexObject("Landroid/os/Bundle;");
        Invoke(registry, state, "Landroid/os/Bundle;", "<init>", "()V", AndroidInvokeKind.Direct, bundle);
        Invoke(registry, state, "Landroid/os/BaseBundle;", "putLong", "(Ljava/lang/String;J)V", AndroidInvokeKind.Virtual, bundle, "wide", 9_000_000_004L);
        Assert.Equal(9_000_000_004L, Invoke(registry, state, "Landroid/os/BaseBundle;", "getLong", "(Ljava/lang/String;)J", AndroidInvokeKind.Virtual, bundle, "wide"));
        Assert.Equal(8_000_000_005L, Invoke(registry, state, "Landroid/os/BaseBundle;", "getLong", "(Ljava/lang/String;J)J", AndroidInvokeKind.Virtual, bundle, "missing", 8_000_000_005L));
    }
    [Theory]
    [InlineData("ABC", "abc", 1)]
    [InlineData("I", "\u0131", 1)]
    [InlineData("\u0130", "i", 1)]
    [InlineData("\u212A", "k", 1)]
    [InlineData("\u017F", "s", 1)]
    [InlineData("\u00B5", "\u039C", 1)]
    [InlineData("\u2C2F", "\u2C5F", 0)]
    [InlineData("\uA7C0", "\uA7C1", 0)]
    [InlineData("short", "longer", 0)]
    [InlineData("\U00010400", "\U00010428", 1)]
    [InlineData("\U00010570", "\U00010597", 0)]
    [InlineData("\U0001F600", "\U0001F600", 1)]
    public void String_equals_ignore_case_matches_java_utf16_char_semantics(string left, string right, int expected)
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        Assert.Equal(expected, Invoke(registry, state, "Ljava/lang/String;", "equalsIgnoreCase", "(Ljava/lang/String;)Z", AndroidInvokeKind.Virtual, left, right));
    }

    [Fact]
    public void String_equals_ignore_case_matches_java_for_malformed_utf16()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        int Equals(string left, string right) => (int)Invoke(registry, state, "Ljava/lang/String;", "equalsIgnoreCase", "(Ljava/lang/String;)Z", AndroidInvokeKind.Virtual, left, right)!;
        string high1 = new([(char)0xD801]), high2 = new([(char)0xD802]);
        string low1 = new([(char)0xDC00]), low2 = new([(char)0xDC01]);
        Assert.Equal(1, Equals(high1, high1));
        Assert.Equal(0, Equals(high1, high2));
        Assert.Equal(1, Equals(high1 + "A", high1 + "a"));
        Assert.Equal(0, Equals(new([(char)0xD801, (char)0xDC00]), high1 + "A"));
        Assert.Equal(1, Equals(low1, low1));
        Assert.Equal(0, Equals(low1, low2));
    }

    [Fact]
    public void Activity_context_text_string_builder_and_color_bindings_have_exact_semantics()
    {
        var peers = new ActivityWindowPeers();
        var activity = new DexObject("Lorg/example/runtimeprobe/MainActivity;");
        var window = new InMemoryActivityWindow();
        window.SetTitle("Probe", default);
        peers.Associate(activity, window);
        var state = new AndroidFrameworkState("s", "org.example.runtimeprobe", activity.TypeDescriptor, peers);
        state.AttachActivity(activity);
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();

        Assert.Equal("Probe", Invoke(registry, state, "Landroid/app/Activity;", "getTitle", "()Ljava/lang/CharSequence;", AndroidInvokeKind.Virtual, activity));
        Assert.Equal("MainActivity", Invoke(registry, state, "Landroid/app/Activity;", "getLocalClassName", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, activity));
        Assert.Equal("org.example.runtimeprobe", Invoke(registry, state, "Landroid/content/Context;", "getPackageName", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, activity));
        Assert.Same(state.ApplicationContext, Invoke(registry, state, "Landroid/content/Context;", "getApplicationContext", "()Landroid/content/Context;", AndroidInvokeKind.Virtual, activity));
        Assert.NotSame(activity, state.ApplicationContext);
        Assert.Equal(0, Invoke(registry, state, "Landroid/app/Activity;", "isFinishing", "()Z", AndroidInvokeKind.Virtual, activity));
        Invoke(registry, state, "Landroid/app/Activity;", "finish", "()V", AndroidInvokeKind.Virtual, activity);
        Invoke(registry, state, "Landroid/app/Activity;", "finish", "()V", AndroidInvokeKind.Virtual, activity);
        Assert.Equal(1, Invoke(registry, state, "Landroid/app/Activity;", "isFinishing", "()Z", AndroidInvokeKind.Virtual, activity));
        Assert.Equal(0, Invoke(registry, state, "Landroid/app/Activity;", "isDestroyed", "()Z", AndroidInvokeKind.Virtual, activity));

        Assert.Equal(1, Invoke(registry, state, "Landroid/text/TextUtils;", "isEmpty", "(Ljava/lang/CharSequence;)Z", AndroidInvokeKind.Static, ""));
        Assert.Equal(1, Invoke(registry, state, "Ljava/lang/String;", "equalsIgnoreCase", "(Ljava/lang/String;)Z", AndroidInvokeKind.Virtual, "AbC", "aBc"));
        Assert.Equal(96354, Invoke(registry, state, "Ljava/lang/String;", "hashCode", "()I", AndroidInvokeKind.Virtual, "abc"));
        Assert.Equal("true", Invoke(registry, state, "Ljava/lang/String;", "valueOf", "(Z)Ljava/lang/String;", AndroidInvokeKind.Static, 1));

        var builder = new DexObject("Ljava/lang/StringBuilder;");
        Invoke(registry, state, "Ljava/lang/StringBuilder;", "<init>", "()V", AndroidInvokeKind.Direct, builder);
        Invoke(registry, state, "Ljava/lang/StringBuilder;", "append", "(Ljava/lang/String;)Ljava/lang/StringBuilder;", AndroidInvokeKind.Virtual, builder, "v=");
        Invoke(registry, state, "Ljava/lang/StringBuilder;", "append", "(I)Ljava/lang/StringBuilder;", AndroidInvokeKind.Virtual, builder, 41);
        Assert.Equal("v=41", Invoke(registry, state, "Ljava/lang/StringBuilder;", "toString", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, builder));

        int color = (int)Invoke(registry, state, "Landroid/graphics/Color;", "argb", "(IIII)I", AndroidInvokeKind.Static, 300, -1, 128, 7)!;
        Assert.Equal(unchecked((int)0xffff8007), color);
        Assert.Equal(255, Invoke(registry, state, "Landroid/graphics/Color;", "alpha", "(I)I", AndroidInvokeKind.Static, color));
    }

    [Fact]
    public void Bundle_and_intent_peers_preserve_null_absence_types_and_session_identity()
    {
        var state = new AndroidFrameworkState("s", "org.example", "Lorg/example/Main;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        var bundle = new DexObject("Landroid/os/Bundle;");
        Invoke(registry, state, "Landroid/os/Bundle;", "<init>", "()V", AndroidInvokeKind.Direct, bundle);
        Invoke(registry, state, "Landroid/os/BaseBundle;", "putString", "(Ljava/lang/String;Ljava/lang/String;)V", AndroidInvokeKind.Virtual, bundle, null!, null!);
        Assert.Equal("fallback", Invoke(registry, state, "Landroid/os/BaseBundle;", "getString", "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;", AndroidInvokeKind.Virtual, bundle, null!, "fallback"));
        Invoke(registry, state, "Landroid/os/BaseBundle;", "putInt", "(Ljava/lang/String;I)V", AndroidInvokeKind.Virtual, bundle, "count", 7);
        Assert.Equal(1, Invoke(registry, state, "Landroid/os/BaseBundle;", "containsKey", "(Ljava/lang/String;)Z", AndroidInvokeKind.Virtual, bundle, null!));
        Assert.Null(Invoke(registry, state, "Landroid/os/BaseBundle;", "getString", "(Ljava/lang/String;)Ljava/lang/String;", AndroidInvokeKind.Virtual, bundle, null!));
        Assert.Equal(0, Invoke(registry, state, "Landroid/os/BaseBundle;", "getBoolean", "(Ljava/lang/String;)Z", AndroidInvokeKind.Virtual, bundle, "count"));

        var activity = new DexObject("Lorg/example/Main;");
        state.AttachActivity(activity);
        Assert.Same(state.LauncherIntent, Invoke(registry, state, "Landroid/app/Activity;", "getIntent", "()Landroid/content/Intent;", AndroidInvokeKind.Virtual, activity));
        Assert.Equal("android.intent.action.MAIN", Invoke(registry, state, "Landroid/content/Intent;", "getAction", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, state.LauncherIntent));
        Invoke(registry, state, "Landroid/content/Intent;", "putExtra", "(Ljava/lang/String;I)Landroid/content/Intent;", AndroidInvokeKind.Virtual, state.LauncherIntent, "n", 9);
        Assert.Equal(9, Invoke(registry, state, "Landroid/content/Intent;", "getIntExtra", "(Ljava/lang/String;I)I", AndroidInvokeKind.Virtual, state.LauncherIntent, "n", -1));

        var otherState = new AndroidFrameworkState("other", "org.example", "Lorg/example/Main;", new ActivityWindowPeers());
        Assert.NotSame(state.ApplicationContext, otherState.ApplicationContext);
        Assert.NotSame(state.LauncherIntent, otherState.LauncherIntent);
    }

    [Fact]
    public void Log_levels_are_finite_attributed_and_filtered_by_configured_priority()
    {
        var sink = new RecordingLogSink();
        var state = new AndroidFrameworkState("session", "org.example", "La;", new ActivityWindowPeers(), minimumLogPriority: 4);
        var registry = AndroidApiBindings.CreateBuilder(state, sink).Build();

        Assert.Equal(0, Invoke(registry, state, "Landroid/util/Log;", "isLoggable", "(Ljava/lang/String;I)Z", AndroidInvokeKind.Static, "tag", 3));
        Assert.Equal(1, Invoke(registry, state, "Landroid/util/Log;", "isLoggable", "(Ljava/lang/String;I)Z", AndroidInvokeKind.Static, "tag", 4));
        Assert.True((int)Invoke(registry, state, "Landroid/util/Log;", "w", "(Ljava/lang/String;Ljava/lang/String;)I", AndroidInvokeKind.Static, "tag", "message")! > 0);
        var entry = Assert.Single(sink.Entries);
        Assert.Equal("session", entry.SessionId);
        Assert.Equal(5, entry.Priority);
        Assert.Equal("W", entry.Level);
    }

    [Fact]
    public void Text_toast_relays_to_the_view_bridge_for_show_cancel_and_mutation()
    {
        var peers = new ActivityWindowPeers();
        var activity = new DexObject("Lorg/example/Main;");
        var window = new InMemoryActivityWindow();
        peers.Associate(activity, window);
        var bridge = new RecordingToastBridge();
        var state = new AndroidFrameworkState("s", "org.example", activity.TypeDescriptor, peers, viewBridge: bridge);
        state.AttachActivity(activity);
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();

        var toast = Assert.IsType<DexObject>(Invoke(registry, state, "Landroid/widget/Toast;", "makeText", "(Landroid/content/Context;Ljava/lang/CharSequence;I)Landroid/widget/Toast;", AndroidInvokeKind.Static, activity, "Hello", 0));
        Assert.Equal("Hello", bridge.MadeText);
        Invoke(registry, state, "Landroid/widget/Toast;", "setText", "(Ljava/lang/CharSequence;)V", AndroidInvokeKind.Virtual, toast, "Changed");
        Assert.Equal("Changed", bridge.MadeText);
        Invoke(registry, state, "Landroid/widget/Toast;", "show", "()V", AndroidInvokeKind.Virtual, toast);
        Invoke(registry, state, "Landroid/widget/Toast;", "cancel", "()V", AndroidInvokeKind.Virtual, toast);
        Assert.Equal(1, bridge.ShowCount);
        Assert.Equal(1, bridge.CancelCount);
    }

    private sealed class RecordingToastBridge : IAndroidViewBridge
    {
        public string? MadeText { get; private set; }
        public int ShowCount { get; private set; }
        public int CancelCount { get; private set; }
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
        public void ToastMakeText(string? text, int duration) => MadeText = text;
        public void ToastSetText(string? text) => MadeText = text;
        public void ToastSetDuration(int duration) { }
        public int ToastGetDuration() => 0;
        public void ToastShow() => ShowCount++;
        public void ToastCancel() => CancelCount++;
        public bool ToastIsActive() => false;
        public void ToastRender() { }
        public void DispatchTouch(int action, float x, float y) { }
        public void DispatchKey(int action, int keyCode) { }
        public int GesturePoll() => 0;
        public bool GestureActive => false;
    }

    [Fact]
    public void Atomic_reference_init_get_set_and_compare_and_set_round_trip()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        var reference = new DexObject("Ljava/util/concurrent/atomic/AtomicReference;");
        var value = new DexObject("Ljava/lang/Object;");
        var other = new DexObject("Ljava/lang/Object;");

        Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicReference;", "<init>", "()V", AndroidInvokeKind.Direct, reference);
        Assert.Null(Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicReference;", "get", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, reference));
        Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicReference;", "set", "(Ljava/lang/Object;)V", AndroidInvokeKind.Virtual, reference, value);
        Assert.Same(value, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicReference;", "get", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, reference));

        // CAS with a different expected instance fails and does not swap.
        Assert.Equal(0, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicReference;", "compareAndSet", "(Ljava/lang/Object;Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, reference, other, other));
        Assert.Same(value, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicReference;", "get", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, reference));
        // CAS with the exact current instance succeeds and swaps.
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicReference;", "compareAndSet", "(Ljava/lang/Object;Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, reference, value, other));
        Assert.Same(other, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicReference;", "get", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, reference));

        // Initial-value constructor holds the given object; CAS treats null == null.
        var seeded = new DexObject("Ljava/util/concurrent/atomic/AtomicReference;");
        Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicReference;", "<init>", "(Ljava/lang/Object;)V", AndroidInvokeKind.Direct, seeded, value);
        Assert.Same(value, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicReference;", "get", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, seeded));
        var fresh = new DexObject("Ljava/util/concurrent/atomic/AtomicReference;");
        Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicReference;", "<init>", "()V", AndroidInvokeKind.Direct, fresh);
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicReference;", "compareAndSet", "(Ljava/lang/Object;Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, fresh, (object)null!, other));
        Assert.Same(other, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicReference;", "get", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, fresh));
    }

    [Fact]
    public void Atomic_reference_peer_quota_fails_closed()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers(), peerLimits: new AndroidPeerLimits(maxAtomicReferences: 1));
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicReference;", "<init>", "()V", AndroidInvokeKind.Direct, new DexObject("Ljava/util/concurrent/atomic/AtomicReference;"));

        Assert.Throws<AndroidPeerQuotaExceededException>(() => Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicReference;", "<init>", "()V", AndroidInvokeKind.Direct, new DexObject("Ljava/util/concurrent/atomic/AtomicReference;")));
    }

    [Fact]
    public void Weak_hash_map_put_get_contains_remove_and_size_round_trip()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        var map = new DexObject("Ljava/util/WeakHashMap;");
        Invoke(registry, state, "Ljava/util/WeakHashMap;", "<init>", "()V", AndroidInvokeKind.Direct, map);

        // String keys use CLR value equality (matches String.equals).
        Assert.Null(Invoke(registry, state, "Ljava/util/WeakHashMap;", "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "key", "first"));
        Assert.Equal("first", Invoke(registry, state, "Ljava/util/WeakHashMap;", "get", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "key"));
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/WeakHashMap;", "containsKey", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, map, "key"));
        Assert.Equal(0, Invoke(registry, state, "Ljava/util/WeakHashMap;", "containsKey", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, map, "missing"));
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/WeakHashMap;", "size", "()I", AndroidInvokeKind.Virtual, map));
        Assert.Equal("first", Invoke(registry, state, "Ljava/util/WeakHashMap;", "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "key", "second"));
        Assert.Equal("second", Invoke(registry, state, "Ljava/util/WeakHashMap;", "get", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "key"));
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/WeakHashMap;", "size", "()I", AndroidInvokeKind.Virtual, map));
        Assert.Equal("second", Invoke(registry, state, "Ljava/util/WeakHashMap;", "remove", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "key"));
        Assert.Null(Invoke(registry, state, "Ljava/util/WeakHashMap;", "get", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "key"));
        Assert.Equal(0, Invoke(registry, state, "Ljava/util/WeakHashMap;", "containsKey", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, map, "key"));
        Assert.Equal(0, Invoke(registry, state, "Ljava/util/WeakHashMap;", "size", "()I", AndroidInvokeKind.Virtual, map));

        // DexObject keys use CLR reference equality (= default Object.equals): the
        // same instance finds the entry, a different instance does not.
        var key = new DexObject("Ljava/lang/Object;");
        var value = new DexObject("Ljava/lang/Object;");
        var other = new DexObject("Ljava/lang/Object;");
        Assert.Null(Invoke(registry, state, "Ljava/util/WeakHashMap;", "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, key, value));
        Assert.Same(value, Invoke(registry, state, "Ljava/util/WeakHashMap;", "get", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, key));
        Assert.Null(Invoke(registry, state, "Ljava/util/WeakHashMap;", "get", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, other));
    }

    [Fact]
    public void Weak_hash_map_peer_quota_fails_closed()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers(), peerLimits: new AndroidPeerLimits(maxWeakHashMaps: 1));
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        Invoke(registry, state, "Ljava/util/WeakHashMap;", "<init>", "()V", AndroidInvokeKind.Direct, new DexObject("Ljava/util/WeakHashMap;"));

        Assert.Throws<AndroidPeerQuotaExceededException>(() => Invoke(registry, state, "Ljava/util/WeakHashMap;", "<init>", "()V", AndroidInvokeKind.Direct, new DexObject("Ljava/util/WeakHashMap;")));
    }

    [Fact]
    public void Hash_map_put_get_contains_remove_size_and_null_key_round_trip()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        var map = new DexObject("Ljava/util/HashMap;");
        Invoke(registry, state, "Ljava/util/HashMap;", "<init>", "()V", AndroidInvokeKind.Direct, map);

        // String keys use CLR value equality.
        Assert.Null(Invoke(registry, state, "Ljava/util/HashMap;", "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "key", "first"));
        Assert.Equal("first", Invoke(registry, state, "Ljava/util/HashMap;", "get", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "key"));
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/HashMap;", "containsKey", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, map, "key"));
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/HashMap;", "size", "()I", AndroidInvokeKind.Virtual, map));
        Assert.Equal("first", Invoke(registry, state, "Ljava/util/HashMap;", "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "key", "second"));
        Assert.Equal("second", Invoke(registry, state, "Ljava/util/HashMap;", "get", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "key"));
        Assert.Equal("second", Invoke(registry, state, "Ljava/util/HashMap;", "remove", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "key"));
        Assert.Equal(0, Invoke(registry, state, "Ljava/util/HashMap;", "containsKey", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, map, "key"));
        Assert.Equal(0, Invoke(registry, state, "Ljava/util/HashMap;", "size", "()I", AndroidInvokeKind.Virtual, map));

        // HashMap allows one null key (WeakHashMap conceptually does not).
        Assert.Null(Invoke(registry, state, "Ljava/util/HashMap;", "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, (object)null!, "nullValue"));
        Assert.Equal("nullValue", Invoke(registry, state, "Ljava/util/HashMap;", "get", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, (object)null!));
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/HashMap;", "containsKey", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, map, (object)null!));
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/HashMap;", "size", "()I", AndroidInvokeKind.Virtual, map));
        Assert.Equal("nullValue", Invoke(registry, state, "Ljava/util/HashMap;", "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, (object)null!, "nullNext"));
        Assert.Equal("nullNext", Invoke(registry, state, "Ljava/util/HashMap;", "get", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, (object)null!));
        Assert.Equal("nullNext", Invoke(registry, state, "Ljava/util/HashMap;", "remove", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, (object)null!));
        Assert.Equal(0, Invoke(registry, state, "Ljava/util/HashMap;", "size", "()I", AndroidInvokeKind.Virtual, map));
    }

    [Fact]
    public void Hash_map_peer_quota_fails_closed()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers(), peerLimits: new AndroidPeerLimits(maxHashMaps: 1));
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        Invoke(registry, state, "Ljava/util/HashMap;", "<init>", "()V", AndroidInvokeKind.Direct, new DexObject("Ljava/util/HashMap;"));

        Assert.Throws<AndroidPeerQuotaExceededException>(() => Invoke(registry, state, "Ljava/util/HashMap;", "<init>", "()V", AndroidInvokeKind.Direct, new DexObject("Ljava/util/HashMap;")));
    }

    [Fact]
    public void Array_list_add_get_size_is_empty_remove_and_shift_round_trip()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        var list = new DexObject("Ljava/util/ArrayList;");
        Invoke(registry, state, "Ljava/util/ArrayList;", "<init>", "()V", AndroidInvokeKind.Direct, list);

        Assert.Equal(1, Invoke(registry, state, "Ljava/util/ArrayList;", "isEmpty", "()Z", AndroidInvokeKind.Virtual, list));
        Assert.Equal(0, Invoke(registry, state, "Ljava/util/ArrayList;", "size", "()I", AndroidInvokeKind.Virtual, list));
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/ArrayList;", "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, "a"));
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/ArrayList;", "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, "b"));
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/ArrayList;", "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, "c"));
        Assert.Equal(0, Invoke(registry, state, "Ljava/util/ArrayList;", "isEmpty", "()Z", AndroidInvokeKind.Virtual, list));
        Assert.Equal(3, Invoke(registry, state, "Ljava/util/ArrayList;", "size", "()I", AndroidInvokeKind.Virtual, list));
        Assert.Equal("a", Invoke(registry, state, "Ljava/util/ArrayList;", "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 0));
        Assert.Equal("c", Invoke(registry, state, "Ljava/util/ArrayList;", "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 2));
        // Remove by index returns the element and shifts the tail.
        Assert.Equal("b", Invoke(registry, state, "Ljava/util/ArrayList;", "remove", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 1));
        Assert.Equal(2, Invoke(registry, state, "Ljava/util/ArrayList;", "size", "()I", AndroidInvokeKind.Virtual, list));
        Assert.Equal("c", Invoke(registry, state, "Ljava/util/ArrayList;", "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 1));
        // Null elements are allowed (real ArrayList allows them).
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/ArrayList;", "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, (object)null!));
        Assert.Null(Invoke(registry, state, "Ljava/util/ArrayList;", "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 2));
    }

    [Fact]
    public void Array_list_capacity_constructor_is_accepted_and_ignored()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        var list = new DexObject("Ljava/util/ArrayList;");
        Invoke(registry, state, "Ljava/util/ArrayList;", "<init>", "(I)V", AndroidInvokeKind.Direct, list, 16);

        Assert.Equal(0, Invoke(registry, state, "Ljava/util/ArrayList;", "size", "()I", AndroidInvokeKind.Virtual, list));
    }

    [Fact]
    public void Array_list_out_of_range_get_and_remove_throw_guest_index_out_of_bounds()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        var list = new DexObject("Ljava/util/ArrayList;");
        Invoke(registry, state, "Ljava/util/ArrayList;", "<init>", "()V", AndroidInvokeKind.Direct, list);
        Invoke(registry, state, "Ljava/util/ArrayList;", "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, "only");

        var getError = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, "Ljava/util/ArrayList;", "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 1));
        Assert.Equal("Ljava/lang/IndexOutOfBoundsException;", getError.Throwable.TypeDescriptor);
        var negativeError = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, "Ljava/util/ArrayList;", "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, -1));
        Assert.Equal("Ljava/lang/IndexOutOfBoundsException;", negativeError.Throwable.TypeDescriptor);
        var removeError = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, "Ljava/util/ArrayList;", "remove", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 5));
        Assert.Equal("Ljava/lang/IndexOutOfBoundsException;", removeError.Throwable.TypeDescriptor);
    }

    [Fact]
    public void Array_list_peer_quota_fails_closed()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers(), peerLimits: new AndroidPeerLimits(maxArrayLists: 1));
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        Invoke(registry, state, "Ljava/util/ArrayList;", "<init>", "()V", AndroidInvokeKind.Direct, new DexObject("Ljava/util/ArrayList;"));

        Assert.Throws<AndroidPeerQuotaExceededException>(() => Invoke(registry, state, "Ljava/util/ArrayList;", "<init>", "()V", AndroidInvokeKind.Direct, new DexObject("Ljava/util/ArrayList;")));
    }

    [Fact]
    public void Weak_reference_init_get_and_clear_round_trip()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        var reference = new DexObject("Ljava/lang/ref/WeakReference;");
        var referent = new DexObject("Ljava/lang/Object;");

        Invoke(registry, state, "Ljava/lang/ref/WeakReference;", "<init>", "(Ljava/lang/Object;)V", AndroidInvokeKind.Direct, reference, referent);
        Assert.Same(referent, Invoke(registry, state, "Ljava/lang/ref/WeakReference;", "get", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, reference));
        Invoke(registry, state, "Ljava/lang/ref/WeakReference;", "clear", "()V", AndroidInvokeKind.Virtual, reference);
        Assert.Null(Invoke(registry, state, "Ljava/lang/ref/WeakReference;", "get", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, reference));
    }

    [Fact]
    public void Weak_reference_peer_quota_fails_closed()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers(), peerLimits: new AndroidPeerLimits(maxWeakReferences: 1));
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        Invoke(registry, state, "Ljava/lang/ref/WeakReference;", "<init>", "(Ljava/lang/Object;)V", AndroidInvokeKind.Direct, new DexObject("Ljava/lang/ref/WeakReference;"), new DexObject("Ljava/lang/Object;"));

        Assert.Throws<AndroidPeerQuotaExceededException>(() => Invoke(registry, state, "Ljava/lang/ref/WeakReference;", "<init>", "(Ljava/lang/Object;)V", AndroidInvokeKind.Direct, new DexObject("Ljava/lang/ref/WeakReference;"), new DexObject("Ljava/lang/Object;")));
    }

    [Fact]
    public void Copy_on_write_array_set_init_and_add_dedupe()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        var set = new DexObject("Ljava/util/concurrent/CopyOnWriteArraySet;");
        Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArraySet;", "<init>", "()V", AndroidInvokeKind.Direct, set);

        // Real Set semantics: add returns 1 for a new element, 0 for a duplicate.
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArraySet;", "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, set, "element"));
        Assert.Equal(0, Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArraySet;", "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, set, "element"));
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArraySet;", "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, set, "other"));
        // DexObject reference identity: same instance dedups, a different instance does not.
        var key = new DexObject("Ljava/lang/Object;");
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArraySet;", "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, set, key));
        Assert.Equal(0, Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArraySet;", "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, set, key));
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArraySet;", "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, set, new DexObject("Ljava/lang/Object;")));
    }

    [Fact]
    public void Copy_on_write_array_set_peer_quota_fails_closed()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers(), peerLimits: new AndroidPeerLimits(maxCopyOnWriteArraySets: 1));
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArraySet;", "<init>", "()V", AndroidInvokeKind.Direct, new DexObject("Ljava/util/concurrent/CopyOnWriteArraySet;"));

        Assert.Throws<AndroidPeerQuotaExceededException>(() => Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArraySet;", "<init>", "()V", AndroidInvokeKind.Direct, new DexObject("Ljava/util/concurrent/CopyOnWriteArraySet;")));
    }

    [Fact]
    public void Iterator_snapshots_the_source_at_creation_time()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        var list = new DexObject("Ljava/util/ArrayList;");
        Invoke(registry, state, "Ljava/util/ArrayList;", "<init>", "()V", AndroidInvokeKind.Direct, list);
        Invoke(registry, state, "Ljava/util/ArrayList;", "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, "a");
        Invoke(registry, state, "Ljava/util/ArrayList;", "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, "b");
        var iterator = (DexObject)Invoke(registry, state, "Ljava/util/ArrayList;", "iterator", "()Ljava/util/Iterator;", AndroidInvokeKind.Virtual, list)!;
        // Mutate the source after obtaining the iterator — the snapshot must not see it.
        Invoke(registry, state, "Ljava/util/ArrayList;", "clear", "()V", AndroidInvokeKind.Virtual, list);

        Assert.Equal(1, Invoke(registry, state, "Ljava/util/Iterator;", "hasNext", "()Z", AndroidInvokeKind.Interface, iterator));
        Assert.Equal("a", Invoke(registry, state, "Ljava/util/Iterator;", "next", "()Ljava/lang/Object;", AndroidInvokeKind.Interface, iterator));
        Assert.Equal("b", Invoke(registry, state, "Ljava/util/Iterator;", "next", "()Ljava/lang/Object;", AndroidInvokeKind.Interface, iterator));
        Assert.Equal(0, Invoke(registry, state, "Ljava/util/Iterator;", "hasNext", "()Z", AndroidInvokeKind.Interface, iterator));
        var error = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, "Ljava/util/Iterator;", "next", "()Ljava/lang/Object;", AndroidInvokeKind.Interface, iterator));
        Assert.Equal("Ljava/util/NoSuchElementException;", error.Throwable.TypeDescriptor);
    }

    [Fact]
    public void Array_list_set_contains_index_of_add_at_clear_and_iterator_round_trip()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        var list = new DexObject("Ljava/util/ArrayList;");
        Invoke(registry, state, "Ljava/util/ArrayList;", "<init>", "()V", AndroidInvokeKind.Direct, list);
        Invoke(registry, state, "Ljava/util/ArrayList;", "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, "a");
        Invoke(registry, state, "Ljava/util/ArrayList;", "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, "b");
        Invoke(registry, state, "Ljava/util/ArrayList;", "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, "c");

        Assert.Equal("b", Invoke(registry, state, "Ljava/util/ArrayList;", "set", "(ILjava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 1, "x"));
        Assert.Equal("x", Invoke(registry, state, "Ljava/util/ArrayList;", "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 1));
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/ArrayList;", "contains", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, "x"));
        Assert.Equal(0, Invoke(registry, state, "Ljava/util/ArrayList;", "contains", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, "missing"));
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/ArrayList;", "indexOf", "(Ljava/lang/Object;)I", AndroidInvokeKind.Virtual, list, "x"));
        Assert.Equal(-1, Invoke(registry, state, "Ljava/util/ArrayList;", "indexOf", "(Ljava/lang/Object;)I", AndroidInvokeKind.Virtual, list, "missing"));
        Invoke(registry, state, "Ljava/util/ArrayList;", "add", "(ILjava/lang/Object;)V", AndroidInvokeKind.Virtual, list, 1, "inserted");
        Assert.Equal("inserted", Invoke(registry, state, "Ljava/util/ArrayList;", "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 1));
        Assert.Equal(4, Invoke(registry, state, "Ljava/util/ArrayList;", "size", "()I", AndroidInvokeKind.Virtual, list));
        Assert.NotNull(Invoke(registry, state, "Ljava/util/ArrayList;", "iterator", "()Ljava/util/Iterator;", AndroidInvokeKind.Virtual, list));
        Invoke(registry, state, "Ljava/util/ArrayList;", "clear", "()V", AndroidInvokeKind.Virtual, list);
        Assert.Equal(0, Invoke(registry, state, "Ljava/util/ArrayList;", "size", "()I", AndroidInvokeKind.Virtual, list));
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/ArrayList;", "isEmpty", "()Z", AndroidInvokeKind.Virtual, list));
    }

    [Fact]
    public void Copy_on_write_array_list_full_list_surface_and_add_if_absent_round_trip()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        var list = new DexObject("Ljava/util/concurrent/CopyOnWriteArrayList;");
        Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "<init>", "()V", AndroidInvokeKind.Direct, list);

        Assert.Equal(1, Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, "a"));
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, "b"));
        Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "add", "(ILjava/lang/Object;)V", AndroidInvokeKind.Virtual, list, 1, "mid");
        Assert.Equal("mid", Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 1));
        Assert.Equal(3, Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "size", "()I", AndroidInvokeKind.Virtual, list));
        Assert.Equal("a", Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "set", "(ILjava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 0, "z"));
        Assert.Equal("z", Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 0));
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "contains", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, "mid"));
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "indexOf", "(Ljava/lang/Object;)I", AndroidInvokeKind.Virtual, list, "mid"));
        Assert.Equal(-1, Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "indexOf", "(Ljava/lang/Object;)I", AndroidInvokeKind.Virtual, list, "nope"));
        Assert.Equal(0, Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "addIfAbsent", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, "mid"));
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "addIfAbsent", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, "new"));
        Assert.Equal("z", Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "remove", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 0));
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "remove", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, "mid"));
        Assert.Equal(0, Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "remove", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, "mid"));
        Assert.NotNull(Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "iterator", "()Ljava/util/Iterator;", AndroidInvokeKind.Virtual, list));
        Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "clear", "()V", AndroidInvokeKind.Virtual, list);
        Assert.Equal(0, Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "size", "()I", AndroidInvokeKind.Virtual, list));
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "isEmpty", "()Z", AndroidInvokeKind.Virtual, list));
    }

    [Fact]
    public void Copy_on_write_array_list_collection_constructor_copies_a_modeled_collection()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        var source = new DexObject("Ljava/util/ArrayList;");
        Invoke(registry, state, "Ljava/util/ArrayList;", "<init>", "()V", AndroidInvokeKind.Direct, source);
        Invoke(registry, state, "Ljava/util/ArrayList;", "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, source, "a");
        Invoke(registry, state, "Ljava/util/ArrayList;", "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, source, "b");
        var target = new DexObject("Ljava/util/concurrent/CopyOnWriteArrayList;");
        Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "<init>", "(Ljava/util/Collection;)V", AndroidInvokeKind.Direct, target, source);

        Assert.Equal("a", Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, target, 0));
        Assert.Equal("b", Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, target, 1));
        Assert.Equal(2, Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "size", "()I", AndroidInvokeKind.Virtual, target));
    }

    [Fact]
    public void Iterator_and_copy_on_write_array_list_peer_quotas_fail_closed()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers(), peerLimits: new AndroidPeerLimits(maxIterators: 1, maxCopyOnWriteArrayLists: 1));
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        var list = new DexObject("Ljava/util/ArrayList;");
        Invoke(registry, state, "Ljava/util/ArrayList;", "<init>", "()V", AndroidInvokeKind.Direct, list);
        Invoke(registry, state, "Ljava/util/ArrayList;", "iterator", "()Ljava/util/Iterator;", AndroidInvokeKind.Virtual, list);
        Assert.Throws<AndroidPeerQuotaExceededException>(() => Invoke(registry, state, "Ljava/util/ArrayList;", "iterator", "()Ljava/util/Iterator;", AndroidInvokeKind.Virtual, list));
        Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "<init>", "()V", AndroidInvokeKind.Direct, new DexObject("Ljava/util/concurrent/CopyOnWriteArrayList;"));
        Assert.Throws<AndroidPeerQuotaExceededException>(() => Invoke(registry, state, "Ljava/util/concurrent/CopyOnWriteArrayList;", "<init>", "()V", AndroidInvokeKind.Direct, new DexObject("Ljava/util/concurrent/CopyOnWriteArrayList;")));
    }

    [Fact]
    public void Enum_init_name_ordinal_to_string_equals_hash_and_compare_to_round_trip()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        var red = new DexObject("Lexample/Color;");
        Invoke(registry, state, "Ljava/lang/Enum;", "<init>", "(Ljava/lang/String;I)V", AndroidInvokeKind.Direct, red, "RED", 0);
        var green = new DexObject("Lexample/Color;");
        Invoke(registry, state, "Ljava/lang/Enum;", "<init>", "(Ljava/lang/String;I)V", AndroidInvokeKind.Direct, green, "GREEN", 1);

        Assert.Equal("RED", Invoke(registry, state, "Ljava/lang/Enum;", "name", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, red));
        Assert.Equal(0, Invoke(registry, state, "Ljava/lang/Enum;", "ordinal", "()I", AndroidInvokeKind.Virtual, red));
        Assert.Equal(1, Invoke(registry, state, "Ljava/lang/Enum;", "ordinal", "()I", AndroidInvokeKind.Virtual, green));
        Assert.Equal("RED", Invoke(registry, state, "Ljava/lang/Enum;", "toString", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, red));
        // equals is reference identity (enum constants are singletons).
        Assert.Equal(1, Invoke(registry, state, "Ljava/lang/Enum;", "equals", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, red, red));
        Assert.Equal(0, Invoke(registry, state, "Ljava/lang/Enum;", "equals", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, red, green));
        // hashCode is the CLR identity hash of the receiver.
        Assert.Equal(red.GetHashCode(), Invoke(registry, state, "Ljava/lang/Enum;", "hashCode", "()I", AndroidInvokeKind.Virtual, red));
        // compareTo compares ordinals.
        Assert.Equal(-1, Invoke(registry, state, "Ljava/lang/Enum;", "compareTo", "(Ljava/lang/Enum;)I", AndroidInvokeKind.Virtual, red, green));
        Assert.Equal(1, Invoke(registry, state, "Ljava/lang/Enum;", "compareTo", "(Ljava/lang/Enum;)I", AndroidInvokeKind.Virtual, green, red));
        Assert.Equal(0, Invoke(registry, state, "Ljava/lang/Enum;", "compareTo", "(Ljava/lang/Enum;)I", AndroidInvokeKind.Virtual, red, red));
    }

    [Fact]
    public void Enum_peer_quota_fails_closed()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers(), peerLimits: new AndroidPeerLimits(maxEnums: 1));
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        Invoke(registry, state, "Ljava/lang/Enum;", "<init>", "(Ljava/lang/String;I)V", AndroidInvokeKind.Direct, new DexObject("Lexample/Color;"), "RED", 0);

        Assert.Throws<AndroidPeerQuotaExceededException>(() => Invoke(registry, state, "Ljava/lang/Enum;", "<init>", "(Ljava/lang/String;I)V", AndroidInvokeKind.Direct, new DexObject("Lexample/Color;"), "GREEN", 1));
    }

    [Fact]
    public void Atomic_integer_full_surface_round_trip()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        var counter = new DexObject("Ljava/util/concurrent/atomic/AtomicInteger;");
        Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicInteger;", "<init>", "()V", AndroidInvokeKind.Direct, counter);
        var seeded = new DexObject("Ljava/util/concurrent/atomic/AtomicInteger;");
        Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicInteger;", "<init>", "(I)V", AndroidInvokeKind.Direct, seeded, 10);

        Assert.Equal(0, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicInteger;", "get", "()I", AndroidInvokeKind.Virtual, counter));
        Assert.Equal(10, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicInteger;", "get", "()I", AndroidInvokeKind.Virtual, seeded));
        Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicInteger;", "set", "(I)V", AndroidInvokeKind.Virtual, counter, 5);
        Assert.Equal(5, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicInteger;", "get", "()I", AndroidInvokeKind.Virtual, counter));
        Assert.Equal(5, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicInteger;", "getAndIncrement", "()I", AndroidInvokeKind.Virtual, counter));
        Assert.Equal(6, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicInteger;", "get", "()I", AndroidInvokeKind.Virtual, counter));
        Assert.Equal(7, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicInteger;", "incrementAndGet", "()I", AndroidInvokeKind.Virtual, counter));
        Assert.Equal(7, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicInteger;", "getAndDecrement", "()I", AndroidInvokeKind.Virtual, counter));
        Assert.Equal(6, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicInteger;", "get", "()I", AndroidInvokeKind.Virtual, counter));
        Assert.Equal(5, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicInteger;", "decrementAndGet", "()I", AndroidInvokeKind.Virtual, counter));
        Assert.Equal(5, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicInteger;", "getAndAdd", "(I)I", AndroidInvokeKind.Virtual, counter, 3));
        Assert.Equal(8, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicInteger;", "get", "()I", AndroidInvokeKind.Virtual, counter));
        Assert.Equal(10, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicInteger;", "addAndGet", "(I)I", AndroidInvokeKind.Virtual, counter, 2));
        // compareAndSet: wrong expected fails, right expected succeeds.
        Assert.Equal(0, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicInteger;", "compareAndSet", "(II)Z", AndroidInvokeKind.Virtual, counter, 999, 1));
        Assert.Equal(10, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicInteger;", "get", "()I", AndroidInvokeKind.Virtual, counter));
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicInteger;", "compareAndSet", "(II)Z", AndroidInvokeKind.Virtual, counter, 10, 42));
        Assert.Equal(42, Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicInteger;", "get", "()I", AndroidInvokeKind.Virtual, counter));
    }

    [Fact]
    public void Atomic_integer_peer_quota_fails_closed()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers(), peerLimits: new AndroidPeerLimits(maxAtomicIntegers: 1));
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicInteger;", "<init>", "()V", AndroidInvokeKind.Direct, new DexObject("Ljava/util/concurrent/atomic/AtomicInteger;"));

        Assert.Throws<AndroidPeerQuotaExceededException>(() => Invoke(registry, state, "Ljava/util/concurrent/atomic/AtomicInteger;", "<init>", "()V", AndroidInvokeKind.Direct, new DexObject("Ljava/util/concurrent/atomic/AtomicInteger;")));
    }

    [Fact]
    public void Portfolio_rejects_invoke_shape_invalid_toast_duration_and_preserves_overloads()
    {
        var peers = new ActivityWindowPeers();
        var activity = new DexObject("Lorg/example/Main;");
        peers.Associate(activity, new InMemoryActivityWindow());
        var state = new AndroidFrameworkState("s", "org.example", activity.TypeDescriptor, peers);
        state.AttachActivity(activity);
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();
        var context = new AndroidApiSessionContext("s", "org.example", activity.TypeDescriptor, default, () => true);
        var log = new AndroidApiMethodId("Landroid/util/Log;", "i", "(Ljava/lang/String;Ljava/lang/String;)I");

        Assert.Throws<ArgumentException>(() => registry.Invoke(context, new AndroidApiCallSite("Lc;->m()V", 0, log, log, AndroidInvokeKind.Virtual), [activity, "t", "m"]));
        Assert.True(registry.Contains(new AndroidApiMethodId("Ljava/lang/String;", "indexOf", "(Ljava/lang/String;)I")));
        Assert.True(registry.Contains(new AndroidApiMethodId("Ljava/lang/String;", "indexOf", "(Ljava/lang/String;I)I")));
        // makeText relays to the view bridge: duration outside {0,1} (the AOSP
        // LENGTH_* domain) fails; any text length is accepted (AOSP has no cap).
        Assert.Throws<AndroidApiBindingException>(() => Invoke(registry, state, "Landroid/widget/Toast;", "makeText", "(Landroid/content/Context;Ljava/lang/CharSequence;I)Landroid/widget/Toast;", AndroidInvokeKind.Static, activity, "ok", 2));
    }

    [Fact]
    public void Return_assignability_null_text_log_title_color_and_peer_quotas_are_strict()
    {
        var peers = new ActivityWindowPeers();
        var activity = new DexObject("Lorg/example/Main;");
        var window = new InMemoryActivityWindow();
        peers.Associate(activity, window);
        var limits = new AndroidPeerLimits(maxStringBuilders: 1, maxBundles: 1, maxIntents: 1, maxToasts: 1);
        var state = new AndroidFrameworkState("s", "org.example", activity.TypeDescriptor, peers, peerLimits: limits);
        state.AttachActivity(activity);
        var logs = new RecordingLogSink();
        var registry = AndroidApiBindings.CreateBuilder(state, logs).Build();

        var invalidReturn = new AndroidApiMethodId("Lexample/Api;", "bad", "()Ljava/lang/String;");
        var badRegistry = new AndroidApiRegistryBuilder().Register(invalidReturn, (_, _) => new DexObject("Landroid/widget/Toast;")).Build();
        Assert.Throws<AndroidApiBindingException>(() => badRegistry.Invoke(
            new AndroidApiSessionContext("s", "p", "La;", default, () => true),
            new AndroidApiCallSite("Lc;->m()V", 0, invalidReturn, invalidReturn, AndroidInvokeKind.Static), []));

        Assert.Throws<AndroidApiNullReferenceException>(() => Invoke(registry, state, "Landroid/text/TextUtils;", "getTrimmedLength", "(Ljava/lang/CharSequence;)I", AndroidInvokeKind.Static, (object)null!)!);
        Assert.True((int)Invoke(registry, state, "Landroid/util/Log;", "i", "(Ljava/lang/String;Ljava/lang/String;)I", AndroidInvokeKind.Static, (object)null!, "message")! > 0);
        Assert.Null(Assert.Single(logs.Entries).Tag);

        var builder = new DexObject("Ljava/lang/StringBuilder;");
        Invoke(registry, state, "Ljava/lang/StringBuilder;", "<init>", "(Ljava/lang/String;)V", AndroidInvokeKind.Direct, builder, "Builder title");
        Invoke(registry, state, "Landroid/app/Activity;", "setTitle", "(Ljava/lang/CharSequence;)V", AndroidInvokeKind.Virtual, activity, builder);
        Assert.Equal("Builder title", window.Title);
        for (int attempt = 0; attempt < 32; attempt++)
            Assert.Throws<AndroidPeerQuotaExceededException>(() => Invoke(registry, state, "Ljava/lang/StringBuilder;", "<init>", "()V", AndroidInvokeKind.Direct, new DexObject("Ljava/lang/StringBuilder;")));
        Assert.Throws<AndroidPeerQuotaExceededException>(() => Invoke(registry, state, "Landroid/content/Intent;", "<init>", "()V", AndroidInvokeKind.Direct, new DexObject("Landroid/content/Intent;")));
        var bundle = new DexObject("Landroid/os/Bundle;");
        Invoke(registry, state, "Landroid/os/Bundle;", "<init>", "()V", AndroidInvokeKind.Direct, bundle);
        Assert.Throws<AndroidPeerQuotaExceededException>(() => Invoke(registry, state, "Landroid/os/Bundle;", "<init>", "()V", AndroidInvokeKind.Direct, new DexObject("Landroid/os/Bundle;")));
        Invoke(registry, state, "Landroid/widget/Toast;", "makeText", "(Landroid/content/Context;Ljava/lang/CharSequence;I)Landroid/widget/Toast;", AndroidInvokeKind.Static, activity, "one", 0);
        Assert.Throws<AndroidPeerQuotaExceededException>(() => Invoke(registry, state, "Landroid/widget/Toast;", "makeText", "(Landroid/content/Context;Ljava/lang/CharSequence;I)Landroid/widget/Toast;", AndroidInvokeKind.Static, activity, "two", 0));
        Assert.Equal(new AndroidPeerCounts(1, 1, 1, 1), state.PeerCounts);

        Assert.Equal(unchecked((int)0xffff8007), Invoke(registry, state, "Landroid/graphics/Color;", "argb", "(IIII)I", AndroidInvokeKind.Static, 300, -1, 128, 7));
        state.Dispose();
        Assert.Equal(new AndroidPeerCounts(0, 0, 0, 0), state.PeerCounts);
    }

    [Fact]
    public void Toast_makeText_validates_duration_domain()
    {
        var peers = new ActivityWindowPeers();
        var activity = new DexObject("Lorg/example/Main;");
        var window = new CountingToastWindow();
        peers.Associate(activity, window);
        var state = new AndroidFrameworkState("s", "p", activity.TypeDescriptor, peers);
        state.AttachActivity(activity);
        var registry = AndroidApiBindings.CreateBuilder(state, new RecordingLogSink()).Build();

        // makeText relays to the view bridge; a duration outside {0,1} (the AOSP
        // LENGTH_* domain) fails before the relay.
        Assert.Throws<AndroidApiBindingException>(() => Invoke(registry, state, "Landroid/widget/Toast;", "makeText", "(Landroid/content/Context;Ljava/lang/CharSequence;I)Landroid/widget/Toast;", AndroidInvokeKind.Static, activity, "ok", 2));
        // Valid SHORT duration with the unavailable bridge: no-op (headless), no throw.
        _ = Invoke(registry, state, "Landroid/widget/Toast;", "makeText", "(Landroid/content/Context;Ljava/lang/CharSequence;I)Landroid/widget/Toast;", AndroidInvokeKind.Static, activity, "ok", 0);
    }

    private static object? Invoke(AndroidApiRegistry registry, AndroidFrameworkState state, string owner, string name, string descriptor, AndroidInvokeKind kind, params object[] args)
    {
        var api = new AndroidApiMethodId(owner, name, descriptor);
        var context = new AndroidApiSessionContext(state.SessionId, state.PackageName, state.ActivityDescriptor, default, () => true);
        return registry.Invoke(context, new AndroidApiCallSite("Ltest;->run()V", 0, api, api, kind), args);
    }

    private sealed class RecordingLogSink : IAndroidLogSink
    {
        public List<AndroidLogEntry> Entries { get; } = [];
        public int Info(AndroidLogEntry entry) { Entries.Add(entry); return 1; }
    }

    private sealed class CountingToastWindow : IActivityWindow
    {
        public event EventHandler? Closed;
        public nint Handle => 0;
        public string Title { get; private set; } = string.Empty;
        public bool IsClosed { get; private set; }
        public void SetTitle(string? title, CancellationToken cancellationToken) => Title = title ?? string.Empty;
        public void Show(CancellationToken cancellationToken) { }
        public void Close() { if (IsClosed) return; IsClosed = true; Closed?.Invoke(this, EventArgs.Empty); }
        public void Dispose() => Close();
    }
    private sealed class FixedClock : IAndroidClock
    {
        public long UptimeMillis() => 5_000_000_001L;
        public long ElapsedRealtime() => 6_000_000_002L;
        public long ElapsedRealtimeNanos() => 7_000_000_003L;
    }
}
