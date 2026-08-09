using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

public sealed class DexDispatchAndArithmeticTests
{
    private const string Base = "Lexample/Base;";
    private const string Child = "Lexample/Child;";
    private const string Caller = "Lexample/Caller;";

    [Fact]
    public void Virtual_dispatch_uses_dynamic_override()
    {
        var baseFoo = Constant(Base, "foo", 1);
        var childFoo = Constant(Child, "foo", 2);
        var virtualBase = CallerMethod("callBase", "(Lexample/Base;)I", 0x106e, baseFoo.Method);
        var dex = DexWith(
            new DexClass { Descriptor = Base, SuperclassDescriptor = "Ljava/lang/Object;", VirtualMethods = { baseFoo } },
            new DexClass { Descriptor = Child, SuperclassDescriptor = Base, VirtualMethods = { childFoo } },
            CallerClass(virtualBase));
        dex.Methods.Add(baseFoo.Method);
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Equal(2, interpreter.InvokeStaticExact(Caller, "callBase", "(Lexample/Base;)I", new DexObject(Child)));
    }

    [Fact]
    public void Virtual_child_callsite_finds_inherited_base_method_when_child_has_no_override()
    {
        var baseFoo = Constant(Base, "foo", 1);
        var inheritedChildRef = Ref(Child, "foo", "()I");
        var inherited = CallerMethod("callInherited", "(Lexample/Child;)I", 0x106e, inheritedChildRef);
        var dex = DexWith(
            new DexClass { Descriptor = Base, SuperclassDescriptor = "Ljava/lang/Object;", VirtualMethods = { baseFoo } },
            new DexClass { Descriptor = Child, SuperclassDescriptor = Base },
            CallerClass(inherited));
        dex.Methods.Add(inheritedChildRef);
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Equal(1, interpreter.InvokeStaticExact(Caller, "callInherited", "(Lexample/Child;)I", new DexObject(Child)));
    }

    [Fact]
    public void Invoke_super_starts_at_callers_superclass_and_does_not_redispatch()
    {
        var baseFoo = Constant(Base, "foo", 1);
        var childFoo = Constant(Child, "foo", 2);
        var superCall = InstanceCaller(Child, "callSuper", 0x106f, baseFoo.Method);
        var dex = DexWith(
            new DexClass { Descriptor = Base, SuperclassDescriptor = "Ljava/lang/Object;", VirtualMethods = { baseFoo } },
            new DexClass { Descriptor = Child, SuperclassDescriptor = Base, VirtualMethods = { childFoo, superCall } });
        dex.Methods.Add(baseFoo.Method);
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Equal(1, interpreter.InvokeInstanceExact(new DexObject(Child), "callSuper", "()I"));
    }

    [Fact]
    public void Direct_and_static_dispatch_use_the_exact_target()
    {
        var baseFoo = Constant(Base, "foo", 1);
        var childFoo = Constant(Child, "foo", 2);
        var direct = CallerMethod("callDirect", "(Lexample/Child;)I", 0x1070, baseFoo.Method);
        var directDex = DexWith(
            new DexClass { Descriptor = Base, SuperclassDescriptor = "Ljava/lang/Object;", DirectMethods = { baseFoo } },
            new DexClass { Descriptor = Child, SuperclassDescriptor = Base, DirectMethods = { childFoo } },
            CallerClass(direct));
        directDex.Methods.Add(baseFoo.Method);
        Assert.Equal(1, new DexInterpreter(directDex, new AndroidApiRegistryBuilder().Build())
            .InvokeStaticExact(Caller, "callDirect", "(Lexample/Child;)I", new DexObject(Child)));

        var baseStatic = Constant(Base, "staticFoo", 3, isStatic: true);
        var childStatic = Constant(Child, "staticFoo", 4, isStatic: true);
        var staticCall = StaticCaller("callStatic", baseStatic.Method);
        var staticDex = DexWith(
            new DexClass { Descriptor = Base, SuperclassDescriptor = "Ljava/lang/Object;", DirectMethods = { baseStatic } },
            new DexClass { Descriptor = Child, SuperclassDescriptor = Base, DirectMethods = { childStatic } },
            CallerClass(staticCall));
        staticDex.Methods.Add(baseStatic.Method);
        Assert.Equal(3, new DexInterpreter(staticDex, new AndroidApiRegistryBuilder().Build())
            .InvokeStaticExact(Caller, "callStatic", "()I"));
    }

    [Fact]
    public void Virtual_null_receiver_fails_before_dispatch_and_framework_fallback_walks_to_context()
    {
        var getPackage = Ref(Child, "getPackageName", "()Ljava/lang/String;");
        var call = CallerMethod("packageName", "(Lexample/Child;)Ljava/lang/String;", 0x106e, getPackage, objectResult: true);
        var dex = DexWith(
            new DexClass { Descriptor = Child, SuperclassDescriptor = "Landroid/app/Activity;" },
            CallerClass(call));
        dex.Methods.Add(getPackage);
        var activity = new DexObject(Child);
        var peers = new ActivityWindowPeers();
        var state = new AndroidFrameworkState("s", "org.example", Child, peers);
        state.AttachActivity(activity);
        var context = new AndroidApiSessionContext("s", "org.example", Child, default, () => true);
        var interpreter = new DexInterpreter(dex, AndroidApiBindings.CreateBuilder(state, new PositiveLogSink()).Build(), apiSession: context);

        Assert.Equal("org.example", interpreter.InvokeStaticExact(Caller, "packageName", "(Lexample/Child;)Ljava/lang/String;", activity));
        Assert.Equal("Ljava/lang/NullPointerException;", Assert.Throws<UncaughtAndroidGuestException>(() => interpreter.InvokeStaticExact(Caller, "packageName", "(Lexample/Child;)Ljava/lang/String;", (object)null!)).TypeDescriptor);
    }

    [Theory]
    [InlineData("div", "(II)I")]
    [InlineData("div2addr", "(II)I")]
    [InlineData("divLit", "(I)I")]
    public void Integer_division_min_value_by_minus_one_wraps_to_min_value(string name, string descriptor)
    {
        var dex = DexWith(CallerClass(Division(name)));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());
        object[] args = name == "divLit" ? [int.MinValue] : [int.MinValue, -1];
        Assert.Equal(int.MinValue, interpreter.InvokeStaticExact(Caller, name, descriptor, args));
    }

    [Fact]
    public void Integer_division_by_zero_throws_typed_guest_arithmetic_error()
    {
        var dex = DexWith(CallerClass(Division("div")));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());
        Assert.Equal("Ljava/lang/ArithmeticException;", Assert.Throws<UncaughtAndroidGuestException>(() => interpreter.InvokeStaticExact(Caller, "div", "(II)I", 1, 0)).TypeDescriptor);
    }

    [Fact]
    public void Monitor_enter_exit_on_valid_object_is_a_noop_and_preserves_register()
    {
        // static Object munge(Object o): monitor-enter v0, monitor-exit v0, return-object v0.
        var method = new DexEncodedMethod
        {
            AccessFlags = DexConstants.ACC_STATIC,
            Method = Ref(Caller, "munge", "(Ljava/lang/Object;)Ljava/lang/Object;"),
            Code = new DexCodeItem { RegistersSize = 1, InsSize = 1, OutsSize = 0, Instructions = [0x001d, 0x001e, 0x0011] }
        };
        var dex = DexWith(CallerClass(method));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());
        var value = new DexObject("Ljava/lang/Object;");

        Assert.Same(value, interpreter.InvokeStaticExact(Caller, "munge", "(Ljava/lang/Object;)Ljava/lang/Object;", value));
    }

    [Fact]
    public void Monitor_enter_on_null_throws_typed_null_pointer_exception()
    {
        var method = new DexEncodedMethod
        {
            AccessFlags = DexConstants.ACC_STATIC,
            Method = Ref(Caller, "enterNull", "(Ljava/lang/Object;)V"),
            Code = new DexCodeItem { RegistersSize = 1, InsSize = 1, OutsSize = 0, Instructions = [0x001d, 0x000e] }
        };
        var dex = DexWith(CallerClass(method));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Equal("Ljava/lang/NullPointerException;", Assert.Throws<UncaughtAndroidGuestException>(() => interpreter.InvokeStaticExact(Caller, "enterNull", "(Ljava/lang/Object;)V", (object)null!)).TypeDescriptor);
    }

    [Fact]
    public void Monitor_exit_on_null_throws_typed_null_pointer_exception()
    {
        var method = new DexEncodedMethod
        {
            AccessFlags = DexConstants.ACC_STATIC,
            Method = Ref(Caller, "exitNull", "(Ljava/lang/Object;)V"),
            Code = new DexCodeItem { RegistersSize = 1, InsSize = 1, OutsSize = 0, Instructions = [0x001e, 0x000e] }
        };
        var dex = DexWith(CallerClass(method));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Equal("Ljava/lang/NullPointerException;", Assert.Throws<UncaughtAndroidGuestException>(() => interpreter.InvokeStaticExact(Caller, "exitNull", "(Ljava/lang/Object;)V", (object)null!)).TypeDescriptor);
    }

    private static DexEncodedMethod Constant(string owner, string name, int value, bool isStatic = false) => new()
    {
        AccessFlags = isStatic ? DexConstants.ACC_STATIC : 0,
        Method = Ref(owner, name, "()I"),
        Code = new DexCodeItem { RegistersSize = 1, InsSize = isStatic ? 0 : 1, Instructions = [(ushort)(0x0012 | (value << 12)), 0x000f] }
    };

    private static DexEncodedMethod StaticCaller(string name, DexMethodRef target) => new()
    {
        AccessFlags = DexConstants.ACC_STATIC,
        Method = Ref(Caller, name, "()I"),
        Code = new DexCodeItem { RegistersSize = 1, OutsSize = 0, Instructions = [0x0071, 0, 0, 0x000a, 0x000f] }
    };

    private static DexEncodedMethod CallerMethod(string name, string descriptor, ushort invoke, DexMethodRef target, bool objectResult = false) => new()
    {
        AccessFlags = DexConstants.ACC_STATIC,
        Method = Ref(Caller, name, descriptor),
        Code = new DexCodeItem { RegistersSize = 1, InsSize = 1, OutsSize = 1, Instructions = [invoke, 0, 0, (ushort)(objectResult ? 0x000c : 0x000a), (ushort)(objectResult ? 0x0011 : 0x000f)] }
    };

    private static DexEncodedMethod InstanceCaller(string owner, string name, ushort invoke, DexMethodRef target) => new()
    {
        Method = Ref(owner, name, "()I"),
        Code = new DexCodeItem { RegistersSize = 1, InsSize = 1, OutsSize = 1, Instructions = [invoke, 0, 0, 0x000a, 0x000f] }
    };

    private static DexEncodedMethod Division(string name) => name switch
    {
        "div" => new() { AccessFlags = DexConstants.ACC_STATIC, Method = Ref(Caller, name, "(II)I"), Code = new DexCodeItem { RegistersSize = 2, InsSize = 2, Instructions = [0x0093, 0x0100, 0x000f] } },
        "div2addr" => new() { AccessFlags = DexConstants.ACC_STATIC, Method = Ref(Caller, name, "(II)I"), Code = new DexCodeItem { RegistersSize = 2, InsSize = 2, Instructions = [0x10b3, 0x000f] } },
        _ => new() { AccessFlags = DexConstants.ACC_STATIC, Method = Ref(Caller, name, "(I)I"), Code = new DexCodeItem { RegistersSize = 1, InsSize = 1, Instructions = [0x00db, 0xff00, 0x000f] } }
    };

    private static DexClass CallerClass(params DexEncodedMethod[] methods)
    {
        var cls = new DexClass { Descriptor = Caller, SuperclassDescriptor = "Ljava/lang/Object;" };
        cls.DirectMethods.AddRange(methods);
        return cls;
    }

    private static DexFile DexWith(params DexClass[] classes) { var dex = new DexFile(); dex.Classes.AddRange(classes); dex.BuildIndexes(); return dex; }
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

    private sealed class PositiveLogSink : IAndroidLogSink { public int Info(AndroidLogEntry entry) => 1; }
}
