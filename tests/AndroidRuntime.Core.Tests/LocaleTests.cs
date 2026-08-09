using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for java.util.Locale against the REAL libcore contract (Locale.java +
/// BaseLocale.java, verified from source): fixed stable default (same object
/// across calls, en-US, not host-derived), the real toString() separator/casing
/// algorithm, value-based equals/hashCode, constructor normalization (language
/// lowercase, country uppercase), toLanguageTag (BCP47), and the static
/// constants resolved through the framework static-field hook.
/// </summary>
public sealed class LocaleTests
{
    private const string Locale = "Ljava/util/Locale;";

    [Fact]
    public void Get_default_returns_the_same_fixed_en_us_object_across_calls()
    {
        var (state, registry, _) = Session();
        var first = (DexObject)Invoke(registry, state, Locale, "getDefault", "()Ljava/util/Locale;", AndroidInvokeKind.Static);
        var second = (DexObject)Invoke(registry, state, Locale, "getDefault", "()Ljava/util/Locale;", AndroidInvokeKind.Static);
        // Same object: real Android caches the default in a field until setDefault.
        Assert.Same(first, second);
        Assert.Equal("en", Invoke(registry, state, Locale, "getLanguage", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, first));
        Assert.Equal("US", Invoke(registry, state, Locale, "getCountry", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, first));
        Assert.Equal("en_US", Invoke(registry, state, Locale, "toString", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, first));
    }

    [Fact]
    public void Get_default_with_category_returns_the_same_fixed_default()
    {
        var (state, registry, _) = Session();
        var category = new DexObject("Ljava/util/Locale$Category;");
        var viaCategory = (DexObject)Invoke(registry, state, Locale, "getDefault", "(Ljava/util/Locale$Category;)Ljava/util/Locale;", AndroidInvokeKind.Static, category);
        Assert.Same(state.LocaleObject, viaCategory);
    }

    [Theory]
    [InlineData("en", "US", "en_US")]
    [InlineData("en", "", "en")]
    [InlineData("fr", "FR", "fr_FR")]
    [InlineData("", "", "")]
    public void To_string_joins_language_underscore_country_omitting_empty_parts(string language, string country, string expected)
    {
        var (state, registry, _) = Session();
        var locale = NewLocale(registry, state, language, country, "");
        Assert.Equal(expected, Invoke(registry, state, Locale, "toString", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, locale));
    }

    [Fact]
    public void To_string_with_variant_appends_underscore_variant()
    {
        var (state, registry, _) = Session();
        var locale = NewLocale(registry, state, "en", "US", "POSIX");
        Assert.Equal("en_US_POSIX", Invoke(registry, state, Locale, "toString", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, locale));
        // Real algorithm: empty country + variant still renders en__POSIX (the
        // separator is appended even when the region is empty, per libcore).
        var emptyCountry = NewLocale(registry, state, "en", "", "POSIX");
        Assert.Equal("en__POSIX", Invoke(registry, state, Locale, "toString", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, emptyCountry));
    }

    [Fact]
    public void Constructor_normalizes_language_lowercase_and_country_uppercase()
    {
        var (state, registry, _) = Session();
        var locale = NewLocale(registry, state, "EN", "us");
        Assert.Equal("en", Invoke(registry, state, Locale, "getLanguage", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, locale));
        Assert.Equal("US", Invoke(registry, state, Locale, "getCountry", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, locale));
        // Legacy ISO codes map per BaseLocale.convertOldISOCodes.
        var legacy = NewLocale(registry, state, "iw", "IL");
        Assert.Equal("he", Invoke(registry, state, Locale, "getLanguage", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, legacy));
    }

    [Fact]
    public void Null_constructor_argument_throws_guest_null_pointer_exception()
    {
        var (state, registry, _) = Session();
        var error = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, Locale, "<init>", "(Ljava/lang/String;)V", AndroidInvokeKind.Direct, new DexObject(Locale), null!));
        Assert.Equal("Ljava/lang/NullPointerException;", error.Throwable.TypeDescriptor);
    }

    [Fact]
    public void Equals_and_hash_code_are_value_based()
    {
        var (state, registry, _) = Session();
        var a = NewLocale(registry, state, "en", "US");
        var b = NewLocale(registry, state, "en", "US");
        var c = NewLocale(registry, state, "en", "GB");
        Assert.NotSame(a, b);
        Assert.Equal(1, Invoke(registry, state, Locale, "equals", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, a, b));
        Assert.Equal(0, Invoke(registry, state, Locale, "equals", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, a, c));
        Assert.Equal(0, Invoke(registry, state, Locale, "equals", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, a, "not-a-locale"));
        // Reference-equal shortcut.
        Assert.Equal(1, Invoke(registry, state, Locale, "equals", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, a, a));

        int hashA = (int)Invoke(registry, state, Locale, "hashCode", "()I", AndroidInvokeKind.Virtual, a)!;
        int hashB = (int)Invoke(registry, state, Locale, "hashCode", "()I", AndroidInvokeKind.Virtual, b)!;
        int hashC = (int)Invoke(registry, state, Locale, "hashCode", "()I", AndroidInvokeKind.Virtual, c)!;
        Assert.Equal(hashA, hashB);
        Assert.NotEqual(hashA, hashC);
        // Real BaseLocale formula: ((lang*31+script)*31+region)*31+variant, Java hash.
        int expected = AndroidApiBindings.JavaHash("en");
        expected = 31 * expected + AndroidApiBindings.JavaHash("US");
        expected = 31 * expected + AndroidApiBindings.JavaHash("");
        Assert.Equal(expected, hashA);
    }

    [Fact]
    public void To_language_tag_uses_bcp47_dash_separator()
    {
        var (state, registry, _) = Session();
        Assert.Equal("en-US", Invoke(registry, state, Locale, "toLanguageTag", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, NewLocale(registry, state, "en", "US")));
        Assert.Equal("en", Invoke(registry, state, Locale, "toLanguageTag", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, NewLocale(registry, state, "en", "")));
        Assert.Equal("", Invoke(registry, state, Locale, "toLanguageTag", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, NewLocale(registry, state, "", "")));
    }

    [Fact]
    public void Get_script_returns_empty_for_constructor_built_locales()
    {
        // Script is only settable via Locale.Builder (not modeled); constructor-built
        // locales have no script per the real contract (getScript -> "").
        var (state, registry, _) = Session();
        var locale = NewLocale(registry, state, "en", "US");
        Assert.Equal("", Invoke(registry, state, Locale, "getScript", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, locale));
    }

    [Fact]
    public void Clone_returns_a_new_value_equal_object()
    {
        var (state, registry, _) = Session();
        var original = NewLocale(registry, state, "en", "US");
        var clone = (DexObject)Invoke(registry, state, Locale, "clone", "()Ljava/lang/Object;", AndroidInvokeKind.Virtual, original);
        Assert.NotSame(original, clone);
        Assert.Equal(1, Invoke(registry, state, Locale, "equals", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, original, clone));
    }

    [Fact]
    public void For_language_tag_parses_bcp47_subtags_with_casing()
    {
        var (state, registry, _) = Session();
        var enUs = (DexObject)Invoke(registry, state, Locale, "forLanguageTag", "(Ljava/lang/String;)Ljava/util/Locale;", AndroidInvokeKind.Static, "en-US");
        Assert.Equal("en", Invoke(registry, state, Locale, "getLanguage", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, enUs));
        Assert.Equal("US", Invoke(registry, state, Locale, "getCountry", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, enUs));
        var plain = (DexObject)Invoke(registry, state, Locale, "forLanguageTag", "(Ljava/lang/String;)Ljava/util/Locale;", AndroidInvokeKind.Static, "fr");
        Assert.Equal("fr", Invoke(registry, state, Locale, "getLanguage", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, plain));
        Assert.Equal("", Invoke(registry, state, Locale, "getCountry", "()Ljava/lang/String;", AndroidInvokeKind.Virtual, plain));
    }

    [Fact]
    public void Static_constants_resolve_through_the_framework_static_field_hook()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var us = (DexObject)state.ResolveFrameworkStaticField(Locale, "US")!;
        var english = (DexObject)state.ResolveFrameworkStaticField(Locale, "ENGLISH")!;
        var korean = (DexObject)state.ResolveFrameworkStaticField(Locale, "KOREAN")!;
        var root = (DexObject)state.ResolveFrameworkStaticField(Locale, "ROOT")!;
        Assert.NotNull(us);
        Assert.Same(us, state.ResolveFrameworkStaticField(Locale, "US")); // canonical identity
        Assert.Equal("en", us.InstanceFields["language"]);
        Assert.Equal("US", us.InstanceFields["country"]);
        Assert.Equal("en", english.InstanceFields["language"]);
        Assert.Equal("ko", korean.InstanceFields["language"]);
        Assert.Equal("", root.InstanceFields["language"]);
        Assert.Null(state.ResolveFrameworkStaticField(Locale, "GERMANY")); // not referenced -> null
    }

    private static DexObject NewLocale(AndroidApiRegistry registry, AndroidFrameworkState state, string language, string country, string? variant = null)
    {
        var locale = new DexObject(Locale);
        if (variant is null)
        {
            Invoke(registry, state, Locale, "<init>", "(Ljava/lang/String;Ljava/lang/String;)V", AndroidInvokeKind.Direct, locale, language, country);
            return locale;
        }
        Invoke(registry, state, Locale, "<init>", "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;)V", AndroidInvokeKind.Direct, locale, language, country, variant);
        return locale;
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
