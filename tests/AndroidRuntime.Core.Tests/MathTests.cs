using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for java.lang.Math against the REAL Java SE 17 contract, with
/// particular care on the two verified quirks: min/max float/double NaN
/// propagation and -0.0-vs-+0.0 handling, and round's half-up-toward-positive-
/// infinity rule (NOT .NET's banker's rounding). Plus abs overflow wrapping,
/// the simple pass-throughs, and the special-case clamps.
/// </summary>
public sealed class MathTests
{
    private const string Math = "Ljava/lang/Math;";

    [Fact]
    public void Min_int_and_long_are_plain_minimums()
    {
        var (state, registry, _) = Session();
        Assert.Equal(3, Invoke(registry, state, Math, "min", "(II)I", AndroidInvokeKind.Static, 3, 7));
        Assert.Equal(-5, Invoke(registry, state, Math, "min", "(II)I", AndroidInvokeKind.Static, 3, -5));
        Assert.Equal(3L, Invoke(registry, state, Math, "min", "(JJ)J", AndroidInvokeKind.Static, 3L, 7L));
    }

    [Fact]
    public void Min_double_propagates_nan_and_treats_negative_zero_as_smaller()
    {
        var (state, registry, _) = Session();
        // NaN propagates.
        Assert.True(double.IsNaN((double)Invoke(registry, state, Math, "min", "(DD)D", AndroidInvokeKind.Static, double.NaN, 5.0)!));
        Assert.True(double.IsNaN((double)Invoke(registry, state, Math, "min", "(DD)D", AndroidInvokeKind.Static, 5.0, double.NaN)!));
        // Negative zero is STRICTLY smaller than positive zero (verified from docs).
        double result = (double)Invoke(registry, state, Math, "min", "(DD)D", AndroidInvokeKind.Static, -0.0, 0.0)!;
        Assert.Equal(0.0, result);
        Assert.True(BitConverter.DoubleToInt64Bits(result) == unchecked((long)0x8000000000000000L)); // -0.0 bit pattern
        // Normal minimum.
        Assert.Equal(2.5, (double)Invoke(registry, state, Math, "min", "(DD)D", AndroidInvokeKind.Static, 2.5, 9.0)!);
    }

    [Fact]
    public void Min_float_propagates_nan_and_treats_negative_zero_as_smaller()
    {
        var (state, registry, _) = Session();
        Assert.True(float.IsNaN((float)Invoke(registry, state, Math, "min", "(FF)F", AndroidInvokeKind.Static, float.NaN, 5.0f)!));
        float result = (float)Invoke(registry, state, Math, "min", "(FF)F", AndroidInvokeKind.Static, -0.0f, 0.0f)!;
        Assert.Equal(0.0f, result);
        Assert.True(BitConverter.SingleToInt32Bits(result) == unchecked((int)0x80000000)); // -0.0f bit pattern
        Assert.Equal(1.5f, (float)Invoke(registry, state, Math, "min", "(FF)F", AndroidInvokeKind.Static, 1.5f, 8.0f)!);
    }

    [Fact]
    public void Max_double_propagates_nan_and_treats_positive_zero_as_larger()
    {
        var (state, registry, _) = Session();
        Assert.True(double.IsNaN((double)Invoke(registry, state, Math, "max", "(DD)D", AndroidInvokeKind.Static, double.NaN, 5.0)!));
        // max(-0.0, +0.0) = +0.0 (positive zero is the max).
        double result = (double)Invoke(registry, state, Math, "max", "(DD)D", AndroidInvokeKind.Static, -0.0, 0.0)!;
        Assert.Equal(0.0, result);
        Assert.True(BitConverter.DoubleToInt64Bits(result) == 0L); // +0.0 bit pattern
        Assert.Equal(9.0, (double)Invoke(registry, state, Math, "max", "(DD)D", AndroidInvokeKind.Static, 2.5, 9.0)!);
    }

    [Fact]
    public void Max_float_propagates_nan_and_treats_positive_zero_as_larger()
    {
        var (state, registry, _) = Session();
        Assert.True(float.IsNaN((float)Invoke(registry, state, Math, "max", "(FF)F", AndroidInvokeKind.Static, 5.0f, float.NaN)!));
        float result = (float)Invoke(registry, state, Math, "max", "(FF)F", AndroidInvokeKind.Static, -0.0f, 0.0f)!;
        Assert.Equal(0.0f, result);
        Assert.True(BitConverter.SingleToInt32Bits(result) == 0); // +0.0f
        Assert.Equal(8.0f, (float)Invoke(registry, state, Math, "max", "(FF)F", AndroidInvokeKind.Static, 1.5f, 8.0f)!);
    }

    [Fact]
    public void Round_float_half_up_toward_positive_infinity()
    {
        var (state, registry, _) = Session();
        // The half-up rule (NOT banker's rounding): 2.5 -> 3, 3.5 -> 4, -2.5 -> -2.
        Assert.Equal(3, Invoke(registry, state, Math, "round", "(F)I", AndroidInvokeKind.Static, 2.5f));
        Assert.Equal(4, Invoke(registry, state, Math, "round", "(F)I", AndroidInvokeKind.Static, 3.5f));
        Assert.Equal(-2, Invoke(registry, state, Math, "round", "(F)I", AndroidInvokeKind.Static, -2.5f)); // toward +inf, NOT -3
        Assert.Equal(2, Invoke(registry, state, Math, "round", "(F)I", AndroidInvokeKind.Static, 2.4f));
        // NaN -> 0; infinity / out-of-range clamps.
        Assert.Equal(0, Invoke(registry, state, Math, "round", "(F)I", AndroidInvokeKind.Static, float.NaN));
        Assert.Equal(int.MinValue, Invoke(registry, state, Math, "round", "(F)I", AndroidInvokeKind.Static, float.NegativeInfinity));
        Assert.Equal(int.MaxValue, Invoke(registry, state, Math, "round", "(F)I", AndroidInvokeKind.Static, float.PositiveInfinity));
    }

    [Fact]
    public void Round_double_half_up_toward_positive_infinity()
    {
        var (state, registry, _) = Session();
        Assert.Equal(3L, Invoke(registry, state, Math, "round", "(D)J", AndroidInvokeKind.Static, 2.5));
        Assert.Equal(-2L, Invoke(registry, state, Math, "round", "(D)J", AndroidInvokeKind.Static, -2.5)); // toward +inf
        Assert.Equal(0L, Invoke(registry, state, Math, "round", "(D)J", AndroidInvokeKind.Static, double.NaN));
        Assert.Equal(long.MinValue, Invoke(registry, state, Math, "round", "(D)J", AndroidInvokeKind.Static, double.NegativeInfinity));
        Assert.Equal(long.MaxValue, Invoke(registry, state, Math, "round", "(D)J", AndroidInvokeKind.Static, double.PositiveInfinity));
    }

    [Fact]
    public void Abs_int_and_long_wrap_on_overflow_like_java()
    {
        var (state, registry, _) = Session();
        Assert.Equal(5, Invoke(registry, state, Math, "abs", "(I)I", AndroidInvokeKind.Static, -5));
        Assert.Equal(int.MinValue, Invoke(registry, state, Math, "abs", "(I)I", AndroidInvokeKind.Static, int.MinValue)); // wraps, no throw
        Assert.Equal(5L, Invoke(registry, state, Math, "abs", "(J)J", AndroidInvokeKind.Static, -5L));
        Assert.Equal(long.MinValue, Invoke(registry, state, Math, "abs", "(J)J", AndroidInvokeKind.Static, long.MinValue));
    }

    [Fact]
    public void Simple_pass_throughs_match_system_math()
    {
        var (state, registry, _) = Session();
        Assert.Equal(System.Math.Sqrt(16.0), (double)Invoke(registry, state, Math, "sqrt", "(D)D", AndroidInvokeKind.Static, 16.0)!);
        Assert.Equal(System.Math.Pow(2.0, 10.0), (double)Invoke(registry, state, Math, "pow", "(DD)D", AndroidInvokeKind.Static, 2.0, 10.0)!);
        Assert.Equal(System.Math.Floor(2.7), (double)Invoke(registry, state, Math, "floor", "(D)D", AndroidInvokeKind.Static, 2.7)!);
        Assert.Equal(System.Math.Ceiling(2.1), (double)Invoke(registry, state, Math, "ceil", "(D)D", AndroidInvokeKind.Static, 2.1)!);
        Assert.Equal(System.Math.Exp(1.0), (double)Invoke(registry, state, Math, "exp", "(D)D", AndroidInvokeKind.Static, 1.0)!);
        Assert.Equal(System.Math.Log(System.Math.E), (double)Invoke(registry, state, Math, "log", "(D)D", AndroidInvokeKind.Static, System.Math.E)!);
        Assert.Equal(System.Math.Log10(100.0), (double)Invoke(registry, state, Math, "log10", "(D)D", AndroidInvokeKind.Static, 100.0)!);
        Assert.Equal(System.Math.Sin(1.0), (double)Invoke(registry, state, Math, "sin", "(D)D", AndroidInvokeKind.Static, 1.0)!);
        Assert.Equal(System.Math.Cos(1.0), (double)Invoke(registry, state, Math, "cos", "(D)D", AndroidInvokeKind.Static, 1.0)!);
        Assert.Equal(System.Math.Tan(1.0), (double)Invoke(registry, state, Math, "tan", "(D)D", AndroidInvokeKind.Static, 1.0)!);
        Assert.Equal(System.Math.Atan2(1.0, 1.0), (double)Invoke(registry, state, Math, "atan2", "(DD)D", AndroidInvokeKind.Static, 1.0, 1.0)!);
        Assert.Equal(System.Math.Sqrt(3.0 * 3.0 + 4.0 * 4.0), (double)Invoke(registry, state, Math, "hypot", "(DD)D", AndroidInvokeKind.Static, 3.0, 4.0)!);
        Assert.Equal(System.Math.Cbrt(27.0), (double)Invoke(registry, state, Math, "cbrt", "(D)D", AndroidInvokeKind.Static, 27.0)!);
    }

    [Fact]
    public void Rint_rounds_half_to_even()
    {
        var (state, registry, _) = Session();
        Assert.Equal(2.0, (double)Invoke(registry, state, Math, "rint", "(D)D", AndroidInvokeKind.Static, 2.5)!);
        Assert.Equal(4.0, (double)Invoke(registry, state, Math, "rint", "(D)D", AndroidInvokeKind.Static, 3.5)!);
        Assert.Equal(3.0, (double)Invoke(registry, state, Math, "rint", "(D)D", AndroidInvokeKind.Static, 3.2)!);
    }

    [Fact]
    public void Random_returns_in_0_to_1_range()
    {
        var (state, registry, _) = Session();
        double r = (double)Invoke(registry, state, Math, "random", "()D", AndroidInvokeKind.Static)!;
        Assert.InRange(r, 0.0, 1.0);
        Assert.True(r < 1.0);
    }

    [Fact]
    public void Signum_and_ulp_follow_real_contract()
    {
        var (state, registry, _) = Session();
        Assert.Equal(-1.0, (double)Invoke(registry, state, Math, "signum", "(D)D", AndroidInvokeKind.Static, -42.0)!);
        Assert.Equal(1.0, (double)Invoke(registry, state, Math, "signum", "(D)D", AndroidInvokeKind.Static, 42.0)!);
        Assert.Equal(0.0, (double)Invoke(registry, state, Math, "signum", "(D)D", AndroidInvokeKind.Static, 0.0)!);
        Assert.True(double.IsNaN((double)Invoke(registry, state, Math, "signum", "(D)D", AndroidInvokeKind.Static, double.NaN)!));
        Assert.Equal(double.Epsilon, (double)Invoke(registry, state, Math, "ulp", "(D)D", AndroidInvokeKind.Static, 0.0)!);
        Assert.Equal(float.Epsilon, (float)Invoke(registry, state, Math, "ulp", "(F)F", AndroidInvokeKind.Static, 0.0f)!);
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

