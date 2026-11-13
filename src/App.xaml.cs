using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using UsageViewer.Core;
using UsageViewer.Windows;
using EventLog=UsageViewer.Core.EventLog;

namespace UsageViewer;

public partial class App : Application
{
    private readonly string data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"UsageViewer");
    private readonly CancellationTokenSource stopping=new();
    private readonly HttpClient http=new();
    private IEventLog log=NullLog.Instance;
    private Panel? panel;
    private TrayIndicator? tray;
    private OutsideClickMonitor? outside;
    private PanelVisibility? visibility;
    private SessionController? session;
    private DispatcherTimer? timer;
    private Mutex? instance,setupMutex;
    private EventWaitHandle? activate,shutdown;
    private RegisteredWaitHandle? activationWait,shutdownWait;
    private SettingsStore? store;
    private AppSettings settings=new();
    private bool ownsInstance,closing,checkingUpdates;
    private string shellNotice="";
    private readonly Version version=Assembly.GetExecutingAssembly().GetName().Version??new(1,1,0);
    private string Profile=>Path.Combine(data,"codex-profile");
    private string Legacy=>Path.Combine(Profile,"auth.json");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        log=new EventLog(Path.Combine(data,"logs"),version.ToString(3));
        DispatcherUnhandledException+=(_,args)=>
        {
            log.Write("dispatcher_unhandled",error:args.Exception);
            args.Handled=true;
            if (!closing) { closing=true; _=StopAsync(); }
        };
        AppDomain.CurrentDomain.UnhandledException+=(_,args)=>log.Write("process_unhandled",error:args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException+=(_,args)=>
        { log.Write("task_unobserved",error:args.Exception); args.SetObserved(); };
        try
        {
            using var identity=WindowsIdentity.GetCurrent();
            string prefix=@"Local\UsageViewer-"+identity.User!.Value;
            bool command=e.Args.Contains("--shutdown")||e.Args.Contains("--uninstall-cleanup");
            if (command)
            {
                try { using var signal=EventWaitHandle.OpenExisting(prefix+"-shutdown"); signal.Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
                try
                {
                    using var running=Mutex.OpenExisting(prefix);
                    try { if (!running.WaitOne(TimeSpan.FromSeconds(12))) throw new IOException("La aplicación no se ha cerrado."); }
                    catch (AbandonedMutexException) { }
                    running.ReleaseMutex();
                }
                catch (WaitHandleCannotBeOpenedException) { }
                if (e.Args.Contains("--uninstall-cleanup"))
                {
                    CredentialCleanup.DeleteOwned(Profile);
                    File.Delete(Legacy);
                    log.Write("uninstall_credentials_removed");
                }
                Shutdown(); return;
            }
            // Windows default DACL plus a SID-scoped name; failures are surfaced instead of crashing silently.
            instance=new Mutex(true,prefix,out ownsInstance);
            if (!ownsInstance)
            {
                try { if(!e.Args.Contains("--background",StringComparer.OrdinalIgnoreCase))
                    { using var signal=EventWaitHandle.OpenExisting(prefix+"-activate"); signal.Set(); } }
                catch (WaitHandleCannotBeOpenedException) { log.Write("instance_activation_unavailable"); }
                Shutdown(); return;
            }
            WaitHandleSecurity.Restrict(instance,identity.User!.Value);
            setupMutex=new Mutex(true,@"Local\UsageViewer-Installer");
            WaitHandleSecurity.Restrict(setupMutex,identity.User.Value);
            activate=new(false,EventResetMode.AutoReset,prefix+"-activate");
            shutdown=new(false,EventResetMode.AutoReset,prefix+"-shutdown");
            WaitHandleSecurity.Restrict(activate,identity.User.Value);
            WaitHandleSecurity.Restrict(shutdown,identity.User.Value);
            store=new(data,log); settings=store.Load();
            panel=new Panel(); MainWindow=panel;
            panel.SessionAction+=()=>Observe(session!.ChangeSessionAsync());
            panel.HideRequested+=HidePanel;
            panel.SizeChanged+=(_,_)=> { if(panel.IsVisible) PositionPanel(); };
            panel.Closing+=(_,args)=> { if(!closing) { args.Cancel=true; HidePanel(); } };
            panel.Deactivated+=(_,_)=> { if(outside is null && panel.IsVisible) Dispatcher.BeginInvoke(FallbackHide); };
            IntPtr hwnd=new WindowInteropHelper(panel).EnsureHandle();
            HwndSource.FromHwnd(hwnd)?.AddHook(WindowMessage);
            if (!NativeMethods.RegisterHotKey(hwnd,1,0x4003,'U'))
            { shellNotice="Ctrl+Alt+U no está disponible: puede estar en uso por otra aplicación."; log.Write("hotkey_unavailable"); }
            tray=new(hwnd,TogglePanel,OpenPanel,HidePanel,()=>Observe(RefreshNowAsync()),
                OpenLogs,enabled=> { settings=settings with { ShowTimeMarker=enabled }; store.Save(settings); },
                ()=>Observe(StopAsync()),settings.ShowTimeMarker,log);
            using var manifest=Assembly.GetExecutingAssembly().GetManifestResourceStream("UsageViewer.dependencies.json")
                ??throw new IOException("No se encontró el manifiesto de dependencias.");
            using var pins=JsonDocument.Parse(manifest);
            string hash=pins.RootElement.GetProperty("codex").GetProperty("sha256").GetString()!;
            session=new(new CodexProcessFactory(Path.Combine(AppContext.BaseDirectory,"codex","codex-app-server.exe"),
                Profile,hash,version.ToString(3),log),LaunchBrowserAsync,log,
                legacyCredentials:()=>File.Exists(Legacy),cleanupLegacy:()=>File.Delete(Legacy));
            session.Changed+=_=>Dispatcher.BeginInvoke(Render);
            visibility=new(
                ()=> { panel.Show();PositionPanel();panel.Activate(); },
                ()=> { panel.Hide();outside=null; },
                ()=>
                {
                    try { return outside=new OutsideClickMonitor(OnMousePressed,log); }
                    catch(Exception error) { log.Write("mouse_observer_unavailable",error:error);return null; }
                });
            activationWait=ThreadPool.RegisterWaitForSingleObject(activate,(_,_)=>Dispatcher.BeginInvoke(OpenPanel),null,Timeout.Infinite,false);
            shutdownWait=ThreadPool.RegisterWaitForSingleObject(shutdown,(_,_)=>Dispatcher.BeginInvoke(()=>Observe(StopAsync())),null,Timeout.Infinite,false);
            SystemEvents.PowerModeChanged+=PowerChanged;
            SystemParameters.StaticPropertyChanged+=SettingsChanged;
            timer=new DispatcherTimer(TimeSpan.FromSeconds(1),DispatcherPriority.Background,(_,_)=>Observe(TickAsync()),Dispatcher);
            Render();
            if (StartupPolicy.ShowPanel(settings.FirstRunCompleted,e.Args))
            {
                settings=settings with { FirstRunCompleted=true }; store.Save(settings);
                OpenPanel();
            }
            Observe(session.RestoreAsync());
            Observe(CheckUpdatesAsync());
            log.Write("application_started");
        }
        catch(Exception error)
        {
            log.Write("startup_failed",error:error);
            if(!e.Args.Contains("--shutdown")&&!e.Args.Contains("--uninstall-cleanup"))
                MessageBox.Show("Usage Viewer no pudo iniciarse. Consulta los registros en "+Path.Combine(data,"logs"),
                    "Usage Viewer",MessageBoxButton.OK,MessageBoxImage.Error);
            else {Shutdown(1);return;}
            await StopAsync();
        }
    }

    private async Task TickAsync()
    {
        if(closing) return;
        Render();
        if(session is not null) await session.TickAsync();
    }
    private void Render()
    {
        if(closing||panel is null||session is null) return;
        var state=session.State; var now=DateTimeOffset.UtcNow;
        panel.Update(state,now,shellNotice);
        string? available=Version.TryParse(settings.AvailableVersion,out var candidate)&&candidate>version?
            settings.AvailableVersion:null;
        panel.SetUpdate(available,()=>Observe(LaunchBrowserAsync(UpdateChecker.Releases)));
        tray?.Update(state.Limits,now);
    }
    private async Task RefreshNowAsync()
    {
        if(session is null) return;
        if(session.State.Session==SessionState.Disconnected) await session.RestoreAsync();
        else await session.RefreshAsync(true);
    }
    private async Task CheckUpdatesAsync()
    {
        if(checkingUpdates||closing) return;
        checkingUpdates=true;
        try
        {
            var result=await new UpdateChecker(http,TimeProvider.System,log).CheckAsync(settings,version,stopping.Token);
            settings=settings with { UpdateChecked=result.UpdateChecked,UpdateETag=result.UpdateETag,
                AvailableVersion=result.AvailableVersion };
            store?.Save(settings); _=Dispatcher.BeginInvoke(Render);
        }
        finally { checkingUpdates=false; }
    }
    private static Task LaunchBrowserAsync(Uri url)
    {
        using var launched=Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute=true })
            ??throw new IOException("No se pudo abrir el navegador.");
        return Task.CompletedTask;
    }
    private void OpenLogs()
    {
        string directory=Path.Combine(data,"logs"); Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute=true })?.Dispose();
    }
    private void OpenPanel()
    {
        if(closing||panel is null) return;
        visibility?.Open();PositionPanel();panel.Activate();
        if(!settings.FirstRunCompleted)
        {settings=settings with{FirstRunCompleted=true};store?.Save(settings);}
        Observe(RefreshNowAsync()); Observe(CheckUpdatesAsync());
    }
    private void TogglePanel() { if(visibility?.Visible==true) HidePanel(); else OpenPanel(); }
    private void HidePanel()
    {
        if(visibility is not null)visibility.Close();
        else {outside?.Dispose();outside=null;panel?.Hide();}
    }
    private void OnMousePressed(int x,int y,bool left)
    {
        if(closing||panel?.IsVisible!=true) return;
        bool insidePanel=NativeMethods.GetWindowRect(new WindowInteropHelper(panel).Handle,out var rect)&&rect.Contains(x,y);
        // Let NIN_SELECT alone handle clicks on the icon; do not hide and then reopen for the same gesture.
        bool insideIcon=tray?.TryGetBounds(out var bounds)==true&&bounds.Contains(x,y);
        if(insidePanel||insideIcon&&left)return;
        Dispatcher.BeginInvoke(()=>visibility?.Press(insidePanel,insideIcon,left),DispatcherPriority.Input);
    }
    private void FallbackHide()
    {
        if(NativeMethods.GetCursorPos(out var point)&&tray?.TryGetBounds(out var bounds)==true&&bounds.Contains(point.X,point.Y))
            return;
        HidePanel();
    }
    private void PositionPanel()
    {
        if(panel is null) return;
        NativeMethods.ScreenRect? bounds=tray?.TryGetBounds(out var rect)==true?rect:null;
        Native.PlacePanel(panel,bounds);
    }
    private IntPtr WindowMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        try
        {
            if(tray?.ProcessMessage(message,lParam)==true) { handled=true; return IntPtr.Zero; }
            if(message is 0x007E or 0x02E0) { if(panel?.IsVisible==true) Dispatcher.BeginInvoke(PositionPanel); }
            if(message==0x0312&&wParam==1) { OpenPanel(); handled=true; }
        }
        catch(Exception error) { log.Write("window_callback_failed",error:error); }
        return IntPtr.Zero;
    }
    private void SettingsChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs args)=>
        Dispatcher.BeginInvoke(()=> { if(panel?.IsVisible==true) PositionPanel(); });
    private void PowerChanged(object sender,PowerModeChangedEventArgs args)
    {
        if(args.Mode==PowerModes.Resume) Dispatcher.BeginInvoke(()=>
        { if(panel?.IsVisible==true) PositionPanel(); Observe(RefreshNowAsync()); });
    }
    private async void Observe(Task task)
    {
        try { await task; }
        catch(OperationCanceledException) when(stopping.IsCancellationRequested) { }
        catch(Exception error) { log.Write("application_operation_failed",error:error); }
    }
    private async Task StopAsync()
    {
        closing=true; stopping.Cancel(); timer?.Stop(); HidePanel();
        try {if(session is not null) await session.DisposeAsync();}
        catch(Exception error){log.Write("application_shutdown_failed",error:error);}
        finally{Shutdown();}
    }
    protected override void OnExit(ExitEventArgs e)
    {
        closing=true; stopping.Cancel(); timer?.Stop();
        if(session is not null)
        {
            try
            {
                // OnExit is synchronous, including Windows shutdown. Never wait indefinitely on the UI thread.
                if(!Task.Run(async()=>await session.DisposeAsync()).Wait(TimeSpan.FromSeconds(5)))
                    log.Write("session_shutdown_deadline");
            }
            catch(Exception error) {log.Write("session_exit_cleanup_failed",error:error);}
        }
        SystemEvents.PowerModeChanged-=PowerChanged;
        SystemParameters.StaticPropertyChanged-=SettingsChanged;
        outside?.Dispose(); tray?.Dispose();
        if(panel is not null) NativeMethods.UnregisterHotKey(new WindowInteropHelper(panel).Handle,1);
        activationWait?.Unregister(null); shutdownWait?.Unregister(null);
        activate?.Dispose(); shutdown?.Dispose();
        if(ownsInstance) { try { instance?.ReleaseMutex(); } catch(ApplicationException) { } }
        instance?.Dispose(); setupMutex?.Dispose(); http.Dispose();
        log.Write("application_stopped");
        base.OnExit(e);
    }
}
