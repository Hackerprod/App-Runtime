using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.Tests;

public sealed class DexIdentityAndBudgetTests
{
    private const string ClassName = "Lexample/Overloads;";

    [Fact]
    public void Exact_lookup_uses_the_complete_descriptor_not_shorty()
    {
        var objectOverload = Method("pick", "(Ljava/lang/Object;)I", 2);
        var stringOverload = Method("pick", "(Ljava/lang/String;)I", 1);
        var dex = DexWith(objectOverload, stringOverload);

        Assert.Same(stringOverload, dex.FindMethodExact(ClassName, "pick", "(Ljava/lang/String;)I"));
        Assert.Same(objectOverload, dex.FindMethodExact(ClassName, "pick", "(Ljava/lang/Object;)I"));
    }

    [Fact]
    public void Internal_invoke_resolves_overloads_by_complete_descriptor()
    {
        var objectOverload = Method("pick", "(Ljava/lang/Object;)I", 2);
        var stringOverload = Method("pick", "(Ljava/lang/String;)I", 1);
        var targetRef = stringOverload.Method;
        var caller = new DexEncodedMethod
        {
            AccessFlags = DexConstants.ACC_STATIC,
            Method = Ref("call", "(Ljava/lang/String;)I"),
            Code = new DexCodeItem
            {
                RegistersSize = 1,
                InsSize = 1,
                OutsSize = 1,
                Instructions = [0x1071, 0x0000, 0x0000, 0x000A, 0x000F]
            }
        };
        var dex = DexWith(objectOverload, stringOverload, caller);
        dex.Methods.Add(targetRef);

        var result = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build()).InvokeStaticExact(
            ClassName, "call", "(Ljava/lang/String;)I", "value");

        Assert.Equal(1, result);
    }

    [Fact]
    public void Step_budget_is_reset_for_each_root_invocation()
    {
        var dex = DexWith(Method("constant", "()I", 1));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build(), maxStepsPerInvocation: 2);

        Assert.Equal(1, interpreter.InvokeStaticExact(ClassName, "constant", "()I"));
        Assert.Equal(1, interpreter.InvokeStaticExact(ClassName, "constant", "()I"));
    }

    [Fact]
    public void Instance_invocation_passes_the_receiver_and_uses_exact_descriptor()
    {
        var method = new DexEncodedMethod
        {
            Method = Ref("set", "(I)V"),
            Code = new DexCodeItem
            {
                RegistersSize = 2,
                InsSize = 2,
                Instructions = [0x0159, 0x0000, 0x000E]
            }
        };
        var dex = DexWith(method);
        dex.Fields.Add(new DexFieldRef { ClassDescriptor = ClassName, Name = "value", Type = "I" });
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());
        var instance = interpreter.CreateInstance(ClassName);

        interpreter.InvokeInstanceExact(instance, "set", "(I)V", 41);

        Assert.Equal(41, instance.InstanceFields["value"]);
    }

    [Fact]
    public void Iput_short_round_trips_through_the_field_like_the_rest_of_the_family()
    {
        // iput-short v1, v0, field@0 stores; iget-short v0, v1, field@0 reads it back.
        // Raw store, no subtype truncation — matching the whole iget/iput family.
        var set = new DexEncodedMethod
        {
            Method = Ref("set", "(S)V"),
            Code = new DexCodeItem { RegistersSize = 2, InsSize = 2, Instructions = [0x015f, 0x0000, 0x000e] }
        };
        var get = new DexEncodedMethod
        {
            Method = Ref("get", "()S"),
            Code = new DexCodeItem { RegistersSize = 2, InsSize = 1, Instructions = [0x1058, 0x0000, 0x000f] }
        };
        var dex = DexWith(set, get);
        dex.Fields.Add(new DexFieldRef { ClassDescriptor = ClassName, Name = "value", Type = "S" });
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());
        var instance = interpreter.CreateInstance(ClassName);

        interpreter.InvokeInstanceExact(instance, "set", "(S)V", (short)-1234);

        Assert.Equal((short)-1234, interpreter.InvokeInstanceExact(instance, "get", "()S"));
        Assert.Equal((short)-1234, instance.InstanceFields["value"]);
    }

    [Fact]
    public void Interpreter_checks_session_cancellation_before_executing_bytecode()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new AndroidApiSessionContext(
            "cancelled",
            "example",
            ClassName,
            cancellation.Token,
            () => true);
        var interpreter = new DexInterpreter(
            DexWith(Method("constant", "()I", 1)),
            new AndroidApiRegistryBuilder().Build(),
            apiSession: context);

        Assert.Throws<OperationCanceledException>(() =>
            interpreter.InvokeStaticExact(ClassName, "constant", "()I"));
    }

    [Fact]
    public void Reference_equality_branches_support_null_and_identity_without_integer_conversion()
    {
        var eq = BranchMethod("referenceEq", "(Ljava/lang/Object;Ljava/lang/Object;)I", 0x1032);
        var eqz = BranchMethod("referenceEqz", "(Ljava/lang/Object;)I", 0x0038);
        var interpreter = new DexInterpreter(DexWith(eq, eqz), new AndroidApiRegistryBuilder().Build());
        var same = new DexObject("Ljava/lang/Object;");

        Assert.Equal(1, interpreter.InvokeStaticExact(ClassName, "referenceEq", "(Ljava/lang/Object;Ljava/lang/Object;)I", same, same));
        Assert.Equal(0, interpreter.InvokeStaticExact(ClassName, "referenceEq", "(Ljava/lang/Object;Ljava/lang/Object;)I", same, new DexObject("Ljava/lang/Object;")));
        Assert.Equal(1, interpreter.InvokeStaticExact(ClassName, "referenceEqz", "(Ljava/lang/Object;)I", (object)null!));
        Assert.Equal(0, interpreter.InvokeStaticExact(ClassName, "referenceEqz", "(Ljava/lang/Object;)I", same));
    }

    private static DexEncodedMethod BranchMethod(string name, string descriptor, ushort branch) => new()
    {
        AccessFlags = DexConstants.ACC_STATIC,
        Method = Ref(name, descriptor),
        Code = new DexCodeItem
        {
            RegistersSize = descriptor.Count(character => character == ';'),
            InsSize = descriptor.Count(character => character == ';'),
            Instructions = [branch, 0x0004, 0x0012, 0x000f, 0x1012, 0x000f]
        }
    };

    private static DexFile DexWith(params DexEncodedMethod[] methods)
    {
        var dex = new DexFile();
        var cls = new DexClass { Descriptor = ClassName };
        cls.DirectMethods.AddRange(methods);
        dex.Classes.Add(cls);
        dex.BuildIndexes();
        return dex;
    }

    private static DexEncodedMethod Method(string name, string descriptor, int value) => new()
    {
        AccessFlags = DexConstants.ACC_STATIC,
        Method = Ref(name, descriptor),
        Code = new DexCodeItem
        {
            RegistersSize = 1,
            InsSize = descriptor == "()I" ? 0 : 1,
            Instructions = [(ushort)(0x0012 | (value << 12)), 0x000F]
        }
    };

    private static DexMethodRef Ref(string name, string descriptor)
    {
        int close = descriptor.IndexOf(')');
        string returnType = descriptor[(close + 1)..];
        var parameters = new List<string>();
        for (int index = 1; index < close;)
        {
            int start = index;
            while (descriptor[index] == '[') index++;
            if (descriptor[index] == 'L') index = descriptor.IndexOf(';', index) + 1; else index++;
            parameters.Add(descriptor[start..index]);
        }
        return new DexMethodRef
        {
            ClassDescriptor = ClassName,
            Name = name,
            Proto = new DexProto
            {
                Shorty = "IL",
                ReturnType = returnType,
                ParameterTypes = parameters
            }
        };
    }
}
