using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for the SCOPED java.util.Date + java.text.SimpleDateFormat surface
/// (SimpleDateFormat continuation brief, Option 1). All formatting asserts run
/// against FIXED known epoch-millis instants (never real "now") so the tests
/// are deterministic. Verifies the real JDK rules for the letters FlexLogger
/// actually uses: zero-padding, the y/yy truncation quirk, quoting, and the
/// fail-closed behavior for out-of-scope pattern letters.
/// </summary>
public sealed class SimpleDateFormatScopedTests
{
    private const string Date = "Ljava/util/Date;";
    private const string SimpleDateFormat = "Ljava/text/SimpleDateFormat;";

    // Fixed instant: 2024-01-15 14:30:45.123 UTC, built from components (not via
    // FromUnixTimeMilliseconds) so the format tests are independent of the
    // implementation's decomposition.
    private static readonly long FixedInstant = new DateTimeOffset(2024, 1, 15, 14, 30, 45, 123, TimeSpan.Zero).ToUnixTimeMilliseconds();

    [Fact]
    public void Date_no_arg_constructor_captures_wall_clock_now()
    {
        var (state, registry, _) = Session(wallClockMillis: FixedInstant);
        var date = new DexObject(Date);
        Invoke(registry, state, Date, "<init>", "()V", AndroidInvokeKind.Direct, date);
        // Real contract: new Date() = System.currentTimeMillis() at construction.
        Assert.Equal(FixedInstant, Invoke(registry, state, Date, "getTime", "()J", AndroidInvokeKind.Virtual, date));
    }

    [Fact]
    public void Date_long_constructor_stores_explicit_epoch_millis()
    {
        var (state, registry, _) = Session();
        var date = new DexObject(Date);
        Invoke(registry, state, Date, "<init>", "(J)V", AndroidInvokeKind.Direct, date, 1_700_000_000_123L);
        Assert.Equal(1_700_000_000_123L, Invoke(registry, state, Date, "getTime", "()J", AndroidInvokeKind.Virtual, date));
    }

    [Fact]
    public void FlexLogger_full_timestamp_pattern_formats_correctly()
    {
        var (state, registry, _) = Session();
        var formatter = NewFormatter(registry, state, "yyyy-MM-dd HH:mm:ss");
        var date = NewDate(registry, state, FixedInstant);
        Assert.Equal("2024-01-15 14:30:45", Invoke(registry, state, SimpleDateFormat, "format", "(Ljava/util/Date;)Ljava/lang/String;", AndroidInvokeKind.Virtual, formatter, date));
    }

    [Fact]
    public void FlexLogger_millis_pattern_formats_correctly()
    {
        var (state, registry, _) = Session();
        var formatter = NewFormatter(registry, state, "HH:mm:ss.SSS");
        var date = NewDate(registry, state, FixedInstant);
        Assert.Equal("14:30:45.123", Invoke(registry, state, SimpleDateFormat, "format", "(Ljava/util/Date;)Ljava/lang/String;", AndroidInvokeKind.Virtual, formatter, date));
    }

    [Fact]
    public void Single_letter_number_fields_use_minimum_digits_no_leading_zero()
    {
        var (state, registry, _) = Session();
        var formatter = NewFormatter(registry, state, "y-M-d H:m:s.S");
        var date = NewDate(registry, state, FixedInstant);
        Assert.Equal("2024-1-15 14:30:45.123", Invoke(registry, state, SimpleDateFormat, "format", "(Ljava/util/Date;)Ljava/lang/String;", AndroidInvokeKind.Virtual, formatter, date));
    }

    [Fact]
    public void Year_pattern_letter_two_truncates_to_last_two_digits()
    {
        var (state, registry, _) = Session();
        // Real JDK quirk (verified from the SimpleDateFormat docs): exactly two
        // "y" letters truncate the year to 2 digits; any other count is a full
        // number ("yyyy" = 4-digit zero-padded, "yyyyy" = 5-digit).
        var yy = NewFormatter(registry, state, "yy");
        var yyyy = NewFormatter(registry, state, "yyyy");
        var yyyyy = NewFormatter(registry, state, "yyyyy");
        var date = NewDate(registry, state, FixedInstant);
        Assert.Equal("24", Invoke(registry, state, SimpleDateFormat, "format", "(Ljava/util/Date;)Ljava/lang/String;", AndroidInvokeKind.Virtual, yy, date));
        Assert.Equal("2024", Invoke(registry, state, SimpleDateFormat, "format", "(Ljava/util/Date;)Ljava/lang/String;", AndroidInvokeKind.Virtual, yyyy, date));
        Assert.Equal("02024", Invoke(registry, state, SimpleDateFormat, "format", "(Ljava/util/Date;)Ljava/lang/String;", AndroidInvokeKind.Virtual, yyyyy, date));
    }

    [Fact]
    public void Unquoted_non_letter_characters_are_copied_verbatim()
    {
        var (state, registry, _) = Session();
        var formatter = NewFormatter(registry, state, "yyyy/MM/dd");
        var date = NewDate(registry, state, FixedInstant);
        Assert.Equal("2024/01/15", Invoke(registry, state, SimpleDateFormat, "format", "(Ljava/util/Date;)Ljava/lang/String;", AndroidInvokeKind.Virtual, formatter, date));
    }

    [Fact]
    public void Quoted_sections_are_literal_and_double_quote_means_a_single_quote()
    {
        var (state, registry, _) = Session();
        var date = NewDate(registry, state, FixedInstant);
        // Single-quoted text is copied verbatim (real rule).
        var quoted = NewFormatter(registry, state, "'at' HH:mm");
        Assert.Equal("at 14:30", Invoke(registry, state, SimpleDateFormat, "format", "(Ljava/util/Date;)Ljava/lang/String;", AndroidInvokeKind.Virtual, quoted, date));
        // "''" represents a literal single quote (real rule, verified from docs).
        var doubleQuote = NewFormatter(registry, state, "yyyy ''yy");
        Assert.Equal("2024 '24", Invoke(registry, state, SimpleDateFormat, "format", "(Ljava/util/Date;)Ljava/lang/String;", AndroidInvokeKind.Virtual, doubleQuote, date));
    }

    [Fact]
    public void Out_of_scope_pattern_letters_fail_closed()
    {
        var (state, registry, _) = Session();
        // Month NAME ("MMM", 3+ letters) and timezone letters are out of scope in
        // this unit; they must fail loudly, not silently approximate.
        var monthName = NewFormatter(registry, state, "MMM");
        var date = NewDate(registry, state, FixedInstant);
        Assert.Throws<AndroidApiUnavailableException>(() => Invoke(registry, state, SimpleDateFormat, "format", "(Ljava/util/Date;)Ljava/lang/String;", AndroidInvokeKind.Virtual, monthName, date));

        var timeZone = NewFormatter(registry, state, "yyyy z");
        Assert.Throws<AndroidApiUnavailableException>(() => Invoke(registry, state, SimpleDateFormat, "format", "(Ljava/util/Date;)Ljava/lang/String;", AndroidInvokeKind.Virtual, timeZone, date));
    }

    [Fact]
    public void Null_pattern_constructor_argument_throws_guest_null_pointer_exception()
    {
        var (state, registry, _) = Session();
        var error = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, SimpleDateFormat, "<init>", "(Ljava/lang/String;Ljava/util/Locale;)V", AndroidInvokeKind.Direct, new DexObject(SimpleDateFormat), null!, state.LocaleObject));
        Assert.Equal("Ljava/lang/NullPointerException;", error.Throwable.TypeDescriptor);
    }

    [Fact]
    public void Utc_wall_clock_default_returns_real_epoch_millis()
    {
        var clock = new UtcAndroidWallClock();
        long before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long now = clock.NowMillis();
        long after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.InRange(now, before, after);
    }

    [Fact]
    public void Wall_clock_port_is_injectable_through_services_and_state()
    {
        var services = new AndroidRuntimeServices(new InMemoryActivityWindowFactory(), new QuietLogSink(), wallClock: new FixedWallClock(FixedInstant));
        Assert.Equal(FixedInstant, services.WallClock.NowMillis());
        using var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers(), wallClock: new FixedWallClock(FixedInstant));
        Assert.Equal(FixedInstant, state.WallClock.NowMillis());
        // Default when not injected is the real-time UTC clock.
        using var defaultState = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        Assert.IsType<UtcAndroidWallClock>(defaultState.WallClock);
    }

    private static DexObject NewFormatter(AndroidApiRegistry registry, AndroidFrameworkState state, string pattern)
    {
        var formatter = new DexObject(SimpleDateFormat);
        Invoke(registry, state, SimpleDateFormat, "<init>", "(Ljava/lang/String;Ljava/util/Locale;)V", AndroidInvokeKind.Direct, formatter, pattern, state.LocaleObject);
        return formatter;
    }

    private static DexObject NewDate(AndroidApiRegistry registry, AndroidFrameworkState state, long millis)
    {
        var date = new DexObject(Date);
        Invoke(registry, state, Date, "<init>", "(J)V", AndroidInvokeKind.Direct, date, millis);
        return date;
    }

    private static (AndroidFrameworkState State, AndroidApiRegistry Registry, DexInterpreter Interpreter) Session(long? wallClockMillis = null)
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers(), wallClock: wallClockMillis is long millis ? new FixedWallClock(millis) : null);
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

    private sealed class FixedWallClock(long millis) : IAndroidWallClock
    {
        public long NowMillis() => millis;
    }
}
