#nullable enable
using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Ui;

namespace AndroidRuntime.Core.Hosting;

public sealed class AndroidHostedActivitySession : IAsyncDisposable
{
    private readonly AndroidExecutionLane _lane;
    private readonly CancellationTokenSource _lifetime;
    private readonly ActivityWindowPeers _peers;
    private readonly EventHandler _closedHandler;
    private readonly EventHandler _closeRequestedHandler;
    private readonly object _shutdownGate = new();
    private readonly TaskCompletionSource _termination = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _sessionId;
    private readonly IDisposable? _ownedApkStream;
    private readonly IDisposable? _frameworkState;
    private readonly AndroidFrameworkState? _androidState;
    private Task? _shutdown;

    internal AndroidHostedActivitySession(
        AndroidActivitySession session,
        IActivityWindow window,
        AndroidApiTraceBuffer trace,
        AndroidExecutionLane lane,
        CancellationTokenSource lifetime,
        ActivityWindowPeers peers,
        string sessionId,
        IDisposable? ownedApkStream = null,
        IDisposable? frameworkState = null)
    {
        Session = session;
        Window = window;
        Trace = trace;
        _lane = lane;
        _lifetime = lifetime;
        _peers = peers;
        _sessionId = sessionId;
        _ownedApkStream = ownedApkStream;
        _frameworkState = frameworkState;
        _androidState = frameworkState as AndroidFrameworkState;
        _closedHandler = (_, _) => _ = BeginShutdown(closeWindow: false);
        _closeRequestedHandler = (_, _) => _ = BeginShutdown(closeWindow: true);
        Window.Closed += _closedHandler;
        if (Window is IDeferredActivityWindowClose deferred) deferred.CloseRequested += _closeRequestedHandler;
        if (Window is IAndroidUiSurfaceHost uiSurface) uiSurface.Attach(this);
        if (_androidState is not null) _androidState.FinishRequested += OnFinishRequested;
        if (_androidState?.IsFinishing == true) _ = BeginShutdown(closeWindow: true);
    }

    public AndroidActivitySession Session { get; }
    public string SessionId => _sessionId;
    public IActivityWindow Window { get; }
    public AndroidApiTraceBuffer Trace { get; }
    public AndroidPeerCounts PeerCounts => _frameworkState is AndroidFrameworkState state ? state.PeerCounts : default;
    public Task Termination => _termination.Task;
    public bool IsTerminated => _termination.Task.IsCompletedSuccessfully;

    public ValueTask DisposeAsync() => new(BeginShutdown(closeWindow: true));

    // Phase 2: view operations (findViewById/performClick/render) now live on the
    // IAndroidViewBridge owned by ViewRuntime; the hosted session exposes the
    // bridge so host-side UI surfaces can forward inputs/frame requests. The
    // previous local FindViewByIdAsync/PerformClickAsync/RenderUiAsync that
    // manipulated the removed C# view hierarchy are gone.
    public IAndroidViewBridge ViewBridge => _androidState?.ViewBridge ?? UnavailableAndroidViewBridge.Instance;

    private Task BeginShutdown(bool closeWindow)
    {
        lock (_shutdownGate)
            return _shutdown ??= Task.Run(() => ShutdownAsync(closeWindow));
    }

    private async Task ShutdownAsync(bool closeWindow)
    {
        Exception? terminalError = null;
        try
        {
            Window.Closed -= _closedHandler;
            if (Window is IDeferredActivityWindowClose deferred) deferred.CloseRequested -= _closeRequestedHandler;
            if (Window is IAndroidUiSurfaceHost uiSurface) uiSurface.Detach(this);
            if (_androidState is not null) _androidState.FinishRequested -= OnFinishRequested;
            try { await _lane.InvokeAsync(() => { try { Session.Terminate(); return true; } finally { _androidState?.ViewBridge.DisposeBridge(); } }, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception error) { terminalError = error; }
            _androidState?.MarkDestroyed();
            CancelNoThrow();
            if (closeWindow)
            {
                try { Window.Close(); } catch { }
            }
            _peers.Remove(Session.Activity);
            try { Window.Dispose(); } catch { }
            await _lane.DisposeAsync().ConfigureAwait(false);
            _frameworkState?.Dispose();
            _ownedApkStream?.Dispose();
            _lifetime.Dispose();
            if (terminalError is null) _termination.TrySetResult(); else _termination.TrySetException(terminalError);
        }
        catch (Exception error)
        {
            _termination.TrySetException(error);
            throw;
        }
        if (terminalError is not null) throw terminalError;
    }

    private void OnFinishRequested() => _ = BeginShutdown(closeWindow: true);

    private void CancelNoThrow()
    {
        try { _lifetime.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}

internal sealed class CompositeAndroidApiTraceSink : IAndroidApiTraceSink
{
    private readonly IAndroidApiTraceSink _first;
    private readonly IAndroidApiTraceSink _second;

    public CompositeAndroidApiTraceSink(IAndroidApiTraceSink first, IAndroidApiTraceSink second)
    {
        _first = first;
        _second = second;
    }

    public void Record(AndroidApiTraceEvent traceEvent)
    {
        try { _first.Record(traceEvent); } catch { }
        try { _second.Record(traceEvent); } catch { }
    }
}
