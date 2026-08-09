using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for java.util.Collections: synchronized* returns the same object (GIL
/// serializes guest execution), unmodifiable*/empty*/singleton* enforce real
/// immutability (reads pass, writes throw UnsupportedOperationException), and
/// sort/reverse/max/min/fill/addAll/newSetFromMap follow the real contracts.
/// </summary>
public sealed class CollectionsTests
{
    private const string CollectionsClass = "Ljava/util/Collections;";
    private const string ArrayListClass = "Ljava/util/ArrayList;";
    private const string HashMapClass = "Ljava/util/HashMap;";
    private const string SetClass = "Ljava/util/Set;";

    [Fact]
    public void Synchronized_map_returns_the_same_object_under_the_gil()
    {
        var (state, registry, _) = Session();
        var map = new DexObject(HashMapClass);
        Invoke(registry, state, HashMapClass, "<init>", "()V", AndroidInvokeKind.Direct, map);
        Assert.Same(map, Invoke(registry, state, CollectionsClass, "synchronizedMap", "(Ljava/util/Map;)Ljava/util/Map;", AndroidInvokeKind.Static, map));
    }

    [Fact]
    public void Unmodifiable_list_reads_pass_and_writes_throw()
    {
        var (state, registry, _) = Session();
        var list = NewList(registry, state);
        Invoke(registry, state, ArrayListClass, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, "a");
        var wrapper = (DexObject)Invoke(registry, state, CollectionsClass, "unmodifiableList", "(Ljava/util/List;)Ljava/util/List;", AndroidInvokeKind.Static, list);

        Assert.Equal(1, Invoke(registry, state, ArrayListClass, "size", "()I", AndroidInvokeKind.Virtual, wrapper));
        Assert.Equal("a", Invoke(registry, state, ArrayListClass, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, wrapper, 0));
        var error = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, ArrayListClass, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, wrapper, "x"));
        Assert.Equal("Ljava/lang/UnsupportedOperationException;", error.Throwable.TypeDescriptor);
        Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, ArrayListClass, "clear", "()V", AndroidInvokeKind.Virtual, wrapper));
        // The wrapper shares the backing: mutating the original is visible.
        Invoke(registry, state, ArrayListClass, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, "b");
        Assert.Equal(2, Invoke(registry, state, ArrayListClass, "size", "()I", AndroidInvokeKind.Virtual, wrapper));
    }

    [Fact]
    public void Unmodifiable_map_reads_pass_and_writes_throw_but_shares_backing()
    {
        var (state, registry, _) = Session();
        var map = NewMap(registry, state);
        Invoke(registry, state, HashMapClass, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "k", state.BoxedObject("Ljava/lang/Integer;", 1));
        var wrapper = (DexObject)Invoke(registry, state, CollectionsClass, "unmodifiableMap", "(Ljava/util/Map;)Ljava/util/Map;", AndroidInvokeKind.Static, map);

        Assert.Equal(state.BoxedObject("Ljava/lang/Integer;", 1), Invoke(registry, state, HashMapClass, "get", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, wrapper, "k"));
        var error = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, HashMapClass, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, wrapper, "x", state.BoxedObject("Ljava/lang/Integer;", 2)));
        Assert.Equal("Ljava/lang/UnsupportedOperationException;", error.Throwable.TypeDescriptor);
        // Shared backing: a put to the ORIGINAL is visible through the wrapper.
        Invoke(registry, state, HashMapClass, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "y", state.BoxedObject("Ljava/lang/Integer;", 3));
        Assert.Equal(state.BoxedObject("Ljava/lang/Integer;", 3), Invoke(registry, state, HashMapClass, "get", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, wrapper, "y"));
    }

    [Fact]
    public void Empty_and_singleton_collections_are_immutable()
    {
        var (state, registry, _) = Session();
        var empty = (DexObject)Invoke(registry, state, CollectionsClass, "emptyList", "()Ljava/util/List;", AndroidInvokeKind.Static);
        Assert.Equal(0, Invoke(registry, state, ArrayListClass, "size", "()I", AndroidInvokeKind.Virtual, empty));
        Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, ArrayListClass, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, empty, "x"));

        var singleton = (DexObject)Invoke(registry, state, CollectionsClass, "singletonList", "(Ljava/lang/Object;)Ljava/util/List;", AndroidInvokeKind.Static, "only");
        Assert.Equal(1, Invoke(registry, state, ArrayListClass, "size", "()I", AndroidInvokeKind.Virtual, singleton));
        Assert.Equal("only", Invoke(registry, state, ArrayListClass, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, singleton, 0));
        Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, ArrayListClass, "set", "(ILjava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, singleton, 0, "nope"));

        var emptyMap = (DexObject)Invoke(registry, state, CollectionsClass, "emptyMap", "()Ljava/util/Map;", AndroidInvokeKind.Static);
        Assert.Equal(0, Invoke(registry, state, HashMapClass, "size", "()I", AndroidInvokeKind.Virtual, emptyMap));
        Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, HashMapClass, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, emptyMap, "k", "v"));
    }

    [Fact]
    public void Sort_reverse_max_min_fill_and_add_all_follow_real_contracts()
    {
        var (state, registry, _) = Session();
        var list = NewList(registry, state);
        foreach (var item in new[] { "b", "a", "c" })
            Invoke(registry, state, ArrayListClass, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, item);

        Invoke(registry, state, CollectionsClass, "sort", "(Ljava/util/List;)V", AndroidInvokeKind.Static, list);
        Assert.Equal("a", Invoke(registry, state, ArrayListClass, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 0));
        Assert.Equal("c", Invoke(registry, state, ArrayListClass, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 2));

        Invoke(registry, state, CollectionsClass, "reverse", "(Ljava/util/List;)V", AndroidInvokeKind.Static, list);
        Assert.Equal("c", Invoke(registry, state, ArrayListClass, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 0));

        var max = Invoke(registry, state, CollectionsClass, "max", "(Ljava/util/Collection;)Ljava/lang/Object;", AndroidInvokeKind.Static, list);
        Assert.Equal("c", max);
        var min = Invoke(registry, state, CollectionsClass, "min", "(Ljava/util/Collection;)Ljava/lang/Object;", AndroidInvokeKind.Static, list);
        Assert.Equal("a", min);

        Invoke(registry, state, CollectionsClass, "fill", "(Ljava/util/List;Ljava/lang/Object;)V", AndroidInvokeKind.Static, list, "z");
        Assert.Equal("z", Invoke(registry, state, ArrayListClass, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 1));

        var empty = NewList(registry, state);
        var items = new DexArray("[Ljava/lang/Object;", 2);
        items.Set(0, "p");
        items.Set(1, "q");
        Assert.Equal(1, Invoke(registry, state, CollectionsClass, "addAll", "(Ljava/util/Collection;[Ljava/lang/Object;)Z", AndroidInvokeKind.Static, empty, items));
        Assert.Equal(2, Invoke(registry, state, ArrayListClass, "size", "()I", AndroidInvokeKind.Virtual, empty));
    }

    [Fact]
    public void Sort_with_guest_comparator_invokes_compare()
    {
        var (state, registry, interpreter) = Session();
        var list = NewList(registry, state);
        foreach (var item in new[] { "b", "a", "c" })
            Invoke(registry, state, ArrayListClass, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, item);

        // Lc/Cmp; compare() delegates to String.compareTo -> natural order.
        var comparator = new DexObject("Lc/Cmp;");
        Invoke(registry, state, CollectionsClass, "sort", "(Ljava/util/List;Ljava/util/Comparator;)V", AndroidInvokeKind.Static, list, comparator);
        Assert.Equal("a", Invoke(registry, state, ArrayListClass, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 0));
        Assert.Equal("c", Invoke(registry, state, ArrayListClass, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 2));
    }

    private static DexObject NewList(AndroidApiRegistry registry, AndroidFrameworkState state)
    {
        var list = new DexObject(ArrayListClass);
        Invoke(registry, state, ArrayListClass, "<init>", "()V", AndroidInvokeKind.Direct, list);
        return list;
    }

    private static DexObject NewMap(AndroidApiRegistry registry, AndroidFrameworkState state)
    {
        var map = new DexObject(HashMapClass);
        Invoke(registry, state, HashMapClass, "<init>", "()V", AndroidInvokeKind.Direct, map);
        return map;
    }

    private static (AndroidFrameworkState State, AndroidApiRegistry Registry, DexInterpreter Interpreter) Session()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new QuietLogSink()).Build();
        var dex = BuildDex();
        var interpreter = new DexInterpreter(dex, registry, gil: state.Gil);
        state.Gil = interpreter.Gil;
        state.AttachInterpreter(interpreter);
        return (state, registry, interpreter);
    }

    private static DexFile BuildDex()
    {
        var dex = new DexFile();
        // Lc/Cmp; compare(Object,Object)I: invoke-virtual String.compareTo {v1,v2};
        // move-result v1; return v1  (delegates to natural string order).
        var cmp = new DexClass { Descriptor = "Lc/Cmp;", SuperclassDescriptor = "Ljava/lang/Object;" };
        cmp.DirectMethods.Add(Method("Lc/Cmp;", "compare", "(Ljava/lang/Object;Ljava/lang/Object;)I", 3, 3,
        [
            0x206e, 0x0000, 0x0021,  // invoke-virtual {v1,v2} String.compareTo (idx 0)
            0x010a,                   // move-result v1
            0x010f                    // return v1
        ], isStatic: false));
        dex.Classes.Add(cmp);
        dex.Methods.Add(Ref("Ljava/lang/String;", "compareTo", "(Ljava/lang/String;)I")); // 0
        dex.BuildIndexes();
        return dex;
    }

    private static DexMethodRef Ref(string owner, string name, string descriptor)
    {
        int close = descriptor.IndexOf(')');
        var parameters = new List<string>();
        for (int index = 1; index < close;)
        {
            int start = index;
            if (descriptor[index] == 'L') index = descriptor.IndexOf(';', index) + 1; else index++;
            parameters.Add(descriptor[start..index]);
        }
        return new DexMethodRef { ClassDescriptor = owner, Name = name, Proto = new DexProto { ReturnType = descriptor[(close + 1)..], ParameterTypes = parameters } };
    }
    private static DexEncodedMethod Method(string owner, string name, string descriptor, int registers, int ins, ushort[] instructions, bool isStatic = true) => new()
    {
        AccessFlags = isStatic ? DexConstants.ACC_STATIC : 0,
        Method = Ref(owner, name, descriptor),
        Code = new DexCodeItem { RegistersSize = registers, InsSize = ins, OutsSize = 0, Instructions = instructions }
    };

    private static object Invoke(AndroidApiRegistry registry, AndroidFrameworkState state, string owner, string name, string descriptor, AndroidInvokeKind kind, params object[] args)
    {
        var api = new AndroidApiMethodId(owner, name, descriptor);
        var context = new AndroidApiSessionContext(state.SessionId, state.PackageName, state.ActivityDescriptor, default, () => true);
        if (state.Interpreter is not null) context.IsTypeAssignable = state.Interpreter.IsGuestTypeAssignable;
        return registry.Invoke(context, new AndroidApiCallSite("Ltest;->run()V", 0, api, api, kind), args);
    }

    private sealed class QuietLogSink : IAndroidLogSink { public int Info(AndroidLogEntry entry) => 1; }
}
