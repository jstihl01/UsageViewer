using System.Runtime.InteropServices;
using UsageViewer.Core;

namespace UsageViewer.Windows;

public interface ITrayShell
{
    bool Notify(uint operation, ref NativeMethods.IconData data);
    bool Bounds(ref NativeMethods.IconIdentifier identifier, out NativeMethods.ScreenRect bounds);
}
public sealed class WindowsTrayShell : ITrayShell
{
    public bool Notify(uint operation, ref NativeMethods.IconData data) => NativeMethods.Shell_NotifyIcon(operation,ref data);
    public bool Bounds(ref NativeMethods.IconIdentifier id,out NativeMethods.ScreenRect bounds) =>
        NativeMethods.Shell_NotifyIconGetRect(ref id,out bounds)==0;
}

public sealed class TrayRegistration(ITrayShell shell, IntPtr owner, Guid guid, IEventLog log, TimeProvider? time = null) : IDisposable
{
    private readonly TimeProvider clock=time??TimeProvider.System;
    private bool registered, useGuid=true, version4, disposed, retrying, identityEstablished, startupPrepared, probing;
    private long failedAt;
    public bool Version4 => version4;
    public bool Registered => registered;
    public bool UsesGuid => useGuid;
    public void ExplorerRestarted() { registered=false; version4=false; retrying=false; }
    public void RecheckAfterResume() { probing=true; retrying=false; }

    public bool Update(IntPtr icon,string tip,bool changed)
    {
        if (disposed) return false;
        if (retrying && clock.GetElapsedTime(failedAt)<TimeSpan.FromSeconds(5)) return false;
        var data=Data(icon,tip);
        if(!startupPrepared)
        {
            // Older versions could leave their GUID icon behind after switching to HWND identity.
            // The application holds its single-instance mutex before registering this owned GUID.
            shell.Notify(2,ref data);
            startupPrepared=true;
        }
        if (registered && !changed && !probing)
        {
            if (!version4) SetVersion(ref data);
            return true;
        }
        if (registered && shell.Notify(1,ref data)) {probing=false;return true;}
        registered=false;
        // A failed MODIFY can mean the icon still exists. Try MODIFY after ADD before deleting anything.
        if (shell.Notify(0,ref data) || shell.Notify(1,ref data))
            return Complete(ref data);
        if (useGuid && !identityEstablished)
        {
            // A failed ADD/MODIFY does not prove absence. Retire the candidate before creating
            // an alternative identity, and never switch identity after a successful registration.
            shell.Notify(2,ref data);
            var candidate=Identifier(true);
            if(shell.Bounds(ref candidate,out _)) return Failed();
            useGuid=false;
            log.Write("tray_identity_fallback");
            data=Data(icon,tip);
            if (shell.Notify(0,ref data) || shell.Notify(1,ref data)) return Complete(ref data);
        }
        return Failed();
    }
    private bool Failed()
    {
        retrying=true; failedAt=clock.GetTimestamp();
        log.Write("tray_registration_failed");
        return false;
    }

    private bool Complete(ref NativeMethods.IconData data)
    { registered=true; identityEstablished=true; probing=false; retrying=false; SetVersion(ref data); return true; }
    private void SetVersion(ref NativeMethods.IconData data)
    {
        data.Version=4;
        version4=shell.Notify(4,ref data);
        if (!version4) { log.Write("tray_version_fallback"); retrying=true; failedAt=clock.GetTimestamp(); }
        else retrying=false;
    }
    private NativeMethods.IconData Data(IntPtr icon,string tip) => new()
    {
        Size=(uint)Marshal.SizeOf<NativeMethods.IconData>(),Window=owner,Id=1,
        Flags=0x0001|0x0002|0x0004|0x0080|(useGuid?0x0020u:0u), Callback=NativeMethods.CallbackMessage,
        Icon=icon, Tip=tip.Length<128?tip:tip[..127], Info="",InfoTitle="", Guid=useGuid?guid:Guid.Empty
    };
    public bool TryGetBounds(out NativeMethods.ScreenRect bounds)
    {
        var id=Identifier(useGuid);
        return shell.Bounds(ref id,out bounds);
    }
    private NativeMethods.IconIdentifier Identifier(bool byGuid)=>new()
    {Size=(uint)Marshal.SizeOf<NativeMethods.IconIdentifier>(),Window=owner,Id=1,Guid=byGuid?guid:Guid.Empty};
    public void Dispose()
    {
        if (disposed) return;
        disposed=true;
        var data=Data(IntPtr.Zero,"");
        shell.Notify(2,ref data);
        // Retire either identity left by a previous registration attempt using this owner.
        data.Guid=useGuid?Guid.Empty:guid;
        if(useGuid)data.Flags&=~0x20u;else data.Flags|=0x20u;
        shell.Notify(2,ref data);
    }
}
