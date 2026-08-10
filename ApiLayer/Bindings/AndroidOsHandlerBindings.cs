#nullable enable
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for android.os.Handler / Looper under the real-concurrency model. The
/// MAIN Looper is structurally the execution lane itself: Handler.post targeting
/// the main Looper enqueues onto the lane's EXISTING queue (AndroidFrameworkState
/// routes it there when a lane exists; standalone sessions drain a private queue
/// on a background pump thread). A background Looper (prepare()+loop()) owns a
/// private AndroidMessageQueue drained by the calling guest thread via RunPump,
/// which releases the GIL while waiting for work. Only the Runnable post family is
/// bound — no Message/sendMessage surface (out of scope, README boundary #38).
/// </summary>
internal static class AndroidOsHandlerBindings
{
    private const string HandlerClass = "Landroid/os/Handler;";
    private const string LooperClass = "Landroid/os/Looper;";

    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        // ---- Looper (all static except quit) ----
        builder.Register(Api(LooperClass, "getMainLooper", "()Landroid/os/Looper;"), (_, _) => state.EnsureMainLooper());
        builder.Register(Api(LooperClass, "myLooper", "()Landroid/os/Looper;"), (_, _) => MyLooper(state) ?? null!);
        builder.Register(Api(LooperClass, "prepare", "()V"), (_, _) => { Prepare(state); return null!; });
        builder.Register(Api(LooperClass, "loop", "()V"), (_, _) => { Loop(state); return null!; });
        builder.Register(Api(LooperClass, "quit", "()V"), (_, args) => { state.Loopers.Get(Receiver(args)).Quit(); return null!; });
        builder.Register(Api(LooperClass, "quitSafely", "()V"), (_, args) => { state.Loopers.Get(Receiver(args)).Quit(); return null!; });
        builder.Register(Api(LooperClass, "getThread", "()Ljava/lang/Thread;"), (_, args) => state.Loopers.Get(Receiver(args)).ThreadObject ?? null!);

        // ---- Handler ----
        builder.Register(Api(HandlerClass, "<init>", "()V"), (_, args) =>
        {
            var looper = MyLooper(state)
                ?? throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/RuntimeException;", "Can't create handler inside thread that has not called Looper.prepare()"));
            state.Handlers.Add(Receiver(args), new HandlerPeer { Looper = state.Loopers.Get(looper), LooperObject = looper });
            return null!;
        });
        builder.Register(Api(HandlerClass, "<init>", "(Landroid/os/Looper;)V"), (_, args) =>
        {
            state.Handlers.Add(Receiver(args), new HandlerPeer { Looper = state.Loopers.Get(RequireDex(args[1])), LooperObject = RequireDex(args[1]) });
            return null!;
        });
        builder.Register(Api(HandlerClass, "getLooper", "()Landroid/os/Looper;"), (_, args) => state.Handlers.Get(Receiver(args)).LooperObject ?? null!);
        builder.Register(Api(HandlerClass, "post", "(Ljava/lang/Runnable;)Z"), (_, args) => Post(state, state.Handlers.Get(Receiver(args)), RequireDex(args[1]), delayMillis: 0) ? 1 : 0);
        builder.Register(Api(HandlerClass, "postDelayed", "(Ljava/lang/Runnable;J)Z"), (_, args) => Post(state, state.Handlers.Get(Receiver(args)), RequireDex(args[1]), RequireLong(args[2])) ? 1 : 0);
        builder.Register(Api(HandlerClass, "removeCallbacks", "(Ljava/lang/Runnable;)V"), (_, args) => { RemoveCallbacks(state.Handlers.Get(Receiver(args)), RequireDex(args[1])); return null!; });
        // removeCallbacksAndMessages(Object token): the standard teardown call
        // (e.g. onDestroy). This runtime tracks Runnable callbacks only (no
        // Message objects), so null AND non-null tokens both clear every tracked
        // callback/timer for the handler — the token is accepted but not matched,
        // which is honest for a model with no per-token messages. Found via the
        // owner's RuntimeApiLab APK: without it, clean shutdown crashed on an
        // unimplemented boundary.
        builder.Register(Api(HandlerClass, "removeCallbacksAndMessages", "(Ljava/lang/Object;)V"), (_, args) => { RemoveAllCallbacks(state.Handlers.Get(Receiver(args))); return null!; });
        builder.Register(Api(HandlerClass, "hasCallbacks", "(Ljava/lang/Runnable;)Z"), (_, args) => { lock (state.Handlers.Get(Receiver(args)).Gate) return state.Handlers.Get(Receiver(args)).Pending.Contains(RequireDex(args[1])) ? 1 : 0; });
        builder.Register(Api(HandlerClass, "postAtFrontOfQueue", "(Ljava/lang/Runnable;)Z"), (_, args) => Post(state, state.Handlers.Get(Receiver(args)), RequireDex(args[1]), delayMillis: 0) ? 1 : 0);
    }

    // ---------------------------------------------------------------------------
    // Looper semantics
    // ---------------------------------------------------------------------------

    private static DexObject? MyLooper(AndroidFrameworkState state)
    {
        // The main lane thread is the main guest thread: it owns the main Looper.
        if (state.Lane?.IsCurrentThread == true)
            return state.EnsureMainLooper();
        return state.ThreadLoopers.TryGetValue(Environment.CurrentManagedThreadId, out var looper) ? looper : null;
    }

    private static void Prepare(AndroidFrameworkState state)
    {
        int threadId = Environment.CurrentManagedThreadId;
        if (state.ThreadLoopers.ContainsKey(threadId))
            throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/RuntimeException;", "Only one Looper may be created per thread"));
        var looper = new DexObject(LooperClass);
        // Bind the calling thread's guest Thread to this Looper (real Looper.getThread()).
        var guestThread = state.Interpreter?.InvokeFrameworkExact("Ljava/lang/Thread;", "currentThread", "()Ljava/lang/Thread;", AndroidInvokeKind.Static) as DexObject;
        state.Loopers.Add(looper, new LooperPeer { IsMain = false, Queue = new AndroidMessageQueue(), ThreadObject = guestThread });
        state.ThreadLoopers[threadId] = looper;
    }

    private static void Loop(AndroidFrameworkState state)
    {
        int threadId = Environment.CurrentManagedThreadId;
        if (!state.ThreadLoopers.TryGetValue(threadId, out var looperObject))
            throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/RuntimeException;", "No Looper; Looper.prepare() wasn't called on this thread."));
        RunPump(state, state.Loopers.Get(looperObject));
    }

    /// <summary>
    /// The shared queue-pump: release the GIL while waiting for work, reacquire to
    /// run each posted action, stop once quit is requested. Used by background
    /// Looper.loop() on the calling thread and by the standalone main pump thread.
    /// </summary>
    internal static void RunPump(AndroidFrameworkState state, LooperPeer peer)
    {
        var gil = state.Gil;
        while (true)
        {
            Action? action;
            using (gil.BeginBlocking())
            {
                if (peer.QuitRequested && peer.Queue is { IsEmpty: true }) break;
                if (peer.Queue is null || !peer.Queue.TryTake(out action, 100))
                {
                    if (peer.QuitRequested) break;
                    continue;
                }
            }
            if (action is not null)
            {
                try { action(); }
                catch (Exception error) { peer.TerminalException = error; }
            }
        }
    }

    // ---------------------------------------------------------------------------
    // Handler semantics
    // ---------------------------------------------------------------------------

    private static bool Post(AndroidFrameworkState state, HandlerPeer handler, DexObject runnable, long delayMillis)
    {
        if (delayMillis < 0)
            throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/IllegalArgumentException;", "delay < 0"));
        lock (handler.Gate) handler.Pending.Add(runnable);
        var wrapper = new Action(() =>
        {
            // Skip if removeCallbacks already removed this runnable (real
            // semantics: a removed callback never runs again).
            lock (handler.Gate)
            {
                if (!handler.Pending.Remove(runnable)) return;
            }
            try
            {
                state.Interpreter?.InvokeInstanceExact(runnable, "run", "()V");
            }
            catch (Exception error)
            {
                handler.LastException = error;
            }
        });
        if (delayMillis == 0)
            return Dispatch(state, handler.Looper, wrapper);
        var cts = new CancellationTokenSource();
        lock (handler.Gate) handler.Timers[runnable] = cts;
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(delayMillis, int.MaxValue)), cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            Dispatch(state, handler.Looper, wrapper);
        });
        return true;
    }

    private static bool Dispatch(AndroidFrameworkState state, LooperPeer looper, Action wrapper)
    {
        if (looper.IsMain)
        {
            // Reuse the execution lane's existing queue when hosted — the lane IS
            // the main message loop; never build a second pump for the main thread.
            if (state.Lane is not null) return state.Lane.TryPost(wrapper);
        }
        return looper.Queue?.Post(wrapper) ?? false;
    }

    /// <summary>Runs a guest Runnable on the main Looper (Activity.runOnUiThread
    /// when called from a background guest thread). No callback tracking: the
    /// runnable runs exactly once, later.</summary>
    internal static bool PostPublic(AndroidFrameworkState state, DexObject runnable)
    {
        var looper = state.MainLooperPeer
            ?? state.Loopers.Get(state.EnsureMainLooper());
        var wrapper = new Action(() =>
        {
            try { state.Interpreter?.InvokeInstanceExact(runnable, "run", "()V"); }
            catch (Exception error) { looper.TerminalException = error; }
        });
        return Dispatch(state, looper, wrapper);
    }

    private static void RemoveCallbacks(HandlerPeer handler, DexObject runnable)
    {
        lock (handler.Gate)
        {
            handler.Pending.Remove(runnable);
            if (handler.Timers.Remove(runnable, out var cts)) cts.Cancel();
        }
    }

    private static void RemoveAllCallbacks(HandlerPeer handler)
    {
        lock (handler.Gate)
        {
            handler.Pending.Clear();
            foreach (CancellationTokenSource cts in handler.Timers.Values)
                cts.Cancel();
            handler.Timers.Clear();
        }
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    private static DexObject Receiver(object[] args) => (DexObject)args[0]!;
    private static long RequireLong(object? value) => value is long l ? l : value is int i ? i : throw new ArgumentException("Expected a long.");
    private static DexObject RequireDex(object? value) => value as DexObject ?? throw new ArgumentException("Expected a guest object.");
}
