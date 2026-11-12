using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace UsageViewer;

internal static class Native
{
    private const uint NoActivate = 0x0010;
    private const uint NoZOrder = 0x0004;
    private const uint NoSize = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    internal struct ScreenRect
    {
        public int Left, Top, Right, Bottom;

        public readonly bool Contains(int x, int y) =>
            x >= Left && x < Right && y >= Top && y < Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public ScreenRect Monitor, Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref ScreenRect rect, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hwnd, out ScreenRect rect);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    internal static void PlacePanel(Window window, ScreenRect? anchor)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        ScreenRect icon = anchor.GetValueOrDefault();
        IntPtr target = anchor.HasValue ? MonitorFromRect(ref icon, 2) : MonitorFromWindow(IntPtr.Zero, 1);
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect) ||
            !GetMonitorInfo(target, ref monitor))
            return;

        int gap = (int)Math.Round(8 * GetDpiForWindow(hwnd) / 96.0);
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        int right = anchor.HasValue ? icon.Right : monitor.Work.Right - gap;
        int y = anchor.HasValue ? icon.Top - height - gap : monitor.Work.Bottom - height - gap;
        if (anchor.HasValue && y < monitor.Work.Top) y = icon.Bottom + gap;
        int x = Math.Clamp(right - width, monitor.Work.Left, Math.Max(monitor.Work.Left, monitor.Work.Right - width));
        y = Math.Clamp(y, monitor.Work.Top, Math.Max(monitor.Work.Top, monitor.Work.Bottom - height));
        if (rect.Left == x && rect.Top == y) return;
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, NoActivate | NoZOrder | NoSize);
    }
}
