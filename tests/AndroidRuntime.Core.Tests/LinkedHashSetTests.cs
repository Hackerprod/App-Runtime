using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for java.util.LinkedHashSet: iteration preserves INSERTION ORDER (its
/// reason to exist over HashSet), a duplicate add returns false and does NOT
/// move the element (first-insertion order, not most-recent-touch), and
/// remove + re-add moves the element to the end (fresh insertion).
/// </summary>
public sealed class LinkedHashSetTests
{
    private const string LinkedHashSet = "Ljava/util/LinkedHashSet;";
    private const string IteratorClass = "Ljava/util/Iterator;";

    [Fact]
    public void Iteration_preserves_insertion_order()
    {
        var (state, registry, _) = Session();
        var set = NewSet(registry, state);
        foreach (var item in new[] { "a", "b", "c", "d" })
            Assert.Equal(1, Invoke(registry, state, LinkedHashSet, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, set, item));
        Assert.Equal(new[] { "a", "b", "c", "d" }, Iterate(registry, state, set));
    }

    [Fact]
    public void Duplicate_add_returns_false_and_does_not_move_the_element()
    {
        var (state, registry, _) = Session();
        var set = NewSet(registry, state);
        foreach (var item in new[] { "a", "b", "c" })
            Invoke(registry, state, LinkedHashSet, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, set, item);
        // Re-adding "a" is false and must NOT move it (first-insertion order).
        Assert.Equal(0, Invoke(registry, state, LinkedHashSet, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, set, "a"));
        Assert.Equal(new[] { "a", "b", "c" }, Iterate(registry, state, set));
    }

    [Fact]
    public void Remove_then_readd_moves_the_element_to_the_end()
    {
        var (state, registry, _) = Session();
        var set = NewSet(registry, state);
        foreach (var item in new[] { "a", "b", "c" })
            Invoke(registry, state, LinkedHashSet, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, set, item);
        Assert.Equal(1, Invoke(registry, state, LinkedHashSet, "remove", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, set, "a"));
        // Fresh insertion after removal goes to the END.
        Assert.Equal(1, Invoke(registry, state, LinkedHashSet, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, set, "a"));
        Assert.Equal(new[] { "b", "c", "a" }, Iterate(registry, state, set));
    }

    [Fact]
    public void Collection_constructor_add_all_and_remove_all_dedup_and_order()
    {
        var (state, registry, _) = Session();
        var source = NewSet(registry, state);
        Invoke(registry, state, LinkedHashSet, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, source, "x");
        Invoke(registry, state, LinkedHashSet, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, source, "y");

        var copy = new DexObject(LinkedHashSet);
        Invoke(registry, state, LinkedHashSet, "<init>", "(Ljava/util/Collection;)V", AndroidInvokeKind.Direct, copy, source);
        Assert.Equal(new[] { "x", "y" }, Iterate(registry, state, copy));
        Assert.Equal(0, Invoke(registry, state, LinkedHashSet, "isEmpty", "()Z", AndroidInvokeKind.Virtual, copy));

        // addAll dedups (returns true only when changed).
        var other = NewSet(registry, state);
        Invoke(registry, state, LinkedHashSet, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, other, "y");
        Assert.Equal(0, Invoke(registry, state, LinkedHashSet, "addAll", "(Ljava/util/Collection;)Z", AndroidInvokeKind.Virtual, copy, other));
        Assert.Equal(new[] { "x", "y" }, Iterate(registry, state, copy));

        // removeAll removes the matched elements.
        Assert.Equal(1, Invoke(registry, state, LinkedHashSet, "removeAll", "(Ljava/util/Collection;)Z", AndroidInvokeKind.Virtual, copy, other));
        Assert.Equal(new[] { "x" }, Iterate(registry, state, copy));
    }

    private static object[] Iterate(AndroidApiRegistry registry, AndroidFrameworkState state, DexObject set)
    {
        var iterator = (DexObject)Invoke(registry, state, LinkedHashSet, "iterator", "()Ljava/util/Iterator;", AndroidInvokeKind.Virtual, set);
        var results = new List<object>();
        while ((int)Invoke(registry, state, IteratorClass, "hasNext", "()Z", AndroidInvokeKind.Virtual, iterator) == 1)
            results.Add(Invoke(registry, state, IteratorClass, "next", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, iterator));
        return results.ToArray();
    }

    private static DexObject NewSet(AndroidApiRegistry registry, AndroidFrameworkState state)
    {
        var set = new DexObject(LinkedHashSet);
        Invoke(registry, state, LinkedHashSet, "<init>", "()V", AndroidInvokeKind.Direct, set);
        return set;
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
