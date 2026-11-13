using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using UsageViewer.Core;
using Xunit;

namespace UsageViewer.Core.Tests;

public class RpcTests
{
    [Fact] public async Task OversizedDiagnosticsAreDiscardedWithoutStoppingTheDrain()
    {
        using var input=new MemoryStream(Encoding.UTF8.GetBytes(new string('x',20000)+"\nnetwork timeout\n"));
        var lines=new List<string>();
        await foreach(var line in BoundedLines.ReadAsync(input,4096,default,skipOversized:true)) lines.Add(line);
        Assert.Equal(new[]{"network timeout"},lines);
    }
    [Fact] public async Task CorrelationMalformedLinesAndUnknownIds()
    {
        await using var pipe=await Peer.Create();
        var request=pipe.Rpc.RequestAsync("account/read",null,default);
        int id=await pipe.ReadId();
        await pipe.Writer.WriteLineAsync("not JSON");
        await pipe.Writer.WriteLineAsync("[]");
        await pipe.Writer.WriteLineAsync("""{"id":999,"result":{}}""");
        await pipe.Writer.WriteLineAsync(JsonSerializer.Serialize(new { id,result=new { account=(object?)null } }));
        Assert.Equal(JsonValueKind.Null,(await request.WaitAsync(TimeSpan.FromSeconds(3))).GetProperty("account").ValueKind);
    }
    [Fact] public async Task RpcErrorsRetainCodeAndMessage()
    {
        await using var pipe=await Peer.Create();
        var request=pipe.Rpc.RequestAsync("account/read",null,default);
        int id=await pipe.ReadId();
        await pipe.Writer.WriteLineAsync(JsonSerializer.Serialize(new { id,error=new { code=401,message="unauthorized" } }));
        var error=await Assert.ThrowsAsync<CodexRpcException>(()=>request);
        Assert.Equal(401,error.Code);Assert.Equal("unauthorized",error.RemoteMessage);Assert.True(error.AuthenticationFailure);
    }
    [Fact] public async Task ServerRequestCannotCompleteClientRequestWithSameId()
    {
        await using var pipe=await Peer.Create();
        var request=pipe.Rpc.RequestAsync("account/read",null,default);
        int id=await pipe.ReadId();
        await pipe.Writer.WriteLineAsync(JsonSerializer.Serialize(new { id,method="unsupported",@params=new{} }));
        using var reply=JsonDocument.Parse((await pipe.Reader.ReadLineAsync())!);
        Assert.Equal(-32601,reply.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.False(request.IsCompleted);
        await pipe.Writer.WriteLineAsync(JsonSerializer.Serialize(new { id,result=new{} }));
        await request;
    }
    [Fact] public async Task NotificationIsDeliveredAndSubscriberFailureDoesNotKillReader()
    {
        await using var pipe=await Peer.Create();
        var delivered=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        pipe.Rpc.Notification+=(method,args)=> { delivered.TrySetResult(method);throw new InvalidOperationException(); };
        await pipe.Writer.WriteLineAsync("""{"method":"account/updated","params":{"authMode":null}}""");
        Assert.Equal("account/updated",await delivered.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        var request=pipe.Rpc.RequestAsync("account/read",null,default);int id=await pipe.ReadId();
        await pipe.Writer.WriteLineAsync(JsonSerializer.Serialize(new { id,result=new{} }));await request;
    }
    [Fact] public async Task DeadlineClosesTransportAndFailsPendingRequests()
    {
        var time=new FakeTimeProvider();
        await using var pipe=await Peer.Create(time);
        var a=pipe.Rpc.RequestAsync("account/read",null,default);await pipe.ReadId();
        var b=pipe.Rpc.RequestAsync("account/rateLimits/read",null,default);await pipe.ReadId();
        time.Advance(TimeSpan.FromSeconds(25));
        await Assert.ThrowsAnyAsync<Exception>(()=>a);
        await Assert.ThrowsAnyAsync<Exception>(()=>b);
        Assert.False(pipe.Rpc.IsAlive);
        await Assert.ThrowsAsync<IOException>(()=>pipe.Rpc.RequestAsync("account/read",null,default));
    }
    [Fact] public async Task CallerCancellationDoesNotPoisonTransport()
    {
        await using var pipe=await Peer.Create();
        using var cancellation=new CancellationTokenSource();
        var request=pipe.Rpc.RequestAsync("account/read",null,cancellation.Token);await pipe.ReadId();
        cancellation.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>request);
        Assert.True(pipe.Rpc.IsAlive);
    }
    [Fact] public async Task EndOfStreamFailsPendingAndFutureRequests()
    {
        await using var pipe=await Peer.Create();
        var request=pipe.Rpc.RequestAsync("account/read",null,default);await pipe.ReadId();
        pipe.Client.Dispose();
        await Assert.ThrowsAnyAsync<IOException>(()=>request.WaitAsync(TimeSpan.FromSeconds(3)));
        await Assert.ThrowsAsync<IOException>(()=>pipe.Rpc.RequestAsync("account/read",null,default));
    }
    [Fact] public async Task OversizedUnterminatedMessageIsRejected()
    {
        using var stream=new MemoryStream(new byte[JsonRpcClient.MaximumMessageBytes+1]);
        await Assert.ThrowsAsync<InvalidDataException>(async()=>
        { await foreach(var _ in BoundedLines.ReadAsync(stream,JsonRpcClient.MaximumMessageBytes,default)) { } });
    }
    [Fact] public async Task ConcurrentWritesAreCompleteJsonMessages()
    {
        await using var pipe=await Peer.Create();
        var requests=Enumerable.Range(0,20).Select(_=>pipe.Rpc.RequestAsync("account/read",null,default)).ToArray();
        for(int i=0;i<20;i++)
        {
            int id=await pipe.ReadId();
            await pipe.Writer.WriteLineAsync(JsonSerializer.Serialize(new { id,result=new{} }));
        }
        await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(5));
    }
    private sealed class Peer: IAsyncDisposable
    {
        public required NamedPipeServerStream Server;public required NamedPipeClientStream Client;
        public required StreamReader Reader;public required StreamWriter Writer;public required JsonRpcClient Rpc;
        public static async Task<Peer> Create(TimeProvider? time=null)
        {
            string name="usage-viewer-tests-"+Guid.NewGuid().ToString("N");
            var server=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
            var client=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);
            await Task.WhenAll(server.WaitForConnectionAsync(),client.ConnectAsync());
            return new()
            {
                Server=server,Client=client,Rpc=new(server,server,clock:time),
                Reader=new(client,new UTF8Encoding(false),false,1024,true),
                Writer=new(client,new UTF8Encoding(false),1024,true) { AutoFlush=true }
            };
        }
        public async Task<int> ReadId()
        {
            string line=(await Reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)))!;
            using var doc=JsonDocument.Parse(line);return doc.RootElement.GetProperty("id").GetInt32();
        }
        public async ValueTask DisposeAsync()
        {
            await Rpc.DisposeAsync();
            try { await Writer.DisposeAsync(); } catch(IOException) { } catch(ObjectDisposedException) { }
            Reader.Dispose();Client.Dispose();Server.Dispose();
        }
    }
}
