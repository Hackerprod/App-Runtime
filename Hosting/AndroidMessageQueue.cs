#nullable enable
using System.Collections.Concurrent;

namespace AndroidRuntime.Core.Hosting;

/// <summary>
/// Small shared "message queue pump" primitive behind android.os.Handler/Looper.
/// It deliberately owns NO thread: the caller decides who drains it. The hosted
/// main Looper bypasses this entirely and enqueues onto the execution lane's own
/// queue (the lane already is a message loop); a background Looper.loop() drains
/// its private queue on the calling guest thread; a standalone main Looper (tests)
/// drains on a dedicated background pump thread. Post is real: it returns
/// immediately and the action runs later on the drainer, never inline.
/// </summary>
internal sealed class AndroidMessageQueue
{
    private readonly BlockingCollection<Action> _queue = new();

    internal bool Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            _queue.Add(action);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false; // completed (quit)
        }
    }

    internal bool TryTake(out Action? action, int timeoutMilliseconds) =>
        _queue.TryTake(out action, timeoutMilliseconds);

    internal bool IsEmpty => _queue.Count == 0;

    /// <summary>Stops accepting new posts; the drainer's TryTake returns false
    /// once the queue is drained (or immediately if empty).</summary>
    internal void Quit()
    {
        try { _queue.CompleteAdding(); }
        catch (InvalidOperationException) { }
    }
}
