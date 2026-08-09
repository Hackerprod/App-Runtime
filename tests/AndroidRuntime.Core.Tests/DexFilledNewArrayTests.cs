using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Focused interpreter tests for filled-new-array (0x24, 35c) and
/// filled-new-array/range (0x25, 3rc): allocate an array of the type at the
/// type-id operand, length = argument count, fill from the listed registers in
/// order, publish the array for a following move-result-object.
/// </summary>
public sealed class DexFilledNewArrayTests
{
    private const string Owner = "Lexample/ArrayProbe;";

    [Fact]
    public void Filled_new_array_builds_an_array_from_listed_registers_in_order()
    {
        // static int first(int,int,int): registers 6, ins 3 -> args v3,v4,v5; temps v0,v1,v2.
        // filled-new-array {v3,v4,v5}, type@0 ([I); move-result-object v0;
        // const/4 v1,#0; aget v2,v0,v1; return v2. "third" reads element 2 instead.
        var dex = DexWithTypeAndMethods("[I",
            Method("first", "(III)I", 6, 3, [0x3524, 0x0000, 0x0543, 0x000c, 0x0112, 0x0244, 0x0100, 0x020f]),
            Method("third", "(III)I", 6, 3, [0x3524, 0x0000, 0x0543, 0x000c, 0x2112, 0x0244, 0x0100, 0x020f]));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Equal(42, interpreter.InvokeStaticExact(Owner, "first", "(III)I", 42, 7, 9));
        Assert.Equal(99, interpreter.InvokeStaticExact(Owner, "third", "(III)I", 1, 2, 99));
    }

    [Fact]
    public void Filled_new_array_range_builds_an_array_from_a_contiguous_register_range()
    {
        // static int firstRange(int,int,int): registers 6, ins 3 -> args v3,v4,v5.
        // filled-new-array/range {v3..v5}, type@0; move-result-object v0;
        // const/4 v1,#0; aget v2,v0,v1; return v2.
        var dex = DexWithTypeAndMethods("[I",
            Method("firstRange", "(III)I", 6, 3, [0x0325, 0x0000, 0x0003, 0x000c, 0x0112, 0x0244, 0x0100, 0x020f]));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Equal(42, interpreter.InvokeStaticExact(Owner, "firstRange", "(III)I", 42, 7, 9));
    }

    [Fact]
    public void Filled_new_array_range_supports_more_than_five_elements()
    {
        // static int last(int,int,int,int,int,int): registers 9, ins 6 -> args v3..v8;
        // temps v0,v1,v2. filled-new-array/range {v3..v8}; move-result-object v0;
        // const/4 v1,#5; aget v2,v0,v1; return v2.
        var dex = DexWithTypeAndMethods("[I",
            Method("last", "(IIIIII)I", 9, 6, [0x0625, 0x0000, 0x0003, 0x000c, 0x5112, 0x0244, 0x0100, 0x020f]));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Equal(99, interpreter.InvokeStaticExact(Owner, "last", "(IIIIII)I", 1, 2, 3, 4, 5, 99));
    }

    [Fact]
    public void Filled_new_array_with_wide_component_type_fails_closed()
    {
        // static int bad(int): registers 2, ins 1 -> arg v1. filled-new-array {v1},
        // type@0 ([J) — DexArray.Set rejects wide elements.
        var dex = DexWithTypeAndMethods("[J",
            Method("bad", "(I)I", 2, 1, [0x1124, 0x0000, 0x0001, 0x000c, 0x000f]));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Throws<InvalidOperationException>(() => interpreter.InvokeStaticExact(Owner, "bad", "(I)I", 1));
    }

    [Fact]
    public void Filled_new_array_result_may_be_discarded_without_move_result()
    {
        // Legal DEX: filled-new-array {v0,v1} (ins=2 -> args v0,v1), then return-void.
        var dex = DexWithTypeAndMethods("[I",
            Method("discard", "(II)V", 2, 2, [0x2124, 0x0000, 0x0010, 0x000e]));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Null(interpreter.InvokeStaticExact(Owner, "discard", "(II)V", 1, 2));
    }

    private static DexFile DexWithTypeAndMethods(string elementArrayType, params DexEncodedMethod[] methods)
    {
        var dex = new DexFile();
        dex.TypeDescriptors.Add(elementArrayType); // index 0, used by filled-new-array
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
        var parameters = new List<string>();
        for (int index = 1; index < close;)
        {
            int start = index;
            if (descriptor[index] == 'L') index = descriptor.IndexOf(';', index) + 1; else index++;
            parameters.Add(descriptor[start..index]);
        }
        return new DexMethodRef { ClassDescriptor = owner, Name = name, Proto = new DexProto { ReturnType = descriptor[(close + 1)..], ParameterTypes = parameters } };
    }
}
