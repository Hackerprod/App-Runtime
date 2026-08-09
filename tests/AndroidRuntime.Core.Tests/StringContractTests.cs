using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for the completed java.lang.String contract: substring/charAt bounds with
/// real StringIndexOutOfBoundsException, compareTo/replace/regionMatches,
/// regex-backed split (Java trailing-empty semantics) / matches / replaceAll,
/// bounded format, getBytes (UTF-8 + charset name handling), and Unicode code
/// point helpers.
/// </summary>
public sealed class StringContractTests
{
    private const string StringClass = "Ljava/lang/String;";

    [Fact]
    public void Substring_and_char_at_follow_the_real_contract()
    {
        var (state, registry, _) = Session();
        var value = "hello world";

        Assert.Equal("world", Invoke(registry, state, StringClass, "substring", "(I)Ljava/lang/String;", AndroidInvokeKind.Virtual, value, 6));
        Assert.Equal("hello", Invoke(registry, state, StringClass, "substring", "(II)Ljava/lang/String;", AndroidInvokeKind.Virtual, value, 0, 5));
        // index == length is legal (empty result); begin > end / out-of-range throws.
        Assert.Equal("", Invoke(registry, state, StringClass, "substring", "(II)Ljava/lang/String;", AndroidInvokeKind.Virtual, value, 11, 11));
        Assert.Equal(104, Invoke(registry, state, StringClass, "charAt", "(I)C", AndroidInvokeKind.Virtual, value, 0));

        var bounds = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, StringClass, "substring", "(I)Ljava/lang/String;", AndroidInvokeKind.Virtual, value, 12));
        Assert.Equal("Ljava/lang/StringIndexOutOfBoundsException;", bounds.Throwable.TypeDescriptor);
        Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, StringClass, "substring", "(II)Ljava/lang/String;", AndroidInvokeKind.Virtual, value, 5, 2));
        Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, StringClass, "charAt", "(I)C", AndroidInvokeKind.Virtual, value, -1));
    }

    [Fact]
    public void Compare_to_and_compare_to_ignore_case_match_java_ordering()
    {
        var (state, registry, _) = Session();
        Assert.True((int)Invoke(registry, state, StringClass, "compareTo", "(Ljava/lang/String;)I", AndroidInvokeKind.Virtual, "abc", "abd") < 0);
        Assert.True((int)Invoke(registry, state, StringClass, "compareTo", "(Ljava/lang/String;)I", AndroidInvokeKind.Virtual, "abd", "abc") > 0);
        Assert.Equal(0, Invoke(registry, state, StringClass, "compareTo", "(Ljava/lang/String;)I", AndroidInvokeKind.Virtual, "abc", "abc"));
        // Shorter prefix sorts before the longer string.
        Assert.True((int)Invoke(registry, state, StringClass, "compareTo", "(Ljava/lang/String;)I", AndroidInvokeKind.Virtual, "abc", "abcd") < 0);
        Assert.Equal(0, Invoke(registry, state, StringClass, "compareToIgnoreCase", "(Ljava/lang/String;)I", AndroidInvokeKind.Virtual, "ABC", "abc"));
    }

    [Fact]
    public void Replace_is_literal_and_case_conversion_is_invariant()
    {
        var (state, registry, _) = Session();
        Assert.Equal("hxllo", Invoke(registry, state, StringClass, "replace", "(CC)Ljava/lang/String;", AndroidInvokeKind.Virtual, "hello", (int)'e', (int)'x'));
        // CharSequence overload is LITERAL, not regex: "$" has no special meaning.
        Assert.Equal("a$b$c", Invoke(registry, state, StringClass, "replace", "(Ljava/lang/CharSequence;Ljava/lang/CharSequence;)Ljava/lang/String;", AndroidInvokeKind.Virtual, "a.b.c", ".", "$"));
        Assert.Equal("HELLO", Invoke(registry, state, StringClass, "toUpperCase", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, "hello"));
        Assert.Equal("hello", Invoke(registry, state, StringClass, "toLowerCase", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, "HELLO"));
    }

    [Fact]
    public void Region_matches_and_starts_with_offset_follow_java()
    {
        var (state, registry, _) = Session();
        Assert.Equal(1, Invoke(registry, state, StringClass, "regionMatches", "(ILjava/lang/String;II)Z", AndroidInvokeKind.Virtual, "hello world", 6, "world", 0, 5));
        Assert.Equal(0, Invoke(registry, state, StringClass, "regionMatches", "(ILjava/lang/String;II)Z", AndroidInvokeKind.Virtual, "hello world", 6, "World", 0, 5));
        Assert.Equal(1, Invoke(registry, state, StringClass, "regionMatches", "(ZILjava/lang/String;II)Z", AndroidInvokeKind.Virtual, "hello world", 1, 6, "WORLD", 0, 5));
        Assert.Equal(1, Invoke(registry, state, StringClass, "startsWith", "(Ljava/lang/String;I)Z", AndroidInvokeKind.Virtual, "hello world", "world", 6));
        Assert.Equal(0, Invoke(registry, state, StringClass, "startsWith", "(Ljava/lang/String;I)Z", AndroidInvokeKind.Virtual, "hello", "hi", 0));
    }

    [Fact]
    public void Split_implements_java_trailing_empty_semantics()
    {
        var (state, registry, _) = Session();
        // Default split (limit 0) drops trailing empty strings.
        var parts = (DexArray)Invoke(registry, state, StringClass, "split", "(Ljava/lang/String;)[Ljava/lang/String;", AndroidInvokeKind.Virtual, "a,b,c", ",");
        Assert.Equal(3, parts.Length);
        Assert.Equal("a", parts.Get(0));
        Assert.Equal("b", parts.Get(1));
        Assert.Equal("c", parts.Get(2));

        var trailing = (DexArray)Invoke(registry, state, StringClass, "split", "(Ljava/lang/String;)[Ljava/lang/String;", AndroidInvokeKind.Virtual, "a,b,", ",");
        Assert.Equal(2, trailing.Length);

        // Negative limit keeps trailing empties.
        var keepAll = (DexArray)Invoke(registry, state, StringClass, "split", "(Ljava/lang/String;I)[Ljava/lang/String;", AndroidInvokeKind.Virtual, "a,b,", ",", -1);
        Assert.Equal(3, keepAll.Length);

        // Positive limit caps the number of pieces.
        var capped = (DexArray)Invoke(registry, state, StringClass, "split", "(Ljava/lang/String;I)[Ljava/lang/String;", AndroidInvokeKind.Virtual, "a,b,c", ",", 2);
        Assert.Equal(2, capped.Length);
        Assert.Equal("a", capped.Get(0));
        Assert.Equal("b,c", capped.Get(1));
    }

    [Fact]
    public void Matches_and_replace_all_use_regex()
    {
        var (state, registry, _) = Session();
        Assert.Equal(1, Invoke(registry, state, StringClass, "matches", "(Ljava/lang/String;)Z", AndroidInvokeKind.Virtual, "abc123", "[a-z]+[0-9]+"));
        Assert.Equal(0, Invoke(registry, state, StringClass, "matches", "(Ljava/lang/String;)Z", AndroidInvokeKind.Virtual, "abc123", "[0-9]+"));
        Assert.Equal("aXbXc", Invoke(registry, state, StringClass, "replaceAll", "(Ljava/lang/String;Ljava/lang/String;)Ljava/lang/String;", AndroidInvokeKind.Virtual, "a.b.c", "\\.", "X"));
    }

    [Fact]
    public void Format_covers_the_bounded_specifier_subset()
    {
        var (state, registry, _) = Session();
        var varargs = new DexArray("[Ljava/lang/Object;", 3);
        varargs.Set(0, "world");
        varargs.Set(1, "you");
        varargs.Set(2, "!");

        Assert.Equal("hello world you !", Invoke(registry, state, StringClass, "format", "(Ljava/lang/String;[Ljava/lang/Object;)Ljava/lang/String;", AndroidInvokeKind.Static, "hello %s %s %s", varargs));

        // Positional + width.
        var positional = new DexArray("[Ljava/lang/Object;", 2);
        positional.Set(0, "a");
        positional.Set(1, "b");
        Assert.Equal("b a", Invoke(registry, state, StringClass, "format", "(Ljava/lang/String;[Ljava/lang/Object;)Ljava/lang/String;", AndroidInvokeKind.Static, "%2$s %1$s", positional));
    }

    [Fact]
    public void Get_bytes_uses_utf8_and_rejects_unknown_charsets()
    {
        var (state, registry, _) = Session();
        var bytes = (DexArray)Invoke(registry, state, StringClass, "getBytes", "()[B", AndroidInvokeKind.Virtual, "héllo");
        Assert.Equal(6, bytes.Length); // é is 2 UTF-8 bytes
        Assert.Equal((int)'h', RequireInt(bytes.Get(0)));

        var latin1 = (DexArray)Invoke(registry, state, StringClass, "getBytes", "(Ljava/lang/String;)[B", AndroidInvokeKind.Virtual, "héllo", "ISO-8859-1");
        Assert.Equal(5, latin1.Length);

        var error = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, StringClass, "getBytes", "(Ljava/lang/String;)[B", AndroidInvokeKind.Virtual, "hello", "X-NOPE"));
        Assert.Equal("Ljava/io/UnsupportedEncodingException;", error.Throwable.TypeDescriptor);
    }

    [Fact]
    public void Last_index_of_and_code_points_follow_java()
    {
        var (state, registry, _) = Session();
        Assert.Equal(3, Invoke(registry, state, StringClass, "lastIndexOf", "(Ljava/lang/String;)I", AndroidInvokeKind.Virtual, "banana", "an"));
        Assert.Equal(3, Invoke(registry, state, StringClass, "lastIndexOf", "(Ljava/lang/String;I)I", AndroidInvokeKind.Virtual, "banana", "an", 3));
        Assert.Equal(5, Invoke(registry, state, StringClass, "lastIndexOf", "(I)I", AndroidInvokeKind.Virtual, "banana", (int)'a'));

        // Surrogate pair counts as ONE code point: "a😀b" = 3 code points.
        string emoji = "a\ud83d\ude00b";
        Assert.Equal(3, Invoke(registry, state, StringClass, "codePointCount", "(II)I", AndroidInvokeKind.Virtual, emoji, 0, emoji.Length));
        Assert.Equal(0x1F600, Invoke(registry, state, StringClass, "codePointAt", "(I)I", AndroidInvokeKind.Virtual, emoji, 1));
    }

    private static int RequireInt(object? value) => value is int i ? i : throw new ArgumentException("Expected int.");

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