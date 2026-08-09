#nullable enable
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for java.util.Map views (entrySet/keySet/values) + Map.Entry,
/// migrated out of the AndroidApiBindings monolith as the Map surface grows
/// (per-package convention). The views are SNAPSHOTS captured at call time (the
/// same convention Iterator established for ArrayList): entrySet/keySet/values
/// each materialize the current pairs/keys/values into a Set/Collection-shaped
/// peer and support iterator()/size()/contains() so the overwhelmingly common
/// real usage (for-each iteration) works correctly. They are deliberately NOT
/// live write-through views — mutating the view does not mutate the backing map
/// (real Java behavior, but a real complexity jump; snapshot-only is the honest
/// bounded scope, confirmed sufficient for the probe's observed usage). Map.Entry
/// is getKey/getValue only — setValue is not referenced by the probe and would
/// need write-through. Iterator.remove (referenced by libs) is also not bound —
/// the snapshot iterator cannot remove from the backing map.
/// Probe-confirmed additional Map methods bound here (all trivial on the
/// existing peers): getOrDefault, remove(Object,Object), containsValue, isEmpty,
/// clear, putAll, HashMap.equals/hashCode (map equality).
/// </summary>
internal static class JavaUtilMapBindings
{
    private const string HashMap = "Ljava/util/HashMap;";
    private const string LinkedHashMap = "Ljava/util/LinkedHashMap;";
    private const string ConcurrentHashMap = "Ljava/util/concurrent/ConcurrentHashMap;";
    private const string WeakHashMap = "Ljava/util/WeakHashMap;";
    private const string MapClass = "Ljava/util/Map;";
    private const string EntryClass = "Ljava/util/Map$Entry;";
    private const string SetClass = "Ljava/util/Set;";
    private const string CollectionClass = "Ljava/util/Collection;";

    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        // ---- Map views (snapshot Set/Collection per backing map type) ----
        RegisterFor(builder, state, HashMap, map: m => m);
        RegisterFor(builder, state, LinkedHashMap, map: m => m);
        RegisterFor(builder, state, ConcurrentHashMap, map: m => m);
        RegisterFor(builder, state, WeakHashMap, map: m => m);
        RegisterFor(builder, state, MapClass, map: m => m);

        // ---- Map.Entry (read-only snapshot entries) ----
        builder.Register(Api(EntryClass, "getKey", "()Ljava/lang/Object;"), (_, args) => state.MapEntries.Get(Receiver(args)).Key ?? null!);
        builder.Register(Api(EntryClass, "getValue", "()Ljava/lang/Object;"), (_, args) => state.MapEntries.Get(Receiver(args)).Value ?? null!);

        // ---- View iteration/query surface (Set + Collection share the shape) ----
        RegisterViewSurface(builder, state, SetClass);
        RegisterViewSurface(builder, state, CollectionClass);

        // ---- Probe-confirmed additional Map methods ----
        // New commons (not in the monolith) for every bound map class.
        RegisterMapCommons(builder, state, HashMap);
        RegisterMapCommons(builder, state, LinkedHashMap);
        RegisterMapCommons(builder, state, WeakHashMap);
        RegisterMapCommons(builder, state, MapClass);
        // The basic accessors exist per-concrete-class in the monolith; mirror the
        // same surface on the Map interface so Map-typed call sites resolve too.
        RegisterMapBasics(builder, state, MapClass);
        builder.Register(Api(HashMap, "equals", "(Ljava/lang/Object;)Z"), (_, args) => MapEquals(state, args[0], args[1]) ? 1 : 0);
        builder.Register(Api(HashMap, "hashCode", "()I"), (_, args) => MapHashCode(state, args[0]));
    }

    private static void RegisterMapBasics(AndroidApiRegistryBuilder builder, AndroidFrameworkState state, string owner)
    {
        builder.Register(Api(owner, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;"), (_, args) => PutValue(state, args[0], args[1], args[2]));
        builder.Register(Api(owner, "get", "(Ljava/lang/Object;)Ljava/lang/Object;"), (_, args) => GetValue(state, args[0], args[1]));
        builder.Register(Api(owner, "containsKey", "(Ljava/lang/Object;)Z"), (_, args) => ContainsKey(state, args[0], args[1]) ? 1 : 0);
        builder.Register(Api(owner, "remove", "(Ljava/lang/Object;)Ljava/lang/Object;"), (_, args) => RemoveValue(state, args[0], args[1]));
        builder.Register(Api(owner, "size", "()I"), (_, args) => MapCount(state, args[0]));
    }

    private static void RegisterMapCommons(AndroidApiRegistryBuilder builder, AndroidFrameworkState state, string owner)
    {
        builder.Register(Api(owner, "isEmpty", "()Z"), (_, args) => MapCount(state, args[0]) == 0 ? 1 : 0);
        builder.Register(Api(owner, "clear", "()V"), (_, args) => { ClearMap(state, args[0]); return null!; });
        builder.Register(Api(owner, "putAll", "(Ljava/util/Map;)V"), (_, args) => { PutAll(state, args[0], args[1]); return null!; });
        builder.Register(Api(owner, "containsValue", "(Ljava/lang/Object;)Z"), (_, args) => ContainsValue(state, args[0], args[1]) ? 1 : 0);
        builder.Register(Api(owner, "getOrDefault", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;"), (_, args) => GetOrDefault(state, args[0], args[1], args[2]));
        builder.Register(Api(owner, "remove", "(Ljava/lang/Object;Ljava/lang/Object;)Z"), (_, args) => RemoveIfValue(state, args[0], args[1], args[2]) ? 1 : 0);
    }

    private static void RegisterFor(AndroidApiRegistryBuilder builder, AndroidFrameworkState state, string owner, Func<object, object> map)
    {
        builder.Register(Api(owner, "entrySet", "()Ljava/util/Set;"), (_, args) =>
        {
            var peer = MapPairs(state, args[0]);
            var view = new HashSet<object?>();
            foreach (var pair in peer)
            {
                var entry = new DexObject(EntryClass);
                state.MapEntries.Add(entry, new MapEntryPeer(pair.Key, pair.Value));
                view.Add(entry);
            }
            var set = new DexObject(SetClass);
            state.MapViews.Add(set, view);
            return set;
        });
        builder.Register(Api(owner, "keySet", "()Ljava/util/Set;"), (_, args) =>
        {
            var view = new HashSet<object?>();
            foreach (var pair in MapPairs(state, args[0])) view.Add(pair.Key);
            var set = new DexObject(SetClass);
            state.MapViews.Add(set, view);
            return set;
        });
        builder.Register(Api(owner, "values", "()Ljava/util/Collection;"), (_, args) =>
        {
            var view = new HashSet<object?>();
            foreach (var pair in MapPairs(state, args[0])) view.Add(pair.Value);
            var collection = new DexObject(CollectionClass);
            state.MapViews.Add(collection, view);
            return collection;
        });
    }



    internal static IEnumerable<KeyValuePair<object?, object?>> MapPairsFor(AndroidFrameworkState state, DexObject receiver) => MapPairs(state, receiver);

    private static IEnumerable<KeyValuePair<object?, object?>> MapPairs(AndroidFrameworkState state, object receiver)
    {
        var dex = (DexObject)receiver;
        if (dex.TypeDescriptor == WeakHashMap)
            return state.WeakHashMaps.Get(dex).Entries.Select(pair => new KeyValuePair<object?, object?>(pair.Key, pair.Value));
        if (dex.TypeDescriptor == LinkedHashMap)
            return state.LinkedHashMaps.Get(dex).Entries();
        if (dex.TypeDescriptor == ConcurrentHashMap)
            return state.ConcurrentHashMaps.Get(dex).Entries.Select(pair => new KeyValuePair<object?, object?>(pair.Key, pair.Value));
        return state.HashMaps.Get(dex).Entries();
    }

    private static void RegisterViewSurface(AndroidApiRegistryBuilder builder, AndroidFrameworkState state, string owner)
    {
        builder.Register(Api(owner, "iterator", "()Ljava/util/Iterator;"), (_, args) =>
        {
            var view = RequireSetBacking(state, Receiver(args));
            var iterator = new DexObject("Ljava/util/Iterator;");
            state.Iterators.Add(iterator, new IteratorPeer(view));
            return iterator;
        });
        builder.Register(Api(owner, "size", "()I"), (_, args) => RequireSetBacking(state, Receiver(args)).Count);
        builder.Register(Api(owner, "contains", "(Ljava/lang/Object;)Z"), (_, args) => RequireSetBacking(state, Receiver(args)).Contains(args[1]) ? 1 : 0);
        builder.Register(Api(owner, "isEmpty", "()Z"), (_, args) => RequireSetBacking(state, Receiver(args)).Count == 0 ? 1 : 0);
    }

    /// <summary>Resolves a Set/Collection receiver's backing hash set across BOTH
    /// Set-shaped stores (snapshot MapViews AND CopyOnWriteArraySets — the
    /// interface ids are shared by both, so a Set-typed call site must work for
    /// either).</summary>
    private static HashSet<object?> RequireSetBacking(AndroidFrameworkState state, DexObject receiver)
    {
        if (state.MapViews.TryGet(receiver, out var view)) return view;
        if (state.CopyOnWriteArraySets.TryGet(receiver, out var cow)) return cow;
        throw new InvalidOperationException("Set/Collection peer is not initialized for " + receiver.TypeDescriptor);
    }

    private static object PutValue(AndroidFrameworkState state, object receiver, object? key, object? value)
    {
        var dex = (DexObject)receiver;
        if (dex.TypeDescriptor == LinkedHashMap) return state.LinkedHashMaps.Get(dex).Put(key, value) ?? null!;
        if (dex.TypeDescriptor == WeakHashMap)
        {
            var entries = state.WeakHashMaps.Get(dex).Entries;
            object? previous = entries.TryGetValue(key!, out var existing) ? existing : null;
            entries[key!] = value!;
            return previous ?? null!;
        }
        return state.HashMaps.Get(dex).Put(key, value) ?? null!;
    }

    private static object GetValue(AndroidFrameworkState state, object receiver, object? key)
    {
        var dex = (DexObject)receiver;
        if (dex.TypeDescriptor == LinkedHashMap) return state.LinkedHashMaps.Get(dex).Get(key) ?? null!;
        return dex.TypeDescriptor == WeakHashMap
            ? (state.WeakHashMaps.Get(dex).Entries.TryGetValue(key!, out var value) ? value : null) ?? null!
            : state.HashMaps.Get(dex).Get(key) ?? null!;
    }

    private static bool ContainsKey(AndroidFrameworkState state, object receiver, object? key)
    {
        var dex = (DexObject)receiver;
        if (dex.TypeDescriptor == LinkedHashMap) return state.LinkedHashMaps.Get(dex).ContainsKey(key);
        return dex.TypeDescriptor == WeakHashMap ? state.WeakHashMaps.Get(dex).Entries.ContainsKey(key!) : state.HashMaps.Get(dex).ContainsKey(key);
    }

    private static object RemoveValue(AndroidFrameworkState state, object receiver, object? key)
    {
        var dex = (DexObject)receiver;
        if (dex.TypeDescriptor == LinkedHashMap) return state.LinkedHashMaps.Get(dex).RemoveValue(key) ?? null!;
        if (dex.TypeDescriptor == WeakHashMap)
        {
            var entries = state.WeakHashMaps.Get(dex).Entries;
            object? removed = entries.TryGetValue(key!, out var existing) ? existing : null;
            entries.Remove(key!);
            return removed ?? null!;
        }
        return state.HashMaps.Get(dex).Remove(key) ?? null!;
    }

    // ---------------------------------------------------------------------------
    // Map common helpers (HashMap + WeakHashMap share shapes)
    // ---------------------------------------------------------------------------

    private static int MapCount(AndroidFrameworkState state, object receiver)
    {
        var dex = (DexObject)receiver;
        if (dex.TypeDescriptor == WeakHashMap) return state.WeakHashMaps.Get(dex).Entries.Count;
        if (dex.TypeDescriptor == LinkedHashMap) return state.LinkedHashMaps.Get(dex).Count;
        return state.HashMaps.Get(dex).Count;
    }

    private static void ClearMap(AndroidFrameworkState state, object receiver)
    {
        var dex = (DexObject)receiver;
        if (dex.TypeDescriptor == LinkedHashMap) { state.LinkedHashMaps.Get(dex).Clear(); return; }
        if (dex.TypeDescriptor == WeakHashMap) state.WeakHashMaps.Get(dex).Entries.Clear();
        else state.HashMaps.Get(dex).Clear();
    }

    private static void PutAll(AndroidFrameworkState state, object target, object source)
    {
        var targetDex = (DexObject)target;
        if (targetDex.TypeDescriptor == LinkedHashMap) { foreach (var pair in MapPairs(state, source)) state.LinkedHashMaps.Get(targetDex).Put(pair.Key, pair.Value); return; }
        foreach (var pair in MapPairs(state, source))
        {
            if (targetDex.TypeDescriptor == WeakHashMap) state.WeakHashMaps.Get(targetDex).Entries[pair.Key!] = pair.Value;
            else state.HashMaps.Get(targetDex).Put(pair.Key, pair.Value);
        }
    }

    private static bool ContainsValue(AndroidFrameworkState state, object receiver, object? value)
    {
        foreach (var pair in MapPairs(state, receiver))
        {
            if (Equals(pair.Value, value)) return true;
        }
        return false;
    }

    private static object GetOrDefault(AndroidFrameworkState state, object receiver, object? key, object? defaultValue)
    {
        var dex = (DexObject)receiver;
        object? found = dex.TypeDescriptor == WeakHashMap
            ? (state.WeakHashMaps.Get(dex).Entries.TryGetValue(key!, out var weak) ? weak : null)
            : dex.TypeDescriptor == LinkedHashMap
                ? state.LinkedHashMaps.Get(dex).Get(key)
                : state.HashMaps.Get(dex).Get(key);
        object result = found ?? defaultValue ?? null!;
        return result;
    }

    private static bool RemoveIfValue(AndroidFrameworkState state, object receiver, object? key, object? expected)
    {
        var dex = (DexObject)receiver;
        if (dex.TypeDescriptor == LinkedHashMap)
        {
            var lhm = state.LinkedHashMaps.Get(dex);
            if (!lhm.ContainsKey(key) || !Equals(lhm.Get(key), expected)) return false;
            lhm.Remove(key);
            return true;
        }
        if (dex.TypeDescriptor == WeakHashMap)
        {
            var entries = state.WeakHashMaps.Get(dex).Entries;
            if (!entries.TryGetValue(key!, out var current) || !Equals(current, expected)) return false;
            entries.Remove(key!);
            return true;
        }
        var peer = state.HashMaps.Get(dex);
        object? existing = peer.Get(key);
        if (!Equals(existing, expected)) return false;
        peer.Remove(key);
        return true;
    }

    private static bool MapEquals(AndroidFrameworkState state, object receiver, object? other)
    {
        if (other is not DexObject otherDex || otherDex.TypeDescriptor is not (HashMap or WeakHashMap or LinkedHashMap)) return false;
        var left = MapPairs(state, receiver).ToList();
        var right = MapPairs(state, otherDex).ToList();
        if (left.Count != right.Count) return false;
        foreach (var pair in left)
        {
            bool matched = false;
            foreach (var candidate in right)
            {
                if (Equals(pair.Key, candidate.Key) && Equals(pair.Value, candidate.Value)) { matched = true; break; }
            }
            if (!matched) return false;
        }
        return true;
    }

    private static int MapHashCode(AndroidFrameworkState state, object receiver)
    {
        int hash = 0;
        foreach (var pair in MapPairs(state, receiver))
        {
            int keyHash = pair.Key?.GetHashCode() ?? 0;
            int valueHash = pair.Value?.GetHashCode() ?? 0;
            hash += keyHash ^ valueHash;
        }
        return hash;
    }

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    private static DexObject Receiver(object[] args) => (DexObject)args[0]!;
}
