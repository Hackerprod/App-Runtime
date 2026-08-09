#nullable enable
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for java.util.Locale (own file per the Java&lt;Package&gt;Bindings.cs
/// convention — migrated out of the monolith, same as String/Collections/etc.).
/// Real contract VERIFIED against Android libcore's ojluni Locale.java and
/// sun.util.locale.BaseLocale.java (fetched during this work unit, NOT from
/// memory):
/// - Constructors normalize: language -> lowercase, country/region -> UPPERCASE
///   (BaseLocale.getInstance: toLowerCase/toUpperCase), variant retained as-is;
///   null language/country/variant throws NullPointerException (real contract).
///   Legacy ISO codes (iw/he, ji/yi, in/id) are mapped per convertOldISOCodes;
///   the ja_JP_JP / th_TH_TH special cases are NOT modeled (they attach
///   locale extensions, and this runtime has no extension model).
/// - Representation: Locale is a small immutable 3-string value; the existing
///   monolith bindings (and Configuration's fixed constants) already use direct
///   DexObject.InstanceFields (_language/_country), NOT a peer store — this
///   file follows that shape. No quota store: no growth beyond constructed
///   values, same reasoning as Configuration's direct fields.
/// - Default: this runtime has no real device locale. getDefault() returns the
///   stable session default (AndroidFrameworkState.LocaleObject) — a FIXED
///   en-US value, NOT derived from the host Windows culture (same honest
///   convention as Configuration's fixed screen/density/orientation constants).
///   Real Android caches the default in a field, so the same instance is
///   returned until setDefault; this binding returns the same object across
///   calls (setDefault is NOT built — probe shows no reference).
/// - toString() real algorithm (verified, libcore lines 1689-1720): start with
///   language; append '_'+region when (region non-empty) OR (language non-empty
///   AND (variant/script/extensions non-empty)); append '_'+variant when
///   variant non-empty AND (language OR region non-empty). No script or
///   extensions exist in this model, so the bounded form is:
///     lang + (r || (l && v) ? "_" + region : "") + (v && (l || r) ? "_" + variant : "")
///   e.g. en_US -> "en_US", en -> "en", en_US_POSIX -> "en_US_POSIX",
///   ROOT -> "", en__POSIX (empty region) -> "en__POSIX".
/// - toLanguageTag() real algorithm (BCP47): language (lowercased) + '-'
///   + region (uppercased) + variants joined by '-'; script not modeled
///   (forLanguageTag drops script subtags — getScript always returns "" for
///   everything this runtime can construct; documented, honest bound).
/// - equals/hashCode are VALUE-based on language+script+region+variant
///   (BaseLocale.equals + hashCode: ((lang*31+script)*31+region)*31+variant,
///   Java string hashCode); extensions are null here so they do not
///   contribute. Not identity (unlike Class/Enum).
/// - getDisplayName/getDisplayLanguage/getDisplayCountry are NOT built: they
///   require real locale display-name (CLDR/ICU) tables — per the brief,
///   "likely out of scope unless proven needed". Probe shows getDisplayName
///   referenced in the method table; if the real run reaches it after this
///   boundary clears, it is reported as the next gap, not silently stubbed.
/// Probe of SKYNET-FlexGrabber.apk: <init>(String), <init>(String,String),
/// <init>(String,String,String), clone, equals, forLanguageTag, getCountry,
/// getDefault, getDefault(Category), getDisplayName(Locale), getLanguage,
/// getScript, hashCode, toLanguageTag, toString — all built except the
/// display-name family (see above); static constants ENGLISH/KOREAN/ROOT/US
/// are referenced and built via the framework static-field resolver (sget).
/// NOT built (not referenced): getVariant, setDefault, getDisplayLanguage,
/// getDisplayCountry, getISO3Language/getISO3Country (legacy ISO3 needs
/// tables).
/// </summary>
internal static class JavaUtilLocaleBindings
{
    private const string Locale = "Ljava/util/Locale;";
    private const string NullPointer = "Ljava/lang/NullPointerException;";

    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        // ---- Constructors (normalize per BaseLocale.getInstance; NPE on null) ----
        builder.Register(Api(Locale, "<init>", "(Ljava/lang/String;)V"), (_, args) =>
        {
            string language = NormalizeLanguage(args[1]);
            SetFields(Receiver(args), language, string.Empty, string.Empty);
            return null!;
        });
        builder.Register(Api(Locale, "<init>", "(Ljava/lang/String;Ljava/lang/String;)V"), (_, args) =>
        {
            string language = NormalizeLanguage(args[1]);
            string country = NormalizeRegion(args[2]);
            SetFields(Receiver(args), language, country, string.Empty);
            return null!;
        });
        builder.Register(Api(Locale, "<init>", "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;)V"), (_, args) =>
        {
            string language = NormalizeLanguage(args[1]);
            string country = NormalizeRegion(args[2]);
            string variant = NormalizeVariant(args[3]);
            SetFields(Receiver(args), language, country, variant);
            return null!;
        });

        // ---- Defaults: stable fixed en-US, same object across calls ----
        builder.Register(Api(Locale, "getDefault", "()Ljava/util/Locale;"), (_, _) => EnsureDefault(state));
        builder.Register(Api(Locale, "getDefault", "(Ljava/util/Locale$Category;)Ljava/util/Locale;"), (_, _) => EnsureDefault(state));

        // ---- Reads ----
        builder.Register(Api(Locale, "getLanguage", "()Ljava/lang/String;"), (_, args) => Field(Receiver(args), "language"));
        builder.Register(Api(Locale, "getCountry", "()Ljava/lang/String;"), (_, args) => Field(Receiver(args), "country"));
        builder.Register(Api(Locale, "getScript", "()Ljava/lang/String;"), (_, _) => string.Empty);
        // getVariant is NOT bound: probe shows no reference. Variant is still
        // stored so toString/equals/hashCode honor the 3-arg constructor.

        // ---- toString / toLanguageTag / equals / hashCode / clone ----
        builder.Register(Api(Locale, "toString", "()Ljava/lang/String;"), (_, args) =>
        {
            var fields = ReadFields(Receiver(args));
            bool l = fields.Language.Length != 0;
            bool r = fields.Country.Length != 0;
            bool v = fields.Variant.Length != 0;
            var result = new System.Text.StringBuilder(fields.Language);
            if (r || (l && v))
                result.Append('_').Append(fields.Country); // may just append '_'
            if (v && (l || r))
                result.Append('_').Append(fields.Variant);
            return result.ToString();
        });
        builder.Register(Api(Locale, "toLanguageTag", "()Ljava/lang/String;"), (_, args) =>
        {
            var fields = ReadFields(Receiver(args));
            var result = new System.Text.StringBuilder(fields.Language);
            if (fields.Country.Length != 0)
                result.Append('-').Append(fields.Country);
            if (fields.Variant.Length != 0)
                result.Append('-').Append(fields.Variant);
            return result.ToString();
        });
        builder.Register(Api(Locale, "equals", "(Ljava/lang/Object;)Z"), (_, args) =>
        {
            var self = Receiver(args);
            if (ReferenceEquals(self, args[1])) return 1;
            if (args[1] is not DexObject other || other.TypeDescriptor != Locale) return 0;
            var left = ReadFields(self);
            var right = ReadFields(other);
            return left.Language == right.Language && left.Country == right.Country && left.Variant == right.Variant ? 1 : 0;
        });
        builder.Register(Api(Locale, "hashCode", "()I"), (_, args) =>
        {
            var fields = ReadFields(Receiver(args));
            // BaseLocale.hashCode: ((lang*31+script)*31+region)*31+variant; script is
            // always "" here (hash 0); Java string hash.
            int h = AndroidApiBindings.JavaHash(fields.Language);
            h = 31 * h + AndroidApiBindings.JavaHash(fields.Country);
            h = 31 * h + AndroidApiBindings.JavaHash(fields.Variant);
            return h;
        });
        builder.Register(Api(Locale, "clone", "()Ljava/lang/Object;"), (_, args) =>
        {
            // Real Locale.clone() is Object.clone(): a NEW object sharing the
            // immutable base fields. Value-equal, not reference-equal.
            var original = Receiver(args);
            var copy = new DexObject(Locale);
            foreach (var pair in original.InstanceFields)
                copy.InstanceFields[pair.Key] = pair.Value;
            return copy;
        });

        // ---- forLanguageTag (migrated from monolith; now BCP47-cased) ----
        builder.Register(Api(Locale, "forLanguageTag", "(Ljava/lang/String;)Ljava/util/Locale;"), (_, args) =>
        {
            string tag = RequireString(args[0], allowNull: true) ?? string.Empty;
            string language = string.Empty;
            string region = string.Empty;
            var variantParts = new List<string>();
            foreach (string subtag in tag.Split('-', StringSplitOptions.RemoveEmptyEntries))
            {
                if (language.Length == 0) { language = subtag.ToLowerInvariant(); continue; }
                if (region.Length == 0 && (subtag.Length == 2 || subtag.Length == 3) && subtag.All(char.IsLetterOrDigit))
                {
                    if (subtag.Length == 2 && subtag.All(char.IsLetter)) { region = subtag.ToUpperInvariant(); continue; }
                    if (subtag.Length == 3 && subtag.All(char.IsDigit)) { region = subtag; continue; }
                }
                // 4-letter script subtags are dropped (no script model), variants kept.
                if (subtag.Length == 4 && subtag.All(char.IsLetter)) continue;
                variantParts.Add(subtag);
            }
            var locale = new DexObject(Locale);
            SetFields(locale, language, region, string.Join("_", variantParts));
            return locale;
        });
    }

    private static DexObject EnsureDefault(AndroidFrameworkState state)
    {
        // Seed the stable session default once: fixed en-US, NOT host-derived
        // (same convention as Configuration's fixed constants).
        var locale = state.LocaleObject;
        if (!locale.InstanceFields.ContainsKey("language"))
            SetFields(locale, "en", "US", string.Empty);
        return locale;
    }

    private static void SetFields(DexObject locale, string language, string country, string variant)
    {
        locale.InstanceFields["language"] = language;
        locale.InstanceFields["country"] = country;
        locale.InstanceFields["variant"] = variant;
    }

    private static (string Language, string Country, string Variant) ReadFields(DexObject locale) => (
        locale.InstanceFields.TryGetValue("language", out object? l) && l is string lt ? lt : string.Empty,
        locale.InstanceFields.TryGetValue("country", out object? c) && c is string ct ? ct : string.Empty,
        locale.InstanceFields.TryGetValue("variant", out object? v) && v is string vt ? vt : string.Empty);

    private static string Field(DexObject locale, string name) =>
        locale.InstanceFields.TryGetValue(name, out object? value) && value is string text ? text : string.Empty;

    private static string NormalizeLanguage(object? value) =>
        RequireString(value, allowNull: false) switch
        {
            // Real convertOldISOCodes mappings (libcore BaseLocale).
            "iw" => "he",
            "ji" => "yi",
            "in" => "id",
            var language => language.ToLowerInvariant()
        };

    private static string NormalizeRegion(object? value) => RequireString(value, allowNull: false).ToUpperInvariant();

    private static string NormalizeVariant(object? value) => RequireString(value, allowNull: false);

    private static string RequireString(object? value, bool allowNull)
    {
        if (value is null)
        {
            if (allowNull) return string.Empty;
            throw new GuestExceptionCarrier(GuestThrowableMetadata.Create(NullPointer));
        }
        if (value is not string text)
            throw new ArgumentException("Expected string.");
        return text;
    }

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    private static DexObject Receiver(object[] args) => (DexObject)args[0]!;
}
