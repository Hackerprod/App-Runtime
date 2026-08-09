using AndroidRuntime.Core.Ui;
using AndroidRuntime.WindowsHost;

namespace AndroidRuntime.WindowsHost.Tests;

public sealed class WindowsRetainedRendererTests
{
    [Fact]
    public void Coalescer_publishes_only_newest_revision_and_drops_stale_work()
    {
        var posted = new Queue<Action>();
        var rendered = new List<long>();
        using var scheduler = new RetainedFrameScheduler(action => posted.Enqueue(action), frame => rendered.Add(frame.Revision));

        scheduler.Publish(Frame(1, "one"));
        scheduler.Publish(Frame(2, "two"));
        scheduler.Publish(Frame(1, "stale"));

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
        renderer.Render(Frame(1, "Ready"));

        WindowsFrameCapture first = renderer.Capture();
        WindowsFrameCapture second = renderer.Capture();
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Null(first.FirstMismatch(second));

        renderer.Render(Frame(2, "Clicked"));
        WindowsFrameCapture changed = renderer.Capture();
        Assert.NotNull(first.FirstMismatch(changed));
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    public void Hit_test_uses_logical_coordinates_at_supported_dpi(float density)
    {
        using var renderer = new WindowsRetainedRenderer();
        renderer.Resize((int)(320 * density), (int)(240 * density), density);
        renderer.Render(Frame(1, "Ready"));

        Assert.Equal(42, renderer.HitTest(50 * density, 45 * density));
        Assert.Null(renderer.HitTest(5 * density, 5 * density));
    }

    private static WindowsRetainedFrame Frame(long revision, string text) => new(
        revision,
        320,
        240,
        1,
        [
            new AndroidFillRectCommand(new AndroidRect(20, 20, 100, 50), new AndroidColor(255, 35, 91, 180), 42),
            new AndroidDrawTextCommand(new AndroidRect(20, 20, 100, 50), text, 18, new AndroidColor(255, 255, 255, 255), 42)
        ],
        $"button|42|{text}");
}
