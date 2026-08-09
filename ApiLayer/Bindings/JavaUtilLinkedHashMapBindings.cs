#nullable enable
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for java.util.LinkedHashMap. INVESTIGATION RESULT (reported): the
/// 3-arg constructor (int,float,boolean) is called by androidx.collection.LruCache's
/// <init> with accessOrder=true, and NO guest subclass of LinkedHashMap exists and
/// NO removeEldestEntry override/reference exists anywhere in the APK (LruCache
/// manages eviction manually via trimToSize, not via the protected hook). Scope
/// built: the plain access-order case — LinkedHashMapPeer tracks insertion order
/// and, when accessOrder=true, reorders on every successful get AND put (including
/// updating an existing key) to most-recently-used-last. NO removeEldestEntry
/// eviction callback machinery (proven unused on this path — same discipline as
/// every reflection-adjacent boundary). Views (entrySet/keySet/values) iterate the
/// map's ACTUAL order via the peer's ordered Entries(); commons (isEmpty/clear/
/// putAll/containsValue/getOrDefault/remove(k,v)/basic accessors) are wired for
/// LinkedHashMap in JavaUtilMapBindings's shared helpers. The float loadFactor
/// and int initialCapacity constructor args are accepted and unused (bounded).
/// </summary>
internal static class JavaUtilLinkedHashMapBindings
{
    private const string LinkedHashMap = "Ljava/util/LinkedHashMap;";

    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        builder.Register(Api(LinkedHashMap, "<init>", "()V"), (_, args) => { state.LinkedHashMaps.Add(Receiver(args), new LinkedHashMapPeer()); return null!; });
        builder.Register(Api(LinkedHashMap, "<init>", "(I)V"), (_, args) => { RequireInt(args[1]); state.LinkedHashMaps.Add(Receiver(args), new LinkedHashMapPeer()); return null!; });
        builder.Register(Api(LinkedHashMap, "<init>", "(IFZ)V"), (_, args) =>
        {
            RequireInt(args[1]);
            RequireFloat(args[2]);
            state.LinkedHashMaps.Add(Receiver(args), new LinkedHashMapPeer { AccessOrder = RequireInt(args[3]) != 0 });
            return null!;
        });
        builder.Register(Api(LinkedHashMap, "<init>", "(Ljava/util/Map;)V"), (_, args) =>
        {
            var peer = new LinkedHashMapPeer();
            foreach (var pair in JavaUtilMapBindings.MapPairsFor(state, RequireDex(args[1])))
                peer.Put(pair.Key, pair.Value);
            state.LinkedHashMaps.Add(Receiver(args), peer);
            return null!;
        });

        // Basic accessors against the ordered peer (Map-interface ids share the
        // helpers via JavaUtilMapBindings; these concrete ids hit the peer directly).
        builder.Register(Api(LinkedHashMap, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;"), (_, args) => state.LinkedHashMaps.Get(Receiver(args)).Put(args[1], args[2]) ?? null!);
        builder.Register(Api(LinkedHashMap, "get", "(Ljava/lang/Object;)Ljava/lang/Object;"), (_, args) => state.LinkedHashMaps.Get(Receiver(args)).Get(args[1]) ?? null!);
        builder.Register(Api(LinkedHashMap, "remove", "(Ljava/lang/Object;)Ljava/lang/Object;"), (_, args) => state.LinkedHashMaps.Get(Receiver(args)).RemoveValue(args[1]) ?? null!);
        builder.Register(Api(LinkedHashMap, "containsKey", "(Ljava/lang/Object;)Z"), (_, args) => state.LinkedHashMaps.Get(Receiver(args)).ContainsKey(args[1]) ? 1 : 0);
        builder.Register(Api(LinkedHashMap, "size", "()I"), (_, args) => state.LinkedHashMaps.Get(Receiver(args)).Count);
        // isEmpty/clear/putAll + the view methods + Map commons are registered via
        // JavaUtilMapBindings's shared RegisterMapCommons/RegisterFor helpers.
    }

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
