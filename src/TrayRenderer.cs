using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UsageViewer.Core;
using UsageViewer.Windows;

namespace UsageViewer;

internal sealed class OwnedIcon : IDisposable
{
    private IntPtr handle;
    internal OwnedIcon(IntPtr handle)=>this.handle=handle!=IntPtr.Zero?handle:throw new InvalidOperationException("No se pudo crear el icono.");
    internal IntPtr Handle=>handle;
    public void Dispose()
    {var previous=Interlocked.Exchange(ref handle,IntPtr.Zero);if(previous!=IntPtr.Zero)NativeMethods.DestroyIcon(previous);}
}

internal static class TrayRenderer
{
    internal static BitmapSource Draw(Limits limits,DateTimeOffset now,int size,bool marker=false)
    {
        var visual=new DrawingVisual();
        using (var drawing=visual.RenderOpen())
        {
            drawing.DrawEllipse(new SolidColorBrush(Color.FromRgb(37,37,37)),
                new Pen(new SolidColorBrush(Color.FromRgb(160,160,160)),.5),new Point(8,8),7.5,7.5);
            drawing.PushTransform(new TranslateTransform(8,8));
            RingsView.DrawRing(drawing,5.5,3,Ring.Arcs(limits.Short,now),
                RingsView.PenFor(SegmentKind.Remaining).Brush);
            if (marker && limits.Short is {} cycle && !cycle.Expired(now) && cycle.Elapsed(now) is {} elapsed)
            {
                double angle=elapsed*Math.Tau;
                drawing.DrawLine(new Pen(Brushes.White,.6),
                    new Point(Math.Sin(angle)*3.1,-Math.Cos(angle)*3.1),
                    new Point(Math.Sin(angle)*4.1,-Math.Cos(angle)*4.1));
            }
            drawing.Pop();
        }
        var bitmap=new RenderTargetBitmap(size,size,size*6,size*6,PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze();
        return bitmap;
    }

    internal static OwnedIcon ToIcon(BitmapSource bitmap)
    {
        int size=bitmap.PixelWidth;
        var pixels=new byte[size*size*4];
        bitmap.CopyPixels(pixels,size*4,0);
        var info=new NativeMethods.BitmapInfo
        { Size=(uint)Marshal.SizeOf<NativeMethods.BitmapInfo>(),Width=size,Height=-size,Planes=1,BitCount=32 };
        IntPtr color=NativeMethods.CreateDIBSection(IntPtr.Zero,ref info,0,out var bits,IntPtr.Zero,0);
        if (color==IntPtr.Zero) throw new InvalidOperationException("No se pudo crear el mapa de bits.");
        IntPtr mask=IntPtr.Zero;
        try
        {
            Marshal.Copy(pixels,0,bits,pixels.Length);
            byte[] maskBytes=new byte[((size+15)/16)*2*size];
            var pin=GCHandle.Alloc(maskBytes,GCHandleType.Pinned);
            try { mask=NativeMethods.CreateBitmap(size,size,1,1,pin.AddrOfPinnedObject()); }
            finally { pin.Free(); }
            if (mask==IntPtr.Zero) throw new InvalidOperationException("No se pudo crear la máscara.");
            var iconInfo=new NativeMethods.IconInfo { IsIcon=true,Color=color,Mask=mask };
            return new(NativeMethods.CreateIconIndirect(ref iconInfo));
        }
        finally
        {
            if (mask!=IntPtr.Zero) NativeMethods.DeleteObject(mask);
            NativeMethods.DeleteObject(color);
        }
    }
}
