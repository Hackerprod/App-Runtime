using AndroidRuntime.Core.Ui;

namespace AndroidRuntime.Core.Tests;

public sealed class AndroidSceneHostTests
{
    [Fact]
    public void Vertical_android_tree_measures_layouts_records_and_hit_tests_deterministically()
    {
        var root = new AndroidLinearLayoutNode(1) { PaddingDp = 24, Orientation = AndroidOrientation.Vertical };
        var text = new AndroidTextViewNode(2) { Text = "Ready", TextSizeSp = 20 };
        var button = new AndroidButtonNode(3) { Text = "Tap", ContentDescription = "Run probe" };
        root.Add(text); root.Add(button);
        var renderer = new RecordingAndroidRenderBackend();
        using var host = new AndroidSceneHost(root, new DeterministicAndroidTextMeasurer(), renderer, new AndroidUiLimits());
        host.SetViewport(300, 200, 1f); AndroidFrameSnapshot first = host.Render(); AndroidFrameSnapshot second = host.Render();

        Assert.Equal(first.SemanticSnapshot, second.SemanticSnapshot);
        Assert.Equal(new AndroidRect(24, 24, 252, 24), text.Bounds);
        Assert.Equal(3, host.HitTest(30, 60)!.ResourceId);
        Assert.Equal(1, host.Metrics.MeasureLayoutPasses);
        Assert.Equal(1, host.Metrics.DisplayListBuilds);
        Assert.Contains(first.DisplayList.Commands, command => command is AndroidDrawTextCommand { Text: "Ready" });
    }

    [Fact]
    public void Invalidation_is_coalesced_and_stale_frames_are_dropped()
    {
        var root = new AndroidLinearLayoutNode(1); root.Add(new AndroidTextViewNode(2) { Text = "A" });
        var renderer = new RecordingAndroidRenderBackend(); using var host = new AndroidSceneHost(root, new DeterministicAndroidTextMeasurer(), renderer, new AndroidUiLimits());
        int requested = 0; host.FrameRequested += (_, _) => requested++;
        host.Invalidate(AndroidInvalidation.PaintChunks); host.Invalidate(AndroidInvalidation.PaintChunks); Assert.Equal(1, requested);
        AndroidFrameSnapshot old = host.BuildFrame(); host.Invalidate(AndroidInvalidation.DisplayList); AndroidFrameSnapshot current = host.BuildFrame();
        Assert.False(host.Publish(old)); Assert.True(host.Publish(current)); Assert.Equal(1, host.Metrics.StaleFramesDropped);
    }

    [Fact]
    public void View_depth_and_count_quotas_fail_closed()
    {
        var root = new AndroidLinearLayoutNode(1); root.Add(new AndroidTextViewNode(2));
        Assert.Throws<AndroidUiQuotaExceededException>(() => new AndroidSceneHost(root, new DeterministicAndroidTextMeasurer(), new RecordingAndroidRenderBackend(), new AndroidUiLimits(MaxViewCount: 1)));
    }
}
