#nullable enable
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for the kotlin.collections.CollectionsKt surface the SKYNET
/// launch path executes. Three members, all on the Lokio Options$Companion.of
/// chain:
/// - MutableList&lt;T&gt;.sort() — thin delegate to the existing
///   Collections.sort(List) machinery (boundary #55).
/// - mutableListOf(vararg elements: T) — a new, independent ArrayList
///   populated from the varargs array (boundary #58).
/// - binarySearch(List, Comparable, fromIndex, toIndex) + its $default
///   wrapper (this brief).
///
/// sort(): real contract VERIFIED against the Kotlin stdlib JVM source
/// (MutableCollectionsJVM.kt): `if (size > 1) java.util.Collections.sort(this)`.
/// T : Comparable&lt;T&gt; maps to the shared Sort helper's documented bounded
/// natural-ordering behavior (README #47).
///
/// mutableListOf(): `if (elements.isEmpty()) ArrayList() else
/// ArrayList(elements.asList())` — new independent ArrayList holding a copy;
/// reuses the ArraysKt.toMutableList construction shape (boundary #54).
///
/// binarySearch(List, Comparable, fromIndex, toIndex): real contract — searches
/// the sorted range [fromIndex, toIndex) using natural ordering via
/// KEY.compareTo(element) (the search key's compareTo is called on each
/// element, NOT element.compareTo(key)); returns the found index, or
/// -(insertion point)-1 when absent. The not-found encoding and insertion-point
/// definition are VERIFIED against the JDK Collections.binarySearch docs
/// (identical formula): "(-(insertion point) - 1). The insertion point is
/// defined as the point at which the key would be inserted into the list: the
/// index of the first element greater than the key, or list.size() if all
/// elements in the list are less than the specified key. Note that this
/// guarantees that the return value will be >= 0 if and only if the key is
/// found." Range validation mirrors the real contract
/// (IndexOutOfBoundsException). The $default wrapper follows the established
/// RegisterWithMask convention (trailing int bitmask + always-null Object
/// marker; bit set = use the documented default) — implemented inline rather
/// than reusing RegisterWithMask itself because toIndex's default is the
/// runtime list size, not the uniform 0 that helper substitutes.
///
/// Probe of SKYNET-FlexGrabber.apk: the executed call is
/// CollectionsKt.binarySearch$default(List, Comparable, III, Object) from Lokio
/// Options$Companion.of. The real binarySearch(List, Comparable, II) overload
/// (which the $default delegates to) is also bound so both resolve; the
/// Function1 (binarySearchBy) and Comparator overloads ARE method-table-
/// referenced but only from bundled-lib helpers (CollectionsKt___*) that do
/// NOT run on this path — NOT built, reported.
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
        // binarySearch(List, Comparable): the REAL overload (fromIndex=0,
        // toIndex=size defaults) — registered so the $default wrapper and any
        // direct call both resolve. Real contract: searches [fromIndex, toIndex)
        // using natural ordering via KEY.compareTo(element) (the search key's
        // compareTo is called on each element, NOT element.compareTo(key));
        // returns the found index, or -(insertion point)-1 when absent (same
        // encoding as Collections.binarySearch — insertion point = index of the
        // first element greater than the key, or size if all less). Not-found
        // formula verified against the JDK Collections.binarySearch docs.
        builder.Register(Api("Lkotlin/collections/CollectionsKt;", "binarySearch", "(Ljava/util/List;Ljava/lang/Comparable;II)I"), (_, args) =>
            BinarySearch(state, RequireDex(args[0]), RequireDex(args[1]), RequireInt(args[2]), RequireInt(args[3])));
        // binarySearch$default: the Kotlin synthetic wrapper — trailing int
        // bitmask + always-null Object marker; bit 0 set = use fromIndex default
        // (0), bit 1 set = use toIndex default (size). Same $default convention
        // RegisterWithMask established for StringsKt; implemented inline because
        // toIndex's default is the runtime list size (not the uniform 0).
        builder.Register(Api("Lkotlin/collections/CollectionsKt;", "binarySearch$default", "(Ljava/util/List;Ljava/lang/Comparable;IIILjava/lang/Object;)I"), (_, args) =>
        {
            var list = RequireDex(args[0]);
            var comparable = RequireDex(args[1]);
            int mask = RequireInt(args[4]);
            int fromIndex = ((mask >> 0) & 1) != 0 ? 0 : RequireInt(args[2]);
            int toIndex = ((mask >> 1) & 1) != 0 ? state.ArrayLists.Get(list).Elements.Count : RequireInt(args[3]);
            return BinarySearch(state, list, comparable, fromIndex, toIndex);
        });
    }

    /// <summary>Real binary search over [fromIndex, toIndex): natural ordering
    /// via KEY.compareTo(element). Returns the found index, else
    /// -(insertion point)-1 (JDK-verified encoding). Range validation mirrors
    /// the real contract: fromIndex &lt; 0 or toIndex &gt; size or
    /// fromIndex &gt; toIndex throws IndexOutOfBoundsException.</summary>
    private static int BinarySearch(AndroidFrameworkState state, DexObject list, DexObject comparable, int fromIndex, int toIndex)
    {
        var elements = state.ArrayLists.Get(list).Elements;
        if (fromIndex < 0 || toIndex > elements.Count || fromIndex > toIndex)
            throw new GuestExceptionCarrier(GuestThrowableMetadata.Create("Ljava/lang/IndexOutOfBoundsException;"));
        int low = fromIndex;
        int high = toIndex - 1;
        while (low <= high)
        {
            int mid = low + ((high - low) >> 1);
            // Real direction: KEY.compareTo(element) — the search key's compareTo.
            int comparison = CompareKeyToElement(state, comparable, elements[mid]);
            if (comparison < 0) high = mid - 1;
            else if (comparison > 0) low = mid + 1;
            else return mid;
        }
        // Not found: -(insertion point) - 1 (JDK-verified; insertion point = low).
        return -low - 1;
    }

    /// <summary>KEY.compareTo(element) with the same dispatch the Collections
    /// Sort/Compare helper uses: framework boxed numerics (Integer/Long/Boolean)
    /// compare by raw value, strings ordinal, guest DexObjects try their guest
    /// compareTo(Object). The KEY is the left operand (real Kotlin binarySearch
    /// direction), which for the numeric/string cases is value-symmetric with
    /// element.compareTo(key).</summary>
    private static int CompareKeyToElement(AndroidFrameworkState state, DexObject key, object? element)
    {
        if (key.TypeDescriptor == "Ljava/lang/Integer;" && element is DexObject elementDex && elementDex.TypeDescriptor == "Ljava/lang/Integer;")
            return IntRaw(state, key).CompareTo(IntRaw(state, elementDex));
        if (key.TypeDescriptor == "Ljava/lang/Long;" && element is DexObject elementDexL && elementDexL.TypeDescriptor == "Ljava/lang/Long;")
            return LongRaw(state, key).CompareTo(LongRaw(state, elementDexL));
        if (key.TypeDescriptor == "Ljava/lang/Boolean;" && element is DexObject elementDexB && elementDexB.TypeDescriptor == "Ljava/lang/Boolean;")
            return BooleanRaw(state, key).CompareTo(BooleanRaw(state, elementDexB));
        var interpreter = state.Interpreter ?? throw new InvalidOperationException("compareTo requires an attached interpreter.");
        return RequireInt(interpreter.InvokeInstanceExact(key, "compareTo", "(Ljava/lang/Object;)I", element));
    }

    private static int IntRaw(AndroidFrameworkState state, DexObject box) => (int)state.Boxed.Get(box).RawValue;
    private static long LongRaw(AndroidFrameworkState state, DexObject box) => (long)state.Boxed.Get(box).RawValue;
    private static int BooleanRaw(AndroidFrameworkState state, DexObject box) => (int)state.Boxed.Get(box).RawValue;
    private static int RequireInt(object? value) => AndroidApiBindings.RequireInt(value ?? 0);
    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    private static DexObject RequireDex(object? value) => value as DexObject ?? throw new ArgumentException("Expected a guest object.");
}
