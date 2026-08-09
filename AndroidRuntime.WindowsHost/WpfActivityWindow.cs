#nullable enable
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.ComponentModel;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.WindowsHost;

public sealed class WpfActivityWindowFactory : IActivityWindowFactory, IDisposable
{
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly Thread _thread;
    private Dispatcher? _dispatcher;
    private readonly AndroidToastLimits _toastLimits;
    private readonly TimeSpan _shutdownTimeout;
    private readonly object _disposeGate = new();
    private int _shutdownRequested;
    private int _disposed;

    public WpfActivityWindowFactory(AndroidToastLimits? toastLimits = null) : this(toastLimits, TimeSpan.FromSeconds(5)) { }

    internal WpfActivityWindowFactory(AndroidToastLimits? toastLimits, TimeSpan shutdownTimeout)
    {
        _toastLimits = toastLimits ?? AndroidToastLimits.Default;
        if (shutdownTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(shutdownTimeout));
        _shutdownTimeout = shutdownTimeout;
        _thread = new Thread(RunDispatcher)
        {
            IsBackground = true,
            Name = "AndroidRuntime-WPF"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("WPF dispatcher did not start within five seconds.");
    }

    public bool IsDispatcherThreadAlive => _thread.IsAlive;

    public IActivityWindow Create(
        string sessionId,
        string packageName,
        string activityDescriptor,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dispatcher = RequireDispatcher();
        return dispatcher.Invoke(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = new Grid();
            var window = new Window
            {
                // Phone-shaped portrait window matching the reference device's
                // 1080x2196 display (aspect 0.4918 = 360x732 DIP at 3x density).
                Width = 360,
                Height = 732,
                Title = packageName,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = root
            };
            return (IActivityWindow)new WpfActivityWindow(window, root, _toastLimits);
        });
    }

    public void Dispose()
    {
        lock (_disposeGate)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            Interlocked.Exchange(ref _shutdownRequested, 1);
            var dispatcher = _dispatcher;
            if (dispatcher is not null && !dispatcher.HasShutdownStarted)
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            if (!_thread.Join(_shutdownTimeout))
                throw new TimeoutException("WPF dispatcher did not stop within the configured timeout.");
            _ready.Dispose();
            Volatile.Write(ref _disposed, 1);
        }
    }

    internal void BlockDispatcherForTest(ManualResetEventSlim entered, ManualResetEventSlim release) =>
        RequireDispatcher().BeginInvoke(() => { entered.Set(); release.Wait(); }, DispatcherPriority.Send);

    private Dispatcher RequireDispatcher()
    {
        if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _shutdownRequested) != 0 || _dispatcher is null ||
            _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
            throw new InvalidOperationException("WPF dispatcher is shutting down.");
        return _dispatcher;
    }

    private void RunDispatcher()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _ready.Set();
        Dispatcher.Run();
    }
}

public sealed class WpfActivityWindow : IActivityWindow, IAndroidToastHost, IDeferredActivityWindowClose, IAndroidUiSurfaceHost
{
    private readonly Window _window;
    private readonly Dispatcher _dispatcher;
    private readonly nint _handle;
    private readonly Border _toastOverlay;
    private readonly TextBlock _toastText;
    private readonly AndroidHwndRenderSurface _surface;
    private readonly Queue<WpfToastNotification> _toastQueue = new();
    private WpfToastNotification? _activeToast;
    private DispatcherTimer? _toastTimer;
    private long _toastGeneration;
    private readonly AndroidToastLimits _toastLimits;
    private readonly Queue<DateTime> _toastShowTimes = new();
    private string _title;
    private int _closed;
    private int _allowClose;
    private int _closeRequested;

    internal WpfActivityWindow(Window window, Grid root, AndroidToastLimits toastLimits)
    {
        _window = window;
        _dispatcher = window.Dispatcher;
        _title = window.Title;
        _toastLimits = toastLimits;
        _surface = new AndroidHwndRenderSurface();
        root.Children.Add(_surface);
        _toastText = new TextBlock { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxWidth = 600 };
        _toastOverlay = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(24),
            Opacity = 0.01,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Visibility = Visibility.Collapsed,
            Child = _toastText
        };
        AutomationProperties.SetLiveSetting(_toastOverlay, AutomationLiveSetting.Polite);
        // Pixels are composited by the retained child HWND. This zero-opacity WPF element is an
        // automation-only live-region mirror; it does not participate in visual composition.
        root.Children.Add(_toastOverlay);
        _handle = new WindowInteropHelper(window).EnsureHandle();
        window.Closed += (_, _) => MarkClosed();
        window.Closing += OnClosing;
    }

    public event EventHandler? Closed;
    public event EventHandler? CloseRequested;
    public nint Handle => _handle;
    public string Title => Volatile.Read(ref _title);
    public bool IsClosed => Volatile.Read(ref _closed) != 0;
    public bool IsToastVisible => _dispatcher.Invoke(() => _activeToast?.IsVisible == true);
    public string? ToastText => _dispatcher.Invoke(() => _activeToast?.IsVisible == true ? _activeToast.Text : null);
    internal nint SurfaceHandle => _dispatcher.Invoke(() => _surface.SurfaceHandle);
    internal WindowsFrameCapture CaptureSurface() => _dispatcher.Invoke(_surface.Capture);
    internal RetainedFrameSchedulerMetrics SurfaceMetrics => _dispatcher.Invoke(() => _surface.SchedulerMetrics);
    internal int? HitTestSurface(float x, float y) => _dispatcher.Invoke(() => _surface.HitTest(x, y));
    internal void InjectPointerClick(float x, float y) => _dispatcher.Invoke(() => _surface.InjectPointerClick(x, y));

    public void Attach(AndroidHostedActivitySession session) => _dispatcher.Invoke(() => _surface.Attach(session));
    public void Detach(AndroidHostedActivitySession session)
    {
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
        _dispatcher.Invoke(() => _surface.Detach(session));
    }
    public long DroppedToastCount { get; private set; }

    public void SetTitle(string? title, CancellationToken cancellationToken) =>
        InvokeWindow(() =>
        {
            string resolved = title ?? string.Empty;
            _window.Title = resolved;
            Volatile.Write(ref _title, resolved);
        }, cancellationToken);

    public void Show(CancellationToken cancellationToken) =>
        InvokeWindow(_window.Show, cancellationToken);

    public void Close()
    {
        if (IsClosed) return;
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            MarkClosed();
            return;
        }
        _dispatcher.Invoke(() =>
        {
            if (!IsClosed) { Volatile.Write(ref _allowClose, 1); _window.Close(); }
        });
    }

    public void Dispose() => Close();

    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (Volatile.Read(ref _allowClose) != 0) return;
        args.Cancel = true;
        if (Interlocked.Exchange(ref _closeRequested, 1) == 0) CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public IAndroidToastNotification CreateToast(string text, int duration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (text.Length > _toastLimits.MaxTextLength) throw new ArgumentOutOfRangeException(nameof(text));
        if (duration is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(duration));
        return new WpfToastNotification(this, text, duration);
    }

    private void ShowToast(WpfToastNotification toast, CancellationToken cancellationToken, bool countAcceptance = true) => InvokeWindow(() =>
    {
        bool enqueue = _activeToast is not null && !ReferenceEquals(_activeToast, toast);
        if (enqueue && _toastQueue.Contains(toast)) return;
        if (enqueue && _toastQueue.Count >= _toastLimits.QueueCapacity) { DroppedToastCount++; return; }
        if (countAcceptance)
        {
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-1);
            while (_toastShowTimes.Count > 0 && _toastShowTimes.Peek() < cutoff) _toastShowTimes.Dequeue();
            if (_toastShowTimes.Count >= _toastLimits.MaxShowsPerMinute) { DroppedToastCount++; return; }
            _toastShowTimes.Enqueue(DateTime.UtcNow);
        }
        if (enqueue)
        {
            _toastQueue.Enqueue(toast);
            return;
        }
        _activeToast = toast;
        toast.MarkVisible(true);
        _toastText.Text = toast.Text;
        _surface.SetToast(toast.Text);
        _toastOverlay.Visibility = Visibility.Visible;
        UIElementAutomationPeer.CreatePeerForElement(_toastOverlay)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        long generation = ++_toastGeneration;
        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(toast.Duration == 0 ? _toastLimits.ShortDurationMilliseconds : _toastLimits.LongDurationMilliseconds) };
        _toastTimer.Tick += (_, _) => { if (generation == _toastGeneration) CancelToast(toast); };
        _toastTimer.Start();
    }, cancellationToken);

    private void CancelToast(WpfToastNotification toast)
    {
        if (_dispatcher.CheckAccess()) CancelToastOnDispatcher(toast);
        else if (!_dispatcher.HasShutdownStarted) _dispatcher.Invoke(() => CancelToastOnDispatcher(toast));
    }

    private void UpdateToast(WpfToastNotification toast)
    {
        if (_dispatcher.CheckAccess()) { if (ReferenceEquals(_activeToast, toast)) { _toastText.Text = toast.Text; _surface.SetToast(toast.Text); } }
        else if (!_dispatcher.HasShutdownStarted) _dispatcher.Invoke(() => { if (ReferenceEquals(_activeToast, toast)) { _toastText.Text = toast.Text; _surface.SetToast(toast.Text); } });
    }

    private void CancelToastOnDispatcher(WpfToastNotification toast)
    {
        if (!ReferenceEquals(_activeToast, toast))
        {
            if (_toastQueue.Contains(toast))
            {
                var retained = _toastQueue.Where(item => !ReferenceEquals(item, toast)).ToArray();
                _toastQueue.Clear();
                foreach (var item in retained) _toastQueue.Enqueue(item);
            }
            toast.MarkVisible(false);
            return;
        }
        _toastTimer?.Stop();
        _toastTimer = null;
        toast.MarkVisible(false);
        _activeToast = null;
        _surface.SetToast(null);
        _toastOverlay.Visibility = Visibility.Collapsed;
        if (_toastQueue.Count > 0) ShowToast(_toastQueue.Dequeue(), CancellationToken.None, countAcceptance: false);
    }

    private sealed class WpfToastNotification : IAndroidToastNotification
    {
        private readonly WpfActivityWindow _owner;
        private string _text;
        private int _disposed;
        internal WpfToastNotification(WpfActivityWindow owner, string text, int duration) { _owner = owner; _text = text; Duration = duration; }
        public bool IsVisible { get; private set; }
        public int Duration { get; set; }
        public string Text { get => _text; set { _text = value; _owner.UpdateToast(this); } }
        public void Show(CancellationToken cancellationToken) { if (_disposed != 0) throw new ObjectDisposedException(nameof(WpfToastNotification)); _owner.ShowToast(this, cancellationToken); }
        public void Cancel() => _owner.CancelToast(this);
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) Cancel(); }
        internal void MarkVisible(bool visible) => IsVisible = visible;
    }

    private void InvokeWindow(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsClosed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
            throw new InvalidOperationException("WPF dispatcher is shutting down or the window is closed.");
        _dispatcher.Invoke(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsClosed) throw new InvalidOperationException("WPF window is closed.");
            action();
        });
    }

    private void MarkClosed()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _toastTimer?.Stop();
        _activeToast?.MarkVisible(false);
        _toastQueue.Clear();
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
