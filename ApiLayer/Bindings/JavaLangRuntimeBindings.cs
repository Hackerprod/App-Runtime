#nullable enable
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for java.lang.Runtime — SCOPED to the surface the SKYNET
/// launch path actually executes. Probe of SKYNET-FlexGrabber.apk: the ONLY
/// Runtime methods referenced on the executed path are
/// `getRuntime()Ljava/lang/Runtime;` and `availableProcessors()I` (from
/// kotlinx.coroutines SystemPropsKt and Lokio SegmentPool class-inits).
///
/// SECURITY STANCE (per the brief, documented explicitly): this runtime
/// executes arbitrary, untrusted, real third-party APKs. The dangerous
/// Runtime methods are therefore NOT built here — and the probe shows they
/// are NOT even referenced on this path:
/// - exec(...) — NEVER real process execution (a sandbox escape). Not
///   referenced; if a future path reaches it, it must fail closed with the
///   real documented guest java.io.IOException.
/// - exit(int)/halt(int) — NEVER wired to terminate the WindowsHost process
///   (a trivial DoS). Not referenced; if reached, session-termination
///   semantics need an explicit scope decision, not a guess.
/// - addShutdownHook/removeShutdownHook — not referenced; only meaningful
///   once exit/session-termination semantics are decided.
///
/// Built (safe, informational, real contract VERIFIED against the Java SE 17
/// Runtime docs):
/// - getRuntime(): process-wide singleton (real contract: "Returns the
///   runtime object associated with the current Java application. Most of the
///   methods of class Runtime are instance methods and must be invoked with
///   respect to the current runtime object" — one stable object per process).
///   Canonical-singleton pattern, same as Class/Locale's stable defaults.
/// - availableProcessors(): "the number of processors available to the Java
///   virtual machine" — reports the host's Environment.ProcessorCount (the
///   honest host truth for this runtime's host process).
/// - totalMemory()/freeMemory()/maxMemory(): "the total amount of memory in
///   the Java virtual machine" / "the amount of free memory" / "the maximum
///   amount of memory that the Java virtual machine will attempt to use"
///   (Long.MAX_VALUE "if there is no inherent limit"). Reported from the
///   CLR process's honest working-set/GC memory stats (GC.GetTotalMemory /
///   GC.GetGCMemoryInfo). Bounded honest values, not fabricated.
/// - gc(): real contract is a SUGGESTION — "There is no guarantee that this
///   effort will recycle any particular number of unused objects" — a
///   genuine GC.Collect() call is the faithful implementation (this runtime
///   has real CLR objects; collecting them is the honest analogue).
///
/// Representation: a stable singleton DexObject("Ljava/lang/Runtime;") on
/// AndroidFrameworkState (canonical identity, same as LocaleObject); the
/// five informational methods are pure stateless reads off that singleton.
/// </summary>
internal static class JavaLangRuntimeBindings
{
    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        builder.Register(Api("Ljava/lang/Runtime;", "getRuntime", "()Ljava/lang/Runtime;"), (_, _) => state.RuntimeObject);
        builder.Register(Api("Ljava/lang/Runtime;", "availableProcessors", "()I"), (_, _) => Environment.ProcessorCount);
        builder.Register(Api("Ljava/lang/Runtime;", "totalMemory", "()J"), (_, _) => GC.GetTotalMemory(forceFullCollection: false));
        builder.Register(Api("Ljava/lang/Runtime;", "freeMemory", "()J"), (_, _) =>
        {
            var info = GC.GetGCMemoryInfo();
            return Math.Max(0, info.TotalAvailableMemoryBytes - GC.GetTotalMemory(forceFullCollection: false));
        });
        builder.Register(Api("Ljava/lang/Runtime;", "maxMemory", "()J"), (_, _) =>
        {
            var info = GC.GetGCMemoryInfo();
            return info.TotalAvailableMemoryBytes;
        });
        builder.Register(Api("Ljava/lang/Runtime;", "gc", "()V"), (_, _) =>
        {
            // Real contract: a suggestion, "no guarantee that this effort will
            // recycle any particular number of unused objects". The faithful
            // implementation is a genuine best-effort CLR collection.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return null!;
        });
    }

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
}
