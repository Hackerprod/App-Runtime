#nullable enable
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for kotlin.collections.MutableList&lt;T&gt;.sort() — the SCOPED
/// CollectionsKt surface the SKYNET-FlexGrabber launch path executes (the
/// crash: Lokio/Options$Companion.of calls
/// Lkotlin/collections/CollectionsKt;->sort(Ljava/util/List;)V right after its
/// toMutableList() — the same Options.of path from the previous boundary).
/// Real contract VERIFIED against the Kotlin stdlib JVM source
/// (libraries/stdlib/jvm/src/kotlin/collections/MutableCollectionsJVM.kt,
/// fetched during this unit):
///   public actual fun &lt;T : Comparable&lt;T&gt;&gt; MutableList&lt;T&gt;.sort(): Unit {
///       if (size > 1) java.util.Collections.sort(this)
///   }
/// Confirmed facts: (1) IN-PLACE — the same list object is mutated, nothing is
/// returned (Unit); (2) a size>1 guard skips trivial lists; (3) it DELEGATES to
/// java.util.Collections.sort(List) — which this codebase ALREADY implements.
/// Per the brief, this binding is a thin delegate: it does NOT reimplement
/// sort logic. It calls the now-internal
/// JavaUtilCollectionsBindings.Sort(state, list, comparator: null) — the same
/// helper backing the existing Collections.sort(List)V binding — so natural
/// ordering semantics have ONE source of truth. The Kotlin type bound
/// T : Comparable&lt;T&gt; maps to that helper's documented bounded behavior:
/// strings ordinal, boxed numerics by value, DexObject tries guest compareTo,
/// non-Comparable compares equal (README boundary #47) — the same
/// simplification the existing Collections.sort already documents.
/// Probe of SKYNET-FlexGrabber.apk: sort(List)V is the ONLY sort-family member
/// on the executed launch path. The rest (sortWith, sortedWith, sorted, sortBy,
/// sortByDescending, sortDescending, sortedBy, sortedByDescending,
/// sortedDescending, reverse, toSortedSet) ARE in the method table but only
/// reachable from bundled-lib helpers (CollectionsKt___*, SequencesKt,
/// UArraysKt) that do NOT run on this path; per strict scope they are NOT
/// built — reported, future boundaries if reached. (Note: some of those would
/// also be thin delegates to existing machinery, but none are executed here.)
/// </summary>
internal static class KotlinCollectionsCollectionsKtBindings
{
    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        builder.Register(Api("Lkotlin/collections/CollectionsKt;", "sort", "(Ljava/util/List;)V"), (_, args) =>
        {
            // Thin delegate: real Kotlin sort() = if (size>1) Collections.sort(this).
            JavaUtilCollectionsBindings.Sort(state, RequireDex(args[0]), null);
            return null!;
        });
    }

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    private static DexObject RequireDex(object? value) => value as DexObject ?? throw new ArgumentException("Expected a guest object.");
}
