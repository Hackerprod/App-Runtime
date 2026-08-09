#nullable enable
using AndroidRuntime.Core.Apk;
using AndroidRuntime.Core.ApiLayer;
using AndroidRuntime.Core.Dex;

namespace AndroidRuntime.Core.Ui;

public readonly record struct AndroidUiMetrics(long TreeBuilds, long MeasureLayoutPasses, long DisplayListBuilds, long Callbacks, int ExecutionLaneThreadId, int LastCallbackThreadId);
public sealed record AndroidUiFrame(string SemanticSnapshot, AndroidDisplayList DisplayList, AndroidUiMetrics Metrics);

internal sealed class AndroidUiSession : IDisposable
{
    private readonly AndroidResourceResolver _resources;
    private readonly AndroidUiLimits _limits;
    private readonly int _peerLimit;
    private readonly IAndroidTextMeasurer? _textMeasurer;
    private readonly Dictionary<DexObject, AndroidViewNode> _guestToNode = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AndroidViewNode, DexObject> _nodeToGuest = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AndroidViewNode, DexObject?> _listeners = new(ReferenceEqualityComparer.Instance);
    private AndroidSceneHost? _scene;
    private DexObject? _activity;
    private DexInterpreter? _interpreter;
    private int _laneThreadId;
    private int _disposed;
    private Exception? _callbackFailure;
    private long _callbacks;
    private int _lastCallbackThreadId;

    internal AndroidUiSession(AndroidResourceResolver resources, AndroidUiLimits limits, int peerLimit, IAndroidTextMeasurer? textMeasurer = null)
    {
        _resources = resources;
        _limits = limits;
        _peerLimit = peerLimit;
        _textMeasurer = textMeasurer;
    }

    internal int PeerCount => _guestToNode.Count;

    internal void Attach(DexObject activity, DexInterpreter interpreter)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_activity is not null && !ReferenceEquals(_activity, activity)) throw new InvalidOperationException("UI session already has an Activity.");
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _interpreter = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
        _laneThreadId = Environment.CurrentManagedThreadId;
    }

    internal void SetContentView(int layoutResourceId)
    {
        RequireLane();
        AndroidXmlDocument document = _resources.LoadLayout(unchecked((uint)layoutResourceId));
        var inflater = new AndroidLayoutInflater(_resources, _limits);
        AndroidViewNode root = inflater.Inflate(document);
        if (inflater.CreatedViewCount > _peerLimit) throw new AndroidPeerQuotaExceededException("View", _peerLimit);
        var guestToNode = new Dictionary<DexObject, AndroidViewNode>(ReferenceEqualityComparer.Instance);
        var nodeToGuest = new Dictionary<AndroidViewNode, DexObject>(ReferenceEqualityComparer.Instance);
        var listeners = new Dictionary<AndroidViewNode, DexObject?>(ReferenceEqualityComparer.Instance);
        Walk(root);
        void Walk(AndroidViewNode node)
        {
            var guest = new DexObject(DescriptorFor(node));
            guestToNode.Add(guest, node); nodeToGuest.Add(node, guest); listeners.Add(node, null);
            foreach (AndroidViewNode child in node.Children) Walk(child);
        }
        // Layout measurement: use the host-injected ViewRuntime-backed measurer
        // when supplied (so layout and paint agree on real glyph widths),
        // falling back to the deterministic stub otherwise.
        var scene = new AndroidSceneHost(root, _textMeasurer ?? new DeterministicAndroidTextMeasurer(), new RecordingAndroidRenderBackend(), _limits);
        _scene?.Dispose();
        _guestToNode.Clear(); foreach (var pair in guestToNode) _guestToNode.Add(pair.Key, pair.Value);
        _nodeToGuest.Clear(); foreach (var pair in nodeToGuest) _nodeToGuest.Add(pair.Key, pair.Value);
        _listeners.Clear(); foreach (var pair in listeners) _listeners.Add(pair.Key, pair.Value);
        _scene = scene;
    }

    /// <summary>
    /// Inflates a layout resource into a DETACHED view tree (real
    /// LayoutInflater.inflate(resId, null, false) semantics): every node gets a
    /// registered guest View so findViewById/setText/etc. work on the returned
    /// root, but the tree is NOT installed as the content view and participates
    /// in no scene. Registration is additive (does not disturb an existing
    /// content view); a later SetContentView rebuilds the maps from the content
    /// tree, which real apps only do for window content.
    /// </summary>
    internal DexObject Inflate(int layoutResourceId)
    {
        RequireLane();
        AndroidXmlDocument document = _resources.LoadLayout(unchecked((uint)layoutResourceId));
        var inflater = new AndroidLayoutInflater(_resources, _limits);
        AndroidViewNode root = inflater.Inflate(document);
        if (inflater.CreatedViewCount > _peerLimit) throw new AndroidPeerQuotaExceededException("View", _peerLimit);
        var pending = new List<(DexObject Guest, AndroidViewNode Node)>();
        Walk(root);
        void Walk(AndroidViewNode node)
        {
            var guest = new DexObject(DescriptorFor(node));
            // Framework-owned view state the guest <init> would normally set: the
            // runtime inflates scaffold views natively (no guest constructor
            // runs), so eagerly initialize fields guest methods read.
            if (node.GuestDescriptor == "Landroidx/appcompat/widget/ContentFrameLayout;")
                guest.InstanceFields["Landroidx/appcompat/widget/ContentFrameLayout;->mDecorPadding:Landroid/graphics/Rect;"] = new DexObject("Landroid/graphics/Rect;");
            pending.Add((guest, node));
            foreach (AndroidViewNode child in node.Children) Walk(child);
        }
        foreach (var (guest, node) in pending)
        {
            _guestToNode.Add(guest, node);
            _nodeToGuest.Add(node, guest);
            _listeners.Add(node, null);
        }
        return pending[0].Guest;
    }

    internal DexObject? FindViewById(int id, DexObject? receiver = null)
    {
        RequireLane();
        if (id == 0) return null;
        // With a receiver, search from that view's node (works for trees
        // inflated via LayoutInflater.inflate even before any content view /
        // scene exists). Without a receiver, the content-view scene must exist.
        if (receiver is not null)
        {
            AndroidViewNode start = Node(receiver);
            AndroidViewNode? found = start.FindById(id);
            return found is null ? null : _nodeToGuest[found];
        }
        if (_scene is null) return null;
        AndroidViewNode? contentFound = _scene.Root.FindById(id);
        return contentFound is null ? null : _nodeToGuest[contentFound];
    }

    internal int GetId(DexObject guest) { RequireLane(); return Node(guest).ResourceId; }
    internal bool IsEnabled(DexObject guest) { RequireLane(); return Node(guest).Enabled; }
    internal void SetEnabled(DexObject guest, bool enabled) { RequireLane(); Node(guest).Enabled = enabled; }
    internal int GetVisibility(DexObject guest) { RequireLane(); return (int)Node(guest).Visibility; }
    internal void SetVisibility(DexObject guest, int visibility) { RequireLane(); Node(guest).Visibility = visibility switch { 0 => AndroidViewVisibility.Visible, 4 => AndroidViewVisibility.Invisible, 8 => AndroidViewVisibility.Gone, _ => throw new ArgumentOutOfRangeException(nameof(visibility), "Visibility must be VISIBLE (0), INVISIBLE (4), or GONE (8).") }; }
    internal string GetText(DexObject guest) { RequireLane(); return TextNode(guest).Text; }
    internal void SetText(DexObject guest, string? text) { RequireLane(); TextNode(guest).Text = text ?? string.Empty; }
    internal void SetOnClickListener(DexObject guest, DexObject? listener) { RequireLane(); _listeners[Node(guest)] = listener; }

    internal bool PerformClick(DexObject guest)
    {
        RequireLane();
        AndroidViewNode node = Node(guest);
        if (!node.Enabled || node.Visibility != AndroidViewVisibility.Visible) return false;
        DexObject? listener = _listeners[node];
        if (listener is null && string.IsNullOrWhiteSpace(node.XmlOnClick)) return false;
        try
        {
            _lastCallbackThreadId = Environment.CurrentManagedThreadId;
            Interlocked.Increment(ref _callbacks);
            if (listener is not null)
                _interpreter!.InvokeVirtualInstanceExact(listener, "onClick", "(Landroid/view/View;)V", guest);
            else
                _interpreter!.InvokePublicInstanceExact(_activity!, node.XmlOnClick!, "(Landroid/view/View;)V", guest);
            return true;
        }
        catch (Exception error)
        {
            _callbackFailure = error;
            throw;
        }
    }

    internal bool PerformClick(int id)
    {
        DexObject? guest = FindViewById(id);
        return guest is not null && PerformClick(guest);
    }

    internal AndroidUiFrame Render(int width, int height, float density)
    {
        RequireLane();
        AndroidSceneHost scene = _scene ?? throw new InvalidOperationException("Activity has no content view.");
        scene.SetViewport(width, height, density);
        AndroidFrameSnapshot frame = scene.Render();
        AndroidSceneMetrics metrics = scene.Metrics;
        return new AndroidUiFrame(frame.SemanticSnapshot, frame.DisplayList, new AndroidUiMetrics(metrics.TreeBuilds, metrics.MeasureLayoutPasses, metrics.DisplayListBuilds, Interlocked.Read(ref _callbacks), _laneThreadId, _lastCallbackThreadId));
    }

    private void RequireLane()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_callbackFailure is not null) throw new InvalidOperationException("UI callback session is faulted.", _callbackFailure);
        if (_interpreter is null || _activity is null) throw new InvalidOperationException("UI session is not attached.");
        if (Environment.CurrentManagedThreadId != _laneThreadId) throw new InvalidOperationException("Android UI access must execute on the session execution lane.");
    }

    private AndroidViewNode Node(DexObject guest) => _guestToNode.TryGetValue(guest, out AndroidViewNode? node) ? node : throw new InvalidOperationException("View receiver does not belong to this session.");
    private AndroidTextViewNode TextNode(DexObject guest) => Node(guest) as AndroidTextViewNode ?? throw new InvalidOperationException("View is not a TextView.");
    private static string DescriptorFor(AndroidViewNode node) => node.GuestDescriptor ?? node switch { AndroidButtonNode => "Landroid/widget/Button;", AndroidTextViewNode => "Landroid/widget/TextView;", AndroidImageViewNode => "Landroid/widget/ImageView;", AndroidLinearLayoutNode => "Landroid/widget/LinearLayout;", _ => "Landroid/view/View;" };

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _scene?.Dispose(); _scene = null;
        _listeners.Clear(); _nodeToGuest.Clear(); _guestToNode.Clear();
        _activity = null; _interpreter = null;
    }
}
