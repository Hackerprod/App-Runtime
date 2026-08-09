#nullable enable
using System.Globalization;
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for primitive boxing (java.lang.Boolean/Integer/Long/Short/Byte/
/// Character/Double/Float). The box is a DexObject typed as the box class with a
/// BoxedPeer holding the raw value — the same peer pattern used everywhere. The
/// valueOf caching contract is the REAL documented JDK behavior (see
/// AndroidFrameworkState.BoxedObject and README boundary #44): Boolean always
/// two singletons, Integer/Short/Byte/Long -128..127, Character 0..127, Double/
/// Float never. Equality is VALUE-based (Integer.equals compares values, not
/// identity — unlike Class/Enum). Hash codes follow the documented JDK formulas.
/// Probe-confirmed scope only: the core Number surface per type; bit operations
/// (Integer/Long bitCount/highestOneBit/rotateLeft/...), Character's Unicode
/// static predicates (isDigit/isLetter/getType/...), and Double/Float bit
/// methods (doubleToLongBits/floatToIntBits/...) are referenced by bundled libs
/// but deliberately NOT built (separate features — report if the run reaches them).
/// </summary>
internal static class JavaLangBoxingBindings
{
    private const string B = "Ljava/lang/Boolean;";
    private const string I = "Ljava/lang/Integer;";
    private const string J = "Ljava/lang/Long;";
    private const string S = "Ljava/lang/Short;";
    private const string By = "Ljava/lang/Byte;";
    private const string C = "Ljava/lang/Character;";
    private const string D = "Ljava/lang/Double;";
    private const string F = "Ljava/lang/Float;";

    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        // ---- Boolean ----
        RegisterIntegral(builder, state, B, "booleanValue", "()Z", toStringOverride: v => v != 0 ? "true" : "false", hashCodeOverride: v => v != 0 ? 1231 : 1237);
        builder.Register(Api(B, "<init>", "(Z)V"), (_, args) => { state.Boxed.Add(Receiver(args), new BoxedPeer(RequireInt(args[1]))); return null!; });
        builder.Register(Api(B, "valueOf", "(Z)Ljava/lang/Boolean;"), (_, args) => state.BoxedObject(B, RequireInt(args[0])));
        builder.Register(Api(B, "parseBoolean", "(Ljava/lang/String;)Z"), (_, args) => string.Equals(RequireString(args[0]), "true", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        builder.Register(Api(B, "toString", "(Z)Ljava/lang/String;"), (_, args) => RequireInt(args[0]) != 0 ? "true" : "false");
        builder.Register(Api(B, "hashCode", "(Z)I"), (_, args) => RequireInt(args[0]) != 0 ? 1231 : 1237);
        builder.Register(Api(B, "compare", "(ZZ)I"), (_, args) => RequireInt(args[0]).CompareTo(RequireInt(args[1])));
        builder.Register(Api(B, "compareTo", "(Ljava/lang/Boolean;)I"), (_, args) => BooleanRaw(state, args[0]).CompareTo(BooleanRaw(state, args[1])));
        builder.Register(Api(B, "equals", "(Ljava/lang/Object;)Z"), (_, args) => args[1] is DexObject other && other.TypeDescriptor == B && BooleanRaw(state, args[0]) == BooleanRaw(state, other) ? 1 : 0);

        // ---- Integer ----
        RegisterIntegral(builder, state, I, "intValue", "()I");
        builder.Register(Api(I, "<init>", "(I)V"), (_, args) => { state.Boxed.Add(Receiver(args), new BoxedPeer(RequireInt(args[1]))); return null!; });
        builder.Register(Api(I, "valueOf", "(I)Ljava/lang/Integer;"), (_, args) => state.BoxedObject(I, RequireInt(args[0])));
        builder.Register(Api(I, "valueOf", "(Ljava/lang/String;)Ljava/lang/Integer;"), (_, args) => state.BoxedObject(I, ParseInt(RequireString(args[0]), 10)));
        builder.Register(Api(I, "parseInt", "(Ljava/lang/String;)I"), (_, args) => ParseInt(RequireString(args[0]), 10));
        builder.Register(Api(I, "parseInt", "(Ljava/lang/String;I)I"), (_, args) => ParseInt(RequireString(args[0]), RequireInt(args[1])));
        builder.Register(Api(I, "toString", "(I)Ljava/lang/String;"), (_, args) => RequireInt(args[0]).ToString(CultureInfo.InvariantCulture));
        builder.Register(Api(I, "toString", "(II)Ljava/lang/String;"), (_, args) => IntegralToString(RequireInt(args[0]), RequireInt(args[1])));
        builder.Register(Api(I, "hashCode", "(I)I"), (_, args) => RequireInt(args[0]));
        builder.Register(Api(I, "compare", "(II)I"), (_, args) => RequireInt(args[0]).CompareTo(RequireInt(args[1])));
        builder.Register(Api(I, "compareTo", "(Ljava/lang/Integer;)I"), (_, args) => IntRaw(state, args[0]).CompareTo(IntRaw(state, args[1])));
        builder.Register(Api(I, "equals", "(Ljava/lang/Object;)Z"), (_, args) => args[1] is DexObject other && other.TypeDescriptor == I && IntRaw(state, args[0]) == IntRaw(state, other) ? 1 : 0);

        // ---- Long ----
        RegisterWide(builder, state, J, "longValue", "()J");
        builder.Register(Api(J, "<init>", "(J)V"), (_, args) => { state.Boxed.Add(Receiver(args), new BoxedPeer(RequireLong(args[1]))); return null!; });
        builder.Register(Api(J, "valueOf", "(J)Ljava/lang/Long;"), (_, args) => state.BoxedObject(J, RequireLong(args[0])));
        builder.Register(Api(J, "parseLong", "(Ljava/lang/String;)J"), (_, args) => ParseLong(RequireString(args[0]), 10));
        builder.Register(Api(J, "parseLong", "(Ljava/lang/String;I)J"), (_, args) => ParseLong(RequireString(args[0]), RequireInt(args[1])));
        builder.Register(Api(J, "toString", "(J)Ljava/lang/String;"), (_, args) => RequireLong(args[0]).ToString(CultureInfo.InvariantCulture));
        builder.Register(Api(J, "toString", "(JI)Ljava/lang/String;"), (_, args) => LongToString(RequireLong(args[0]), RequireInt(args[1])));
        builder.Register(Api(J, "hashCode", "(J)I"), (_, args) => LongHash(RequireLong(args[0])));
        builder.Register(Api(J, "compare", "(JJ)I"), (_, args) => RequireLong(args[0]).CompareTo(RequireLong(args[1])));
        builder.Register(Api(J, "compareTo", "(Ljava/lang/Long;)I"), (_, args) => LongRaw(state, args[0]).CompareTo(LongRaw(state, args[1])));
        builder.Register(Api(J, "equals", "(Ljava/lang/Object;)Z"), (_, args) => args[1] is DexObject other && other.TypeDescriptor == J && LongRaw(state, args[0]) == LongRaw(state, other) ? 1 : 0);

        // ---- Short ----
        RegisterIntegral(builder, state, S, "shortValue", "()S");
        builder.Register(Api(S, "<init>", "(S)V"), (_, args) => { state.Boxed.Add(Receiver(args), new BoxedPeer((short)RequireInt(args[1]))); return null!; });
        builder.Register(Api(S, "valueOf", "(S)Ljava/lang/Short;"), (_, args) => state.BoxedObject(S, (int)(short)RequireInt(args[0])));
        builder.Register(Api(S, "parseShort", "(Ljava/lang/String;)S"), (_, args) => (int)(short)ParseInt(RequireString(args[0]), 10));
        builder.Register(Api(S, "parseShort", "(Ljava/lang/String;I)S"), (_, args) => (int)(short)ParseInt(RequireString(args[0]), RequireInt(args[1])));
        builder.Register(Api(S, "hashCode", "(S)I"), (_, args) => (short)RequireInt(args[0]));
        builder.Register(Api(S, "compareTo", "(Ljava/lang/Short;)I"), (_, args) => ShortRaw(state, args[0]).CompareTo(ShortRaw(state, args[1])));
        builder.Register(Api(S, "equals", "(Ljava/lang/Object;)Z"), (_, args) => args[1] is DexObject other && other.TypeDescriptor == S && ShortRaw(state, args[0]) == ShortRaw(state, other) ? 1 : 0);
        

        // ---- Byte ----
        RegisterIntegral(builder, state, By, "byteValue", "()B");
        builder.Register(Api(By, "<init>", "(B)V"), (_, args) => { state.Boxed.Add(Receiver(args), new BoxedPeer((sbyte)RequireInt(args[1]))); return null!; });
        builder.Register(Api(By, "valueOf", "(B)Ljava/lang/Byte;"), (_, args) => state.BoxedObject(By, (int)(sbyte)RequireInt(args[0])));
        builder.Register(Api(By, "parseByte", "(Ljava/lang/String;)B"), (_, args) => (int)(sbyte)ParseInt(RequireString(args[0]), 10));
        builder.Register(Api(By, "parseByte", "(Ljava/lang/String;I)B"), (_, args) => (int)(sbyte)ParseInt(RequireString(args[0]), RequireInt(args[1])));
        builder.Register(Api(By, "hashCode", "(B)I"), (_, args) => (sbyte)RequireInt(args[0]));
        builder.Register(Api(By, "compareTo", "(Ljava/lang/Byte;)I"), (_, args) => ByteRaw(state, args[0]).CompareTo(ByteRaw(state, args[1])));
        builder.Register(Api(By, "equals", "(Ljava/lang/Object;)Z"), (_, args) => args[1] is DexObject other && other.TypeDescriptor == By && ByteRaw(state, args[0]) == ByteRaw(state, other) ? 1 : 0);
        

        // ---- Character ----
        RegisterIntegral(builder, state, C, "charValue", "()C", toStringOverride: v => ((char)v).ToString());
        builder.Register(Api(C, "<init>", "(C)V"), (_, args) => { state.Boxed.Add(Receiver(args), new BoxedPeer(RequireInt(args[1]))); return null!; });
        builder.Register(Api(C, "valueOf", "(C)Ljava/lang/Character;"), (_, args) => state.BoxedObject(C, RequireInt(args[0])));
        builder.Register(Api(C, "toString", "(C)Ljava/lang/String;"), (_, args) => ((char)RequireInt(args[0])).ToString());
        builder.Register(Api(C, "compareTo", "(Ljava/lang/Character;)I"), (_, args) => CharRaw(state, args[0]).CompareTo(CharRaw(state, args[1])));
        builder.Register(Api(C, "equals", "(Ljava/lang/Object;)Z"), (_, args) => args[1] is DexObject other && other.TypeDescriptor == C && CharRaw(state, args[0]) == CharRaw(state, other) ? 1 : 0);

        // ---- Double (never cached) ----
        RegisterFloating(builder, state, D, "doubleValue", "()D");
        builder.Register(Api(D, "<init>", "(D)V"), (_, args) => { state.Boxed.Add(Receiver(args), new BoxedPeer(RequireDouble(args[1]))); return null!; });
        builder.Register(Api(D, "valueOf", "(D)Ljava/lang/Double;"), (_, args) => state.BoxedObject(D, RequireDouble(args[0])));
        builder.Register(Api(D, "valueOf", "(Ljava/lang/String;)Ljava/lang/Double;"), (_, args) => state.BoxedObject(D, ParseDouble(RequireString(args[0]))));
        builder.Register(Api(D, "parseDouble", "(Ljava/lang/String;)D"), (_, args) => ParseDouble(RequireString(args[0])));
        builder.Register(Api(D, "toString", "(D)Ljava/lang/String;"), (_, args) => JavaDouble(RequireDouble(args[0])));
        builder.Register(Api(D, "hashCode", "(D)I"), (_, args) => DoubleHash(RequireDouble(args[0])));
        builder.Register(Api(D, "compare", "(DD)I"), (_, args) => CompareDouble(RequireDouble(args[0]), RequireDouble(args[1])));
        builder.Register(Api(D, "compareTo", "(Ljava/lang/Double;)I"), (_, args) => CompareDouble(DoubleRaw(state, args[0]), DoubleRaw(state, args[1])));
        builder.Register(Api(D, "equals", "(Ljava/lang/Object;)Z"), (_, args) => args[1] is DexObject other && other.TypeDescriptor == D && CompareDouble(DoubleRaw(state, args[0]), DoubleRaw(state, other)) == 0 ? 1 : 0);
        builder.Register(Api(D, "isNaN", "()Z"), (_, args) => double.IsNaN(DoubleRaw(state, args[0])) ? 1 : 0);
        builder.Register(Api(D, "isInfinite", "()Z"), (_, args) => double.IsInfinity(DoubleRaw(state, args[0])) ? 1 : 0);

        // ---- Float (never cached) ----
        RegisterFloating(builder, state, F, "floatValue", "()F");
        builder.Register(Api(F, "<init>", "(F)V"), (_, args) => { state.Boxed.Add(Receiver(args), new BoxedPeer(RequireDouble(args[1]))); return null!; });
        builder.Register(Api(F, "valueOf", "(F)Ljava/lang/Float;"), (_, args) => state.BoxedObject(F, RequireDouble(args[0])));
        builder.Register(Api(F, "parseFloat", "(Ljava/lang/String;)F"), (_, args) => ParseDouble(RequireString(args[0])));
        builder.Register(Api(F, "toString", "(F)Ljava/lang/String;"), (_, args) => JavaDouble(RequireDouble(args[0])));
        builder.Register(Api(F, "hashCode", "(F)I"), (_, args) => FloatHash(RequireDouble(args[0])));
        builder.Register(Api(F, "compare", "(FF)I"), (_, args) => CompareFloat(RequireDouble(args[0]), RequireDouble(args[1])));
        builder.Register(Api(F, "compareTo", "(Ljava/lang/Float;)I"), (_, args) => CompareFloat(FloatRaw(state, args[0]), FloatRaw(state, args[1])));
        builder.Register(Api(F, "equals", "(Ljava/lang/Object;)Z"), (_, args) => args[1] is DexObject other && other.TypeDescriptor == F && CompareFloat(FloatRaw(state, args[0]), FloatRaw(state, other)) == 0 ? 1 : 0);
        builder.Register(Api(F, "isNaN", "()Z"), (_, args) => double.IsNaN(FloatRaw(state, args[0])) ? 1 : 0);
        builder.Register(Api(F, "isInfinite", "()Z"), (_, args) => double.IsInfinity(FloatRaw(state, args[0])) ? 1 : 0);
    }

    // ---------------------------------------------------------------------------
    // Generic per-shape registration (integral: same-type unboxing + value-based
    // toString/hashCode; wide and floating mirror the same shape)
    // ---------------------------------------------------------------------------

    private static void RegisterIntegral(
        AndroidApiRegistryBuilder builder,
        AndroidFrameworkState state,
        string type,
        string unboxName,
        string unboxDescriptor,
        Func<int, string>? toStringOverride = null,
        Func<int, int>? hashCodeOverride = null)
    {
        builder.Register(Api(type, unboxName, unboxDescriptor), (_, args) => IntegralRaw(state, args[0]));
        builder.Register(Api(type, "toString", "()Ljava/lang/String;"), (_, args) => toStringOverride?.Invoke(IntegralRaw(state, args[0])) ?? IntegralRaw(state, args[0]).ToString(CultureInfo.InvariantCulture));
        builder.Register(Api(type, "hashCode", "()I"), (_, args) => hashCodeOverride?.Invoke(IntegralRaw(state, args[0])) ?? IntegralRaw(state, args[0]));
    }

    private static void RegisterWide(AndroidApiRegistryBuilder builder, AndroidFrameworkState state, string type, string unboxName, string unboxDescriptor)
    {
        builder.Register(Api(type, unboxName, unboxDescriptor), (_, args) => LongRaw(state, args[0]));
        builder.Register(Api(type, "toString", "()Ljava/lang/String;"), (_, args) => LongRaw(state, args[0]).ToString(CultureInfo.InvariantCulture));
        builder.Register(Api(type, "hashCode", "()I"), (_, args) => LongHash(LongRaw(state, args[0])));
    }

    private static void RegisterFloating(AndroidApiRegistryBuilder builder, AndroidFrameworkState state, string type, string unboxName, string unboxDescriptor)
    {
        builder.Register(Api(type, unboxName, unboxDescriptor), (_, args) => FloatingRaw(state, args[0]));
        builder.Register(Api(type, "toString", "()Ljava/lang/String;"), (_, args) => JavaDouble(FloatingRaw(state, args[0])));
        builder.Register(Api(type, "hashCode", "()I"), (_, args) => type == "Ljava/lang/Float;" ? FloatHash(FloatingRaw(state, args[0])) : DoubleHash(FloatingRaw(state, args[0])));
    }

    // ---------------------------------------------------------------------------
    // Raw accessors
    // ---------------------------------------------------------------------------

    private static int IntegralRaw(AndroidFrameworkState state, object receiver) => state.Boxed.Get((DexObject)receiver).RawValue switch
    {
        int i => i,
        short s => s,
        sbyte b => b,
        _ => throw new ArgumentException("Unexpected boxed integral value.")
    };
    private static int BooleanRaw(AndroidFrameworkState state, object receiver) => state.Boxed.Get((DexObject)receiver).RawValue switch { int i => i, bool b => b ? 1 : 0, _ => throw new ArgumentException("Unexpected boxed boolean value.") };
    private static int IntRaw(AndroidFrameworkState state, object receiver) => (int)state.Boxed.Get((DexObject)receiver).RawValue;
    private static long LongRaw(AndroidFrameworkState state, object receiver) => (long)state.Boxed.Get((DexObject)receiver).RawValue;
    private static int ShortRaw(AndroidFrameworkState state, object receiver) => (short)state.Boxed.Get((DexObject)receiver).RawValue;
    private static int ByteRaw(AndroidFrameworkState state, object receiver) => (sbyte)state.Boxed.Get((DexObject)receiver).RawValue;
    private static int CharRaw(AndroidFrameworkState state, object receiver) => (int)state.Boxed.Get((DexObject)receiver).RawValue;
    private static double FloatingRaw(AndroidFrameworkState state, object receiver) => (double)state.Boxed.Get((DexObject)receiver).RawValue;
    private static double DoubleRaw(AndroidFrameworkState state, object receiver) => (double)state.Boxed.Get((DexObject)receiver).RawValue;
    private static double FloatRaw(AndroidFrameworkState state, object receiver) => (double)state.Boxed.Get((DexObject)receiver).RawValue;

    // ---------------------------------------------------------------------------
    // Parsing / formatting / hash helpers (real JDK semantics where it matters)
    // ---------------------------------------------------------------------------

    private static int ParseInt(string text, int radix)
    {
        try
        {
            if (radix == 10) return int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (radix is 2 or 8 or 16) return Convert.ToInt32(text, radix);
            return int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture); // bounded fallback (see README #44)
        }
        catch (Exception)
        {
            throw NumberFormat();
        }
    }

    private static long ParseLong(string text, int radix)
    {
        try
        {
            if (radix == 10) return long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (radix is 2 or 8 or 16) return Convert.ToInt64(text, radix);
            return long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture); // bounded fallback
        }
        catch (Exception)
        {
            throw NumberFormat();
        }
    }

    private static double ParseDouble(string text)
    {
        if (string.Equals(text, "NaN", StringComparison.OrdinalIgnoreCase)) return double.NaN;
        if (string.Equals(text, "Infinity", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "+Infinity", StringComparison.OrdinalIgnoreCase)) return double.PositiveInfinity;
        if (string.Equals(text, "-Infinity", StringComparison.OrdinalIgnoreCase)) return double.NegativeInfinity;
        try { return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture); }
        catch (Exception) { throw NumberFormat(); }
    }

    private static GuestExceptionCarrier NumberFormat() =>
        new(GuestThrowableMetadata.Create("Ljava/lang/NumberFormatException;", "For input string"));

    private static string IntegralToString(int value, int radix) => radix switch
    {
        2 or 8 or 10 or 16 => Convert.ToString(value, radix),
        _ => value.ToString(CultureInfo.InvariantCulture) // bounded (see README #44)
    };

    private static string LongToString(long value, int radix) => radix switch
    {
        2 or 8 or 10 or 16 => Convert.ToString(value, radix),
        _ => value.ToString(CultureInfo.InvariantCulture)
    };

    private static int LongHash(long value) => (int)(value ^ (value >> 32));

    private static int DoubleHash(double value) => LongHash(BitConverter.DoubleToInt64Bits(value));

    private static int FloatHash(double value) => BitConverter.SingleToInt32Bits((float)value);

    private static int CompareDouble(double left, double right)
    {
        // Real Java Double.compare: NaN sorts greater than everything; -0.0 < 0.0.
        if (double.IsNaN(left)) return double.IsNaN(right) ? 0 : 1;
        if (double.IsNaN(right)) return -1;
        if (left == right)
        {
            if (left == 0.0)
                return BitConverter.DoubleToInt64Bits(left) < BitConverter.DoubleToInt64Bits(right) ? -1 : BitConverter.DoubleToInt64Bits(left) > BitConverter.DoubleToInt64Bits(right) ? 1 : 0;
            return 0;
        }
        return left < right ? -1 : 1;
    }

    private static int CompareFloat(double left, double right)
    {
        if (double.IsNaN(left)) return double.IsNaN(right) ? 0 : 1;
        if (double.IsNaN(right)) return -1;
        if (left == right)
        {
            if (left == 0.0f)
                return BitConverter.SingleToInt32Bits((float)left) < BitConverter.SingleToInt32Bits((float)right) ? -1 : BitConverter.SingleToInt32Bits((float)left) > BitConverter.SingleToInt32Bits((float)right) ? 1 : 0;
            return 0;
        }
        return left < right ? -1 : 1;
    }

    private static string JavaDouble(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    private static DexObject Receiver(object[] args) => (DexObject)args[0]!;
    private static string RequireString(object value, bool allowNull = false) => AndroidApiBindings.RequireString(value, allowNull);
    private static int RequireInt(object value) => AndroidApiBindings.RequireInt(value);
    private static long RequireLong(object value) => AndroidApiBindings.RequireLong(value);
    private static double RequireDouble(object value) => value is double d ? d : value is float f ? f : throw new ArgumentException("Expected a double.");
}
