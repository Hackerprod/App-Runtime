using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Focused interpreter tests for the float arithmetic family: cmpl/cmpg-float
/// (0x2d/0x2e), add/sub/mul/div/rem-float 23x (0xa6..0xaa), and the /2addr
/// in-place forms (0xc6..0xca). Float registers hold raw IEEE 754 bit patterns
/// boxed as int; args/returns convert at the method boundary like wide values.
/// </summary>
public sealed class DexFloatTests
{
    private const string Owner = "Lfloat/Probe;";

    [Fact]
    public void Float_binary_ops_23x_follow_ieee_arithmetic()
    {
        var dex = File(
            Method("add", "(FF)F", [0x00a6, 0x0100, 0x000f]),
            Method("sub", "(FF)F", [0x00a7, 0x0100, 0x000f]),
            Method("mul", "(FF)F", [0x00a8, 0x0100, 0x000f]),
            Method("div", "(FF)F", [0x00a9, 0x0100, 0x000f]),
            Method("rem", "(FF)F", [0x00aa, 0x0100, 0x000f]));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Equal(3.75f, interpreter.InvokeStaticExact(Owner, "add", "(FF)F", 1.5f, 2.25f));
        Assert.Equal(3.0f, interpreter.InvokeStaticExact(Owner, "sub", "(FF)F", 5.0f, 2.0f));
        Assert.Equal(3.0f, interpreter.InvokeStaticExact(Owner, "mul", "(FF)F", 1.5f, 2.0f));
        Assert.Equal(2.5f, interpreter.InvokeStaticExact(Owner, "div", "(FF)F", 5.0f, 2.0f));
        Assert.Equal(1.0f, interpreter.InvokeStaticExact(Owner, "rem", "(FF)F", 7.0f, 3.0f));
    }

    [Fact]
    public void Float_binary_ops_2addr_follow_ieee_arithmetic()
    {
        var dex = File(
            Method("add2", "(FF)F", [0x10c6, 0x000f]),
            Method("sub2", "(FF)F", [0x10c7, 0x000f]),
            Method("mul2", "(FF)F", [0x10c8, 0x000f]),
            Method("div2", "(FF)F", [0x10c9, 0x000f]),
            Method("rem2", "(FF)F", [0x10ca, 0x000f]));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Equal(3.75f, interpreter.InvokeStaticExact(Owner, "add2", "(FF)F", 1.5f, 2.25f));
        Assert.Equal(3.0f, interpreter.InvokeStaticExact(Owner, "sub2", "(FF)F", 5.0f, 2.0f));
        Assert.Equal(3.0f, interpreter.InvokeStaticExact(Owner, "mul2", "(FF)F", 1.5f, 2.0f));
        Assert.Equal(2.5f, interpreter.InvokeStaticExact(Owner, "div2", "(FF)F", 5.0f, 2.0f));
        Assert.Equal(1.0f, interpreter.InvokeStaticExact(Owner, "rem2", "(FF)F", 7.0f, 3.0f));
    }

    [Fact]
    public void Cmp_float_orders_and_handles_nan_like_java()
    {
        var dex = File(
            Method("cmpl", "(FF)I", [0x002d, 0x0100, 0x000f]),
            Method("cmpg", "(FF)I", [0x002e, 0x0100, 0x000f]));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Equal(-1, interpreter.InvokeStaticExact(Owner, "cmpl", "(FF)I", 1.0f, 2.0f));
        Assert.Equal(1, interpreter.InvokeStaticExact(Owner, "cmpl", "(FF)I", 2.0f, 1.0f));
        Assert.Equal(0, interpreter.InvokeStaticExact(Owner, "cmpl", "(FF)I", 1.0f, 1.0f));
        Assert.Equal(-1, interpreter.InvokeStaticExact(Owner, "cmpl", "(FF)I", float.NaN, 1.0f));
        Assert.Equal(1, interpreter.InvokeStaticExact(Owner, "cmpg", "(FF)I", float.NaN, 1.0f));
    }

    [Fact]
    public void Float_division_by_zero_follows_ieee_not_throw()
    {
        var dex = File(
            Method("div", "(FF)F", [0x00a9, 0x0100, 0x000f]),
            Method("zero", "(FF)F", [0x00a9, 0x0100, 0x000f]));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.True(float.IsPositiveInfinity((float)interpreter.InvokeStaticExact(Owner, "div", "(FF)F", 1.0f, 0.0f)!));
        Assert.True(float.IsNaN((float)interpreter.InvokeStaticExact(Owner, "zero", "(FF)F", 0.0f, 0.0f)!));
    }

    [Fact]
    public void Float_nan_propagates_through_arithmetic()
    {
        var dex = File(Method("add", "(FF)F", [0x00a6, 0x0100, 0x000f]));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.True(float.IsNaN((float)interpreter.InvokeStaticExact(Owner, "add", "(FF)F", float.NaN, 1.0f)!));
    }

    [Fact]
    public void Float_args_and_returns_round_trip_through_int_bits()
    {
        var dex = File(Method("identity", "(F)F", [0x000f]));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Equal(1.5f, interpreter.InvokeStaticExact(Owner, "identity", "(F)F", 1.5f));
        Assert.Equal(-0.0f, interpreter.InvokeStaticExact(Owner, "identity", "(F)F", -0.0f));
        Assert.True(float.IsNaN((float)interpreter.InvokeStaticExact(Owner, "identity", "(F)F", float.NaN)!));
    }

    [Fact]
    public void Neg_float_bit_flips_the_sign_including_zero_and_nan()
    {
        var dex = File(Method("neg", "(F)F", [0x007f, 0x000f]));
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Equal(-1.5f, interpreter.InvokeStaticExact(Owner, "neg", "(F)F", 1.5f));
        Assert.Equal(1.5f, interpreter.InvokeStaticExact(Owner, "neg", "(F)F", -1.5f));
        // -0.0f bits 0x80000000 flip to +0.0f bits 0x00000000; IEEE NaN stays NaN.
        Assert.Equal(0, BitConverter.SingleToInt32Bits((float)interpreter.InvokeStaticExact(Owner, "neg", "(F)F", -0.0f)!));
        Assert.Equal(int.MinValue, BitConverter.SingleToInt32Bits((float)interpreter.InvokeStaticExact(Owner, "neg", "(F)F", 0.0f)!));
        Assert.True(float.IsNaN((float)interpreter.InvokeStaticExact(Owner, "neg", "(F)F", float.NaN)!));
    }

    private static DexFile File(params DexEncodedMethod[] methods)
    {
        var dex = new DexFile();
        var cls = new DexClass { Descriptor = Owner, SuperclassDescriptor = "Ljava/lang/Object;" };
        cls.DirectMethods.AddRange(methods);
        dex.Classes.Add(cls);
        dex.BuildIndexes();
        return dex;
    }

    private static DexEncodedMethod Method(string name, string descriptor, ushort[] instructions)
    {
        int close = descriptor.IndexOf(')');
        int parameterWords = 0;
        for (int i = 1; i < close; i++)
            if (descriptor[i] is 'Z' or 'B' or 'S' or 'C' or 'I' or 'F') parameterWords++;
        return new DexEncodedMethod
        {
            AccessFlags = DexConstants.ACC_STATIC,
            Method = Ref(name, descriptor),
            Code = new DexCodeItem { RegistersSize = parameterWords, InsSize = parameterWords, OutsSize = 0, Instructions = instructions }
        };
    }

    private static DexMethodRef Ref(string name, string descriptor)
    {
        int close = descriptor.IndexOf(')');
        var parameters = new List<string>();
        for (int i = 1; i < close; i++) parameters.Add(descriptor[i].ToString());
        return new DexMethodRef { ClassDescriptor = Owner, Name = name, Proto = new DexProto { ReturnType = descriptor[(close + 1)..], ParameterTypes = parameters } };
    }
}
