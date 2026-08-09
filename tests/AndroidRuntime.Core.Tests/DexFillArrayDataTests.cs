using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Focused interpreter tests for fill-array-data (0x26, 31t, payload kind 3):
/// fills an EXISTING primitive array (created by new-array) from packed initializer
/// bytes; no allocation, no result, no branch — always falls through.
/// </summary>
public sealed class DexFillArrayDataTests
{
    private const string Owner = "Larrayfill/Probe;";

    [Fact]
    public void Fill_int_array_from_payload_via_set()
    {
        // new int[3]; fill {1,2,3}; return a[2] == 3.
        // payload at pc 16: ident 0x0300, width 4, size 3, data units 0x0100/0x0200/0x0300.
        var dex = DexWithArrayType("[I",
            Method("third", "()I", 3, 0,
            [
                0x3112, 0x1023, 0x0000, 0x0026, 0x000D, 0x0000, 0x2112, 0x0244, 0x0100, 0x020f,
                0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
                0x0300, 0x0004, 0x0003, 0x0000, 0x0001, 0x0000, 0x0002, 0x0000, 0x0003, 0x0000
            ]));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Equal(3, interpreter.InvokeStaticExact(Owner, "third", "()I"));
    }

    [Fact]
    public void Fill_long_array_from_payload_via_set_wide()
    {
        // new long[2]; fill {0x1122334455667788L, 0xDEADBEEFCAFEBABEL}; return a[1].
        var dex = DexWithArrayType("[J",
            Method("second", "()J", 4, 0,
            [
                0x3112, 0x1023, 0x0000, 0x0026, 0x000D, 0x0000, 0x1112, 0x0245, 0x0100, 0x0210,
                0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
                0x0300, 0x0008, 0x0002, 0x0000, 0x7788, 0x5566, 0x3344, 0x1122, 0xBABE, 0xCAFE, 0xBEEF, 0xDEAD
            ]));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Equal(unchecked((long)0xDEADBEEFCAFEBABEL), interpreter.InvokeStaticExact(Owner, "second", "()J"));
    }

    [Fact]
    public void Fill_short_payload_into_larger_array_fills_only_the_first_elements()
    {
        // new int[3]; fill size 1 {42}; return a[0] == 42 (a[1]/a[2] untouched).
        var dex = DexWithArrayType("[I",
            Method("first", "()I", 3, 0,
            [
                0x3112, 0x1023, 0x0000, 0x0026, 0x000D, 0x0000, 0x0112, 0x0244, 0x0100, 0x020f,
                0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
                0x0300, 0x0004, 0x0001, 0x0000, 0x002A, 0x0000
            ]));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Equal(42, interpreter.InvokeStaticExact(Owner, "first", "()I"));
    }

    [Fact]
    public void Fill_element_width_mismatch_fails_closed()
    {
        // int[] array but payload element_width 8 — malformed DEX.
        var dex = DexWithArrayType("[I",
            Method("bad", "()I", 3, 0,
            [
                0x3112, 0x1023, 0x0000, 0x0026, 0x000D, 0x0000, 0x0112, 0x0244, 0x0100, 0x020f,
                0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000,
                0x0300, 0x0008, 0x0001, 0x0000, 0x0100, 0x0000
            ]));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Throws<InvalidOperationException>(() => interpreter.InvokeStaticExact(Owner, "bad", "()I"));
    }

    [Fact]
    public void Fill_null_array_register_throws_typed_null_pointer_exception()
    {
        var dex = DexWithArrayType("[I",
            Method("nullFill", "()V", 1, 0, [0x0026, 0x0005, 0x0000, 0x000e, 0x0300, 0x0004, 0x0001, 0x0000, 0x0000, 0x0000]));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Equal("Ljava/lang/NullPointerException;", Assert.Throws<UncaughtAndroidGuestException>(() => interpreter.InvokeStaticExact(Owner, "nullFill", "()V")).TypeDescriptor);
    }

    private static DexFile DexWithArrayType(string arrayType, params DexEncodedMethod[] methods)
    {
        var dex = new DexFile();
        dex.TypeDescriptors.Add(arrayType); // index 0, used by new-array/fill-array-data
        var cls = new DexClass { Descriptor = Owner, SuperclassDescriptor = "Ljava/lang/Object;" };
        cls.DirectMethods.AddRange(methods);
        dex.Classes.Add(cls);
        dex.BuildIndexes();
        return dex;
    }

    private static DexEncodedMethod Method(string name, string descriptor, int registers, int ins, ushort[] instructions) => new()
    {
        AccessFlags = DexConstants.ACC_STATIC,
        Method = Ref(Owner, name, descriptor),
        Code = new DexCodeItem { RegistersSize = registers, InsSize = ins, OutsSize = 0, Instructions = instructions }
    };

    private static DexMethodRef Ref(string owner, string name, string descriptor)
    {
        int close = descriptor.IndexOf(')');
        return new DexMethodRef { ClassDescriptor = owner, Name = name, Proto = new DexProto { ReturnType = descriptor[(close + 1)..], ParameterTypes = [] } };
    }
}
