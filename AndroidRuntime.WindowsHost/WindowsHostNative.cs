#nullable enable
using System.Runtime.InteropServices;

namespace AndroidRuntime.WindowsHost;

/// <summary>Small Win32 interop used by the launcher view (bringing the spawned
/// app window to the foreground — the app can open behind the launcher).</summary>
internal static class WindowsHostNative
{
    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(nint hWnd);
}
