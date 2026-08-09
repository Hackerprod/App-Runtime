using AndroidRuntime.WindowsHost;

namespace AndroidRuntime.WindowsHost.Tests;

/// <summary>
/// Phase-2 presentation-shim tests: the renderer no longer interprets display
/// lists (that moved to ViewRuntime); it holds a finished BGRA buffer and
/// presents/captures it. Scheduler revision logic is unchanged.
/// </summary>
public sealed class WindowsRetainedRendererTests
{
    [Fact]
    public void Coalescer_publishes_only_newest_revision_and_drops_stale_work()
    {
        var posted = new Queue<Action>();
        var rendered = new List<long>();
        using var scheduler = new RetainedFrameScheduler(action => posted.Enqueue(action), frame => rendered.Add(frame.Revision));

        scheduler.Publish(Frame(1));
        scheduler.Publish(Frame(2));
        scheduler.Publish(Frame(1));

        Assert.Single(posted);
        posted.Dequeue()();
        Assert.Equal([2L], rendered);
        Assert.Equal(1, scheduler.Metrics.StaleFramesDropped);
        Assert.Equal(1, scheduler.Metrics.CoalescedFrames);
    }

    [Fact]
    public void Software_capture_is_repeatable_and_reports_first_mismatch()
    {
        using var renderer = new WindowsRetainedRenderer();
        renderer.Resize(320, 240, 1f);
        renderer.Render(Frame(1, fill: 0x10));

        WindowsFrameCapture first = renderer.Capture();
        WindowsFrameCapture second = renderer.Capture();
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Null(first.FirstMismatch(second));

        renderer.Render(Frame(2, fill: 0x20));
        WindowsFrameCapture changed = renderer.Capture();
        Assert.NotNull(first.FirstMismatch(changed));
    }

    [Fact]
    public void Capture_without_frame_reports_empty_surface_not_stale_data()
    {
        using var renderer = new WindowsRetainedRenderer();
        renderer.Resize(64, 64, 1f);

        WindowsFrameCapture capture = renderer.Capture();
        Assert.Equal(0, capture.Revision);
        Assert.Equal(64, capture.Width);
        Assert.Equal(64, capture.Height);
        Assert.All(capture.Bgra, channel => Assert.Equal(0, channel));
    }

    private static WindowsRetainedFrame Frame(long revision, byte fill = 0) => new(
        revision,
        320,
        240,
        1,
        Enumerable.Repeat(fill, 320 * 240 * 4).ToArray(),
        string.Empty);
}
