#nullable enable
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for kotlin.collections.Array&lt;T&gt;.toMutableList() — the SCOPED
/// ArraysKt surface the SKYNET-FlexGrabber launch path actually executes (the
/// crash: Lokio/Options$Companion.of calls
/// Lkotlin/collections/ArraysKt;->toMutableList([Ljava/lang/Object;)Ljava/util/List;).
/// Real contract VERIFIED against the Kotlin stdlib source (libraries/stdlib/
/// common/src/generated/_Arrays.kt, fetched during this unit): Array&lt;T&gt;.toMutableList()
/// returns `ArrayList(this.asCollection())` — a NEW, independent MutableList
/// containing a COPY of the array's elements. Mutating the returned list does
/// NOT affect the source array, and mutating the array does NOT affect the
/// list (independent storage) — this is the semantics the tests pin down.
/// Reuses the existing ArrayList/ListPeer machinery exactly as
/// KotlinTextStringsKtBindings.split already does: new
/// DexObject("Ljava/util/ArrayList;") + ListPeer + state.ArrayLists.Add. No new
/// list representation invented.
/// Probe: the Object[] overload is the ONLY one on the executed launch path.
/// The primitive overloads (toMutableList([B/[C/[D/[F/[I/[J/[S/[Z)) ARE in the
/// method table but only reachable from bundled-lib helpers that do not run on
/// this path (ArraysKt___ArraysKt.reversed/toList/take/takeLast/takeLastWhile);
/// per strict scope + the SDF brief's "don't build for non-executed libs"
/// rule, they are NOT built — reported, not silently added.
/// </summary>
internal static class KotlinCollectionsArraysKtBindings
{
    private const string ArrayListClass = "Ljava/util/ArrayList;";

    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        builder.Register(Api("Lkotlin/collections/ArraysKt;", "toMutableList", "([Ljava/lang/Object;)Ljava/util/List;"), (_, args) =>
        {
            var array = (DexArray)args[0]!;
            var listObject = new DexObject(ArrayListClass);
            var peer = new ListPeer();
            for (int index = 0; index < array.Length; index++)
                peer.Elements.Add(array.Get(index));
            state.ArrayLists.Add(listObject, peer);
            return listObject;
        });
    }

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
}
