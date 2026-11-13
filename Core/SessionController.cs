using System.Text.Json;

namespace UsageViewer.Core;

public enum SessionState { Connecting, Disconnected, SignedOut, SigningIn, Connected, SigningOut, Stopping }
public enum SessionNotice { None, Connecting, Disconnected, ServerMissing, ServerIntegrity, CredentialStoreUnavailable, SignIn, Migration, LoginPending, LoginFailed,
    BrowserFailed, InvalidQuotas, NoMatchingWindows, Stale, AuthenticationRequired, UnexpectedError, CleanupFailed }
public sealed record ViewState(SessionState Session, Limits Limits, SessionNotice Notice,
    string? Email = null, DateTimeOffset? LastSuccess = null);

public static class LoginUrl
{
    public static bool TryValidate(string? text, out Uri? url)
    {
        url = null;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var candidate) ||
            candidate.Scheme != Uri.UriSchemeHttps || candidate.UserInfo.Length != 0 || !candidate.IsDefaultPort ||
            candidate.Host is not ("auth.openai.com" or "chatgpt.com")) return false;
        url = candidate;
        return true;
    }
}

public sealed class SessionController : IAsyncDisposable
{
    private readonly ICodexClientFactory factory;
    private readonly TimeProvider clock;
    private readonly IEventLog log;
    private readonly Func<Uri, Task> browser;
    private readonly Func<bool> legacyCredentials;
    private readonly Action cleanupLegacy;
    private readonly object sync = new();
    private readonly SemaphoreSlim connectionGate = new(1);
    private readonly SemaphoreSlim operationGate = new(1);
    private readonly CancellationTokenSource stopping = new();
    private readonly RefreshSchedule schedule;
    private CancellationTokenSource refreshStop = new();
    private ICodexClient? connection;
    private Task refreshTask = Task.CompletedTask;
    private bool refreshing, refreshQueued, restoring, disposed, cleanupFailed;
    private string? loginId;
    private int sessionVersion, restoreFailures, quotaFailures;
    private long restoreFailedAt, lastNotification, lastAccountNotification;
    private bool notificationSeen, accountNotificationSeen;
    private ViewState state = new(SessionState.Connecting, Limits.Empty, SessionNotice.Connecting);
    public event Action<ViewState>? Changed;
    public ViewState State { get { lock (sync) return state; } }

    public SessionController(ICodexClientFactory factory, Func<Uri, Task> browser,
        IEventLog? log = null, TimeProvider? clock = null,
        Func<bool>? legacyCredentials = null, Action? cleanupLegacy = null)
    {
        this.factory = factory; this.browser = browser;
        this.log = log ?? NullLog.Instance; this.clock = clock ?? TimeProvider.System;
        this.legacyCredentials = legacyCredentials ?? (() => false);
        this.cleanupLegacy = cleanupLegacy ?? (() => { });
        schedule = new(this.clock);
    }

    private void Publish(ViewState next)
    {
        lock (sync)
        {
            if (disposed && next.Session != SessionState.Stopping) return;
            state = next;
        }
        try { Changed?.Invoke(next); }
        catch (Exception error) { log.Write("session_view_handler_failed", error: error); }
    }

    private async Task<ICodexClient> ConnectAsync()
    {
        await connectionGate.WaitAsync(stopping.Token);
        try
        {
            if (connection?.IsAlive == true) return connection;
            if (connection is { } old) { connection = null; await old.DisposeAsync(); }
            var client = await factory.StartAsync(stopping.Token);
            connection = client;
            client.Notification += (method, args) => Notification(client, method, args);
            return client;
        }
        finally { connectionGate.Release(); }
    }

    public async Task RestoreAsync()
    {
        lock (sync) { if (disposed || restoring) return; restoring = true; }
        bool entered = false;
        try
        {
            await operationGate.WaitAsync(stopping.Token);
            entered = true;
            Publish(State with { Session = SessionState.Connecting, Notice = SessionNotice.Connecting });
            await ReadAccountAsync(await ConnectAsync());
            restoreFailures = 0;
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (CodexRpcException error) when(error.AuthenticationFailure)
        {
            log.Write("session_authentication_required","account/read",error,error.Code);
            Interlocked.Increment(ref sessionVersion);
            Publish(new(SessionState.SignedOut,Limits.Empty,SessionNotice.AuthenticationRequired));
        }
        catch (Exception error)
        {
            log.Write("session_restore_failed", "account/read", error);
            restoreFailedAt = clock.GetTimestamp();
            restoreFailures++;
            var notice=error is FileNotFoundException or DirectoryNotFoundException?SessionNotice.ServerMissing:
                error is InvalidDataException?SessionNotice.ServerIntegrity:
                error is CodexRpcException {CredentialStorageFailure:true}?
                SessionNotice.CredentialStoreUnavailable:SessionNotice.Disconnected;
            Publish(State with { Session = SessionState.Disconnected, Notice = notice,
                Limits = State.Limits.MarkStale() });
        }
        finally
        {
            if (entered) operationGate.Release();
            lock (sync) restoring = false;
        }
        if (State.Session == SessionState.Connected) await RefreshAsync(true);
    }

    private async Task ReadAccountAsync(ICodexClient client,bool refreshToken=false)
    {
        var result = await client.RequestAsync("account/read", new { refreshToken }, stopping.Token);
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("account", out var account))
            throw new IOException("La respuesta de cuenta no es válida.");
        if (account.ValueKind == JsonValueKind.Null)
        {
            Interlocked.Increment(ref sessionVersion);
            Publish(new(SessionState.SignedOut, Limits.Empty,
                legacyCredentials() ? SessionNotice.Migration : SessionNotice.SignIn));
            return;
        }
        if (account.ValueKind != JsonValueKind.Object || !account.TryGetProperty("type", out var type) ||
            type.ValueKind != JsonValueKind.String)
            throw new IOException("La respuesta de cuenta no es válida.");
        if (type.GetString() != "chatgpt")
        {
            Interlocked.Increment(ref sessionVersion);
            Publish(new(SessionState.SignedOut, Limits.Empty, SessionNotice.AuthenticationRequired));
            return;
        }
        string? email = account.TryGetProperty("email", out var value) && value.ValueKind == JsonValueKind.String ?
            value.GetString() : null;
        var previous = State;
        bool changed = previous.Session is not (SessionState.Connected or SessionState.SigningOut or SessionState.Connecting) ||
            previous.Email != email;
        if (changed) Interlocked.Increment(ref sessionVersion);
        SessionNotice notice = !changed && previous.Limits.Stale ? SessionNotice.Stale : SessionNotice.None;
        loginId=null;
        try { if (legacyCredentials()) cleanupLegacy(); cleanupFailed=false; }
        catch (Exception error) { cleanupFailed=true;log.Write("credential_migration_cleanup_failed", error: error); notice = SessionNotice.CleanupFailed; }
        Publish(new(SessionState.Connected, changed ? Limits.Empty : previous.Limits, notice, email, previous.LastSuccess));
    }

    public async Task TickAsync()
    {
        if (disposed) return;
        var snapshot = State;
        if (snapshot.Session == SessionState.Disconnected)
        {
            int[] seconds = [5, 15, 45, 135, 300];
            if (clock.GetElapsedTime(restoreFailedAt) >= TimeSpan.FromSeconds(seconds[Math.Clamp(restoreFailures - 1, 0, 4)]))
                await RestoreAsync();
        }
        else if (snapshot.Session == SessionState.Connected)
        {
            bool due; lock (sync) due = schedule.IsDue(snapshot.Limits);
            if (due) await RefreshAsync();
        }
        else if (snapshot.Session == SessionState.SigningIn && connection?.IsAlive != true)
        {
            loginId = null;
            Publish(snapshot with { Session = SessionState.Disconnected, Notice = SessionNotice.LoginFailed });
        }
    }

    public Task RefreshAsync(bool force = false)
    {
        lock (sync)
        {
            if (disposed || state.Session != SessionState.Connected) return Task.CompletedTask;
            if (refreshing) { refreshQueued |= force; return refreshTask; }
            if (!force && !schedule.IsDue(state.Limits)) return Task.CompletedTask;
            refreshing = true;
            refreshStop.Dispose();
            refreshStop = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
            var token=refreshStop.Token;
            refreshTask = Task.Run(() => RefreshLoopAsync(token));
            return refreshTask;
        }
    }

    private async Task RefreshLoopAsync(CancellationToken cancellation)
    {
        try
        {
            // At most one queued pass. Repeated identical server notifications are ignored.
            for (int pass = 0; pass < 2; pass++)
            {
                int version = Volatile.Read(ref sessionVersion);
                lock (sync) schedule.Started();
                try
                {
                    var client = await ConnectAsync();
                    var result = await client.RequestAsync("account/rateLimits/read", null, cancellation);
                    if (version != Volatile.Read(ref sessionVersion) || cancellation.IsCancellationRequested) return;
                    var parsed = QuotaParser.Parse(result);
                    if (parsed.Kind == ParseKind.Invalid) throw new InvalidDataException("Respuesta de cuotas no reconocible.");
                    quotaFailures = 0;
                    Publish(State with { Limits = parsed.Limits, LastSuccess = clock.GetUtcNow(),
                        Notice = cleanupFailed?SessionNotice.CleanupFailed:
                            parsed.Kind == ParseKind.NoMatchingWindows ? SessionNotice.NoMatchingWindows : SessionNotice.None });
                    lock (sync) schedule.Succeeded(parsed.Limits);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
                catch (Exception error)
                {
                    log.Write("session_refresh_failed", "account/rateLimits/read", error,
                        error is CodexRpcException rpc ? rpc.Code : null);
                    if (version != Volatile.Read(ref sessionVersion)) return;
                    quotaFailures++;
                    Publish(State with { Limits = State.Limits.MarkStale(), Notice =
                        error is InvalidDataException ? SessionNotice.InvalidQuotas : SessionNotice.Stale });
                    lock (sync) schedule.Failed();
                    if (quotaFailures >= 3 || error is CodexRpcException { AuthenticationFailure: true })
                    {
                        await operationGate.WaitAsync(cancellation);
                        try
                        {
                            try { await ReadAccountAsync(await ConnectAsync(),true); }
                            catch(CodexRpcException auth) when(auth.AuthenticationFailure)
                            {
                                Interlocked.Increment(ref sessionVersion);
                                Publish(new(SessionState.SignedOut,Limits.Empty,SessionNotice.AuthenticationRequired));
                            }
                        }
                        finally { operationGate.Release(); }
                    }
                }
                lock (sync)
                {
                    if (!refreshQueued || state.Session != SessionState.Connected) break;
                    refreshQueued = false;
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { log.Write("session_recheck_failed", error: error); }
        finally { lock (sync) { refreshing = false; refreshQueued = false; } }
    }

    private async Task CancelRefreshAsync()
    {
        Task task;
        lock (sync) { refreshStop.Cancel(); task = refreshTask; }
        try { await task; } catch (Exception error) { log.Write("refresh_cancel_failed", error: error); }
    }

    public async Task ChangeSessionAsync()
    {
        if (disposed) return;
        if (State.Session == SessionState.Disconnected) { await RestoreAsync(); return; }
        if (!await operationGate.WaitAsync(0, stopping.Token)) return;
        try
        {
            await CancelRefreshAsync();
            var client = await ConnectAsync();
            var before = State;
            if (before.Session == SessionState.Connected)
            {
                Publish(before with { Session = SessionState.SigningOut });
                try { await client.RequestAsync("account/logout", null, stopping.Token); }
                catch
                {
                    await ReadAccountAsync(await ConnectAsync());
                    throw;
                }
                Interlocked.Increment(ref sessionVersion);
                Publish(new(SessionState.SignedOut, Limits.Empty, SessionNotice.SignIn));
            }
            else if (loginId is { } pending)
            {
                await client.RequestAsync("account/login/cancel", new { loginId = pending }, stopping.Token);
                loginId = null;
                Publish(new(SessionState.SignedOut, Limits.Empty, SessionNotice.SignIn));
            }
            else
            {
                var result = await client.RequestAsync("account/login/start", new { type = "chatgpt" }, stopping.Token);
                if (result.ValueKind != JsonValueKind.Object ||
                    !result.TryGetProperty("loginId", out var id) || id.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(id.GetString()))
                    throw new InvalidDataException("Inicio de sesión no válido.");
                string pendingId = id.GetString()!;
                try
                {
                    if (!result.TryGetProperty("authUrl", out var text) || text.ValueKind != JsonValueKind.String ||
                        !LoginUrl.TryValidate(text.GetString(), out var url))
                        throw new InvalidDataException("URL de inicio de sesión no válida.");
                    // Set while holding operationGate so a fast completion cannot race browser startup.
                    loginId = pendingId;
                    await browser(url!);
                    Publish(new(SessionState.SigningIn, Limits.Empty, SessionNotice.LoginPending));
                }
                catch
                {
                    loginId = null;
                    try { await client.RequestAsync("account/login/cancel", new { loginId = pendingId }, stopping.Token); }
                    catch (Exception error) { log.Write("login_cancel_failed", error: error); }
                    Publish(new(SessionState.SignedOut, Limits.Empty, SessionNotice.BrowserFailed));
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception error)
        {
            log.Write("session_action_failed", error: error);
            var current = State;
            Publish(current with { Session = current.Session is SessionState.SigningOut ? SessionState.Disconnected :
                current.Session is SessionState.SigningIn ? SessionState.SignedOut : current.Session,
                Notice = current.Notice == SessionNotice.BrowserFailed ? current.Notice :
                    error is CodexRpcException {CredentialStorageFailure:true} ?
                    SessionNotice.CredentialStoreUnavailable : SessionNotice.LoginFailed });
        }
        finally { operationGate.Release(); }
        if (State.Session == SessionState.Connected) await RefreshAsync(true);
    }

    private void Notification(ICodexClient source, string method, JsonElement args)
    {
        if (disposed || source != connection) return;
        try
        {
            if (method == "account/rateLimits/updated")
            {
                var parsed = QuotaParser.Parse(args);
                if (parsed.Kind == ParseKind.Valid && parsed.Limits == State.Limits with { Stale = false }) return;
                lock (sync)
                {
                    if (notificationSeen && clock.GetElapsedTime(lastNotification) < TimeSpan.FromSeconds(5)) return;
                    notificationSeen = true; lastNotification = clock.GetTimestamp();
                }
                Observe(RefreshAsync(true));
            }
            else if (method == "account/updated")
            {
                lock (sync)
                {
                    if (accountNotificationSeen && clock.GetElapsedTime(lastAccountNotification) < TimeSpan.FromSeconds(5)) return;
                    accountNotificationSeen = true; lastAccountNotification = clock.GetTimestamp();
                }
                Observe(RecheckAsync(source));
            }
            else if (method == "account/login/completed")
                Observe(CompleteLoginAsync(source, args));
        }
        catch (Exception error) { log.Write("notification_invalid", method, error); }
    }

    private async Task RecheckAsync(ICodexClient source)
    {
        await operationGate.WaitAsync(stopping.Token);
        try
        {
            if (source != connection || disposed) return;
            if (State.Session==SessionState.SigningIn && loginId is not null) return;
            await CancelRefreshAsync();
            await ReadAccountAsync(source);
        }
        finally { operationGate.Release(); }
        if (State.Session == SessionState.Connected) await RefreshAsync(true);
    }

    private async Task CompleteLoginAsync(ICodexClient source, JsonElement args)
    {
        await operationGate.WaitAsync(stopping.Token);
        try
        {
            if (source != connection || disposed || loginId is null || args.ValueKind != JsonValueKind.Object ||
                !args.TryGetProperty("loginId", out var id) || id.ValueKind != JsonValueKind.String ||
                id.GetString() != loginId) return;
            loginId = null;
            if (!args.TryGetProperty("success", out var success) || success.ValueKind is not JsonValueKind.True)
            {
                bool storage=args.TryGetProperty("error",out var error) && error.ValueKind==JsonValueKind.String &&
                    CodexRpcException.IsCredentialStorageFailure(error.GetString()!);
                log.Write(storage?"login_persistence_failed":"login_completed_failed","account/login/completed");
                Publish(new(SessionState.SignedOut, Limits.Empty,
                    storage?SessionNotice.CredentialStoreUnavailable:SessionNotice.LoginFailed));
            }
            else await ReadAccountAsync(source);
        }
        finally { operationGate.Release(); }
        if (State.Session == SessionState.Connected) await RefreshAsync(true);
    }

    private async void Observe(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception error) { log.Write("session_notification_failed", error: error); }
    }

    public async ValueTask DisposeAsync()
    {
        lock (sync) { if (disposed) return; disposed = true; }
        stopping.Cancel();
        await CancelRefreshAsync();
        await connectionGate.WaitAsync();
        try { if (connection is { } client) { connection = null; await client.DisposeAsync(); } }
        finally { connectionGate.Release(); }
        Publish(State with { Session = SessionState.Stopping });
    }
}
