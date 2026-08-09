using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for java.lang.Runtime — the SCOPED safe-informational surface on the
/// SKYNET launch path (probe: only getRuntime + availableProcessors are
/// executed). getRuntime() is a canonical singleton (Assert.Same across
/// calls); the informational methods return plausible real values. The
/// security-sensitive exec/exit/halt are NOT built (probe shows them not even
/// referenced) — their absence is the deliberate stance, not an omission.
/// </summary>
public sealed class RuntimeTests
{
    private const string Runtime = "Ljava/lang/Runtime;";

    [Fact]
    public void Get_runtime_returns_the_same_singleton_across_calls()
    {
        var (state, registry, _) = Session();
        var first = (DexObject)Invoke(registry, state, Runtime, "getRuntime", "()Ljava/lang/Runtime;", AndroidInvokeKind.Static);
        var second = (DexObject)Invoke(registry, state, Runtime, "getRuntime", "()Ljava/lang/Runtime;", AndroidInvokeKind.Static);
        // Real contract: one runtime object per process.
        Assert.Same(first, second);
        Assert.Same(state.RuntimeObject, first);
        Assert.Equal("Ljava/lang/Runtime;", first.TypeDescriptor);
    }

    [Fact]
    public void Available_processors_returns_a_positive_plausible_count()
    {
        var (state, registry, _) = Session();
        var runtime = (DexObject)Invoke(registry, state, Runtime, "getRuntime", "()Ljava/lang/Runtime;", AndroidInvokeKind.Static);
        int processors = (int)Invoke(registry, state, Runtime, "availableProcessors", "()I", AndroidInvokeKind.Virtual, runtime)!;
        Assert.True(processors >= 1);
        Assert.Equal(Environment.ProcessorCount, processors);
    }

    [Fact]
    public void Memory_methods_return_plausible_non_negative_values()
    {
        var (state, registry, _) = Session();
        var runtime = (DexObject)Invoke(registry, state, Runtime, "getRuntime", "()Ljava/lang/Runtime;", AndroidInvokeKind.Static);
        long total = (long)Invoke(registry, state, Runtime, "totalMemory", "()J", AndroidInvokeKind.Virtual, runtime)!;
        long free = (long)Invoke(registry, state, Runtime, "freeMemory", "()J", AndroidInvokeKind.Virtual, runtime)!;
        long max = (long)Invoke(registry, state, Runtime, "maxMemory", "()J", AndroidInvokeKind.Virtual, runtime)!;
        Assert.True(total > 0);
        Assert.True(free >= 0);
        Assert.True(max > 0);
        Assert.True(free <= max);
    }

    [Fact]
    public void Gc_is_a_suggestion_and_returns_void()
    {
        var (state, registry, _) = Session();
        var runtime = (DexObject)Invoke(registry, state, Runtime, "getRuntime", "()Ljava/lang/Runtime;", AndroidInvokeKind.Static);
        // Real contract: a suggestion, no guarantee of any reclamation — must not throw.
        var result = Invoke(registry, state, Runtime, "gc", "()V", AndroidInvokeKind.Virtual, runtime);
        Assert.Null(result);
    }

    [Fact]
    public void Exec_and_exit_are_deliberately_not_built()
    {
        // SECURITY STANCE: exec() must NEVER be real process execution (sandbox
        // escape) and exit()/halt() must NEVER terminate the host. The probe
        // shows they are not even referenced on the launch path, so they are NOT
        // bound — this test pins that deliberate absence (a future brief that
        // reaches them must decide fail-closed semantics explicitly).
        var (state, registry, _) = Session();
        var runtime = (DexObject)Invoke(registry, state, Runtime, "getRuntime", "()Ljava/lang/Runtime;", AndroidInvokeKind.Static);
        Assert.False(registry.Contains(new AndroidApiMethodId(Runtime, "exec", "(Ljava/lang/String;)Ljava/lang/Process;")));
        Assert.False(registry.Contains(new AndroidApiMethodId(Runtime, "exit", "(I)V")));
        Assert.False(registry.Contains(new AndroidApiMethodId(Runtime, "halt", "(I)V")));
        Assert.False(registry.Contains(new AndroidApiMethodId(Runtime, "addShutdownHook", "(Ljava/lang/Thread;)V")));
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
