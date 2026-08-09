#nullable enable
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for android.content.SharedPreferences + SharedPreferences.Editor
/// (own file for the android.content preferences family). Real contract VERIFIED
/// against the AOSP source (frameworks/base/core/java/android/app/
/// SharedPreferencesImpl.java, fetched during this work unit — NOT from memory):
/// - Context.getSharedPreferences(name, mode) returns a process-wide singleton
///   per name: the SAME SharedPreferences object for repeated calls with the
///   same name within one process (AOSP ContextImpl caches per ContextImpl but
///   the documented contract is per-name singleton within the app process; this
///   runtime has exactly one session Context facade, so the per-name map IS the
///   process-wide cache). mode is legacy (MODE_PRIVATE=0; MODE_WORLD_READABLE/
///   WRITEABLE deprecated and effectively no-ops on modern Android) — accepted,
///   ignored, no multi-process semantics.
/// - Reads cast the stored value to the requested type: getString returns the
///   default when absent and the stored value otherwise; a stored value of a
///   DIFFERENT type surfaces the real cast semantics (ClassCastException), same
///   as AOSP's `(String) mMap.get(key)`.
/// - edit() returns a NEW Editor each call (real Android: new EditorImpl()).
///   put* accumulate into the editor's pending map and return `this` for
///   chaining; remove(key) records a removal marker; clear() sets a flag; a
///   null value passed to putString is documented to be EQUIVALENT to remove
///   (verified in commitToMemory: `v == null` is treated as removal). apply()
///   and commit() both fold the pending writes into the shared store.
/// - apply() is async-conceptually for the DISK write only; commitToMemory()
///   (the in-memory fold) runs synchronously in BOTH apply() and commit().
///   This runtime has no real disk/app-data directory — persistence is
///   in-memory only, per session: values do NOT survive process restart
///   (stated plainly as a known limitation, same tone as WeakHashMap's "no
///   guest GC model" note). Under the GIL a synchronous in-memory fold is
///   therefore equivalent to apply()'s async disk write, and commit() always
///   returns true (an in-memory write cannot fail) — same reasoning as
///   AtomicReference/Collections.synchronizedMap/kotlin.Lazy.
/// - registerOnSharedPreferenceChangeListener/unregister are NOT built: the
///   probe shows no listener references, per the brief's "only build if
///   referenced" rule.
/// Probe of SKYNET-FlexGrabber.apk method table: getSharedPreferences(String,I),
/// edit, getBoolean, getString, getLong, getInt, putBoolean, putString,
/// putLong, putInt, remove, clear, apply, commit — all built to their complete
/// real contract; getFloat/getStringSet/getAll/contains/putFloat/putStringSet
/// are NOT referenced by the probe and are NOT built (same discipline as every
/// prior boundary).
/// </summary>
internal static class AndroidContentSharedPreferencesBindings
{
    private const string SharedPreferences = "Landroid/content/SharedPreferences;";
    private const string Editor = "Landroid/content/SharedPreferences$Editor;";
    private const string ClassCast = "Ljava/lang/ClassCastException;";

    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        // Context.getSharedPreferences(String, I) -> per-name singleton.
        builder.Register(Api("Landroid/content/Context;", "getSharedPreferences", "(Ljava/lang/String;I)Landroid/content/SharedPreferences;"), (_, args) =>
        {
            RequireContext(state, Receiver(args));
            return state.EnsureSharedPreferences(AndroidApiBindings.RequireString(args[1]));
        });

        // ---- Reads: default when absent; real cast semantics on stored value ----
        builder.Register(Api(SharedPreferences, "getString", "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;"), (_, args) =>
        {
            var peer = Store(state, args);
            string key = AndroidApiBindings.RequireString(args[1]);
            if (!peer.Values.TryGetValue(key, out object? value)) return args[2] ?? null!;
            if (value is not string text) throw TypeCastError(key);
            return text;
        });
        builder.Register(Api(SharedPreferences, "getInt", "(Ljava/lang/String;I)I"), (_, args) =>
        {
            var peer = Store(state, args);
            string key = AndroidApiBindings.RequireString(args[1]);
            if (!peer.Values.TryGetValue(key, out object? value)) return args[2];
            if (value is not int number) throw TypeCastError(key);
            return number;
        });
        builder.Register(Api(SharedPreferences, "getLong", "(Ljava/lang/String;J)J"), (_, args) =>
        {
            var peer = Store(state, args);
            string key = AndroidApiBindings.RequireString(args[1]);
            if (!peer.Values.TryGetValue(key, out object? value)) return args[2];
            if (value is not long wide) throw TypeCastError(key);
            return wide;
        });
        builder.Register(Api(SharedPreferences, "getBoolean", "(Ljava/lang/String;Z)Z"), (_, args) =>
        {
            var peer = Store(state, args);
            string key = AndroidApiBindings.RequireString(args[1]);
            if (!peer.Values.TryGetValue(key, out object? value)) return args[2];
            if (value is not bool flag) throw TypeCastError(key);
            return flag ? 1 : 0;
        });

        // edit() -> NEW Editor each call (real Android: new EditorImpl()).
        builder.Register(Api(SharedPreferences, "edit", "()Landroid/content/SharedPreferences$Editor;"), (_, args) =>
        {
            var owner = Store(state, args);
            var editor = new DexObject(Editor);
            state.SharedPreferencesEditors.Add(editor, new SharedPreferencesEditorPeer { Owner = owner });
            return editor;
        });

        // ---- Editor writes: accumulate pending, return this for chaining ----
        builder.Register(Api(Editor, "putString", "(Ljava/lang/String;Ljava/lang/String;)Landroid/content/SharedPreferences$Editor;"), (_, args) =>
        {
            var editor = EditorPeer(state, args);
            string key = AndroidApiBindings.RequireString(args[1]);
            // Null value is documented EQUIVALENT to remove(key) (AOSP commitToMemory).
            editor.Modified[key] = args[2] is string text ? text : null!;
            return args[0];
        });
        builder.Register(Api(Editor, "putInt", "(Ljava/lang/String;I)Landroid/content/SharedPreferences$Editor;"), (_, args) =>
        {
            var editor = EditorPeer(state, args);
            string key = AndroidApiBindings.RequireString(args[1]);
            editor.Modified[key] = AndroidApiBindings.RequireInt(args[2]);
            return args[0];
        });
        builder.Register(Api(Editor, "putLong", "(Ljava/lang/String;J)Landroid/content/SharedPreferences$Editor;"), (_, args) =>
        {
            var editor = EditorPeer(state, args);
            string key = AndroidApiBindings.RequireString(args[1]);
            editor.Modified[key] = AndroidApiBindings.RequireLong(args[2]);
            return args[0];
        });
        builder.Register(Api(Editor, "putBoolean", "(Ljava/lang/String;Z)Landroid/content/SharedPreferences$Editor;"), (_, args) =>
        {
            var editor = EditorPeer(state, args);
            string key = AndroidApiBindings.RequireString(args[1]);
            editor.Modified[key] = AndroidApiBindings.RequireInt(args[2]) != 0;
            return args[0];
        });
        builder.Register(Api(Editor, "remove", "(Ljava/lang/String;)Landroid/content/SharedPreferences$Editor;"), (_, args) =>
        {
            var editor = EditorPeer(state, args);
            string key = AndroidApiBindings.RequireString(args[1]);
            editor.Modified[key] = SharedPreferencesEditorPeer.RemoveMarker;
            return args[0];
        });
        builder.Register(Api(Editor, "clear", "()Landroid/content/SharedPreferences$Editor;"), (_, args) =>
        {
            EditorPeer(state, args).Clear = true;
            return args[0];
        });

        // ---- apply()/commit(): fold pending into the shared store synchronously ----
        builder.Register(Api(Editor, "apply", "()V"), (_, args) => { Fold(EditorPeer(state, args)); return null!; });
        builder.Register(Api(Editor, "commit", "()Z"), (_, args) => { Fold(EditorPeer(state, args)); return 1; });
    }

    /// <summary>Folds an editor's pending writes into the owner store, mirroring
    /// AOSP EditorImpl.commitToMemory's observable semantics: clear() first wipes
    /// the store, then each pending write is applied — a removal marker or null
    /// removes the key, a value puts it. The editor is then empty again (real
    /// EditorImpl clears mModified after commit). GIL note: apply() in real
    /// Android folds memory synchronously and only the DISK write is async; this
    /// runtime has no disk, so a synchronous fold is equivalent (documented in
    /// the class comment). commit() returns true because an in-memory write
    /// cannot fail.</summary>
    private static void Fold(SharedPreferencesEditorPeer editor)
    {
        var values = editor.Owner.Values;
        if (editor.Clear)
        {
            values.Clear();
            editor.Clear = false;
        }
        foreach (var pair in editor.Modified)
        {
            if (pair.Value == SharedPreferencesEditorPeer.RemoveMarker || pair.Value is null)
                values.Remove(pair.Key);
            else
                values[pair.Key] = pair.Value;
        }
        editor.Modified.Clear();
    }

    private static SharedPreferencesPeer Store(AndroidFrameworkState state, object[] args) => state.SharedPreferences.Get(Receiver(args));
    private static SharedPreferencesEditorPeer EditorPeer(AndroidFrameworkState state, object[] args) => state.SharedPreferencesEditors.Get(Receiver(args));

    private static void RequireContext(AndroidFrameworkState state, DexObject value)
    {
        // Same bounded check as AndroidApiBindings.RequireContext: the receiver
        // must be this session's Activity or Application context.
        if (value != state.Activity && value != state.ApplicationContext)
            throw new ArgumentException("Context receiver does not belong to this session.");
    }

    private static GuestExceptionCarrier TypeCastError(string key) =>
        new(GuestThrowableMetadata.Create(ClassCast, "SharedPreferences value for key '" + key + "' is not of the requested type"));

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    private static DexObject Receiver(object[] args) => (DexObject)args[0]!;
}
