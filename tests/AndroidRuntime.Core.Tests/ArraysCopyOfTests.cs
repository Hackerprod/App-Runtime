using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for java.util.Arrays.copyOf(T[], int) against the REAL Java SE 17
/// contract: truncation (newLength < original.length), padding
/// (newLength > original.length, extra slots null), exact-length copy
/// (independent array, not the same reference), and runtime-type preservation
/// mirroring the SOURCE array's descriptor (same principle as the
/// Collection.toArray fix).
/// </summary>
public sealed class ArraysCopyOfTests
{
    private const string Arrays = "Ljava/util/Arrays;";
    private const string Integer = "Ljava/lang/Integer;";

    [Fact]
    public void Copy_of_truncates_when_new_length_is_smaller()
    {
        var (state, registry, _) = Session();
        var source = new DexArray("[Ljava/lang/Object;", 4);
        source.Set(0, "a");
        source.Set(1, "b");
        source.Set(2, "c");
        source.Set(3, "d");

        var result = (DexArray)Invoke(registry, state, Arrays, "copyOf", "([Ljava/lang/Object;I)[Ljava/lang/Object;", AndroidInvokeKind.Static, source, 2);

        Assert.Equal("[Ljava/lang/Object;", result.ArrayDescriptor);
        Assert.Equal(2, result.Length);
        Assert.Equal("a", result.Get(0));
        Assert.Equal("b", result.Get(1));
        // Source unchanged.
        Assert.Equal(4, source.Length);
        Assert.Equal("c", source.Get(2));
    }

    [Fact]
    public void Copy_of_pads_with_null_when_new_length_is_larger()
    {
        var (state, registry, _) = Session();
        var source = new DexArray("[Ljava/lang/Object;", 2);
        source.Set(0, "x");
        source.Set(1, "y");

        var result = (DexArray)Invoke(registry, state, Arrays, "copyOf", "([Ljava/lang/Object;I)[Ljava/lang/Object;", AndroidInvokeKind.Static, source, 5);

        Assert.Equal(5, result.Length);
        Assert.Equal("x", result.Get(0));
        Assert.Equal("y", result.Get(1));
        Assert.Null(result.Get(2)); // padded with the reference default (null)
        Assert.Null(result.Get(3));
        Assert.Null(result.Get(4));
    }

    [Fact]
    public void Copy_of_exact_length_returns_an_independent_array()
    {
        var (state, registry, _) = Session();
        var source = new DexArray("[Ljava/lang/Object;", 2);
        source.Set(0, "a");
        source.Set(1, "b");

        var result = (DexArray)Invoke(registry, state, Arrays, "copyOf", "([Ljava/lang/Object;I)[Ljava/lang/Object;", AndroidInvokeKind.Static, source, 2);

        Assert.NotSame(source, result); // new array, never the same reference
        Assert.Equal(2, result.Length);
        Assert.Equal("a", result.Get(0));
        Assert.Equal("b", result.Get(1));
        // Mutating the result must not affect the source.
        result.Set(0, "changed");
        Assert.Equal("a", source.Get(0));
    }

    [Fact]
    public void Copy_of_preserves_the_source_runtime_descriptor()
    {
        // Real contract: copyOf(T[],int) mirrors original's runtime component
        // type (delegates to copyOf(original, newLength, original.getClass())) —
        // same principle as the Collection.toArray fix.
        var (state, registry, _) = Session();
        var source = new DexArray("[Ljava/lang/Integer;", 2);
        source.Set(0, state.BoxedObject(Integer, 10));
        source.Set(1, state.BoxedObject(Integer, 20));

        var result = (DexArray)Invoke(registry, state, Arrays, "copyOf", "([Ljava/lang/Object;I)[Ljava/lang/Object;", AndroidInvokeKind.Static, source, 4);

        Assert.Equal("[Ljava/lang/Integer;", result.ArrayDescriptor);
        Assert.Equal(4, result.Length);
        Assert.Equal(state.BoxedObject(Integer, 10), result.Get(0));
        Assert.Equal(state.BoxedObject(Integer, 20), result.Get(1));
        Assert.Null(result.Get(2)); // padded slots stay null
        Assert.Null(result.Get(3));
    }

    [Fact]
    public void Copy_of_zero_length_returns_an_empty_array_of_the_source_type()
    {
        var (state, registry, _) = Session();
        var source = new DexArray("[Ljava/lang/Object;", 3);
        source.Set(0, "a");

        var result = (DexArray)Invoke(registry, state, Arrays, "copyOf", "([Ljava/lang/Object;I)[Ljava/lang/Object;", AndroidInvokeKind.Static, source, 0);

        Assert.Equal(0, result.Length);
        Assert.Equal("[Ljava/lang/Object;", result.ArrayDescriptor);
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
