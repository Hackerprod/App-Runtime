using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for kotlin.collections.mutableListOf(vararg) against the real Kotlin
/// stdlib contract: returns a NEW independent MutableList containing a COPY of
/// the varargs elements (mutating it does not affect the source array and vice
/// versa), element order preserved, zero-arg call produces an empty (but still
/// real, mutable) list.
/// </summary>
public sealed class MutableListOfTests
{
    private const string CollectionsKt = "Lkotlin/collections/CollectionsKt;";
    private const string ArrayList = "Ljava/util/ArrayList;";

    [Fact]
    public void Mutable_list_of_returns_an_independent_copy_in_element_order()
    {
        var (state, registry, _) = Session();
        var array = new DexArray("[Ljava/lang/Object;", 3);
        array.Set(0, "a");
        array.Set(1, "b");
        array.Set(2, "c");

        var list = (DexObject)Invoke(registry, state, CollectionsKt, "mutableListOf", "([Ljava/lang/Object;)Ljava/util/List;", AndroidInvokeKind.Static, array);

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

        var list = (DexObject)Invoke(registry, state, CollectionsKt, "mutableListOf", "([Ljava/lang/Object;)Ljava/util/List;", AndroidInvokeKind.Static, array);
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

        var list = (DexObject)Invoke(registry, state, CollectionsKt, "mutableListOf", "([Ljava/lang/Object;)Ljava/util/List;", AndroidInvokeKind.Static, array);
        array.Set(0, "changed");

        Assert.Equal(1, Invoke(registry, state, ArrayList, "size", "()I", AndroidInvokeKind.Virtual, list));
        Assert.Equal("only", Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 0));
    }

    [Fact]
    public void Empty_varargs_produces_an_empty_but_real_mutable_list()
    {
        var (state, registry, _) = Session();
        var array = new DexArray("[Ljava/lang/Object;", 0);

        var list = (DexObject)Invoke(registry, state, CollectionsKt, "mutableListOf", "([Ljava/lang/Object;)Ljava/util/List;", AndroidInvokeKind.Static, array);

        // Empty, but a real ArrayList peer: mutable — add must succeed.
        Assert.Equal(1, Invoke(registry, state, ArrayList, "isEmpty", "()Z", AndroidInvokeKind.Virtual, list));
        Assert.Equal(0, Invoke(registry, state, ArrayList, "size", "()I", AndroidInvokeKind.Virtual, list));
        Invoke(registry, state, ArrayList, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, "later");
        Assert.Equal(1, Invoke(registry, state, ArrayList, "size", "()I", AndroidInvokeKind.Virtual, list));
        Assert.Equal("later", Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 0));
    }

    [Fact]
    public void Null_elements_are_copied_as_null_entries()
    {
        var (state, registry, _) = Session();
        var array = new DexArray("[Ljava/lang/Object;", 2);
        array.Set(0, null!);
        array.Set(1, "v");

        var list = (DexObject)Invoke(registry, state, CollectionsKt, "mutableListOf", "([Ljava/lang/Object;)Ljava/util/List;", AndroidInvokeKind.Static, array);
        Assert.Equal(2, Invoke(registry, state, ArrayList, "size", "()I", AndroidInvokeKind.Virtual, list));
        Assert.Null(Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 0));
        Assert.Equal("v", Invoke(registry, state, ArrayList, "get", "(I)Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, 1));
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
