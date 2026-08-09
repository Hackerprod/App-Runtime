#nullable enable
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for java.util.LinkedHashSet — a Set that guarantees ITERATION IN
/// INSERTION ORDER (its entire reason to exist over plain HashSet). The backing
/// peer is OrderedSetPeer: a List&lt;object?&gt; keeping order plus a linear Contains
/// check before Add for no-duplicate set semantics — deliberately NOT the raw
/// HashSet&lt;object?&gt; used by CopyOnWriteArraySet, because .NET does not reliably
/// guarantee HashSet enumeration order (wrong order would be a latent bug even if
/// not observed on this path). Real semantics: a duplicate add returns false and
/// does NOT move the element (first-insertion order, not most-recent-touch);
/// remove then re-add DOES move it to the end. Probe-confirmed surface only:
/// &lt;init&gt;()V, &lt;init&gt;(I)V, &lt;init&gt;(Collection), add, addAll, clear, isEmpty,
/// iterator, remove, removeAll. size/contains are NOT referenced — omitted by
/// discipline.
/// </summary>
internal static class JavaUtilLinkedHashSetBindings
{
    private const string LinkedHashSet = "Ljava/util/LinkedHashSet;";

    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        builder.Register(Api(LinkedHashSet, "<init>", "()V"), (_, args) => { state.LinkedHashSets.Add(Receiver(args), new OrderedSetPeer()); return null!; });
        builder.Register(Api(LinkedHashSet, "<init>", "(I)V"), (_, args) => { RequireInt(args[1]); state.LinkedHashSets.Add(Receiver(args), new OrderedSetPeer()); return null!; });
        builder.Register(Api(LinkedHashSet, "<init>", "(Ljava/util/Collection;)V"), (_, args) =>
        {
            var peer = new OrderedSetPeer();
            foreach (object? item in CollectionItems(state, RequireDex(args[1])))
                peer.Add(item);
            state.LinkedHashSets.Add(Receiver(args), peer);
            return null!;
        });

        builder.Register(Api(LinkedHashSet, "add", "(Ljava/lang/Object;)Z"), (_, args) => state.LinkedHashSets.Get(Receiver(args)).Add(args[1]) ? 1 : 0);
        builder.Register(Api(LinkedHashSet, "addAll", "(Ljava/util/Collection;)Z"), (_, args) =>
        {
            var peer = state.LinkedHashSets.Get(Receiver(args));
            bool changed = false;
            foreach (object? item in CollectionItems(state, RequireDex(args[1])))
                changed |= peer.Add(item);
            return changed ? 1 : 0;
        });
        builder.Register(Api(LinkedHashSet, "remove", "(Ljava/lang/Object;)Z"), (_, args) => state.LinkedHashSets.Get(Receiver(args)).Remove(args[1]) ? 1 : 0);
        builder.Register(Api(LinkedHashSet, "removeAll", "(Ljava/util/Collection;)Z"), (_, args) =>
        {
            var peer = state.LinkedHashSets.Get(Receiver(args));
            bool changed = false;
            foreach (object? item in CollectionItems(state, RequireDex(args[1])))
                changed |= peer.Remove(item);
            return changed ? 1 : 0;
        });
        builder.Register(Api(LinkedHashSet, "clear", "()V"), (_, args) => { state.LinkedHashSets.Get(Receiver(args)).Clear(); return null!; });
        builder.Register(Api(LinkedHashSet, "isEmpty", "()Z"), (_, args) => state.LinkedHashSets.Get(Receiver(args)).Count == 0 ? 1 : 0);
        builder.Register(Api(LinkedHashSet, "iterator", "()Ljava/util/Iterator;"), (_, args) => CreateIterator(state, state.LinkedHashSets.Get(Receiver(args)).Elements));
    }

    internal static IEnumerable<object?> CollectionItems(AndroidFrameworkState state, DexObject collection)
    {
        if (state.ArrayLists.TryGet(collection, out var list)) return list.Elements;
        if (state.ArrayDeques.TryGet(collection, out var deque)) return deque.Elements;
        if (state.LinkedHashSets.TryGet(collection, out var set)) return set.Elements;
        if (state.MapViews.TryGet(collection, out var view)) return view;
        if (state.CopyOnWriteArraySets.TryGet(collection, out var cow)) return cow;
        throw new InvalidOperationException("Collection source is not a bound collection: " + collection.TypeDescriptor);
    }

    private static DexObject CreateIterator(AndroidFrameworkState state, IEnumerable<object?> elements)
    {
        var iterator = new DexObject("Ljava/util/Iterator;");
        state.Iterators.Add(iterator, new IteratorPeer(elements));
        return iterator;
    }

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    private static DexObject Receiver(object[] args) => (DexObject)args[0]!;
    private static DexObject RequireDex(object? value) => value as DexObject ?? throw new ArgumentException("Expected a guest object.");
    private static int RequireInt(object? value) => AndroidApiBindings.RequireInt(value ?? 0);
}
