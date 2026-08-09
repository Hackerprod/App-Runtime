#nullable enable
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for kotlin.Lazy — the `by lazy { }` property-delegation idiom.
/// Probe-confirmed shape: only `LazyKt.lazy(Function0)`, `Lazy.getValue()`, and
/// `Lazy.isInitialized()` are referenced — NO explicit LazyThreadSafetyMode
/// overload and NO `getValue(Lazy,Object,KProperty)` delegate-operator extension
/// (the real call sites call Lazy.getValue() directly), so only those are built.
/// getValue() reuses the established guest-functional-interface invocation
/// pattern (state.Interpreter.InvokeInstanceExact on the Function0's
/// "invoke()Ljava/lang/Object;" — the same one-line shape Thread uses for
/// Runnable.run()). No real thread-safety mechanism is built: the GIL serializes
/// all guest bytecode execution, so a plain "compute once and cache" is correct
/// under all three real LazyThreadSafetyMode values — no observable difference
/// exists under this execution model (same reasoning as monitor-enter/
/// AtomicReference).
/// </summary>
internal static class KotlinLazyBindings
{
    private const string LazyClass = "Lkotlin/Lazy;";
    private const string LazyKt = "Lkotlin/LazyKt;";

    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        builder.Register(Api(LazyKt, "lazy", "(Lkotlin/jvm/functions/Function0;)Lkotlin/Lazy;"), (_, args) =>
        {
            var lazy = new DexObject(LazyClass);
            state.Lazies.Add(lazy, new LazyPeer { Function0 = RequireDex(args[0]) });
            return lazy;
        });
        builder.Register(Api(LazyClass, "getValue", "()Ljava/lang/Object;"), (_, args) =>
        {
            var peer = state.Lazies.Get(Receiver(args));
            if (!peer.Computed)
            {
                var interpreter = state.Interpreter ?? throw new InvalidOperationException("Lazy.getValue requires an attached interpreter.");
                peer.CachedValue = interpreter.InvokeInstanceExact(peer.Function0, "invoke", "()Ljava/lang/Object;");
                peer.Computed = true;
            }
            return peer.CachedValue ?? null!;
        });
        builder.Register(Api(LazyClass, "isInitialized", "()Z"), (_, args) => state.Lazies.Get(Receiver(args)).Computed ? 1 : 0);
    }

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    private static DexObject Receiver(object[] args) => (DexObject)args[0]!;
    private static DexObject RequireDex(object? value) => value as DexObject ?? throw new ArgumentException("Expected a guest object.");
}
