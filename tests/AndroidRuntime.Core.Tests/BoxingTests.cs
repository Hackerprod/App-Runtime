using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for primitive boxing: the REAL JDK valueOf caching contract per type
/// (Boolean two singletons; Integer/Short/Byte/Long -128..127; Character 0..127;
/// Double/Float never), value-based equals across distinct objects, documented
/// hash codes, unboxing, parsing (NumberFormatException on failure), toString,
/// compareTo/compare, and the static-field resolver (TRUE/FALSE, TYPE).
/// </summary>
public sealed class BoxingTests
{
    private const string B = "Ljava/lang/Boolean;";
    private const string I = "Ljava/lang/Integer;";
    private const string J = "Ljava/lang/Long;";
    private const string S = "Ljava/lang/Short;";
    private const string By = "Ljava/lang/Byte;";
    private const string C = "Ljava/lang/Character;";
    private const string D = "Ljava/lang/Double;";

    [Fact]
    public void Value_of_caching_follows_the_real_jdk_contract()
    {
        var (state, registry, _) = Session();
        // Boolean: always the two singletons.
        Assert.Same(Invoke(registry, state, B, "valueOf", "(Z)Ljava/lang/Boolean;", AndroidInvokeKind.Static, 1), Invoke(registry, state, B, "valueOf", "(Z)Ljava/lang/Boolean;", AndroidInvokeKind.Static, 1));
        Assert.Same(state.BoxedObject(B, 1), state.BoxedObject(B, 1));
        Assert.NotSame(state.BoxedObject(B, 1), state.BoxedObject(B, 0));

        // Integer: -128..127 cached, outside fresh.
        Assert.Same(Invoke(registry, state, I, "valueOf", "(I)Ljava/lang/Integer;", AndroidInvokeKind.Static, 5), Invoke(registry, state, I, "valueOf", "(I)Ljava/lang/Integer;", AndroidInvokeKind.Static, 5));
        Assert.NotSame(Invoke(registry, state, I, "valueOf", "(I)Ljava/lang/Integer;", AndroidInvokeKind.Static, 1000), Invoke(registry, state, I, "valueOf", "(I)Ljava/lang/Integer;", AndroidInvokeKind.Static, 1000));

        // Long: same range, wide values.
        Assert.Same(state.BoxedObject(J, 5L), state.BoxedObject(J, 5L));
        Assert.NotSame(state.BoxedObject(J, 1000L), state.BoxedObject(J, 1000L));

        // Character: 0..127 (unsigned asymmetry), outside fresh.
        Assert.Same(state.BoxedObject(C, 65), state.BoxedObject(C, 65));
        Assert.NotSame(state.BoxedObject(C, 200), state.BoxedObject(C, 200));

        // Double/Float: NEVER cached.
        Assert.NotSame(state.BoxedObject(D, 1.0), state.BoxedObject(D, 1.0));
        Assert.NotSame(state.BoxedObject("Ljava/lang/Float;", 1.0), state.BoxedObject("Ljava/lang/Float;", 1.0));
    }

    [Fact]
    public void Equals_is_value_based_not_identity()
    {
        var (state, registry, _) = Session();
        // Distinct objects (outside cache), equal values -> equals true.
        var a = state.BoxedObject(I, 1000);
        var b = state.BoxedObject(I, 1000);
        Assert.NotSame(a, b);
        Assert.Equal(1, Invoke(registry, state, I, "equals", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, a, b));
        Assert.Equal(0, Invoke(registry, state, I, "equals", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, a, state.BoxedObject(I, 1001)));
        // Boolean equality by value too.
        Assert.Equal(1, Invoke(registry, state, B, "equals", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, state.BoxedObject(B, 1), state.BoxedObject(B, 1)));
        Assert.Equal(0, Invoke(registry, state, B, "equals", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, state.BoxedObject(B, 1), state.BoxedObject(B, 0)));
    }

    [Fact]
    public void Hash_codes_match_the_documented_formulas()
    {
        var (state, registry, _) = Session();
        Assert.Equal(1231, Invoke(registry, state, B, "hashCode", "()I", AndroidInvokeKind.Virtual, state.BoxedObject(B, 1)));
        Assert.Equal(1237, Invoke(registry, state, B, "hashCode", "()I", AndroidInvokeKind.Virtual, state.BoxedObject(B, 0)));
        Assert.Equal(42, Invoke(registry, state, I, "hashCode", "()I", AndroidInvokeKind.Virtual, state.BoxedObject(I, 42)));
        // Long.hashCode() = (int)(value ^ (value >>> 32)).
        long big = 0x1_0000_0001L;
        Assert.Equal((int)(big ^ (big >> 32)), Invoke(registry, state, J, "hashCode", "()I", AndroidInvokeKind.Virtual, state.BoxedObject(J, big)));
        // Static hashCode(primitive) overloads match.
        Assert.Equal(1231, Invoke(registry, state, B, "hashCode", "(Z)I", AndroidInvokeKind.Static, 1));
        Assert.Equal(7, Invoke(registry, state, I, "hashCode", "(I)I", AndroidInvokeKind.Static, 7));
    }

    [Fact]
    public void Unboxing_and_to_string_follow_java()
    {
        var (state, registry, _) = Session();
        Assert.Equal(1, Invoke(registry, state, B, "booleanValue", "()Z", AndroidInvokeKind.Virtual, state.BoxedObject(B, 1)));
        Assert.Equal(42, Invoke(registry, state, I, "intValue", "()I", AndroidInvokeKind.Virtual, state.BoxedObject(I, 42)));
        Assert.Equal(99L, Invoke(registry, state, J, "longValue", "()J", AndroidInvokeKind.Virtual, state.BoxedObject(J, 99L)));
        Assert.Equal("true", Invoke(registry, state, B, "toString", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, state.BoxedObject(B, 1)));
        Assert.Equal("42", Invoke(registry, state, I, "toString", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, state.BoxedObject(I, 42)));
        Assert.Equal("42", Invoke(registry, state, I, "toString", "(I)Ljava/lang/String;", AndroidInvokeKind.Static, 42));
        Assert.Equal("A", Invoke(registry, state, C, "toString", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, state.BoxedObject(C, 65)));
    }

    [Fact]
    public void Parse_methods_succeed_and_throw_number_format_exception()
    {
        var (state, registry, _) = Session();
        Assert.Equal(42, Invoke(registry, state, I, "parseInt", "(Ljava/lang/String;)I", AndroidInvokeKind.Static, "42"));
        Assert.Equal(255, Invoke(registry, state, I, "parseInt", "(Ljava/lang/String;I)I", AndroidInvokeKind.Static, "ff", 16));
        Assert.Equal(1, Invoke(registry, state, B, "parseBoolean", "(Ljava/lang/String;)Z", AndroidInvokeKind.Static, "TRUE"));
        Assert.Equal(0, Invoke(registry, state, B, "parseBoolean", "(Ljava/lang/String;)Z", AndroidInvokeKind.Static, "nope"));
        Assert.Equal(1234L, Invoke(registry, state, J, "parseLong", "(Ljava/lang/String;)J", AndroidInvokeKind.Static, "1234"));

        var error = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, I, "parseInt", "(Ljava/lang/String;)I", AndroidInvokeKind.Static, "not-a-number"));
        Assert.Equal("Ljava/lang/NumberFormatException;", error.Throwable.TypeDescriptor);
        var radixError = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, I, "parseInt", "(Ljava/lang/String;)I", AndroidInvokeKind.Static, ""));
        Assert.Equal("Ljava/lang/NumberFormatException;", radixError.Throwable.TypeDescriptor);
    }

    [Fact]
    public void Compare_to_and_compare_cover_ordering_and_nan()
    {
        var (state, registry, _) = Session();
        Assert.True((int)Invoke(registry, state, I, "compareTo", "(Ljava/lang/Integer;)I", AndroidInvokeKind.Virtual, state.BoxedObject(I, 5), state.BoxedObject(I, 9)) < 0);
        Assert.True((int)Invoke(registry, state, B, "compareTo", "(Ljava/lang/Boolean;)I", AndroidInvokeKind.Virtual, state.BoxedObject(B, 0), state.BoxedObject(B, 1)) < 0);
        // Real Double.compare: NaN sorts greater than everything.
        Assert.True((int)Invoke(registry, state, D, "compare", "(DD)I", AndroidInvokeKind.Static, double.NaN, 1.0) > 0);
        Assert.True((int)Invoke(registry, state, D, "compare", "(DD)I", AndroidInvokeKind.Static, 1.0, double.NaN) < 0);
        Assert.Equal(0, Invoke(registry, state, D, "compare", "(DD)I", AndroidInvokeKind.Static, 1.5, 1.5));
    }

    [Fact]
    public void Static_field_resolver_exposes_true_false_and_type()
    {
        var (state, registry, interpreter) = Session();
        // Boolean.TRUE/FALSE singletons via the framework static-field hook.
        Assert.Same(state.BoxedObject(B, 1), state.ResolveFrameworkStaticField(B, "TRUE"));
        Assert.Same(state.BoxedObject(B, 0), state.ResolveFrameworkStaticField(B, "FALSE"));
        // Integer.TYPE == int.class (canonical Class object for the primitive).
        var type = (DexObject)state.ResolveFrameworkStaticField(I, "TYPE")!;
        Assert.Same(state.EnsureClassObject("I"), type);
        Assert.Equal("int", Invoke(registry, state, "Ljava/lang/Class;", "getName", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, type));
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
