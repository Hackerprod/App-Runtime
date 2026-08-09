using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for java.lang.Integer/Long bit-manipulation statics against the REAL
/// Java SE 17 contract (verified from the docs): highestOneBit/lowestOneBit
/// return a single-one-bit VALUE (not an index); the zero-count methods return
/// the full width for zero; rotateLeft/Right wrap bits around; bitCount is the
/// population count; toBinaryString/toHexString are unsigned, no prefix, no
/// leading zeros, lowercase.
/// </summary>
public sealed class BitOpsTests
{
    private const string Integer = "Ljava/lang/Integer;";
    private const string Long = "Ljava/lang/Long;";

    [Fact]
    public void Highest_and_lowest_one_bit_return_a_value_not_an_index()
    {
        var (state, registry, _) = Session();
        // highestOneBit(0x0000_8000) = 0x0000_8000 (the highest set bit VALUE).
        Assert.Equal(0x8000, Invoke(registry, state, Integer, "highestOneBit", "(I)I", AndroidInvokeKind.Static, 0x8000));
        Assert.Equal(int.MinValue, Invoke(registry, state, Integer, "highestOneBit", "(I)I", AndroidInvokeKind.Static, unchecked((int)0x8000_0001)));
        Assert.Equal(0, Invoke(registry, state, Integer, "highestOneBit", "(I)I", AndroidInvokeKind.Static, 0));
        Assert.Equal(int.MinValue, Invoke(registry, state, Integer, "highestOneBit", "(I)I", AndroidInvokeKind.Static, -1)); // top bit
        // lowestOneBit(0x0000_8000) = 0x0000_8000.
        Assert.Equal(1, Invoke(registry, state, Integer, "lowestOneBit", "(I)I", AndroidInvokeKind.Static, 0x101));
        Assert.Equal(0, Invoke(registry, state, Integer, "lowestOneBit", "(I)I", AndroidInvokeKind.Static, 0));
        Assert.Equal(1, Invoke(registry, state, Integer, "lowestOneBit", "(I)I", AndroidInvokeKind.Static, -1));
    }

    [Fact]
    public void Long_highest_and_lowest_one_bit_follow_the_same_contract()
    {
        var (state, registry, _) = Session();
        Assert.Equal(0x1_0000_0000L, Invoke(registry, state, Long, "highestOneBit", "(J)J", AndroidInvokeKind.Static, 0x1_8000_0000L));
        Assert.Equal(0L, Invoke(registry, state, Long, "highestOneBit", "(J)J", AndroidInvokeKind.Static, 0L));
        Assert.Equal(long.MinValue, Invoke(registry, state, Long, "highestOneBit", "(J)J", AndroidInvokeKind.Static, -1L));
        Assert.Equal(8L, Invoke(registry, state, Long, "lowestOneBit", "(J)J", AndroidInvokeKind.Static, 0x108L));
        Assert.Equal(1L, Invoke(registry, state, Long, "lowestOneBit", "(J)J", AndroidInvokeKind.Static, -1L));
    }

    [Fact]
    public void Zero_count_methods_return_the_full_width_for_zero()
    {
        var (state, registry, _) = Session();
        Assert.Equal(32, Invoke(registry, state, Integer, "numberOfLeadingZeros", "(I)I", AndroidInvokeKind.Static, 0));
        Assert.Equal(32, Invoke(registry, state, Integer, "numberOfTrailingZeros", "(I)I", AndroidInvokeKind.Static, 0));
        Assert.Equal(0, Invoke(registry, state, Integer, "numberOfLeadingZeros", "(I)I", AndroidInvokeKind.Static, int.MinValue));
        Assert.Equal(31, Invoke(registry, state, Integer, "numberOfLeadingZeros", "(I)I", AndroidInvokeKind.Static, 1));
        Assert.Equal(3, Invoke(registry, state, Integer, "numberOfTrailingZeros", "(I)I", AndroidInvokeKind.Static, 8));
        Assert.Equal(64, Invoke(registry, state, Long, "numberOfLeadingZeros", "(J)I", AndroidInvokeKind.Static, 0L));
        Assert.Equal(64, Invoke(registry, state, Long, "numberOfTrailingZeros", "(J)I", AndroidInvokeKind.Static, 0L));
        Assert.Equal(0, Invoke(registry, state, Long, "numberOfLeadingZeros", "(J)I", AndroidInvokeKind.Static, long.MinValue));
    }

    [Fact]
    public void Rotate_left_and_right_wrap_bits_around()
    {
        var (state, registry, _) = Session();
        // rotateLeft(0x8000_0000, 1) = 0x0000_0001 (top bit wraps to bottom).
        Assert.Equal(1, Invoke(registry, state, Integer, "rotateLeft", "(II)I", AndroidInvokeKind.Static, unchecked((int)0x8000_0000), 1));
        // rotateRight(1, 1) = 0x8000_0000.
        Assert.Equal(unchecked((int)0x8000_0000), Invoke(registry, state, Integer, "rotateRight", "(II)I", AndroidInvokeKind.Static, 1, 1));
        // Distance masked to 31 (rotate by 32 == no-op, by 33 == by 1).
        Assert.Equal(0x12345678, Invoke(registry, state, Integer, "rotateLeft", "(II)I", AndroidInvokeKind.Static, 0x12345678, 32));
        Assert.Equal(0x2468ACF0, Invoke(registry, state, Integer, "rotateLeft", "(II)I", AndroidInvokeKind.Static, 0x12345678, 33));
        // Long: rotateLeft(long.MinValue, 1) = 1; rotateRight(1, 1) = long.MinValue.
        Assert.Equal(1L, Invoke(registry, state, Long, "rotateLeft", "(JI)J", AndroidInvokeKind.Static, long.MinValue, 1));
        Assert.Equal(long.MinValue, Invoke(registry, state, Long, "rotateRight", "(JI)J", AndroidInvokeKind.Static, 1L, 1));
        Assert.Equal(0x123456789ABCDEFL, Invoke(registry, state, Long, "rotateLeft", "(JI)J", AndroidInvokeKind.Static, 0x123456789ABCDEFL, 64));
    }

    [Fact]
    public void Bit_count_is_the_population_count()
    {
        var (state, registry, _) = Session();
        Assert.Equal(0, Invoke(registry, state, Integer, "bitCount", "(I)I", AndroidInvokeKind.Static, 0));
        Assert.Equal(3, Invoke(registry, state, Integer, "bitCount", "(I)I", AndroidInvokeKind.Static, 0b1011));
        Assert.Equal(32, Invoke(registry, state, Integer, "bitCount", "(I)I", AndroidInvokeKind.Static, -1));
        Assert.Equal(3, Invoke(registry, state, Long, "bitCount", "(J)I", AndroidInvokeKind.Static, 0b1011L));
        Assert.Equal(64, Invoke(registry, state, Long, "bitCount", "(J)I", AndroidInvokeKind.Static, -1L));
    }

    [Fact]
    public void Integer_signum_is_minus_one_zero_one()
    {
        var (state, registry, _) = Session();
        Assert.Equal(-1, Invoke(registry, state, Integer, "signum", "(I)I", AndroidInvokeKind.Static, -42));
        Assert.Equal(0, Invoke(registry, state, Integer, "signum", "(I)I", AndroidInvokeKind.Static, 0));
        Assert.Equal(1, Invoke(registry, state, Integer, "signum", "(I)I", AndroidInvokeKind.Static, 42));
    }

    [Fact]
    public void To_binary_string_and_to_hex_string_are_unsigned_no_prefix_no_leading_zeros()
    {
        var (state, registry, _) = Session();
        // Integer.toHexString(-1) = "ffffffff" (unsigned, lowercase, no prefix).
        Assert.Equal("ffffffff", Invoke(registry, state, Integer, "toHexString", "(I)Ljava/lang/String;", AndroidInvokeKind.Static, -1));
        Assert.Equal("80000000", Invoke(registry, state, Integer, "toHexString", "(I)Ljava/lang/String;", AndroidInvokeKind.Static, int.MinValue));
        Assert.Equal("ff", Invoke(registry, state, Integer, "toHexString", "(I)Ljava/lang/String;", AndroidInvokeKind.Static, 255));
        Assert.Equal("0", Invoke(registry, state, Integer, "toHexString", "(I)Ljava/lang/String;", AndroidInvokeKind.Static, 0));
        // Integer.toBinaryString(-1) = 32 ones.
        Assert.Equal(new string('1', 32), Invoke(registry, state, Integer, "toBinaryString", "(I)Ljava/lang/String;", AndroidInvokeKind.Static, -1));
        Assert.Equal("1011", Invoke(registry, state, Integer, "toBinaryString", "(I)Ljava/lang/String;", AndroidInvokeKind.Static, 0b1011));
        // Long: 64 bits for -1.
        Assert.Equal(new string('f', 16), Invoke(registry, state, Long, "toHexString", "(J)Ljava/lang/String;", AndroidInvokeKind.Static, -1L));
        Assert.Equal(new string('1', 64), Invoke(registry, state, Long, "toBinaryString", "(J)Ljava/lang/String;", AndroidInvokeKind.Static, -1L));
        Assert.Equal("10", Invoke(registry, state, Long, "toBinaryString", "(J)Ljava/lang/String;", AndroidInvokeKind.Static, 2L));
    }

    [Fact]
    public void Unreferenced_bit_ops_remain_unbuilt()
    {
        // Per strict scope, only the probe-referenced surface is built; the rest
        // of the classifier's static-method list stays deferred (reverse,
        // reverseBytes, toOctalString, toUnsignedString, divideUnsigned, etc.).
        var (state, registry, _) = Session();
        Assert.False(registry.Contains(new AndroidApiMethodId(Integer, "reverse", "(I)I")));
        Assert.False(registry.Contains(new AndroidApiMethodId(Integer, "reverseBytes", "(I)I")));
        Assert.False(registry.Contains(new AndroidApiMethodId(Integer, "toOctalString", "(I)Ljava/lang/String;")));
        Assert.False(registry.Contains(new AndroidApiMethodId(Integer, "toUnsignedString", "(I)Ljava/lang/String;")));
        Assert.False(registry.Contains(new AndroidApiMethodId(Integer, "divideUnsigned", "(II)I")));
        Assert.False(registry.Contains(new AndroidApiMethodId(Integer, "remainderUnsigned", "(II)I")));
    }

    private static (AndroidFrameworkState State, AndroidApiRegistry Registry, DexInterpreter Interpreter) Session()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new QuietLogSink()).Build();
        var dex = new DexFile();
        var interpreter = new DexInterpreter(dex, registry, gil: state.Gil);
        state.Gil = interpreter.Gil;
        state.AttachInterpreter(interpreter);
        return (state, registry, interpreter);
    }

    private static object Invoke(AndroidApiRegistry registry, AndroidFrameworkState state, string owner, string name, string descriptor, AndroidInvokeKind kind, params object[] args)
    {
        var api = new AndroidApiMethodId(owner, name, descriptor);
        var context = new AndroidApiSessionContext(state.SessionId, state.PackageName, state.ActivityDescriptor, default, () => true);
        if (state.Interpreter is not null) context.IsTypeAssignable = state.Interpreter.IsGuestTypeAssignable;
        return registry.Invoke(context, new AndroidApiCallSite("Ltest;->run()V", 0, api, api, kind), args);
    }

    private sealed class QuietLogSink : IAndroidLogSink { public int Info(AndroidLogEntry entry) => 1; }
}
