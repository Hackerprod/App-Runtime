#nullable enable
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for java.util.Date — SCOPED surface per the SimpleDateFormat
/// continuation brief (Option 1): only the three members the FlexLogger launch
/// path executes, to their complete real contract:
/// - `Date()` (no-arg): captures the CURRENT wall-clock epoch millis at
///   construction (real contract: new Date() = System.currentTimeMillis()).
///   This is the reason the wall-clock port exists — the monotonic
///   IAndroidClock cannot answer "what time is it now".
/// - `Date(long)`: explicit epoch millis (real contract: new Date(date)).
/// - `getTime()J`: returns the epoch millis (real contract).
/// Representation: direct DexObject.InstanceFields under key "time" (a long),
/// the same direct-field shape Locale/Configuration use for small immutable
/// values — no peer store needed.
/// Deliberately NOT built (confirmed not on the current crash path, per brief):
/// toInstant (java.time), toString (needs date components), equals/hashCode
/// (value-based but unreferenced), clone, the deprecated getYear/getMonth/...,
/// and parse. Date.toString is referenced by libs in the method table but not
/// executed on this path — if the run reaches it, it is the next reported gap.
/// Probe: SKYNET-FlexGrabber.apk references Date.<init>()V, <init>(J)V,
/// getTime()J, toInstant() (okhttp lib, not executed here) — the two ctors and
/// getTime are built.
/// </summary>
internal static class JavaUtilDateBindings
{
    private const string Date = "Ljava/util/Date;";

    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        builder.Register(Api(Date, "<init>", "()V"), (_, args) =>
        {
            // Real contract: captures the wall-clock now at construction.
            Receiver(args).InstanceFields["time"] = state.WallClock.NowMillis();
            return null!;
        });
        builder.Register(Api(Date, "<init>", "(J)V"), (_, args) =>
        {
            Receiver(args).InstanceFields["time"] = AndroidApiBindings.RequireLong(args[1]);
            return null!;
        });
        builder.Register(Api(Date, "getTime", "()J"), (_, args) =>
        {
            if (Receiver(args).InstanceFields.TryGetValue("time", out object? time) && time is long millis)
                return millis;
            throw new ArgumentException("Date has no stored epoch millis.");
        });
    }

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    private static DexObject Receiver(object[] args) => (DexObject)args[0]!;
}
