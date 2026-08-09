using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for kotlin.collections.MutableList&lt;T&gt;.sort() against the REAL Kotlin
/// JVM stdlib contract (verified from MutableCollectionsJVM.kt): sorts IN
/// PLACE (same list object mutated, nothing returned — the binding returns
/// void), delegates to java.util.Collections.sort(List) semantics via the
/// shared JavaUtilCollectionsBindings.Sort helper, size>1 guard skips
/// already-sorted/empty/single-element lists, natural ordering for
/// strings/boxed numerics.
/// </summary>
public sealed class CollectionsKtSortTests
{
    private const string CollectionsKt = "Lkotlin/collections/CollectionsKt;";
    private const string ArrayList = "Ljava/util/ArrayList;";
    private const string Integer = "Ljava/lang/Integer;";

    [Fact]
    public void Sort_sorts_strings_in_place_on_the_same_list_object()
    {
        var (state, registry, _) = Session();
        var list = NewList(registry, state, ["banana", "apple", "cherry"]);

        var result = Invoke(registry, state, CollectionsKt, "sort", "(Ljava/util/List;)V", AndroidInvokeKind.Static, list);

        // Real contract: sort() is Unit — returns nothing, mutates the SAME list.
        Assert.Null(result);
        Assert.Equal(3, Invoke(registry, state, ArrayList, "size", "()I", AndroidInvokeKind.Virtual, list));
        Assert.Equal("apple", Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 0));
        Assert.Equal("banana", Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 1));
        Assert.Equal("cherry", Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 2));
    }

    [Fact]
    public void Sort_sorts_boxed_numerics_by_value()
    {
        var (state, registry, _) = Session();
        var list = new DexObject(ArrayList);
        Invoke(registry, state, ArrayList, "<init>", "()V", AndroidInvokeKind.Direct, list);
        Invoke(registry, state, ArrayList, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, state.BoxedObject(Integer, 30));
        Invoke(registry, state, ArrayList, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, state.BoxedObject(Integer, 10));
        Invoke(registry, state, ArrayList, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, state.BoxedObject(Integer, 20));

        Invoke(registry, state, CollectionsKt, "sort", "(Ljava/util/List;)V", AndroidInvokeKind.Static, list);

        Assert.Equal(state.BoxedObject(Integer, 10), Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 0));
        Assert.Equal(state.BoxedObject(Integer, 20), Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 1));
        Assert.Equal(state.BoxedObject(Integer, 30), Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 2));
    }

    [Fact]
    public void Sort_is_a_noop_on_empty_single_and_already_sorted_lists()
    {
        var (state, registry, _) = Session();

        var empty = NewList(registry, state, []);
        Invoke(registry, state, CollectionsKt, "sort", "(Ljava/util/List;)V", AndroidInvokeKind.Static, empty);
        Assert.Equal(0, Invoke(registry, state, ArrayList, "size", "()I", AndroidInvokeKind.Virtual, empty));

        var single = NewList(registry, state, ["only"]);
        Invoke(registry, state, CollectionsKt, "sort", "(Ljava/util/List;)V", AndroidInvokeKind.Static, single);
        Assert.Equal("only", Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, single, 0));

        var sorted = NewList(registry, state, ["a", "b", "c"]);
        Invoke(registry, state, CollectionsKt, "sort", "(Ljava/util/List;)V", AndroidInvokeKind.Static, sorted);
        Assert.Equal("a", Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, sorted, 0));
        Assert.Equal("b", Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, sorted, 1));
        Assert.Equal("c", Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, sorted, 2));
    }

    [Fact]
    public void Sort_uses_the_same_helper_as_collections_sort_natural_ordering()
    {
        var (state, registry, _) = Session();
        var list = NewList(registry, state, ["z", "a", "m"]);

        // Same observable behavior as java.util.Collections.sort(List) — the
        // binding delegates to the identical JavaUtilCollectionsBindings.Sort.
        Invoke(registry, state, CollectionsKt, "sort", "(Ljava/util/List;)V", AndroidInvokeKind.Static, list);
        Invoke(registry, state, "Ljava/util/Collections;", "sort", "(Ljava/util/List;)V", AndroidInvokeKind.Static, list);

        // Sorting an already-sorted list is a no-op; order is stable natural.
        Assert.Equal("a", Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 0));
        Assert.Equal("m", Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 1));
        Assert.Equal("z", Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 2));
    }

    private static DexObject NewList(AndroidApiRegistry registry, AndroidFrameworkState state, string[] items)
    {
        var list = new DexObject(ArrayList);
        Invoke(registry, state, ArrayList, "<init>", "()V", AndroidInvokeKind.Direct, list);
        foreach (string item in items)
            Invoke(registry, state, ArrayList, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, item);
        return list;
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
