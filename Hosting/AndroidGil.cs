#nullable enable
namespace AndroidRuntime.Core.Hosting;

/// <summary>
/// Global Interpreter Lock (GIL): the single per-session lock that any real OS
/// thread must hold while executing guest bytecode (Execute and everything it
/// calls transitively — API bindings, EnsureClassInitialized, peer bodies).
/// This is a real, permanent architectural choice modeled on CPython's GIL:
/// real OS threads with real blocking semantics for sleep/join/monitor, at the
/// explicit, documented cost of NO parallel guest bytecode execution. That
/// tradeoff is accepted — this project targets compatibility, not throughput.
///
/// The lock is reentrant per thread (nested guest calls stay inside an
/// already-held GIL). Blocking operations (sleep, join, monitor-enter, the
/// class-initialization wait) must release the GIL before blocking and
/// reacquire afterwards, via <see cref="BeginBlocking"/> — otherwise the
/// blocked thread would deadlock every other thread.
/// </summary>
public sealed class AndroidGil
{
    private readonly object _gate = new();
    private int _ownerThreadId;
    private int _depth;

    /// <summary>Acquires the GIL (reentrant for the owning thread), blocking
    /// until it is free. Marks the beginning of guest execution on this thread.</summary>
    public void Enter()
    {
        int current = Environment.CurrentManagedThreadId;
        lock (_gate)
        {
            if (_ownerThreadId == current)
            {
                _depth++;
                return;
            }
            while (_ownerThreadId != 0)
                System.Threading.Monitor.Wait(_gate);
            _ownerThreadId = current;
            _depth = 1;
        }
    }

    /// <summary>Releases one hold of the GIL. Must be balanced with Enter.</summary>
    public void Exit()
    {
        lock (_gate)
        {
            if (_ownerThreadId != Environment.CurrentManagedThreadId || _depth == 0)
                throw new InvalidOperationException("GIL released by a thread that does not own it.");
            if (--_depth == 0)
            {
                _ownerThreadId = 0;
                System.Threading.Monitor.PulseAll(_gate);
            }
        }
    }

    /// <summary>Acquires the GIL for the duration of the returned scope
    /// (reentrant for the owning thread).</summary>
    public IDisposable Acquire()
    {
        Enter();
        return new GilHold(this);
    }

    /// <summary>
    /// Releases ALL of the current thread's GIL holds so a genuinely blocking
    /// operation can proceed (another thread needs the GIL to make progress),
    /// and re-acquires to the same depth when the returned scope is disposed.
    /// </summary>
    public GilBlockingScope BeginBlocking()
    {
        int depth;
        lock (_gate)
        {
            if (_ownerThreadId != Environment.CurrentManagedThreadId || _depth == 0)
                throw new InvalidOperationException("BeginBlocking requires the GIL to be held by the current thread.");
            depth = _depth;
            _depth = 0;
            _ownerThreadId = 0;
            System.Threading.Monitor.PulseAll(_gate);
        }
        return new GilBlockingScope(this, depth);
    }

    /// <summary>Disposable scope that restores the GIL depth after a blocking operation.</summary>
    public sealed class GilBlockingScope : IDisposable
    {
        private readonly AndroidGil _gil;
        private readonly int _depth;
        private int _disposed;

        internal GilBlockingScope(AndroidGil gil, int depth)
        {
            _gil = gil;
            _depth = depth;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _gil.Enter();
            if (_depth > 1)
            {
                lock (_gil._gate)
                {
                    // Enter acquired to depth 1; restore the original nesting depth.
                    _gil._depth = _depth;
                }
            }
        }
    }

    private sealed class GilHold : IDisposable
    {
        private readonly AndroidGil _gil;
        private int _disposed;
        internal GilHold(AndroidGil gil) { _gil = gil; }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _gil.Exit();
        }
    }
}
