using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for java.util.Map views (entrySet/keySet/values) + Map.Entry: snapshot
/// iteration for both HashMap and WeakHashMap, snapshot semantics (mutating the
/// map after getting the view does not affect it), Map.Entry getKey/getValue, and
/// the probe-confirmed Map commons (getOrDefault/remove(key,value)/containsValue/
/// isEmpty/clear/putAll/equals/hashCode).
/// </summary>
public sealed class MapViewTests
{
    private const string HashMap = "Ljava/util/HashMap;";
    private const string WeakHashMap = "Ljava/util/WeakHashMap;";
    private const string MapClass = "Ljava/util/Map;";
    private const string EntryClass = "Ljava/util/Map$Entry;";
    private const string SetClass = "Ljava/util/Set;";
    private const string IteratorClass = "Ljava/util/Iterator;";

    [Theory]
    [InlineData(HashMap)]
    [InlineData(WeakHashMap)]
    public void Entry_set_iteration_yields_all_pairs_for_both_map_types(string mapType)
    {
        var (state, registry, _) = Session();
        var map = NewMap(registry, state, mapType);
        Invoke(registry, state, mapType, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "a", state.BoxedObject("Ljava/lang/Integer;", 1));
        Invoke(registry, state, mapType, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "b", state.BoxedObject("Ljava/lang/Integer;", 2));
        if (mapType == HashMap)
            Invoke(registry, state, mapType, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, null!, state.BoxedObject("Ljava/lang/Integer;", 3));

        var set = (DexObject)Invoke(registry, state, mapType, "entrySet", "()Ljava/util/Set;", AndroidInvokeKind.Virtual, map);
        int expected = mapType == HashMap ? 3 : 2;
        Assert.Equal(expected, Invoke(registry, state, SetClass, "size", "()I", AndroidInvokeKind.Virtual, set));
        var entries = Iterate(registry, state, set);
        Assert.Equal(expected, entries.Count);
        var byKey = entries.Where(e => e.Key is not null).ToDictionary(e => e.Key!, e => e.Value);
        Assert.Equal(state.BoxedObject("Ljava/lang/Integer;", 1), byKey["a"]);
        Assert.Equal(state.BoxedObject("Ljava/lang/Integer;", 2), byKey["b"]);
        if (mapType == HashMap)
            Assert.Equal(state.BoxedObject("Ljava/lang/Integer;", 3), entries.Single(e => e.Key is null).Value);
    }

    [Fact]
    public void Entry_set_is_a_snapshot_not_a_live_view()
    {
        var (state, registry, _) = Session();
        var map = NewMap(registry, state, HashMap);
        Invoke(registry, state, HashMap, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "a", state.BoxedObject("Ljava/lang/Integer;", 1));
        var set = (DexObject)Invoke(registry, state, HashMap, "entrySet", "()Ljava/util/Set;", AndroidInvokeKind.Virtual, map);
        // Mutate AFTER the view: the already-obtained view does not see the change.
        Invoke(registry, state, HashMap, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "b", state.BoxedObject("Ljava/lang/Integer;", 2));
        Assert.Equal(1, Invoke(registry, state, SetClass, "size", "()I", AndroidInvokeKind.Virtual, set));
        var entries = Iterate(registry, state, set);
        Assert.Single(entries);
    }

    [Theory]
    [InlineData(HashMap)]
    [InlineData(WeakHashMap)]
    public void Key_set_and_values_are_snapshots(string mapType)
    {
        var (state, registry, _) = Session();
        var map = NewMap(registry, state, mapType);
        Invoke(registry, state, mapType, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "a", state.BoxedObject("Ljava/lang/Integer;", 10));
        Invoke(registry, state, mapType, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "b", state.BoxedObject("Ljava/lang/Integer;", 20));

        var keys = (DexObject)Invoke(registry, state, mapType, "keySet", "()Ljava/util/Set;", AndroidInvokeKind.Virtual, map);
        Assert.Equal(2, Invoke(registry, state, SetClass, "size", "()I", AndroidInvokeKind.Virtual, keys));
        var keyList = IterateRaw(registry, state, keys);
        Assert.Contains("a", keyList);
        Assert.Contains("b", keyList);

        var values = (DexObject)Invoke(registry, state, mapType, "values", "()Ljava/util/Collection;", AndroidInvokeKind.Virtual, map);
        Assert.Equal(2, Invoke(registry, state, "Ljava/util/Collection;", "size", "()I", AndroidInvokeKind.Virtual, values));
        Assert.Equal(1, Invoke(registry, state, SetClass, "contains", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, keys, "a"));
    }

    [Fact]
    public void Map_entry_get_key_and_get_value_read_the_snapshot()
    {
        var (state, registry, _) = Session();
        var map = NewMap(registry, state, HashMap);
        Invoke(registry, state, HashMap, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "k", "v");
        var set = (DexObject)Invoke(registry, state, HashMap, "entrySet", "()Ljava/util/Set;", AndroidInvokeKind.Virtual, map);
        var entry = Iterate(registry, state, set).Single().EntryObject;
        Assert.Equal("k", Invoke(registry, state, EntryClass, "getKey", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, entry));
        Assert.Equal("v", Invoke(registry, state, EntryClass, "getValue", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, entry));
    }

    [Fact]
    public void Map_commons_follow_real_java()
    {
        var (state, registry, _) = Session();
        var map = NewMap(registry, state, HashMap);
        var one = state.BoxedObject("Ljava/lang/Integer;", 1);
        var two = state.BoxedObject("Ljava/lang/Integer;", 2);
        var missingDefault = state.BoxedObject("Ljava/lang/Integer;", 99);
        Assert.Equal(1, Invoke(registry, state, MapClass, "isEmpty", "()Z", AndroidInvokeKind.Virtual, map));
        Invoke(registry, state, MapClass, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "a", one);
        Assert.Equal(0, Invoke(registry, state, MapClass, "isEmpty", "()Z", AndroidInvokeKind.Virtual, map));
        Assert.Same(missingDefault, Invoke(registry, state, MapClass, "getOrDefault", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "missing", missingDefault));
        Assert.Same(one, Invoke(registry, state, MapClass, "getOrDefault", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "a", missingDefault));
        Assert.Equal(1, Invoke(registry, state, MapClass, "containsValue", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, map, one));
        Assert.Equal(0, Invoke(registry, state, MapClass, "containsValue", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, map, two));
        // remove(key, value) only removes when the value matches.
        Assert.Equal(0, Invoke(registry, state, MapClass, "remove", "(Ljava/lang/Object;Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, map, "a", two));
        Assert.Equal(1, Invoke(registry, state, MapClass, "remove", "(Ljava/lang/Object;Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, map, "a", one));
        Assert.Equal(1, Invoke(registry, state, MapClass, "isEmpty", "()Z", AndroidInvokeKind.Virtual, map));

        // putAll copies pairs; equals/hashCode compare by value.
        var source = NewMap(registry, state, HashMap);
        Invoke(registry, state, HashMap, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, source, "x", state.BoxedObject("Ljava/lang/Integer;", 5));
        var target = NewMap(registry, state, HashMap);
        Invoke(registry, state, HashMap, "putAll", "(Ljava/util/Map;)V", AndroidInvokeKind.Virtual, target, source);
        Assert.Equal(1, Invoke(registry, state, HashMap, "equals", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, target, source));
        Assert.Equal(Invoke(registry, state, HashMap, "hashCode", "()I", AndroidInvokeKind.Virtual, source), Invoke(registry, state, HashMap, "hashCode", "()I", AndroidInvokeKind.Virtual, target));
        Invoke(registry, state, HashMap, "clear", "()V", AndroidInvokeKind.Virtual, target);
        Assert.Equal(1, Invoke(registry, state, HashMap, "isEmpty", "()Z", AndroidInvokeKind.Virtual, target));
    }

    private static DexObject NewMap(AndroidApiRegistry registry, AndroidFrameworkState state, string type)
    {
        var map = new DexObject(type);
        Invoke(registry, state, type, "<init>", "()V", AndroidInvokeKind.Direct, map);
        return map;
    }

    private static List<object?> IterateRaw(AndroidApiRegistry registry, AndroidFrameworkState state, DexObject view)
    {
        var iterator = (DexObject)Invoke(registry, state, "Ljava/util/Collection;", "iterator", "()Ljava/util/Iterator;", AndroidInvokeKind.Virtual, view);
        var results = new List<object?>();
        while ((int)Invoke(registry, state, IteratorClass, "hasNext", "()Z", AndroidInvokeKind.Virtual, iterator) == 1)
            results.Add(Invoke(registry, state, IteratorClass, "next", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, iterator));
        return results;
    }

    private static List<(object? Key, object? Value, DexObject EntryObject)> Iterate(AndroidApiRegistry registry, AndroidFrameworkState state, DexObject view)
    {
        var iterator = (DexObject)Invoke(registry, state, "Ljava/util/Collection;", "iterator", "()Ljava/util/Iterator;", AndroidInvokeKind.Virtual, view);
        var results = new List<(object?, object?, DexObject)>();
        while ((int)Invoke(registry, state, IteratorClass, "hasNext", "()Z", AndroidInvokeKind.Virtual, iterator) == 1)
        {
            var entry = (DexObject)Invoke(registry, state, IteratorClass, "next", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, iterator);
            var key = Invoke(registry, state, EntryClass, "getKey", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, entry);
            var value = Invoke(registry, state, EntryClass, "getValue", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, entry);
            results.Add((key, value, entry));
        }
        return results;
    }

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
