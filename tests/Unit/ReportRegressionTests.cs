using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using UsageViewer.Core;
using Xunit;

namespace UsageViewer.Core.Tests;

public partial class SessionTests
{
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while(!condition()) await Task.Delay(1,deadline.Token);
    }
    [Fact] public async Task ConnectionGateSharesAnActuallyPendingServerStart()
    {
        var release=new TaskCompletionSource<ICodexClient>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory=new Factory(Client()){Create=()=>release.Task};
        await using var controller=new SessionController(factory,_=>Task.CompletedTask);
        var calls=Enumerable.Range(0,20).Select(_=>controller.ConnectAsync()).ToArray();
        try { Assert.Equal(1,factory.Starts); }
        finally { release.SetResult(Client()); }
        await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1,factory.Starts);
        Assert.All(calls,call=>Assert.Same(calls[0].Result,call.Result));
    }
    [Fact] public async Task EveryStartupBackoffStepIsAppliedAndCapped()
    {
        var time=new FakeTimeProvider();var factory=new Factory(Client()){Failures=100};
        await using var controller=new SessionController(factory,_=>Task.CompletedTask,clock:time);
        await controller.RestoreAsync();
        foreach(int seconds in new[]{5,15,45,135,300,300})
        {
            int before=factory.Starts;
            time.Advance(TimeSpan.FromSeconds(seconds-1));await controller.TickAsync();Assert.Equal(before,factory.Starts);
            time.Advance(TimeSpan.FromSeconds(1));await controller.TickAsync();Assert.Equal(before+1,factory.Starts);
        }
    }
    [Theory] [InlineData(0,SessionNotice.ServerMissing)] [InlineData(1,SessionNotice.ServerIntegrity)]
    [InlineData(2,SessionNotice.CredentialStoreUnavailable)]
    public async Task StartupFailureNoticesDistinguishMissingTamperedAndSecureStore(int kind,SessionNotice notice)
    {
        Exception error=kind switch{0=>new FileNotFoundException(),1=>new InvalidDataException(),_=>new CodexRpcException(401,"authentication keyring failed")};
        var factory=new Factory(Client()){Create=()=>Task.FromException<ICodexClient>(error)};
        await using var controller=new SessionController(factory,_=>Task.CompletedTask);
        await controller.RestoreAsync();Assert.Equal(notice,controller.State.Notice);
        Assert.Equal(SessionState.Disconnected,controller.State.Session);
    }
    [Fact] public async Task InvalidQuotaNoticeSurvivesRepeatedFailuresWithoutTokenRefresh()
    {
        var client=Client();var normal=client.Handle;
        await using var controller=new SessionController(new Factory(client),_=>Task.CompletedTask);
        await controller.RestoreAsync();int accounts=client.Methods.Count(x=>x=="account/read");
        client.Handle=(method,args,token)=>method=="account/rateLimits/read"?Task.FromResult(Json("{}")):normal(method,args,token);
        for(int i=0;i<5;i++)await controller.RefreshAsync(true);
        Assert.Equal(SessionNotice.InvalidQuotas,controller.State.Notice);
        Assert.Equal(accounts,client.Methods.Count(x=>x=="account/read"));
        Assert.NotNull(controller.State.Limits.Short);Assert.True(controller.State.Limits.Stale);
    }
    [Fact] public async Task ThirdNetworkFailureRechecksAccountOnceWithoutRefreshingToken()
    {
        var client=Client();var normal=client.Handle;int reads=0;bool refreshed=false;
        await using var controller=new SessionController(new Factory(client),_=>Task.CompletedTask);
        await controller.RestoreAsync();
        client.Handle=(method,args,token)=>
        {
            if(method=="account/read") { reads++;refreshed|=JsonSerializer.SerializeToElement(args).GetProperty("refreshToken").GetBoolean();return Task.FromResult(SignedIn); }
            return Task.FromException<JsonElement>(new IOException());
        };
        await controller.RefreshAsync(true);await controller.RefreshAsync(true);Assert.Equal(0,reads);
        await controller.RefreshAsync(true);Assert.Equal(1,reads);Assert.False(refreshed);
        await controller.RefreshAsync(true);Assert.Equal(1,reads);Assert.Equal(SessionNotice.Stale,controller.State.Notice);
    }
    [Fact] public async Task ForcedRefreshDuringTheSecondPassIsNotLost()
    {
        var client=Client();var normal=client.Handle;
        await using var controller=new SessionController(new Factory(client),_=>Task.CompletedTask);
        await controller.RestoreAsync();
        var gates=Enumerable.Range(0,3).Select(_=>new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        int calls=0;
        client.Handle=async(method,args,token)=>
        {
            if(method=="account/rateLimits/read") {int index=Interlocked.Increment(ref calls)-1;await gates[index].Task.WaitAsync(token);}
            return await normal(method,args,token);
        };
        var first=controller.RefreshAsync(true);await WaitUntilAsync(()=>calls==1);
        _=controller.RefreshAsync(true);gates[0].SetResult();await WaitUntilAsync(()=>calls==2);
        _=controller.RefreshAsync(true);gates[1].SetResult();
        try {await WaitUntilAsync(()=>calls==3);}
        finally {gates[2].TrySetResult();}
        await first;Assert.Equal(3,calls);
    }
    private static FakeClient LoginClient()=>new()
    { Handle=(method,_,_)=>Task.FromResult(method switch
        {"account/read"=>Json("""{"account":null}"""),"account/login/start"=>Json("""{"loginId":"pending","authUrl":"https://auth.openai.com/authorize"}"""),_=>Json("{}")}) };
    [Fact] public async Task FailedCancellationDoesNotPreventTheNextLogin()
    {
        var client=LoginClient();var normal=client.Handle;
        client.Handle=(method,args,token)=>method=="account/login/cancel"?Task.FromException<JsonElement>(new CodexRpcException(-32602,"unknown login")):normal(method,args,token);
        await using var controller=new SessionController(new Factory(client),_=>Task.CompletedTask);
        await controller.RestoreAsync();await controller.ChangeSessionAsync();await controller.ChangeSessionAsync();
        Assert.Equal(SessionState.SignedOut,controller.State.Session);
        await controller.ChangeSessionAsync();Assert.Equal(SessionState.SigningIn,controller.State.Session);
        Assert.Equal(2,client.Methods.Count(x=>x=="account/login/start"));
    }
    [Fact] public async Task PendingLoginIgnoresMalformedForeignAndIncompleteNotificationsThenCompletes()
    {
        var client=LoginClient();
        await using var controller=new SessionController(new Factory(client),_=>Task.CompletedTask);
        await controller.RestoreAsync();await controller.ChangeSessionAsync();
        foreach(string json in new[]{"null","""{"loginId":42,"success":true}""","""{"loginId":"other","success":true}""","""{"loginId":"pending"}"""})
        {
            await controller.HandleNotificationAsync(client,"account/login/completed",Json(json));
            Assert.Equal(SessionState.SigningIn,controller.State.Session);
        }
        client.Handle=(method,_,_)=>Task.FromResult(method=="account/read"?SignedIn:Json("""{"rateLimits":null}"""));
        await controller.HandleNotificationAsync(client,"account/login/completed",Json("""{"loginId":"pending","success":true}"""));
        Assert.Equal(SessionState.Connected,controller.State.Session);
        Assert.Equal("test@example.invalid",controller.State.Email);
    }
    [Fact] public async Task ChangedQuotaNotificationsAreLimitedToOnePerFiveSeconds()
    {
        var time=new FakeTimeProvider();var client=Client();
        await using var controller=new SessionController(new Factory(client),_=>Task.CompletedTask,clock:time);
        await controller.RestoreAsync();int before=client.Methods.Count(x=>x=="account/rateLimits/read");
        void Notify(int used)=>client.Notify("account/rateLimits/updated",JsonSerializer.SerializeToElement(new { rateLimits=new { primary=new { usedPercent=used,windowDurationMins=300 } } }));
        Notify(50);await controller.RefreshAsync();
        for(int i=51;i<90;i++)Notify(i);
        await controller.RefreshAsync();Assert.Equal(before+1,client.Methods.Count(x=>x=="account/rateLimits/read"));
        time.Advance(TimeSpan.FromSeconds(5));Notify(91);await controller.RefreshAsync();
        Assert.Equal(before+2,client.Methods.Count(x=>x=="account/rateLimits/read"));
    }
    [Fact] public async Task ManualRefreshBypassesTheFailureBackoff()
    {
        var client=Client();var normal=client.Handle;var time=new FakeTimeProvider();
        await using var controller=new SessionController(new Factory(client),_=>Task.CompletedTask,clock:time);
        await controller.RestoreAsync();
        client.Handle=(_,_,_)=>Task.FromException<JsonElement>(new IOException());await controller.RefreshAsync(true);
        int before=client.Methods.Count;await controller.RefreshAsync();Assert.Equal(before,client.Methods.Count);
        client.Handle=normal;await controller.RefreshAsync(true);Assert.False(controller.State.Limits.Stale);
    }
    [Fact] public async Task DeadServerDuringLoginRecoversAndOldNotificationsAreIgnored()
    {
        var first=LoginClient();var second=Client();var time=new FakeTimeProvider();var factory=new Factory(first);
        await using var controller=new SessionController(factory,_=>Task.CompletedTask,clock:time);
        await controller.RestoreAsync();await controller.ChangeSessionAsync();first.Alive=false;
        await controller.TickAsync();Assert.Equal(SessionState.Disconnected,controller.State.Session);
        factory.Create=()=>Task.FromResult<ICodexClient>(second);time.Advance(TimeSpan.FromSeconds(5));await controller.TickAsync();
        Assert.Equal(SessionState.Connected,controller.State.Session);int reads=second.Methods.Count;
        first.Notify("account/updated",Json("{}"));Assert.Equal(reads,second.Methods.Count);
    }
    [Fact] public void OverloadAndStorageErrorsAreNotMisclassifiedAsExpiredSessions()
    {
        Assert.False(new CodexRpcException(-32001,"authentication service busy").AuthenticationFailure);
        Assert.False(new CodexRpcException(401,"authentication keyring failure").AuthenticationFailure);
        Assert.True(new CodexRpcException(403,"unauthorized").AuthenticationFailure);
    }
    [Fact] public async Task AuthenticationErrorFromAccountNotificationRecoversWithoutNewLogin()
    {
        var client=Client();var normal=client.Handle;var time=new FakeTimeProvider();
        await using var controller=new SessionController(new Factory(client),_=>Task.CompletedTask,clock:time);
        await controller.RestoreAsync();
        client.Handle=(_,_,_)=>Task.FromException<JsonElement>(new CodexRpcException(401,"unauthorized"));
        await controller.HandleNotificationAsync(client,"account/updated",Json("{}"));
        Assert.Equal(SessionState.SignedOut,controller.State.Session);
        client.Handle=normal;time.Advance(TimeSpan.FromSeconds(5));await controller.TickAsync();
        Assert.Equal(SessionState.Connected,controller.State.Session);Assert.DoesNotContain("account/login/start",client.Methods);
    }
    [Fact] public async Task CompletedLoginWithTemporaryAccountFailureLeavesPendingStateAndRetries()
    {
        var client=LoginClient();var time=new FakeTimeProvider();
        await using var controller=new SessionController(new Factory(client),_=>Task.CompletedTask,clock:time);
        await controller.RestoreAsync();await controller.ChangeSessionAsync();
        client.Handle=(_,_,_)=>Task.FromException<JsonElement>(new IOException());
        await controller.HandleNotificationAsync(client,"account/login/completed",Json("""{"loginId":"pending","success":true}"""));
        Assert.Equal(SessionState.Disconnected,controller.State.Session);
        client.Handle=Client().Handle;time.Advance(TimeSpan.FromSeconds(5));await controller.TickAsync();
        Assert.Equal(SessionState.Connected,controller.State.Session);
    }
}

public class ParserReportTests
{
    [Theory] [InlineData("null")] [InlineData("missing")]
    public void DurationlessWindowDoesNotDiscardTheOtherCycle(string duration)
    {
        string secondary=duration=="missing"?"{}":"\"windowDurationMins\":"+duration;
        if(duration!="missing")secondary="{"+secondary+"}";
        using var doc=JsonDocument.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":40,\"windowDurationMins\":300},\"secondary\":"+secondary+"}}");
        var parsed=QuotaParser.Parse(doc.RootElement);
        Assert.Equal(ParseKind.Valid,parsed.Kind);Assert.Equal(.4,parsed.Limits.Short!.Used);Assert.Null(parsed.Limits.Weekly);
    }
    [Fact] public void ServerArgumentsRequireEncryptedKeyringAndContractVersionMatchesPins()
    {
        var args=CodexContract.ServerArguments();
        Assert.Contains("cli_auth_credentials_store=\"keyring\"",args);Assert.Contains("features.secret_auth_storage=true",args);
        Assert.DoesNotContain(args,a=>a.Contains("\"auto\"")||a.Contains("\"file\""));
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../"));
        using var pins=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"dependencies.json")));
        Assert.Equal(CodexContract.Version,pins.RootElement.GetProperty("codex").GetProperty("version").GetString());
    }
}
