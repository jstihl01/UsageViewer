using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace UsageViewer;

internal sealed class TrayIndicator : IDisposable
{
    private const uint CallbackMessage = 0x8001;
    private const uint IconFlags = 0x0001 | 0x0002 | 0x0004 | 0x0020 | 0x0080;
    private static readonly Guid IconId = new("F4E7BD90-A53E-45A9-91A9-77291E4052DC");
    private readonly IntPtr owner;
    private readonly Action<bool> select;
    private readonly Action hide;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private Drawing.Icon? image;
    private (Cycle? Short, int Step, int Size)? displayed;
    private string tooltip = "Usage Viewer";
    private bool registered;
    private bool disposed;

    public TrayIndicator(IntPtr owner, Action<bool> select, Action open, Action hide, Action exit)
    {
        this.owner = owner;
        this.select = select;
        this.hide = hide;
        menu.Items.Add("Abrir Usage Viewer", null, (_, _) => open());
        menu.Items.Add("Salir", null, (_, _) => exit());
        menu.Closed += (_, _) => PostMessage(owner, 0, IntPtr.Zero, IntPtr.Zero);
    }

    public void Update(Limits limits, DateTimeOffset now)
    {
        if (disposed) return;
        int size = Math.Max(16, GetSystemMetricsForDpi(49, GetDpiForSystem()));
        var snapshot = (limits.Short, Step(limits.Short, now), size);
        bool changed = displayed != snapshot;
        if (changed)
        {
            var next = DrawIcon(limits, now, size);
            var previous = image;
            image = next;
            displayed = snapshot;
            previous?.Dispose();
        }

        string text = $"Usage Viewer · 5 horas: {Remaining(limits.Short)} · 7 días: {Remaining(limits.Weekly)}";
        if (limits.Stale) text += " *";
        changed |= tooltip != text;
        tooltip = text;
        if (!registered) Show();
        else if (changed)
        {
            var data = CreateData();
            if (!Shell_NotifyIcon(1, ref data)) registered = false;
        }

        string Remaining(Cycle? cycle) =>
            cycle is null || cycle.Reset <= now ? "—" : $"{(1 - cycle.Used):P0}";
    }

    private void Show()
    {
        if (disposed || registered || image is null) return;
        var data = CreateData();
        registered = Shell_NotifyIcon(0, ref data);
        if (!registered) return;
        data.Version = 4;
        Shell_NotifyIcon(4, ref data);
    }

    public bool ProcessMessage(int message, IntPtr lParam)
    {
        if (disposed) return false;
        if (taskbarCreated != 0 && (uint)message == taskbarCreated)
        {
            registered = false;
            Show();
            return false;
        }
        if ((uint)message != CallbackMessage) return false;
        uint notification = (uint)(lParam.ToInt64() & 0xFFFF);
        switch (notification)
        {
            case 0x0400: // NIN_SELECT
                select(false);
                break;
            case 0x0401: // NIN_KEYSELECT
                select(true);
                break;
            case 0x007B: // WM_CONTEXTMENU
                hide();
                SetForegroundWindow(owner);
                menu.Show(Forms.Cursor.Position);
                break;
        }
        return true;
    }

    public bool TryGetBounds(out Native.ScreenRect bounds)
    {
        var identifier = new IconIdentifier
        {
            Size = (uint)Marshal.SizeOf<IconIdentifier>(),
            Window = owner,
            Id = 1,
            Guid = IconId
        };
        return Shell_NotifyIconGetRect(ref identifier, out bounds) == 0;
    }

    private IconData CreateData() => new()
    {
        Size = (uint)Marshal.SizeOf<IconData>(),
        Window = owner,
        Id = 1,
        Flags = IconFlags,
        Callback = CallbackMessage,
        Icon = image?.Handle ?? IntPtr.Zero,
        Tip = tooltip,
        Info = "",
        InfoTitle = "",
        Guid = IconId
    };

    // Avoid regenerating the icon for time changes smaller than 1/128 of a cycle.
    private static int Step(Cycle? cycle, DateTimeOffset now) =>
        cycle is null ? 0 : (int)Math.Floor(cycle.Elapsed(now) * 128);

    private static Drawing.Icon DrawIcon(Limits limits, DateTimeOffset now, int size)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var background = new SolidColorBrush(Color.FromRgb(37, 37, 37));
            var outline = new Pen(new SolidColorBrush(Color.FromRgb(160, 160, 160)), .5);
            drawing.DrawEllipse(background, outline, new Point(8, 8), 7.5, 7.5);
            drawing.PushTransform(new TranslateTransform(8, 8));
            var track = RingsView.PenFor(Ink.Remaining).Brush;
            RingsView.DrawRing(drawing, 5.5, 3, Ring.Arcs(limits.Short, now), track);
            drawing.Pop();
        }
        var bitmap = new RenderTargetBitmap(size, size, size * 6, size * 6, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        png.Save(stream);
        stream.Position = 0;
        using var nativeBitmap = new Drawing.Bitmap(stream);
        IntPtr handle = nativeBitmap.GetHicon();
        try
        {
            using var icon = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var data = CreateData();
        Shell_NotifyIcon(2, ref data);
        menu.Dispose();
        image?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct IconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, Callback;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconIdentifier
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public Guid Guid;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref IconData data);
    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(ref IconIdentifier identifier, out Native.ScreenRect bounds);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int metric, uint dpi);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
