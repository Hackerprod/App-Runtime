#nullable enable
using System.Globalization;
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for java.util.Collections (own file — a distinct, substantial static
/// utility class). Two families with genuinely different contracts, deliberately
/// NOT conflated:
/// - synchronized*: under this runtime's GIL all guest bytecode already executes
///   serialized, so synchronizedMap simply returns the SAME underlying map object
///   (no synchronization wrapper needed — real architectural consequence of the
///   GIL, same reasoning as Lazy/monitor-enter). Only synchronizedMap is
///   referenced by the probe.
/// - unmodifiable*/empty*/singleton*: REAL immutability enforcement — a wrapper
///   sharing the backing peer whose writes (put/add/remove/clear/set) throw guest
///   UnsupportedOperationException while reads delegate. List/Map use an
///   Unmodifiable flag on the shared peer; Set writes are bound to always throw
///   (every Set this runtime produces — snapshot views, unmodifiable, singleton,
///   empty — is immutable).
/// Also probe-confirmed: sort(List)/(List,Comparator) (natural ordering for
/// string/boxed elements, Comparator invoked via the guest-functional-interface
/// pattern), reverse, max/min (+Comparator), fill, addAll(Collection,Object[]),
/// newSetFromMap (bounded: independent HashSet — the JSONObject add/contains use
/// case works, no write-through to the backing map; identity semantics not
/// modeled). Deliberately NOT built (referenced but depend on unmodeled
/// machinery): binarySearch/shuffle (Comparable/Random), list(Enumeration)
/// (Enumeration unmodeled).
/// </summary>
internal static class JavaUtilCollectionsBindings
{
    private const string CollectionsClass = "Ljava/util/Collections;";
    private const string ListClass = "Ljava/util/List;";
    private const string SetClass = "Ljava/util/Set;";
    private const string MapClass = "Ljava/util/Map;";
    private const string CollectionClass = "Ljava/util/Collection;";
    private const string ArrayListClass = "Ljava/util/ArrayList;";
    private const string HashMapClass = "Ljava/util/HashMap;";
    private const string UnsupportedOperation = "Ljava/lang/UnsupportedOperationException;";

    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        // ---- synchronized*: GIL already serializes guest execution -> same object ----
        builder.Register(Api(CollectionsClass, "synchronizedMap", "(Ljava/util/Map;)Ljava/util/Map;"), (_, args) => args[0]!);

        // ---- unmodifiable* / empty* / singleton*: REAL immutability enforcement ----
        builder.Register(Api(CollectionsClass, "unmodifiableList", "(Ljava/util/List;)Ljava/util/List;"), (_, args) =>
        {
            var backing = state.ArrayLists.Get(RequireDex(args[0]));
            var wrapper = new DexObject(ArrayListClass);
            // Share the backing list so reads stay live; writes throw.
            state.ArrayLists.Add(wrapper, new ListPeer(backing.Elements) { Unmodifiable = true });
            return wrapper;
        });
        builder.Register(Api(CollectionsClass, "unmodifiableMap", "(Ljava/util/Map;)Ljava/util/Map;"), (_, args) =>
        {
            var backing = state.HashMaps.Get(RequireDex(args[0]));
            var wrapper = new DexObject(HashMapClass);
            // Share the backing dictionary so reads stay live; writes throw.
            state.HashMaps.Add(wrapper, new HashMapPeer(backing.SharedEntries()) { Unmodifiable = true });
            return wrapper;
        });
        builder.Register(Api(CollectionsClass, "unmodifiableSet", "(Ljava/util/Set;)Ljava/util/Set;"), (_, args) =>
        {
            var backing = state.MapViews.Get(RequireDex(args[0]));
            var wrapper = new DexObject(SetClass);
            state.MapViews.Add(wrapper, new HashSet<object?>(backing));
            return wrapper;
        });
        builder.Register(Api(CollectionsClass, "emptyList", "()Ljava/util/List;"), (_, args) => EmptyList(state));
        builder.Register(Api(CollectionsClass, "emptyMap", "()Ljava/util/Map;"), (_, args) => EmptyMap(state));
        builder.Register(Api(CollectionsClass, "emptySet", "()Ljava/util/Set;"), (_, args) => EmptySet(state));
        builder.Register(Api(CollectionsClass, "singletonList", "(Ljava/lang/Object;)Ljava/util/List;"), (_, args) =>
        {
            var wrapper = new DexObject(ArrayListClass);
            var peer = new ListPeer { Unmodifiable = true };
            peer.Elements.Add(args[0]);
            state.ArrayLists.Add(wrapper, peer);
            return wrapper;
        });
        builder.Register(Api(CollectionsClass, "singletonMap", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/util/Map;"), (_, args) =>
        {
            var wrapper = new DexObject(HashMapClass);
            var peer = new HashMapPeer { Unmodifiable = true };
            peer.Put(args[0], args[1]);
            state.HashMaps.Add(wrapper, peer);
            return wrapper;
        });
        builder.Register(Api(CollectionsClass, "singleton", "(Ljava/lang/Object;)Ljava/util/Set;"), (_, args) =>
        {
            var wrapper = new DexObject(SetClass);
            state.MapViews.Add(wrapper, new HashSet<object?> { args[0] });
            return wrapper;
        });

        // ---- Set writes always throw (every Set this runtime produces is immutable) ----
        builder.Register(Api(SetClass, "add", "(Ljava/lang/Object;)Z"), (_, _) => throw Uoe());
        builder.Register(Api(SetClass, "remove", "(Ljava/lang/Object;)Z"), (_, _) => throw Uoe());
        builder.Register(Api(SetClass, "clear", "()V"), (_, _) => throw Uoe());

        // ---- sort / reverse / max / min / fill / addAll ----
        builder.Register(Api(CollectionsClass, "sort", "(Ljava/util/List;)V"), (_, args) => { Sort(state, RequireDex(args[0]), null); return null!; });
        builder.Register(Api(CollectionsClass, "sort", "(Ljava/util/List;Ljava/util/Comparator;)V"), (_, args) => { Sort(state, RequireDex(args[0]), RequireDex(args[1])); return null!; });
        builder.Register(Api(CollectionsClass, "reverse", "(Ljava/util/List;)V"), (_, args) => { state.ArrayLists.Get(RequireDex(args[0])).Elements.Reverse(); return null!; });
        builder.Register(Api(CollectionsClass, "max", "(Ljava/util/Collection;)Ljava/lang/Object;"), (_, args) => MaxMin(state, RequireDex(args[0]), null, max: true));
        builder.Register(Api(CollectionsClass, "max", "(Ljava/util/Collection;Ljava/util/Comparator;)Ljava/lang/Object;"), (_, args) => MaxMin(state, RequireDex(args[0]), RequireDex(args[1]), max: true));
        builder.Register(Api(CollectionsClass, "min", "(Ljava/util/Collection;)Ljava/lang/Object;"), (_, args) => MaxMin(state, RequireDex(args[0]), null, max: false));
        builder.Register(Api(CollectionsClass, "min", "(Ljava/util/Collection;Ljava/util/Comparator;)Ljava/lang/Object;"), (_, args) => MaxMin(state, RequireDex(args[0]), RequireDex(args[1]), max: false));
        builder.Register(Api(CollectionsClass, "fill", "(Ljava/util/List;Ljava/lang/Object;)V"), (_, args) =>
        {
            var peer = state.ArrayLists.Get(RequireDex(args[0]));
            peer.RequireMutable();
            for (int index = 0; index < peer.Elements.Count; index++) peer.Elements[index] = args[1];
            return null!;
        });
        builder.Register(Api(CollectionsClass, "addAll", "(Ljava/util/Collection;[Ljava/lang/Object;)Z"), (_, args) =>
        {
            var target = RequireDex(args[0]);
            var items = args[1] as DexArray ?? throw new ArgumentException("addAll requires an Object[].");
            if (target.TypeDescriptor == ArrayListClass)
            {
                var peer = state.ArrayLists.Get(target);
                peer.RequireMutable();
                for (int index = 0; index < items.Length; index++) peer.Elements.Add(items.Get(index));
                return 1;
            }
            throw Uoe();
        });
        builder.Register(Api(CollectionsClass, "newSetFromMap", "(Ljava/util/Map;)Ljava/util/Set;"), (_, args) =>
        {
            // Bounded: an independent HashSet. The JSONObject recursion-avoidance use
            // (add/contains) works; no write-through to the backing map, and identity
            // (not equals) semantics are not modeled — documented in README #47.
            var set = new DexObject(SetClass);
            state.MapViews.Add(set, new HashSet<object?>());
            return set;
        });
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static DexObject EmptyList(AndroidFrameworkState state)
    {
        var wrapper = new DexObject(ArrayListClass);
        state.ArrayLists.Add(wrapper, new ListPeer { Unmodifiable = true });
        return wrapper;
    }

    private static DexObject EmptyMap(AndroidFrameworkState state)
    {
        var wrapper = new DexObject(HashMapClass);
        state.HashMaps.Add(wrapper, new HashMapPeer { Unmodifiable = true });
        return wrapper;
    }

    private static DexObject EmptySet(AndroidFrameworkState state)
    {
        var wrapper = new DexObject(SetClass);
        state.MapViews.Add(wrapper, new HashSet<object?>());
        return wrapper;
    }

    private static void Sort(AndroidFrameworkState state, DexObject list, DexObject? comparator)
    {
        var peer = state.ArrayLists.Get(list);
        peer.RequireMutable();
        var elements = peer.Elements;
        if (elements.Count <= 1) return;
        var array = elements.ToArray();
        Array.Sort(array, (left, right) => Compare(state, comparator, left, right));
        elements.Clear();
        elements.AddRange(array);
    }

    private static int Compare(AndroidFrameworkState state, DexObject? comparator, object? left, object? right)
    {
        if (comparator is not null)
        {
            var interpreter = state.Interpreter ?? throw new InvalidOperationException("Comparator requires an attached interpreter.");
            return RequireInt(interpreter.InvokeInstanceExact(comparator, "compare", "(Ljava/lang/Object;Ljava/lang/Object;)I", left, right));
        }
        // Natural ordering: strings ordinal, boxed numerics by value, DexObject
        // tries its guest compareTo(Object) (bounded; anything else compares equal).
        if (left is string ls && right is string rs) return string.CompareOrdinal(ls, rs);
        if (left is DexObject ld && right is DexObject rd && ld.TypeDescriptor == rd.TypeDescriptor)
        {
            if (ld.TypeDescriptor == "Ljava/lang/Integer;") return IntRaw(state, ld).CompareTo(IntRaw(state, rd));
            if (ld.TypeDescriptor == "Ljava/lang/Long;") return LongRaw(state, ld).CompareTo(LongRaw(state, rd));
            if (ld.TypeDescriptor == "Ljava/lang/Boolean;") return BooleanRaw(state, ld).CompareTo(BooleanRaw(state, rd));
            if (state.Interpreter is not null)
            {
                try { return RequireInt(state.Interpreter.InvokeInstanceExact(ld, "compareTo", "(Ljava/lang/Object;)I", rd)); }
                catch (Exception) { /* fall through: not Comparable */ }
            }
        }
        return 0;
    }

    private static object MaxMin(AndroidFrameworkState state, DexObject collection, DexObject? comparator, bool max)
    {
        var elements = CollectionElements(state, collection);
        if (elements.Count == 0)
            throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/util/NoSuchElementException;"));
        object? best = elements[0];
        for (int index = 1; index < elements.Count; index++)
        {
            int comparison = Compare(state, comparator, elements[index], best);
            if (max ? comparison > 0 : comparison < 0) best = elements[index];
        }
        return best ?? null!;
    }

    private static List<object?> CollectionElements(AndroidFrameworkState state, DexObject collection) =>
        collection.TypeDescriptor == ArrayListClass
            ? state.ArrayLists.Get(collection).Elements
            : state.MapViews.Get(collection).ToList();

    private static int IntRaw(AndroidFrameworkState state, DexObject box) => (int)state.Boxed.Get(box).RawValue;
    private static long LongRaw(AndroidFrameworkState state, DexObject box) => (long)state.Boxed.Get(box).RawValue;
    private static int BooleanRaw(AndroidFrameworkState state, DexObject box) => (int)state.Boxed.Get(box).RawValue;
    private static int RequireInt(object? value) => AndroidApiBindings.RequireInt(value ?? 0);
    private static GuestExceptionCarrier Uoe() => new(GuestThrowableMetadata.Create(UnsupportedOperation));

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    private static DexObject RequireDex(object? value) => value as DexObject ?? throw new ArgumentException("Expected a guest object.");
}
