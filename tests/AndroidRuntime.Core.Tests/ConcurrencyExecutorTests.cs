using System.Diagnostics;
using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Real-concurrency tests for java.util.concurrent Executors/ExecutorService/
/// Future/TimeUnit and android.os.Handler/Looper: real thread pools, real
/// blocking Future.get, framework TimeUnit singletons, and Handler.post reusing
/// the main-lane queue (or a standalone pump without a lane). Everything runs on
/// real background threads — nothing is simulated inline.
/// </summary>
public sealed class ConcurrencyExecutorTests
{
    private const string Probe = "Lc/Probe;";
    private const string ThreadClass = "Ljava/lang/Thread;";
    private const string ExecutorsClass = "Ljava/util/concurrent/Executors;";
    private const string ExecutorServiceClass = "Ljava/util/concurrent/ExecutorService;";
    private const string FutureClass = "Ljava/util/concurrent/Future;";
    private const string FutureTaskClass = "Ljava/util/concurrent/FutureTask;";
    private const string TimeUnitClass = "Ljava/util/concurrent/TimeUnit;";
    private const string HandlerClass = "Landroid/os/Handler;";
    private const string LooperClass = "Landroid/os/Looper;";

    // ---------------------------------------------------------------------------
    // Executors / Future
    // ---------------------------------------------------------------------------

    [Fact]
    public void Fixed_pool_runs_tasks_on_real_background_threads_and_future_get_returns_results()
    {
        var (state, registry, interpreter) = Session();
        var mainThread = Invoke(registry, state, ThreadClass, "currentThread", "()Ljava/lang/Thread;", AndroidInvokeKind.Static);
        interpreter.InvokeStaticExact(Probe, "storeResult1", "(Ljava/lang/String;)V", "calc-1");
        interpreter.InvokeStaticExact(Probe, "storeResult2", "(Ljava/lang/String;)V", "calc-2");

        var pool = Invoke(registry, state, ExecutorsClass, "newFixedThreadPool", "(I)Ljava/util/concurrent/ExecutorService;", AndroidInvokeKind.Static, 2);
        var f1 = Invoke(registry, state, ExecutorServiceClass, "submit", "(Ljava/util/concurrent/Callable;)Ljava/util/concurrent/Future;", AndroidInvokeKind.Virtual, pool, new DexObject("Lc/Calc;"));
        var f2 = Invoke(registry, state, ExecutorServiceClass, "submit", "(Ljava/util/concurrent/Callable;)Ljava/util/concurrent/Future;", AndroidInvokeKind.Virtual, pool, new DexObject("Lc/Calc2;"));

        object? r1;
        object? r2;
        using (state.Gil.Acquire())
        {
            // Future.get is a real blocking wait: it releases the GIL while waiting.
            r1 = Invoke(registry, state, FutureClass, "get", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, f1);
            r2 = Invoke(registry, state, FutureClass, "get", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, f2);
        }

        Assert.Equal("calc-1", r1);
        Assert.Equal("calc-2", r2);
        Assert.Equal(1, Invoke(registry, state, FutureClass, "isDone", "()Z", AndroidInvokeKind.Virtual, f1));
        // Each Callable ran on a REAL background thread, distinct from the caller.
        var background = (DexObject)interpreter.InvokeStaticExact(Probe, "getThread", "()Ljava/lang/Object;")!;
        Assert.NotSame(mainThread, background);
        Invoke(registry, state, ExecutorServiceClass, "shutdown", "()V", AndroidInvokeKind.Virtual, pool);
    }

    [Fact]
    public void Future_get_actually_blocks_until_the_task_completes()
    {
        var (state, registry, _) = Session();
        var pool = Invoke(registry, state, ExecutorsClass, "newFixedThreadPool", "(I)Ljava/util/concurrent/ExecutorService;", AndroidInvokeKind.Static, 1);
        var future = Invoke(registry, state, ExecutorServiceClass, "submit", "(Ljava/util/concurrent/Callable;)Ljava/util/concurrent/Future;", AndroidInvokeKind.Virtual, pool, new DexObject("Lc/SlowCalc;"));

        var watch = Stopwatch.StartNew();
        using (state.Gil.Acquire())
            Invoke(registry, state, FutureClass, "get", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, future);
        watch.Stop();

        // The callable sleeps 300ms before returning result2: a synchronous fake
        // would return in ~0ms; a real Future.get genuinely blocks.
        Assert.True(watch.ElapsedMilliseconds >= 250, $"get returned after {watch.ElapsedMilliseconds}ms; did not block");
        Invoke(registry, state, ExecutorServiceClass, "shutdown", "()V", AndroidInvokeKind.Virtual, pool);
    }

    [Fact]
    public void Future_get_with_timeout_throws_timeout_exception_then_succeeds()
    {
        var (state, registry, interpreter) = Session();
        // SlowCalc returns result2 after sleeping 300ms; pre-seed it.
        interpreter.InvokeStaticExact(Probe, "storeResult2", "(Ljava/lang/String;)V", "slow-calc");
        var pool = Invoke(registry, state, ExecutorsClass, "newFixedThreadPool", "(I)Ljava/util/concurrent/ExecutorService;", AndroidInvokeKind.Static, 1);
        var future = Invoke(registry, state, ExecutorServiceClass, "submit", "(Ljava/util/concurrent/Callable;)Ljava/util/concurrent/Future;", AndroidInvokeKind.Virtual, pool, new DexObject("Lc/SlowCalc;"));

        var error = Assert.Throws<GuestExceptionCarrier>(() =>
        {
            using (state.Gil.Acquire())
                Invoke(registry, state, FutureClass, "get", "(JLjava/util/concurrent/TimeUnit;)Ljava/lang/Object;", AndroidInvokeKind.Virtual, future, 50L, TimeUnitMillis(state));
        });
        Assert.Equal("Ljava/util/concurrent/TimeoutException;", error.Throwable.TypeDescriptor);

        // After the task eventually completes, the same future returns its result.
        object? result;
        using (state.Gil.Acquire())
            result = Invoke(registry, state, FutureClass, "get", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, future);
        Assert.Equal("slow-calc", result);
        Invoke(registry, state, ExecutorServiceClass, "shutdown", "()V", AndroidInvokeKind.Virtual, pool);
    }

    [Fact]
    public void Submit_runnable_and_execute_fire_and_forget_both_run_tasks()
    {
        var (state, registry, interpreter) = Session();
        var pool = Invoke(registry, state, ExecutorsClass, "newFixedThreadPool", "(I)Ljava/util/concurrent/ExecutorService;", AndroidInvokeKind.Static, 1);
        var future = Invoke(registry, state, ExecutorServiceClass, "submit", "(Ljava/lang/Runnable;)Ljava/util/concurrent/Future;", AndroidInvokeKind.Virtual, pool, new DexObject("Lc/Starter;"));
        Invoke(registry, state, ExecutorServiceClass, "execute", "(Ljava/lang/Runnable;)V", AndroidInvokeKind.Virtual, pool, new DexObject("Lc/Starter;"));

        using (state.Gil.Acquire())
            Assert.Null(Invoke(registry, state, FutureClass, "get", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, future));
        WaitFor(() => ((int)interpreter.InvokeStaticExact(Probe, "getStarted", "()I") == 2), timeoutMs: 2000);        Assert.Equal(2, interpreter.InvokeStaticExact(Probe, "getStarted", "()I"));
        Invoke(registry, state, ExecutorServiceClass, "shutdown", "()V", AndroidInvokeKind.Virtual, pool);
    }

    [Fact]
    public void Shutdown_stops_new_submissions_and_await_termination_returns_true()
    {
        var (state, registry, interpreter) = Session();
        var pool = Invoke(registry, state, ExecutorsClass, "newFixedThreadPool", "(I)Ljava/util/concurrent/ExecutorService;", AndroidInvokeKind.Static, 2);
        Invoke(registry, state, ExecutorServiceClass, "execute", "(Ljava/lang/Runnable;)V", AndroidInvokeKind.Virtual, pool, new DexObject("Lc/Starter;"));
        Invoke(registry, state, ExecutorServiceClass, "shutdown", "()V", AndroidInvokeKind.Virtual, pool);

        var rejected = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, ExecutorServiceClass, "execute", "(Ljava/lang/Runnable;)V", AndroidInvokeKind.Virtual, pool, new DexObject("Lc/Starter;")));
        Assert.Equal("Ljava/util/concurrent/RejectedExecutionException;", rejected.Throwable.TypeDescriptor);
        Assert.Equal(1, Invoke(registry, state, ExecutorServiceClass, "isShutdown", "()Z", AndroidInvokeKind.Virtual, pool));

        // Queued task still ran to completion after shutdown.
        WaitFor(() => ((int)interpreter.InvokeStaticExact(Probe, "getStarted", "()I") == 1), timeoutMs: 2000);
        int terminated;
        using (state.Gil.Acquire())
        {
            Assert.Equal(1, Invoke(registry, state, ExecutorServiceClass, "awaitTermination", "(JLjava/util/concurrent/TimeUnit;)Z", AndroidInvokeKind.Virtual, pool, 5L, TimeUnitSeconds(state)));
            terminated = (int)Invoke(registry, state, ExecutorServiceClass, "isTerminated", "()Z", AndroidInvokeKind.Virtual, pool);
        }
        Assert.Equal(1, terminated);
    }

    [Fact]
    public void Shutdown_now_returns_queued_not_started_tasks()
    {
        var (state, registry, interpreter) = Session();
        var pool = Invoke(registry, state, ExecutorsClass, "newFixedThreadPool", "(I)Ljava/util/concurrent/ExecutorService;", AndroidInvokeKind.Static, 1);
        Invoke(registry, state, ExecutorServiceClass, "execute", "(Ljava/lang/Runnable;)V", AndroidInvokeKind.Virtual, pool, new DexObject("Lc/Sleeper;"));
        // Give the single worker time to pick up the sleeping task, then queue one more.
        Thread.Sleep(80);
        Invoke(registry, state, ExecutorServiceClass, "execute", "(Ljava/lang/Runnable;)V", AndroidInvokeKind.Virtual, pool, new DexObject("Lc/Starter;"));

        var returned = (DexObject)Invoke(registry, state, ExecutorServiceClass, "shutdownNow", "()Ljava/util/List;", AndroidInvokeKind.Virtual, pool);
        var peer = state.ArrayLists.Get(returned);
        Assert.Single(peer.Elements);
        // The not-yet-started Starter never ran.
        Assert.Equal(0, interpreter.InvokeStaticExact(Probe, "getStarted", "()I"));
    }

    [Fact]
    public void Cancel_before_start_marks_future_cancelled_and_task_never_runs()
    {
        var (state, registry, interpreter) = Session();
        var pool = Invoke(registry, state, ExecutorsClass, "newSingleThreadExecutor", "()Ljava/util/concurrent/ExecutorService;", AndroidInvokeKind.Static);
        Invoke(registry, state, ExecutorServiceClass, "execute", "(Ljava/lang/Runnable;)V", AndroidInvokeKind.Virtual, pool, new DexObject("Lc/Sleeper;"));
        Thread.Sleep(80);
        var future = Invoke(registry, state, ExecutorServiceClass, "submit", "(Ljava/lang/Runnable;)Ljava/util/concurrent/Future;", AndroidInvokeKind.Virtual, pool, new DexObject("Lc/Starter;"));

        Assert.Equal(1, Invoke(registry, state, FutureClass, "cancel", "(Z)Z", AndroidInvokeKind.Virtual, future, 1));
        Assert.Equal(1, Invoke(registry, state, FutureClass, "isCancelled", "()Z", AndroidInvokeKind.Virtual, future));
        Assert.Equal(1, Invoke(registry, state, FutureClass, "isDone", "()Z", AndroidInvokeKind.Virtual, future));
        var cancelled = Assert.Throws<GuestExceptionCarrier>(() =>
        {
            using (state.Gil.Acquire())
                Invoke(registry, state, FutureClass, "get", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, future);
        });
        Assert.Equal("Ljava/util/concurrent/CancellationException;", cancelled.Throwable.TypeDescriptor);
        Thread.Sleep(100);
        Assert.Equal(0, interpreter.InvokeStaticExact(Probe, "getStarted", "()I"));
        Invoke(registry, state, ExecutorServiceClass, "shutdownNow", "()Ljava/util/List;", AndroidInvokeKind.Virtual, pool);
    }

    [Fact]
    public void Fixed_pool_with_guest_thread_factory_runs_tasks_via_factory_threads()
    {
        var (state, registry, interpreter) = Session();
        var factory = new DexObject("Lc/Factory;");
        var pool = Invoke(registry, state, ExecutorsClass, "newFixedThreadPool", "(ILjava/util/concurrent/ThreadFactory;)Ljava/util/concurrent/ExecutorService;", AndroidInvokeKind.Static, 2, factory);

        Invoke(registry, state, ExecutorServiceClass, "execute", "(Ljava/lang/Runnable;)V", AndroidInvokeKind.Virtual, pool, new DexObject("Lc/Starter;"));
        Invoke(registry, state, ExecutorServiceClass, "execute", "(Ljava/lang/Runnable;)V", AndroidInvokeKind.Virtual, pool, new DexObject("Lc/Starter;"));

        WaitFor(() => ((int)interpreter.InvokeStaticExact(Probe, "getStarted", "()I") == 2), timeoutMs: 2000);
        Assert.Equal(2, interpreter.InvokeStaticExact(Probe, "getStarted", "()I"));
        Invoke(registry, state, ExecutorServiceClass, "shutdown", "()V", AndroidInvokeKind.Virtual, pool);
    }

    // ---------------------------------------------------------------------------
    // TimeUnit (framework singletons + sget)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Time_unit_constants_resolve_and_convert()
    {
        var (state, registry, interpreter) = Session();
        var seconds = TimeUnitSeconds(state);
        var millis = TimeUnitMillis(state);

        Assert.Equal(2000L, Invoke(registry, state, TimeUnitClass, "toMillis", "(J)J", AndroidInvokeKind.Virtual, seconds, 2L));
        Assert.Equal(1_000_000L, Invoke(registry, state, TimeUnitClass, "toNanos", "(J)J", AndroidInvokeKind.Virtual, millis, 1L));
        Assert.Equal(2L, Invoke(registry, state, TimeUnitClass, "toSeconds", "(J)J", AndroidInvokeKind.Virtual, millis, 2000L));
        // millis.convert(1, SECONDS) = 1000: one second expressed in milliseconds.
        Assert.Equal(1000L, Invoke(registry, state, TimeUnitClass, "convert", "(JLjava/util/concurrent/TimeUnit;)J", AndroidInvokeKind.Virtual, millis, 1L, seconds));
        Assert.Equal(3, Invoke(registry, state, TimeUnitClass, "ordinal", "()I", AndroidInvokeKind.Virtual, seconds));
        Assert.Equal("SECONDS", Invoke(registry, state, TimeUnitClass, "toString", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, seconds));

        var values = (DexArray)Invoke(registry, state, TimeUnitClass, "values", "()[Ljava/util/concurrent/TimeUnit;", AndroidInvokeKind.Static);
        Assert.Equal(7, values.Length);

        // Guest sget of TimeUnit.SECONDS resolves through the framework
        // static-field hook (no DEX class/field table exists for it).
        interpreter.InvokeStaticExact("Lc/TimeReader;", "read", "()V");
        Assert.Equal(2000L, interpreter.InvokeStaticExact(Probe, "getTimeMillis", "()J"));
    }

    // ---------------------------------------------------------------------------
    // Handler / Looper
    // ---------------------------------------------------------------------------

    [Fact]
    public void Handler_post_on_main_looper_runs_asynchronously_not_inline()
    {
        var (state, registry, interpreter) = Session();
        var mainThread = Invoke(registry, state, ThreadClass, "currentThread", "()Ljava/lang/Thread;", AndroidInvokeKind.Static);
        var mainLooper = Invoke(registry, state, LooperClass, "getMainLooper", "()Landroid/os/Looper;", AndroidInvokeKind.Static);
        var handler = new DexObject(HandlerClass);
        Invoke(registry, state, HandlerClass, "<init>", "(Landroid/os/Looper;)V", AndroidInvokeKind.Direct, handler, mainLooper);

        var posted = Invoke(registry, state, HandlerClass, "post", "(Ljava/lang/Runnable;)Z", AndroidInvokeKind.Virtual, handler, new DexObject("Lc/Starter;"));
        Assert.Equal(1, posted);

        WaitFor(() => ((int)interpreter.InvokeStaticExact(Probe, "getStarted", "()I") == 1), timeoutMs: 2000);
        Assert.Equal(1, interpreter.InvokeStaticExact(Probe, "getFinished", "()I"));
        // Ran asynchronously on the looper pump, NOT inline on the posting thread:
        // the runnable observed a different guest Thread as currentThread().
        var background = (DexObject)interpreter.InvokeStaticExact(Probe, "getThread", "()Ljava/lang/Object;")!;
        Assert.NotSame(mainThread, background);
    }

    [Fact]
    public void Handler_post_delayed_actually_delays()
    {
        var (state, registry, interpreter) = Session();
        var mainLooper = Invoke(registry, state, LooperClass, "getMainLooper", "()Landroid/os/Looper;", AndroidInvokeKind.Static);
        var handler = new DexObject(HandlerClass);
        Invoke(registry, state, HandlerClass, "<init>", "(Landroid/os/Looper;)V", AndroidInvokeKind.Direct, handler, mainLooper);

        var watch = Stopwatch.StartNew();
        Invoke(registry, state, HandlerClass, "postDelayed", "(Ljava/lang/Runnable;J)Z", AndroidInvokeKind.Virtual, handler, new DexObject("Lc/Starter;"), 150L);
        WaitFor(() => ((int)interpreter.InvokeStaticExact(Probe, "getStarted", "()I") == 1), timeoutMs: 2000);
        watch.Stop();

        Assert.True(watch.ElapsedMilliseconds >= 100, $"delayed post ran after {watch.ElapsedMilliseconds}ms; did not delay");
    }

    [Fact]
    public void Handler_remove_callbacks_prevents_execution()
    {
        var (state, registry, interpreter) = Session();
        var mainLooper = Invoke(registry, state, LooperClass, "getMainLooper", "()Landroid/os/Looper;", AndroidInvokeKind.Static);
        var handler = new DexObject(HandlerClass);
        Invoke(registry, state, HandlerClass, "<init>", "(Landroid/os/Looper;)V", AndroidInvokeKind.Direct, handler, mainLooper);
        var runnable = new DexObject("Lc/Starter;");

        // Deterministic: a DELAYED callback is still pending when we remove it —
        // removeCallbacks cancels the timer and the wrapper skips when it would run.
        Invoke(registry, state, HandlerClass, "postDelayed", "(Ljava/lang/Runnable;J)Z", AndroidInvokeKind.Virtual, handler, runnable, 150L);
        Invoke(registry, state, HandlerClass, "removeCallbacks", "(Ljava/lang/Runnable;)V", AndroidInvokeKind.Virtual, handler, runnable);
        Assert.Equal(0, Invoke(registry, state, HandlerClass, "hasCallbacks", "(Ljava/lang/Runnable;)Z", AndroidInvokeKind.Virtual, handler, runnable));
        Thread.Sleep(400);
        Assert.Equal(0, interpreter.InvokeStaticExact(Probe, "getStarted", "()I"));
    }

    [Fact]
    public void No_arg_handler_throws_when_thread_has_no_looper()
    {
        var (state, registry, _) = Session();
        var handler = new DexObject(HandlerClass);
        var error = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, HandlerClass, "<init>", "()V", AndroidInvokeKind.Direct, handler));
        Assert.Equal("Ljava/lang/RuntimeException;", error.Throwable.TypeDescriptor);
    }

    [Fact]
    public async Task Background_thread_with_prepare_and_loop_hosts_its_own_handler()
    {
        var (state, registry, interpreter) = Session();
        DexObject? handler = null;
        DexObject? looper = null;
        var handlerReady = new ManualResetEventSlim(false);
        var background = Task.Run(() =>
        {
            using (state.Gil.Acquire())
            {
                Invoke(registry, state, LooperClass, "prepare", "()V", AndroidInvokeKind.Static);
                looper = (DexObject)Invoke(registry, state, LooperClass, "myLooper", "()Landroid/os/Looper;", AndroidInvokeKind.Static);
                var local = new DexObject(HandlerClass);
                Invoke(registry, state, HandlerClass, "<init>", "()V", AndroidInvokeKind.Direct, local);
                handler = local;
                handlerReady.Set();
                // Blocks pumping this thread's private queue until quit.
                Invoke(registry, state, LooperClass, "loop", "()V", AndroidInvokeKind.Static);
            }
        });

        Assert.True(handlerReady.Wait(2000), "background looper did not come up");
        Assert.NotNull(looper);

        Invoke(registry, state, HandlerClass, "post", "(Ljava/lang/Runnable;)Z", AndroidInvokeKind.Virtual, handler!, new DexObject("Lc/Starter;"));
        WaitFor(() => ((int)interpreter.InvokeStaticExact(Probe, "getStarted", "()I") == 1), timeoutMs: 2000);
        Assert.Equal(1, interpreter.InvokeStaticExact(Probe, "getFinished", "()I"));

        Invoke(registry, state, LooperClass, "quit", "()V", AndroidInvokeKind.Virtual, looper!);
        await background.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(background.IsCompleted, "looper loop did not stop after quit");
    }

    [Fact]
    public void My_looper_returns_null_for_thread_without_prepare()
    {
        var (state, registry, _) = Session();
        // The TEST thread never called prepare and has no lane: myLooper -> null.
        Assert.Null(Invoke(registry, state, LooperClass, "myLooper", "()Landroid/os/Looper;", AndroidInvokeKind.Static));
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static object TimeUnitSeconds(AndroidFrameworkState state) => state.TimeUnitByName["SECONDS"];
    private static object TimeUnitMillis(AndroidFrameworkState state) => state.TimeUnitByName["MILLISECONDS"];

    private static void WaitFor(Func<bool> condition, int timeoutMs)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < timeoutMs)
            Thread.Sleep(10);
        Assert.True(condition(), $"condition not met within {timeoutMs}ms");
    }

    private static (AndroidFrameworkState State, AndroidApiRegistry Registry, DexInterpreter Interpreter) Session()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new QuietLogSink()).Build();
        var dex = BuildDex();
        var interpreter = new DexInterpreter(dex, registry, gil: state.Gil);
        state.Gil = interpreter.Gil;
        state.AttachInterpreter(interpreter);
        return (state, registry, interpreter);
    }

    private static DexFile BuildDex()
    {
        var dex = new DexFile();
        dex.TypeDescriptors.Add("Ljava/lang/Thread;"); // type index 0, used by Lc/Factory; new-instance
        // Probe fields: 0 started I, 1 finished I, 2 ran I, 3 threadObj Object,
        // 4 completed I, 5 result1 String, 6 result2 String, 7 timeMillis J,
        // 8 TimeUnit.SECONDS (framework static, resolved by the interpreter hook).
        dex.Fields.Add(Field(Probe, "started", "I"));
        dex.Fields.Add(Field(Probe, "finished", "I"));
        dex.Fields.Add(Field(Probe, "ran", "I"));
        dex.Fields.Add(Field(Probe, "threadObj", "Ljava/lang/Object;"));
        dex.Fields.Add(Field(Probe, "completed", "I"));
        dex.Fields.Add(Field(Probe, "result1", "Ljava/lang/String;"));
        dex.Fields.Add(Field(Probe, "result2", "Ljava/lang/String;"));
        dex.Fields.Add(Field(Probe, "timeMillis", "J"));
        dex.Fields.Add(Field("Ljava/util/concurrent/TimeUnit;", "SECONDS", "Ljava/util/concurrent/TimeUnit;"));

        var probe = new DexClass { Descriptor = Probe, SuperclassDescriptor = "Ljava/lang/Object;" };
        probe.DirectMethods.Add(Method(Probe, "getStarted", "()I", 1, 0, [0x0060, 0x0000, 0x000f]));
        probe.DirectMethods.Add(Method(Probe, "getFinished", "()I", 1, 0, [0x0060, 0x0001, 0x000f]));
        probe.DirectMethods.Add(Method(Probe, "getRan", "()I", 1, 0, [0x0060, 0x0002, 0x000f]));
        probe.DirectMethods.Add(Method(Probe, "getThread", "()Ljava/lang/Object;", 1, 0, [0x0060, 0x0003, 0x0011]));
        probe.DirectMethods.Add(Method(Probe, "getCompleted", "()I", 1, 0, [0x0060, 0x0004, 0x000f]));
        probe.DirectMethods.Add(Method(Probe, "getTimeMillis", "()J", 2, 0, [0x0061, 0x0007, 0x0010]));
        probe.DirectMethods.Add(Method(Probe, "markStarted", "()V", 2, 0, [0x0060, 0x0000, 0x1112, 0x0090, 0x0100, 0x0067, 0x0000, 0x000e]));
        probe.DirectMethods.Add(Method(Probe, "markFinished", "()V", 2, 0, [0x0060, 0x0001, 0x1112, 0x0090, 0x0100, 0x0067, 0x0001, 0x000e]));
        probe.DirectMethods.Add(Method(Probe, "markCompleted", "()V", 2, 0, [0x0060, 0x0004, 0x1112, 0x0090, 0x0100, 0x0067, 0x0004, 0x000e]));
        probe.DirectMethods.Add(Method(Probe, "storeThread", "(Ljava/lang/Object;)V", 1, 1, [0x0067, 0x0003, 0x000e]));
        probe.DirectMethods.Add(Method(Probe, "storeResult1", "(Ljava/lang/String;)V", 1, 1, [0x0067, 0x0005, 0x000e]));
        probe.DirectMethods.Add(Method(Probe, "storeResult2", "(Ljava/lang/String;)V", 1, 1, [0x0067, 0x0006, 0x000e]));
        probe.DirectMethods.Add(Method(Probe, "storeMillis", "(J)V", 2, 1, [0x0068, 0x0007, 0x000e]));

        // Lc/Starter; run()V: markStarted; currentThread; storeThread; markFinished.
        var starter = new DexClass { Descriptor = "Lc/Starter;", SuperclassDescriptor = "Ljava/lang/Object;" };
        starter.DirectMethods.Add(Method("Lc/Starter;", "run", "()V", 1, 1,
        [
            0x0071, 0x0006, 0x0000,  // invoke-static Probe.markStarted (idx 6)
            0x0071, 0x0001, 0x0000,  // invoke-static Thread.currentThread (idx 1)
            0x000c,                   // move-result-object v0
            0x1071, 0x0002, 0x0000,  // invoke-static Probe.storeThread(v0) (idx 2)
            0x0071, 0x0003, 0x0000,  // invoke-static Probe.markFinished (idx 3)
            0x000e
        ], isStatic: false));

        // Lc/Sleeper; run()V: Thread.sleep(300); Probe.markCompleted.
        var sleeper = new DexClass { Descriptor = "Lc/Sleeper;", SuperclassDescriptor = "Ljava/lang/Object;" };
        sleeper.DirectMethods.Add(Method("Lc/Sleeper;", "run", "()V", 2, 1,
        [
            0x0016, 0x012C,           // const-wide/16 v0, 300
            0x2171, 0x0004, 0x0010,  // invoke-static Thread.sleep {v0,v1} (idx 4)
            0x0071, 0x0005, 0x0000,  // invoke-static Probe.markCompleted (idx 5)
            0x000e
        ], isStatic: false));

        // Lc/Calc; call()Ljava/lang/Object;: sget-object result1; return-object.
        var calc = new DexClass { Descriptor = "Lc/Calc;", SuperclassDescriptor = "Ljava/lang/Object;" };
        calc.DirectMethods.Add(Method("Lc/Calc;", "call", "()Ljava/lang/Object;", 2, 1, [0x0062, 0x0005, 0x0011], isStatic: false));

        // Lc/Calc2; call(): sget-object result2; return-object.
        var calc2 = new DexClass { Descriptor = "Lc/Calc2;", SuperclassDescriptor = "Ljava/lang/Object;" };
        calc2.DirectMethods.Add(Method("Lc/Calc2;", "call", "()Ljava/lang/Object;", 2, 1, [0x0062, 0x0006, 0x0011], isStatic: false));

        // Lc/SlowCalc; call(): sleep(300); sget-object result2; return-object.
        var slowCalc = new DexClass { Descriptor = "Lc/SlowCalc;", SuperclassDescriptor = "Ljava/lang/Object;" };
        slowCalc.DirectMethods.Add(Method("Lc/SlowCalc;", "call", "()Ljava/lang/Object;", 3, 1,
        [
            0x0016, 0x012C,           // const-wide/16 v0, 300
            0x2171, 0x0004, 0x0010,  // Thread.sleep {v0,v1} (idx 4)
            0x0062, 0x0006, 0x0000,  // sget-object v0, result2
            0x0011                    // return-object v0
        ], isStatic: false));

        // Lc/Factory; newThread(Runnable)Thread: new-instance Thread; <init>(Runnable); return.
        // Register plan (regs=3, ins=2): v1=this, v2=runnable, v0=local result.
        var factory = new DexClass { Descriptor = "Lc/Factory;", SuperclassDescriptor = "Ljava/lang/Object;" };
        factory.DirectMethods.Add(Method("Lc/Factory;", "newThread", "(Ljava/lang/Runnable;)Ljava/lang/Thread;", 3, 2,
        [
            0x0022, 0x0000,          // new-instance v0, Ljava/lang/Thread; (type idx 0)
            0x2070, 0x000B, 0x0020,  // invoke-direct {v0,v2} Thread.<init>(Runnable) (idx 11)
            0x0011                    // return-object v0
        ], isStatic: false));

        // Lc/TimeReader; read()V: sget-object v0 TimeUnit.SECONDS; const-wide 2;
        // invoke-virtual {v0,v1,v2} TimeUnit.toMillis; sput-wide timeMillis; return.
        var timeReader = new DexClass { Descriptor = "Lc/TimeReader;", SuperclassDescriptor = "Ljava/lang/Object;" };
        timeReader.DirectMethods.Add(Method("Lc/TimeReader;", "read", "()V", 3, 0,
        [
            0x0062, 0x0008,          // sget-object v0, TimeUnit.SECONDS (field idx 8)
            0x0116, 0x0002,           // const-wide/16 v1, 2
            0x3072, 0x0007, 0x0210,  // invoke-virtual {v0,v1,v2} TimeUnit.toMillis (idx 7)
            0x010b,                   // move-result-wide v1
            0x0168, 0x0007,           // sput-wide v1, Probe.timeMillis (field idx 7)
            0x000e
        ]));

        dex.Classes.Add(probe);
        dex.Classes.Add(starter);
        dex.Classes.Add(sleeper);
        dex.Classes.Add(calc);
        dex.Classes.Add(calc2);
        dex.Classes.Add(slowCalc);
        dex.Classes.Add(factory);
        dex.Classes.Add(timeReader);

        // Shared method reference pool.
        dex.Methods.Add(Ref(Probe, "markRan", "()V"));                                      // 0
        dex.Methods.Add(Ref(ThreadClass, "currentThread", "()Ljava/lang/Thread;"));          // 1
        dex.Methods.Add(Ref(Probe, "storeThread", "(Ljava/lang/Object;)V"));                 // 2
        dex.Methods.Add(Ref(Probe, "markFinished", "()V"));                                  // 3
        dex.Methods.Add(Ref(ThreadClass, "sleep", "(J)V"));                                  // 4
        dex.Methods.Add(Ref(Probe, "markCompleted", "()V"));                                 // 5
        dex.Methods.Add(Ref(Probe, "markStarted", "()V"));                                   // 6
        dex.Methods.Add(Ref(TimeUnitClass, "toMillis", "(J)J"));                             // 7
        dex.Methods.Add(Ref(Probe, "storeMillis", "(J)V"));                                  // 8
        dex.Methods.Add(Ref(Probe, "storeResult1", "(Ljava/lang/String;)V"));                // 9
        dex.Methods.Add(Ref(Probe, "storeResult2", "(Ljava/lang/String;)V"));                // 10
        dex.Methods.Add(Ref(ThreadClass, "<init>", "(Ljava/lang/Runnable;)V"));              // 11

        dex.BuildIndexes();
        return dex;
    }

    private static DexFieldRef Field(string owner, string name, string type) => new() { ClassDescriptor = owner, Name = name, Type = type };
    private static DexMethodRef Ref(string owner, string name, string descriptor)
    {
        int close = descriptor.IndexOf(')');
        var parameters = new List<string>();
        for (int index = 1; index < close;)
        {
            int start = index;
            if (descriptor[index] == 'L') index = descriptor.IndexOf(';', index) + 1; else index++;
            parameters.Add(descriptor[start..index]);
        }
        return new DexMethodRef { ClassDescriptor = owner, Name = name, Proto = new DexProto { ReturnType = descriptor[(close + 1)..], ParameterTypes = parameters } };
    }
    private static DexEncodedMethod Method(string owner, string name, string descriptor, int registers, int ins, ushort[] instructions, bool isStatic = true) => new()
    {
        AccessFlags = isStatic ? DexConstants.ACC_STATIC : 0,
        Method = Ref(owner, name, descriptor),
        Code = new DexCodeItem { RegistersSize = registers, InsSize = ins, OutsSize = 0, Instructions = instructions }
    };

    private static object Invoke(AndroidApiRegistry registry, AndroidFrameworkState state, string owner, string name, string descriptor, AndroidInvokeKind kind, params object[] args)
    {
        var api = new AndroidApiMethodId(owner, name, descriptor);
        var context = new AndroidApiSessionContext(state.SessionId, state.PackageName, state.ActivityDescriptor, default, () => true);
        return registry.Invoke(context, new AndroidApiCallSite("Ltest;->run()V", 0, api, api, kind), args);
    }

    private sealed class QuietLogSink : IAndroidLogSink { public int Info(AndroidLogEntry entry) => 1; }
}
