using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for java.util.ArrayDeque: the throws-vs-null-on-empty distinction
/// (getLast/remove/removeFirst/removeLast/pop throw NoSuchElementException;
/// peek/poll/pollFirst return null), null-element rejection (NPE, unlike
/// ArrayList), FIFO queue order, LIFO stack order, and remove(Object).
/// </summary>
public sealed class ArrayDequeTests
{
    private const string ArrayDeque = "Ljava/util/ArrayDeque;";

    [Fact]
    public void Throwing_and_null_returns_on_empty_follow_real_contracts()
    {
        var (state, registry, _) = Session();
        var deque = NewDeque(registry, state);

        Assert.Null(Invoke(registry, state, ArrayDeque, "peek", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, deque));
        Assert.Null(Invoke(registry, state, ArrayDeque, "poll", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, deque));
        Assert.Null(Invoke(registry, state, ArrayDeque, "pollFirst", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, deque));

        var getLast = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, ArrayDeque, "getLast", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, deque));
        Assert.Equal("Ljava/util/NoSuchElementException;", getLast.Throwable.TypeDescriptor);
        Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, ArrayDeque, "remove", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, deque));
        Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, ArrayDeque, "removeFirst", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, deque));
        Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, ArrayDeque, "removeLast", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, deque));
        Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, ArrayDeque, "pop", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, deque));
    }

    [Fact]
    public void Null_elements_are_rejected_with_null_pointer_exception()
    {
        var (state, registry, _) = Session();
        var deque = NewDeque(registry, state);
        var error = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, ArrayDeque, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, deque, null!));
        Assert.Equal("Ljava/lang/NullPointerException;", error.Throwable.TypeDescriptor);
        Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, ArrayDeque, "addFirst", "(Ljava/lang/Object;)V", AndroidInvokeKind.Virtual, deque, null!));
        Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, ArrayDeque, "push", "(Ljava/lang/Object;)V", AndroidInvokeKind.Virtual, deque, null!));
    }

    [Fact]
    public void Fifo_queue_and_lifo_stack_orders_share_the_storage()
    {
        var (state, registry, _) = Session();
        var queue = NewDeque(registry, state);
        foreach (var item in new[] { "a", "b", "c" })
            Invoke(registry, state, ArrayDeque, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, queue, item);
        Assert.Equal("a", Invoke(registry, state, ArrayDeque, "poll", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, queue));
        Assert.Equal("b", Invoke(registry, state, ArrayDeque, "poll", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, queue));
        Assert.Equal("c", Invoke(registry, state, ArrayDeque, "poll", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, queue));

        var stack = NewDeque(registry, state);
        foreach (var item in new[] { "a", "b", "c" })
            Invoke(registry, state, ArrayDeque, "push", "(Ljava/lang/Object;)V", AndroidInvokeKind.Virtual, stack, item);
        Assert.Equal("c", Invoke(registry, state, ArrayDeque, "pop", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, stack));
        Assert.Equal("b", Invoke(registry, state, ArrayDeque, "pop", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, stack));
        Assert.Equal("a", Invoke(registry, state, ArrayDeque, "pop", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, stack));
    }

    [Fact]
    public void First_last_operations_and_remove_object_follow_real_semantics()
    {
        var (state, registry, _) = Session();
        var deque = NewDeque(registry, state);
        Invoke(registry, state, ArrayDeque, "addLast", "(Ljava/lang/Object;)V", AndroidInvokeKind.Virtual, deque, "x");
        Invoke(registry, state, ArrayDeque, "addLast", "(Ljava/lang/Object;)V", AndroidInvokeKind.Virtual, deque, "y");
        Invoke(registry, state, ArrayDeque, "addFirst", "(Ljava/lang/Object;)V", AndroidInvokeKind.Virtual, deque, "z");
        // z, x, y
        Assert.Equal("z", Invoke(registry, state, ArrayDeque, "peek", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, deque));
        Assert.Equal("y", Invoke(registry, state, ArrayDeque, "getLast", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, deque));
        Assert.Equal("y", Invoke(registry, state, ArrayDeque, "removeLast", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, deque));
        Assert.Equal("z", Invoke(registry, state, ArrayDeque, "removeFirst", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, deque));
        // x remains; remove(Object) removes the first occurrence.
        Assert.Equal(1, Invoke(registry, state, ArrayDeque, "remove", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, deque, "x"));
        Assert.Equal(0, Invoke(registry, state, ArrayDeque, "remove", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, deque, "x"));
        Assert.Equal(1, Invoke(registry, state, ArrayDeque, "isEmpty", "()Z", AndroidInvokeKind.Virtual, deque));
    }

    [Fact]
    public void Collection_constructor_and_iterator_work()
    {
        var (state, registry, _) = Session();
        var source = NewDeque(registry, state);
        Invoke(registry, state, ArrayDeque, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, source, "a");
        Invoke(registry, state, ArrayDeque, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, source, "b");
        var copy = new DexObject(ArrayDeque);
        Invoke(registry, state, ArrayDeque, "<init>", "(Ljava/util/Collection;)V", AndroidInvokeKind.Direct, copy, source);
        Assert.Equal(2, Invoke(registry, state, ArrayDeque, "size", "()I", AndroidInvokeKind.Virtual, copy));
        Assert.Equal("a", Invoke(registry, state, ArrayDeque, "poll", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, copy));
        Assert.Equal("b", Invoke(registry, state, ArrayDeque, "poll", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, copy));
    }

    private static DexObject NewDeque(AndroidApiRegistry registry, AndroidFrameworkState state)
    {
        var deque = new DexObject(ArrayDeque);
        Invoke(registry, state, ArrayDeque, "<init>", "()V", AndroidInvokeKind.Direct, deque);
        return deque;
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
