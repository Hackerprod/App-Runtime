using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for java.util.Collection.toArray(T[]) against the REAL Java SE 17
/// contract (verified from the Collection.toArray(T[]) docs):
/// - a.length >= size: elements copied INTO a; if a.length > size the slot
///   IMMEDIATELY AFTER the last copied element is set to null (the documented
///   null terminator), the rest of a untouched.
/// - a.length < size: a NEW array of the specified array's runtime type and
///   the collection's size is allocated and returned; the caller's a untouched.
/// - Iteration order is the peer's own order; the multi-peer-store dispatch
///   (same pattern as Collection.removeAll/remove/addAll) works across at
///   least two backing stores (ArrayList and CopyOnWriteArraySet).
/// </summary>
public sealed class CollectionToArrayTests
{
    private const string Collection = "Ljava/util/Collection;";
    private const string ArrayList = "Ljava/util/ArrayList;";
    private const string CopyOnWriteArraySet = "Ljava/util/concurrent/CopyOnWriteArraySet;";
    private const string HashSet = "Ljava/util/HashSet;";

    [Fact]
    public void Destination_large_enough_copies_in_place_with_null_terminator_when_strictly_larger()
    {
        var (state, registry, _) = Session();
        var list = NewArrayList(registry, state, ["a", "b"]);
        var destination = new DexArray("[Ljava/lang/Object;", 4);
        destination.Set(3, "keep-me"); // beyond the terminator slot must be untouched

        var result = (DexArray)Invoke(registry, state, Collection, "toArray", "([Ljava/lang/Object;)[Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, destination);

        Assert.Same(destination, result); // fits: the SAME array is returned
        Assert.Equal(4, result.Length);
        Assert.Equal("a", result.Get(0));
        Assert.Equal("b", result.Get(1));
        Assert.Null(result.Get(2)); // null terminator immediately after last element
        Assert.Equal("keep-me", result.Get(3)); // rest untouched
    }

    [Fact]
    public void Destination_exactly_equal_size_copies_without_null_terminator()
    {
        var (state, registry, _) = Session();
        var list = NewArrayList(registry, state, ["x", "y", "z"]);
        var destination = new DexArray("[Ljava/lang/Object;", 3);

        var result = (DexArray)Invoke(registry, state, Collection, "toArray", "([Ljava/lang/Object;)[Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, destination);

        Assert.Same(destination, result);
        Assert.Equal(3, result.Length);
        Assert.Equal("x", result.Get(0));
        Assert.Equal("y", result.Get(1));
        Assert.Equal("z", result.Get(2));
    }

    [Fact]
    public void Destination_too_small_allocates_new_array_and_leaves_original_untouched()
    {
        var (state, registry, _) = Session();
        var list = NewArrayList(registry, state, ["a", "b", "c"]);
        var destination = new DexArray("[Ljava/lang/Object;", 1);
        destination.Set(0, "only");

        var result = (DexArray)Invoke(registry, state, Collection, "toArray", "([Ljava/lang/Object;)[Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, destination);

        Assert.NotSame(destination, result); // too small: a NEW array is allocated
        Assert.Equal("[Ljava/lang/Object;", result.ArrayDescriptor);
        Assert.Equal(3, result.Length);
        Assert.Equal("a", result.Get(0));
        Assert.Equal("b", result.Get(1));
        Assert.Equal("c", result.Get(2));
        // The caller's array is untouched.
        Assert.Equal(1, destination.Length);
        Assert.Equal("only", destination.Get(0));
    }

    [Fact]
    public void Destination_too_small_allocates_new_array_of_the_destination_runtime_type()
    {
        // Real contract: toArray(T[] a) allocates a NEW array with the SPECIFIED
        // array's runtime type — passing Integer[0] yields a new Integer[] the
        // caller can then check-cast (Lokio Options.of does exactly this).
        var (state, registry, _) = Session();
        var list = new DexObject(ArrayList);
        Invoke(registry, state, ArrayList, "<init>", "()V", AndroidInvokeKind.Direct, list);
        Invoke(registry, state, ArrayList, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, state.BoxedObject("Ljava/lang/Integer;", 10));
        Invoke(registry, state, ArrayList, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, state.BoxedObject("Ljava/lang/Integer;", 20));
        var destination = new DexArray("[Ljava/lang/Integer;", 0);

        var result = (DexArray)Invoke(registry, state, Collection, "toArray", "([Ljava/lang/Object;)[Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, destination);

        Assert.NotSame(destination, result);
        Assert.Equal("[Ljava/lang/Integer;", result.ArrayDescriptor);
        Assert.Equal(2, result.Length);
        Assert.Equal(state.BoxedObject("Ljava/lang/Integer;", 10), result.Get(0));
        Assert.Equal(state.BoxedObject("Ljava/lang/Integer;", 20), result.Get(1));
    }

    [Fact]
    public void Empty_collection_fits_any_array_and_null_terminates_the_first_slot()
    {
        var (state, registry, _) = Session();
        var list = NewArrayList(registry, state, []);
        var destination = new DexArray("[Ljava/lang/Object;", 2);

        var result = (DexArray)Invoke(registry, state, Collection, "toArray", "([Ljava/lang/Object;)[Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, destination);

        Assert.Same(destination, result);
        Assert.Null(result.Get(0)); // slot immediately after the (zero) copied elements
        Assert.Null(result.Get(1)); // untouched default (still null)
    }

    [Fact]
    public void Dispatch_works_across_arraylist_and_copy_on_write_array_set_receivers()
    {
        var (state, registry, _) = Session();

        // ArrayList receiver (list store).
        var list = NewArrayList(registry, state, ["first", "second"]);
        var listDestination = new DexArray("[Ljava/lang/Object;", 2);
        var fromList = (DexArray)Invoke(registry, state, Collection, "toArray", "([Ljava/lang/Object;)[Ljava/lang/Object;", AndroidInvokeKind.Virtual, list, listDestination);
        Assert.Equal("first", fromList.Get(0));
        Assert.Equal("second", fromList.Get(1));

        // CopyOnWriteArraySet receiver (set store) — the regression-preventing
        // dispatch must resolve to the set store, not silently the list store.
        var set = new DexObject(CopyOnWriteArraySet);
        Invoke(registry, state, CopyOnWriteArraySet, "<init>", "()V", AndroidInvokeKind.Direct, set);
        Invoke(registry, state, HashSet, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, set, "alpha");
        Invoke(registry, state, HashSet, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, set, "beta");
        var setDestination = new DexArray("[Ljava/lang/Object;", 2);
        var fromSet = (DexArray)Invoke(registry, state, Collection, "toArray", "([Ljava/lang/Object;)[Ljava/lang/Object;", AndroidInvokeKind.Virtual, set, setDestination);
        Assert.Contains(fromSet.Get(0), new[] { "alpha", "beta" });
        Assert.Contains(fromSet.Get(1), new[] { "alpha", "beta" });
        Assert.NotEqual(fromSet.Get(0), fromSet.Get(1));
    }

    private static DexObject NewArrayList(AndroidApiRegistry registry, AndroidFrameworkState state, string[] items)
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
