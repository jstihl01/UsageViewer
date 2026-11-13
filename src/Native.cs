using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using UsageViewer.Windows;

namespace UsageViewer;

internal static class Native
{
    internal static void PlacePanel(Window panel,NativeMethods.ScreenRect? anchor)
    {
        var handle=new WindowInteropHelper(panel).Handle;
        var rect=anchor??new NativeMethods.ScreenRect();
        var monitor=anchor is null?NativeMethods.MonitorFromWindow(handle,2):NativeMethods.MonitorFromRect(ref rect,2);
        var info=new NativeMethods.MonitorInfo { Size=Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor,ref info)) return;
        var transform=PresentationSource.FromVisual(panel)?.CompositionTarget?.TransformToDevice??System.Windows.Media.Matrix.Identity;
        int width=(int)Math.Ceiling(panel.ActualWidth*transform.M11);
        int height=(int)Math.Ceiling(panel.ActualHeight*transform.M22);
        int x=anchor is {} target?(target.Left+target.Right-width)/2:info.Work.Right-width-12;
        int y=anchor is {} icon?icon.Top-height-8:info.Work.Bottom-height-12;
        x=Math.Clamp(x,info.Work.Left,Math.Max(info.Work.Left,info.Work.Right-width));
        y=Math.Clamp(y,info.Work.Top,Math.Max(info.Work.Top,info.Work.Bottom-height));
        NativeMethods.SetWindowPos(handle,IntPtr.Zero,x,y,0,0,0x0010|0x0004|0x0001);
    }
}
