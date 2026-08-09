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
    public void Vertical_layout_with_center_gravity_centers_children_horizontally()
    {
        var root = new AndroidLinearLayoutNode(1) { Orientation = AndroidOrientation.Vertical, Gravity = 0x11 };
        var child = new AndroidTextViewNode(2) { Text = "Centered", LayoutWidth = AndroidLayoutDimension.Exact(50) };
        root.Add(child);
        var renderer = new RecordingAndroidRenderBackend();
        using var host = new AndroidSceneHost(root, new DeterministicAndroidTextMeasurer(), renderer, new AndroidUiLimits());
        host.SetViewport(200, 200, 1f); host.Render();

        // 200 wide container, 50 wide child: centered horizontally at (200-50)/2.
        Assert.Equal(75f, child.Bounds.X);
        Assert.Equal(0f, child.Bounds.Y);
        Assert.Equal(50f, child.Bounds.Width);
    }

    [Fact]
    public void Center_gravity_text_offsets_draw_rect_inside_large_button_bounds()
    {
        var button = new AndroidButtonNode(1) { Text = "Connect", LayoutWidth = AndroidLayoutDimension.Exact(200), LayoutHeight = AndroidLayoutDimension.Exact(60) };
        var renderer = new RecordingAndroidRenderBackend();
        using var host = new AndroidSceneHost(button, new DeterministicAndroidTextMeasurer(), renderer, new AndroidUiLimits());
        host.SetViewport(200, 200, 1f); host.Render();

        AndroidDrawTextCommand text = Assert.Single(host.Render().DisplayList.Commands.OfType<AndroidDrawTextCommand>());
        // Button default gravity is CENTER (0x11): the text draw rect must start
        // inset from the button bounds (not at 0,0).
        Assert.True(text.Rect.X > 0, $"expected centered x > 0, got {text.Rect.X}");
        Assert.True(text.Rect.Y > 0, $"expected centered y > 0, got {text.Rect.Y}");
        Assert.True(text.Rect.X < 100, $"expected left half x, got {text.Rect.X}");
    }

    [Fact]
    public void View_depth_and_count_quotas_fail_closed()
    {
        var root = new AndroidLinearLayoutNode(1); root.Add(new AndroidTextViewNode(2));
        Assert.Throws<AndroidUiQuotaExceededException>(() => new AndroidSceneHost(root, new DeterministicAndroidTextMeasurer(), new RecordingAndroidRenderBackend(), new AndroidUiLimits(MaxViewCount: 1)));
    }
}
