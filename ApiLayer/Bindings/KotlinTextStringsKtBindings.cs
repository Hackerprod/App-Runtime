#nullable enable
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for kotlin.text.StringsKt — Kotlin's String extension-function
/// library. The class IS guest-defined in SKYNET's dex (Kotlin bundles the
/// stdlib), but R8 shrunk it to almost nothing: confirmed with the interpreter's
/// own DexFileSet.FindMethodExact that every method below is a GAP, so these are
/// genuine framework-binding gaps. The bound core subset mirrors what
/// java.lang.String already has and DELEGATES to the same logic
/// (JavaLangStringBindings/AndroidApiBindings helpers) instead of reimplementing.
///
/// Kotlin's `$default` synthetic-wrapper convention: a function with default
/// parameters compiles to the real function plus a &lt;name&gt;$default wrapper with
/// two extra trailing parameters — an int bitmask (bit N set = parameter N was
/// omitted, use its default) and an Object marker (always null, ABI padding).
/// Each wrapper applies the mask substitution once, then delegates.
///
/// NOT bound: getIndices (returns kotlin.ranges.IntRange — no representation in
/// this runtime, skipped per brief), toIntOrNull/toLongOrNull (return Integer/
/// Long — this runtime has no boxing model; a binding cannot produce a usable
/// boxed value, reported as a representation boundary if the run reaches them),
/// and the ~150 remaining StringsKt references (substringBefore/After family,
/// removePrefix/Suffix, capitalize, lines, windowed, sequences, radix overloads,
/// etc.) — deliberately out of scope until the real run confirms them.
/// </summary>
internal static class KotlinTextStringsKtBindings
{
    private const string K = "Lkotlin/text/StringsKt;";

    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        // ---- replace ----
        RegisterWithMask(builder, K, "replace", "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;Z)Ljava/lang/String;", "ILjava/lang/Object;", 1, (args) => KotlinReplace(RequireString(args[0]), RequireString(args[1]), RequireString(args[2]), RequireInt(args[3]) != 0));
        RegisterWithMask(builder, K, "replace", "(Ljava/lang/String;CCZ)Ljava/lang/String;", "ILjava/lang/Object;", 1, (args) => KotlinReplaceChar(RequireString(args[0]), (char)RequireInt(args[1]), (char)RequireInt(args[2]), RequireInt(args[3]) != 0));

        // ---- contains ----
        RegisterWithMask(builder, K, "contains", "(Ljava/lang/CharSequence;CZ)Z", "ILjava/lang/Object;", 1, (args) => Contains(RequireText(args[0]), ((char)RequireInt(args[1])).ToString(), RequireInt(args[2]) != 0) ? 1 : 0);
        RegisterWithMask(builder, K, "contains", "(Ljava/lang/CharSequence;Ljava/lang/CharSequence;Z)Z", "ILjava/lang/Object;", 1, (args) => Contains(RequireText(args[0]), RequireText(args[1]), RequireInt(args[2]) != 0) ? 1 : 0);

        // ---- startsWith / endsWith ----
        RegisterWithMask(builder, K, "startsWith", "(Ljava/lang/CharSequence;CZ)Z", "ILjava/lang/Object;", 1, (args) => StartsWith(RequireText(args[0]), ((char)RequireInt(args[1])).ToString(), 0, RequireInt(args[2]) != 0) ? 1 : 0);
        RegisterWithMask(builder, K, "startsWith", "(Ljava/lang/CharSequence;Ljava/lang/CharSequence;Z)Z", "ILjava/lang/Object;", 1, (args) => StartsWith(RequireText(args[0]), RequireText(args[1]), 0, RequireInt(args[2]) != 0) ? 1 : 0);
        RegisterWithMask(builder, K, "startsWith", "(Ljava/lang/String;Ljava/lang/String;Z)Z", "ILjava/lang/Object;", 1, (args) => StartsWith(RequireString(args[0]), RequireString(args[1]), 0, RequireInt(args[2]) != 0) ? 1 : 0);
        RegisterWithMask(builder, K, "startsWith", "(Ljava/lang/String;Ljava/lang/String;IZ)Z", "ILjava/lang/Object;", 2, (args) => StartsWith(RequireString(args[0]), RequireString(args[1]), RequireInt(args[2]), RequireInt(args[3]) != 0) ? 1 : 0);
        RegisterWithMask(builder, K, "endsWith", "(Ljava/lang/CharSequence;CZ)Z", "ILjava/lang/Object;", 1, (args) => EndsWith(RequireText(args[0]), ((char)RequireInt(args[1])).ToString(), RequireInt(args[2]) != 0) ? 1 : 0);
        RegisterWithMask(builder, K, "endsWith", "(Ljava/lang/CharSequence;Ljava/lang/CharSequence;Z)Z", "ILjava/lang/Object;", 1, (args) => EndsWith(RequireText(args[0]), RequireText(args[1]), RequireInt(args[2]) != 0) ? 1 : 0);
        RegisterWithMask(builder, K, "endsWith", "(Ljava/lang/String;Ljava/lang/String;Z)Z", "ILjava/lang/Object;", 1, (args) => EndsWith(RequireString(args[0]), RequireString(args[1]), RequireInt(args[2]) != 0) ? 1 : 0);

        // ---- indexOf / lastIndexOf ----
        RegisterWithMask(builder, K, "indexOf", "(Ljava/lang/CharSequence;CIZ)I", "(IZILjava/lang/Object;)I", 2, (args) => IndexOf(RequireText(args[0]), ((char)RequireInt(args[1])).ToString(), RequireInt(args[2]), RequireInt(args[3]) != 0));
        RegisterWithMask(builder, K, "indexOf", "(Ljava/lang/CharSequence;Ljava/lang/String;IZ)I", "(IZILjava/lang/Object;)I", 2, (args) => IndexOf(RequireText(args[0]), RequireString(args[1]), RequireInt(args[2]), RequireInt(args[3]) != 0));
        RegisterWithMask(builder, K, "lastIndexOf", "(Ljava/lang/CharSequence;CIZ)I", "(IZILjava/lang/Object;)I", 2, (args) => LastIndexOf(RequireText(args[0]), ((char)RequireInt(args[1])).ToString(), RequireInt(args[2]), RequireInt(args[3]) != 0));
        RegisterWithMask(builder, K, "lastIndexOf", "(Ljava/lang/CharSequence;Ljava/lang/String;IZ)I", "(IZILjava/lang/Object;)I", 2, (args) => LastIndexOf(RequireText(args[0]), RequireString(args[1]), RequireInt(args[2]), RequireInt(args[3]) != 0));

        // ---- equals / compareTo ----
        builder.Register(Api(K, "equals", "(Ljava/lang/String;Ljava/lang/String;Z)Z"), (_, args) => RequireInt(args[2]) != 0
            ? AndroidApiBindings.JavaEqualsIgnoreCase(RequireString(args[0]), RequireString(args[1])) ? 1 : 0
            : string.Equals(RequireString(args[0]), RequireString(args[1]), StringComparison.Ordinal) ? 1 : 0);
        builder.Register(Api(K, "compareTo", "(Ljava/lang/String;Ljava/lang/String;Z)I"), (_, args) => RequireInt(args[2]) != 0
            ? JavaLangStringBindings.JavaCompareToIgnoreCase(RequireString(args[0]), RequireString(args[1]))
            : JavaLangStringBindings.JavaCompareTo(RequireString(args[0]), RequireString(args[1])));

        // ---- trim family (same <= ' ' convention as the established Java trim) ----
        builder.Register(Api(K, "trim", "(Ljava/lang/CharSequence;)Ljava/lang/CharSequence;"), (_, args) => AndroidApiBindings.JavaTrim(RequireText(args[0])));
        builder.Register(Api(K, "trimStart", "(Ljava/lang/CharSequence;)Ljava/lang/CharSequence;"), (_, args) => TrimStart(RequireText(args[0])));
        builder.Register(Api(K, "trimEnd", "(Ljava/lang/CharSequence;)Ljava/lang/CharSequence;"), (_, args) => TrimEnd(RequireText(args[0])));

        // ---- isBlank ----
        builder.Register(Api(K, "isBlank", "(Ljava/lang/CharSequence;)Z"), (_, args) => IsBlank(RequireText(args[0])) ? 1 : 0);

        // ---- take / drop ----
        builder.Register(Api(K, "take", "(Ljava/lang/String;I)Ljava/lang/String;"), (_, args) => Take(RequireString(args[0]), RequireInt(args[1])));
        builder.Register(Api(K, "take", "(Ljava/lang/CharSequence;I)Ljava/lang/CharSequence;"), (_, args) => Take(RequireText(args[0]), RequireInt(args[1])));
        builder.Register(Api(K, "drop", "(Ljava/lang/String;I)Ljava/lang/String;"), (_, args) => Drop(RequireString(args[0]), RequireInt(args[1])));

        // ---- padStart / padEnd ----
        builder.Register(Api(K, "padStart", "(Ljava/lang/String;IC)Ljava/lang/String;"), (_, args) => PadStart(RequireString(args[0]), RequireInt(args[1]), (char)RequireInt(args[2])));
        builder.Register(Api(K, "padStart", "(Ljava/lang/CharSequence;IC)Ljava/lang/CharSequence;"), (_, args) => PadStart(RequireText(args[0]), RequireInt(args[1]), (char)RequireInt(args[2])));
        builder.Register(Api(K, "padEnd", "(Ljava/lang/String;IC)Ljava/lang/String;"), (_, args) => PadEnd(RequireString(args[0]), RequireInt(args[1]), (char)RequireInt(args[2])));
        builder.Register(Api(K, "padEnd", "(Ljava/lang/CharSequence;IC)Ljava/lang/CharSequence;"), (_, args) => PadEnd(RequireText(args[0]), RequireInt(args[1]), (char)RequireInt(args[2])));

        // ---- repeat ----
        builder.Register(Api(K, "repeat", "(Ljava/lang/CharSequence;I)Ljava/lang/String;"), (_, args) => Repeat(RequireText(args[0]), RequireInt(args[1])));

        // ---- split (char-array delimiters) + $default (mask: bit0=Z ignoreCase,
        // bit1=I limit) ----
        RegisterWithMask(builder, K, "split", "(Ljava/lang/CharSequence;[CZI)Ljava/util/List;", "ILjava/lang/Object;", 2, (args) => SplitByChars(state, RequireText(args[0]), RequireCharArray(args[1]), RequireInt(args[2]) != 0, RequireInt(args[3])));

        // ---- trivial ----
        builder.Register(Api(K, "getLastIndex", "(Ljava/lang/CharSequence;)I"), (_, args) => RequireText(args[0]).Length - 1);
        builder.Register(Api(K, "toCharArray", "(Ljava/lang/String;II)[C"), (_, args) => ToCharArray(RequireString(args[0]), RequireInt(args[1]), RequireInt(args[2])));
    }

    // ---------------------------------------------------------------------------
    // $default mask wrapper
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Registers the real function and its $default twin. The wrapper receives the
    /// full argument list INCLUDING the trailing mask+marker; for each optional
    /// parameter whose bit is set in the mask, the passed value is replaced by its
    /// default (bit 0 = first optional param, bit 1 = second, ...). The marker
    /// Object is always null and never read.
    /// </summary>
    private static void RegisterWithMask(
        AndroidApiRegistryBuilder builder,
        string owner,
        string name,
        string realDescriptor,
        string defaultSuffix,
        int optionalCount,
        Func<object[], object> core)
    {
        builder.Register(Api(owner, name, realDescriptor), (_, args) => core(args));
        // The $default descriptor = the real descriptor with the mask+marker params
        // inserted before the parameter-list close. defaultSuffix carries ONLY those
        // extra params ("ILjava/lang/Object;" = int mask + Object marker); the real
        // optional params (Z/I) are already in realDescriptor.
        int close = realDescriptor.IndexOf(')');
        string defaultDescriptor = realDescriptor[..close] + defaultSuffix + realDescriptor[close..];
        builder.Register(Api(owner, name + "$default", defaultDescriptor), (_, args) =>
        {
            // The optional params occupy the LAST slots before the mask; the mask is
            // the second-to-last arg, the marker the last. A bit set means "use the
            // method's default instead of the passed value" — every optional param
            // in this surface defaults to 0/false, so the substitution is uniform.
            int maskIndex = args.Length - 2;
            int mask = RequireInt(args[maskIndex]);
            int optionalStart = maskIndex - optionalCount;
            var adjusted = (object[])args.Clone();
            for (int bit = 0; bit < optionalCount; bit++)
            {
                if (((mask >> bit) & 1) != 0)
                    adjusted[optionalStart + bit] = 0;
            }
            return core(adjusted);
        });
    }

    // ---------------------------------------------------------------------------
    // Core logic (delegates to the established Java String logic where identical)
    // ---------------------------------------------------------------------------

    private static string KotlinReplace(string value, string oldValue, string newValue, bool ignoreCase)
    {
        if (!ignoreCase) return JavaLangStringBindings.ReplaceLiteral(value, oldValue, newValue);
        // Real Kotlin with ignoreCase uses regex with the CASE_INSENSITIVE flag on
        // the quoted pattern.
        return Regex.Replace(value, Regex.Escape(oldValue), newValue, RegexOptions.IgnoreCase);
    }

    private static string KotlinReplaceChar(string value, char oldChar, char newChar, bool ignoreCase)
    {
        if (!ignoreCase) return value.Replace(oldChar, newChar);
        return Regex.Replace(value, Regex.Escape(oldChar.ToString()), newChar.ToString(), RegexOptions.IgnoreCase);
    }

    private static bool Contains(string value, string other, bool ignoreCase)
    {
        if (other.Length == 0) return true;
        if (!ignoreCase) return value.Contains(other, StringComparison.Ordinal);
        for (int index = 0; index <= value.Length - other.Length; index++)
        {
            if (AndroidApiBindings.JavaEqualsIgnoreCase(value.Substring(index, other.Length), other)) return true;
        }
        return false;
    }

    private static bool StartsWith(string value, string prefix, int fromIndex, bool ignoreCase)
    {
        if (fromIndex < 0) fromIndex = 0;
        if (fromIndex > value.Length || prefix.Length > value.Length - fromIndex) return false;
        return ignoreCase
            ? AndroidApiBindings.JavaEqualsIgnoreCase(value.Substring(fromIndex, prefix.Length), prefix)
            : string.CompareOrdinal(value, fromIndex, prefix, 0, prefix.Length) == 0;
    }

    private static bool EndsWith(string value, string suffix, bool ignoreCase)
    {
        if (suffix.Length > value.Length) return false;
        return ignoreCase
            ? AndroidApiBindings.JavaEqualsIgnoreCase(value.Substring(value.Length - suffix.Length), suffix)
            : value.EndsWith(suffix, StringComparison.Ordinal);
    }

    private static int IndexOf(string value, string search, int fromIndex, bool ignoreCase)
    {
        int start = Math.Clamp(fromIndex, 0, value.Length);
        if (search.Length == 0) return start;
        if (!ignoreCase) return value.IndexOf(search, start, StringComparison.Ordinal);
        for (int index = start; index <= value.Length - search.Length; index++)
        {
            if (AndroidApiBindings.JavaEqualsIgnoreCase(value.Substring(index, search.Length), search)) return index;
        }
        return -1;
    }

    private static int LastIndexOf(string value, string search, int fromIndex, bool ignoreCase)
    {
        if (search.Length == 0) return Math.Min(fromIndex, value.Length);
        int bound = Math.Clamp(fromIndex, 0, value.Length - search.Length);
        if (!ignoreCase)
        {
            for (int index = bound; index >= 0; index--)
            {
                if (string.CompareOrdinal(value, index, search, 0, search.Length) == 0) return index;
            }
            return -1;
        }
        for (int index = bound; index >= 0; index--)
        {
            if (AndroidApiBindings.JavaEqualsIgnoreCase(value.Substring(index, search.Length), search)) return index;
        }
        return -1;
    }

    private static string TrimStart(string value)
    {
        int start = 0;
        while (start < value.Length && value[start] <= ' ') start++;
        return value[start..];
    }

    private static string TrimEnd(string value)
    {
        int end = value.Length;
        while (end > 0 && value[end - 1] <= ' ') end--;
        return value[..end];
    }

    private static bool IsBlank(string value)
    {
        foreach (char c in value)
        {
            if (!char.IsWhiteSpace(c)) return false;
        }
        return true;
    }

    private static string Take(string value, int count)
    {
        if (count < 0) throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/IllegalArgumentException;", "Requested character count " + count + " is less than zero."));
        return value[..Math.Min(count, value.Length)];
    }

    private static string Drop(string value, int count)
    {
        if (count < 0) throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/IllegalArgumentException;", "Requested character count " + count + " is less than zero."));
        return count >= value.Length ? string.Empty : value[count..];
    }

    private static string PadStart(string value, int length, char padChar)
    {
        if (value.Length >= length) return value;
        return new string(padChar, length - value.Length) + value;
    }

    private static string PadEnd(string value, int length, char padChar)
    {
        if (value.Length >= length) return value;
        return value + new string(padChar, length - value.Length);
    }

    private static string Repeat(string value, int count)
    {
        if (count < 0) throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/IllegalArgumentException;", "Count " + count + " is less than zero."));
        if (count == 0 || value.Length == 0) return string.Empty;
        var builder = new StringBuilder(value.Length * count);
        for (int index = 0; index < count; index++) builder.Append(value);
        return builder.ToString();
    }

    private static object SplitByChars(AndroidFrameworkState state, string value, DexArray delimiters, bool ignoreCase, int limit)
    {
        // Real Kotlin split(delimiters: CharArray): each char is a LITERAL
        // delimiter; limit semantics match the Java split (0 drops trailing
        // empties, <0 keeps them, >0 caps). The result is a java.util.List.
        var parts = new List<string>();
        if (delimiters.Length == 0)
        {
            parts.Add(value);
        }
        else
        {
            var separatorChars = new char[delimiters.Length];
            for (int index = 0; index < delimiters.Length; index++) separatorChars[index] = (char)RequireInt(delimiters.Get(index) ?? 0);
            string[] pieces = value.Split(separatorChars, StringSplitOptions.None);
            parts.AddRange(pieces);
            if (limit == 0)
            {
                while (parts.Count > 0 && parts[^1].Length == 0) parts.RemoveAt(parts.Count - 1);
            }
            else if (limit > 0 && parts.Count > limit)
            {
                // Java/Kotlin positive-limit: keep the first limit-1 pieces and
                // concatenate the remainder back.
                var remainder = string.Join(separatorChars[0], parts.Skip(limit - 1).ToArray());
                parts.RemoveRange(limit - 1, parts.Count - (limit - 1));
                parts.Add(remainder);
            }
        }
        var listObject = new DexObject("Ljava/util/ArrayList;");
        var peer = new ListPeer();
        peer.Elements.AddRange(parts);
        state.ArrayLists.Add(listObject, peer);
        return listObject;
    }

    private static DexArray ToCharArray(string value, int startIndex, int endIndex)
    {
        if (startIndex < 0 || endIndex > value.Length || startIndex > endIndex)
            throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/IndexOutOfBoundsException;", "startIndex " + startIndex + ", endIndex " + endIndex + ", length " + value.Length));
        var array = new DexArray("[C", endIndex - startIndex);
        for (int index = startIndex; index < endIndex; index++) array.Set(index - startIndex, (int)value[index]);
        return array;
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    private static string RequireString(object value, bool allowNull = false) => AndroidApiBindings.RequireString(value, allowNull);
    private static int RequireInt(object value) => AndroidApiBindings.RequireInt(value);
    private static string RequireText(object value) => AndroidApiBindings.AsText(null, value) ?? throw new ArgumentException("Expected a CharSequence.");
    private static DexArray RequireCharArray(object value) => value as DexArray ?? throw new ArgumentException("Expected a char[].");
}
