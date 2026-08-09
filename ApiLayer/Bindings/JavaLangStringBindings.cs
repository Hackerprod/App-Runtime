#nullable enable
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for java.lang.String, migrated out of the AndroidApiBindings monolith
/// the same way Object/Enum/Class were when touched (the group is now the full
/// commonly-used real contract). Scope confirmed against SKYNET's method_id table
/// (see README boundary #40 for the full referenced list). Regex-backed methods
/// (split/matches/replaceAll) use .NET Regex under the hood — no hand-rolled
/// engine; Java split trailing-empty semantics are implemented explicitly.
/// Locale handling follows the established convention: equalsIgnoreCase already
/// pins Unicode mapping to Java SE 17 (JavaEqualsIgnoreCase), and the no-arg
/// case conversions use the invariant culture as the bounded default.
/// NOT bound here: the String constructors (&lt;init&gt;([C/[B/...) — a guest
/// String is a CLR string, not a DexObject, so constructing one in a binding
/// cannot yield a usable String value; those are a representation change and are
/// reported as a separate boundary if the real run ever executes them.
/// </summary>
internal static class JavaLangStringBindings
{
    private const string StringClass = "Ljava/lang/String;";

    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        // ---- Existing surface (moved from the monolith) ----
        builder.Register(Api(StringClass, "length", "()I"), (_, args) => RequireString(args[0]).Length);
        builder.Register(Api(StringClass, "isEmpty", "()Z"), (_, args) => RequireString(args[0]).Length == 0 ? 1 : 0);
        builder.Register(Api(StringClass, "equals", "(Ljava/lang/Object;)Z"), (_, args) => args[1] is string b && string.Equals(RequireString(args[0]), b, StringComparison.Ordinal) ? 1 : 0);
        builder.Register(Api(StringClass, "equalsIgnoreCase", "(Ljava/lang/String;)Z"), (_, args) => args[1] is string right && AndroidApiBindings.JavaEqualsIgnoreCase(RequireString(args[0]), right) ? 1 : 0);
        builder.Register(Api(StringClass, "startsWith", "(Ljava/lang/String;)Z"), (_, args) => RequireString(args[0]).StartsWith(RequireString(args[1]), StringComparison.Ordinal) ? 1 : 0);
        builder.Register(Api(StringClass, "endsWith", "(Ljava/lang/String;)Z"), (_, args) => RequireString(args[0]).EndsWith(RequireString(args[1]), StringComparison.Ordinal) ? 1 : 0);
        builder.Register(Api(StringClass, "contains", "(Ljava/lang/CharSequence;)Z"), (_, args) => RequireString(args[0]).Contains(AsText(state, args[1]) ?? throw new ArgumentException("String.contains argument is null."), StringComparison.Ordinal) ? 1 : 0);
        builder.Register(Api(StringClass, "indexOf", "(Ljava/lang/String;)I"), (_, args) => RequireString(args[0]).IndexOf(RequireString(args[1]), StringComparison.Ordinal));
        builder.Register(Api(StringClass, "indexOf", "(Ljava/lang/String;I)I"), (_, args) => AndroidApiBindings.JavaIndexOf(RequireString(args[0]), RequireString(args[1]), RequireInt(args[2])));
        builder.Register(Api(StringClass, "concat", "(Ljava/lang/String;)Ljava/lang/String;"), (_, args) => RequireString(args[0]) + RequireString(args[1]));
        builder.Register(Api(StringClass, "trim", "()Ljava/lang/String;"), (_, args) => AndroidApiBindings.JavaTrim(RequireString(args[0])));
        builder.Register(Api(StringClass, "toString", "()Ljava/lang/String;"), (_, args) => RequireString(args[0]));
        builder.Register(Api(StringClass, "hashCode", "()I"), (_, args) => AndroidApiBindings.JavaHash(RequireString(args[0])));
        builder.Register(Api(StringClass, "valueOf", "(I)Ljava/lang/String;"), (_, args) => RequireInt(args[0]).ToString(CultureInfo.InvariantCulture));
        builder.Register(Api(StringClass, "valueOf", "(Z)Ljava/lang/String;"), (_, args) => RequireInt(args[0]) == 0 ? "false" : "true");
        builder.Register(Api(StringClass, "valueOf", "(C)Ljava/lang/String;"), (_, args) => ((char)RequireInt(args[0])).ToString());

        // ---- New surface (probe-confirmed + common real contract) ----
        // Bounds: real StringIndexOutOfBoundsException for out-of-range indices
        // (extends IndexOutOfBoundsException, see AndroidFrameworkHierarchy.Parents).
        builder.Register(Api(StringClass, "substring", "(I)Ljava/lang/String;"), (_, args) => Substring(RequireString(args[0]), RequireInt(args[1]), RequireString(args[0]).Length));
        builder.Register(Api(StringClass, "substring", "(II)Ljava/lang/String;"), (_, args) => Substring(RequireString(args[0]), RequireInt(args[1]), RequireInt(args[2])));
        builder.Register(Api(StringClass, "subSequence", "(II)Ljava/lang/CharSequence;"), (_, args) => Substring(RequireString(args[0]), RequireInt(args[1]), RequireInt(args[2])));
        builder.Register(Api(StringClass, "charAt", "(I)C"), (_, args) =>
        {
            string value = RequireString(args[0]);
            int index = RequireInt(args[1]);
            if (index < 0 || index >= value.Length) throw StringIndexOutOfBounds(index);
            return (int)value[index];
        });
        builder.Register(Api(StringClass, "toCharArray", "()[C"), (_, args) =>
        {
            string value = RequireString(args[0]);
            var array = new DexArray("[C", value.Length);
            for (int index = 0; index < value.Length; index++) array.Set(index, (int)value[index]);
            return array;
        });
        builder.Register(Api(StringClass, "compareTo", "(Ljava/lang/String;)I"), (_, args) => JavaCompareTo(RequireString(args[0]), RequireString(args[1])));
        builder.Register(Api(StringClass, "compareToIgnoreCase", "(Ljava/lang/String;)I"), (_, args) => JavaCompareToIgnoreCase(RequireString(args[0]), RequireString(args[1])));
        builder.Register(Api(StringClass, "replace", "(CC)Ljava/lang/String;"), (_, args) => RequireString(args[0]).Replace((char)RequireInt(args[1]), (char)RequireInt(args[2])));
        builder.Register(Api(StringClass, "replace", "(Ljava/lang/CharSequence;Ljava/lang/CharSequence;)Ljava/lang/String;"), (_, args) =>
        {
            // Literal replacement (real Java's CharSequence overload is NOT regex;
            // replaceAll is the regex one).
            string target = AsText(state, args[1]) ?? throw new ArgumentException("String.replace target is null.");
            return RequireString(args[0]).Replace(target, AsText(state, args[2]) ?? throw new ArgumentException("String.replace replacement is null."), StringComparison.Ordinal);
        });
        builder.Register(Api(StringClass, "toUpperCase", "()Ljava/lang/String;"), (_, args) => RequireString(args[0]).ToUpperInvariant());
        builder.Register(Api(StringClass, "toLowerCase", "()Ljava/lang/String;"), (_, args) => RequireString(args[0]).ToLowerInvariant());
        builder.Register(Api(StringClass, "toUpperCase", "(Ljava/util/Locale;)Ljava/lang/String;"), (_, args) => RequireString(args[0]).ToUpperInvariant());
        builder.Register(Api(StringClass, "toLowerCase", "(Ljava/util/Locale;)Ljava/lang/String;"), (_, args) => RequireString(args[0]).ToLowerInvariant());
        builder.Register(Api(StringClass, "startsWith", "(Ljava/lang/String;I)Z"), (_, args) =>
        {
            string value = RequireString(args[0]);
            int from = RequireInt(args[2]);
            string prefix = RequireString(args[1]);
            if (from < 0 || from > value.Length) return 0;
            return value.AsSpan(from).StartsWith(prefix, StringComparison.Ordinal) ? 1 : 0;
        });
        builder.Register(Api(StringClass, "regionMatches", "(ILjava/lang/String;II)Z"), (_, args) => JavaRegionMatches(state, RequireString(args[0]), ignoreCase: false, RequireInt(args[1]), RequireString(args[2]), RequireInt(args[3]), RequireInt(args[4])) ? 1 : 0);
        builder.Register(Api(StringClass, "regionMatches", "(ZILjava/lang/String;II)Z"), (_, args) => JavaRegionMatches(state, RequireString(args[0]), ignoreCase: RequireInt(args[1]) != 0, RequireInt(args[2]), RequireString(args[3]), RequireInt(args[4]), RequireInt(args[5])) ? 1 : 0);
        builder.Register(Api(StringClass, "contentEquals", "(Ljava/lang/CharSequence;)Z"), (_, args) => string.Equals(RequireString(args[0]), AsText(state, args[1]), StringComparison.Ordinal) ? 1 : 0);
        builder.Register(Api(StringClass, "lastIndexOf", "(Ljava/lang/String;)I"), (_, args) => RequireString(args[0]).LastIndexOf(RequireString(args[1]), StringComparison.Ordinal));
        builder.Register(Api(StringClass, "lastIndexOf", "(Ljava/lang/String;I)I"), (_, args) => JavaLastIndexOf(RequireString(args[0]), RequireString(args[1]), RequireInt(args[2])));
        builder.Register(Api(StringClass, "lastIndexOf", "(I)I"), (_, args) => RequireString(args[0]).LastIndexOf((char)RequireInt(args[1])));
        builder.Register(Api(StringClass, "lastIndexOf", "(II)I"), (_, args) =>
        {
            string value = RequireString(args[0]);
            if (value.Length == 0) return -1;
            return value.LastIndexOf((char)RequireInt(args[1]), Math.Clamp(RequireInt(args[2]), 0, value.Length - 1));
        });
        builder.Register(Api(StringClass, "indexOf", "(I)I"), (_, args) => RequireString(args[0]).IndexOf((char)RequireInt(args[1])));
        builder.Register(Api(StringClass, "indexOf", "(II)I"), (_, args) => RequireString(args[0]).IndexOf((char)RequireInt(args[1]), Math.Clamp(RequireInt(args[2]), 0, RequireString(args[0]).Length)));
        builder.Register(Api(StringClass, "intern", "()Ljava/lang/String;"), (_, args) => RequireString(args[0]));
        // Unicode code points (bounded UTF-16 surrogate handling; char arrays are
        // code units, matching real Java's charAt/codePointAt distinction).
        builder.Register(Api(StringClass, "codePointAt", "(I)I"), (_, args) => CodePointAt(RequireString(args[0]), RequireInt(args[1])));
        builder.Register(Api(StringClass, "codePointBefore", "(I)I"), (_, args) => CodePointBefore(RequireString(args[0]), RequireInt(args[1])));
        builder.Register(Api(StringClass, "codePointCount", "(II)I"), (_, args) => CodePointCount(RequireString(args[0]), RequireInt(args[1]), RequireInt(args[2])));
        builder.Register(Api(StringClass, "offsetByCodePoints", "(II)I"), (_, args) => OffsetByCodePoints(RequireString(args[0]), RequireInt(args[1]), RequireInt(args[2])));
        // Regex-backed surface (uses .NET Regex — same core Perl-derived syntax as
        // Java; dialect differences are flagged, not silently picked).
        builder.Register(Api(StringClass, "split", "(Ljava/lang/String;)[Ljava/lang/String;"), (_, args) => Split(RequireString(args[0]), RequireString(args[1]), limit: 0));
        builder.Register(Api(StringClass, "split", "(Ljava/lang/String;I)[Ljava/lang/String;"), (_, args) => Split(RequireString(args[0]), RequireString(args[1]), RequireInt(args[2])));
        builder.Register(Api(StringClass, "matches", "(Ljava/lang/String;)Z"), (_, args) => Regex.IsMatch(RequireString(args[0]), "^(?:" + RequireString(args[1]) + ")$") ? 1 : 0);
        builder.Register(Api(StringClass, "replaceAll", "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;"), (_, args) => Regex.Replace(RequireString(args[0]), RequireString(args[1]), RequireString(args[2])));
        // format: bounded to the specifiers actually in common use (%s %d %f %x %X
        // %b %c %n %%) plus basic width/precision and positional (%1$s) — NOT a full
        // printf engine (README boundary #40).
        builder.Register(Api(StringClass, "format", "(Ljava/lang/String;[Ljava/lang/Object;)Ljava/lang/String;"), (_, args) => Format(state, RequireString(args[0]), VarArgs(state, args[1])));
        builder.Register(Api(StringClass, "format", "(Ljava/util/Locale;Ljava/lang/String;[Ljava/lang/Object;)Ljava/lang/String;"), (_, args) => Format(state, RequireString(args[1]), VarArgs(state, args[2])));
        builder.Register(Api(StringClass, "copyValueOf", "([C)Ljava/lang/String;"), (_, args) => FromCharArray(state, RequireCharArray(args[1])));
        builder.Register(Api(StringClass, "valueOf", "([C)Ljava/lang/String;"), (_, args) => FromCharArray(state, RequireCharArray(args[1])));
        builder.Register(Api(StringClass, "valueOf", "(J)Ljava/lang/String;"), (_, args) => RequireLong(args[0]).ToString(CultureInfo.InvariantCulture));
        builder.Register(Api(StringClass, "valueOf", "(D)Ljava/lang/String;"), (_, args) => JavaDouble(RequireDouble(args[0])));
        builder.Register(Api(StringClass, "valueOf", "(F)Ljava/lang/String;"), (_, args) => JavaDouble(RequireDouble(args[0])));
        builder.Register(Api(StringClass, "valueOf", "(Ljava/lang/Object;)Ljava/lang/String;"), (_, args) => ValueOfObject(state, args[1]));
        builder.Register(Api(StringClass, "getBytes", "()[B"), (_, args) => ToBytes(RequireString(args[0])));
        builder.Register(Api(StringClass, "getBytes", "(Ljava/lang/String;)[B"), (_, args) => ToBytes(RequireString(args[0]), RequireString(args[1])));
        builder.Register(Api(StringClass, "getBytes", "(Ljava/nio/charset/Charset;)[B"), (_, args) => ToBytes(RequireString(args[0]), "UTF-8"));
        builder.Register(Api(StringClass, "getChars", "(II[CI)V"), (_, args) => { GetChars(state, RequireString(args[0]), RequireInt(args[1]), RequireInt(args[2]), RequireCharArray(args[3]), RequireInt(args[4])); return null!; });
    }

    // ---------------------------------------------------------------------------
    // Core helpers (internal: shared with kotlin.text.StringsKt bindings, which
    // delegate to the SAME logic instead of reimplementing)
    // ---------------------------------------------------------------------------

    internal static string Substring(string value, int begin, int end)
    {
        if (begin < 0) throw StringIndexOutOfBounds(begin);
        if (end > value.Length) throw StringIndexOutOfBounds(end);
        if (begin > end) throw StringIndexOutOfBounds(end - begin);
        return value.Substring(begin, end - begin);
    }

    internal static int JavaCompareTo(string left, string right)
    {
        int shared = Math.Min(left.Length, right.Length);
        for (int index = 0; index < shared; index++)
        {
            int difference = left[index] - right[index];
            if (difference != 0) return difference;
        }
        return left.Length - right.Length;
    }

    internal static int JavaCompareToIgnoreCase(string left, string right)
    {
        int shared = Math.Min(left.Length, right.Length);
        for (int index = 0; index < shared; index++)
        {
            int difference = char.ToUpperInvariant(left[index]) - char.ToUpperInvariant(right[index]);
            if (difference != 0) return difference;
        }
        return left.Length - right.Length;
    }

    internal static string ReplaceLiteral(string value, string target, string replacement) =>
        value.Replace(target, replacement, StringComparison.Ordinal);

    private static GuestExceptionCarrier StringIndexOutOfBounds(int index) =>
        new(GuestThrowableMetadata.Create("Ljava/lang/StringIndexOutOfBoundsException;", index.ToString(CultureInfo.InvariantCulture)));

    private static bool JavaRegionMatches(AndroidFrameworkState state, string left, bool ignoreCase, int toffset, string other, int ooffset, int length)
    {
        if (toffset < 0 || ooffset < 0 || toffset > left.Length - length || ooffset > other.Length - length) return false;
        string leftSlice = left.Substring(toffset, length);
        string rightSlice = other.Substring(ooffset, length);
        return ignoreCase ? AndroidApiBindings.JavaEqualsIgnoreCase(leftSlice, rightSlice) : string.Equals(leftSlice, rightSlice, StringComparison.Ordinal);
    }

    private static int JavaLastIndexOf(string value, string search, int from)
    {
        if (from >= value.Length) return value.LastIndexOf(search, StringComparison.Ordinal);
        if (search.Length == 0) return Math.Min(from, value.Length);
        int bound = Math.Max(from, 0);
        for (int index = Math.Min(bound, value.Length - search.Length); index >= 0; index--)
        {
            if (string.CompareOrdinal(value, index, search, 0, search.Length) == 0) return index;
        }
        return -1;
    }

    private static int CodePointAt(string value, int index)
    {
        if (index < 0 || index >= value.Length) throw StringIndexOutOfBounds(index);
        if (char.IsHighSurrogate(value[index]) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
            return char.ConvertToUtf32(value[index], value[index + 1]);
        return value[index];
    }

    private static int CodePointBefore(string value, int index)
    {
        if (index <= 0 || index > value.Length) throw StringIndexOutOfBounds(index);
        if (index >= 2 && char.IsLowSurrogate(value[index - 1]) && char.IsHighSurrogate(value[index - 2]))
            return char.ConvertToUtf32(value[index - 2], value[index - 1]);
        return value[index - 1];
    }

    private static int CodePointCount(string value, int begin, int end)
    {
        if (begin < 0 || end > value.Length || begin > end) throw StringIndexOutOfBounds(end - begin);
        int count = 0;
        for (int index = begin; index < end; index++)
        {
            if (char.IsHighSurrogate(value[index]) && index + 1 < end && char.IsLowSurrogate(value[index + 1])) index++;
            count++;
        }
        return count;
    }

    private static int OffsetByCodePoints(string value, int index, int codePointOffset)
    {
        if (index < 0 || index > value.Length) throw StringIndexOutOfBounds(index);
        if (codePointOffset == 0) return index;
        int position = index;
        int step = codePointOffset > 0 ? 1 : -1;
        int remaining = Math.Abs(codePointOffset);
        while (remaining > 0)
        {
            if (step > 0)
            {
                if (position >= value.Length) throw StringIndexOutOfBounds(position);
                if (char.IsHighSurrogate(value[position]) && position + 1 < value.Length && char.IsLowSurrogate(value[position + 1])) position++;
                position++;
            }
            else
            {
                if (position <= 0) throw StringIndexOutOfBounds(position);
                if (position >= 2 && char.IsLowSurrogate(value[position - 1]) && char.IsHighSurrogate(value[position - 2])) position--;
                position--;
            }
            remaining--;
        }
        return position;
    }

    private static DexArray Split(string value, string regex, int limit)
    {
        // Real Java split semantics over .NET Regex (same Perl-derived core
        // syntax; dialect differences flagged in README). limit == 0 drops
        // trailing empty strings; limit < 0 keeps them; limit > 0 caps the count.
        var parts = new List<string>();
        if (regex.Length == 0)
        {
            for (int index = 0; index < value.Length; index++) parts.Add(value[index].ToString());
            if (limit == 0)
            {
                while (parts.Count > 0 && parts[^1].Length == 0) parts.RemoveAt(parts.Count - 1);
            }
        }
        else
        {
            var matches = Regex.Matches(value, regex);
            int last = 0;
            int count = 0;
            foreach (Match match in matches)
            {
                if (limit > 0 && count >= limit - 1) break;
                parts.Add(value[last..match.Index]);
                last = match.Index + match.Length;
                count++;
            }
            parts.Add(value[last..]);
            if (limit == 0)
            {
                while (parts.Count > 0 && parts[^1].Length == 0) parts.RemoveAt(parts.Count - 1);
            }
        }
        var array = new DexArray("[Ljava/lang/String;", parts.Count);
        for (int index = 0; index < parts.Count; index++) array.Set(index, parts[index]);
        return array;
    }

    private static string Format(AndroidFrameworkState state, string format, object?[] varargs)
    {
        // Bounded printf subset: %s %d %f %x %X %b %c %n %% plus basic width,
        // precision, and positional arguments (%1$s). Not a full printf engine.
        var builder = new StringBuilder();
        int argIndex = 0;
        int length = format.Length;
        for (int index = 0; index < length; index++)
        {
            char current = format[index];
            if (current != '%')
            {
                builder.Append(current);
                continue;
            }
            if (index + 1 >= length)
            {
                builder.Append('%');
                break;
            }
            index++;
            char specifier = format[index];
            if (specifier == '%')
            {
                builder.Append('%');
                continue;
            }
            if (specifier == 'n')
            {
                builder.Append(Environment.NewLine);
                continue;
            }
            // Parse optional positional "%N$" and flags/width/precision.
            int? position = null;
            int width = 0;
            int precision = -1;
            bool leftJustify = false;
            char pad = ' ';
            while (index < length)
            {
                char look = format[index];
                if (look == '-' ) { leftJustify = true; index++; }
                else if (look == '0' && width == 0) { pad = '0'; index++; }
                else if (char.IsDigit(look))
                {
                    int start = index;
                    while (index < length && char.IsDigit(format[index])) index++;
                    if (index < length && format[index] == '$')
                    {
                        position = int.Parse(format[start..index], CultureInfo.InvariantCulture) - 1;
                        index++;
                    }
                    else
                    {
                        width = int.Parse(format[start..index], CultureInfo.InvariantCulture);
                    }
                }
                else if (look == '.')
                {
                    index++;
                    int start = index;
                    while (index < length && char.IsDigit(format[index])) index++;
                    precision = start == index ? 0 : int.Parse(format[start..index], CultureInfo.InvariantCulture);
                }
                else break;
            }
            if (index >= length) break;
            char conversion = format[index];
            if (position.HasValue) argIndex = position.Value;
            if (argIndex >= varargs.Length)
            {
                builder.Append('%').Append(conversion);
                index++;
                continue;
            }
            object? argument = varargs[argIndex++];
            object numeric = NumericArgument(state, argument);
            string? rendered = conversion switch
            {
                's' => ValueOfObject(state, argument),
                'd' => Convert.ToInt64(numeric, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                'f' => JavaDouble(Convert.ToDouble(numeric, CultureInfo.InvariantCulture)),
                'x' => Convert.ToInt64(numeric, CultureInfo.InvariantCulture).ToString("x", CultureInfo.InvariantCulture),
                'X' => Convert.ToInt64(numeric, CultureInfo.InvariantCulture).ToString("X", CultureInfo.InvariantCulture),
                'b' => argument is null || (argument is int zero && zero == 0) ? "false" : "true",
                'c' => ((char)Convert.ToInt32(numeric, CultureInfo.InvariantCulture)).ToString(),
                _ => null
            };
            if (rendered is null)
            {
                builder.Append('%').Append(conversion);
                continue;
            }
            if (conversion == 'f' && precision >= 0)
            {
                double numericDouble = Convert.ToDouble(numeric, CultureInfo.InvariantCulture);
                rendered = numericDouble.ToString("F" + precision, CultureInfo.InvariantCulture);
            }
            else if (conversion == 's' && precision >= 0 && rendered.Length > precision)
            {
                rendered = rendered[..precision];
            }
            if (width > 0 && rendered.Length < width)
            {
                rendered = leftJustify ? rendered.PadRight(width) : rendered.PadLeft(width, pad);
            }
            builder.Append(rendered);
        }
        return builder.ToString();
    }

    private static object?[] VarArgs(AndroidFrameworkState state, object value) =>
        value is DexArray array ? Enumerable.Range(0, array.Length).Select(array.Get).ToArray() : [];

    /// <summary>Unboxes a boxed-primitive DexObject argument to its raw CLR
    /// IConvertible value (int/long/double) for the numeric format specifiers;
    /// non-DexObject arguments pass through unchanged (already-raw CLR values
    /// keep working exactly as before — additive, not a rewrite).</summary>
    private static object NumericArgument(AndroidFrameworkState state, object? argument) =>
        argument is DexObject boxed && state.Boxed.TryGet(boxed, out var peer) ? peer.RawValue : argument!;

    private static string FromCharArray(AndroidFrameworkState state, DexArray array)
    {
        var chars = new char[array.Length];
        for (int index = 0; index < array.Length; index++) chars[index] = (char)RequireInt(array.Get(index) ?? 0);
        return new string(chars);
    }

    private static void GetChars(AndroidFrameworkState state, string value, int srcBegin, int srcEnd, DexArray destination, int dstBegin)
    {
        if (srcBegin < 0 || srcEnd > value.Length || srcBegin > srcEnd) throw StringIndexOutOfBounds(srcEnd - srcBegin);
        for (int index = srcBegin; index < srcEnd; index++)
        {
            int destinationIndex = dstBegin + (index - srcBegin);
            if ((uint)destinationIndex >= (uint)destination.Length) throw StringIndexOutOfBounds(destinationIndex);
            destination.Set(destinationIndex, (int)value[index]);
        }
    }

    private static DexArray ToBytes(string value) => ToBytes(value, "UTF-8");

    private static DexArray ToBytes(string value, string charsetName)
    {
        // UTF-8 is the documented safe default. An explicit charset name is honored
        // when known; unknown names fail closed with guest UnsupportedEncodingException
        // (real Java contract) instead of silently picking a different encoding.
        Encoding encoding = charsetName.ToUpperInvariant() switch
        {
            "UTF-8" or "UTF8" => Encoding.UTF8,
            "ISO-8859-1" or "LATIN1" or "ISO8859_1" => Encoding.Latin1,
            "US-ASCII" or "ASCII" => Encoding.ASCII,
            _ => throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/io/UnsupportedEncodingException;", charsetName))
        };
        byte[] bytes = encoding.GetBytes(value);
        var array = new DexArray("[B", bytes.Length);
        for (int index = 0; index < bytes.Length; index++) array.Set(index, (int)bytes[index]);
        return array;
    }

    private static string ValueOfObject(AndroidFrameworkState state, object? value)
    {
        if (value is null) return "null";
        if (value is string text) return text;
        if (value is DexObject guest && guest.TypeDescriptor == "Ljava/lang/StringBuilder;")
            return state.StringBuilders.Get(guest).ToString();
        // Real contract: obj.toString(). A guest object's toString resolves through
        // the interpreter's virtual chain (guest override or bound Object.toString);
        // a framework-typed object (e.g. a boxed primitive) falls back to the bound
        // framework toString (Integer.toString etc. — registered in BoxingBindings).
        if (value is DexObject guestObject && state.Interpreter is not null)
        {
            try
            {
                return state.Interpreter.InvokeVirtualInstanceExact(guestObject, "toString", "()Ljava/lang/String;") as string ?? guestObject.TypeDescriptor;
            }
            catch (MissingMethodException)
            {
                try
                {
                    return state.Interpreter.InvokeFrameworkExact(guestObject.TypeDescriptor, "toString", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, guestObject) as string ?? guestObject.TypeDescriptor;
                }
                catch (Exception) { return guestObject.TypeDescriptor; }
            }
            catch (Exception) { return guestObject.TypeDescriptor; }
        }
        return value.ToString() ?? string.Empty;
    }

    private static string JavaDouble(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    private static string RequireString(object value, bool allowNull = false) => AndroidApiBindings.RequireString(value, allowNull);
    private static int RequireInt(object value) => AndroidApiBindings.RequireInt(value);
    private static long RequireLong(object value) => AndroidApiBindings.RequireLong(value);
    private static double RequireDouble(object value) => value is double d ? d : value is float f ? f : throw new ArgumentException("Expected a double.");
    private static string? AsText(AndroidFrameworkState state, object value) => AndroidApiBindings.AsText(state, value);
    private static DexArray RequireCharArray(object value) => value as DexArray ?? throw new ArgumentException("Expected a char[].");
}
