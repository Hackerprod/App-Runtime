using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for java.util.concurrent.ConcurrentHashMap against the REAL OpenJDK
/// contract (verified from source, not memory): constructors incl.
/// IllegalArgumentException guards, the null-key/null-value NPE contract
/// (unlike HashMap — the easiest thing to get wrong), putIfAbsent not
/// overwriting an existing key, remove, and the shared view machinery.
/// </summary>
public sealed class ConcurrentHashMapTests
{
    private const string Chm = "Ljava/util/concurrent/ConcurrentHashMap;";

    [Fact]
    public void Constructors_accept_capacity_and_map_and_reject_invalid()
    {
        var (state, registry, _) = Session();
        var empty = new DexObject(Chm);
        Invoke(registry, state, Chm, "<init>", "()V", AndroidInvokeKind.Direct, empty);
        Assert.Equal(0, Invoke(registry, state, Chm, "size", "()I", AndroidInvokeKind.Virtual, empty));

        var sized = new DexObject(Chm);
        Invoke(registry, state, Chm, "<init>", "(I)V", AndroidInvokeKind.Direct, sized, 16);
        Assert.Equal(1, Invoke(registry, state, Chm, "isEmpty", "()Z", AndroidInvokeKind.Virtual, sized));

        var sizedFloat = new DexObject(Chm);
        Invoke(registry, state, Chm, "<init>", "(IF)V", AndroidInvokeKind.Direct, sizedFloat, 16, 0.75);
        var sizedAll = new DexObject(Chm);
        Invoke(registry, state, Chm, "<init>", "(IFI)V", AndroidInvokeKind.Direct, sizedAll, 16, 0.75, 4);

        var negative = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, Chm, "<init>", "(I)V", AndroidInvokeKind.Direct, new DexObject(Chm), -1));
        Assert.Equal("Ljava/lang/IllegalArgumentException;", negative.Throwable.TypeDescriptor);
        Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, Chm, "<init>", "(IFI)V", AndroidInvokeKind.Direct, new DexObject(Chm), 16, 0.75, 0));

        var source = new DexObject(Chm);
        Invoke(registry, state, Chm, "<init>", "()V", AndroidInvokeKind.Direct, source);
        Invoke(registry, state, Chm, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, source, "a", state.BoxedObject("Ljava/lang/Integer;", 1));
        var copy = new DexObject(Chm);
        Invoke(registry, state, Chm, "<init>", "(Ljava/util/Map;)V", AndroidInvokeKind.Direct, copy, source);
        Assert.Equal(1, Invoke(registry, state, Chm, "size", "()I", AndroidInvokeKind.Virtual, copy));
    }

    [Fact]
    public void Null_key_or_null_value_throws_null_pointer_exception()
    {
        var (state, registry, _) = Session();
        var map = new DexObject(Chm);
        Invoke(registry, state, Chm, "<init>", "()V", AndroidInvokeKind.Direct, map);

        var nullValue = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, Chm, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "k", null!));
        Assert.Equal("Ljava/lang/NullPointerException;", nullValue.Throwable.TypeDescriptor);
        var nullKey = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, Chm, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, null!, "v"));
        Assert.Equal("Ljava/lang/NullPointerException;", nullKey.Throwable.TypeDescriptor);
        Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, Chm, "putIfAbsent", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, null!, "v"));
        Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, Chm, "get", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, null!));
        Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, Chm, "remove", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, null!));
        Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, Chm, "containsValue", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, map, null!));
        // Nothing was inserted by the rejected puts.
        Assert.Equal(1, Invoke(registry, state, Chm, "isEmpty", "()Z", AndroidInvokeKind.Virtual, map));
    }

    [Fact]
    public void Put_if_absent_does_not_overwrite_an_existing_key()
    {
        var (state, registry, _) = Session();
        var map = new DexObject(Chm);
        Invoke(registry, state, Chm, "<init>", "()V", AndroidInvokeKind.Direct, map);
        var one = state.BoxedObject("Ljava/lang/Integer;", 1);
        var two = state.BoxedObject("Ljava/lang/Integer;", 2);
        Invoke(registry, state, Chm, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "k", one);
        // putIfAbsent on an existing key returns the EXISTING value, no overwrite.
        Assert.Same(one, Invoke(registry, state, Chm, "putIfAbsent", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "k", two));
        Assert.Same(one, Invoke(registry, state, Chm, "get", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "k"));
        // putIfAbsent on a NEW key inserts and returns null.
        Assert.Null(Invoke(registry, state, Chm, "putIfAbsent", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "new", two));
        Assert.Same(two, Invoke(registry, state, Chm, "get", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "new"));
    }

    [Fact]
    public void Remove_get_contains_and_views_follow_the_contract()
    {
        var (state, registry, _) = Session();
        var map = new DexObject(Chm);
        Invoke(registry, state, Chm, "<init>", "()V", AndroidInvokeKind.Direct, map);
        Invoke(registry, state, Chm, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "a", state.BoxedObject("Ljava/lang/Integer;", 1));
        Invoke(registry, state, Chm, "put", "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "b", state.BoxedObject("Ljava/lang/Integer;", 2));

        Assert.Equal(1, Invoke(registry, state, Chm, "containsKey", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, map, "a"));
        Assert.Equal(1, Invoke(registry, state, Chm, "containsValue", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, map, state.BoxedObject("Ljava/lang/Integer;", 2)));
        Assert.Equal(state.BoxedObject("Ljava/lang/Integer;", 1), Invoke(registry, state, Chm, "remove", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "a"));
        Assert.Null(Invoke(registry, state, Chm, "get", "(Ljava/lang/Object;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, map, "a"));

        // The shared Map-view machinery iterates the CHM peer (entrySet/keySet).
        var keys = (DexObject)Invoke(registry, state, Chm, "keySet", "()Ljava/util/Set;", AndroidInvokeKind.Virtual, map);
        Assert.Equal(1, Invoke(registry, state, "Ljava/util/Set;", "size", "()I", AndroidInvokeKind.Virtual, keys));

        Assert.Equal(1L, Invoke(registry, state, Chm, "mappingCount", "()J", AndroidInvokeKind.Virtual, map));
        Invoke(registry, state, Chm, "clear", "()V", AndroidInvokeKind.Virtual, map);
        Assert.Equal(1, Invoke(registry, state, Chm, "isEmpty", "()Z", AndroidInvokeKind.Virtual, map));
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
