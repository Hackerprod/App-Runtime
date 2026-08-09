#nullable enable
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.ApiLayer.Bindings;

/// <summary>
/// Bindings for java.util.ArrayDeque (double-ended queue implementing Deque and
/// Queue). Reuses the shared ListPeer shape (same as ArrayList/CopyOnWriteArrayList)
/// — double-ended operations map directly onto List<object?> (addFirst = insert 0,
/// addLast = append, etc.). Two real contracts are honored distinctly, NOT
/// conflated:
/// - getLast/remove/removeFirst/removeLast/pop THROW NoSuchElementException on
///   empty (real Java; pop is documented as removeFirst);
/// - peek/poll/pollFirst return null on empty instead.
/// - ArrayDeque does NOT permit null elements (unlike ArrayList) — every add-style
///   method throws NullPointerException for null, per the spec.
/// Probe-confirmed surface only: <init>()V, <init>(Collection), add, addFirst,
/// addLast, offer (Queue alias), clear, getLast, isEmpty, iterator, peek, poll,
/// pollFirst, pop, push, remove, removeFirst, removeLast, size, remove(Object)Z
/// (first occurrence). NOT built (not referenced): getFirst, peekFirst/peekLast,
/// offerFirst/offerLast, element, removeFirstOccurrence/removeLastOccurrence,
/// descendingIterator, toArray/array-conversion.
/// </summary>
internal static class JavaUtilArrayDequeBindings
{
    private const string ArrayDeque = "Ljava/util/ArrayDeque;";
    private const string Deque = "Ljava/util/Deque;";
    private const string Queue = "Ljava/util/Queue;";
    private const string NoSuchElement = "Ljava/util/NoSuchElementException;";
    private const string NullPointer = "Ljava/lang/NullPointerException;";

    internal static void Register(AndroidApiRegistryBuilder builder, AndroidFrameworkState state)
    {
        builder.Register(Api(ArrayDeque, "<init>", "()V"), (_, args) => { state.ArrayDeques.Add(Receiver(args), new ListPeer()); return null!; });
        builder.Register(Api(ArrayDeque, "<init>", "(Ljava/util/Collection;)V"), (_, args) =>
        {
            var peer = new ListPeer();
            foreach (object? item in CollectionItems(state, RequireDex(args[1])))
                peer.Elements.Add(RequireNonNull(item));
            state.ArrayDeques.Add(Receiver(args), peer);
            return null!;
        });

        // ---- Adds (all reject null with NPE) ----
        builder.Register(Api(ArrayDeque, "add", "(Ljava/lang/Object;)Z"), (_, args) => { Peer(state, args[0]).Elements.Add(RequireNonNull(args[1])); return 1; });
        builder.Register(Api(ArrayDeque, "addFirst", "(Ljava/lang/Object;)V"), (_, args) => { Peer(state, args[0]).Elements.Insert(0, RequireNonNull(args[1])); return null!; });
        builder.Register(Api(ArrayDeque, "addLast", "(Ljava/lang/Object;)V"), (_, args) => { Peer(state, args[0]).Elements.Add(RequireNonNull(args[1])); return null!; });
        builder.Register(Api(ArrayDeque, "offer", "(Ljava/lang/Object;)Z"), (_, args) => { Peer(state, args[0]).Elements.Add(RequireNonNull(args[1])); return 1; });
        builder.Register(Api(ArrayDeque, "push", "(Ljava/lang/Object;)V"), (_, args) => { Peer(state, args[0]).Elements.Insert(0, RequireNonNull(args[1])); return null!; });

        // ---- Removes: getLast/remove/removeFirst/removeLast/pop THROW on empty ----
        builder.Register(Api(ArrayDeque, "remove", "()Ljava/lang/Object;"), (_, args) => RemoveFirst(state, args[0], throwOnEmpty: true));
        builder.Register(Api(ArrayDeque, "removeFirst", "()Ljava/lang/Object;"), (_, args) => RemoveFirst(state, args[0], throwOnEmpty: true));
        builder.Register(Api(ArrayDeque, "removeLast", "()Ljava/lang/Object;"), (_, args) => RemoveLast(state, args[0], throwOnEmpty: true));
        builder.Register(Api(ArrayDeque, "pop", "()Ljava/lang/Object;"), (_, args) => RemoveFirst(state, args[0], throwOnEmpty: true));
        builder.Register(Api(ArrayDeque, "getLast", "()Ljava/lang/Object;"), (_, args) =>
        {
            var elements = Peer(state, args[0]).Elements;
            if (elements.Count == 0) throw new GuestExceptionCarrier(GuestThrowableMetadata.Create(NoSuchElement));
            return elements[^1] ?? null!;
        });

        // ---- Peeks/polls: poll/pollFirst return null on empty ----
        builder.Register(Api(ArrayDeque, "peek", "()Ljava/lang/Object;"), (_, args) => PeekFirst(state, args[0]));
        builder.Register(Api(ArrayDeque, "poll", "()Ljava/lang/Object;"), (_, args) => RemoveFirst(state, args[0], throwOnEmpty: false));
        builder.Register(Api(ArrayDeque, "pollFirst", "()Ljava/lang/Object;"), (_, args) => RemoveFirst(state, args[0], throwOnEmpty: false));

        // ---- remove(Object)Z: remove the FIRST occurrence ----
        builder.Register(Api(ArrayDeque, "remove", "(Ljava/lang/Object;)Z"), (_, args) => RemoveOccurrence(state, args[0], args[1]) ? 1 : 0);
        builder.Register(Api(Deque, "remove", "(Ljava/lang/Object;)Z"), (_, args) => RemoveOccurrence(state, args[0], args[1]) ? 1 : 0);

        // ---- Size / empty / clear / iterator ----
        builder.Register(Api(ArrayDeque, "size", "()I"), (_, args) => Peer(state, args[0]).Elements.Count);
        builder.Register(Api(ArrayDeque, "isEmpty", "()Z"), (_, args) => Peer(state, args[0]).Elements.Count == 0 ? 1 : 0);
        builder.Register(Api(ArrayDeque, "clear", "()V"), (_, args) => { Peer(state, args[0]).Elements.Clear(); return null!; });
        builder.Register(Api(ArrayDeque, "iterator", "()Ljava/util/Iterator;"), (_, args) => CreateIterator(state, Peer(state, args[0]).Elements));

        // ---- Interface-typed aliases (Queue/Deque ids so interface call sites resolve) ----
        builder.Register(Api(Queue, "add", "(Ljava/lang/Object;)Z"), (_, args) => { Peer(state, args[0]).Elements.Add(RequireNonNull(args[1])); return 1; });
        builder.Register(Api(Queue, "offer", "(Ljava/lang/Object;)Z"), (_, args) => { Peer(state, args[0]).Elements.Add(RequireNonNull(args[1])); return 1; });
        builder.Register(Api(Queue, "poll", "()Ljava/lang/Object;"), (_, args) => RemoveFirst(state, args[0], throwOnEmpty: false));
        builder.Register(Api(Deque, "peek", "()Ljava/lang/Object;"), (_, args) => PeekFirst(state, args[0]));
        builder.Register(Api(Deque, "pop", "()Ljava/lang/Object;"), (_, args) => RemoveFirst(state, args[0], throwOnEmpty: true));
        builder.Register(Api(Deque, "push", "(Ljava/lang/Object;)V"), (_, args) => { Peer(state, args[0]).Elements.Insert(0, RequireNonNull(args[1])); return null!; });
        builder.Register(Api(Deque, "removeFirst", "()Ljava/lang/Object;"), (_, args) => RemoveFirst(state, args[0], throwOnEmpty: true));
        builder.Register(Api(Deque, "isEmpty", "()Z"), (_, args) => Peer(state, args[0]).Elements.Count == 0 ? 1 : 0);
        builder.Register(Api(Deque, "iterator", "()Ljava/util/Iterator;"), (_, args) => CreateIterator(state, Peer(state, args[0]).Elements));
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static ListPeer Peer(AndroidFrameworkState state, object receiver) => state.ArrayDeques.Get((DexObject)receiver);

    private static object RequireNonNull(object? value) =>
        value is null || value is int zero && zero == 0
            ? throw new GuestExceptionCarrier(GuestThrowableMetadata.Create(NullPointer))
            : value;

    private static object PeekFirst(AndroidFrameworkState state, object receiver)
    {
        var elements = Peer(state, receiver).Elements;
        return elements.Count == 0 ? null! : elements[0] ?? null!;
    }

    private static object RemoveFirst(AndroidFrameworkState state, object receiver, bool throwOnEmpty)
    {
        var elements = Peer(state, receiver).Elements;
        if (elements.Count == 0)
        {
            if (throwOnEmpty) throw new GuestExceptionCarrier(GuestThrowableMetadata.Create(NoSuchElement));
            return null!;
        }
        object? result = elements[0];
        elements.RemoveAt(0);
        return result ?? null!;
    }

    private static object RemoveLast(AndroidFrameworkState state, object receiver, bool throwOnEmpty)
    {
        var elements = Peer(state, receiver).Elements;
        if (elements.Count == 0)
        {
            if (throwOnEmpty) throw new GuestExceptionCarrier(GuestThrowableMetadata.Create(NoSuchElement));
            return null!;
        }
        object? result = elements[^1];
        elements.RemoveAt(elements.Count - 1);
        return result ?? null!;
    }

    private static bool RemoveOccurrence(AndroidFrameworkState state, object receiver, object? value)
    {
        RequireNonNull(value);
        var elements = Peer(state, receiver).Elements;
        int index = elements.IndexOf(value);
        if (index < 0) return false;
        elements.RemoveAt(index);
        return true;
    }

    private static IEnumerable<object?> CollectionItems(AndroidFrameworkState state, DexObject collection) =>
        collection.TypeDescriptor == "Ljava/util/ArrayList;" || collection.TypeDescriptor == "Ljava/util/ArrayDeque;"
            ? collection.TypeDescriptor == "Ljava/util/ArrayList;"
                ? state.ArrayLists.Get(collection).Elements
                : state.ArrayDeques.Get(collection).Elements
            : state.MapViews.Get(collection).Cast<object?>();

    private static DexObject CreateIterator(AndroidFrameworkState state, IEnumerable<object?> elements)
    {
        var iterator = new DexObject("Ljava/util/Iterator;");
        state.Iterators.Add(iterator, new IteratorPeer(elements));
        return iterator;
    }

    private static AndroidApiMethodId Api(string owner, string name, string descriptor) => new(owner, name, descriptor);
    private static DexObject Receiver(object[] args) => (DexObject)args[0]!;
    private static DexObject RequireDex(object? value) => value as DexObject ?? throw new ArgumentException("Expected a guest object.");
}
