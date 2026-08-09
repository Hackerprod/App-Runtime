#nullable enable
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>Bindings for java.util.concurrent.atomic framework types. AtomicInteger
/// lands in the same real JDK package as AtomicReference, so both live here
/// ("migrate as touched").</summary>
internal static class JavaUtilConcurrentAtomicBindings
{
    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        RegisterAtomicReference(builder, state);
        RegisterAtomicInteger(builder, state);
    }

    private static void RegisterAtomicReference(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        // No real atomicity/synchronization: this runtime is one serial execution
        // lane with no concurrent guest threads, so a plain compare-and-swap-shaped
        // check with no Interlocked/lock is behaviorally indistinguishable from a
        // real atomic one here (same reasoning as monitor-enter/exit, README
        // boundary #17). compareAndSet uses guest reference identity (ReferenceEquals,
        // with null normalized to null by NormalizeApiArguments) — matching how the
        // interpreter's if-eq/if-ne compare reference values, not value equality.
        builder.Register(Api("Ljava/util/concurrent/atomic/AtomicReference;", "<init>", "()V"), (_, args) => { state.AtomicReferences.Add(Receiver(args), new AtomicReferencePeer()); return null!; });
        builder.Register(Api("Ljava/util/concurrent/atomic/AtomicReference;", "<init>", "(Ljava/lang/Object;)V"), (_, args) => { state.AtomicReferences.Add(Receiver(args), new AtomicReferencePeer { Value = args[1] }); return null!; });
        builder.Register(Api("Ljava/util/concurrent/atomic/AtomicReference;", "get", "()Ljava/lang/Object;"), (_, args) => state.AtomicReferences.Get(Receiver(args)).Value!);
        builder.Register(Api("Ljava/util/concurrent/atomic/AtomicReference;", "set", "(Ljava/lang/Object;)V"), (_, args) => { state.AtomicReferences.Get(Receiver(args)).Value = args[1]; return null!; });
        builder.Register(Api("Ljava/util/concurrent/atomic/AtomicReference;", "compareAndSet", "(Ljava/lang/Object;Ljava/lang/Object;)Z"), (_, args) =>
        {
            var peer = state.AtomicReferences.Get(Receiver(args));
            if (!ReferenceEquals(peer.Value, args[1])) return 0;
            peer.Value = args[2];
            return 1;
        });
    }

    private static void RegisterAtomicInteger(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        // Complete real java.util.concurrent.atomic.AtomicInteger contract. No real
        // synchronization — same single-serial-lane reasoning as AtomicReference
        // above (README boundaries #17/#26): a plain read/modify/write is
        // behaviorally indistinguishable from an atomic one here.
        builder.Register(Api("Ljava/util/concurrent/atomic/AtomicInteger;", "<init>", "()V"), (_, args) => { state.AtomicIntegers.Add(Receiver(args), new AtomicIntegerPeer()); return null!; });
        builder.Register(Api("Ljava/util/concurrent/atomic/AtomicInteger;", "<init>", "(I)V"), (_, args) => { state.AtomicIntegers.Add(Receiver(args), new AtomicIntegerPeer { Value = RequireInt(args[1]) }); return null!; });
        builder.Register(Api("Ljava/util/concurrent/atomic/AtomicInteger;", "get", "()I"), (_, args) => state.AtomicIntegers.Get(Receiver(args)).Value);
        builder.Register(Api("Ljava/util/concurrent/atomic/AtomicInteger;", "set", "(I)V"), (_, args) => { state.AtomicIntegers.Get(Receiver(args)).Value = RequireInt(args[1]); return null!; });
        builder.Register(Api("Ljava/util/concurrent/atomic/AtomicInteger;", "getAndIncrement", "()I"), (_, args) => { var peer = state.AtomicIntegers.Get(Receiver(args)); int old = peer.Value; peer.Value = old + 1; return old; });
        builder.Register(Api("Ljava/util/concurrent/atomic/AtomicInteger;", "incrementAndGet", "()I"), (_, args) => { var peer = state.AtomicIntegers.Get(Receiver(args)); peer.Value += 1; return peer.Value; });
        builder.Register(Api("Ljava/util/concurrent/atomic/AtomicInteger;", "getAndDecrement", "()I"), (_, args) => { var peer = state.AtomicIntegers.Get(Receiver(args)); int old = peer.Value; peer.Value = old - 1; return old; });
        builder.Register(Api("Ljava/util/concurrent/atomic/AtomicInteger;", "decrementAndGet", "()I"), (_, args) => { var peer = state.AtomicIntegers.Get(Receiver(args)); peer.Value -= 1; return peer.Value; });
        builder.Register(Api("Ljava/util/concurrent/atomic/AtomicInteger;", "getAndAdd", "(I)I"), (_, args) => { var peer = state.AtomicIntegers.Get(Receiver(args)); int old = peer.Value; peer.Value = old + RequireInt(args[1]); return old; });
        builder.Register(Api("Ljava/util/concurrent/atomic/AtomicInteger;", "addAndGet", "(I)I"), (_, args) => { var peer = state.AtomicIntegers.Get(Receiver(args)); peer.Value += RequireInt(args[1]); return peer.Value; });
        builder.Register(Api("Ljava/util/concurrent/atomic/AtomicInteger;", "compareAndSet", "(II)Z"), (_, args) =>
        {
            var peer = state.AtomicIntegers.Get(Receiver(args));
            int expected = RequireInt(args[1]);
            int update = RequireInt(args[2]);
            if (peer.Value != expected) return 0;
            peer.Value = update;
            return 1;
        });
    }

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    private static DexObject Receiver(object[] args) => (DexObject)args[0]!;
    private static int RequireInt(object? value) => value is int i ? i : throw new ArgumentException("Expected an int.");
}
