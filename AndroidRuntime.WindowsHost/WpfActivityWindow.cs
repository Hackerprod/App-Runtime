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
    private readonly TimeSpan _shutdownTimeout;
    private readonly object _disposeGate = new();
    private int _shutdownRequested;
    private int _disposed;

    public WpfActivityWindowFactory() : this(TimeSpan.FromSeconds(5)) { }

    internal WpfActivityWindowFactory(TimeSpan shutdownTimeout)
    {
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
                // FIXED-SIZE per the selected device preset (docs\installer-
                // launcher-design.md: "fixed-size-per-preset-only"): dragging the
                // edges would change the resolution the guest reports, so the
                // app window is NOT user-resizable.
                Width = 360,
                Height = 732,
                ResizeMode = ResizeMode.NoResize,
                Title = packageName,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = root
            };
            return (IActivityWindow)new WpfActivityWindow(window, root);
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

public sealed class WpfActivityWindow : IActivityWindow, IDeferredActivityWindowClose, IAndroidUiSurfaceHost
{
    private readonly Window _window;
    private readonly Dispatcher _dispatcher;
    private readonly nint _handle;
    private readonly AndroidHwndRenderSurface _surface;
    private string _title;
    private int _closed;
    private int _allowClose;
    private int _closeRequested;

    internal WpfActivityWindow(Window window, Grid root)
    {
        _window = window;
        _dispatcher = window.Dispatcher;
        _title = window.Title;
        _surface = new AndroidHwndRenderSurface();
        root.Children.Add(_surface);
        _handle = new WindowInteropHelper(window).EnsureHandle();
        window.Closed += (_, _) => MarkClosed();
        window.Closing += OnClosing;
    }

    public event EventHandler? Closed;
    public event EventHandler? CloseRequested;
    public nint Handle => _handle;
    public string Title => Volatile.Read(ref _title);
    public bool IsClosed => Volatile.Read(ref _closed) != 0;
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
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
