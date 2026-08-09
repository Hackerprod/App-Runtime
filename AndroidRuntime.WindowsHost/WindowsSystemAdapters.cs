#nullable enable
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.WindowsHost;

public interface IWindowsClipboardBackend
{
    void SetText(string text);
    string? GetText();
    void Clear();
}

public sealed class WindowsClipboardAdapter : IAndroidClipboard, IDisposable
{
    private readonly IWindowsClipboardBackend _backend;
    private readonly Func<bool> _focus;
    private readonly Thread _thread;
    private readonly TaskCompletionSource<Dispatcher> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;
    private int _shutdownRequested;
    public WindowsClipboardAdapter(IWindowsClipboardBackend? backend = null, Func<bool>? focus = null)
    {
        _backend = backend ?? new SystemClipboardBackend(); _focus = focus ?? IsCurrentProcessForeground;
        _thread = new Thread(() => { Dispatcher dispatcher = Dispatcher.CurrentDispatcher; _ready.SetResult(dispatcher); Dispatcher.Run(); }) { IsBackground = true, Name = "Android clipboard STA" };
        _thread.SetApartmentState(ApartmentState.STA); _thread.Start();
    }
    public bool IsAvailable => Volatile.Read(ref _shutdownRequested) == 0 && Volatile.Read(ref _disposed) == 0;
    internal bool IsDispatcherThreadAlive => _thread.IsAlive;
    public bool IsReadFocused => IsAvailable && _focus();
    public ValueTask SetTextAsync(string text, CancellationToken token) => new(InvokeWithRetriesAsync(() => _backend.SetText(text), token));
    public async ValueTask<string?> GetTextAsync(CancellationToken token)
    {
        if (!IsReadFocused) return null;
        return await InvokeWithRetriesAsync(() => _focus() ? _backend.GetText() : null, token).ConfigureAwait(false);
    }
    public ValueTask ClearAsync(CancellationToken token) => new(InvokeWithRetriesAsync(_backend.Clear, token));
    private async Task InvokeWithRetriesAsync(Action action, CancellationToken token) => await InvokeWithRetriesAsync<object?>(() => { action(); return null; }, token).ConfigureAwait(false);
    private async Task<T> InvokeWithRetriesAsync<T>(Func<T> action, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(2));
        for (int attempt = 0; ; attempt++)
        {
            deadline.Token.ThrowIfCancellationRequested();
            try { Dispatcher dispatcher = await _ready.Task.WaitAsync(deadline.Token).ConfigureAwait(false); return await dispatcher.InvokeAsync(action, DispatcherPriority.Send, deadline.Token).Task.ConfigureAwait(false); }
            catch (Exception error) when (attempt < 2 && error is COMException or ExternalException) { await Task.Delay(25 * (attempt + 1), deadline.Token).ConfigureAwait(false); }
        }
    }
    public void Dispose()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Interlocked.Exchange(ref _shutdownRequested, 1);
        if (_thread.IsAlive)
        {
            if (!_ready.Task.Wait(TimeSpan.FromSeconds(2))) throw new TimeoutException("Clipboard dispatcher did not initialize before shutdown.");
            _ready.Task.Result.BeginInvokeShutdown(DispatcherPriority.Send);
            if (!_thread.Join(TimeSpan.FromSeconds(2))) throw new TimeoutException("Clipboard dispatcher did not terminate before the shutdown timeout.");
        }
        Volatile.Write(ref _disposed, 1);
    }
    private static bool IsCurrentProcessForeground()
    {
        nint window = GetForegroundWindow();
        if (window == 0) return false;
        GetWindowThreadProcessId(window, out uint processId);
        return processId == (uint)Environment.ProcessId;
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    private sealed class SystemClipboardBackend : IWindowsClipboardBackend
    {
        public void SetText(string text) => Clipboard.SetDataObject(text, true);
        public string? GetText() => Clipboard.ContainsText() ? Clipboard.GetText() : null;
        public void Clear() => Clipboard.Clear();
    }
}

public sealed class WindowsConnectivityAdapter : IAndroidConnectivity
{
    private readonly object _gate = new(); private AndroidConnectivitySnapshot? _last; private long _revision;
    public bool IsAvailable => true;
    public AndroidConnectivitySnapshot GetSnapshot(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); bool online = NetworkInterface.GetIsNetworkAvailable(); AndroidNetworkTransport transports = AndroidNetworkTransport.None;
        if (online)
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                transports |= nic.NetworkInterfaceType switch { NetworkInterfaceType.Wireless80211 => AndroidNetworkTransport.Wifi, NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet => AndroidNetworkTransport.Ethernet, NetworkInterfaceType.Ppp => AndroidNetworkTransport.Vpn, _ => AndroidNetworkTransport.None };
            }
        }
        lock (_gate)
        {
            bool changed = _last is null || _last.Online != online || _last.Transports != transports;
            if (changed) { _revision++; _last = new(_revision, DateTimeOffset.UtcNow, Guid.NewGuid(), online, false, null, true, !transports.HasFlag(AndroidNetworkTransport.Vpn), true, transports); }
            return _last! with { Timestamp = DateTimeOffset.UtcNow };
        }
    }
}

public sealed class WindowsPowerAdapter : IAndroidPower
{
    public bool IsAvailable => OperatingSystem.IsWindows();

    public HostPowerSnapshot GetSnapshot(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!GetSystemPowerStatus(out SystemPowerStatus status))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "GetSystemPowerStatus failed.");
        bool hasBattery = status.BatteryFlag != 255 && (status.BatteryFlag & 128) == 0;
        bool? charging = !hasBattery || status.BatteryFlag == 255 ? null : (status.BatteryFlag & 8) != 0 || status.ACLineStatus == 1;
        int? capacity = hasBattery && status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : null;
        return new(hasBattery, charging, capacity, null, null, null, null, null, status.SystemStatusFlag == 1);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        internal byte ACLineStatus;
        internal byte BatteryFlag;
        internal byte BatteryLifePercent;
        internal byte SystemStatusFlag;
        internal uint BatteryLifeTime;
        internal uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);
}
