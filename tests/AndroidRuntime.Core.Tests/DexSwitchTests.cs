using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Focused interpreter tests for packed-switch (0x2b) and sparse-switch (0x2c).
/// Bytecode is hand-assembled; payload targets are signed offsets in code units
/// relative to the switch instruction's own pc. Fall-through (no matching case)
/// continues at pc + 3 past the 3-unit 31t instruction.
/// </summary>
public sealed class DexSwitchTests
{
    private const string Owner = "Lexample/SwitchProbe;";

    // packed-switch v0, +21: keys 10..13 -> return 7/9/11/13; anything else falls
    // through to return 5 (const/16, since 9+ overflows const/4's signed nibble).
    // Payload at pc 21 (ident 0x0100, size 4, first_key 10).
    private static readonly ushort[] PackedMethod =
    [
        0x002B, 0x0015, 0x0000,
        0x0013, 0x0005, 0x000F, 0x0013, 0x0007, 0x000F, 0x0013, 0x0009, 0x000F,
        0x0013, 0x000B, 0x000F, 0x0013, 0x000D, 0x000F, 0x0013, 0x000F, 0x000F,
        0x0100, 0x0004, 0x000A, 0x0000,
        0x0006, 0x0000, 0x0009, 0x0000, 0x000C, 0x0000, 0x000F, 0x0000
    ];

    // sparse-switch v0, +21: keys 100/200/300 -> return 7/9/11; anything else falls
    // through to return 5. Payload at pc 21 (ident 0x0200, size 3).
    private static readonly ushort[] SparseMethod =
    [
        0x002C, 0x0015, 0x0000,
        0x0013, 0x0005, 0x000F, 0x0013, 0x0007, 0x000F, 0x0013, 0x0009, 0x000F,
        0x0013, 0x000B, 0x000F, 0x0013, 0x000D, 0x000F, 0x0013, 0x000F, 0x000F,
        0x0200, 0x0003,
        0x0064, 0x0000, 0x00C8, 0x0000, 0x012C, 0x0000,
        0x0006, 0x0000, 0x0009, 0x0000, 0x000C, 0x0000
    ];

    [Fact]
    public void Packed_switch_matching_key_jumps_to_the_correct_target()
    {
        var interpreter = Interpreter(PackedMethod);

        Assert.Equal(7, interpreter.InvokeStaticExact(Owner, "packed", "(I)I", 10));
        Assert.Equal(9, interpreter.InvokeStaticExact(Owner, "packed", "(I)I", 11));
        Assert.Equal(11, interpreter.InvokeStaticExact(Owner, "packed", "(I)I", 12));
        Assert.Equal(13, interpreter.InvokeStaticExact(Owner, "packed", "(I)I", 13));
    }

    [Fact]
    public void Packed_switch_non_matching_key_falls_through()
    {
        var interpreter = Interpreter(PackedMethod);

        Assert.Equal(5, interpreter.InvokeStaticExact(Owner, "packed", "(I)I", 0));
        Assert.Equal(5, interpreter.InvokeStaticExact(Owner, "packed", "(I)I", 9));
        Assert.Equal(5, interpreter.InvokeStaticExact(Owner, "packed", "(I)I", 14));
    }

    [Fact]
    public void Sparse_switch_matching_key_jumps_to_the_correct_target()
    {
        var interpreter = Interpreter(SparseMethod);

        Assert.Equal(7, interpreter.InvokeStaticExact(Owner, "sparse", "(I)I", 100));
        Assert.Equal(9, interpreter.InvokeStaticExact(Owner, "sparse", "(I)I", 200));
        Assert.Equal(11, interpreter.InvokeStaticExact(Owner, "sparse", "(I)I", 300));
    }

    [Fact]
    public void Sparse_switch_non_matching_key_falls_through()
    {
        var interpreter = Interpreter(SparseMethod);

        Assert.Equal(5, interpreter.InvokeStaticExact(Owner, "sparse", "(I)I", 50));
        Assert.Equal(5, interpreter.InvokeStaticExact(Owner, "sparse", "(I)I", 150));
        Assert.Equal(5, interpreter.InvokeStaticExact(Owner, "sparse", "(I)I", 999));
    }

    [Fact]
    public void Sparse_switch_unsorted_keys_fail_closed()
    {
        // Keys 300, 100 (descending) violate the spec's ascending requirement.
        ushort[] instructions =
        [
            0x002C, 0x0004, 0x0000,
            0x000E,
            0x0200, 0x0002,
            0x012C, 0x0000, 0x0064, 0x0000,
            0x0003, 0x0000, 0x0003, 0x0000
        ];

        Assert.Throws<InvalidOperationException>(() => Interpreter(instructions).InvokeStaticExact(Owner, "sparse", "(I)I", 100));
    }

    [Fact]
    public void Switch_with_wrong_payload_ident_fails_closed()
    {
        // packed-switch offset lands on a sparse payload ident (0x0200).
        ushort[] instructions =
        [
            0x002B, 0x0004, 0x0000,
            0x000E,
            0x0200, 0x0001, 0x0007, 0x0000, 0x0003, 0x0000
        ];

        Assert.Throws<InvalidOperationException>(() => Interpreter(instructions).InvokeStaticExact(Owner, "packed", "(I)I", 7));
    }

    private static DexInterpreter Interpreter(ushort[] instructions)
    {
        var method = new DexEncodedMethod
        {
            AccessFlags = DexConstants.ACC_STATIC,
            Method = Ref(Owner, instructions[0] == 0x002B ? "packed" : "sparse", "(I)I"),
            Code = new DexCodeItem { RegistersSize = 1, InsSize = 1, OutsSize = 0, Instructions = instructions }
        };
        var dex = new DexFile();
        var cls = new DexClass { Descriptor = Owner, SuperclassDescriptor = "Ljava/lang/Object;" };
        cls.DirectMethods.Add(method);
        dex.Classes.Add(cls);
        dex.BuildIndexes();
        return new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());
    }

    private static DexMethodRef Ref(string owner, string name, string descriptor)
    {
        int close = descriptor.IndexOf(')');
        return new DexMethodRef { ClassDescriptor = owner, Name = name, Proto = new DexProto { ReturnType = descriptor[(close + 1)..], ParameterTypes = ["I"] } };
    }
}
