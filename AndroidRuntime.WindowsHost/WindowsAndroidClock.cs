using System.Diagnostics;
using System.Runtime.InteropServices;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.WindowsHost;

public sealed class WindowsAndroidClock : IAndroidClock
{
    private readonly Func<(bool Success, ulong Ticks100ns)> _uptimeSource;
    private readonly Func<ulong> _elapsedSource;
    private readonly Func<long> _nanosSource;
    private long _lastUptime, _lastElapsed, _lastNanos;

    public WindowsAndroidClock()
        : this(ReadUnbiasedInterruptTime, GetTickCount64, ReadStopwatchNanos) { }

    public WindowsAndroidClock(
        Func<(bool Success, ulong Ticks100ns)> uptimeSource,
        Func<ulong> elapsedSource,
        Func<long> nanosSource)
    {
        _uptimeSource = uptimeSource ?? throw new ArgumentNullException(nameof(uptimeSource));
        _elapsedSource = elapsedSource ?? throw new ArgumentNullException(nameof(elapsedSource));
        _nanosSource = nanosSource ?? throw new ArgumentNullException(nameof(nanosSource));
    }

    public long UptimeMillis()
    {
        (bool success, ulong ticks100ns) = _uptimeSource();
        if (!success)
            throw new InvalidOperationException("Windows unbiased interrupt time is unavailable.");
        return Monotonic(ref _lastUptime, checked((long)(ticks100ns / 10_000)));
    }
    public long ElapsedRealtime() => Monotonic(ref _lastElapsed, checked((long)_elapsedSource()));
    public long ElapsedRealtimeNanos() => Monotonic(ref _lastNanos, _nanosSource());

    private static (bool Success, ulong Ticks100ns) ReadUnbiasedInterruptTime()
    {
        try
        {
            if (QueryUnbiasedInterruptTimePrecise(out ulong precise)) return (true, precise);
        }
        catch (EntryPointNotFoundException) { }
        catch (DllNotFoundException) { }

        return (QueryUnbiasedInterruptTime(out ulong fallback), fallback);
    }

    private static long ReadStopwatchNanos() => checked((long)((Int128)Stopwatch.GetTimestamp() * 1_000_000_000 / Stopwatch.Frequency));
    private static long Monotonic(ref long location, long value)
    {
        while (true) { long prior = Volatile.Read(ref location); long next = Math.Max(prior, value); if (Interlocked.CompareExchange(ref location, next, prior) == prior) return next; }
    }
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryUnbiasedInterruptTimePrecise(out ulong unbiasedTime);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryUnbiasedInterruptTime(out ulong unbiasedTime);
    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();
}
