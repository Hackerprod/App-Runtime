#nullable enable
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for java.text.SimpleDateFormat — SCOPED surface per the
/// SimpleDateFormat continuation brief (Option 1, format-only). Real contract
/// VERIFIED against the Oracle Java SE 17 SimpleDateFormat docs (fetched during
/// this unit):
/// - `SimpleDateFormat(String pattern, Locale locale)`: stores the pattern;
///   locale is accepted (per the real signature) but this bounded formatter has
///   no locale-specific name tables — only FlexLogger's numeric-letter patterns
///   are in scope, which never need them. NPE on null pattern per real contract.
/// - `format(Date)Ljava/lang/String;` (via DateFormat.format(Object) shape is
///   NOT needed — the app calls SimpleDateFormat.format(Date) directly).
/// Pattern rules implemented (from the docs):
///   - Unquoted letters A-Z/a-z are pattern letters; unknown/reserved letters
///     FAIL CLOSED (AndroidApiUnavailableException naming the letter) rather
///     than silently approximating — the docs say all other letters are
///     reserved, and this unit builds only the letters FlexLogger actually
///     uses.
///   - Number fields (y M d H m s S): repeated letters = minimum digit count,
///     zero-padded. Year quirk (verified): exactly 2 letters ("yy") truncates
///     to the last two digits; any other count formats the full year as a
///     number ("yyyy" = 4-digit, "yyyyy" = 5-digit).
///   - Month M: 1-2 letters = numeric month (1-12); 3+ letters = month NAME
///     (needs DateFormatSymbols — OUT of scope, fail closed; FlexLogger never
///     uses it).
///   - Quoting: single quotes quote literal text verbatim; "''" is a literal
///     single quote; all other (non-letter) characters are copied verbatim.
/// - Time-zone letters (z Z X) fail closed (no timezone model in this unit —
///   out of scope per brief; FlexLogger's patterns have none).
/// Decomposition: epoch millis -> civil fields is done in UTC via
/// DateTimeOffset.FromUnixTimeMilliseconds (the wall-clock port returns UTC
/// epoch; this runtime has no device-timezone model, so UTC is the honest,
/// deterministic default — documented limitation, same tone as Locale's fixed
/// default). Not host-locale-dependent.
/// Explicitly NOT built (confirmed not on the current crash path, per brief):
/// parse() (ProfileActivity call sites), applyPattern/toPattern/setLenient/
/// setTimeZone (okhttp/material datepicker call sites), month/day names,
/// Calendar/GregorianCalendar/TimeZone, DateFormat.getDateInstance,
/// DateFormatSymbols. Those are separate future boundaries.
/// Probe: SKYNET-FlexGrabber.apk FlexLogger clinit constructs
/// SimpleDateFormat(String,Locale) with "HH:mm:ss.SSS" and
/// "yyyy-MM-dd HH:mm:ss" then format(new Date()) — exactly the letters
/// y/M/d/H/m/s/S (numeric only) built here.
/// </summary>
internal static class JavaTextDateFormatBindings
{
    private const string SimpleDateFormat = "Ljava/text/SimpleDateFormat;";
    private const string NullPointer = "Ljava/lang/NullPointerException;";

    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        builder.Register(Api(SimpleDateFormat, "<init>", "(Ljava/lang/String;Ljava/util/Locale;)V"), (_, args) =>
        {
            if (args[1] is null)
                throw new GuestExceptionCarrier(GuestThrowableMetadata.Create(NullPointer, "pattern is null"));
            Receiver(args).InstanceFields["pattern"] = AndroidApiBindings.RequireString(args[1]);
            return null!;
        });
        builder.Register(Api(SimpleDateFormat, "format", "(Ljava/util/Date;)Ljava/lang/String;"), (_, args) =>
        {
            var formatter = Receiver(args);
            string pattern = formatter.InstanceFields.TryGetValue("pattern", out object? p) && p is string text ? text : string.Empty;
            var date = (DexObject)args[1]!;
            if (!date.InstanceFields.TryGetValue("time", out object? time) || time is not long millis)
                throw new ArgumentException("Date has no stored epoch millis.");
            return Format(pattern, millis);
        });
    }

    private static string Format(string pattern, long epochMillis)
    {
        var output = new System.Text.StringBuilder();
        bool inQuotes = false;
        int index = 0;
        while (index < pattern.Length)
        {
            char c = pattern[index];
            if (c == '\'')
            {
                // "''" is a literal single quote and does NOT toggle quote state
                // (real rule, verified from the docs: "''yy" -> "'01", and
                // "'o''clock'" -> "o'clock"). A lone quote toggles literal mode.
                if (index + 1 < pattern.Length && pattern[index + 1] == '\'')
                {
                    output.Append('\'');
                    index += 2;
                    continue;
                }
                inQuotes = !inQuotes;
                index++;
                continue;
            }
            if (inQuotes)
            {
                // Quoted text is copied verbatim; an unterminated quote quotes to
                // the end (real SimpleDateFormat behavior).
                output.Append(c);
                index++;
                continue;
            }
            if (IsAsciiLetter(c))
            {
                int run = 1;
                while (index + run < pattern.Length && pattern[index + run] == c) run++;
                output.Append(FormatField(c, run, epochMillis));
                index += run;
                continue;
            }
            output.Append(c);
            index++;
        }
        return output.ToString();
    }

    private static string FormatField(char letter, int width, long epochMillis)
    {
        // Epoch millis -> UTC civil fields (documented: no device-timezone model).
        if (letter == 'M' && width >= 3)
            throw new AndroidApiUnavailableException(
                new AndroidApiMethodId(SimpleDateFormat, "format", "(Ljava/util/Date;)Ljava/lang/String;"),
                "SimpleDateFormat month-name pattern letter 'M' (3+ letters) is not built in this scoped unit (needs DateFormatSymbols; out of scope per the continuation brief).");
        var utc = DateTimeOffset.FromUnixTimeMilliseconds(epochMillis).UtcDateTime;
        int value = letter switch
        {
            'y' => utc.Year,
            'M' => utc.Month,
            'd' => utc.Day,
            'H' => utc.Hour,
            'm' => utc.Minute,
            's' => utc.Second,
            'S' => utc.Millisecond,
            _ => throw new AndroidApiUnavailableException(
                new AndroidApiMethodId(SimpleDateFormat, "format", "(Ljava/util/Date;)Ljava/lang/String;"),
                "SimpleDateFormat pattern letter '" + letter + "' is not built in this scoped unit (out of scope per the continuation brief).")
        };
        if (letter == 'y' && width == 2)
        {
            // Real quirk (verified): "yy" truncates to the last two digits.
            int truncated = value % 100;
            return truncated.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
        }
        // Number field: repeated letters = minimum digit count, zero-padded.
        return value.ToString(new string('0', width), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool IsAsciiLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    private static DexObject Receiver(object[] args) => (DexObject)args[0]!;
}
