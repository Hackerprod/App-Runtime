#nullable enable
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace AndroidRuntime.Core.Ui;

[Flags]
public enum AndroidInvalidation { None = 0, Tree = 1, MeasureLayout = 2, DisplayList = 4, PaintChunks = 8, Scroll = 16 }
public enum AndroidMeasureMode { Unspecified, Exactly, AtMost }
public enum AndroidLayoutSize { MatchParent, WrapContent, Exact }
public enum AndroidOrientation { Horizontal, Vertical }
public enum AndroidViewVisibility { Visible = 0, Invisible = 4, Gone = 8 }
public readonly record struct AndroidLayoutDimension(AndroidLayoutSize Kind, float Value = 0)
{
    public static AndroidLayoutDimension MatchParent => new(AndroidLayoutSize.MatchParent);
    public static AndroidLayoutDimension WrapContent => new(AndroidLayoutSize.WrapContent);
    public static AndroidLayoutDimension Exact(float value) => new(AndroidLayoutSize.Exact, value);
}
public readonly record struct AndroidMeasureSpec(float Size, AndroidMeasureMode Mode)
{
    public static AndroidMeasureSpec Exactly(float size) => new(size, AndroidMeasureMode.Exactly);
    public static AndroidMeasureSpec AtMost(float size) => new(size, AndroidMeasureMode.AtMost);
}
public readonly record struct AndroidSize(float Width, float Height);
public readonly record struct AndroidRect(float X, float Y, float Width, float Height)
{
    public bool Contains(float x, float y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
}
public readonly record struct AndroidColor(byte A, byte R, byte G, byte B);
public sealed record AndroidUiLimits(int MaxViewCount = 4096, int MaxViewDepth = 64, int MaxDisplayCommands = 32768, int MaxInvalidationsPerFrame = 4096)
{
    internal void Validate() { if (MaxViewCount <= 0 || MaxViewDepth <= 0 || MaxDisplayCommands <= 0 || MaxInvalidationsPerFrame <= 0) throw new ArgumentOutOfRangeException(nameof(AndroidUiLimits)); }
}
public sealed class AndroidUiQuotaExceededException : InvalidOperationException { public AndroidUiQuotaExceededException(string message) : base(message) { } }

public abstract class AndroidViewNode
{
    private readonly List<AndroidViewNode> _children = [];
    private Action<AndroidInvalidation>? _invalidate;
    private bool _enabled = true;
    private AndroidViewVisibility _visibility;
    private AndroidColor? _backgroundColor;
    protected AndroidViewNode(int resourceId) => ResourceId = resourceId;
    public int ResourceId { get; }
    /// <summary>Additional resource ids this view answers to (real Android views
    /// can be looked up by several ids; appcompat's scaffold ContentFrameLayout
    /// is registered under both its own id and android.R.id.content).</summary>
    public List<int> AliasIds { get; } = [];
    public bool MatchesId(int id) => ResourceId == id || AliasIds.Contains(id);
    /// <summary>The guest descriptor this node is registered under (defaults to
    /// the canonical widget descriptor; appcompat scaffold classes like
    /// ContentFrameLayout register under their own class so casts/findViewById
    /// resolve).</summary>
    public string? GuestDescriptor { get; set; }
    public string? ContentDescription { get; set; }
    public string? XmlOnClick { get; set; }
    /// <summary>android:gravity bits (android.view.Gravity constants). The bounded
    /// subset honors CENTER_HORIZONTAL (0x01) and CENTER_VERTICAL (0x10): layout
    /// containers center children on the cross axis and text nodes center their
    /// text within their bounds.</summary>
    public int Gravity { get; set; }
    public AndroidLayoutDimension LayoutWidth { get; set; } = AndroidLayoutDimension.MatchParent;
    public AndroidLayoutDimension LayoutHeight { get; set; } = AndroidLayoutDimension.WrapContent;
    public AndroidRect Bounds { get; internal set; }
    public AndroidSize MeasuredSize { get; internal set; }
    public AndroidViewNode? Parent { get; private set; }
    public IReadOnlyList<AndroidViewNode> Children => _children;
    public bool Focusable { get; set; }
    public bool Enabled { get => _enabled; set { if (_enabled == value) return; _enabled = value; _invalidate?.Invoke(AndroidInvalidation.DisplayList); } }
    public AndroidViewVisibility Visibility { get => _visibility; set { if (value is not (AndroidViewVisibility.Visible or AndroidViewVisibility.Invisible or AndroidViewVisibility.Gone)) throw new ArgumentOutOfRangeException(nameof(value)); if (_visibility == value) return; _visibility = value; _invalidate?.Invoke(AndroidInvalidation.MeasureLayout | AndroidInvalidation.DisplayList); } }
    public AndroidColor? BackgroundColor { get => _backgroundColor; set { if (_backgroundColor == value) return; _backgroundColor = value; _invalidate?.Invoke(AndroidInvalidation.DisplayList); } }
    public void Add(AndroidViewNode child) { ArgumentNullException.ThrowIfNull(child); if (child.Parent is not null) throw new InvalidOperationException("View already has a parent."); child.Parent = this; _children.Add(child); if (_invalidate is not null) child.AttachInvalidator(_invalidate); _invalidate?.Invoke(AndroidInvalidation.Tree); }
    internal void AttachInvalidator(Action<AndroidInvalidation> invalidate) { _invalidate = invalidate; foreach (AndroidViewNode child in _children) child.AttachInvalidator(invalidate); }
    protected void Invalidate(AndroidInvalidation invalidation) => _invalidate?.Invoke(invalidation);
    internal abstract AndroidSize Measure(AndroidMeasureSpec width, AndroidMeasureSpec height, AndroidUiContext context);
    internal virtual void Layout(float x, float y, float width, float height, AndroidUiContext context) => Bounds = new(x, y, width, height);
    internal virtual void Record(List<AndroidDrawCommand> commands, AndroidUiContext context) { if (Visibility != AndroidViewVisibility.Visible) return; if (BackgroundColor is { } color) commands.Add(new AndroidFillRectCommand(Bounds, color, ResourceId)); foreach (AndroidViewNode child in _children) child.Record(commands, context); }
    public AndroidViewNode? FindById(int id) { if (MatchesId(id)) return this; foreach (AndroidViewNode child in _children) if (child.FindById(id) is { } found) return found; return null; }
}

public sealed class AndroidLinearLayoutNode : AndroidViewNode
{
    public AndroidLinearLayoutNode(int resourceId) : base(resourceId) { }
    public float PaddingDp { get; set; }
    public AndroidOrientation Orientation { get; set; } = AndroidOrientation.Horizontal;
    internal override AndroidSize Measure(AndroidMeasureSpec width, AndroidMeasureSpec height, AndroidUiContext context)
    {
        if (Visibility == AndroidViewVisibility.Gone) return MeasuredSize = default;
        float padding = PaddingDp * context.Density, contentWidth = Math.Max(0, width.Size - padding * 2), contentHeight = Math.Max(0, height.Size - padding * 2), primary = 0, cross = 0;
        foreach (AndroidViewNode child in Children)
        {
            if (child.Visibility == AndroidViewVisibility.Gone) continue;
            AndroidSize size = child.Measure(ChildSpec(child.LayoutWidth, contentWidth, context.Density), ChildSpec(child.LayoutHeight, contentHeight, context.Density), context);
            primary += Orientation == AndroidOrientation.Vertical ? size.Height : size.Width;
            cross = Math.Max(cross, Orientation == AndroidOrientation.Vertical ? size.Width : size.Height);
        }
        float desiredWidth = (Orientation == AndroidOrientation.Vertical ? cross : primary) + padding * 2;
        float desiredHeight = (Orientation == AndroidOrientation.Vertical ? primary : cross) + padding * 2;
        return MeasuredSize = new(ResolveSize(LayoutWidth, desiredWidth, width, context.Density), ResolveSize(LayoutHeight, desiredHeight, height, context.Density));
    }
    internal override void Layout(float x, float y, float width, float height, AndroidUiContext context)
    {
        base.Layout(x, y, width, height, context); float padding = PaddingDp * context.Density, left = x + padding, top = y + padding;
        float contentWidth = Math.Max(0, width - padding * 2), contentHeight = Math.Max(0, height - padding * 2);
        bool centerHorizontal = (Gravity & 0x01) != 0, centerVertical = (Gravity & 0x10) != 0;
        foreach (AndroidViewNode child in Children)
        {
            if (child.Visibility == AndroidViewVisibility.Gone) continue;
            float childX = left, childY = top;
            if (Orientation == AndroidOrientation.Vertical && centerHorizontal)
                childX = left + Math.Max(0, (contentWidth - child.MeasuredSize.Width) / 2);
            else if (Orientation == AndroidOrientation.Horizontal && centerVertical)
                childY = top + Math.Max(0, (contentHeight - child.MeasuredSize.Height) / 2);
            child.Layout(childX, childY, child.MeasuredSize.Width, child.MeasuredSize.Height, context);
            if (Orientation == AndroidOrientation.Vertical) top += child.MeasuredSize.Height; else left += child.MeasuredSize.Width;
        }
    }
    private static AndroidMeasureSpec ChildSpec(AndroidLayoutDimension dimension, float available, float density) => dimension.Kind switch { AndroidLayoutSize.MatchParent => AndroidMeasureSpec.Exactly(available), AndroidLayoutSize.Exact => AndroidMeasureSpec.Exactly(dimension.Value * density), _ => AndroidMeasureSpec.AtMost(available) };
    private static float ResolveSize(AndroidLayoutDimension dimension, float desired, AndroidMeasureSpec spec, float density) => dimension.Kind switch { AndroidLayoutSize.MatchParent => spec.Size, AndroidLayoutSize.Exact => Math.Min(spec.Size, dimension.Value * density), _ => spec.Mode == AndroidMeasureMode.Exactly ? spec.Size : Math.Min(spec.Size, desired) };
}

public class AndroidTextViewNode : AndroidViewNode
{
    private string _text = string.Empty;
    public AndroidTextViewNode(int resourceId) : base(resourceId) { }
    public string Text { get => _text; set { value ??= string.Empty; if (_text == value) return; _text = value; Invalidate(AndroidInvalidation.MeasureLayout | AndroidInvalidation.DisplayList); } }
    public float TextSizeSp { get; set; } = 16;
    public AndroidColor TextColor { get; set; } = new(255, 32, 32, 32);
    internal override AndroidSize Measure(AndroidMeasureSpec width, AndroidMeasureSpec height, AndroidUiContext context)
    {
        if (Visibility == AndroidViewVisibility.Gone) return MeasuredSize = default;
        AndroidTextMetrics metrics = context.TextMeasurer.Measure(Text, TextSizeSp * context.ScaledDensity, width.Size);
        float desiredWidth = metrics.Width, desiredHeight = metrics.Height;
        return MeasuredSize = new(LayoutWidth.Kind == AndroidLayoutSize.MatchParent || width.Mode == AndroidMeasureMode.Exactly ? width.Size : Math.Min(width.Size, desiredWidth), LayoutHeight.Kind == AndroidLayoutSize.Exact ? height.Size : Math.Min(height.Size, desiredHeight));
    }
    internal override void Record(List<AndroidDrawCommand> commands, AndroidUiContext context)
    {
        if (Visibility != AndroidViewVisibility.Visible) return;
        base.Record(commands, context);
        AndroidTextMetrics metrics = context.TextMeasurer.Measure(Text, TextSizeSp * context.ScaledDensity, Bounds.Width);
        float x = Bounds.X, y = Bounds.Y, width = Bounds.Width, height = Bounds.Height;
        if ((Gravity & 0x01) != 0 && Bounds.Width > metrics.Width)
            x += Math.Max(0, (Bounds.Width - metrics.Width) / 2);
        if ((Gravity & 0x10) != 0 && Bounds.Height > metrics.Height)
            y += Math.Max(0, (Bounds.Height - metrics.Height) / 2);
        commands.Add(new AndroidDrawTextCommand(new AndroidRect(x, y, Bounds.Width - (x - Bounds.X), Bounds.Height - (y - Bounds.Y)), Text, TextSizeSp * context.ScaledDensity, TextColor, ResourceId));
    }
}
public sealed class AndroidButtonNode : AndroidTextViewNode
{
    public AndroidButtonNode(int resourceId) : base(resourceId) { Focusable = true; Gravity = 0x11; BackgroundColor = new(255, 224, 224, 224); }
    internal override AndroidSize Measure(AndroidMeasureSpec width, AndroidMeasureSpec height, AndroidUiContext context) { AndroidSize text = base.Measure(width, height, context); return MeasuredSize = new(text.Width, Math.Min(height.Size, text.Height + 24 * context.Density)); }
}

/// <summary>
/// An android.widget.ImageView peer. The deliberately bounded platform subset
/// renders NO bitmap/drawable content (no image decoding pipeline exists): the
/// node renders only its background color (or nothing), inflates, measures, and
/// lays out like a real ImageView, and stays clickable/findable. The layout
/// inflater IGNORES the src/imageResource attributes rather than failing, so an
/// APK whose layout declares an ImageView still runs; the visual is a plain
/// box. This is a documented visual subset, not a crash surface.
/// </summary>
public sealed class AndroidImageViewNode : AndroidViewNode
{
    public AndroidImageViewNode(int resourceId) : base(resourceId) { }
    internal override AndroidSize Measure(AndroidMeasureSpec width, AndroidMeasureSpec height, AndroidUiContext context)
    {
        if (Visibility == AndroidViewVisibility.Gone) return MeasuredSize = default;
        float desiredWidth = 24 * context.Density, desiredHeight = 24 * context.Density;
        return MeasuredSize = new(
            LayoutWidth.Kind == AndroidLayoutSize.MatchParent || width.Mode == AndroidMeasureMode.Exactly ? width.Size : Math.Min(width.Size, desiredWidth),
            LayoutHeight.Kind == AndroidLayoutSize.MatchParent || height.Mode == AndroidMeasureMode.Exactly ? height.Size : Math.Min(height.Size, desiredHeight));
    }
}

public readonly record struct AndroidTextMetrics(float Width, float Height, float Baseline);
public interface IAndroidTextMeasurer { AndroidTextMetrics Measure(string text, float textSizePixels, float maxWidth); }
public sealed class DeterministicAndroidTextMeasurer : IAndroidTextMeasurer
{
    public AndroidTextMetrics Measure(string text, float size, float maxWidth) { float width = Math.Min(maxWidth, text.Sum(c => char.IsWhiteSpace(c) ? .33f : .56f) * size); return new(width, size * 1.2f, size * .8f); }
}
internal readonly record struct AndroidUiContext(float Density, float ScaledDensity, IAndroidTextMeasurer TextMeasurer);

public abstract record AndroidDrawCommand(int ResourceId);
public sealed record AndroidFillRectCommand(AndroidRect Rect, AndroidColor Color, int ViewId) : AndroidDrawCommand(ViewId);
public sealed record AndroidDrawTextCommand(AndroidRect Rect, string Text, float TextSizePixels, AndroidColor Color, int ViewId) : AndroidDrawCommand(ViewId);
public sealed class AndroidDisplayList
{
    internal AndroidDisplayList(IEnumerable<AndroidDrawCommand> commands) => Commands = new ReadOnlyCollection<AndroidDrawCommand>(commands.ToArray());
    public IReadOnlyList<AndroidDrawCommand> Commands { get; }
}
public interface IAndroidRenderBackend : IDisposable { void Resize(int pixelWidth, int pixelHeight, float density); void Render(AndroidDisplayList displayList); }
public interface IAndroidIncrementalRenderBackend { bool TryRenderIncremental(AndroidDisplayList displayList, AndroidInvalidation invalidation); }
public sealed class RecordingAndroidRenderBackend : IAndroidRenderBackend
{
    public List<AndroidDisplayList> Frames { get; } = []; public void Resize(int pixelWidth, int pixelHeight, float density) { } public void Render(AndroidDisplayList displayList) => Frames.Add(displayList); public void Dispose() { }
}
public readonly record struct AndroidSceneMetrics(long TreeBuilds, long MeasureLayoutPasses, long DisplayListBuilds, long PaintChunkBuilds, long RenderCalls, long StaleFramesDropped);
public sealed record AndroidFrameSnapshot(long Revision, AndroidDisplayList DisplayList, string SemanticSnapshot);

public sealed class AndroidSceneHost : IDisposable
{
    private readonly AndroidViewNode _root; private readonly IAndroidTextMeasurer _text; private readonly IAndroidRenderBackend _backend; private readonly AndroidUiLimits _limits;
    private AndroidInvalidation _dirty = AndroidInvalidation.Tree; private bool _framePending; private int _invalidations; private long _nextRevision, _latestBuilt; private AndroidDisplayList? _displayList; private string _semantics = string.Empty; private int _width = 1, _height = 1; private float _density = 1; private bool _disposed;
    private long _trees, _layouts, _lists, _chunks, _renders, _stale;
    public AndroidSceneHost(AndroidViewNode root, IAndroidTextMeasurer textMeasurer, IAndroidRenderBackend backend, AndroidUiLimits limits)
    { _root = root ?? throw new ArgumentNullException(nameof(root)); _text = textMeasurer ?? throw new ArgumentNullException(nameof(textMeasurer)); _backend = backend ?? throw new ArgumentNullException(nameof(backend)); _limits = limits ?? throw new ArgumentNullException(nameof(limits)); limits.Validate(); ValidateTree(); _root.AttachInvalidator(Invalidate); }
    public event EventHandler? FrameRequested;
    public AndroidSceneMetrics Metrics => new(_trees, _layouts, _lists, _chunks, _renders, _stale);
    public AndroidViewNode Root => _root;
    public void SetViewport(int width, int height, float density) { if (width <= 0 || height <= 0 || !float.IsFinite(density) || density <= 0) throw new ArgumentOutOfRangeException(nameof(width)); _width = width; _height = height; _density = density; _backend.Resize(width, height, density); Invalidate(AndroidInvalidation.MeasureLayout); }
    public void Invalidate(AndroidInvalidation invalidation) { ObjectDisposedException.ThrowIf(_disposed, this); if (invalidation == AndroidInvalidation.None) return; if (++_invalidations > _limits.MaxInvalidationsPerFrame) throw new AndroidUiQuotaExceededException("Invalidations per frame quota exceeded."); _dirty |= invalidation; if (_framePending) return; _framePending = true; FrameRequested?.Invoke(this, EventArgs.Empty); }
    public AndroidFrameSnapshot BuildFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this); var context = new AndroidUiContext(_density, _density, _text);
        if ((_dirty & AndroidInvalidation.Tree) != 0) { ValidateTree(); _trees++; }
        if ((_dirty & (AndroidInvalidation.Tree | AndroidInvalidation.MeasureLayout)) != 0) { _root.Measure(AndroidMeasureSpec.Exactly(_width / _density), AndroidMeasureSpec.Exactly(_height / _density), context); _root.Layout(0, 0, _root.MeasuredSize.Width, _root.MeasuredSize.Height, context); _layouts++; }
        if (_displayList is null || (_dirty & (AndroidInvalidation.Tree | AndroidInvalidation.MeasureLayout | AndroidInvalidation.DisplayList | AndroidInvalidation.PaintChunks)) != 0)
        { var commands = new List<AndroidDrawCommand>(); _root.Record(commands, context); if (commands.Count > _limits.MaxDisplayCommands) throw new AndroidUiQuotaExceededException("Display command quota exceeded."); _displayList = new(commands); _lists++; if ((_dirty & AndroidInvalidation.PaintChunks) != 0) _chunks++; }
        _semantics = SemanticSnapshot(); _dirty = AndroidInvalidation.None; _framePending = false; _invalidations = 0; _latestBuilt = ++_nextRevision; return new(_latestBuilt, _displayList, _semantics);
    }
    public bool Publish(AndroidFrameSnapshot frame) { if (frame.Revision != _latestBuilt) { _stale++; return false; } _backend.Render(frame.DisplayList); _renders++; return true; }
    public AndroidFrameSnapshot Render() { AndroidFrameSnapshot frame = BuildFrame(); Publish(frame); return frame; }
    public AndroidViewNode? HitTest(float x, float y) => Hit(_root, x, y);
    private static AndroidViewNode? Hit(AndroidViewNode node, float x, float y) { if (node.Visibility != AndroidViewVisibility.Visible || !node.Enabled || !node.Bounds.Contains(x, y)) return null; for (int i = node.Children.Count - 1; i >= 0; i--) if (Hit(node.Children[i], x, y) is { } hit) return hit; return node; }
    private void ValidateTree() { int count = 0; var seen = new HashSet<AndroidViewNode>(ReferenceEqualityComparer.Instance); Walk(_root, 1); void Walk(AndroidViewNode node, int depth) { if (!seen.Add(node)) throw new InvalidDataException("View tree contains a cycle."); if (++count > _limits.MaxViewCount || depth > _limits.MaxViewDepth) throw new AndroidUiQuotaExceededException("View tree quota exceeded."); foreach (AndroidViewNode child in node.Children) Walk(child, depth + 1); } }
    private string SemanticSnapshot() { var builder = new StringBuilder(); Walk(_root, 0); return builder.ToString(); void Walk(AndroidViewNode node, int depth) { builder.Append(depth).Append('|').Append(node.GetType().Name).Append('|').Append(node.ResourceId).Append('|').Append(node is AndroidTextViewNode text ? text.Text : null).Append('|').Append(node.ContentDescription).Append('|').Append(node.Enabled ? '1' : '0').Append('|').Append((int)node.Visibility).Append('|').Append(node.Bounds.X.ToString("0.###", CultureInfo.InvariantCulture)).Append(',').Append(node.Bounds.Y.ToString("0.###", CultureInfo.InvariantCulture)).Append(',').Append(node.Bounds.Width.ToString("0.###", CultureInfo.InvariantCulture)).Append(',').Append(node.Bounds.Height.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine(); foreach (AndroidViewNode child in node.Children) Walk(child, depth + 1); } }
    public void Dispose() { if (_disposed) return; _disposed = true; _backend.Dispose(); }
}
