#nullable enable
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for java.util.concurrent.ConcurrentHashMap (own file — the
/// java.util.concurrent package itself, distinct from the .atomic and
/// executor-family files). Real contract VERIFIED against the OpenJDK source
/// (src/java.base/.../java/util/concurrent/ConcurrentHashMap.java, which
/// Android's libcore imports near-verbatim — NOT a reimplementation): the
/// critical difference from HashMap is that ConcurrentHashMap does NOT permit
/// null keys OR null values — put/putIfAbsent throw NullPointerException on
/// either (verified: putVal's `if (key == null || value == null) throw new
/// NullPointerException();`), and containsValue(null)/get(null) also throw
/// (get NPEs via key.hashCode()). putIfAbsent returns the EXISTING value
/// without overwriting when the key is present. Constructors: ()/(int)/
/// (int,float)/(int,float,int)/(Map), with IllegalArgumentException for
/// negative capacity, nonpositive loadFactor, or nonpositive concurrencyLevel
/// (verified in the 3-arg constructor's guard). No internal lock-striping/CAS
/// machinery is modeled: the GIL serializes all guest bytecode, so a plain
/// unsynchronized dictionary has no observable behavioral difference (same
/// reasoning as AtomicReference/Collections.synchronizedMap/kotlin.Lazy).
/// Probe of MelyNails.apk: <init>()V, get, isEmpty, keySet, put, putIfAbsent,
/// remove(Object) — all built to their complete real contract; the 5
/// constructors are built (the contract), views reuse the shared Map-view
/// machinery (entrySet/keySet/values via JavaUtilMapBindings MapPairs).
/// </summary>
internal static class JavaUtilConcurrentBindings
{
    private const string ConcurrentHashMap = "Ljava/util/concurrent/ConcurrentHashMap;";
    private const string NullPointer = "Ljava/lang/NullPointerException;";

    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        builder.Register(Api(ConcurrentHashMap, "<init>", "()V"), (_, args) => { state.ConcurrentHashMaps.Add(Receiver(args), new ConcurrentHashMapPeer()); return null!; });
        builder.Register(Api(ConcurrentHashMap, "<init>", "(I)V"), (_, args) =>
        {
            if (RequireInt(args[1]) < 0) throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/IllegalArgumentException;", "Initial capacity cannot be negative"));
            state.ConcurrentHashMaps.Add(Receiver(args), new ConcurrentHashMapPeer());
            return null!;
        });
        builder.Register(Api(ConcurrentHashMap, "<init>", "(IF)V"), (_, args) =>
        {
            if (RequireInt(args[1]) < 0 || RequireFloat(args[2]) <= 0.0) throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/IllegalArgumentException;"));
            state.ConcurrentHashMaps.Add(Receiver(args), new ConcurrentHashMapPeer());
            return null!;
        });
        builder.Register(Api(ConcurrentHashMap, "<init>", "(IFI)V"), (_, args) =>
        {
            if (RequireInt(args[1]) < 0 || RequireFloat(args[2]) <= 0.0 || RequireInt(args[3]) <= 0) throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/IllegalArgumentException;"));
            state.ConcurrentHashMaps.Add(Receiver(args), new ConcurrentHashMapPeer());
            return null!;
        });
        builder.Register(Api(ConcurrentHashMap, "<init>", "(Ljava/util/Map;)V"), (_, args) =>
        {
            var peer = new ConcurrentHashMapPeer();
            foreach (var pair in JavaUtilMapBindings.MapPairsFor(state, RequireDex(args[1])))
                peer.Entries[RequireNonNullKey(pair.Key)] = RequireNonNullValue(pair.Value);
            state.ConcurrentHashMaps.Add(Receiver(args), peer);
            return null!;
        });

        // Real contract (OpenJDK): null key OR null value -> NPE.
        builder.Register(Api(ConcurrentHashMap, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;"), (_, args) =>
        {
            var peer = state.ConcurrentHashMaps.Get(Receiver(args));
            object key = RequireNonNullKey(args[1]);
            object value = RequireNonNullValue(args[2]);
            bool existed = peer.Entries.TryGetValue(key, out var previous);
            peer.Entries[key] = value;
            return existed ? previous ?? null! : null!;
        });
        builder.Register(Api(ConcurrentHashMap, "putIfAbsent", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;"), (_, args) =>
        {
            // Real contract: if the key is present, return its EXISTING value
            // WITHOUT overwriting (onlyIfAbsent); otherwise insert and return null.
            var peer = state.ConcurrentHashMaps.Get(Receiver(args));
            object key = RequireNonNullKey(args[1]);
            object value = RequireNonNullValue(args[2]);
            if (peer.Entries.TryGetValue(key, out var existing)) return existing ?? null!;
            peer.Entries[key] = value;
            return null!;
        });
        builder.Register(Api(ConcurrentHashMap, "get", "(Ljava/lang/Object;)Ljava/lang/Object;"), (_, args) =>
        {
            var peer = state.ConcurrentHashMaps.Get(Receiver(args));
            return peer.Entries.TryGetValue(RequireNonNullKey(args[1]), out var value) ? value ?? null! : null!;
        });
        builder.Register(Api(ConcurrentHashMap, "remove", "(Ljava/lang/Object;)Ljava/lang/Object;"), (_, args) =>
        {
            var peer = state.ConcurrentHashMaps.Get(Receiver(args));
            object key = RequireNonNullKey(args[1]);
            if (!peer.Entries.TryGetValue(key, out var removed)) return null!;
            peer.Entries.Remove(key);
            return removed ?? null!;
        });
        builder.Register(Api(ConcurrentHashMap, "isEmpty", "()Z"), (_, args) => state.ConcurrentHashMaps.Get(Receiver(args)).Count == 0 ? 1 : 0);
        builder.Register(Api(ConcurrentHashMap, "size", "()I"), (_, args) => state.ConcurrentHashMaps.Get(Receiver(args)).Count);
        builder.Register(Api(ConcurrentHashMap, "containsKey", "(Ljava/lang/Object;)Z"), (_, args) => state.ConcurrentHashMaps.Get(Receiver(args)).Entries.ContainsKey(RequireNonNullKey(args[1])) ? 1 : 0);
        builder.Register(Api(ConcurrentHashMap, "containsValue", "(Ljava/lang/Object;)Z"), (_, args) =>
        {
            // Real contract: containsValue(null) throws NPE (verified in OpenJDK).
            RequireNonNullValue(args[1]);
            return state.ConcurrentHashMaps.Get(Receiver(args)).Entries.ContainsValue(args[1]) ? 1 : 0;
        });
        builder.Register(Api(ConcurrentHashMap, "clear", "()V"), (_, args) => { state.ConcurrentHashMaps.Get(Receiver(args)).Clear(); return null!; });
        builder.Register(Api(ConcurrentHashMap, "putAll", "(Ljava/util/Map;)V"), (_, args) =>
        {
            var peer = state.ConcurrentHashMaps.Get(Receiver(args));
            foreach (var pair in JavaUtilMapBindings.MapPairsFor(state, RequireDex(args[1])))
                peer.Entries[RequireNonNullKey(pair.Key)] = RequireNonNullValue(pair.Value);
            return null!;
        });
        builder.Register(Api(ConcurrentHashMap, "mappingCount", "()J"), (_, args) => (long)state.ConcurrentHashMaps.Get(Receiver(args)).Count);
    }

    private static object RequireNonNullKey(object? value) => value is null || value is int zero && zero == 0
        ? throw new GuestExceptionCarrier(GuestThrowableMetadata.Create(NullPointer))
        : value;
    private static object RequireNonNullValue(object? value) => value is null || value is int zero && zero == 0
        ? throw new GuestExceptionCarrier(GuestThrowableMetadata.Create(NullPointer))
        : value;

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    private static DexObject Receiver(object[] args) => (DexObject)args[0]!;
    private static DexObject RequireDex(object? value) => value as DexObject ?? throw new ArgumentException("Expected a guest object.");
    private static int RequireInt(object? value) => AndroidApiBindings.RequireInt(value ?? 0);
    private static double RequireFloat(object? value) => value switch
    {
        float f => f,
        double d => d,
        int bits => BitConverter.Int32BitsToSingle(bits),
        _ => throw new ArgumentException("Expected a float.")
    };
}
