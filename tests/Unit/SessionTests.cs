using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using UsageViewer.Core;
using Xunit;

namespace UsageViewer.Core.Tests;

public class SessionTests
{
    private static readonly JsonElement SignedIn=Json("""{"account":{"type":"chatgpt","email":"test@example.invalid"}}""");
    private static JsonElement Json(string value)=>JsonDocument.Parse(value).RootElement.Clone();
    private static FakeClient Client()=>new()
    {
        Handle=(method,_,_)=>Task.FromResult(method=="account/read"?SignedIn:
            method=="account/rateLimits/read"?Json("""{"rateLimits":{"limitId":"codex","primary":{"usedPercent":40,"windowDurationMins":300,"resetsAt":1900000000}}}"""):
            Json("{}"))
    };
    [Fact] public async Task StartupFailuresRecoverWithoutAskingUserToLogIn()
    {
        var time=new FakeTimeProvider();
        var factory=new Factory(Client()) { Failures=1 };
        await using var controller=new SessionController(factory,_=>Task.CompletedTask,clock:time);
        await controller.RestoreAsync();
        Assert.Equal(SessionState.Disconnected,controller.State.Session);
        time.Advance(TimeSpan.FromSeconds(4));await controller.TickAsync();Assert.Equal(1,factory.Starts);
        time.Advance(TimeSpan.FromSeconds(1));await controller.TickAsync();
        Assert.Equal(2,factory.Starts);Assert.Equal(SessionState.Connected,controller.State.Session);
    }
    [Fact] public async Task ConcurrentRestoreSharesOneServer()
    {
        var factory=new Factory(Client());
        await using var controller=new SessionController(factory,_=>Task.CompletedTask);
        await Task.WhenAll(Enumerable.Range(0,20).Select(_=>controller.RestoreAsync()));
        Assert.Equal(1,factory.Starts);
    }
    [Fact] public async Task ConcurrentReconnectAndLogoutNeverStartTwoChildren()
    {
        var first=Client();var second=Client();var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory=new Factory(first);
        await using var controller=new SessionController(factory,_=>Task.CompletedTask);
        await controller.RestoreAsync();
        first.Alive=false;
        factory.Create=async()=> { started.SetResult();await release.Task;return second; };
        var refresh=controller.RefreshAsync(true);await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var logout=controller.ChangeSessionAsync();
        release.SetResult();
        await Task.WhenAll(refresh,logout).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2,factory.Starts);Assert.Equal(1,first.Disposals);
        Assert.Equal(SessionState.SignedOut,controller.State.Session);
    }
    [Fact] public async Task InvalidQuotaPreservesLastGoodReadingAndExplainsFailure()
    {
        var client=Client();var factory=new Factory(client);
        await using var controller=new SessionController(factory,_=>Task.CompletedTask);
        await controller.RestoreAsync();var before=controller.State.Limits;
        client.Handle=(method,_,_)=>Task.FromResult(method=="account/read"?SignedIn:Json("""{"unexpected":true}"""));
        await controller.RefreshAsync(true);
        Assert.Equal(before.Short,controller.State.Limits.Short);
        Assert.True(controller.State.Limits.Stale);
        Assert.Equal(SessionNotice.InvalidQuotas,controller.State.Notice);
    }
    [Fact] public async Task AuthenticationFailureRechecksAccountAndClearsOldQuota()
    {
        var client=Client();
        await using var controller=new SessionController(new Factory(client),_=>Task.CompletedTask);
        await controller.RestoreAsync();
        client.Handle=(method,_,_)=>method=="account/read"?Task.FromResult(Json("""{"account":null}""")):
            Task.FromException<JsonElement>(new CodexRpcException(401,"unauthorized"));
        await controller.RefreshAsync(true);
        Assert.Equal(SessionState.SignedOut,controller.State.Session);
        Assert.Equal(Limits.Empty,controller.State.Limits);
    }
    [Fact] public async Task LogoutCancelsInFlightRefreshWithoutLeakingOldQuota()
    {
        var client=Client();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var controller=new SessionController(new Factory(client),_=>Task.CompletedTask);
        await controller.RestoreAsync();
        client.Handle=async(method,_,cancellation)=>
        {
            if(method=="account/rateLimits/read")
            { entered.SetResult();await Task.Delay(Timeout.InfiniteTimeSpan,cancellation); }
            return Json("{}");
        };
        var refresh=controller.RefreshAsync(true);await entered.Task;
        await controller.ChangeSessionAsync().WaitAsync(TimeSpan.FromSeconds(3));
        await refresh;
        Assert.Equal(SessionState.SignedOut,controller.State.Session);
        Assert.Equal(Limits.Empty,controller.State.Limits);
    }
    [Fact] public async Task BrowserFailureCancelsPendingLogin()
    {
        var client=Client();
        client.Handle=(method,_,_)=>Task.FromResult(method switch
        {
            "account/read"=>Json("""{"account":null}"""),
            "account/login/start"=>Json("""{"loginId":"test-login","authUrl":"https://auth.openai.com/authorize"}"""),
            _=>Json("{}")
        });
        await using var controller=new SessionController(new Factory(client),_=>throw new IOException());
        await controller.RestoreAsync();await controller.ChangeSessionAsync();
        Assert.Contains("account/login/cancel",client.Methods);
        Assert.Equal(SessionState.SignedOut,controller.State.Session);
        Assert.Equal(SessionNotice.BrowserFailed,controller.State.Notice);
    }
    [Theory]
    [InlineData(null)] [InlineData("http://auth.openai.com/authorize")]
    [InlineData("https://auth.openai.com.evil.invalid/")]
    [InlineData("https://name@auth.openai.com/")]
    [InlineData("https://auth.openai.com:444/")]
    public void UntrustedLoginUrlsAreRejected(string? text)=>Assert.False(LoginUrl.TryValidate(text,out _));
    [Theory] [InlineData("https://auth.openai.com/authorize")] [InlineData("https://chatgpt.com/oauth/authorize")]
    public void OfficialLoginHostsAreAccepted(string text)=>Assert.True(LoginUrl.TryValidate(text,out _));
    [Fact] public async Task NullLoginUrlIsContainedAndCanceled()
    {
        var client=Client();
        client.Handle=(method,_,_)=>Task.FromResult(method switch
        {
            "account/read"=>Json("""{"account":null}"""),
            "account/login/start"=>Json("""{"loginId":"test-login","authUrl":null}"""),
            _=>Json("{}")
        });
        await using var controller=new SessionController(new Factory(client),_=>throw new Exception("must not open"));
        await controller.RestoreAsync();await controller.ChangeSessionAsync();
        Assert.Contains("account/login/cancel",client.Methods);
        Assert.Equal(SessionState.SignedOut,controller.State.Session);
    }
    [Fact] public async Task MalformedLoginNotificationsDoNotCrashOrChangeSession()
    {
        var client=Client();
        await using var controller=new SessionController(new Factory(client),_=>Task.CompletedTask);
        await controller.RestoreAsync();
        client.Notify("account/login/completed",Json("null"));
        client.Notify("account/login/completed",Json("""{"loginId":42,"success":true}"""));
        Assert.Equal(SessionState.Connected,controller.State.Session);
    }
    [Fact] public async Task CredentialMigrationWaitsForConfirmedProtectedAccount()
    {
        bool legacy=true;var client=Client();
        client.Handle=(method,_,_)=>Task.FromResult(Json("""{"account":null}"""));
        await using var controller=new SessionController(new Factory(client),_=>Task.CompletedTask,
            legacyCredentials:()=>legacy,cleanupLegacy:()=>legacy=false);
        await controller.RestoreAsync();
        Assert.True(legacy);Assert.Equal(SessionNotice.Migration,controller.State.Notice);
        client.Handle=(method,_,_)=>Task.FromResult(method=="account/read"?SignedIn:Json("""{"rateLimits":null}"""));
        await controller.RestoreAsync();
        Assert.False(legacy);Assert.Equal(SessionState.Connected,controller.State.Session);
    }
    [Fact] public async Task BurstOfIdenticalNotificationsCannotCreateRefreshLoop()
    {
        var client=Client();
        await using var controller=new SessionController(new Factory(client),_=>Task.CompletedTask);
        await controller.RestoreAsync();
        int before=client.Methods.Count(x=>x=="account/rateLimits/read");
        var snapshot=Json("""{"rateLimits":{"limitId":"codex","primary":{"usedPercent":40,"windowDurationMins":300,"resetsAt":1900000000}}}""");
        for(int i=0;i<100;i++) client.Notify("account/rateLimits/updated",snapshot);
        await controller.RefreshAsync();
        Assert.Equal(before,client.Methods.Count(x=>x=="account/rateLimits/read"));
    }
    [Fact] public async Task RepeatedAccountNotificationsAreCoalesced()
    {
        var client=Client();
        await using var controller=new SessionController(new Factory(client),_=>Task.CompletedTask);
        await controller.RestoreAsync();
        int before=client.Methods.Count(x=>x=="account/read");
        for(int i=0;i<100;i++)client.Notify("account/updated",Json("{}"));
        await controller.RefreshAsync();
        Assert.InRange(client.Methods.Count(x=>x=="account/read")-before,0,1);
    }
    [Fact] public async Task FailedLogoutRetainsConfirmedAccountAndItsLastValidQuota()
    {
        var client=Client();
        var normal=client.Handle;
        client.Handle=(method,args,cancellation)=>method=="account/logout"
            ?Task.FromException<JsonElement>(new CodexRpcException(-32603,"temporary failure"))
            :normal(method,args,cancellation);
        await using var controller=new SessionController(new Factory(client),_=>Task.CompletedTask);
        await controller.RestoreAsync();
        var connected=new List<ViewState>();
        controller.Changed+=state=>{if(state.Session==SessionState.Connected)connected.Add(state);};
        await controller.ChangeSessionAsync();
        Assert.Equal(SessionState.Connected,controller.State.Session);
        Assert.All(connected,state=>Assert.NotNull(state.Limits.Short));
    }
    [Fact] public async Task InitialAuthenticationFailureRequiresSignInRatherThanNetworkRetry()
    {
        var client=Client();
        client.Handle=(_,_,_)=>Task.FromException<JsonElement>(new CodexRpcException(401,"authentication required"));
        await using var controller=new SessionController(new Factory(client),_=>Task.CompletedTask);
        await controller.RestoreAsync();
        Assert.Equal(SessionState.SignedOut,controller.State.Session);
        Assert.Equal(SessionNotice.AuthenticationRequired,controller.State.Notice);
    }
    private sealed class Factory(FakeClient client):ICodexClientFactory
    {
        public int Starts,Failures;public Func<Task<ICodexClient>>? Create;
        public async Task<ICodexClient> StartAsync(CancellationToken cancellation)
        {
            Interlocked.Increment(ref Starts);
            if(Failures-->0) throw new IOException();
            return Create is null?client:await Create();
        }
    }
    private sealed class FakeClient:ICodexClient
    {
        public bool Alive=true;public int Disposals;
        public bool IsAlive=>Alive;
        public required Func<string,object?,CancellationToken,Task<JsonElement>> Handle;
        public ConcurrentQueue<string> Methods=new();
        public event Action<string,JsonElement>? Notification;
        public Task<JsonElement> RequestAsync(string method,object? parameters,CancellationToken cancellation)
        { Methods.Enqueue(method);return Handle(method,parameters,cancellation); }
        public void Notify(string method,JsonElement value)=>Notification?.Invoke(method,value);
        public ValueTask DisposeAsync() { Alive=false;Interlocked.Increment(ref Disposals);return ValueTask.CompletedTask; }
    }
}
