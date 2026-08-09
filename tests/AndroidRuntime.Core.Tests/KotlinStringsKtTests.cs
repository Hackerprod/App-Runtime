using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for kotlin.text.StringsKt: the $default mask-substitution convention
/// (a set bit must use the method default even when the passed value is wrong),
/// the Kotlin extension-function call shape (static, receiver as first arg), and
/// the semantics that differ from java.lang.String (toIntOrNull excluded — no
/// Integer boxing model; split returning a List; ignoreCase variants).
/// </summary>
public sealed class KotlinStringsKtTests
{
    private const string K = "Lkotlin/text/StringsKt;";

    [Fact]
    public void Default_mask_forces_the_default_when_the_bit_is_set()
    {
        var (state, registry, _) = Session();
        // replace$default(String,String,String,Z,I,Object): the Z bit (bit 0) is
        // SET, so the passed ignoreCase=true must be IGNORED and the literal
        // replacement used (proving the default false really gets applied).
        string value = "Hello";
        var result = Invoke(registry, state, K, "replace$default", "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;ZILjava/lang/Object;)Ljava/lang/String;", AndroidInvokeKind.Static,
            value, "hello", "X", 1, 1, null!);
        Assert.Equal("Hello", result);

        // Same call with the bit CLEAR uses the passed ignoreCase=true -> case-insensitive replace.
        var caseInsensitive = Invoke(registry, state, K, "replace$default", "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;ZILjava/lang/Object;)Ljava/lang/String;", AndroidInvokeKind.Static,
            value, "hello", "X", 1, 0, null!);
        Assert.Equal("X", caseInsensitive);
    }

    [Fact]
    public void Replace_matches_kotlin_literal_and_ignore_case_semantics()
    {
        var (state, registry, _) = Session();
        Assert.Equal("hXlo", Invoke(registry, state, K, "replace", "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;Z)Ljava/lang/String;", AndroidInvokeKind.Static, "hello", "el", "X", 0));
        Assert.Equal("heXXo", Invoke(registry, state, K, "replace", "(Ljava/lang/String;CCZ)Ljava/lang/String;", AndroidInvokeKind.Static, "hello", (int)'l', (int)'X', 0));
        Assert.Equal("X", Invoke(registry, state, K, "replace", "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;Z)Ljava/lang/String;", AndroidInvokeKind.Static, "Hello", "HELLO", "X", 1));
    }

    [Fact]
    public void Contains_starts_with_and_ends_with_honor_ignore_case()
    {
        var (state, registry, _) = Session();
        Assert.Equal(1, Invoke(registry, state, K, "contains", "(Ljava/lang/CharSequence;Ljava/lang/CharSequence;Z)Z", AndroidInvokeKind.Static, "hello world", "WORLD", 1));
        Assert.Equal(0, Invoke(registry, state, K, "contains", "(Ljava/lang/CharSequence;Ljava/lang/CharSequence;Z)Z", AndroidInvokeKind.Static, "hello world", "WORLD", 0));
        Assert.Equal(1, Invoke(registry, state, K, "startsWith", "(Ljava/lang/String;Ljava/lang/String;IZ)Z", AndroidInvokeKind.Static, "hello world", "WORLD", 6, 1));
        Assert.Equal(1, Invoke(registry, state, K, "endsWith", "(Ljava/lang/CharSequence;Ljava/lang/CharSequence;Z)Z", AndroidInvokeKind.Static, "hello world", "WORLD", 1));
        Assert.Equal(1, Invoke(registry, state, K, "contains", "(Ljava/lang/CharSequence;CZ)Z", AndroidInvokeKind.Static, "hello", (int)'E', 1));
    }

    [Fact]
    public void Index_of_and_last_index_of_apply_from_index_and_ignore_case()
    {
        var (state, registry, _) = Session();
        Assert.Equal(6, Invoke(registry, state, K, "indexOf", "(Ljava/lang/CharSequence;Ljava/lang/String;IZ)I", AndroidInvokeKind.Static, "Hello World", "world", 0, 1));
        Assert.Equal(6, Invoke(registry, state, K, "indexOf", "(Ljava/lang/CharSequence;Ljava/lang/String;IZ)I", AndroidInvokeKind.Static, "Hello World", "W", 6, 0));
        // "banana": the last 'a' at or before index 9 is at index 5.
        Assert.Equal(5, Invoke(registry, state, K, "lastIndexOf", "(Ljava/lang/CharSequence;CIZ)I", AndroidInvokeKind.Static, "banana", (int)'a', 9, 0));
    }

    [Fact]
    public void Equals_and_compare_to_cover_the_ignore_case_variant()
    {
        var (state, registry, _) = Session();
        Assert.Equal(1, Invoke(registry, state, K, "equals", "(Ljava/lang/String;Ljava/lang/String;Z)Z", AndroidInvokeKind.Static, "Hello", "HELLO", 1));
        Assert.Equal(0, Invoke(registry, state, K, "equals", "(Ljava/lang/String;Ljava/lang/String;Z)Z", AndroidInvokeKind.Static, "Hello", "HELLO", 0));
        Assert.Equal(0, Invoke(registry, state, K, "compareTo", "(Ljava/lang/String;Ljava/lang/String;Z)I", AndroidInvokeKind.Static, "Hello", "HELLO", 1));
        Assert.True((int)Invoke(registry, state, K, "compareTo", "(Ljava/lang/String;Ljava/lang/String;Z)I", AndroidInvokeKind.Static, "abc", "abd", 0) < 0);
    }

    [Fact]
    public void Trim_family_is_blank_take_drop_pad_repeat_follow_kotlin()
    {
        var (state, registry, _) = Session();
        Assert.Equal("hi", Invoke(registry, state, K, "trim", "(Ljava/lang/CharSequence;)Ljava/lang/CharSequence;", AndroidInvokeKind.Static, "  hi  "));
        Assert.Equal("hi  ", Invoke(registry, state, K, "trimStart", "(Ljava/lang/CharSequence;)Ljava/lang/CharSequence;", AndroidInvokeKind.Static, "  hi  "));
        Assert.Equal("  hi", Invoke(registry, state, K, "trimEnd", "(Ljava/lang/CharSequence;)Ljava/lang/CharSequence;", AndroidInvokeKind.Static, "  hi  "));
        Assert.Equal(1, Invoke(registry, state, K, "isBlank", "(Ljava/lang/CharSequence;)Z", AndroidInvokeKind.Static, "   \t"));
        Assert.Equal(0, Invoke(registry, state, K, "isBlank", "(Ljava/lang/CharSequence;)Z", AndroidInvokeKind.Static, " x "));
        Assert.Equal("hel", Invoke(registry, state, K, "take", "(Ljava/lang/String;I)Ljava/lang/String;", AndroidInvokeKind.Static, "hello", 3));
        Assert.Equal("hello", Invoke(registry, state, K, "take", "(Ljava/lang/String;I)Ljava/lang/String;", AndroidInvokeKind.Static, "hello", 99));
        Assert.Equal("lo", Invoke(registry, state, K, "drop", "(Ljava/lang/String;I)Ljava/lang/String;", AndroidInvokeKind.Static, "hello", 3));
        Assert.Equal("", Invoke(registry, state, K, "drop", "(Ljava/lang/String;I)Ljava/lang/String;", AndroidInvokeKind.Static, "hello", 99));
        Assert.Equal("00042", Invoke(registry, state, K, "padStart", "(Ljava/lang/String;IC)Ljava/lang/String;", AndroidInvokeKind.Static, "42", 5, (int)'0'));
        Assert.Equal("42aaa", Invoke(registry, state, K, "padEnd", "(Ljava/lang/String;IC)Ljava/lang/String;", AndroidInvokeKind.Static, "42", 5, (int)'a'));
        Assert.Equal("abab", Invoke(registry, state, K, "repeat", "(Ljava/lang/CharSequence;I)Ljava/lang/String;", AndroidInvokeKind.Static, "ab", 2));
        var negative = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, K, "take", "(Ljava/lang/String;I)Ljava/lang/String;", AndroidInvokeKind.Static, "hello", -1));
        Assert.Equal("Ljava/lang/IllegalArgumentException;", negative.Throwable.TypeDescriptor);
    }

    [Fact]
    public void Split_by_char_array_returns_a_list_with_java_limit_semantics()
    {
        var (state, registry, _) = Session();
        var delimiters = new DexArray("[C", 1);
        delimiters.Set(0, (int)',');

        var list = (DexObject)Invoke(registry, state, K, "split", "(Ljava/lang/CharSequence;[CZI)Ljava/util/List;", AndroidInvokeKind.Static, "a,b,c", delimiters, 0, 0);
        var peer = state.ArrayLists.Get(list);
        Assert.Equal(3, peer.Elements.Count);
        Assert.Equal("a", peer.Elements[0]);
        Assert.Equal("c", peer.Elements[2]);

        // limit 2: first piece + concatenated remainder.
        var capped = (DexObject)Invoke(registry, state, K, "split", "(Ljava/lang/CharSequence;[CZI)Ljava/util/List;", AndroidInvokeKind.Static, "a,b,c", delimiters, 0, 2);
        Assert.Equal(2, state.ArrayLists.Get(capped).Elements.Count);
        Assert.Equal("b,c", state.ArrayLists.Get(capped).Elements[1]);

        // $default with the limit bit set (bit 1) uses limit=0 and drops trailing empties.
        var viaDefault = (DexObject)Invoke(registry, state, K, "split$default", "(Ljava/lang/CharSequence;[CZIILjava/lang/Object;)Ljava/util/List;", AndroidInvokeKind.Static, "a,b,", delimiters, 0, 99, 2, null!);
        Assert.Equal(2, state.ArrayLists.Get(viaDefault).Elements.Count);
    }

    [Fact]
    public void Get_last_index_and_to_char_array_follow_kotlin()
    {
        var (state, registry, _) = Session();
        Assert.Equal(4, Invoke(registry, state, K, "getLastIndex", "(Ljava/lang/CharSequence;)I", AndroidInvokeKind.Static, "hello"));
        var chars = (DexArray)Invoke(registry, state, K, "toCharArray", "(Ljava/lang/String;II)[C", AndroidInvokeKind.Static, "hello", 1, 3);
        Assert.Equal(2, chars.Length);
        Assert.Equal((int)'e', chars.Get(0));
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
