#nullable enable
using System.Text;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;
using AndroidRuntime.Core.Apk;
using AndroidRuntime.Core.Ui;

namespace AndroidRuntime.Core.ApiLayer;

public sealed record AndroidPeerLimits
{
    public AndroidPeerLimits(int maxStringBuilders = 256, int maxBundles = 256, int maxIntents = 256, int maxToasts = 64, int maxViews = 4096, int maxAtomicReferences = 256, int maxWeakHashMaps = 256, int maxHashMaps = 256, int maxArrayLists = 256, int maxWeakReferences = 256, int maxCopyOnWriteArraySets = 256, int maxIterators = 256, int maxCopyOnWriteArrayLists = 256, int maxEnums = 256, int maxAtomicIntegers = 256, int maxThreads = 64)
    {
        StringBuilders = maxStringBuilders;
        Bundles = maxBundles;
        Intents = maxIntents;
        Toasts = maxToasts;
        Views = maxViews;
        AtomicReferences = maxAtomicReferences;
        WeakHashMaps = maxWeakHashMaps;
        HashMaps = maxHashMaps;
        ArrayLists = maxArrayLists;
        WeakReferences = maxWeakReferences;
        CopyOnWriteArraySets = maxCopyOnWriteArraySets;
        Iterators = maxIterators;
        CopyOnWriteArrayLists = maxCopyOnWriteArrayLists;
        Enums = maxEnums;
        AtomicIntegers = maxAtomicIntegers;
        Threads = maxThreads;
        Validate();
    }

    public static AndroidPeerLimits Default { get; } = new();
    public int StringBuilders { get; }
    public int Bundles { get; }
    public int Intents { get; }
    public int Toasts { get; }
    public int Views { get; }
    public int AtomicReferences { get; }
    public int WeakHashMaps { get; }
    public int HashMaps { get; }
    public int ArrayLists { get; }
    public int WeakReferences { get; }
    public int CopyOnWriteArraySets { get; }
    public int Iterators { get; }
    public int CopyOnWriteArrayLists { get; }
    public int Enums { get; }
    public int AtomicIntegers { get; }
    public int Threads { get; }

    public void Validate()
    {
        if (StringBuilders <= 0 || Bundles <= 0 || Intents <= 0 || Toasts <= 0 || Views <= 0 || AtomicReferences <= 0 || WeakHashMaps <= 0 || HashMaps <= 0 || ArrayLists <= 0 || WeakReferences <= 0 || CopyOnWriteArraySets <= 0 || Iterators <= 0 || CopyOnWriteArrayLists <= 0 || Enums <= 0 || AtomicIntegers <= 0 || Threads <= 0)
            throw new ArgumentOutOfRangeException(nameof(AndroidPeerLimits), "Peer limits must be positive.");
    }
}

public readonly record struct AndroidPeerCounts(int StringBuilders, int Bundles, int Intents, int Toasts);

public sealed class AndroidPeerQuotaExceededException : InvalidOperationException
{
    public AndroidPeerQuotaExceededException(string peerType, int limit)
        : base($"Android {peerType} peer quota exceeded ({limit}).")
    {
        PeerType = peerType;
        Limit = limit;
    }

    public string PeerType { get; }
    public int Limit { get; }
}

public sealed class AndroidFrameworkState : IDisposable
{
    private int _disposed;
    private int _finishing;
    private int _destroyed;

    public AndroidFrameworkState(
        string sessionId,
        string packageName,
        string activityDescriptor,
        ActivityWindowPeers windowPeers,
        int minimumLogPriority = 2,
        AndroidToastLimits? toastLimits = null,
        AndroidPeerLimits? peerLimits = null,
        IAndroidClock? clock = null,
        IReadOnlyCollection<string>? declaredPermissions = null,
        IAndroidCapabilityPolicy? capabilityPolicy = null,
        IAndroidClipboard? clipboard = null,
        IAndroidConnectivity? connectivity = null,
        IAndroidServiceAuditSink? serviceAudit = null,
        AndroidServiceLimits? serviceLimits = null,
        int targetSdkVersion = 1,
        IAndroidPower? power = null,
        AndroidResourceResolver? resources = null,
        AndroidUiLimits? uiLimits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        SessionId = sessionId;
        PackageName = packageName ?? string.Empty;
        ActivityDescriptor = activityDescriptor ?? string.Empty;
        WindowPeers = windowPeers ?? throw new ArgumentNullException(nameof(windowPeers));
        if (minimumLogPriority is < 2 or > 7)
            throw new ArgumentOutOfRangeException(nameof(minimumLogPriority));
        MinimumLogPriority = minimumLogPriority;
        ToastLimits = toastLimits ?? AndroidToastLimits.Default;
        PeerLimits = peerLimits ?? AndroidPeerLimits.Default;
        PeerLimits.Validate();
        Clock = clock ?? new StopwatchAndroidClock();
        DeclaredPermissions = Array.AsReadOnly((declaredPermissions ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray());
        CapabilityPolicy = capabilityPolicy ?? AndroidCapabilityPolicy.DenyAll;
        Clipboard = clipboard ?? new UnavailableAndroidClipboard();
        Connectivity = connectivity ?? new UnavailableAndroidConnectivity();
        ServiceAudit = serviceAudit ?? new NullAndroidServiceAuditSink();
        ServiceLimits = serviceLimits ?? AndroidServiceLimits.Default; ServiceLimits.Validate();
        TargetSdkVersion = targetSdkVersion;
        Power = power ?? new UnavailableAndroidPower();
        Ui = resources is null ? null : new AndroidUiSession(resources, uiLimits ?? new AndroidUiLimits(), PeerLimits.Views);
        StringBuilders = new AndroidPeerStore<StringBuilder>("StringBuilder", PeerLimits.StringBuilders);
        Bundles = new AndroidPeerStore<BundlePeer>("Bundle", PeerLimits.Bundles);
        Intents = new AndroidPeerStore<IntentPeer>("Intent", PeerLimits.Intents);
        Toasts = new AndroidPeerStore<ToastPeer>("Toast", PeerLimits.Toasts, peer => peer.Notification.Dispose());
        AtomicReferences = new AndroidPeerStore<AtomicReferencePeer>("AtomicReference", PeerLimits.AtomicReferences);
        WeakHashMaps = new AndroidPeerStore<WeakHashMapPeer>("WeakHashMap", PeerLimits.WeakHashMaps);
        HashMaps = new AndroidPeerStore<HashMapPeer>("HashMap", PeerLimits.HashMaps);
        ArrayLists = new AndroidPeerStore<ListPeer>("ArrayList", PeerLimits.ArrayLists);
        WeakReferences = new AndroidPeerStore<WeakReferencePeer>("WeakReference", PeerLimits.WeakReferences);
        CopyOnWriteArraySets = new AndroidPeerStore<HashSet<object?>>("CopyOnWriteArraySet", PeerLimits.CopyOnWriteArraySets);
        Iterators = new AndroidPeerStore<IteratorPeer>("Iterator", PeerLimits.Iterators);
        CopyOnWriteArrayLists = new AndroidPeerStore<ListPeer>("CopyOnWriteArrayList", PeerLimits.CopyOnWriteArrayLists);
        Enums = new AndroidPeerStore<EnumPeer>("Enum", PeerLimits.Enums);
        AtomicIntegers = new AndroidPeerStore<AtomicIntegerPeer>("AtomicInteger", PeerLimits.AtomicIntegers);
        Threads = new AndroidPeerStore<ThreadPeer>("Thread", PeerLimits.Threads);
        ApplicationContext = new DexObject("Landroid/app/Application;");
        LauncherIntent = new DexObject("Landroid/content/Intent;");
        Intents.Add(LauncherIntent, new IntentPeer { Action = "android.intent.action.MAIN" });
    }

    public string SessionId { get; }
    public string PackageName { get; }
    public string ActivityDescriptor { get; }
    public int MinimumLogPriority { get; }
    public DexObject ApplicationContext { get; }
    public DexObject LauncherIntent { get; }
    public AndroidToastLimits ToastLimits { get; }
    public AndroidPeerLimits PeerLimits { get; }
    public IAndroidClock Clock { get; }
    public IReadOnlyCollection<string> DeclaredPermissions { get; }
    internal IAndroidCapabilityPolicy CapabilityPolicy { get; }
    internal IAndroidClipboard Clipboard { get; }
    internal IAndroidConnectivity Connectivity { get; }
    internal IAndroidServiceAuditSink ServiceAudit { get; }
    internal AndroidServiceLimits ServiceLimits { get; }
    internal int TargetSdkVersion { get; }
    internal IAndroidPower Power { get; }
    internal AndroidUiSession? Ui { get; }
    internal AndroidSystemServiceRegistry? SystemServices { get; set; }
    public AndroidPeerCounts PeerCounts => new(StringBuilders.Count, Bundles.Count, Intents.Count, Toasts.Count);
    internal bool IsFinishing => Volatile.Read(ref _finishing) != 0;
    internal bool IsDestroyed => Volatile.Read(ref _destroyed) != 0;
    internal event Action? FinishRequested;
    internal void RequestFinish() { if (Interlocked.Exchange(ref _finishing, 1) == 0) FinishRequested?.Invoke(); }
    internal void MarkDestroyed() => Volatile.Write(ref _destroyed, 1);
    internal AndroidPeerStore<StringBuilder> StringBuilders { get; }
    internal AndroidPeerStore<BundlePeer> Bundles { get; }
    internal AndroidPeerStore<IntentPeer> Intents { get; }
    internal AndroidPeerStore<ToastPeer> Toasts { get; }
    internal AndroidPeerStore<AtomicReferencePeer> AtomicReferences { get; }
    internal AndroidPeerStore<WeakHashMapPeer> WeakHashMaps { get; }
    internal AndroidPeerStore<HashMapPeer> HashMaps { get; }
    internal AndroidPeerStore<ListPeer> ArrayLists { get; }
    internal AndroidPeerStore<WeakReferencePeer> WeakReferences { get; }
    internal AndroidPeerStore<HashSet<object?>> CopyOnWriteArraySets { get; }
    internal AndroidPeerStore<IteratorPeer> Iterators { get; }
    internal AndroidPeerStore<ListPeer> CopyOnWriteArrayLists { get; }
    internal AndroidPeerStore<EnumPeer> Enums { get; }
    internal AndroidPeerStore<AtomicIntegerPeer> AtomicIntegers { get; }
    internal AndroidPeerStore<ThreadPeer> Threads { get; }
    /// <summary>The session's GIL: shared by the interpreter and every binding that
    /// must release it around real blocking (sleep/join/monitor-enter/class-init
    /// wait). AndroidAppRuntime replaces this with the execution lane's GIL.</summary>
    internal AndroidGil Gil { get; set; } = new();
    /// <summary>The session interpreter, attached after construction so bindings
    /// that spawn guest work (Thread.start) can dispatch into it.</summary>
    internal DexInterpreter? Interpreter { get; private set; }
    internal ActivityWindowPeers WindowPeers { get; }
    internal DexObject? Activity { get; private set; }
    public void AttachActivity(DexObject activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (Activity is not null && !ReferenceEquals(Activity, activity))
            throw new InvalidOperationException("Framework session already has an Activity.");
        Activity = activity;
    }

    internal void AttachUiInterpreter(DexInterpreter interpreter)
    {
        if (Ui is null || Activity is null) return;
        Ui.Attach(Activity, interpreter);
    }

    internal void AttachInterpreter(DexInterpreter interpreter)
    {
        ArgumentNullException.ThrowIfNull(interpreter);
        Interpreter = interpreter;
    }

    internal void DisposeUi() => Ui?.Dispose();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Ui?.Dispose();
        Toasts.Clear();
        Intents.Clear();
        Bundles.Clear();
        StringBuilders.Clear();
        AtomicReferences.Clear();
        WeakHashMaps.Clear();
        HashMaps.Clear();
        ArrayLists.Clear();
        WeakReferences.Clear();
        CopyOnWriteArraySets.Clear();
        Iterators.Clear();
        CopyOnWriteArrayLists.Clear();
        Enums.Clear();
        AtomicIntegers.Clear();
        Threads.Clear();
        Activity = null;
        SystemServices?.Dispose();
    }
}

internal sealed class AndroidPeerStore<T> where T : class
{
    private readonly object _gate = new();
    private readonly Dictionary<DexObject, T> _peers = new(ReferenceEqualityComparer.Instance);
    private readonly string _peerType;
    private readonly int _limit;
    private readonly Action<T>? _dispose;
    private int _reserved;

    internal AndroidPeerStore(string peerType, int limit, Action<T>? dispose = null)
    {
        _peerType = peerType;
        _limit = limit;
        _dispose = dispose;
    }

    internal int Count { get { lock (_gate) return _peers.Count; } }
    internal void Add(DexObject guest, T peer)
    {
        lock (_gate)
        {
            if (_peers.ContainsKey(guest))
                throw new InvalidOperationException("Peer already exists for " + guest.TypeDescriptor);
            if (_peers.Count >= _limit)
                throw new AndroidPeerQuotaExceededException(_peerType, _limit);
            _peers.Add(guest, peer);
        }
    }
    internal void AddCreated(DexObject guest, Func<T> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (_gate)
        {
            if (_peers.ContainsKey(guest))
                throw new InvalidOperationException("Peer already exists for " + guest.TypeDescriptor);
            if (_peers.Count + _reserved >= _limit)
                throw new AndroidPeerQuotaExceededException(_peerType, _limit);
            _reserved++;
        }

        T? peer = null;
        bool added = false;
        try
        {
            peer = factory() ?? throw new InvalidOperationException("Peer factory returned null for " + guest.TypeDescriptor);
            lock (_gate)
            {
                if (_peers.ContainsKey(guest))
                    throw new InvalidOperationException("Peer already exists for " + guest.TypeDescriptor);
                _peers.Add(guest, peer);
                _reserved--;
                added = true;
            }
        }
        finally
        {
            if (!added)
            {
                lock (_gate) _reserved--;
                if (peer is not null) _dispose?.Invoke(peer);
            }
        }
    }
    internal T Get(DexObject guest)
    {
        lock (_gate)
            return _peers.TryGetValue(guest, out var peer)
                ? peer
                : throw new InvalidOperationException("Peer is not initialized for " + guest.TypeDescriptor);
    }
    internal void Clear()
    {
        T[] peers;
        lock (_gate) { peers = _peers.Values.ToArray(); _peers.Clear(); }
        if (_dispose is not null)
            foreach (T peer in peers) _dispose(peer);
    }
}

internal enum BundleValueKind { String, Int, Long, Boolean }
internal sealed record BundleValue(BundleValueKind Kind, object? Value);

internal sealed class BundlePeer
{
    private readonly List<KeyValuePair<string?, BundleValue>> _values = [];
    internal int Count => _values.Count;
    internal bool Contains(string? key) => Find(key) >= 0;
    internal void Put(string? key, BundleValue value)
    {
        int index = Find(key);
        if (index >= 0) _values[index] = new(key, value);
        else _values.Add(new(key, value));
    }
    internal BundleValue? Get(string? key)
    {
        int index = Find(key);
        return index < 0 ? null : _values[index].Value;
    }
    internal void Remove(string? key)
    {
        int index = Find(key);
        if (index >= 0) _values.RemoveAt(index);
    }
    internal void Clear() => _values.Clear();
    internal BundlePeer Copy()
    {
        var copy = new BundlePeer();
        copy._values.AddRange(_values);
        return copy;
    }
    private int Find(string? key) => _values.FindIndex(pair => string.Equals(pair.Key, key, StringComparison.Ordinal));
}

internal sealed class IntentPeer
{
    internal string? Action { get; set; }
    internal BundlePeer Extras { get; } = new();
}

internal sealed class ToastPeer
{
    internal required IAndroidToastNotification Notification { get; init; }
}

/// <summary>Mutable per-instance state for a guest AtomicReference. No real
/// synchronization: the runtime is a single serial execution lane with no
/// concurrent guest threads, so plain reads/writes are indistinguishable from
/// atomic ones here (same reasoning as monitor-enter/exit).</summary>
internal sealed class AtomicReferencePeer
{
    internal object? Value { get; set; }
}

/// <summary>
/// Mutable per-instance map state for a guest WeakHashMap, with two deliberate,
/// documented simplifications (see the binding comment in AndroidApiBindings):
/// keys are strongly referenced (no guest GC model, safe over-approximation) and
/// key equality is CLR equality, not guest-overridden equals()/hashCode().
/// </summary>
internal sealed class WeakHashMapPeer
{
    internal Dictionary<object, object?> Entries { get; } = new();
}

/// <summary>
/// Mutable per-instance map state for a guest HashMap. Distinct from
/// WeakHashMapPeer because real HashMap supports one null key (WeakHashMap
/// conceptually cannot): the null key is mapped onto an internal sentinel so a
/// null-keyed entry behaves like any other. Same key-equality simplification as
/// WeakHashMap (CLR equality, not guest equals()/hashCode()).
/// </summary>
internal sealed class HashMapPeer
{
    private static readonly object NullKey = new();
    private readonly Dictionary<object, object?> _entries = new();

    internal object? Put(object? key, object? value)
    {
        object normalized = key ?? NullKey;
        object? previous = _entries.TryGetValue(normalized, out object? existing) ? existing : null;
        _entries[normalized] = value;
        return previous;
    }
    internal object? Get(object? key) => _entries.TryGetValue(key ?? NullKey, out object? value) ? value : null;
    internal bool ContainsKey(object? key) => _entries.ContainsKey(key ?? NullKey);
    internal object? Remove(object? key)
    {
        object normalized = key ?? NullKey;
        object? removed = _entries.TryGetValue(normalized, out object? value) ? value : null;
        _entries.Remove(normalized);
        return removed;
    }
    internal int Count => _entries.Count;
}

/// <summary>Mutable ordered-list state shared by guest ArrayList and
/// CopyOnWriteArrayList (both are index-ordered List<object?> with nullable
/// elements; the two API classes differ only in binding surface, not shape).</summary>
internal sealed class ListPeer
{
    internal List<object?> Elements { get; } = new();
}

/// <summary>
/// Snapshot iterator state for a guest java.util.Iterator: captures the source
/// collection's elements at iterator() time and walks that snapshot regardless of
/// later mutation — matches CopyOnWriteArrayList's real documented behavior and
/// is correct for ArrayList too in this single-lane runtime.
/// </summary>
internal sealed class IteratorPeer
{
    private readonly object?[] _snapshot;
    private int _position;
    internal IteratorPeer(IEnumerable<object?> snapshot) { _snapshot = snapshot.ToArray(); }
    internal bool HasNext => _position < _snapshot.Length;
    internal object? Next()
    {
        if (_position >= _snapshot.Length)
            throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/util/NoSuchElementException;"));
        return _snapshot[_position++];
    }
}

/// <summary>Per-constant name/ordinal state for a guest java.lang.Enum constant
/// (the receiver is a guest DexObject; the binding owns this state, not the
/// guest's InstanceFields).</summary>
internal sealed record EnumPeer(string Name, int Ordinal);

/// <summary>Mutable per-instance int state for a guest AtomicInteger. No real
/// synchronization: single serial lane, same reasoning as AtomicReferencePeer
/// (README boundary #26).</summary>
internal sealed class AtomicIntegerPeer
{
    internal int Value { get; set; }
}

/// <summary>
/// Per-instance state for a guest java.lang.Thread: name, cooperative interrupt
/// flag/signals, the underlying real CLR thread, and the Runnable target if any.
/// Completion/Interrupt signals are ManualResetEvents so real blocking waits
/// (join/sleep) can be genuinely released and woken.
/// </summary>
internal sealed class ThreadPeer
{
    internal string? Name { get; set; }
    internal Thread? ClrThread { get; set; }
    internal DexObject? Runnable { get; set; }
    internal ManualResetEventSlim Completion { get; } = new(false);
    internal ManualResetEventSlim InterruptSignal { get; } = new(false);
    private volatile int _interrupted;
    internal bool Interrupted { get => _interrupted != 0; set => Volatile.Write(ref _interrupted, value ? 1 : 0); }
    internal void Interrupt()
    {
        Interlocked.Exchange(ref _interrupted, 1);
        InterruptSignal.Set();
    }
}

/// <summary>
/// Mutable referent state for a guest WeakReference. The referent is held
/// STRONGLY: this runtime has no guest GC model (nothing is ever collected
/// early), so strong retention is a safe over-approximation — same class of
/// simplification as WeakHashMap (README boundary #27). This is a GC-model
/// justification, distinct from AtomicReferencePeer's no-locking one.
/// </summary>
internal sealed class WeakReferencePeer
{
    internal object? Value { get; set; }
}

internal static class AndroidFrameworkHierarchy
{
    private static readonly IReadOnlyDictionary<string, string> Parents = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Landroid/app/Activity;"] = "Landroid/view/ContextThemeWrapper;",
        ["Landroid/view/ContextThemeWrapper;"] = "Landroid/content/ContextWrapper;",
        ["Landroid/content/ContextWrapper;"] = "Landroid/content/Context;",
        ["Landroid/content/Context;"] = "Ljava/lang/Object;",
        ["Landroid/app/Application;"] = "Landroid/content/ContextWrapper;",
        ["Landroid/os/Bundle;"] = "Landroid/os/BaseBundle;",
        ["Landroid/os/BaseBundle;"] = "Ljava/lang/Object;",
        ["Landroid/content/ClipboardManager;"] = "Ljava/lang/Object;",
        ["Landroid/content/ClipData;"] = "Ljava/lang/Object;",
        ["Landroid/content/ClipData$Item;"] = "Ljava/lang/Object;",
        ["Landroid/net/ConnectivityManager;"] = "Ljava/lang/Object;",
        ["Landroid/net/Network;"] = "Ljava/lang/Object;",
        ["Landroid/net/NetworkCapabilities;"] = "Ljava/lang/Object;",
        ["Landroid/os/BatteryManager;"] = "Ljava/lang/Object;",
        ["Landroid/os/PowerManager;"] = "Ljava/lang/Object;",
        ["Landroid/view/View;"] = "Ljava/lang/Object;",
        ["Landroid/view/ViewGroup;"] = "Landroid/view/View;",
        ["Landroid/widget/LinearLayout;"] = "Landroid/view/ViewGroup;",
        ["Landroid/widget/TextView;"] = "Landroid/view/View;",
        ["Landroid/widget/Button;"] = "Landroid/widget/TextView;",
        ["Landroid/view/View$OnClickListener;"] = "Ljava/lang/Object;",
        ["Ljava/lang/Throwable;"] = "Ljava/lang/Object;",
        ["Ljava/lang/Error;"] = "Ljava/lang/Throwable;",
        ["Ljava/lang/LinkageError;"] = "Ljava/lang/Error;",
        ["Ljava/lang/IncompatibleClassChangeError;"] = "Ljava/lang/LinkageError;",
        ["Ljava/lang/NoSuchMethodError;"] = "Ljava/lang/IncompatibleClassChangeError;",
        ["Ljava/lang/Exception;"] = "Ljava/lang/Throwable;",
        ["Ljava/lang/RuntimeException;"] = "Ljava/lang/Exception;",
        ["Ljava/lang/NullPointerException;"] = "Ljava/lang/RuntimeException;",
        ["Ljava/lang/ArithmeticException;"] = "Ljava/lang/RuntimeException;",
        ["Ljava/lang/ClassCastException;"] = "Ljava/lang/RuntimeException;",
        ["Ljava/lang/IllegalArgumentException;"] = "Ljava/lang/RuntimeException;",
        ["Ljava/lang/IllegalStateException;"] = "Ljava/lang/RuntimeException;",
        ["Ljava/lang/IllegalThreadStateException;"] = "Ljava/lang/IllegalArgumentException;",
        ["Ljava/lang/SecurityException;"] = "Ljava/lang/RuntimeException;",
        ["Ljava/lang/IndexOutOfBoundsException;"] = "Ljava/lang/RuntimeException;",
        ["Ljava/lang/ArrayIndexOutOfBoundsException;"] = "Ljava/lang/IndexOutOfBoundsException;",
        ["Ljava/lang/NegativeArraySizeException;"] = "Ljava/lang/RuntimeException;",
        ["Ljava/util/NoSuchElementException;"] = "Ljava/lang/RuntimeException;",
        ["Ljava/lang/InterruptedException;"] = "Ljava/lang/Exception;"
    };

    internal static string? ParentOf(string descriptor) => Parents.TryGetValue(descriptor, out var parent) ? parent : null;

    /// <summary>Direct interface relationships for framework/API-bound types (the
    /// classes this runtime binds that implement real Java interfaces), plus the
    /// interface-extends-interface edges. The transitive closure is walked by
    /// IsAssignable's InterfaceClosureContains; Map deliberately has no
    /// super-interface (it does not extend Collection in real Java).</summary>
    private static readonly IReadOnlyDictionary<string, string[]> Interfaces = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Ljava/util/concurrent/CopyOnWriteArraySet;"] = ["Ljava/util/Set;"],
        ["Ljava/util/ArrayList;"] = ["Ljava/util/List;"],
        ["Ljava/util/HashMap;"] = ["Ljava/util/Map;"],
        ["Ljava/util/WeakHashMap;"] = ["Ljava/util/Map;"],
        ["Ljava/util/Set;"] = ["Ljava/util/Collection;"],
        ["Ljava/util/List;"] = ["Ljava/util/Collection;"],
        ["Ljava/util/Collection;"] = ["Ljava/lang/Iterable;"]
    };

    internal static IEnumerable<string> InterfacesOf(string descriptor) =>
        Interfaces.TryGetValue(descriptor, out var interfaces) ? interfaces : Array.Empty<string>();

    internal static bool IsAssignable(string actual, string expected) => IsAssignable(actual, expected, ParentOf, _ => Array.Empty<string>());

    internal static bool IsAssignable(string actual, string expected, Func<string, string?> parentOf) => IsAssignable(actual, expected, parentOf, _ => Array.Empty<string>());

    internal static bool IsAssignable(string actual, string expected, Func<string, string?> parentOf, Func<string, IEnumerable<string>> interfacesOf)
    {
        if (actual == expected) return true;
        bool actualArray = actual.StartsWith("[", StringComparison.Ordinal);
        bool expectedArray = expected.StartsWith("[", StringComparison.Ordinal);
        if (actualArray)
        {
            if (expected is "Ljava/lang/Object;" or "Ljava/lang/Cloneable;" or "Ljava/io/Serializable;") return true;
            if (!expectedArray) return false;
            string actualComponent = actual[1..];
            string expectedComponent = expected[1..];
            bool actualPrimitive = IsPrimitiveComponent(actualComponent);
            bool expectedPrimitive = IsPrimitiveComponent(expectedComponent);
            if (actualPrimitive || expectedPrimitive) return actualPrimitive && expectedPrimitive && actualComponent == expectedComponent;
            return IsAssignable(actualComponent, expectedComponent, parentOf, interfacesOf);
        }
        if (expectedArray) return false;
        if (expected == "Ljava/lang/Object;") return true;
        if (expected == "Ljava/lang/CharSequence;" && actual is "Ljava/lang/String;" or "Ljava/lang/StringBuilder;") return true;
        // Superclass chain AND, per class in that chain (including actual itself),
        // the transitively-closed interface set: a concrete class is assignable to
        // an interface it (or any ancestor class) implements, and an interface is
        // assignable to an interface it extends.
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var interfaceVisited = new HashSet<string>(StringComparer.Ordinal);
        for (string? current = actual; current is not null && visited.Add(current); current = parentOf(current))
        {
            if (current == expected) return true;
            if (InterfaceClosureContains(current, expected, interfacesOf, interfaceVisited, 0)) return true;
        }
        return false;
    }

    private const int MaxInterfaceDepth = 128;

    /// <summary>Walks the transitive closure of a class/interface's declared
    /// interfaces, cycle-safe via the visited set and bounded by MaxInterfaceDepth —
    /// malformed/adversarial DEX with a cyclic or absurdly deep interface graph fails
    /// closed instead of hanging or overflowing the stack.</summary>
    private static bool InterfaceClosureContains(string descriptor, string expected, Func<string, IEnumerable<string>> interfacesOf, HashSet<string> visited, int depth)
    {
        if (depth >= MaxInterfaceDepth)
            throw new InvalidDataException("DEX interface hierarchy exceeds the bounded depth.");
        foreach (string iface in interfacesOf(descriptor))
        {
            if (!visited.Add(iface)) continue;
            if (iface == expected) return true;
            if (InterfaceClosureContains(iface, expected, interfacesOf, visited, depth + 1)) return true;
        }
        return false;
    }

    private static bool IsPrimitiveComponent(string descriptor) => descriptor.Length == 1 && "ZBCSIFJD".IndexOf(descriptor[0]) >= 0;
}
