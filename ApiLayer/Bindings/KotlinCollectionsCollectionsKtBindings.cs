#nullable enable
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for the kotlin.collections.CollectionsKt surface the SKYNET
/// launch path executes. Two members, both on the Lokio Options$Companion.of
/// chain:
/// - MutableList&lt;T&gt;.sort() — thin delegate to the existing
///   Collections.sort(List) machinery (boundary #55).
/// - mutableListOf(vararg elements: T) — a new, independent ArrayList
///   populated from the varargs array (this brief).
///
/// sort(): real contract VERIFIED against the Kotlin stdlib JVM source
/// (libraries/stdlib/jvm/src/kotlin/collections/MutableCollectionsJVM.kt,
/// fetched during the #55 unit):
///   public actual fun &lt;T : Comparable&lt;T&gt;&gt; MutableList&lt;T&gt;.sort(): Unit {
///       if (size > 1) java.util.Collections.sort(this)
///   }
/// Confirmed: IN-PLACE (Unit, same list mutated, nothing returned); size>1
/// guard; DELEGATES to java.util.Collections.sort(List) — already implemented.
/// The Kotlin T : Comparable&lt;T&gt; bound maps to that helper's documented bounded
/// natural-ordering behavior (README boundary #47).
///
/// mutableListOf(): real contract per the brief (verified against the Kotlin
/// stdlib source): `if (elements.isEmpty()) ArrayList() else
/// ArrayList(elements.asList())` — a new, independent ArrayList containing a
/// COPY of the varargs elements. Same shape as the already-built
/// ArraysKt.toMutableList (boundary #54): new
/// DexObject("Ljava/util/ArrayList;") + ListPeer + copy each array element +
/// state.ArrayLists.Add. The empty-varargs special case (ArrayList() no-arg vs
/// ArrayList(Collection)) differs only in initial capacity, which the bounded
/// ListPeer does not model — observably identical here (an empty list either
/// way), confirmed rather than assumed.
///
/// Probe of SKYNET-FlexGrabber.apk: the executed call is
/// CollectionsKt.mutableListOf([Ljava/lang/Object;) from Lokio
/// Options$Companion.of (the same Options.of chain as boundaries #54-57). The
/// zero-arg mutableListOf(), listOf (1-arg/vararg), arrayListOf,
/// listOfNotNull, buildList, createListBuilder, emptyList ARE method-table-
/// referenced but only from bundled-lib helpers (ArraysKt___*,
/// CollectionsKt___*, gms, androidx) that do NOT run on this path; per strict
/// scope they are NOT built — reported, future boundaries if reached.
/// </summary>
internal static class KotlinCollectionsCollectionsKtBindings
{
    private const string ArrayListClass = "Ljava/util/ArrayList;";

    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        builder.Register(Api("Lkotlin/collections/CollectionsKt;", "sort", "(Ljava/util/List;)V"), (_, args) =>
        {
            // Thin delegate: real Kotlin sort() = if (size>1) Collections.sort(this).
            JavaUtilCollectionsBindings.Sort(state, RequireDex(args[0]), null);
            return null!;
        });
        builder.Register(Api("Lkotlin/collections/CollectionsKt;", "mutableListOf", "([Ljava/lang/Object;)Ljava/util/List;"), (_, args) =>
        {
            // Real Kotlin mutableListOf(vararg): if empty -> ArrayList(), else
            // ArrayList(elements.asList()) — a new independent ArrayList holding a
            // copy of the elements. Reuses the exact ArrayList/ListPeer
            // construction shape ArraysKt.toMutableList established (boundary #54).
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
    private static DexObject RequireDex(object? value) => value as DexObject ?? throw new ArgumentException("Expected a guest object.");
}
