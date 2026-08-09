using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for android.content.SharedPreferences + SharedPreferences.Editor against
/// the REAL AOSP contract (SharedPreferencesImpl.java, verified from source):
/// per-name singleton identity, typed get/put round-trips, default-when-absent,
/// the real type-cast semantics (wrong stored type -> guest ClassCastException),
/// apply()/commit() folding, Editor chaining returning this, remove/clear, and
/// the documented null-value-is-remove behavior.
/// </summary>
public sealed class SharedPreferencesTests
{
    private const string Context = "Landroid/content/Context;";
    private const string SharedPreferences = "Landroid/content/SharedPreferences;";
    private const string Editor = "Landroid/content/SharedPreferences$Editor;";

    [Fact]
    public void Get_shared_preferences_returns_the_same_instance_per_name()
    {
        var (state, registry, _) = Session();
        var first = (DexObject)Invoke(registry, state, Context, "getSharedPreferences", "(Ljava/lang/String;I)Landroid/content/SharedPreferences;", AndroidInvokeKind.Virtual, state.ApplicationContext, "prefs", 0);
        var second = (DexObject)Invoke(registry, state, Context, "getSharedPreferences", "(Ljava/lang/String;I)Landroid/content/SharedPreferences;", AndroidInvokeKind.Virtual, state.ApplicationContext, "prefs", 0);
        Assert.Same(first, second);

        var other = (DexObject)Invoke(registry, state, Context, "getSharedPreferences", "(Ljava/lang/String;I)Landroid/content/SharedPreferences;", AndroidInvokeKind.Virtual, state.ApplicationContext, "other", 0);
        Assert.NotSame(first, other);
        // Mode is accepted and ignored: the same name still returns the same instance.
        var modeIgnored = (DexObject)Invoke(registry, state, Context, "getSharedPreferences", "(Ljava/lang/String;I)Landroid/content/SharedPreferences;", AndroidInvokeKind.Virtual, state.ApplicationContext, "prefs", 3);
        Assert.Same(first, modeIgnored);
    }

    [Theory]
    [InlineData("str-key", "hello")]
    [InlineData("str-key", "")]
    [InlineData("str-key", "multi\nline\u0000")]
    public void Put_string_apply_get_string_round_trips(string key, string value)
    {
        var (state, registry, _) = Session();
        var prefs = Prefs(registry, state, "p");
        var editor = Edit(registry, state, prefs);
        Assert.Same(editor, Invoke(registry, state, Editor, "putString", "(Ljava/lang/String;Ljava/lang/String;)Landroid/content/SharedPreferences$Editor;", AndroidInvokeKind.Virtual, editor, key, value));
        Invoke(registry, state, Editor, "apply", "()V", AndroidInvokeKind.Virtual, editor);
        Assert.Equal(value, Invoke(registry, state, SharedPreferences, "getString", "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;", AndroidInvokeKind.Virtual, prefs, key, "default"));
    }

    [Fact]
    public void Put_int_long_boolean_apply_get_round_trips()
    {
        var (state, registry, _) = Session();
        var prefs = Prefs(registry, state, "p");
        var editor = Edit(registry, state, prefs);
        Assert.Same(editor, Invoke(registry, state, Editor, "putInt", "(Ljava/lang/String;I)Landroid/content/SharedPreferences$Editor;", AndroidInvokeKind.Virtual, editor, "int", 42));
        Assert.Same(editor, Invoke(registry, state, Editor, "putLong", "(Ljava/lang/String;J)Landroid/content/SharedPreferences$Editor;", AndroidInvokeKind.Virtual, editor, "long", 9_000_000_004L));
        Assert.Same(editor, Invoke(registry, state, Editor, "putBoolean", "(Ljava/lang/String;Z)Landroid/content/SharedPreferences$Editor;", AndroidInvokeKind.Virtual, editor, "bool", 1));
        Invoke(registry, state, Editor, "apply", "()V", AndroidInvokeKind.Virtual, editor);

        Assert.Equal(42, Invoke(registry, state, SharedPreferences, "getInt", "(Ljava/lang/String;I)I", AndroidInvokeKind.Virtual, prefs, "int", -1));
        Assert.Equal(9_000_000_004L, Invoke(registry, state, SharedPreferences, "getLong", "(Ljava/lang/String;J)J", AndroidInvokeKind.Virtual, prefs, "long", -1L));
        Assert.Equal(1, Invoke(registry, state, SharedPreferences, "getBoolean", "(Ljava/lang/String;Z)Z", AndroidInvokeKind.Virtual, prefs, "bool", 0));
    }

    [Fact]
    public void Reads_return_the_default_when_absent()
    {
        var (state, registry, _) = Session();
        var prefs = Prefs(registry, state, "p");
        Assert.Equal("absent-default", Invoke(registry, state, SharedPreferences, "getString", "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;", AndroidInvokeKind.Virtual, prefs, "missing", "absent-default"));
        Assert.Equal(7, Invoke(registry, state, SharedPreferences, "getInt", "(Ljava/lang/String;I)I", AndroidInvokeKind.Virtual, prefs, "missing", 7));
        Assert.Equal(8L, Invoke(registry, state, SharedPreferences, "getLong", "(Ljava/lang/String;J)J", AndroidInvokeKind.Virtual, prefs, "missing", 8L));
        Assert.Equal(1, Invoke(registry, state, SharedPreferences, "getBoolean", "(Ljava/lang/String;Z)Z", AndroidInvokeKind.Virtual, prefs, "missing", 1));
    }

    [Fact]
    public void Wrong_stored_type_throws_guest_class_cast_exception()
    {
        var (state, registry, _) = Session();
        var prefs = Prefs(registry, state, "p");
        var editor = Edit(registry, state, prefs);
        Invoke(registry, state, Editor, "putInt", "(Ljava/lang/String;I)Landroid/content/SharedPreferences$Editor;", AndroidInvokeKind.Virtual, editor, "k", 5);
        Invoke(registry, state, Editor, "apply", "()V", AndroidInvokeKind.Virtual, editor);

        var error = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, SharedPreferences, "getString", "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;", AndroidInvokeKind.Virtual, prefs, "k", "d"));
        Assert.Equal("Ljava/lang/ClassCastException;", error.Throwable.TypeDescriptor);
        Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, SharedPreferences, "getLong", "(Ljava/lang/String;J)J", AndroidInvokeKind.Virtual, prefs, "k", -1L));
        Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, SharedPreferences, "getBoolean", "(Ljava/lang/String;Z)Z", AndroidInvokeKind.Virtual, prefs, "k", 0));
    }

    [Fact]
    public void Commit_returns_true_and_applies_changes_synchronously()
    {
        var (state, registry, _) = Session();
        var prefs = Prefs(registry, state, "p");
        var editor = Edit(registry, state, prefs);
        Invoke(registry, state, Editor, "putString", "(Ljava/lang/String;Ljava/lang/String;)Landroid/content/SharedPreferences$Editor;", AndroidInvokeKind.Virtual, editor, "k", "v");
        Assert.Equal(1, Invoke(registry, state, Editor, "commit", "()Z", AndroidInvokeKind.Virtual, editor));
        Assert.Equal("v", Invoke(registry, state, SharedPreferences, "getString", "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;", AndroidInvokeKind.Virtual, prefs, "k", "d"));
    }

    [Fact]
    public void Remove_and_clear_fold_into_the_store()
    {
        var (state, registry, _) = Session();
        var prefs = Prefs(registry, state, "p");
        var editor = Edit(registry, state, prefs);
        Invoke(registry, state, Editor, "putString", "(Ljava/lang/String;Ljava/lang/String;)Landroid/content/SharedPreferences$Editor;", AndroidInvokeKind.Virtual, editor, "keep", "yes");
        Invoke(registry, state, Editor, "putString", "(Ljava/lang/String;Ljava/lang/String;)Landroid/content/SharedPreferences$Editor;", AndroidInvokeKind.Virtual, editor, "drop", "no");
        Invoke(registry, state, Editor, "apply", "()V", AndroidInvokeKind.Virtual, editor);

        var removeEditor = Edit(registry, state, prefs);
        Invoke(registry, state, Editor, "remove", "(Ljava/lang/String;)Landroid/content/SharedPreferences$Editor;", AndroidInvokeKind.Virtual, removeEditor, "drop");
        Invoke(registry, state, Editor, "apply", "()V", AndroidInvokeKind.Virtual, removeEditor);
        Assert.Equal("yes", Invoke(registry, state, SharedPreferences, "getString", "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;", AndroidInvokeKind.Virtual, prefs, "keep", "d"));
        Assert.Equal("d", Invoke(registry, state, SharedPreferences, "getString", "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;", AndroidInvokeKind.Virtual, prefs, "drop", "d"));

        var clearEditor = Edit(registry, state, prefs);
        Invoke(registry, state, Editor, "clear", "()Landroid/content/SharedPreferences$Editor;", AndroidInvokeKind.Virtual, clearEditor);
        Invoke(registry, state, Editor, "apply", "()V", AndroidInvokeKind.Virtual, clearEditor);
        Assert.Equal("d", Invoke(registry, state, SharedPreferences, "getString", "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;", AndroidInvokeKind.Virtual, prefs, "keep", "d"));
    }

    [Fact]
    public void Null_value_in_put_string_is_equivalent_to_remove()
    {
        var (state, registry, _) = Session();
        var prefs = Prefs(registry, state, "p");
        var editor = Edit(registry, state, prefs);
        Invoke(registry, state, Editor, "putString", "(Ljava/lang/String;Ljava/lang/String;)Landroid/content/SharedPreferences$Editor;", AndroidInvokeKind.Virtual, editor, "k", "v");
        Invoke(registry, state, Editor, "apply", "()V", AndroidInvokeKind.Virtual, editor);
        Assert.Equal("v", Invoke(registry, state, SharedPreferences, "getString", "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;", AndroidInvokeKind.Virtual, prefs, "k", "d"));

        var nullEditor = Edit(registry, state, prefs);
        Invoke(registry, state, Editor, "putString", "(Ljava/lang/String;Ljava/lang/String;)Landroid/content/SharedPreferences$Editor;", AndroidInvokeKind.Virtual, nullEditor, "k", null!);
        Invoke(registry, state, Editor, "apply", "()V", AndroidInvokeKind.Virtual, nullEditor);
        Assert.Equal("d", Invoke(registry, state, SharedPreferences, "getString", "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;", AndroidInvokeKind.Virtual, prefs, "k", "d"));
    }

    [Fact]
    public void Each_edit_call_returns_a_new_editor()
    {
        var (state, registry, _) = Session();
        var prefs = Prefs(registry, state, "p");
        var first = (DexObject)Invoke(registry, state, SharedPreferences, "edit", "()Landroid/content/SharedPreferences$Editor;", AndroidInvokeKind.Virtual, prefs);
        var second = (DexObject)Invoke(registry, state, SharedPreferences, "edit", "()Landroid/content/SharedPreferences$Editor;", AndroidInvokeKind.Virtual, prefs);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void Pending_edits_are_not_visible_before_apply_or_commit()
    {
        var (state, registry, _) = Session();
        var prefs = Prefs(registry, state, "p");
        var editor = Edit(registry, state, prefs);
        Invoke(registry, state, Editor, "putString", "(Ljava/lang/String;Ljava/lang/String;)Landroid/content/SharedPreferences$Editor;", AndroidInvokeKind.Virtual, editor, "k", "v");
        // Not yet folded: reads still see the default.
        Assert.Equal("d", Invoke(registry, state, SharedPreferences, "getString", "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;", AndroidInvokeKind.Virtual, prefs, "k", "d"));
        Invoke(registry, state, Editor, "apply", "()V", AndroidInvokeKind.Virtual, editor);
        Assert.Equal("v", Invoke(registry, state, SharedPreferences, "getString", "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;", AndroidInvokeKind.Virtual, prefs, "k", "d"));
    }

    private static DexObject Prefs(AndroidApiRegistry registry, AndroidFrameworkState state, string name)
        => (DexObject)Invoke(registry, state, Context, "getSharedPreferences", "(Ljava/lang/String;I)Landroid/content/SharedPreferences;", AndroidInvokeKind.Virtual, state.ApplicationContext, name, 0);

    private static DexObject Edit(AndroidApiRegistry registry, AndroidFrameworkState state, DexObject prefs)
        => (DexObject)Invoke(registry, state, SharedPreferences, "edit", "()Landroid/content/SharedPreferences$Editor;", AndroidInvokeKind.Virtual, prefs);

    private static (AndroidFrameworkState State, AndroidApiRegistry Registry, DexInterpreter Interpreter) Session()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new QuietLogSink()).Build();
        var dex = new DexFile();
        var interpreter = new DexInterpreter(dex, registry, gil: state.Gil);
        state.Gil = interpreter.Gil;
        state.AttachInterpreter(interpreter);
        return (state, registry, interpreter);
    }

    private static object Invoke(AndroidApiRegistry registry, AndroidFrameworkState state, string owner, string name, string descriptor, AndroidInvokeKind kind, params object[] args)
    {
        var api = new AndroidApiMethodId(owner, name, descriptor);
        var context = new AndroidApiSessionContext(state.SessionId, state.PackageName, state.ActivityDescriptor, default, () => true);
        if (state.Interpreter is not null) context.IsTypeAssignable = state.Interpreter.IsGuestTypeAssignable;
        return registry.Invoke(context, new AndroidApiCallSite("Ltest;->run()V", 0, api, api, kind), args);
    }

    private sealed class QuietLogSink : IAndroidLogSink { public int Info(AndroidLogEntry entry) => 1; }
}
