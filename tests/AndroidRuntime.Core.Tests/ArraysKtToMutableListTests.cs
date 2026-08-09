using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for kotlin.collections.Array&lt;T&gt;.toMutableList() against the REAL
/// Kotlin stdlib contract (verified from _Arrays.kt source): the returned list
/// is a new, INDEPENDENT ArrayList containing a copy of the array's elements —
/// mutating the list does not affect the source array and vice versa; element
/// order preserved; empty array produces an empty list.
/// </summary>
public sealed class ArraysKtToMutableListTests
{
    private const string ArraysKt = "Lkotlin/collections/ArraysKt;";
    private const string ArrayList = "Ljava/util/ArrayList;";
    private const string IteratorClass = "Ljava/util/Iterator;";

    [Fact]
    public void To_mutable_list_returns_an_independent_copy_in_array_order()
    {
        var (state, registry, _) = Session();
        var array = new DexArray("[Ljava/lang/Object;", 3);
        array.Set(0, "a");
        array.Set(1, "b");
        array.Set(2, "c");

        var list = (DexObject)Invoke(registry, state, ArraysKt, "toMutableList", "([Ljava/lang/Object;)Ljava/util/List;", AndroidInvokeKind.Static, array);

        Assert.Equal("Ljava/util/ArrayList;", list.TypeDescriptor);
        Assert.Equal(3, Invoke(registry, state, ArrayList, "size", "()I", AndroidInvokeKind.Virtual, list));
        Assert.Equal("a", Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 0));
        Assert.Equal("b", Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 1));
        Assert.Equal("c", Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 2));
    }

    [Fact]
    public void Mutating_the_returned_list_does_not_affect_the_source_array()
    {
        var (state, registry, _) = Session();
        var array = new DexArray("[Ljava/lang/Object;", 2);
        array.Set(0, "x");
        array.Set(1, "y");

        var list = (DexObject)Invoke(registry, state, ArraysKt, "toMutableList", "([Ljava/lang/Object;)Ljava/util/List;", AndroidInvokeKind.Static, array);
        Invoke(registry, state, ArrayList, "remove", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 0);
        Assert.Equal(1, Invoke(registry, state, ArrayList, "size", "()I", AndroidInvokeKind.Virtual, list));

        // Independent storage: the source array still holds both elements.
        Assert.Equal(2, array.Length);
        Assert.Equal("x", array.Get(0));
        Assert.Equal("y", array.Get(1));
    }

    [Fact]
    public void Mutating_the_source_array_does_not_affect_the_returned_list()
    {
        var (state, registry, _) = Session();
        var array = new DexArray("[Ljava/lang/Object;", 1);
        array.Set(0, "only");

        var list = (DexObject)Invoke(registry, state, ArraysKt, "toMutableList", "([Ljava/lang/Object;)Ljava/util/List;", AndroidInvokeKind.Static, array);
        array.Set(0, "changed");

        Assert.Equal(1, Invoke(registry, state, ArrayList, "size", "()I", AndroidInvokeKind.Virtual, list));
        Assert.Equal("only", Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 0));
    }

    [Fact]
    public void Empty_array_produces_an_empty_list()
    {
        var (state, registry, _) = Session();
        var array = new DexArray("[Ljava/lang/Object;", 0);
        var list = (DexObject)Invoke(registry, state, ArraysKt, "toMutableList", "([Ljava/lang/Object;)Ljava/util/List;", AndroidInvokeKind.Static, array);
        Assert.Equal(1, Invoke(registry, state, ArrayList, "isEmpty", "()Z", AndroidInvokeKind.Virtual, list));
        Assert.Equal(0, Invoke(registry, state, ArrayList, "size", "()I", AndroidInvokeKind.Virtual, list));
    }

    [Fact]
    public void Null_elements_are_copied_as_null_entries()
    {
        var (state, registry, _) = Session();
        var array = new DexArray("[Ljava/lang/Object;", 2);
        array.Set(0, null!);
        array.Set(1, "v");

        var list = (DexObject)Invoke(registry, state, ArraysKt, "toMutableList", "([Ljava/lang/Object;)Ljava/util/List;", AndroidInvokeKind.Static, array);
        Assert.Equal(2, Invoke(registry, state, ArrayList, "size", "()I", AndroidInvokeKind.Virtual, list));
        Assert.Null(Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 0));
        Assert.Equal("v", Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 1));
    }

    [Fact]
    public void Returned_list_iterates_the_copied_elements()
    {
        var (state, registry, _) = Session();
        var array = new DexArray("[Ljava/lang/Object;", 3);
        array.Set(0, state.BoxedObject("Ljava/lang/Integer;", 1));
        array.Set(1, state.BoxedObject("Ljava/lang/Integer;", 2));
        array.Set(2, state.BoxedObject("Ljava/lang/Integer;", 3));

        var list = (DexObject)Invoke(registry, state, ArraysKt, "toMutableList", "([Ljava/lang/Object;)Ljava/util/List;", AndroidInvokeKind.Static, array);
        var iterator = (DexObject)Invoke(registry, state, ArrayList, "iterator", "()Ljava/util/Iterator;", AndroidInvokeKind.Virtual, list);
        var seen = new List<object?>();
        while ((int)Invoke(registry, state, IteratorClass, "hasNext", "()Z", AndroidInvokeKind.Virtual, iterator) == 1)
            seen.Add(Invoke(registry, state, IteratorClass, "next", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, iterator));
        Assert.Equal(state.BoxedObject("Ljava/lang/Integer;", 1), seen[0]);
        Assert.Equal(state.BoxedObject("Ljava/lang/Integer;", 2), seen[1]);
        Assert.Equal(state.BoxedObject("Ljava/lang/Integer;", 3), seen[2]);
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
