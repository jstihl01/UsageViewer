using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace UsageViewer;

public partial class App : Application
{
    private readonly string data = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageViewer");
    private readonly CancellationTokenSource stopping = new();
    private readonly RefreshSchedule schedule = new();
    private Panel panel = null!;
    private DispatcherTimer? timer;
    private TrayIndicator? tray;
    private OutsideClickMonitor? outsideClicks;
    private bool detailVisibleAtMouseDown;
    private CodexConnection? connection;
    private Mutex? instance;
    private EventWaitHandle? activate;
    private RegisteredWaitHandle? activationWait;
    private bool ownsInstance;
    private bool signedIn;
    private bool busy;
    private bool refreshing;
    private bool refreshQueued;
    private int sessionVersion;
    private string? email;
    private string? loginId;
    private string message = "";
    private Limits limits = Limits.Empty;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        using var identity = WindowsIdentity.GetCurrent();
        string prefix = @"Local\UsageViewer-" + identity.User!.Value;
        activate = new EventWaitHandle(false, EventResetMode.AutoReset, prefix + "-activate");
        instance = new Mutex(true, prefix, out ownsInstance);
        if (!ownsInstance)
        {
            activate.Set();
            Shutdown();
            return;
        }

        panel = new Panel();
        panel.SessionAction += ChangeSession;
        panel.SizeChanged += (_, _) => { if (panel.IsVisible) PositionPanel(); };
        panel.Closing += (_, args) =>
        {
            if (stopping.IsCancellationRequested) return;
            args.Cancel = true;
            HidePanel();
        };
        MainWindow = panel;
        IntPtr handle = new WindowInteropHelper(panel).EnsureHandle();
        HwndSource.FromHwnd(handle)?.AddHook(WindowMessage);
        Native.RegisterHotKey(handle, 1, 0x4003, 'U');
        tray = new TrayIndicator(handle, SelectTrayIcon, OpenPanel, HidePanel, Shutdown);
        outsideClicks = new OutsideClickMonitor(OnMousePressed);
        Render();
        activationWait = ThreadPool.RegisterWaitForSingleObject(activate,
            (_, _) => Dispatcher.BeginInvoke(OpenPanel), null, Timeout.Infinite, false);
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemParameters.StaticPropertyChanged += SystemSettingsChanged;
        timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            var now = DateTimeOffset.UtcNow;
            Render();
            if (signedIn && schedule.IsDue(now, limits) && !refreshing)
                _ = RefreshAsync();
            else if (loginId is not null && connection?.IsAlive != true)
            {
                loginId = null;
                message = "Se interrumpió el inicio de sesión. Vuelve a intentarlo.";
                Render();
            }
        }, Dispatcher);
        _ = RestoreAsync();
    }

    private async Task<CodexConnection> ConnectAsync()
    {
        if (connection?.IsAlive == true) return connection;
        connection?.Dispose();
        connection = null;
        var server = await CodexConnection.StartAsync(Path.Combine(data, "codex-profile"), stopping.Token);
        connection = server;
        server.Notification += (method, args) =>
            Dispatcher.BeginInvoke(() => OnNotification(server, method, args));
        return server;
    }

    private async Task RestoreAsync()
    {
        busy = true;
        message = "Conectando con Codex…";
        Render();
        try
        {
            var server = await ConnectAsync();
            await ReadAccountAsync(server);
            message = signedIn ? "" : "Inicia sesión con ChatGPT para ver tu uso de Codex.";
        }
        catch (Exception error) when (IsRecoverable(error))
        {
            message = "No se pudo conectar con Codex. Vuelve a intentarlo.";
        }
        finally
        {
            busy = false;
            Render();
        }
        if (signedIn) await RefreshAsync();
    }

    private async Task ReadAccountAsync(CodexConnection server)
    {
        var result = await server.RequestAsync("account/read", new { refreshToken = false }, stopping.Token);
        signedIn = result.TryGetProperty("account", out var account) &&
            account.ValueKind == JsonValueKind.Object &&
            account.GetProperty("type").GetString() == "chatgpt";
        email = signedIn && account.TryGetProperty("email", out var value) ? value.GetString() : null;
    }

    private async void ChangeSession()
    {
        if (busy) return;
        busy = true;
        message = "";
        Render();
        bool startRefresh = false;
        try
        {
            var server = await ConnectAsync();
            if (signedIn)
            {
                sessionVersion++;
                await server.RequestAsync("account/logout", null, stopping.Token);
                signedIn = false;
                email = null;
                limits = Limits.Empty;
                message = "Sesión cerrada.";
            }
            else if (loginId is { } pending)
            {
                loginId = null;
                await server.RequestAsync("account/login/cancel", new { loginId = pending }, stopping.Token);
                message = "Inicio de sesión cancelado.";
            }
            else
            {
                await ReadAccountAsync(server);
                if (signedIn)
                    startRefresh = true;
                else
                {
                    var result = await server.RequestAsync("account/login/start", new { type = "chatgpt" }, stopping.Token);
                    loginId = result.GetProperty("loginId").GetString();
                    var url = new Uri(result.GetProperty("authUrl").GetString()!);
                    if (url.Scheme != Uri.UriSchemeHttps)
                        throw new IOException("La URL de inicio de sesión no es segura.");
                    Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })?.Dispose();
                    message = "Completa el inicio de sesión en tu navegador.";
                }
            }
        }
        catch (Exception error) when (IsRecoverable(error))
        {
            message = "No se pudo completar la operación con Codex. Vuelve a intentarlo.";
        }
        finally
        {
            busy = false;
            Render();
        }
        if (startRefresh) await RefreshAsync();
    }

    private async void OnNotification(CodexConnection server, string method, JsonElement args)
    {
        if (server != connection || stopping.IsCancellationRequested) return;
        if (method == "account/rateLimits/updated")
        {
            await RefreshAsync();
            return;
        }
        if (method != "account/login/completed" || loginId is null ||
            !args.TryGetProperty("loginId", out var id) || id.GetString() != loginId)
            return;
        loginId = null;
        busy = true;
        try
        {
            if (args.GetProperty("success").GetBoolean())
                await ReadAccountAsync(server);
            message = signedIn ? "" : "No se pudo iniciar sesión. Vuelve a intentarlo.";
        }
        catch (Exception error) when (IsRecoverable(error))
        {
            message = "No se pudo comprobar la cuenta de ChatGPT. Vuelve a intentarlo.";
        }
        finally
        {
            busy = false;
            Render();
        }
        if (signedIn) await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (!signedIn || busy || stopping.IsCancellationRequested) return;
        if (refreshing)
        {
            refreshQueued = true;
            return;
        }
        refreshing = true;
        int version = sessionVersion;
        schedule.Started(DateTimeOffset.UtcNow);
        try
        {
            var server = await ConnectAsync();
            var result = await server.RequestAsync("account/rateLimits/read", null, stopping.Token);
            if (version == sessionVersion)
            {
                limits = Limits.Parse(result);
                message = "";
            }
        }
        catch (Exception error) when (IsRecoverable(error))
        {
            if (version == sessionVersion)
                limits = limits.MarkStale();
        }
        finally
        {
            refreshing = false;
            Render();
        }
        if (refreshQueued)
        {
            refreshQueued = false;
            await RefreshAsync();
        }
    }

    private static bool IsRecoverable(Exception error) => error is IOException or UnauthorizedAccessException or
        System.ComponentModel.Win32Exception or OperationCanceledException or JsonException or
        InvalidOperationException or KeyNotFoundException or UriFormatException;

    private void Render()
    {
        if (stopping.IsCancellationRequested) return;
        var now = DateTimeOffset.UtcNow;
        panel.Update(limits, now, signedIn, loginId is not null, busy, email, message);
        tray?.Update(limits, now);
    }

    private void OpenPanel()
    {
        if (stopping.IsCancellationRequested) return;
        panel.Show();
        PositionPanel();
        panel.Activate();
        _ = RefreshAsync();
    }

    private void SelectTrayIcon(bool fromKeyboard)
    {
        // Windows may deactivate or hide the panel before delivering the icon's mouse-up callback.
        bool close = fromKeyboard ? panel.IsVisible : detailVisibleAtMouseDown;
        if (close) HidePanel();
        else OpenPanel();
    }

    private void OnMousePressed(int x, int y)
    {
        if (stopping.IsCancellationRequested) return;
        detailVisibleAtMouseDown = panel.IsVisible;
        if (!detailVisibleAtMouseDown) return;
        if (Native.GetWindowRect(new WindowInteropHelper(panel).Handle, out var bounds) && bounds.Contains(x, y))
            return;
        // Queue the hide so the mouse hook returns promptly and the original click reaches its target.
        Dispatcher.BeginInvoke(HidePanel, DispatcherPriority.Input);
    }

    private void HidePanel() => panel.Hide();

    private void PositionPanel()
    {
        Native.ScreenRect? anchor = tray?.TryGetBounds(out var bounds) == true ? bounds : null;
        Native.PlacePanel(panel, anchor);
    }

    private void Reposition()
    {
        if (stopping.IsCancellationRequested) return;
        if (panel.IsVisible) PositionPanel();
    }

    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (tray?.ProcessMessage(msg, lParam) == true)
        {
            handled = true;
            return IntPtr.Zero;
        }
        if (msg is 0x007E or 0x02E0)
            Dispatcher.BeginInvoke(Reposition);
        if (msg == 0x0312 && wParam == 1)
        {
            OpenPanel();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void SystemSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) =>
        Dispatcher.BeginInvoke(Reposition);

    private void PowerChanged(object sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Resume)
            Dispatcher.BeginInvoke(() => { Reposition(); Render(); _ = RefreshAsync(); });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        stopping.Cancel();
        if (ownsInstance)
        {
            SystemEvents.PowerModeChanged -= PowerChanged;
            SystemParameters.StaticPropertyChanged -= SystemSettingsChanged;
            timer?.Stop();
            outsideClicks?.Dispose();
            tray?.Dispose();
            connection?.Dispose();
            activationWait?.Unregister(null);
            instance?.ReleaseMutex();
        }
        activate?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}
