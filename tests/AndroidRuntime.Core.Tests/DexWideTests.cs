using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.Tests;

public sealed class DexWideTests
{
    private const string Owner = "Lwide/Probe;";

    [Fact]
    public void Const_move_arithmetic_and_range_words_preserve_wide_values()
    {
        var add = Method("add", "(JJ)J", 4, 4, [0x009b, 0x0200, 0x0010]);
        var nested = Method("nested", "(JJ)J", 4, 4, [0x0477, 0, 0, 0x000b, 0x0010]);
        var overlap = Method("overlap", "(J)J", 3, 2, [0x1004, 0x0010]);
        var constant = Method("constant", "()J", 2, 0, [0x0018, 0x7788, 0x5566, 0x3344, 0x1122, 0x0010]);
        var dex = File(add, nested, overlap, constant);
        dex.Methods.Add(add.Method);
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Equal(0x1122334455667788L, interpreter.InvokeStaticExact(Owner, "constant", "()J"));
        Assert.Equal(long.MinValue, interpreter.InvokeStaticExact(Owner, "add", "(JJ)J", long.MaxValue, 1L));
        Assert.Equal(12L, interpreter.InvokeStaticExact(Owner, "nested", "(JJ)J", 5L, 7L));
        Assert.Equal(0x1020304050607080L, interpreter.InvokeStaticExact(Owner, "overlap", "(J)J", 0x1020304050607080L));
    }

    [Fact]
    public void Double_ieee_and_api_wide_returns_are_descriptor_aware()
    {
        var add = Method("addDouble", "(DD)D", 4, 4, [0x00ab, 0x0200, 0x0010]);
        var dex = File(add);
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());
        Assert.Equal(3.75d, interpreter.InvokeStaticExact(Owner, "addDouble", "(DD)D", 1.5d, 2.25d));

        var longApi = new AndroidApiMethodId("Lapi/Wide;", "now", "()J");
        var doubleApi = new AndroidApiMethodId("Lapi/Wide;", "ratio", "()D");
        var registry = new AndroidApiRegistryBuilder().Register(longApi, (_, _) => 5_000_000_000L).Register(doubleApi, (_, _) => -0.0d).Build();
        var session = new AndroidApiSessionContext("s", "p", "La;", default, () => true);
        Assert.Equal(5_000_000_000L, registry.Invoke(session, new("Lc;->m()V", 0, longApi, longApi, AndroidInvokeKind.Static), []));
        Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0d), BitConverter.DoubleToInt64Bits((double)registry.Invoke(session, new("Lc;->m()V", 0, doubleApi, doubleApi, AndroidInvokeKind.Static), [])));
    }

    [Fact]
    public void Invalid_half_pairs_result_placement_frame_bounds_and_invoke_word_counts_fail_closed()
    {
        Assert.Throws<InvalidOperationException>(() => Run(Method("half", "()J", 2, 0, [0x0010])));
        Assert.Throws<InvalidOperationException>(() => Run(Method("result", "()J", 2, 0, [0x000b, 0x0010])));
        Assert.Throws<InvalidOperationException>(() => Run(Method("bounds", "()J", 1, 0, [0x0016, 1, 0x0010])));
        var target = Method("target", "(J)J", 2, 2, [0x0010]);
        var caller = Method("badInvoke", "(J)J", 2, 2, [0x0177, 0, 0, 0x000b, 0x0010]);
        var dex = File(target, caller); dex.Methods.Add(target.Method);
        Assert.Throws<InvalidOperationException>(() => new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build()).InvokeStaticExact(Owner, "badInvoke", "(J)J", 1L));
    }

    [Fact]
    public void All_wide_constant_and_move_encodings_preserve_raw_bits()
    {
        var c16 = Method("c16", "()J", 2, 0, [0x0016, 0xfffe, 0x0010]);
        var c32 = Method("c32", "()J", 2, 0, [0x0017, 0xcdef, 0x89ab, 0x0010]);
        var high = Method("high", "()J", 2, 0, [0x0019, 0x8000, 0x0010]);
        var move16 = Method("move16", "(J)J", 258, 2, [0x0006, 0, 256, 0x0010]);
        var dex = File(c16, c32, high, move16);
        var interpreter = new DexInterpreter(dex, new AndroidApiRegistryBuilder().Build());

        Assert.Equal(-2L, interpreter.InvokeStaticExact(Owner, "c16", "()J"));
        Assert.Equal(unchecked((long)0xffffffff89abcdefUL), interpreter.InvokeStaticExact(Owner, "c32", "()J"));
        Assert.Equal(long.MinValue, interpreter.InvokeStaticExact(Owner, "high", "()J"));
        Assert.Equal(0x123456789abcdef0L, interpreter.InvokeStaticExact(Owner, "move16", "(J)J", 0x123456789abcdef0L));
    }

    [Fact]
    public void Long_division_remainder_shifts_and_compare_follow_java_edges()
    {
        var div = Method("div", "(JJ)J", 4, 4, [0x009e, 0x0200, 0x0010]);
        var rem = Method("rem", "(JJ)J", 4, 4, [0x009f, 0x0200, 0x0010]);
        var div2 = Method("div2", "(JJ)J", 4, 4, [0x20be, 0x0010]);
        var shl = Method("shl", "(JI)J", 3, 3, [0x00a3, 0x0200, 0x0010]);
        var cmp = Method("cmp", "(JJ)I", 4, 4, [0x0031, 0x0200, 0x000f]);
        var interpreter = new DexInterpreter(File(div, rem, div2, shl, cmp), new AndroidApiRegistryBuilder().Build());

        Assert.Equal(long.MinValue, interpreter.InvokeStaticExact(Owner, "div", "(JJ)J", long.MinValue, -1L));
        Assert.Equal(0L, interpreter.InvokeStaticExact(Owner, "rem", "(JJ)J", long.MinValue, -1L));
        Assert.Equal(long.MinValue, interpreter.InvokeStaticExact(Owner, "div2", "(JJ)J", long.MinValue, -1L));
        Assert.Equal(2L, interpreter.InvokeStaticExact(Owner, "shl", "(JI)J", 1L, 65));
        Assert.Equal(-1, interpreter.InvokeStaticExact(Owner, "cmp", "(JJ)I", -2L, 1L));
        Assert.Equal("Ljava/lang/ArithmeticException;", Assert.Throws<UncaughtAndroidGuestException>(() => interpreter.InvokeStaticExact(Owner, "div", "(JJ)J", 1L, 0L)).TypeDescriptor);
    }

    private static object Run(DexEncodedMethod method) => new DexInterpreter(File(method), new AndroidApiRegistryBuilder().Build()).InvokeStaticExact(Owner, method.Method.Name, method.Method.Proto.Descriptor());

    private static DexEncodedMethod Method(string name, string descriptor, int registers, int ins, ushort[] instructions) => new()
    {
        AccessFlags = DexConstants.ACC_STATIC,
        Method = Ref(name, descriptor),
        Code = new DexCodeItem { RegistersSize = registers, InsSize = ins, OutsSize = 4, Instructions = instructions }
    };

    private static DexFile File(params DexEncodedMethod[] methods)
    {
        var dex = new DexFile();
        var cls = new DexClass { Descriptor = Owner, SuperclassDescriptor = "Ljava/lang/Object;" };
        cls.DirectMethods.AddRange(methods); dex.Classes.Add(cls); dex.BuildIndexes(); return dex;
    }

    private static DexMethodRef Ref(string name, string descriptor)
    {
        int close = descriptor.IndexOf(')'); var parameters = new List<string>();
        for (int i = 1; i < close;) { string p = descriptor[i].ToString(); parameters.Add(p); i++; }
        return new DexMethodRef { ClassDescriptor = Owner, Name = name, Proto = new DexProto { ReturnType = descriptor[(close + 1)..], ParameterTypes = parameters } };
    }
}
