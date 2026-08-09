using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.ApiLayer.Bindings;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.Core.Tests;

/// <summary>
/// Tests for kotlin.collections.List&lt;T&gt;.binarySearch(Comparable, fromIndex,
/// toIndex) + its $default wrapper against the REAL contract: searches the
/// sorted range using KEY.compareTo(element); found returns the index, not
/// found returns -(insertion point) - 1 (the JDK-verified encoding — e.g. a
/// value that would insert at the start returns -1, in the middle -k-1, at the
/// end -(size)-1). Range validation throws IndexOutOfBoundsException.
/// </summary>
public sealed class BinarySearchTests
{
    private const string CollectionsKt = "Lkotlin/collections/CollectionsKt;";
    private const string ArrayList = "Ljava/util/ArrayList;";
    private const string Integer = "Ljava/lang/Integer;";

    private static readonly int[] Sorted = [10, 20, 30, 40, 50];

    [Theory]
    [InlineData(10, 0)]
    [InlineData(20, 1)]
    [InlineData(30, 2)]
    [InlineData(40, 3)]
    [InlineData(50, 4)]
    public void Found_element_returns_its_index(int key, int expected)
    {
        var (state, registry, _) = Session();
        var list = NewSortedList(registry, state);
        var comparable = state.BoxedObject(Integer, key);
        Assert.Equal(expected, Invoke(registry, state, CollectionsKt, "binarySearch", "(Ljava/util/List;Ljava/lang/Comparable;II)I", AndroidInvokeKind.Static, list, comparable, 0, Sorted.Length));
    }

    [Fact]
    public void Not_found_returns_negative_insertion_point_minus_one()
    {
        var (state, registry, _) = Session();
        var list = NewSortedList(registry, state);
        int Size = Sorted.Length;

        // Would insert at start (index 0) -> -(0)-1 = -1.
        Assert.Equal(-1, Invoke(registry, state, CollectionsKt, "binarySearch", "(Ljava/util/List;Ljava/lang/Comparable;II)I", AndroidInvokeKind.Static, list, state.BoxedObject(Integer, 5), 0, Size));
        // Would insert in the middle (index 2) -> -(2)-1 = -3.
        Assert.Equal(-3, Invoke(registry, state, CollectionsKt, "binarySearch", "(Ljava/util/List;Ljava/lang/Comparable;II)I", AndroidInvokeKind.Static, list, state.BoxedObject(Integer, 25), 0, Size));
        // Would insert at the end (index 5) -> -(5)-1 = -6.
        Assert.Equal(-6, Invoke(registry, state, CollectionsKt, "binarySearch", "(Ljava/util/List;Ljava/lang/Comparable;II)I", AndroidInvokeKind.Static, list, state.BoxedObject(Integer, 60), 0, Size));
        // The ">= 0 iff found" guarantee: every miss is negative.
        Assert.True((int)Invoke(registry, state, CollectionsKt, "binarySearch", "(Ljava/util/List;Ljava/lang/Comparable;II)I", AndroidInvokeKind.Static, list, state.BoxedObject(Integer, 35), 0, Size) < 0);
    }

    [Fact]
    public void Range_restricts_the_search()
    {
        var (state, registry, _) = Session();
        var list = NewSortedList(registry, state);
        // Range [2,3) contains only index 2 = 30. 20 is less than 30: insertion
        // point within the range is 2 -> -(2)-1 = -3.
        Assert.Equal(-3, Invoke(registry, state, CollectionsKt, "binarySearch", "(Ljava/util/List;Ljava/lang/Comparable;II)I", AndroidInvokeKind.Static, list, state.BoxedObject(Integer, 20), 2, 3));
        // 30 inside [2,3) -> index 2.
        Assert.Equal(2, Invoke(registry, state, CollectionsKt, "binarySearch", "(Ljava/util/List;Ljava/lang/Comparable;II)I", AndroidInvokeKind.Static, list, state.BoxedObject(Integer, 30), 2, 3));
        // 40 is outside [0,2): insertion point within the range is 2 -> -(2)-1 = -3.
        Assert.Equal(-3, Invoke(registry, state, CollectionsKt, "binarySearch", "(Ljava/util/List;Ljava/lang/Comparable;II)I", AndroidInvokeKind.Static, list, state.BoxedObject(Integer, 40), 0, 2));
    }

    [Fact]
    public void Empty_range_returns_negative_insertion_point()
    {
        var (state, registry, _) = Session();
        var list = NewSortedList(registry, state);
        // fromIndex == toIndex: empty range; insertion point = fromIndex -> -(2)-1 = -3.
        Assert.Equal(-3, Invoke(registry, state, CollectionsKt, "binarySearch", "(Ljava/util/List;Ljava/lang/Comparable;II)I", AndroidInvokeKind.Static, list, state.BoxedObject(Integer, 30), 2, 2));
    }

    [Fact]
    public void Single_element_range()
    {
        var (state, registry, _) = Session();
        var list = NewSortedList(registry, state);
        Assert.Equal(2, Invoke(registry, state, CollectionsKt, "binarySearch", "(Ljava/util/List;Ljava/lang/Comparable;II)I", AndroidInvokeKind.Static, list, state.BoxedObject(Integer, 30), 2, 3));
        Assert.Equal(-3, Invoke(registry, state, CollectionsKt, "binarySearch", "(Ljava/util/List;Ljava/lang/Comparable;II)I", AndroidInvokeKind.Static, list, state.BoxedObject(Integer, 25), 2, 3));
    }

    [Fact]
    public void Out_of_bounds_range_throws_index_out_of_bounds()
    {
        var (state, registry, _) = Session();
        var list = NewSortedList(registry, state);
        var error = Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, CollectionsKt, "binarySearch", "(Ljava/util/List;Ljava/lang/Comparable;II)I", AndroidInvokeKind.Static, list, state.BoxedObject(Integer, 10), -1, Sorted.Length));
        Assert.Equal("Ljava/lang/IndexOutOfBoundsException;", error.Throwable.TypeDescriptor);
        Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, CollectionsKt, "binarySearch", "(Ljava/util/List;Ljava/lang/Comparable;II)I", AndroidInvokeKind.Static, list, state.BoxedObject(Integer, 10), 0, Sorted.Length + 1));
        Assert.Throws<GuestExceptionCarrier>(() => Invoke(registry, state, CollectionsKt, "binarySearch", "(Ljava/util/List;Ljava/lang/Comparable;II)I", AndroidInvokeKind.Static, list, state.BoxedObject(Integer, 10), 3, 2));
    }

    [Fact]
    public void Default_wrapper_applies_from_index_zero_and_to_index_size()
    {
        var (state, registry, _) = Session();
        var list = NewSortedList(registry, state);
        // $default(List, Comparable, fromIndex, toIndex, mask, marker):
        // mask 0 = use BOTH passed values (0, size) — same as full call.
        Assert.Equal(4, Invoke(registry, state, CollectionsKt, "binarySearch$default", "(Ljava/util/List;Ljava/lang/Comparable;IIILjava/lang/Object;)I", AndroidInvokeKind.Static, list, state.BoxedObject(Integer, 50), 0, Sorted.Length, 0, null!));
        // mask bit0 (1) = fromIndex default 0; bit1 (2) = toIndex default size.
        // mask 1: fromIndex defaults to 0, toIndex = passed (say 3) -> searches [0,3).
        Assert.Equal(2, Invoke(registry, state, CollectionsKt, "binarySearch$default", "(Ljava/util/List;Ljava/lang/Comparable;IIILjava/lang/Object;)I", AndroidInvokeKind.Static, list, state.BoxedObject(Integer, 30), 0, 3, 1, null!));
        // mask 2: fromIndex = passed 1, toIndex defaults to size -> [1, size).
        Assert.Equal(2, Invoke(registry, state, CollectionsKt, "binarySearch$default", "(Ljava/util/List;Ljava/lang/Comparable;IIILjava/lang/Object;)I", AndroidInvokeKind.Static, list, state.BoxedObject(Integer, 30), 1, 0, 2, null!));
        // mask 3: both default -> [0, size) — found at index 2.
        Assert.Equal(2, Invoke(registry, state, CollectionsKt, "binarySearch$default", "(Ljava/util/List;Ljava/lang/Comparable;IIILjava/lang/Object;)I", AndroidInvokeKind.Static, list, state.BoxedObject(Integer, 30), 99, 99, 3, null!));
    }

    [Fact]
    public void Duplicate_elements_find_a_matching_index_not_necessarily_first()
    {
        var (state, registry, _) = Session();
        var list = new DexObject(ArrayList);
        Invoke(registry, state, ArrayList, "<init>", "()V", AndroidInvokeKind.Direct, list);
        foreach (int value in new[] { 10, 20, 20, 20, 30 })
            Invoke(registry, state, ArrayList, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, state.BoxedObject(Integer, value));

        int found = (int)Invoke(registry, state, CollectionsKt, "binarySearch", "(Ljava/util/List;Ljava/lang/Comparable;II)I", AndroidInvokeKind.Static, list, state.BoxedObject(Integer, 20), 0, 5)!;
        // Real algorithm guarantees A matching index (one of 1..3), not the first.
        Assert.InRange(found, 1, 3);
    }

    private static DexObject NewSortedList(AndroidApiRegistry registry, AndroidFrameworkState state)
    {
        var list = new DexObject(ArrayList);
        Invoke(registry, state, ArrayList, "<init>", "()V", AndroidInvokeKind.Direct, list);
        foreach (int value in Sorted)
            Invoke(registry, state, ArrayList, "add", "(Ljava/lang/Object;)Z", AndroidInvokeKind.Virtual, list, state.BoxedObject(Integer, value));
        return list;
    }

    private static (AndroidFrameworkState State, AndroidApiRegistry Registry, DexInterpreter Interpreter) Session()
    {
        var state = new AndroidFrameworkState("s", "p", "La;", new ActivityWindowPeers());
        var registry = AndroidApiBindings.CreateBuilder(state, new QuietLogSink()).Build();
        var dex = new DexFile();
        var interpreter = new DexInterpreter(dex, registry, gil: state.Gil);
        state.Gil = interpreter.Gil;
        state.AttachInterpreter(interpreter);
        return (state, registry, interpreter);
    }

    private static object Invoke(AndroidApiRegistry registry, AndroidFrameworkState state, string owner, string name, string descriptor, AndroidInvokeKind kind, params object[] args)
    {
        var api = new AndroidApiMethodId(owner, name, descriptor);
        var context = new AndroidApiSessionContext(state.SessionId, state.PackageName, state.ActivityDescriptor, default, () => true);
        if (state.Interpreter is not null) context.IsTypeAssignable = state.Interpreter.IsGuestTypeAssignable;
        return registry.Invoke(context, new AndroidApiCallSite("Ltest;->run()V", 0, api, api, kind), args);
    }

    private sealed class QuietLogSink : IAndroidLogSink { public int Info(AndroidLogEntry entry) => 1; }
}
