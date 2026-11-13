using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using UsageViewer.Core;
using UsageViewer.Windows;

namespace UsageViewer;

internal sealed class TrayIndicator : IDisposable
{
    private readonly IntPtr owner;
    private readonly Action toggle;
    private readonly ContextMenu menu=new();
    private readonly MenuItem marker;
    private readonly TrayRegistration registration;
    private readonly uint taskbarCreated=NativeMethods.RegisterWindowMessage("TaskbarCreated");
    private OwnedIcon? icon;
    private (Cycle? Cycle,int Step,int Size,bool Marker)? displayed;
    private string tooltip="";
    private bool dirty=true;
    internal bool ShowMarker { get; private set; }

    internal TrayIndicator(IntPtr owner,Action toggle,Action open,Action hide,Action refresh,
        Action logs,Action<bool> markerChanged,Action exit,bool showMarker,IEventLog log)
    {
        this.owner=owner; this.toggle=toggle; ShowMarker=showMarker;
        registration=new(new WindowsTrayShell(),owner,new("F4E7BD90-A53E-45A9-91A9-77291E4052DC"),log);
        Add("Abrir Usage Viewer",open); Add("Actualizar ahora",refresh);
        marker=new() { Header="Marca de tiempo accesible",IsCheckable=true,IsChecked=showMarker };
        marker.Click+=(_,_)=> { ShowMarker=marker.IsChecked; markerChanged(ShowMarker); dirty=true; };
        menu.Items.Add(marker); Add("Abrir carpeta de registros",logs);
        menu.Items.Add(new Separator()); Add("Salir",exit);
        menu.Opened+=(_,_)=>hide();
        menu.Closed+=(_,_)=>NativeMethods.PostMessage(owner,0,IntPtr.Zero,IntPtr.Zero);
        void Add(string text,Action action)
        {
            var item=new MenuItem { Header=text };
            item.Click+=(_,_)=>action();
            menu.Items.Add(item);
        }
    }

    internal void Update(Limits limits,DateTimeOffset now)
    {
        int size=Math.Max(16,NativeMethods.GetSystemMetricsForDpi(49,NativeMethods.GetDpiForSystem()));
        int step=limits.Short?.Elapsed(now) is {} elapsed?(int)Math.Floor(elapsed*128):0;
        var snapshot=(limits.Short,step,size,ShowMarker);
        if (displayed!=snapshot)
        {
            var next=TrayRenderer.ToIcon(TrayRenderer.Draw(limits,now,size,ShowMarker));
            icon?.Dispose(); icon=next; displayed=snapshot; dirty=true;
        }
        string pace=UsageText.PaceLabel(Ring.GetPace(limits.Short,now));
        string weeklyPace=UsageText.PaceLabel(Ring.GetPace(limits.Weekly,now));
        string text=$"5 horas: {UsageText.Remaining(limits.Short,now)}{(pace.Length>0?" · "+pace:"")}\n"+
            $"7 días: {UsageText.Remaining(limits.Weekly,now)}{(weeklyPace.Length>0?" · "+weeklyPace:"")}";
        if (limits.Stale) text+=" · Desactualizado";
        dirty|=text!=tooltip; tooltip=text;
        if (registration.Update(icon!.Handle,tooltip,dirty)) dirty=false;
    }

    internal bool ProcessMessage(int message,IntPtr lParam)
    {
        if (taskbarCreated!=0 && (uint)message==taskbarCreated)
        { registration.ExplorerRestarted(); dirty=true; return false; }
        if ((uint)message!=NativeMethods.CallbackMessage) return false;
        uint notification=(uint)(lParam.ToInt64()&0xFFFF);
        if (notification is 0x0400 or 0x0401 || (!registration.Version4 && notification==0x0202)) toggle();
        else if (notification==0x007B || (!registration.Version4 && notification==0x0205))
        {
            NativeMethods.SetForegroundWindow(owner);
            if (NativeMethods.GetCursorPos(out var point))
            {
                var source=System.Windows.Interop.HwndSource.FromHwnd(owner);
                var position=source?.CompositionTarget?.TransformFromDevice.Transform(new Point(point.X,point.Y))??new Point(point.X,point.Y);
                menu.Placement=PlacementMode.AbsolutePoint;
                menu.HorizontalOffset=position.X; menu.VerticalOffset=position.Y;
            }
            menu.IsOpen=true;
        }
        return true;
    }
    internal bool TryGetBounds(out NativeMethods.ScreenRect bounds)=>registration.TryGetBounds(out bounds);
    public void Dispose() { menu.IsOpen=false; registration.Dispose(); icon?.Dispose(); }
}
