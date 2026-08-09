using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for kotlin.Lazy (`by lazy {}`): the Function0 initializer runs exactly
/// once across multiple getValue()/property accesses, the cached value is the
/// SAME object on subsequent accesses, and isInitialized() reflects computed
/// state before/after the first access.
/// </summary>
public sealed class LazyTests
{
    private const string LazyKt = "Lkotlin/LazyKt;";
    private const string LazyClass = "Lkotlin/Lazy;";

    [Fact]
    public void Lazy_initializer_runs_exactly_once_and_caches_the_same_object()
    {
        var (state, registry, interpreter) = Session();
        var lazy = (DexObject)Invoke(registry, state, LazyKt, "lazy", "(Lkotlin/jvm/functions/Function0;)Lkotlin/Lazy;", AndroidInvokeKind.Static, new DexObject("Lc/Init;"));

        var first = Invoke(registry, state, LazyClass, "getValue", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, lazy);
        Assert.Equal("lazy-result", first);
        var second = Invoke(registry, state, LazyClass, "getValue", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, lazy);
        var third = Invoke(registry, state, LazyClass, "getValue", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, lazy);
        Assert.Same(first, second);
        Assert.Same(first, third);
        // The initializer ran exactly once despite three accesses.
        Assert.Equal(1, interpreter.InvokeStaticExact("Lc/Init;", "getCount", "()I"));
    }

    [Fact]
    public void Is_initialized_reflects_computed_state()
    {
        var (state, registry, _) = Session();
        var lazy = (DexObject)Invoke(registry, state, LazyKt, "lazy", "(Lkotlin/jvm/functions/Function0;)Lkotlin/Lazy;", AndroidInvokeKind.Static, new DexObject("Lc/Init;"));
        Assert.Equal(0, Invoke(registry, state, LazyClass, "isInitialized", "()Z", AndroidInvokeKind.Virtual, lazy));
        Invoke(registry, state, LazyClass, "getValue", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, lazy);
        Assert.Equal(1, Invoke(registry, state, LazyClass, "isInitialized", "()Z", AndroidInvokeKind.Virtual, lazy));
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
        dex.Strings.Add("lazy-result");
        dex.Fields.Add(Field("Lc/Init;", "count", "I"));

        // Lc/Init; is the guest Function0: invoke()Ljava/lang/Object; increments a
        // static counter (observable proof of how many times it ran) and returns a
        // stable string.
        var init = new DexClass { Descriptor = "Lc/Init;", SuperclassDescriptor = "Ljava/lang/Object;" };
        init.DirectMethods.Add(Method("Lc/Init;", "invoke", "()Ljava/lang/Object;", 3, 1,
        [
            0x0060, 0x0000,          // sget v0, count (field 0)
            0x1112,                   // const/4 v1, 1
            0x0090, 0x0100,          // add-int v0, v0, v1
            0x0067, 0x0000,          // sput v0, count
            0x021a, 0x0000,          // const-string v2, "lazy-result" (string idx 0)
            0x0211                    // return-object v2
        ], isStatic: false));
        init.DirectMethods.Add(Method("Lc/Init;", "getCount", "()I", 1, 0, [0x0060, 0x0000, 0x000f]));

        dex.Classes.Add(init);
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
        if (state.Interpreter is not null) context.IsTypeAssignable = state.Interpreter.IsGuestTypeAssignable;
        return registry.Invoke(context, new AndroidApiCallSite("Ltest;->run()V", 0, api, api, kind), args);
    }

    private sealed class QuietLogSink : IAndroidLogSink { public int Info(AndroidLogEntry entry) => 1; }
}
