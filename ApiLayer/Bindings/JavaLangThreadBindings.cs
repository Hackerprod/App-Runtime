#nullable enable
using System.Diagnostics;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for java.lang.Thread under the real-concurrency GIL model. Guest
/// threads are REAL CLR threads; the GIL (see AndroidGil) serializes guest
/// bytecode execution, so sleep/join/monitor-enter must release it while
/// blocking and reacquire afterwards. The explicit, accepted tradeoff: no
/// parallel guest bytecode execution (compatibility over throughput).
/// </summary>
internal static class JavaLangThreadBindings
{
    [ThreadStatic]
    private static DexObject? _currentGuestThread;

    /// <summary>Seeds the calling real thread (the main guest thread, called on the
    /// execution lane) with its guest Thread object + peer, so currentThread()
    /// returns a stable identity for the main thread from the start.</summary>
    internal static void InitializeMainGuestThread(AndroidFrameworkState state)
    {
        if (_currentGuestThread is not null) return;
        var thread = new DexObject("Ljava/lang/Thread;");
        state.Threads.Add(thread, new ThreadPeer { Name = "main" });
        state.MainThreadObject = thread;
        _currentGuestThread = thread;
    }

    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        builder.Register(Api("Ljava/lang/Thread;", "<init>", "()V"), (_, args) => { state.Threads.Add(Receiver(args), new ThreadPeer()); return null!; });
        builder.Register(Api("Ljava/lang/Thread;", "<init>", "(Ljava/lang/Runnable;)V"), (_, args) => { state.Threads.Add(Receiver(args), new ThreadPeer { Runnable = RequireDex(args[1]) }); return null!; });
        builder.Register(Api("Ljava/lang/Thread;", "start", "()V"), (_, args) => StartThread(state, Receiver(args)));
        builder.Register(Api("Ljava/lang/Thread;", "run", "()V"), (_, args) => RunDefault(state, Receiver(args)));
        builder.Register(Api("Ljava/lang/Thread;", "join", "()V"), (_, args) => JoinCore(state, Receiver(args), timeoutMillis: -1));
        builder.Register(Api("Ljava/lang/Thread;", "join", "(J)V"), (_, args) => JoinCore(state, Receiver(args), RequireLong(args[1])));
        builder.Register(Api("Ljava/lang/Thread;", "interrupt", "()V"), (_, args) => { state.Threads.Get(Receiver(args)).Interrupt(); return null!; });
        builder.Register(Api("Ljava/lang/Thread;", "isInterrupted", "()Z"), (_, args) => state.Threads.Get(Receiver(args)).Interrupted ? 1 : 0);
        builder.Register(Api("Ljava/lang/Thread;", "isAlive", "()Z"), (_, args) => state.Threads.Get(Receiver(args)).ClrThread is { IsAlive: true } ? 1 : 0);
        builder.Register(Api("Ljava/lang/Thread;", "setName", "(Ljava/lang/String;)V"), (_, args) => { state.Threads.Get(Receiver(args)).Name = RequireString(args[1]); return null!; });
        builder.Register(Api("Ljava/lang/Thread;", "getName", "()Ljava/lang/String;"), (_, args) => state.Threads.Get(Receiver(args)).Name ?? "Thread-" + state.Threads.Get(Receiver(args)).GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.Register(Api("Ljava/lang/Thread;", "currentThread", "()Ljava/lang/Thread;"), (_, _) => CurrentThreadObject(state));
        builder.Register(Api("Ljava/lang/Thread;", "sleep", "(J)V"), (_, args) => SleepCore(state, RequireLong(args[0])));
    }

    private static object StartThread(AndroidFrameworkState state, DexObject receiver)
    {
        var peer = state.Threads.Get(receiver);
        if (peer.ClrThread is not null)
            throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/IllegalThreadStateException;", "thread already started"));
        var interpreter = state.Interpreter ?? throw new InvalidOperationException("Thread.start requires an attached interpreter.");
        var gil = state.Gil;
        peer.ClrThread = new Thread(() =>
        {
            _currentGuestThread = receiver;
            try
            {
                gil.Enter();
                try
                {
                    interpreter.RunGuestThreadBody(receiver, peer.Runnable);
                }
                finally
                {
                    gil.Exit();
                }
            }
            catch (Exception error)
            {
                // The guest thread's uncaught exception terminates the thread (real
                // Java semantics; InterruptedException from an interrupt is a normal
                // outcome). It is recorded on the peer rather than crashing the host
                // or silently swallowed — a future uncaughtExceptionHandler binding
                // can surface it.
                peer.TerminalException = error;
            }
            finally
            {
                peer.Completion.Set();
            }
        })
        {
            IsBackground = true,
            Name = peer.Name ?? "AndroidRuntime-GuestThread-" + receiver.GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        peer.ClrThread.Start();
        return null!;
    }

    private static object RunDefault(AndroidFrameworkState state, DexObject receiver)
    {
        // Real Thread.run() default: dispatch to the Runnable target if present, else no-op.
        var peer = state.Threads.Get(receiver);
        if (peer.Runnable is not null && state.Interpreter is not null)
            state.Interpreter.InvokeInstanceExact(peer.Runnable, "run", "()V");
        return null!;
    }

    private static object JoinCore(AndroidFrameworkState state, DexObject target, long timeoutMillis)
    {
        var currentPeer = CurrentThreadPeer(state);
        var targetPeer = state.Threads.Get(target);
        var gil = state.Gil;
        using (gil.BeginBlocking())
        {
            if (timeoutMillis < 0)
            {
                targetPeer.Completion.Wait();
            }
            else if (timeoutMillis > 0)
            {
                var sw = Stopwatch.StartNew();
                while (!targetPeer.Completion.Wait(50))
                {
                    if (currentPeer.Interrupted) break;
                    if (sw.ElapsedMilliseconds >= timeoutMillis) break;
                }
            }
            // timeoutMillis == 0: non-blocking probe, do nothing.
        }
        if (currentPeer.Interrupted)
        {
            currentPeer.Interrupted = false;
            throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/InterruptedException;"));
        }
        return null!;
    }

    private static object SleepCore(AndroidFrameworkState state, long millis)
    {
        if (millis < 0)
            throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/IllegalArgumentException;", "negative sleep"));
        var peer = CurrentThreadPeer(state);
        var gil = state.Gil;
        using (gil.BeginBlocking())
        {
            if (millis > 0)
                peer.InterruptSignal.Wait(TimeSpan.FromMilliseconds(Math.Min(millis, int.MaxValue)));
        }
        if (peer.Interrupted)
        {
            peer.Interrupted = false;
            throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/InterruptedException;"));
        }
        return null!;
    }

    private static DexObject CurrentThreadObject(AndroidFrameworkState state)
    {
        if (_currentGuestThread is not null)
        {
            try
            {
                state.Threads.Get(_currentGuestThread);
                return _currentGuestThread;
            }
            catch (InvalidOperationException)
            {
                // Stale association from a reused thread that ran guest code under a
                // different session (or a prior test): re-associate fresh.
            }
        }
        // Lazily associate the calling real thread with a fresh guest Thread peer
        // (e.g. the main guest thread before AndroidAppRuntime seeds it, or a test
        // thread calling currentThread() directly).
        var thread = new DexObject("Ljava/lang/Thread;");
        state.Threads.Add(thread, new ThreadPeer());
        _currentGuestThread = thread;
        return thread;
    }

    private static ThreadPeer CurrentThreadPeer(AndroidFrameworkState state) => state.Threads.Get(CurrentThreadObject(state));

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    private static DexObject Receiver(object[] args) => (DexObject)args[0]!;
    private static string RequireString(object? value) => value as string ?? throw new ArgumentException("Expected a string.");
    private static long RequireLong(object? value) => value is long l ? l : value is int i ? i : throw new ArgumentException("Expected a long.");
    private static DexObject RequireDex(object? value) => value as DexObject ?? throw new ArgumentException("Expected a guest object.");
}
