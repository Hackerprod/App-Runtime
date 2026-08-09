using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for java.util.LinkedHashMap: insertion-order mode (default, accessOrder=false
/// — get/put on an existing key does NOT reorder), access-order mode (the 3-arg
/// constructor's accessOrder=true — every successful get AND put moves the entry
/// to the end), and entrySet iteration following the map's ACTUAL order.
/// </summary>
public sealed class LinkedHashMapTests
{
    private const string LinkedHashMap = "Ljava/util/LinkedHashMap;";
    private const string EntryClass = "Ljava/util/Map$Entry;";
    private const string SetClass = "Ljava/util/Set;";
    private const string IteratorClass = "Ljava/util/Iterator;";

    [Fact]
    public void Insertion_order_mode_does_not_reorder_on_get_or_put()
    {
        var (state, registry, _) = Session();
        var map = NewMap(registry, state, accessOrder: 0);
        Put(registry, state, map, "a");
        Put(registry, state, map, "b");
        Put(registry, state, map, "c");

        Assert.Equal(new[] { "a", "b", "c" }, Keys(registry, state, map));
        // get and put on an EXISTING key must not reorder (insertion order).
        Invoke(registry, state, LinkedHashMap, "get", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "a");
        Put(registry, state, map, "b");
        Assert.Equal(new[] { "a", "b", "c" }, Keys(registry, state, map));
    }

    [Fact]
    public void Access_order_mode_moves_entries_to_the_end_on_get_and_put()
    {
        var (state, registry, _) = Session();
        var map = NewMap(registry, state, accessOrder: 1);
        Put(registry, state, map, "a");
        Put(registry, state, map, "b");
        Put(registry, state, map, "c");

        // get on the OLDEST moves it to the end.
        Invoke(registry, state, LinkedHashMap, "get", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "a");
        Assert.Equal(new[] { "b", "c", "a" }, Keys(registry, state, map));
        // put updating an EXISTING key also moves it to the end.
        Put(registry, state, map, "b");
        Assert.Equal(new[] { "c", "a", "b" }, Keys(registry, state, map));
    }

    [Fact]
    public void Entry_set_iterates_in_the_maps_actual_order()
    {
        var (state, registry, _) = Session();
        var map = NewMap(registry, state, accessOrder: 1);
        Put(registry, state, map, "a");
        Put(registry, state, map, "b");
        Put(registry, state, map, "c");
        Invoke(registry, state, LinkedHashMap, "get", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "a");

        var set = (DexObject)Invoke(registry, state, LinkedHashMap, "entrySet", "()Ljava/util/Set;", AndroidInvokeKind.Virtual, map);
        var entries = Iterate(registry, state, set);
        Assert.Equal(new[] { "b", "c", "a" }, entries.Select(e => (string)e.Key!).ToArray());
    }

    [Fact]
    public void Remove_and_map_constructor_preserve_order()
    {
        var (state, registry, _) = Session();
        var map = NewMap(registry, state, accessOrder: 0);
        Put(registry, state, map, "a");
        Put(registry, state, map, "b");
        Put(registry, state, map, "c");
        Invoke(registry, state, LinkedHashMap, "remove", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "b");
        Assert.Equal(new[] { "a", "c" }, Keys(registry, state, map));

        var copy = new DexObject(LinkedHashMap);
        Invoke(registry, state, LinkedHashMap, "<init>", "(Ljava/util/Map;)V", AndroidInvokeKind.Direct, copy, map);
        Assert.Equal(new[] { "a", "c" }, Keys(registry, state, copy));
    }

    private static void Put(AndroidApiRegistry registry, AndroidFrameworkState state, DexObject map, string key) => Invoke(registry, state, LinkedHashMap, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, key, key + "-v");

    private static object[] Keys(AndroidApiRegistry registry, AndroidFrameworkState state, DexObject map)
    {
        var keys = (DexObject)Invoke(registry, state, LinkedHashMap, "keySet", "()Ljava/util/Set;", AndroidInvokeKind.Virtual, map);
        var iterator = (DexObject)Invoke(registry, state, SetClass, "iterator", "()Ljava/util/Iterator;", AndroidInvokeKind.Virtual, keys);
        var results = new List<object>();
        while ((int)Invoke(registry, state, IteratorClass, "hasNext", "()Z", AndroidInvokeKind.Virtual, iterator) == 1)
            results.Add(Invoke(registry, state, IteratorClass, "next", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, iterator));
        return results.ToArray();
    }

    private static List<(object? Key, object? Value)> Iterate(AndroidApiRegistry registry, AndroidFrameworkState state, DexObject set)
    {
        var iterator = (DexObject)Invoke(registry, state, SetClass, "iterator", "()Ljava/util/Iterator;", AndroidInvokeKind.Virtual, set);
        var results = new List<(object?, object?)>();
        while ((int)Invoke(registry, state, IteratorClass, "hasNext", "()Z", AndroidInvokeKind.Virtual, iterator) == 1)
        {
            var entry = (DexObject)Invoke(registry, state, IteratorClass, "next", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, iterator);
            results.Add((Invoke(registry, state, EntryClass, "getKey", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, entry), Invoke(registry, state, EntryClass, "getValue", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, entry)));
        }
        return results;
    }

    private static DexObject NewMap(AndroidApiRegistry registry, AndroidFrameworkState state, int accessOrder)
    {
        var map = new DexObject(LinkedHashMap);
        Invoke(registry, state, LinkedHashMap, "<init>", "(IFZ)V", AndroidInvokeKind.Direct, map, 0, 0.75, accessOrder);
        return map;
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

    private static readonly (AndroidFrameworkState State, AndroidApiRegistry Registry, DexInterpreter Interpreter) SessionCache = Session();

    private static object Invoke(AndroidApiRegistry registry, AndroidFrameworkState state, string owner, string name, string descriptor, AndroidInvokeKind kind, params object[] args)
    {
        var api = new AndroidApiMethodId(owner, name, descriptor);
        var context = new AndroidApiSessionContext(state.SessionId, state.PackageName, state.ActivityDescriptor, default, () => true);
        if (state.Interpreter is not null) context.IsTypeAssignable = state.Interpreter.IsGuestTypeAssignable;
        return registry.Invoke(context, new AndroidApiCallSite("Ltest;->run()V", 0, api, api, kind), args);
    }

    private sealed class QuietLogSink : IAndroidLogSink { public int Info(AndroidLogEntry entry) => 1; }
}
