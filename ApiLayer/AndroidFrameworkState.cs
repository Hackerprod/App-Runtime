#nullable enable
using System.Collections.Concurrent;
using System.Text;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;
using AndroidRuntime.Core.Apk;
using AndroidRuntime.Core.Ui;

namespace AndroidRuntime.Core.ApiLayer;

public sealed record AndroidPeerLimits
{
    public AndroidPeerLimits(int maxStringBuilders = 256, int maxBundles = 256, int maxIntents = 256, int maxToasts = 64, int maxViews = 4096, int maxAtomicReferences = 256, int maxWeakHashMaps = 256, int maxHashMaps = 256, int maxArrayLists = 256, int maxWeakReferences = 256, int maxCopyOnWriteArraySets = 256, int maxIterators = 256, int maxCopyOnWriteArrayLists = 256, int maxEnums = 256, int maxAtomicIntegers = 256, int maxThreads = 64, int maxExecutorServices = 16, int maxFutures = 256, int maxLoopers = 16, int maxHandlers = 256, int maxMethods = 1024, int maxBoxed = 1024, int maxMapEntries = 1024, int maxMapViews = 256, int maxLazies = 256, int maxArrayDeques = 256, int maxLinkedHashSets = 256, int maxLinkedHashMaps = 256, int maxConcurrentHashMaps = 256, int maxSharedPreferences = 64, int maxSharedPreferencesEditors = 64)
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
        ExecutorServices = maxExecutorServices;
        Futures = maxFutures;
        Loopers = maxLoopers;
        Handlers = maxHandlers;
        Methods = maxMethods;
        Boxed = maxBoxed;
        MapEntries = maxMapEntries;
        MapViews = maxMapViews;
        Lazies = maxLazies;
        ArrayDeques = maxArrayDeques;
        LinkedHashSets = maxLinkedHashSets;
        LinkedHashMaps = maxLinkedHashMaps;
        ConcurrentHashMaps = maxConcurrentHashMaps;
        SharedPreferences = maxSharedPreferences;
        SharedPreferencesEditors = maxSharedPreferencesEditors;
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
    public int ExecutorServices { get; }
    public int Futures { get; }
    public int Loopers { get; }
    public int Handlers { get; }
    public int Methods { get; }
    public int Boxed { get; }
    public int MapEntries { get; }
    public int MapViews { get; }
    public int Lazies { get; }
    public int ArrayDeques { get; }
    public int LinkedHashSets { get; }
    public int LinkedHashMaps { get; }
    public int ConcurrentHashMaps { get; }
    public int SharedPreferences { get; }
    public int SharedPreferencesEditors { get; }

    public void Validate()
    {
        if (StringBuilders <= 0 || Bundles <= 0 || Intents <= 0 || Toasts <= 0 || Views <= 0 || AtomicReferences <= 0 || WeakHashMaps <= 0 || HashMaps <= 0 || ArrayLists <= 0 || WeakReferences <= 0 || CopyOnWriteArraySets <= 0 || Iterators <= 0 || CopyOnWriteArrayLists <= 0 || Enums <= 0 || AtomicIntegers <= 0 || Threads <= 0 || ExecutorServices <= 0 || Futures <= 0 || Loopers <= 0 || Handlers <= 0 || Methods <= 0 || Boxed <= 0 || MapEntries <= 0 || MapViews <= 0 || Lazies <= 0 || ArrayDeques <= 0 || LinkedHashSets <= 0 || LinkedHashMaps <= 0 || ConcurrentHashMaps <= 0 || SharedPreferences <= 0 || SharedPreferencesEditors <= 0)
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
        Resources = resources;
        StringBuilders = new AndroidPeerStore<StringBuilder>("StringBuilder", PeerLimits.StringBuilders);
        Bundles = new AndroidPeerStore<BundlePeer>("Bundle", PeerLimits.Bundles);
        Intents = new AndroidPeerStore<IntentPeer>("Intent", PeerLimits.Intents);
        Toasts = new AndroidPeerStore<ToastPeer>("Toast", PeerLimits.Toasts, peer => peer.Notification.Dispose());
        AtomicReferences = new AndroidPeerStore<AtomicReferencePeer>("AtomicReference", PeerLimits.AtomicReferences);
        AtomicBooleans = new AndroidPeerStore<AtomicBooleanPeer>("AtomicBoolean", PeerLimits.AtomicIntegers);
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
        ExecutorServices = new AndroidPeerStore<ExecutorServicePeer>("ExecutorService", PeerLimits.ExecutorServices, peer => peer.DisposeWorkers());
        Futures = new AndroidPeerStore<FuturePeer>("Future", PeerLimits.Futures);
        Loopers = new AndroidPeerStore<LooperPeer>("Looper", PeerLimits.Loopers, peer => peer.Quit());
        Handlers = new AndroidPeerStore<HandlerPeer>("Handler", PeerLimits.Handlers);
        Methods = new AndroidPeerStore<MethodPeer>("Method", PeerLimits.Methods);
        Boxed = new AndroidPeerStore<BoxedPeer>("Boxed", PeerLimits.Boxed);
        MapEntries = new AndroidPeerStore<MapEntryPeer>("MapEntry", PeerLimits.MapEntries);
        MapViews = new AndroidPeerStore<HashSet<object?>>("MapView", PeerLimits.MapViews);
        Lazies = new AndroidPeerStore<LazyPeer>("Lazy", PeerLimits.Lazies);
        ArrayDeques = new AndroidPeerStore<ListPeer>("ArrayDeque", PeerLimits.ArrayDeques);
        LinkedHashSets = new AndroidPeerStore<OrderedSetPeer>("LinkedHashSet", PeerLimits.LinkedHashSets);
        LinkedHashMaps = new AndroidPeerStore<LinkedHashMapPeer>("LinkedHashMap", PeerLimits.LinkedHashMaps);
        ConcurrentHashMaps = new AndroidPeerStore<ConcurrentHashMapPeer>("ConcurrentHashMap", PeerLimits.ConcurrentHashMaps);
        SharedPreferences = new AndroidPeerStore<SharedPreferencesPeer>("SharedPreferences", PeerLimits.SharedPreferences);
        SharedPreferencesEditors = new AndroidPeerStore<SharedPreferencesEditorPeer>("SharedPreferences.Editor", PeerLimits.SharedPreferencesEditors);
        ApplicationContext = new DexObject("Landroid/app/Application;");
        LauncherIntent = new DexObject("Landroid/content/Intent;");
        TypedArrayObject = new DexObject("Landroid/content/res/TypedArray;");
        Intents.Add(LauncherIntent, new IntentPeer { Action = "android.intent.action.MAIN" });
        InitializeTimeUnitConstants();
    }

    public string SessionId { get; }
    public string PackageName { get; }
    public string ActivityDescriptor { get; }
    public int MinimumLogPriority { get; }
    public DexObject ApplicationContext { get; }
    public DexObject LauncherIntent { get; }
    public DexObject TypedArrayObject { get; }
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
    internal AndroidPeerStore<AtomicBooleanPeer> AtomicBooleans { get; }
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
    internal AndroidPeerStore<ExecutorServicePeer> ExecutorServices { get; }
    internal AndroidPeerStore<FuturePeer> Futures { get; }
    internal AndroidPeerStore<LooperPeer> Loopers { get; }
    internal AndroidPeerStore<HandlerPeer> Handlers { get; }
    internal AndroidPeerStore<MethodPeer> Methods { get; }
    internal AndroidPeerStore<BoxedPeer> Boxed { get; }
    internal AndroidPeerStore<MapEntryPeer> MapEntries { get; }
    internal AndroidPeerStore<HashSet<object?>> MapViews { get; }
    internal AndroidPeerStore<LazyPeer> Lazies { get; }
    internal AndroidPeerStore<ListPeer> ArrayDeques { get; }
    internal AndroidPeerStore<OrderedSetPeer> LinkedHashSets { get; }
    internal AndroidPeerStore<LinkedHashMapPeer> LinkedHashMaps { get; }
    internal AndroidPeerStore<ConcurrentHashMapPeer> ConcurrentHashMaps { get; }
    /// <summary>The session's GIL: shared by the interpreter and every binding that
    /// must release it around real blocking (sleep/join/monitor-enter/class-init
    /// wait). AndroidAppRuntime replaces this with the execution lane's GIL.</summary>
    internal AndroidGil Gil { get; set; } = new();
    /// <summary>The hosted execution lane, when the session runs under a host. The
    /// main Looper's Handler.post reuses this lane's existing queue (the lane IS
    /// already a message loop); null for standalone/synchronous sessions.</summary>
    internal AndroidExecutionLane? Lane { get; set; }
    /// <summary>The session interpreter, attached after construction so bindings
    /// that spawn guest work (Thread.start) can dispatch into it.</summary>
    internal DexInterpreter? Interpreter { get; private set; }
    /// <summary>Per-real-thread Looper association for Looper.prepare()/myLooper()/loop().</summary>
    internal ConcurrentDictionary<int, DexObject> ThreadLoopers { get; } = new();
    /// <summary>The stable main Looper peer (created lazily by getMainLooper).</summary>
    internal DexObject? MainLooperObject { get; private set; }
    internal LooperPeer? MainLooperPeer { get; private set; }
    /// <summary>The stable guest Thread object for the main guest thread, seeded by
    /// JavaLangThreadBindings.InitializeMainGuestThread before any guest code runs.</summary>
    internal DexObject? MainThreadObject { get; set; }
    /// <summary>Framework singleton objects for java.util.concurrent.TimeUnit constants.</summary>
    internal DexObject[] TimeUnitObjects { get; private set; } = [];
    internal IReadOnlyDictionary<string, DexObject> TimeUnitByName { get; private set; } = new Dictionary<string, DexObject>(StringComparer.Ordinal);
    internal IReadOnlyDictionary<DexObject, TimeUnitConstantPeer> TimeUnitByObject { get; private set; } = new Dictionary<DexObject, TimeUnitConstantPeer>(ReferenceEqualityComparer.Instance);
    /// <summary>The singleton guest ThreadFactory object returned by Executors.defaultThreadFactory().</summary>
    internal DexObject? DefaultThreadFactory { get; set; }
    /// <summary>Maps each synthetic pool worker Runnable to its owning pool so the
    /// worker body binding can find it. Guarded by WorkerRunnablesGate; written at
    /// worker spawn, read once by the worker thread on start.</summary>
    internal Dictionary<DexObject, ExecutorServicePeer> WorkerRunnables { get; } = new(ReferenceEqualityComparer.Instance);
    internal object WorkerRunnablesGate { get; } = new();
    internal ActivityWindowPeers WindowPeers { get; }
    internal DexObject? Activity { get; private set; }
    /// <summary>The stable per-session Resources facade object returned by
    /// Context.getResources(). Reads resolve through the APK resource resolver;
    /// the facade object itself is stateless.</summary>
    internal DexObject ResourcesObject { get; } = new("Landroid/content/res/Resources;");
    /// <summary>The stable en-US Locale singleton returned by LocaleList/get().
    /// Real Android has per-locale objects; one honest neutral locale is enough
    /// for this runtime (no localization pipeline).</summary>
    internal DexObject LocaleObject { get; } = new("Ljava/util/Locale;");
    /// <summary>Stable Window facade returned by Activity.getWindow(); content
    /// plumbing lives in the UI session, the object is stateless.</summary>
    internal DexObject WindowObject { get; } = new("Landroid/view/Window;");
    /// <summary>The guest Window.Callback installed via Window.setCallback
    /// (appcompat registers its delegate); null until set. Guarded by the
    /// execution lane (single-threaded guest access).</summary>
    internal DexObject? WindowCallback { get; set; }
    /// <summary>Stable DecorView facade returned by Window.getDecorView().</summary>
    internal DexObject DecorViewObject { get; } = new("Landroid/widget/FrameLayout;");
    /// <summary>Stable WindowManager.LayoutParams facade returned by
    /// Window.getAttributes(); mutable fields default to 0 via iget.</summary>
    internal DexObject WindowAttributesObject { get; } = new("Landroid/view/WindowManager$LayoutParams;");
    /// <summary>The stable ThreadLocalRandom facade object returned by
    /// ThreadLocalRandom.current(); random draws use a shared lock-protected
    /// CLR Random (single execution lane, so contention is negligible).</summary>
    internal DexObject ThreadLocalRandomObject { get; } = new("Ljava/util/concurrent/ThreadLocalRandom;");
    internal Random ThreadLocalRandomSource { get; } = new();
    /// <summary>Per-session SharedPreferences stores, keyed by preference-file
    /// name (real Android persists to disk; the runtime keeps an in-memory
    /// session store so getSharedPreferences flows work end-to-end).</summary>
    internal AndroidPeerStore<SharedPreferencesPeer> SharedPreferences { get; }
    /// <summary>Per-session SharedPreferences.Editor peers, one per edit() call
    /// (real Android creates a new EditorImpl per edit(); the peer holds the
    /// pending writes until apply()/commit()).</summary>
    internal AndroidPeerStore<SharedPreferencesEditorPeer> SharedPreferencesEditors { get; }
    /// <summary>Guest SharedPreferences facade objects per preference-file name
    /// (stable identity per file, same reasoning as other facades).</summary>
    internal Dictionary<string, DexObject> SharedPreferenceObjects { get; } = new(StringComparer.Ordinal);
    internal DexObject EnsureSharedPreferences(string name)
    {
        lock (_classCacheGate)
        {
            if (!SharedPreferenceObjects.TryGetValue(name, out var existing))
            {
                existing = new DexObject("Landroid/content/SharedPreferences;");
                SharedPreferenceObjects[name] = existing;
                SharedPreferences.Add(existing, new SharedPreferencesPeer());
            }
            return existing;
        }
    }
    /// <summary>Stable OnBackInvokedDispatcher facade; back dispatch is handled
    /// host-side, so registration is a no-op.</summary>
    internal DexObject OnBackInvokedDispatcherObject { get; } = new("Landroid/window/OnBackInvokedDispatcher;");
    /// <summary>Stable legacy FragmentManager facade returned by
    /// Activity.getFragmentManager(); findFragmentByTag answers null so
    /// lifecycle-reporting probes proceed.</summary>
    internal DexObject FragmentManagerObject { get; } = new("Landroid/app/FragmentManager;");
    /// <summary>Stable no-op FragmentTransaction facade (ReportFragment injection).</summary>
    internal DexObject FragmentTransactionObject { get; } = new("Landroid/app/FragmentTransaction;");
    /// <summary>Canonical ComponentName per activity descriptor (stable identity
    /// per class, same reasoning as the Class cache).</summary>
    private readonly Dictionary<string, DexObject> _componentNames = new(StringComparer.Ordinal);
    internal DexObject EnsureComponentName(string activityDescriptor)
    {
        lock (_classCacheGate)
        {
            if (!_componentNames.TryGetValue(activityDescriptor, out var existing))
            {
                existing = new DexObject("Landroid/content/ComponentName;");
                existing.InstanceFields["_packageName"] = PackageName;
                existing.InstanceFields["_className"] = "L" == activityDescriptor[..1]
                    ? activityDescriptor.Substring(1, activityDescriptor.Length - 2).Replace('/', '.')
                    : activityDescriptor;
                _componentNames[activityDescriptor] = existing;
            }
            return existing;
        }
    }
    /// <summary>The per-session APK resource resolver (null when no APK resources
    /// were attached, e.g. standalone test sessions).</summary>
    internal AndroidResourceResolver? Resources { get; }
    /// <summary>The stable per-session LayoutInflater facade object returned by
    /// LayoutInflater.from(Context) (real Android caches one per Context; the
    /// facade itself is stateless here because inflation state lives in the UI
    /// session).</summary>
    internal DexObject LayoutInflaterObject { get; } = new("Landroid/view/LayoutInflater;");
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
        // Framework static-field reads (e.g. TimeUnit.SECONDS via sget) resolve
        // through this hook: framework classes have no DEX <clinit>/field table.
        interpreter.FrameworkStaticField = ResolveFrameworkStaticField;
        // const-class and Object.getClass() produce Class objects through the SAME
        // canonical per-descriptor cache, so reference identity holds (real Java
        // compares Class objects with == / equals identity).
        interpreter.ClassObjectProvider = EnsureClassObject;
    }

    // ---------------------------------------------------------------------------
    // java.lang.Class canonical cache
    // ---------------------------------------------------------------------------

    private readonly object _classCacheGate = new();
    private readonly Dictionary<string, DexObject> _classObjects = new(StringComparer.Ordinal);
    private readonly Dictionary<DexObject, ClassPeer> _classPeers = new(ReferenceEqualityComparer.Instance);

    /// <summary>Returns the canonical Class object for a type descriptor: the same
    /// DexObject for the same descriptor from any code path (const-class,
    /// Object.getClass, Enum.getDeclaringClass, getSuperclass). Real Java compares
    /// Class objects by identity, so one object per descriptor is REQUIRED for
    /// correctness, not a style choice. The cache is bounded by the number of
    /// distinct type descriptors actually referenced/constructed in the session —
    /// no quota store needed (the mutable-peer quota pattern doesn't apply to a
    /// cache that cannot grow beyond the loaded type universe).</summary>
    internal DexObject EnsureClassObject(string descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor);
        lock (_classCacheGate)
        {
            if (_classObjects.TryGetValue(descriptor, out var existing)) return existing;
            var classObject = new DexObject("Ljava/lang/Class;");
            _classObjects[descriptor] = classObject;
            _classPeers[classObject] = new ClassPeer(descriptor);
            return classObject;
        }
    }

    internal ClassPeer ClassPeerOf(DexObject classObject)
    {
        lock (_classCacheGate)
            return _classPeers.TryGetValue(classObject, out var peer)
                ? peer
                : throw new InvalidOperationException("Class peer is not initialized for " + classObject);
    }

    // ---------------------------------------------------------------------------
    // java.lang.Package canonical cache
    // ---------------------------------------------------------------------------

    private readonly object _packageCacheGate = new();
    private readonly Dictionary<string, DexObject> _packageObjects = new(StringComparer.Ordinal);
    private readonly Dictionary<DexObject, PackagePeer> _packagePeers = new(ReferenceEqualityComparer.Instance);

    /// <summary>Canonical Package object per package name (same identity reasoning
    /// as the Class cache: real code compares Package references).</summary>
    internal DexObject EnsurePackageObject(string packageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        lock (_packageCacheGate)
        {
            if (_packageObjects.TryGetValue(packageName, out var existing)) return existing;
            var packageObject = new DexObject("Ljava/lang/Package;");
            _packageObjects[packageName] = packageObject;
            _packagePeers[packageObject] = new PackagePeer(packageName);
            return packageObject;
        }
    }

    internal PackagePeer PackagePeerOf(DexObject packageObject)
    {
        lock (_packageCacheGate)
            return _packagePeers.TryGetValue(packageObject, out var peer)
                ? peer
                : throw new InvalidOperationException("Package peer is not initialized for " + packageObject);
    }

    /// <summary>Materializes the TimeUnit constants as stable framework singletons
    /// (JDK enum semantics OUTSIDE the guest Enum machinery — see README). The
    /// seven real JDK constants in ordinal order.</summary>
    private void InitializeTimeUnitConstants()
    {
        var objects = new DexObject[TimeUnitDefinitions.Length];
        var byName = new Dictionary<string, DexObject>(StringComparer.Ordinal);
        var byObject = new Dictionary<DexObject, TimeUnitConstantPeer>(ReferenceEqualityComparer.Instance);
        for (int index = 0; index < TimeUnitDefinitions.Length; index++)
        {
            var constant = new DexObject("Ljava/util/concurrent/TimeUnit;");
            objects[index] = constant;
            byName[TimeUnitDefinitions[index].Name] = constant;
            byObject[constant] = new TimeUnitConstantPeer(TimeUnitDefinitions[index].Name, index, TimeUnitDefinitions[index].NanosPerUnit);
        }
        TimeUnitObjects = objects;
        TimeUnitByName = byName;
        TimeUnitByObject = byObject;
    }

    private static readonly (string Name, long NanosPerUnit)[] TimeUnitDefinitions =
    [
        ("NANOSECONDS", 1L),
        ("MICROSECONDS", 1_000L),
        ("MILLISECONDS", 1_000_000L),
        ("SECONDS", 1_000_000_000L),
        ("MINUTES", 60_000_000_000L),
        ("HOURS", 3_600_000_000_000L),
        ("DAYS", 86_400_000_000_000L)
    ];

    internal object? ResolveFrameworkStaticField(string classDescriptor, string fieldName)
    {
        if (classDescriptor == "Ljava/util/concurrent/TimeUnit;")
            return TimeUnitByName.TryGetValue(fieldName, out var constant) ? constant : null;
        // Boxed-primitive static fields: Boolean.TRUE/FALSE singletons and the
        // per-type TYPE class constants (real Java: Integer.TYPE == int.class, etc.).
        if (classDescriptor == "Ljava/lang/Boolean;")
        {
            if (fieldName == "TRUE") return BoxedObject("Ljava/lang/Boolean;", 1);
            if (fieldName == "FALSE") return BoxedObject("Ljava/lang/Boolean;", 0);
        }
        if (fieldName == "TYPE")
        {
            string? primitive = classDescriptor switch
            {
                "Ljava/lang/Boolean;" => "Z",
                "Ljava/lang/Byte;" => "B",
                "Ljava/lang/Character;" => "C",
                "Ljava/lang/Short;" => "S",
                "Ljava/lang/Integer;" => "I",
                "Ljava/lang/Long;" => "J",
                "Ljava/lang/Float;" => "F",
                "Ljava/lang/Double;" => "D",
                _ => null
            };
            if (primitive is not null) return EnsureClassObject(primitive);
        }
        // android.os.Build fields: the host is not an Android device, so identity
        // fields report a bounded, honest neutral value (not fabricated hardware),
        // and SDK_INT reports the session's targetSdkVersion so SDK-gated code
        // follows the app's own declared target. Real apps commonly call
        // Build.MANUFACTURER.toLowerCase() etc. for vendor checks; a string (never
        // null) keeps those paths alive without lying about the device.
        if (classDescriptor == "Landroid/os/Build$VERSION;")
        {
            if (fieldName == "SDK_INT") return TargetSdkVersion;
        }
        if (classDescriptor == "Landroid/os/Build;")
        {
            return fieldName switch
            {
                "MANUFACTURER" => "unknown",
                "BRAND" => "unknown",
                "MODEL" => "AndroidRuntime",
                "DEVICE" => "generic",
                "PRODUCT" => "generic",
                "BOARD" => "generic",
                "HARDWARE" => "generic",
                "FINGERPRINT" => "generic",
                "TAGS" => "release-keys",
                "TYPE" => "user",
                "USER" => "android-build",
                "HOST" => "android-build",
                "SUPPORTED_ABIS" => null,
                _ => null
            };
        }
        // java.util.Locale static constants (Locale.US, Locale.ENGLISH, etc.):
        // canonical stable singletons per constant, resolved through the same
        // framework static-field hook as TimeUnit/Build constants (sget path).
        // Only the probe-referenced constants are materialized; unknown names
        // resolve null (honest — the class has no other static fields modeled).
        if (classDescriptor == "Ljava/util/Locale;")
            return EnsureLocaleConstant(fieldName);
        return null;
    }

    private readonly object _localeConstantGate = new();
    private readonly Dictionary<string, DexObject> _localeConstants = new(StringComparer.Ordinal);

    /// <summary>Returns the canonical Locale constant for a referenced name
    /// (Locale.US/ENGLISH/KOREAN/ROOT, per the APK probe), creating it once with
    /// the REAL values (libcore Locale constants: US=en_US, ENGLISH=en,
    /// KOREAN=ko, ROOT=""). The same DexObject is returned for the same name so
    /// reference identity holds (real Java static final constants). Unknown
    /// names return null.</summary>
    internal DexObject? EnsureLocaleConstant(string name)
    {
        lock (_localeConstantGate)
        {
            if (_localeConstants.TryGetValue(name, out var existing)) return existing;
            (string language, string country) = name switch
            {
                "US" => ("en", "US"),
                "ENGLISH" => ("en", string.Empty),
                "KOREAN" => ("ko", string.Empty),
                "ROOT" => (string.Empty, string.Empty),
                _ => (null!, null!)
            };
            if (language is null) return null;
            var constant = new DexObject("Ljava/util/Locale;");
            constant.InstanceFields["language"] = language;
            constant.InstanceFields["country"] = country;
            constant.InstanceFields["variant"] = string.Empty;
            _localeConstants[name] = constant;
            return constant;
        }
    }

    // ---------------------------------------------------------------------------
    // Boxed-primitive caches (real JDK documented ranges — see README boundary #44)
    // ---------------------------------------------------------------------------

    private readonly object _boxedGate = new();
    private readonly Dictionary<(string Type, long Key), DexObject> _boxedCache = new();

    /// <summary>
    /// Materializes a boxed primitive with the REAL JDK valueOf caching contract:
    /// Boolean always returns one of two singletons; Integer/Short/Byte/Long
    /// cache -128..127; Character caches 0..127 (unsigned asymmetry, per the JDK
    /// spec); Double/Float NEVER cache. Cached entries are the same object for
    /// the same (type, value); outside the range a fresh object is created
    /// (identity then differs, equals is value-based and still works).
    /// </summary>
    internal DexObject BoxedObject(string type, object rawValue)
    {
        bool cacheable = type switch
        {
            "Ljava/lang/Boolean;" => true,
            "Ljava/lang/Integer;" or "Ljava/lang/Short;" or "Ljava/lang/Byte;" => (int)rawValue is >= -128 and <= 127,
            "Ljava/lang/Character;" => (int)rawValue is >= 0 and <= 127,
            "Ljava/lang/Long;" => (long)rawValue is >= -128 and <= 127,
            _ => false
        };
        if (cacheable)
        {
            long key = type == "Ljava/lang/Long;" ? (long)rawValue : (int)rawValue;
            lock (_boxedGate)
            {
                if (_boxedCache.TryGetValue((type, key), out var existing)) return existing;
                var created = new DexObject(type);
                Boxed.Add(created, new BoxedPeer(rawValue));
                _boxedCache[(type, key)] = created;
                return created;
            }
        }
        var fresh = new DexObject(type);
        Boxed.Add(fresh, new BoxedPeer(rawValue));
        return fresh;
    }

    /// <summary>Returns the stable main Looper peer, creating it lazily. Under a
    /// hosted lane the main Looper dispatches onto the lane's own queue (the lane
    /// already is a message loop); without a lane (standalone/tests) a dedicated
    /// background pump drains a private queue so posts still run asynchronously.</summary>
    internal DexObject EnsureMainLooper()
    {
        if (MainLooperObject is not null) return MainLooperObject;
        var looper = new DexObject("Landroid/os/Looper;");
        var peer = new LooperPeer { IsMain = true, Queue = new AndroidMessageQueue(), ThreadObject = MainThreadObject };
        Loopers.Add(looper, peer);
        MainLooperObject = looper;
        MainLooperPeer = peer;
        if (Lane is null)
            peer.PumpThread = new Thread(() =>
            {
                try
                {
                    Gil.Enter();
                    try { AndroidOsHandlerBindings.RunPump(this, peer); }
                    finally { Gil.Exit(); }
                }
                catch (Exception error) { peer.TerminalException = error; }
            })
            {
                IsBackground = true,
                Name = "AndroidRuntime-MainLooper"
            };
        if (peer.PumpThread is not null) peer.PumpThread.Start();
        return looper;
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
        AtomicBooleans.Clear();
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
        ExecutorServices.Clear();
        Futures.Clear();
        Loopers.Clear();
        Handlers.Clear();
        Methods.Clear();
        Boxed.Clear();
        MapEntries.Clear();
        MapViews.Clear();
        Lazies.Clear();
        ArrayDeques.Clear();
        LinkedHashSets.Clear();
        LinkedHashMaps.Clear();
        ConcurrentHashMaps.Clear();
        SharedPreferences.Clear();
        SharedPreferencesEditors.Clear();
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
    internal bool TryGet(DexObject guest, out T peer)
    {
        lock (_gate)
            return _peers.TryGetValue(guest, out peer!);
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
    private readonly Dictionary<object, object?> _entries;

    internal HashMapPeer() => _entries = new();
    /// <summary>Wrapper over a SHARED backing dictionary (Collections.unmodifiableMap):
    /// the wrapper and its backing map see the same entries.</summary>
    internal HashMapPeer(Dictionary<object, object?> shared) => _entries = shared;
    /// <summary>True for Collections.unmodifiableMap/emptyMap/singletonMap wrappers:
    /// writes throw UnsupportedOperationException, reads delegate.</summary>
    internal bool Unmodifiable { get; set; }
    internal void RequireMutable()
    {
        if (Unmodifiable)
            throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/UnsupportedOperationException;"));
    }

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
    internal void Clear() => _entries.Clear();
    /// <summary>Exposes the backing dictionary for a shared-wrapper (unmodifiableMap
    /// wraps the same dictionary as its backing map).</summary>
    internal Dictionary<object, object?> SharedEntries() => _entries;
    /// <summary>Enumerates the map's key/value pairs, translating the null-key
    /// sentinel back to null on the way out (mirrors Get/Remove) so views can
    /// consume the same shape as WeakHashMapPeer.Entries.</summary>
    internal IEnumerable<KeyValuePair<object?, object?>> Entries()
    {
        foreach (var entry in _entries)
            yield return new KeyValuePair<object?, object?>(entry.Key == NullKey ? null : entry.Key, entry.Value);
    }
}

/// <summary>Mutable ordered-list state shared by guest ArrayList and
/// CopyOnWriteArrayList (both are index-ordered List<object?> with nullable
/// elements; the two API classes differ only in binding surface, not shape).
/// Unmodifiable marks an immutable wrapper (Collections.unmodifiableList/
/// emptyList/singletonList) whose writes throw UnsupportedOperationException.</summary>
internal sealed class ListPeer
{
    internal List<object?> Elements { get; }
    internal ListPeer() => Elements = new();
    /// <summary>Wrapper over a SHARED backing list (Collections.unmodifiableList):
    /// the wrapper and its backing list see the same elements.</summary>
    internal ListPeer(List<object?> shared) => Elements = shared;
    internal bool Unmodifiable { get; set; }
    internal void RequireMutable()
    {
        if (Unmodifiable)
            throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/UnsupportedOperationException;"));
    }
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

/// <summary>Mutable per-instance bool state for a guest AtomicBoolean. Same
/// single-serial-lane reasoning as AtomicIntegerPeer.</summary>
internal sealed class AtomicBooleanPeer
{
    internal bool Value { get; set; }
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
    internal Exception? TerminalException { get; set; }
    internal ManualResetEventSlim Completion { get; } = new(false);
    internal ManualResetEventSlim InterruptSignal { get; } = new(false);
    private int _interrupted;
    internal bool Interrupted { get => Volatile.Read(ref _interrupted) != 0; set => Volatile.Write(ref _interrupted, value ? 1 : 0); }
    internal void Interrupt()
    {
        Interlocked.Exchange(ref _interrupted, 1);
        InterruptSignal.Set();
    }
}

/// <summary>Per-constant identity/scale for a java.util.concurrent.TimeUnit constant.
/// TimeUnit is a JDK enum — the constants are framework singletons, deliberately
/// OUTSIDE the guest Enum machinery (which only covers DEX-defined enums).</summary>
internal sealed record TimeUnitConstantPeer(string Name, int Ordinal, long NanosPerUnit);

/// <summary>Identity state for a guest java.lang.Class object: WHICH type it
/// represents. The Class DexObject's own TypeDescriptor is always
/// "Ljava/lang/Class;" — the represented type lives here.</summary>
internal sealed record ClassPeer(string RepresentedDescriptor);

/// <summary>Identity state for a guest java.lang.Package object.</summary>
internal sealed record PackagePeer(string Name);

/// <summary>
/// Identity state for a guest java.lang.reflect.Method object: the underlying
/// method's declaring class, name, and descriptor. Real Java does NOT guarantee
/// Method reference identity across separate reflective calls, so a fresh
/// object per getDeclaredMethods() call is correct — no canonical cache needed.
/// </summary>
internal sealed record MethodPeer(string DeclaringClassDescriptor, string Name, string Descriptor);

/// <summary>
/// Underlying raw value of a guest boxed primitive (java.lang.Boolean/Integer/
/// Long/Short/Byte/Character/Double/Float). The boxed object's own
/// TypeDescriptor is the box type; this peer holds the value. Equality for the
/// boxes is VALUE-based (unlike Class/Enum identity) — the binding compares
/// RawValue, never object identity.
/// </summary>
internal sealed class BoxedPeer
{
    internal BoxedPeer(object rawValue) => RawValue = rawValue;
    internal object RawValue { get; }
}

/// <summary>Snapshot state for a guest java.util.Map.Entry object: the key/value
/// captured when the view was created. Read-only (getKey/getValue); setValue is
/// not bound — the view is snapshot-only, no write-through to the backing map.</summary>
internal sealed record MapEntryPeer(object? Key, object? Value);

/// <summary>
/// State for a guest kotlin.Lazy delegate: the Function0 initializer and the
/// computed value. No synchronization is needed under this runtime — the GIL
/// serializes all guest bytecode execution, so a plain "compute once and cache"
/// is correct under all three real LazyThreadSafetyMode values (SYNCHRONIZED/
/// PUBLICATION/NONE): no observable difference exists under this execution
/// model (same reasoning as monitor-enter/AtomicReference).
/// </summary>
internal sealed class LazyPeer
{
    internal required DexObject Function0 { get; init; }
    internal bool Computed { get; set; }
    internal object? CachedValue { get; set; }
}

/// <summary>
/// State for a guest java.util.LinkedHashSet: ORDER-PRESERVING set semantics.
/// Deliberately NOT the raw HashSet<object?> peer used by CopyOnWriteArraySet —
/// .NET does not reliably guarantee enumeration order for HashSet. A
/// List<object?> keeps insertion order and a linear Contains check before Add
/// preserves no-duplicate set semantics; obviously correct and fast enough at
/// this runtime's bounded quotas. A duplicate add returns false and does NOT
/// move the element (insertion order of first occurrence, not most-recent-touch
/// — unlike LinkedHashMap's optional access-order mode, which does not apply).
/// Remove then re-add DOES move the element to the end (fresh insertion).
/// </summary>
internal sealed class OrderedSetPeer
{
    internal List<object?> Elements { get; } = new();
    internal bool Add(object? value)
    {
        if (Elements.Contains(value)) return false;
        Elements.Add(value);
        return true;
    }
    internal bool Remove(object? value) => Elements.Remove(value);
    internal int Count => Elements.Count;
    internal void Clear() => Elements.Clear();
}

/// <summary>
/// Ordered map state for a guest java.util.LinkedHashMap. Storage mirrors
/// HashMapPeer (private dictionary + null-key sentinel, CLR equality for keys);
/// a parallel key-order list preserves iteration order. accessOrder=false
/// (default): insertion order, get/put on an existing key does NOT reorder
/// (same as LinkedHashSet). accessOrder=true: ACCESS order — every successful
/// get AND put (including updating an existing key) moves that entry to the end
/// (most-recently-used-last). Investigation result: NO guest subclass overrides
/// removeEldestEntry anywhere in the APK (the 3-arg constructor is called by
/// androidx.collection.LruCache, which manages eviction manually via trimToSize)
/// — so the removeEldestEntry eviction callback is NOT built (case: plain
/// access-order semantics, no LRU-eviction machinery).
/// </summary>
internal sealed class LinkedHashMapPeer
{
    private static readonly object NullKey = new();
    private readonly Dictionary<object, object?> _entries = new();
    internal List<object> Order { get; } = new();
    internal bool AccessOrder { get; set; }

    internal int Count => _entries.Count;

    internal object? Get(object? key)
    {
        object normalized = key ?? NullKey;
        if (!_entries.TryGetValue(normalized, out var value)) return null;
        if (AccessOrder) MoveToEnd(normalized);
        return value;
    }

    internal object? Put(object? key, object? value)
    {
        object normalized = key ?? NullKey;
        bool existed = _entries.TryGetValue(normalized, out var previous);
        _entries[normalized] = value;
        if (!existed) Order.Add(normalized);
        else if (AccessOrder) MoveToEnd(normalized);
        return existed ? previous : null;
    }

    internal bool Remove(object? key)
    {
        object normalized = key ?? NullKey;
        if (!_entries.Remove(normalized)) return false;
        Order.Remove(normalized);
        return true;
    }

    internal object? RemoveValue(object? key)
    {
        object normalized = key ?? NullKey;
        if (!_entries.TryGetValue(normalized, out var removed)) return null;
        _entries.Remove(normalized);
        Order.Remove(normalized);
        return removed;
    }

    internal bool ContainsKey(object? key) => _entries.ContainsKey(key ?? NullKey);

    internal void Clear()
    {
        _entries.Clear();
        Order.Clear();
    }

    /// <summary>Ordered enumeration (insertion or access order per AccessOrder),
    /// un-sentinelizing null keys — views read THIS, never an unordered scan.</summary>
    internal IEnumerable<KeyValuePair<object?, object?>> Entries() =>
        Order.Select(key => new KeyValuePair<object?, object?>(key == NullKey ? null : key, _entries[key]));

    private void MoveToEnd(object key)
    {
        Order.Remove(key);
        Order.Add(key);
    }
}

/// <summary>
/// Storage for a guest java.util.concurrent.ConcurrentHashMap. Real
/// ConcurrentHashMap's internal lock-striping/CAS machinery has NO observable
/// behavioral difference from a plain unsynchronized dictionary under this
/// runtime's GIL (only one thread executes guest bytecode at a time) — same
/// reasoning as AtomicReference/Collections.synchronizedMap/kotlin.Lazy.
/// Unlike HashMap, real ConcurrentHashMap does NOT permit null keys OR values
/// (NPE — verified against OpenJDK source); the bindings reject nulls before
/// touching this store, so no null-key sentinel is needed.
/// </summary>
internal sealed class ConcurrentHashMapPeer
{
    internal Dictionary<object, object?> Entries { get; } = new();
    internal int Count => Entries.Count;
    internal void Clear() => Entries.Clear();
}

/// <summary>
/// In-memory store for a guest android.content.SharedPreferences file. Real
/// Android persists each named file to disk; this runtime has no Android
/// app-data directory, so the store is in-memory only, per session — values do
/// NOT survive process restart (documented limitation, same honest tone as
/// WeakHashMap's "no guest GC model" note). The per-name singleton facade lives
/// in AndroidFrameworkState.SharedPreferenceObjects; this peer holds the actual
/// values behind it. No real synchronization: the GIL serializes all guest
/// bytecode (same reasoning as AtomicReference).
/// </summary>
internal sealed class SharedPreferencesPeer
{
    internal Dictionary<string, object> Values { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Pending-write state for a guest SharedPreferences.Editor (one per edit()
/// call — real Android creates a new EditorImpl per edit()). Mirrors the real
/// EditorImpl: put* accumulate in Modified, remove() stores a removal sentinel,
/// clear() sets a flag; apply()/commit() fold the pending writes into the owner
/// peer's Values. Under this runtime's GIL a synchronous in-memory fold is
/// equivalent to apply()'s async-conceptually disk write (no disk exists) and
/// commit() always returns true (an in-memory write cannot fail) — same
/// reasoning as AtomicReference/Collections.synchronizedMap/kotlin.Lazy.
/// </summary>
internal sealed class SharedPreferencesEditorPeer
{
    internal static readonly object RemoveMarker = new();
    internal required SharedPreferencesPeer Owner { get; init; }
    internal Dictionary<string, object> Modified { get; } = new(StringComparer.Ordinal);
    internal bool Clear { get; set; }
}

/// <summary>
/// Completion/state for a guest java.util.concurrent.Future. State transitions:
/// 0 pending, 1 running, 2 done, 3 cancelled. Completion is a ManualResetEvent so
/// Future.get() genuinely blocks (releasing the GIL) like Thread.join does.
/// </summary>
internal sealed class FuturePeer
{
    internal DexObject? Runnable { get; set; }
    internal DexObject? Callable { get; set; }
    internal ManualResetEventSlim Completion { get; } = new(false);
    private int _state;
    internal int State { get => Volatile.Read(ref _state); set => Volatile.Write(ref _state, value); }
    internal object? Result { get; set; }
    internal Thread? RunningClrThread { get; set; }
    internal Exception? TerminalException { get; set; }
    internal bool IsDone => State is 2 or 3;
    internal bool IsCancelled => State == 3;
    /// <summary>Atomic state transition (used by cancel: pending/running -> cancelled).</summary>
    internal bool TryTransition(int from, int to) => Interlocked.CompareExchange(ref _state, to, from) == from;
}

/// <summary>
/// Real thread pool behind java.util.concurrent.ExecutorService: N real background
/// worker threads pulling FuturePeer tasks off a shared BlockingCollection and
/// executing guest Runnable/Callable bodies under the session GIL. Workers release
/// the GIL while waiting for work (same discipline as Thread.join). Fixed pools
/// keep a constant worker count; cached pools grow up to a bounded max and idle
/// workers exit after a keepalive timeout (never truly unbounded — fail-closed).
/// </summary>
internal sealed class ExecutorServicePeer
{
    internal const int CachedPoolMaxWorkers = 32;
    internal const int CachedPoolKeepaliveMs = 60_000;

    internal required int MaxWorkers { get; init; }
    internal DexObject? ThreadFactory { get; init; }
    internal required int IdleKeepaliveMs { get; init; }
    internal BlockingCollection<FuturePeer> Tasks { get; } = new();
    internal object WorkerGate { get; } = new();
    /// <summary>Worker threads currently alive (spawned, not yet exited).</summary>
    internal int ActiveWorkers;
    /// <summary>Futures whose guest body is currently executing (for shutdownNow interrupts).</summary>
    internal HashSet<FuturePeer> Running { get; } = new();
    internal ManualResetEventSlim Terminated { get; } = new(false);
    internal Exception? TerminalException { get; set; }
    private int _shutdown;
    internal bool IsShutdown => Volatile.Read(ref _shutdown) != 0;
    internal void RequestShutdown() => Volatile.Write(ref _shutdown, 1);
    internal void DisposeWorkers()
    {
        RequestShutdown();
        try { Tasks.CompleteAdding(); } catch (InvalidOperationException) { }
    }
}

/// <summary>
/// A guest android.os.Looper. The MAIN looper dispatches onto the hosted execution
/// lane's existing queue when one exists (the lane IS already a message loop — no
/// second pump); without a lane it owns a private AndroidMessageQueue drained by a
/// background pump thread. A BACKGROUND looper (Looper.prepare()+loop()) owns a
/// private queue drained by the calling thread itself via RunPump.
/// </summary>
internal sealed class LooperPeer
{
    internal required bool IsMain { get; init; }
    internal AndroidMessageQueue? Queue { get; init; }
    internal Thread? PumpThread { get; set; }
    /// <summary>The guest Thread object bound to this Looper (main guest thread for
    /// the main Looper; the calling thread's guest Thread for prepare()).</summary>
    internal DexObject? ThreadObject { get; set; }
    private int _quitRequested;
    internal bool QuitRequested => Volatile.Read(ref _quitRequested) != 0;
    internal Exception? TerminalException { get; set; }
    internal void Quit() => Volatile.Write(ref _quitRequested, 1);
}

/// <summary>
/// A guest android.os.Handler: binds to a LooperPeer and tracks pending guest
/// Runnables so post/removeCallbacks/hasCallbacks honor real semantics. A wrapper
/// Action checks the pending set under a lock when it runs: if removeCallbacks
/// removed the runnable first, the wrapper skips execution. Delayed posts use a
/// cancellable Task.Delay timer.
/// </summary>
internal sealed class HandlerPeer
{
    internal required LooperPeer Looper { get; init; }
    /// <summary>The guest Looper object this handler bound to (for
    /// Handler.getLooper()); null when the handler was created on a looper-less
    /// path (not possible — constructors always bind one).</summary>
    internal DexObject? LooperObject { get; set; }
    internal object Gate { get; } = new();
    internal HashSet<DexObject> Pending { get; } = new(ReferenceEqualityComparer.Instance);
    internal Dictionary<DexObject, CancellationTokenSource> Timers { get; } = new(ReferenceEqualityComparer.Instance);
    internal Exception? LastException { get; set; }
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
        ["Landroid/widget/FrameLayout;"] = "Landroid/view/ViewGroup;",
        ["Landroidx/appcompat/widget/ContentFrameLayout;"] = "Landroid/widget/FrameLayout;",
        ["Landroid/view/LayoutInflater;"] = "Ljava/lang/Object;",
        ["Ljava/util/Random;"] = "Ljava/lang/Object;",
        ["Ljava/util/concurrent/ThreadLocalRandom;"] = "Ljava/util/Random;",
        ["Landroid/widget/LinearLayout;"] = "Landroid/view/ViewGroup;",
        ["Landroid/widget/TextView;"] = "Landroid/view/View;",
        ["Landroid/widget/Button;"] = "Landroid/widget/TextView;",
        ["Landroid/widget/ImageView;"] = "Landroid/view/View;",
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
        ["Ljava/lang/StringIndexOutOfBoundsException;"] = "Ljava/lang/IndexOutOfBoundsException;",
        ["Ljava/lang/NumberFormatException;"] = "Ljava/lang/IllegalArgumentException;",
        ["Ljava/lang/UnsupportedOperationException;"] = "Ljava/lang/RuntimeException;",
        ["Ljava/lang/NegativeArraySizeException;"] = "Ljava/lang/RuntimeException;",
        ["Ljava/util/NoSuchElementException;"] = "Ljava/lang/RuntimeException;",
        ["Ljava/lang/InterruptedException;"] = "Ljava/lang/Exception;",
        ["Ljava/lang/ClassNotFoundException;"] = "Ljava/lang/Exception;",
        ["Ljava/util/concurrent/RejectedExecutionException;"] = "Ljava/lang/RuntimeException;",
        ["Ljava/util/concurrent/CancellationException;"] = "Ljava/lang/IllegalStateException;",
        ["Ljava/util/concurrent/TimeoutException;"] = "Ljava/lang/Exception;",
        ["Ljava/util/concurrent/ExecutionException;"] = "Ljava/lang/Exception;"
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
        ["Ljava/util/LinkedHashMap;"] = ["Ljava/util/Map;"],
        ["Ljava/util/concurrent/ConcurrentHashMap;"] = ["Ljava/util/Map;"],
        ["Ljava/util/LinkedHashSet;"] = ["Ljava/util/Set;"],
        ["Ljava/util/HashSet;"] = ["Ljava/util/Set;"],
        // Real Activity implements Window.Callback (the window callback default).
        ["Landroid/app/Activity;"] = ["Landroid/view/Window$Callback;"],
        ["Ljava/util/Set;"] = ["Ljava/util/Collection;"],
        ["Ljava/util/List;"] = ["Ljava/util/Collection;"],
        ["Ljava/util/Collection;"] = ["Ljava/lang/Iterable;"],
        ["Ljava/util/concurrent/ThreadPoolExecutor;"] = ["Ljava/util/concurrent/ExecutorService;"],
        ["Ljava/util/concurrent/ExecutorService;"] = ["Ljava/util/concurrent/Executor;"],
        ["Ljava/util/concurrent/FutureTask;"] = ["Ljava/util/concurrent/Future;", "Ljava/lang/Runnable;"],
        // android.window back-navigation interfaces: guest anonymous classes
        // declare these framework interfaces; the interface-extends-interface
        // edges let check-cast/instance-of prove assignability through them.
        ["Landroid/window/OnBackAnimationCallback;"] = ["Landroid/window/OnBackInvokedCallback;"]
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
