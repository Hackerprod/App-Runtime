#nullable enable
using System.Collections.Concurrent;

namespace AndroidRuntime.Core.Hosting;

internal sealed class AndroidExecutionLane : IAsyncDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private readonly object _disposeGate = new();
    private int _disposeState;
    private int _threadId;

    public AndroidExecutionLane(string sessionId)
    {
        Gil = new AndroidGil();
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "AndroidRuntime-" + sessionId
        };
        _thread.Start();
    }

    /// <summary>The session's Global Interpreter Lock: every real OS thread that
    /// executes guest bytecode must hold it. The lane's own thread is the main
    /// guest thread and runs guest code through it like any other.</summary>
    public AndroidGil Gil { get; }

    public bool IsCurrentThread => Environment.CurrentManagedThreadId == Volatile.Read(ref _threadId);

    public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Volatile.Read(ref _disposeState) != 0)
            return Task.FromException<T>(new ObjectDisposedException(nameof(AndroidExecutionLane)));
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _queue.Add(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    completion.TrySetResult(action());
                }
                catch (OperationCanceledException error)
                {
                    completion.TrySetCanceled(error.CancellationToken);
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            completion.TrySetCanceled(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            completion.TrySetException(new ObjectDisposedException(nameof(AndroidExecutionLane)));
        }
        return completion.Task;
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            if (_disposeState == 2)
                return ValueTask.CompletedTask;
            if (_disposeState == 0)
            {
                Volatile.Write(ref _disposeState, 1);
                _queue.CompleteAdding();
            }
            if (!IsCurrentThread && !_thread.Join(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Android execution lane did not stop within five seconds; disposal may be retried.");
            _queue.Dispose();
            Volatile.Write(ref _disposeState, 2);
        }
        return ValueTask.CompletedTask;
    }

    private void Run()
    {
        Volatile.Write(ref _threadId, Environment.CurrentManagedThreadId);
        foreach (var action in _queue.GetConsumingEnumerable())
            action();
    }
}
