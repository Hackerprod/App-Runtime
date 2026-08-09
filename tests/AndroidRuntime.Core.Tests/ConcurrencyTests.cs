using System.Diagnostics;
using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Real-concurrency tests for the GIL model: guest Threads run on real CLR threads,
/// join really waits, class initialization blocks a second real thread, and real
/// per-object monitors serialize genuinely different threads.
/// </summary>
public sealed class ConcurrencyTests
{
    private const string Probe = "Lc/Probe;";
    private const string ThreadClass = "Ljava/lang/Thread;";

    [Fact]
    public void Guest_thread_runs_its_runnable_on_a_real_background_thread_and_join_waits()
    {
        var (state, registry, interpreter) = Session();
        var mainThread = Invoke(registry, state, ThreadClass, "currentThread", "()Ljava/lang/Thread;", AndroidInvokeKind.Static);

        var thread = new DexObject(ThreadClass);
        Invoke(registry, state, ThreadClass, "<init>", "(Ljava/lang/Runnable;)V", AndroidInvokeKind.Direct, thread, new DexObject("Lc/Runner;"));
        using (state.Gil.Acquire())
        {
            Invoke(registry, state, ThreadClass, "start", "()V", AndroidInvokeKind.Virtual, thread);
            Invoke(registry, state, ThreadClass, "join", "()V", AndroidInvokeKind.Virtual, thread);
        }

        // The background run() body executed and finished (join waited for it).
        Assert.Equal(1, interpreter.InvokeStaticExact(Probe, "getRan", "()I"));
        Assert.Equal(1, interpreter.InvokeStaticExact(Probe, "getFinished", "()I"));
        Assert.Equal(0, Invoke(registry, state, ThreadClass, "isAlive", "()Z", AndroidInvokeKind.Virtual, thread));
        // currentThread() inside the background thread differs from the caller's.
        var background = (DexObject)interpreter.InvokeStaticExact(Probe, "getThread", "()Ljava/lang/Object;")!;
        Assert.NotSame(mainThread, background);
    }

    [Fact]
    public void Double_start_throws_illegal_thread_state_exception()
    {
        var (state, registry, _) = Session();
        var thread = new DexObject(ThreadClass);
        Invoke(registry, state, ThreadClass, "<init>", "()V", AndroidInvokeKind.Direct, thread);
        using (state.Gil.Acquire())
        {
            Invoke(registry, state, ThreadClass, "start", "()V", AndroidInvokeKind.Virtual, thread);
            var error = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, ThreadClass, "start", "()V", AndroidInvokeKind.Virtual, thread));
            Assert.Equal("Ljava/lang/IllegalThreadStateException;", error.Throwable.TypeDescriptor);
            Invoke(registry, state, ThreadClass, "join", "()V", AndroidInvokeKind.Virtual, thread);
        }
    }

    [Fact]
    public void Interrupt_wakes_a_guest_sleep_and_terminates_the_thread()
    {
        var (state, registry, interpreter) = Session();
        var thread = new DexObject(ThreadClass);
        Invoke(registry, state, ThreadClass, "<init>", "(Ljava/lang/Runnable;)V", AndroidInvokeKind.Direct, thread, new DexObject("Lc/Sleeper;"));
        Invoke(registry, state, ThreadClass, "start", "()V", AndroidInvokeKind.Virtual, thread);

        // Start WITHOUT holding the GIL so the background thread can enter its guest
        // sleep; then interrupt it and join (join releases the GIL while waiting).
        System.Threading.Thread.Sleep(50);
        var watch = Stopwatch.StartNew();
        Invoke(registry, state, ThreadClass, "interrupt", "()V", AndroidInvokeKind.Virtual, thread);
        using (state.Gil.Acquire())
            Invoke(registry, state, ThreadClass, "join", "()V", AndroidInvokeKind.Virtual, thread);
        watch.Stop();

        // The sleep (10s) was interrupted: join returned quickly and markCompleted never ran.
        Assert.True(watch.ElapsedMilliseconds < 2000, $"join took {watch.ElapsedMilliseconds}ms; interrupt did not wake the sleep");
        Assert.Equal(0, interpreter.InvokeStaticExact(Probe, "getCompleted", "()I"));
    }

    [Fact]
    public async Task Class_initialization_blocks_a_second_real_thread_until_the_first_finishes()
    {
        var (state, registry, interpreter) = Session();
        var startGate = new ManualResetEventSlim(false);
        var t1Done = new ManualResetEventSlim(false);
        var t2Elapsed = TimeSpan.Zero;

        var t1 = Task.Run(() =>
        {
            startGate.Wait();
            interpreter.InvokeStaticExact("Lc/InitRunner;", "run", "()V");
            t1Done.Set();
        });
        var t2 = Task.Run(() =>
        {
            startGate.Wait();
            var watch = Stopwatch.StartNew();
            interpreter.InvokeStaticExact("Lc/InitRunner;", "run", "()V");
            watch.Stop();
            t2Elapsed = watch.Elapsed;
        });

        startGate.Set();
        await Task.WhenAll(t1, t2);

        // The <clinit> ran exactly once despite two threads triggering it.
        Assert.Equal(1, interpreter.InvokeStaticExact(Probe, "getStarted", "()I"));
        Assert.Equal(1, interpreter.InvokeStaticExact(Probe, "getFinished", "()I"));
        // The second thread genuinely blocked until the first finished (the <clinit>
        // sleeps 200ms mid-body; without the blocking fix it would return in ~0ms).
        Assert.True(t2Elapsed >= TimeSpan.FromMilliseconds(150), $"second trigger returned after {t2Elapsed.TotalMilliseconds}ms; did not block");
        Assert.True(t1Done.IsSet);
    }

    [Fact]
    public async Task Real_monitor_serializes_two_different_guest_threads()
    {
        var (state, registry, interpreter) = Session();
        var lockObject = new DexObject("Ljava/lang/Object;");
        var startGate = new ManualResetEventSlim(false);
        var t1Elapsed = TimeSpan.Zero;
        var t2Elapsed = TimeSpan.Zero;

        var t1 = Task.Run(() =>
        {
            startGate.Wait();
            var watch = Stopwatch.StartNew();
            interpreter.InvokeStaticExact("Lc/Lock;", "contend", "(Ljava/lang/Object;)V", lockObject);
            watch.Stop();
            t1Elapsed = watch.Elapsed;
        });
        var t2 = Task.Run(() =>
        {
            startGate.Wait();
            var watch = Stopwatch.StartNew();
            interpreter.InvokeStaticExact("Lc/Lock;", "contend", "(Ljava/lang/Object;)V", lockObject);
            watch.Stop();
            t2Elapsed = watch.Elapsed;
        });

        startGate.Set();
        await Task.WhenAll(t1, t2);

        // Each contend() entered and exited the monitor exactly once.
        Assert.Equal(2, interpreter.InvokeStaticExact(Probe, "getIn", "()I"));
        Assert.Equal(2, interpreter.InvokeStaticExact(Probe, "getOut", "()I"));
        // Real mutual exclusion: whichever thread finishes second must have waited
        // for the other to release the monitor (each holds it while sleeping 200ms),
        // so its elapsed time exceeds the first finisher's by roughly one sleep. A
        // no-op monitor would let both finish in ~200ms with no extra wait.
        double faster = Math.Min(t1Elapsed.TotalMilliseconds, t2Elapsed.TotalMilliseconds);
        double slower = Math.Max(t1Elapsed.TotalMilliseconds, t2Elapsed.TotalMilliseconds);
        Assert.True(slower >= faster + 150, $"monitor did not serialize: faster={faster}ms slower={slower}ms");
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
        // Probe static fields: started(0) I, finished(1) I, ran(2) I, threadObj(3) Object, completed(4) I, in(5) I, out(6) I.
        dex.Fields.Add(Field(Probe, "started", "I"));
        dex.Fields.Add(Field(Probe, "finished", "I"));
        dex.Fields.Add(Field(Probe, "ran", "I"));
        dex.Fields.Add(Field(Probe, "threadObj", "Ljava/lang/Object;"));
        dex.Fields.Add(Field(Probe, "completed", "I"));
        dex.Fields.Add(Field(Probe, "in", "I"));
        dex.Fields.Add(Field(Probe, "out", "I"));

        var probe = new DexClass { Descriptor = Probe, SuperclassDescriptor = "Ljava/lang/Object;" };
        probe.DirectMethods.Add(Method(Probe, "getStarted", "()I", 1, 0, [0x0060, 0x0000, 0x000f]));
        probe.DirectMethods.Add(Method(Probe, "getFinished", "()I", 1, 0, [0x0060, 0x0001, 0x000f]));
        probe.DirectMethods.Add(Method(Probe, "getRan", "()I", 1, 0, [0x0060, 0x0002, 0x000f]));
        probe.DirectMethods.Add(Method(Probe, "getThread", "()Ljava/lang/Object;", 1, 0, [0x0060, 0x0003, 0x0011]));
        probe.DirectMethods.Add(Method(Probe, "getCompleted", "()I", 1, 0, [0x0060, 0x0004, 0x000f]));
        probe.DirectMethods.Add(Method(Probe, "getIn", "()I", 1, 0, [0x0060, 0x0005, 0x000f]));
        probe.DirectMethods.Add(Method(Probe, "getOut", "()I", 1, 0, [0x0060, 0x0006, 0x000f]));
        probe.DirectMethods.Add(Method(Probe, "markRan", "()V", 1, 0, [0x1012, 0x0067, 0x0002, 0x000e]));
        probe.DirectMethods.Add(Method(Probe, "markFinished", "()V", 1, 0, [0x1012, 0x0067, 0x0001, 0x000e]));
        probe.DirectMethods.Add(Method(Probe, "markStarted", "()V", 1, 0, [0x1012, 0x0067, 0x0000, 0x000e]));
        probe.DirectMethods.Add(Method(Probe, "markCompleted", "()V", 1, 0, [0x1012, 0x0067, 0x0004, 0x000e]));
        probe.DirectMethods.Add(Method(Probe, "storeThread", "(Ljava/lang/Object;)V", 1, 1, [0x0067, 0x0003, 0x000e]));
        probe.DirectMethods.Add(Method(Probe, "markIn", "()V", 2, 0, [0x0060, 0x0005, 0x1112, 0x0090, 0x0100, 0x0067, 0x0005, 0x000e]));
        probe.DirectMethods.Add(Method(Probe, "markOut", "()V", 2, 0, [0x0060, 0x0006, 0x1112, 0x0090, 0x0100, 0x0067, 0x0006, 0x000e]));

        // Lc/Runner; run()V (INSTANCE): markRan; currentThread; storeThread; markFinished; return.
        var runner = new DexClass { Descriptor = "Lc/Runner;", SuperclassDescriptor = "Ljava/lang/Object;" };
        runner.DirectMethods.Add(Method("Lc/Runner;", "run", "()V", 1, 1,
        [
            0x0071, 0x0000, 0x0000,  // invoke-static Probe.markRan (idx 0)
            0x0071, 0x0001, 0x0000,  // invoke-static Thread.currentThread (idx 1)
            0x000c,                   // move-result-object v0
            0x1071, 0x0002, 0x0000,  // invoke-static Probe.storeThread(v0) (idx 2)
            0x0071, 0x0003, 0x0000,  // invoke-static Probe.markFinished (idx 3)
            0x000e                    // return-void
        ], isStatic: false));

        // Lc/Sleeper; run()V (INSTANCE): Thread.sleep(10000); Probe.markCompleted; return.
        var sleeper = new DexClass { Descriptor = "Lc/Sleeper;", SuperclassDescriptor = "Ljava/lang/Object;" };
        sleeper.DirectMethods.Add(Method("Lc/Sleeper;", "run", "()V", 2, 1,
        [
            0x0016, 0x2710,           // const-wide/16 v0, 10000
            0x2171, 0x0004, 0x0010,  // invoke-static Thread.sleep {v0,v1} (pool idx 4)
            0x0071, 0x0005, 0x0000,  // invoke-static Probe.markCompleted (pool idx 5)
            0x000e
        ], isStatic: false));

        // Lc/Init; <clinit>: markStarted; Thread.sleep(200); markFinished; return. trigger()V empty.
        var init = new DexClass { Descriptor = "Lc/Init;", SuperclassDescriptor = "Ljava/lang/Object;" };
        init.DirectMethods.Add(Method("Lc/Init;", "<clinit>", "()V", 2, 0,
        [
            0x0071, 0x0006, 0x0000,  // markStarted (pool idx 6)
            0x0016, 0x00C8,           // const-wide/16 v0, 200
            0x2171, 0x0004, 0x0010,  // Thread.sleep {v0,v1} (pool idx 4)
            0x0071, 0x0003, 0x0000,  // markFinished (pool idx 3)
            0x000e
        ]));
        init.DirectMethods.Add(Method("Lc/Init;", "trigger", "()V", 0, 0, [0x000e]));
        // Lc/InitRunner; run()V: invoke-static Init.trigger (pool idx 9); return — the
        // BYTECODE invoke is what triggers class initialization.
        var initRunner = new DexClass { Descriptor = "Lc/InitRunner;", SuperclassDescriptor = "Ljava/lang/Object;" };
        initRunner.DirectMethods.Add(Method("Lc/InitRunner;", "run", "()V", 0, 0, [0x0071, 0x0009, 0x0000, 0x000e]));

        // Lc/Lock; contend(Object): monitor-enter v2; markIn; sleep(200); markOut; monitor-exit v2; return.
        var lockCls = new DexClass { Descriptor = "Lc/Lock;", SuperclassDescriptor = "Ljava/lang/Object;" };
        lockCls.DirectMethods.Add(Method("Lc/Lock;", "contend", "(Ljava/lang/Object;)V", 3, 1,
        [
            0x021d,                   // monitor-enter v2
            0x0071, 0x0007, 0x0000,  // markIn (pool idx 7)
            0x0016, 0x00C8,           // const-wide/16 v0, 200
            0x2171, 0x0004, 0x0010,  // Thread.sleep {v0,v1} (pool idx 4)
            0x0071, 0x0008, 0x0000,  // markOut (pool idx 8)
            0x021e,                   // monitor-exit v2
            0x000e
        ]));

        dex.Classes.Add(probe);
        dex.Classes.Add(runner);
        dex.Classes.Add(sleeper);
        dex.Classes.Add(init);
        dex.Classes.Add(initRunner);
        dex.Classes.Add(lockCls);

        // Shared method reference pool (indices used by every invoke-static operand).
        dex.Methods.Add(Ref(Probe, "markRan", "()V"));                              // 0
        dex.Methods.Add(Ref(ThreadClass, "currentThread", "()Ljava/lang/Thread;")); // 1
        dex.Methods.Add(Ref(Probe, "storeThread", "(Ljava/lang/Object;)V"));        // 2
        dex.Methods.Add(Ref(Probe, "markFinished", "()V"));                         // 3
        dex.Methods.Add(Ref(ThreadClass, "sleep", "(J)V"));                         // 4
        dex.Methods.Add(Ref(Probe, "markCompleted", "()V"));                        // 5
        dex.Methods.Add(Ref(Probe, "markStarted", "()V"));                          // 6
        dex.Methods.Add(Ref(Probe, "markIn", "()V"));                               // 7
        dex.Methods.Add(Ref(Probe, "markOut", "()V"));                              // 8
        dex.Methods.Add(Ref("Lc/Init;", "trigger", "()V"));                         // 9

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

    private static object? Invoke(AndroidApiRegistry registry, AndroidFrameworkState state, string owner, string name, string descriptor, AndroidInvokeKind kind, params object[] args)
    {
        var api = new AndroidApiMethodId(owner, name, descriptor);
        var context = new AndroidApiSessionContext(state.SessionId, state.PackageName, state.ActivityDescriptor, default, () => true);
        return registry.Invoke(context, new AndroidApiCallSite("Ltest;->run()V", 0, api, api, kind), args);
    }

    private sealed class QuietLogSink : IAndroidLogSink { public int Info(AndroidLogEntry entry) => 1; }
}
