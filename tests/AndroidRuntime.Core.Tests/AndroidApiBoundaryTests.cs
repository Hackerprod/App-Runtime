using AndroidRuntime.Core.ApiLayer;

namespace AndroidRuntime.Core.Tests;

public sealed class AndroidApiBoundaryTests
{
    private static readonly AndroidApiMethodId StringPick = new("Lexample/Api;", "pick", "(Ljava/lang/String;)I");
    private static readonly AndroidApiMethodId ObjectPick = new("Lexample/Api;", "pick", "(Ljava/lang/Object;)I");

    [Fact]
    public void Builder_rejects_duplicates_and_built_registry_is_an_immutable_snapshot()
    {
        var builder = new AndroidApiRegistryBuilder().Register(StringPick, (_, _) => 1);

        Assert.Throws<ArgumentException>(() => builder.Register(StringPick, (_, _) => 2));
        var snapshot = builder.Build();
        builder.Register(ObjectPick, (_, _) => 2);

        Assert.True(snapshot.Contains(StringPick));
        Assert.False(snapshot.Contains(ObjectPick));
        Assert.Equal(1, snapshot.Invoke(Session(), Call(StringPick), ["value"]));
    }

    [Fact]
    public void Descriptor_overloads_remain_distinct_and_invoke_kind_is_call_metadata()
    {
        var registry = new AndroidApiRegistryBuilder()
            .Register(StringPick, (invocation, _) => invocation.InvokeKind == AndroidInvokeKind.Static ? 1 : -1)
            .Register(ObjectPick, (_, _) => 2)
            .Build();

        Assert.Equal(1, registry.Invoke(Session(), Call(StringPick, AndroidInvokeKind.Static), ["value"]));
        Assert.Equal(2, registry.Invoke(Session(), Call(ObjectPick, AndroidInvokeKind.Static), [new object()]));
    }

    [Fact]
    public void Successful_invocation_emits_correlated_requested_and_completed_events()
    {
        var trace = new AndroidApiTraceBuffer(8);
        var registry = new AndroidApiRegistryBuilder().Register(StringPick, (_, _) => 7).Build();

        Assert.Equal(7, registry.Invoke(Session(trace), Call(StringPick), ["secret-value"]));

        var events = trace.Snapshot();
        Assert.Equal([AndroidApiEventKind.Requested, AndroidApiEventKind.Completed], events.Select(item => item.Kind));
        Assert.Equal(events[0].Invocation.InvocationId, events[1].Invocation.InvocationId);
        Assert.Equal(12, events[0].Invocation.DexPc);
        Assert.Equal("Lcaller/Main;->run()V", events[0].Invocation.CallerMethod);
        Assert.Equal("session-1", events[0].Invocation.SessionId);
        Assert.Contains("len=12", events[0].Invocation.ArgumentSummaries[0], StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", events[0].Invocation.ArgumentSummaries[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_api_emits_unimplemented_and_throws_typed_error()
    {
        var trace = new AndroidApiTraceBuffer(8);
        var registry = new AndroidApiRegistryBuilder().Build();

        var error = Assert.Throws<AndroidApiNotImplementedException>(() =>
            registry.Invoke(Session(trace), Call(StringPick), ["value"]));

        Assert.Equal(StringPick, error.Api);
        Assert.Equal([AndroidApiEventKind.Requested, AndroidApiEventKind.Unimplemented], trace.Snapshot().Select(item => item.Kind));
    }

    [Fact]
    public void Binding_failure_is_wrapped_with_inner_exception_and_traced()
    {
        var trace = new AndroidApiTraceBuffer(8);
        var cause = new InvalidOperationException("binding broke");
        var registry = new AndroidApiRegistryBuilder().Register(StringPick, (_, _) => throw cause).Build();

        var error = Assert.Throws<AndroidApiBindingException>(() =>
            registry.Invoke(Session(trace), Call(StringPick), ["value"]));

        Assert.Same(cause, error.InnerException);
        Assert.Equal(AndroidApiEventKind.Failed, trace.Snapshot()[^1].Kind);
    }

    [Fact]
    public void Cancellation_is_not_wrapped_and_is_traced()
    {
        var trace = new AndroidApiTraceBuffer(8);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var registry = new AndroidApiRegistryBuilder().Register(StringPick, (_, _) => 1).Build();

        Assert.Throws<OperationCanceledException>(() =>
            registry.Invoke(Session(trace, cancellation.Token), Call(StringPick), ["value"]));

        Assert.Equal(AndroidApiEventKind.Cancelled, trace.Snapshot()[^1].Kind);
    }

    [Fact]
    public void Trace_sink_failure_never_changes_binding_semantics()
    {
        var registry = new AndroidApiRegistryBuilder().Register(StringPick, (_, _) => 9).Build();

        Assert.Equal(9, registry.Invoke(Session(new ThrowingTraceSink()), Call(StringPick), ["value"]));
    }

    [Fact]
    public void Trace_buffer_is_bounded_and_counts_dropped_events()
    {
        var trace = new AndroidApiTraceBuffer(2);
        var registry = new AndroidApiRegistryBuilder().Register(StringPick, (_, _) => 1).Build();
        var session = Session(trace);

        registry.Invoke(session, Call(StringPick), ["one"]);
        registry.Invoke(session, Call(StringPick), ["two"]);

        Assert.Equal(2, trace.Snapshot().Count);
        Assert.Equal(2, trace.DroppedCount);
    }

    [Fact]
    public void Immutable_snapshot_supports_concurrent_reads()
    {
        var registry = new AndroidApiRegistryBuilder().Register(StringPick, (_, _) => 11).Build();
        var results = new int[256];

        Parallel.For(0, results.Length, index =>
            results[index] = (int)registry.Invoke(Session(), Call(StringPick), ["value"]));

        Assert.All(results, value => Assert.Equal(11, value));
    }

    [Fact]
    public void Invocation_rejects_null_instance_receiver_static_shape_and_invalid_return()
    {
        var voidApi = new AndroidApiMethodId("Lexample/Api;", "run", "()V");
        var registry = new AndroidApiRegistryBuilder()
            .Register(voidApi, (_, _) => 1)
            .Register(StringPick, (_, _) => 1)
            .Build();

        Assert.Throws<AndroidApiNullReferenceException>(() => registry.Invoke(
            Session(), Call(voidApi, AndroidInvokeKind.Virtual), [null!]));
        Assert.Throws<ArgumentException>(() => registry.Invoke(
            Session(), Call(StringPick, AndroidInvokeKind.Static), [new object(), "extra"]));
        Assert.Throws<AndroidApiBindingException>(() => registry.Invoke(
            Session(), Call(voidApi, AndroidInvokeKind.Static), []));
    }

    private static AndroidApiSessionContext Session(
        IAndroidApiTraceSink? trace = null,
        CancellationToken cancellationToken = default) =>
        new("session-1", "org.example", "Lexample/Main;", cancellationToken, () => true, trace);

    private static AndroidApiCallSite Call(
        AndroidApiMethodId api,
        AndroidInvokeKind kind = AndroidInvokeKind.Static) =>
        new("Lcaller/Main;->run()V", 12, api, api, kind);

    private sealed class ThrowingTraceSink : IAndroidApiTraceSink
    {
        public void Record(AndroidApiTraceEvent traceEvent) => throw new InvalidOperationException("sink failed");
    }
}
