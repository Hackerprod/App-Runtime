using AndroidRuntime.Core.Apk;
using AndroidRuntime.Core.Dex;
using AndroidRuntime.Core.Hosting;
using AndroidRuntime.Core.Ui;

namespace AndroidRuntime.Core.Tests;

public sealed class AndroidUiRuntimeIntegrationTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "UiProbe.apk");

    [Fact]
    public async Task Real_apk_inflates_retained_scene_and_dispatches_xml_click_once_on_android_lane()
    {
        LoadedApk apk = ApkLoader.Load(FixturePath);
        var resources = AndroidResourceResolver.Create(apk);
        int labelId = checked((int)resources.GetIdentifier("id", "label"));
        int actionId = checked((int)resources.GetIdentifier("id", "action"));
        var runtime = new AndroidAppRuntime();
        var services = AndroidRuntimeServices.CreateHeadless();
        var hosted = await runtime.LaunchSessionAsync(FixturePath, services);

        Assert.Equal(AndroidActivityState.Resumed, hosted.Session.State);
        Assert.Equal(actionId, hosted.Session.Activity.InstanceFields["observedId"]);
        Assert.Equal(1, hosted.Session.Activity.InstanceFields["stateRoundTrip"]);
        Assert.Equal("Ready", hosted.Session.Activity.InstanceFields["observedText"]);
        Assert.Equal(3, hosted.ViewPeerCount);
        DexObject first = await hosted.FindViewByIdAsync(labelId);
        DexObject second = await hosted.FindViewByIdAsync(labelId);
        Assert.Same(first, second);
        Assert.Equal("Landroid/widget/TextView;", first.TypeDescriptor);

        AndroidUiFrame initial = await hosted.RenderUiAsync(300, 200, 1f);
        string expectedInitial = $"0|AndroidLinearLayoutNode|0|||1|0|0,0,300,200{Environment.NewLine}" +
            $"1|AndroidTextViewNode|{labelId}|Ready||1|0|16,16,67.2,28.8{Environment.NewLine}" +
            $"1|AndroidButtonNode|{actionId}|Tap|Run probe|1|0|16,44.8,26.88,43.2{Environment.NewLine}";
        Assert.Equal(expectedInitial, initial.SemanticSnapshot);
        long treeBuilds = initial.Metrics.TreeBuilds;

        Assert.True(await hosted.PerformClickAsync(actionId));
        AndroidUiFrame clicked = await hosted.RenderUiAsync(300, 200, 1f);
        string expectedClicked = $"0|AndroidLinearLayoutNode|0|||1|0|0,0,300,200{Environment.NewLine}" +
            $"1|AndroidTextViewNode|{labelId}|Clicked||1|0|16,16,94.08,28.8{Environment.NewLine}" +
            $"1|AndroidButtonNode|{actionId}|Tap|Run probe|1|0|16,44.8,26.88,43.2{Environment.NewLine}";
        Assert.Equal(expectedClicked, clicked.SemanticSnapshot);
        Assert.Equal(treeBuilds, clicked.Metrics.TreeBuilds);
        Assert.Equal(1, clicked.Metrics.Callbacks);
        Assert.Equal(clicked.Metrics.ExecutionLaneThreadId, clicked.Metrics.LastCallbackThreadId);
        Assert.True(Assert.IsType<InMemoryActivityWindow>(hosted.Window).IsToastVisible);
        Assert.Equal("Clicked", Assert.IsType<InMemoryActivityWindow>(hosted.Window).ToastText);

        await hosted.DisposeAsync();
        Assert.Equal(0, hosted.ViewPeerCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => hosted.PerformClickAsync(actionId));
    }

    [Fact]
    public void Inflater_rejects_unknown_class_and_view_quotas_without_partial_peers()
    {
        LoadedApk apk = ApkLoader.Load(FixturePath);
        var resolver = AndroidResourceResolver.Create(apk);
        AndroidXmlDocument layout = resolver.LoadLayout("main");
        var inflater = new AndroidLayoutInflater(resolver, new AndroidUiLimits(MaxViewCount: 2));

        Assert.Throws<AndroidUiQuotaExceededException>(() => inflater.Inflate(layout));
        Assert.Equal(0, inflater.CreatedViewCount);

        var unknownRoot = new AndroidXmlElement(null, "WebView", 1, Array.Empty<AndroidXmlAttribute>());
        var unknown = new AndroidXmlDocument(unknownRoot, Array.Empty<AndroidXmlEvent>());
        Assert.StartsWith("UI_UNKNOWN_CLASS:", Assert.Throws<NotSupportedException>(() => inflater.Inflate(unknown)).Message, StringComparison.Ordinal);

        var malformedRoot = new AndroidXmlElement(null, "LinearLayout", 7, Array.Empty<AndroidXmlAttribute>());
        var malformed = new AndroidXmlDocument(malformedRoot, Array.Empty<AndroidXmlEvent>());
        Assert.Equal("UI_REQUIRED_ATTRIBUTE: layout_width", Assert.Throws<InvalidDataException>(() => inflater.Inflate(malformed)).Message);
    }

    [Fact]
    public async Task View_peer_quota_fails_closed_during_content_view_installation()
    {
        var services = new AndroidRuntimeServices(
            new InMemoryActivityWindowFactory(),
            new ConsoleAndroidLogSink(),
            peerLimits: new AndroidRuntime.Core.ApiLayer.AndroidPeerLimits(maxViews: 2));

        await Assert.ThrowsAsync<AndroidRuntime.Core.ApiLayer.AndroidPeerQuotaExceededException>(
            () => new AndroidAppRuntime().LaunchSessionAsync(FixturePath, services));
    }
}
