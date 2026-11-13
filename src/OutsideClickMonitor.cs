using System.ComponentModel;
using System.Runtime.InteropServices;
using UsageViewer.Core;
using UsageViewer.Windows;

namespace UsageViewer;

internal sealed class OutsideClickMonitor : IDisposable
{
    private readonly NativeMethods.MouseCallback callback;
    private IntPtr hook;
    internal OutsideClickMonitor(Action<int,int,bool> pressed,IEventLog log)
    {
        callback=(code,message,data)=>
        {
            try
            {
                if (code>=0 && message.ToInt64() is 0x0201 or 0x0204 or 0x0207 or 0x020B)
                {
                    var point=Marshal.PtrToStructure<NativeMethods.MouseInput>(data);
                    pressed(point.X,point.Y,message.ToInt64()==0x0201);
                }
            }
            catch (Exception error) { log.Write("mouse_observer_failed",error:error); }
            return NativeMethods.CallNextHookEx(IntPtr.Zero,code,message,data);
        };
        hook=NativeMethods.SetWindowsHookEx(14,callback,NativeMethods.GetModuleHandle(null),0);
        if (hook==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Dispose()
    {
        if (hook==IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(hook); hook=IntPtr.Zero;
        GC.KeepAlive(callback);
    }
}
