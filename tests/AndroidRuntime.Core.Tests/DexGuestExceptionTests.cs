using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.Tests;

public sealed class DexGuestExceptionTests
{
    private const string Owner = "Lexceptions/Probe;";

    [Fact]
    public void Throw_uses_first_assignable_handler_and_move_exception_receives_same_object()
    {
        var method = Method("caught", "()I", 2, [0x0022, 0, 0x0027, 0x0012, 0x000f, 0x010d, 0x7012, 0x000f, 0x010d, 0x3012, 0x000f]);
        method.Code.TryBlocks.Add(new DexTryBlock
        {
            StartAddress = 0,
            InstructionCount = 3,
            Handlers =
            [
                new DexExceptionHandler { TypeDescriptor = "Ljava/lang/Exception;", TargetAddress = 5 },
                new DexExceptionHandler { TypeDescriptor = "Ljava/lang/RuntimeException;", TargetAddress = 8 }
            ]
        });
        DexFile dex = File(method);
        dex.TypeDescriptors.Add("Lexceptions/CustomException;");
        dex.Classes.Add(new DexClass { Descriptor = "Lexceptions/CustomException;", SuperclassDescriptor = "Ljava/lang/RuntimeException;" });
        dex.BuildIndexes();

        Assert.Equal(7, new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build()).InvokeStaticExact(Owner, "caught", "()I"));
    }

    [Fact]
    public void Throw_null_is_catchable_as_NullPointerException_and_uncaught_is_sanitized()
    {
        var caught = Method("caughtNull", "()I", 2, [0x0012, 0x0027, 0x0012, 0x000f, 0x010d, 0x5012, 0x000f]);
        caught.Code.TryBlocks.Add(new DexTryBlock { StartAddress = 0, InstructionCount = 2, Handlers = [new DexExceptionHandler { TypeDescriptor = "Ljava/lang/NullPointerException;", TargetAddress = 4 }] });
        var uncaught = Method("uncaught", "()V", 1, [0x0022, 0, 0x0027, 0x000e]);
        DexFile dex = File(caught, uncaught);
        dex.TypeDescriptors.Add("Ljava/lang/IllegalStateException;");
        dex.BuildIndexes();
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Equal(5, interpreter.InvokeStaticExact(Owner, "caughtNull", "()I"));
        var error = Assert.Throws<UncaughtAndroidGuestException>(() => interpreter.InvokeStaticExact(Owner, "uncaught", "()V"));
        Assert.Equal("Ljava/lang/IllegalStateException;", error.TypeDescriptor);
        Assert.DoesNotContain("D:\\", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidOperationException>(() => new DexInterpreter(File(Method("badMove", "()V", 1, [0x000d, 0x000e])), new AndroidApiRegistryBuilder().Build()).InvokeStaticExact(Owner, "badMove", "()V"));
    }

    private static DexEncodedMethod Method(string name, string descriptor, int registers, ushort[] instructions) => new()
    {
        AccessFlags = DexConstants.ACC_STATIC,
        Method = Ref(name, descriptor),
        Code = new DexCodeItem { RegistersSize = registers, InsSize = 0, OutsSize = 4, Instructions = instructions }
    };

    private static DexFile File(params DexEncodedMethod[] methods)
    {
        var dex = new DexFile();
        var cls = new DexClass { Descriptor = Owner, SuperclassDescriptor = "Ljava/lang/Object;" };
        cls.DirectMethods.AddRange(methods); dex.Classes.Add(cls); dex.BuildIndexes(); return dex;
    }

    private static DexMethodRef Ref(string name, string descriptor)
    {
        int close = descriptor.IndexOf(')');
        return new DexMethodRef { ClassDescriptor = Owner, Name = name, Proto = new DexProto { ReturnType = descriptor[(close + 1)..] } };
    }
}
