#nullable enable
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for java.util.concurrent Executors/ExecutorService/Future/FutureTask/
/// TimeUnit under the real-concurrency GIL model. Pools are REAL thread pools: N
/// real background worker threads (the same mechanism Thread.start uses) pulling
/// guest Runnable/Callable tasks off a shared queue. Nothing is simulated by
/// running tasks inline. TimeUnit is a JDK enum modeled as stable framework
/// singletons OUTSIDE the guest Enum machinery (the constants live in
/// AndroidFrameworkState and resolve via the interpreter's framework static-field
/// hook for sget reads).
/// </summary>
internal static class JavaUtilConcurrentExecutorBindings
{
    private const string ExecutorsClass = "Ljava/util/concurrent/Executors;";
    private const string ThreadPoolExecutorClass = "Ljava/util/concurrent/ThreadPoolExecutor;";
    private const string ExecutorServiceClass = "Ljava/util/concurrent/ExecutorService;";
    private const string ExecutorClass = "Ljava/util/concurrent/Executor;";
    private const string FutureClass = "Ljava/util/concurrent/Future;";
    private const string FutureTaskClass = "Ljava/util/concurrent/FutureTask;";
    private const string ThreadFactoryClass = "Ljava/util/concurrent/ThreadFactory;";
    private const string TimeUnitClass = "Ljava/util/concurrent/TimeUnit;";
    private const string WorkerRunnableClass = "LandroidRuntime/WorkerRunnable;";

    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        // ---- Executors factories (all static) ----
        builder.Register(Api(ExecutorsClass, "newFixedThreadPool", "(I)Ljava/util/concurrent/ExecutorService;"), (_, args) => CreatePool(state, RequireInt(args[0]), null, fixedPool: true));
        builder.Register(Api(ExecutorsClass, "newFixedThreadPool", "(ILjava/util/concurrent/ThreadFactory;)Ljava/util/concurrent/ExecutorService;"), (_, args) => CreatePool(state, RequireInt(args[0]), RequireDex(args[1]), fixedPool: true));
        builder.Register(Api(ExecutorsClass, "newSingleThreadExecutor", "()Ljava/util/concurrent/ExecutorService;"), (_, _) => CreatePool(state, 1, null, fixedPool: true));
        builder.Register(Api(ExecutorsClass, "newCachedThreadPool", "()Ljava/util/concurrent/ExecutorService;"), (_, _) => CreatePool(state, ExecutorServicePeer.CachedPoolMaxWorkers, null, fixedPool: false));
        builder.Register(Api(ExecutorsClass, "defaultThreadFactory", "()Ljava/util/concurrent/ThreadFactory;"), (_, _) => DefaultFactory(state));

        // ---- ThreadFactory.newThread (single method guest interface) ----
        builder.Register(Api(ThreadFactoryClass, "newThread", "(Ljava/lang/Runnable;)Ljava/lang/Thread;"), (_, args) =>
        {
            // The guest factory's own newThread runs through InvokeFrameworkExact when
            // a concrete factory is provided; this binding is the framework default.
            var thread = new DexObject("Ljava/lang/Thread;");
            state.Threads.Add(thread, new ThreadPeer { Runnable = RequireDex(args[1]) });
            return thread;
        });
        builder.Register(Api("Ljava/util/concurrent/Executors$DefaultThreadFactory;", "newThread", "(Ljava/lang/Runnable;)Ljava/lang/Thread;"), (_, args) =>
        {
            var thread = new DexObject("Ljava/lang/Thread;");
            state.Threads.Add(thread, new ThreadPeer { Runnable = RequireDex(args[1]) });
            return thread;
        });

        // ---- Executor/ExecutorService (register the declared ids; invoke-virtual
        // resolves through the declared ref, which is the interface id in DEX) ----
        RegisterPoolMethods(builder, state, ThreadPoolExecutorClass);
        RegisterPoolMethods(builder, state, ExecutorServiceClass);
        builder.Register(Api(ExecutorClass, "execute", "(Ljava/lang/Runnable;)V"), (_, args) => { Enqueue(state, state.ExecutorServices.Get(Receiver(args)), new FuturePeer { Runnable = RequireDex(args[1]) }); return null!; });

        // ---- Future / FutureTask ----
        RegisterFutureMethods(builder, state, FutureClass);
        RegisterFutureMethods(builder, state, FutureTaskClass);
        builder.Register(Api(FutureTaskClass, "<init>", "(Ljava/util/concurrent/Callable;)V"), (_, args) =>
        {
            state.Futures.Add(Receiver(args), new FuturePeer { Callable = RequireDex(args[1]) });
            return null!;
        });
        builder.Register(Api(FutureTaskClass, "run", "()V"), (_, args) => { ExecuteTask(state, state.Futures.Get(Receiver(args))); return null!; });

        // ---- TimeUnit (instance methods on the singleton constants; values() static) ----
        RegisterTimeUnit(builder, state);

        // ---- Worker loop entry: the synthetic Runnable body real pool workers run.
        // RunGuestThreadBody falls through to this binding when the worker thread
        // starts; the loop itself releases the GIL while waiting for tasks. ----
        builder.Register(Api(WorkerRunnableClass, "run", "()V"), (_, args) =>
        {
            var pool = GetPoolForWorker(state, Receiver(args));
            var gil = state.Gil;
            var interpreter = state.Interpreter ?? throw new InvalidOperationException("Pool worker requires an attached interpreter.");
            RunWorkerLoop(state, pool, gil, interpreter);
            return null!;
        });
    }

    // ---------------------------------------------------------------------------
    // Pool lifecycle
    // ---------------------------------------------------------------------------

    private static object CreatePool(AndroidFrameworkState state, int poolSize, DexObject? threadFactory, bool fixedPool)
    {
        if (poolSize <= 0)
            throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/IllegalArgumentException;", "pool size must be positive"));
        int max = fixedPool ? poolSize : ExecutorServicePeer.CachedPoolMaxWorkers;
        if (max > state.PeerLimits.Threads)
            throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/IllegalArgumentException;", "pool size exceeds the session thread quota"));
        var poolObject = new DexObject(ThreadPoolExecutorClass);
        var peer = new ExecutorServicePeer
        {
            MaxWorkers = max,
            ThreadFactory = threadFactory,
            IdleKeepaliveMs = fixedPool ? -1 : ExecutorServicePeer.CachedPoolKeepaliveMs
        };
        state.ExecutorServices.Add(poolObject, peer);
        if (fixedPool)
        {
            for (int index = 0; index < max; index++)
                EnsureWorker(state, peer, force: true);
        }
        return poolObject;
    }

    private static object DefaultFactory(AndroidFrameworkState state)
    {
        if (state.DefaultThreadFactory is not null) return state.DefaultThreadFactory;
        var factory = new DexObject("Ljava/util/concurrent/Executors$DefaultThreadFactory;");
        state.DefaultThreadFactory = factory;
        return factory;
    }

    /// <summary>Spawns one real worker thread. With a guest ThreadFactory, the
    /// worker is created through factory.newThread(workerRunnable) and started
    /// through the existing Thread.start machinery (same real-thread mechanism);
    /// without one, a raw CLR background thread runs the worker loop directly.</summary>
    private static void EnsureWorker(AndroidFrameworkState state, ExecutorServicePeer pool, bool force)
    {
        lock (pool.WorkerGate)
        {
            if (pool.IsShutdown) return;
            if (!force && Volatile.Read(ref pool.ActiveWorkers) >= pool.MaxWorkers) return;
            var gil = state.Gil;
            var interpreter = state.Interpreter ?? throw new InvalidOperationException("Pool creation requires an attached interpreter.");

            if (pool.ThreadFactory is not null)
            {
                var workerRunnable = new DexObject(WorkerRunnableClass);
                lock (state.WorkerRunnablesGate) state.WorkerRunnables[workerRunnable] = pool;
                DexObject guestThread;
                try
                {
                    // A guest factory (DEX-defined newThread) dispatches to its own
                    // implementation; the framework default falls back to the binding.
                    guestThread = (DexObject)interpreter.InvokeVirtualInstanceExact(pool.ThreadFactory, "newThread", "(Ljava/lang/Runnable;)Ljava/lang/Thread;", workerRunnable);
                }
                catch (MissingMethodException)
                {
                    guestThread = (DexObject)interpreter.InvokeFrameworkExact(ThreadFactoryClass, "newThread", "(Ljava/lang/Runnable;)Ljava/lang/Thread;", AndroidInvokeKind.Virtual, pool.ThreadFactory, workerRunnable);
                }
                Interlocked.Increment(ref pool.ActiveWorkers);
                interpreter.InvokeFrameworkExact("Ljava/lang/Thread;", "start", "()V", AndroidInvokeKind.Virtual, guestThread);
            }
            else
            {
                var thread = new Thread(() =>
                {
                    try
                    {
                        gil.Enter();
                        try { RunWorkerLoop(state, pool, gil, interpreter); }
                        finally { gil.Exit(); }
                    }
                    catch (Exception error)
                    {
                        pool.TerminalException = error;
                    }
                })
                {
                    IsBackground = true,
                    Name = "AndroidRuntime-Pool-" + pool.GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture)
                };
                Interlocked.Increment(ref pool.ActiveWorkers);
                thread.Start();
            }
        }
    }

    /// <summary>Worker body shared by direct and factory workers: release the GIL
    /// while waiting for a task, reacquire to run it, repeat until shutdown drains.</summary>
    private static void RunWorkerLoop(AndroidFrameworkState state, ExecutorServicePeer pool, AndroidGil gil, DexInterpreter interpreter)
    {
        try
        {
            while (true)
            {
                FuturePeer? task;
                using (gil.BeginBlocking())
                {
                    if (pool.Tasks.IsCompleted) break;
                    bool ok = pool.IdleKeepaliveMs < 0
                        ? pool.Tasks.TryTake(out task, Timeout.InfiniteTimeSpan)
                        : pool.Tasks.TryTake(out task, pool.IdleKeepaliveMs);
                    if (!ok) break; // idle timeout (cached pool) — worker exits
                }
                if (task is null) continue;
                ExecuteTask(state, task);
            }
        }
        finally
        {
            // Every worker shape increments ActiveWorkers at spawn and decrements
            // here on exit; the pool is terminated once the last worker exits
            // after a shutdown (awaitTermination/isTerminated depend on it).
            if (Interlocked.Decrement(ref pool.ActiveWorkers) == 0 && pool.IsShutdown)
                pool.Terminated.Set();
        }
    }

    private static void ExecuteTask(AndroidFrameworkState state, FuturePeer task)
    {
        if (task.IsCancelled)
        {
            task.Completion.Set();
            return;
        }
        task.State = 1;
        task.RunningClrThread = Thread.CurrentThread;
        try
        {
            task.Result = task.Callable is not null
                ? state.Interpreter?.InvokeInstanceExact(task.Callable, "call", "()Ljava/lang/Object;")
                : state.Interpreter?.InvokeInstanceExact(task.Runnable!, "run", "()V");
            task.State = 2;
        }
        catch (Exception error)
        {
            task.TerminalException = error;
            task.State = 2;
        }
        finally
        {
            task.RunningClrThread = null;
            task.Completion.Set();
        }
    }

    private static void RegisterPoolMethods(AndroidApiRegistryBuilder builder, AndroidFrameworkState state, string owner)
    {
        builder.Register(Api(owner, "execute", "(Ljava/lang/Runnable;)V"), (_, args) => { Enqueue(state, state.ExecutorServices.Get(Receiver(args)), new FuturePeer { Runnable = RequireDex(args[1]) }); return null!; });
        builder.Register(Api(owner, "submit", "(Ljava/util/concurrent/Callable;)Ljava/util/concurrent/Future;"), (_, args) => Submit(state, Receiver(args), new FuturePeer { Callable = RequireDex(args[1]) }));
        builder.Register(Api(owner, "submit", "(Ljava/lang/Runnable;)Ljava/util/concurrent/Future;"), (_, args) => Submit(state, Receiver(args), new FuturePeer { Runnable = RequireDex(args[1]) }));
        builder.Register(Api(owner, "shutdown", "()V"), (_, args) => { Shutdown(state.ExecutorServices.Get(Receiver(args))); return null!; });
        builder.Register(Api(owner, "shutdownNow", "()Ljava/util/List;"), (_, args) => ShutdownNow(state, state.ExecutorServices.Get(Receiver(args))));
        builder.Register(Api(owner, "isShutdown", "()Z"), (_, args) => state.ExecutorServices.Get(Receiver(args)).IsShutdown ? 1 : 0);
        builder.Register(Api(owner, "isTerminated", "()Z"), (_, args) => state.ExecutorServices.Get(Receiver(args)).Terminated.IsSet ? 1 : 0);
        builder.Register(Api(owner, "awaitTermination", "(JLjava/util/concurrent/TimeUnit;)Z"), (_, args) =>
        {
            var pool = state.ExecutorServices.Get(Receiver(args));
            long millis = ToMillis(RequireTimeUnit(state, args[2]), RequireLong(args[1]));
            var gil = state.Gil;
            using (gil.BeginBlocking())
            {
                if (millis <= 0) return pool.Terminated.IsSet ? 1 : 0;
                return pool.Terminated.Wait(TimeSpan.FromMilliseconds(millis)) ? 1 : 0;
            }
        });
    }

    private static object Submit(AndroidFrameworkState state, DexObject poolObject, FuturePeer future)
    {
        var pool = state.ExecutorServices.Get(poolObject);
        Enqueue(state, pool, future);
        var futureObject = new DexObject(FutureTaskClass);
        state.Futures.Add(futureObject, future);
        return futureObject;
    }

    private static void Enqueue(AndroidFrameworkState state, ExecutorServicePeer pool, FuturePeer future)
    {
        try
        {
            lock (pool.WorkerGate)
            {
                if (pool.IsShutdown)
                    throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/util/concurrent/RejectedExecutionException;", "executor has been shut down"));
                pool.Tasks.Add(future);
                EnsureWorker(state, pool, force: false);
            }
        }
        catch (InvalidOperationException)
        {
            throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/util/concurrent/RejectedExecutionException;", "executor has been shut down"));
        }
    }

    private static void Shutdown(ExecutorServicePeer pool)
    {
        pool.RequestShutdown();
        try { pool.Tasks.CompleteAdding(); } catch (InvalidOperationException) { }
    }

    private static object ShutdownNow(AndroidFrameworkState state, ExecutorServicePeer pool)
    {
        pool.RequestShutdown();
        var remaining = new List<object?>();
        while (pool.Tasks.TryTake(out var task))
            remaining.Add(task.Runnable is not null ? task.Runnable : (object?)task.Callable);
        try { pool.Tasks.CompleteAdding(); } catch (InvalidOperationException) { }
        // Best-effort cooperative interrupt of running tasks, like real Java.
        lock (pool.WorkerGate)
        {
            foreach (FuturePeer running in pool.Running)
                running.RunningClrThread?.Interrupt();
        }
        var list = new DexObject("Ljava/util/ArrayList;");
        var peer = new ListPeer();
        peer.Elements.AddRange(remaining);
        state.ArrayLists.Add(list, peer);
        return list;
    }

    // ---------------------------------------------------------------------------
    // Future
    // ---------------------------------------------------------------------------

    private static void RegisterFutureMethods(AndroidApiRegistryBuilder builder, AndroidFrameworkState state, string owner)
    {
        builder.Register(Api(owner, "get", "()Ljava/lang/Object;"), (_, args) => Get(state, state.Futures.Get(Receiver(args)), timeoutMillis: -1));
        builder.Register(Api(owner, "get", "(JLjava/util/concurrent/TimeUnit;)Ljava/lang/Object;"), (_, args) =>
        {
            long millis = ToMillis(RequireTimeUnit(state, args[2]), RequireLong(args[1]));
            return Get(state, state.Futures.Get(Receiver(args)), timeoutMillis: millis);
        });
        builder.Register(Api(owner, "isDone", "()Z"), (_, args) => state.Futures.Get(Receiver(args)).IsDone ? 1 : 0);
        builder.Register(Api(owner, "isCancelled", "()Z"), (_, args) => state.Futures.Get(Receiver(args)).IsCancelled ? 1 : 0);
        builder.Register(Api(owner, "cancel", "(Z)Z"), (_, args) => Cancel(state, state.Futures.Get(Receiver(args)), RequireInt(args[1]) != 0) ? 1 : 0);
    }

    private static object Get(AndroidFrameworkState state, FuturePeer future, long timeoutMillis)
    {
        var gil = state.Gil;
        using (gil.BeginBlocking())
        {
            if (!future.IsDone)
            {
                if (timeoutMillis < 0)
                {
                    future.Completion.Wait();
                }
                else if (timeoutMillis > 0)
                {
                    if (!future.Completion.Wait(TimeSpan.FromMilliseconds(timeoutMillis)))
                        throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/util/concurrent/TimeoutException;"));
                }
                else if (!future.IsDone)
                {
                    throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/util/concurrent/TimeoutException;"));
                }
            }
        }
        if (future.IsCancelled)
            throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/util/concurrent/CancellationException;"));
        if (future.TerminalException is not null)
            throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/util/concurrent/ExecutionException;", "task threw " + future.TerminalException.GetType().Name));
        return future.Result ?? null!;
    }

    private static bool Cancel(AndroidFrameworkState state, FuturePeer future, bool mayInterruptIfRunning)
    {
        // 0 pending -> cancelled (task never runs; the worker skips it).
        if (future.TryTransition(from: 0, to: 3))
        {
            future.Completion.Set();
            return true;
        }
        // 1 running -> cancelled + best-effort interrupt when requested.
        if (mayInterruptIfRunning && future.TryTransition(from: 1, to: 3))
        {
            future.RunningClrThread?.Interrupt();
            future.Completion.Set();
            return true;
        }
        return false;
    }

    private static ExecutorServicePeer GetPoolForWorker(AndroidFrameworkState state, DexObject workerRunnable)
    {
        lock (state.WorkerRunnablesGate)
        {
            return state.WorkerRunnables.TryGetValue(workerRunnable, out var pool)
                ? pool
                : throw new InvalidOperationException("Unknown pool worker runnable.");
        }
    }

    // ---------------------------------------------------------------------------
    // TimeUnit (framework singletons)
    // ---------------------------------------------------------------------------

    private static void RegisterTimeUnit(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        builder.Register(Api(TimeUnitClass, "values", "()[Ljava/util/concurrent/TimeUnit;"), (_, _) => ValuesArray(state));
        builder.Register(Api(TimeUnitClass, "toMillis", "(J)J"), (_, args) => ToMillis(RequireConstant(state, args[0]), RequireLong(args[1])));
        builder.Register(Api(TimeUnitClass, "toNanos", "(J)J"), (_, args) => Scale(RequireLong(args[1]), RequireConstant(state, args[0]).NanosPerUnit));
        builder.Register(Api(TimeUnitClass, "toSeconds", "(J)J"), (_, args) => ScaleDivide(RequireLong(args[1]), RequireConstant(state, args[0]).NanosPerUnit, 1_000_000_000L));
        builder.Register(Api(TimeUnitClass, "convert", "(JLjava/util/concurrent/TimeUnit;)J"), (_, args) =>
        {
            // Real semantics: thisUnit.convert(duration, targetUnit) =
            // duration * targetUnit.nanos / thisUnit.nanos.
            var source = RequireConstant(state, args[0]);
            var target = RequireConstant(state, args[2]);
            return ScaleDivide(RequireLong(args[1]), target.NanosPerUnit, source.NanosPerUnit);
        });
        builder.Register(Api(TimeUnitClass, "ordinal", "()I"), (_, args) => RequireConstant(state, args[0]).Ordinal);
        builder.Register(Api(TimeUnitClass, "toString", "()Ljava/lang/String;"), (_, args) => RequireConstant(state, args[0]).Name);
    }

    private static object ValuesArray(AndroidFrameworkState state)
    {
        var array = new DexArray("[Ljava/util/concurrent/TimeUnit;", state.TimeUnitObjects.Length);
        for (int index = 0; index < state.TimeUnitObjects.Length; index++)
            array.Set(index, state.TimeUnitObjects[index]);
        return array;
    }

    private static long ToMillis(TimeUnitConstantPeer unit, long duration) => ScaleDivide(duration, unit.NanosPerUnit, 1_000_000L);

    private static long Scale(long value, long factor)
    {
        if (value == 0 || factor == 1) return value;
        if (value > 0)
        {
            if (value > long.MaxValue / factor) return long.MaxValue;
        }
        else
        {
            if (value < long.MinValue / factor) return long.MinValue;
        }
        return value * factor;
    }

    private static long ScaleDivide(long value, long factor, long divisor)
    {
        long scaled = Scale(value, factor);
        if (scaled == long.MaxValue) return long.MaxValue / divisor;
        if (scaled == long.MinValue) return long.MinValue / divisor;
        return scaled / divisor;
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    private static DexObject Receiver(object[] args) => (DexObject)args[0]!;
    private static int RequireInt(object? value) => value is int i ? i : throw new ArgumentException("Expected an int.");
    private static long RequireLong(object? value) => value is long l ? l : value is int i ? i : throw new ArgumentException("Expected a long.");
    private static DexObject RequireDex(object? value) => value as DexObject ?? throw new ArgumentException("Expected a guest object.");
    private static TimeUnitConstantPeer RequireConstant(AndroidFrameworkState state, object? value)
    {
        if (value is DexObject dex && state.TimeUnitByObject.TryGetValue(dex, out var constant)) return constant;
        throw new ArgumentException("Expected a java.util.concurrent.TimeUnit constant.");
    }
    private static TimeUnitConstantPeer RequireTimeUnit(AndroidFrameworkState state, object? value) => RequireConstant(state, value);
}
