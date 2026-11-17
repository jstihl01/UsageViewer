using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using UsageViewer.Core;
using UsageViewer.Windows;
using Xunit;
using Microsoft.Extensions.Time.Testing;

namespace UsageViewer.Integration.Tests;

public class ProcessBoundaryTests
{
    private sealed class AcceptFixture:IBinaryVerifier {public bool Verify(string path)=>true;}
    private static string Fixture=>Environment.GetEnvironmentVariable("USAGEVIEWER_PROCESS_FIXTURE")??throw new InvalidOperationException("Falta el fixture del build.");
    private static string Root()=>Path.Combine(Path.GetTempPath(),"UsageViewer-boundary-"+Guid.NewGuid().ToString("N"));
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static Task<CodexProcessClient> Start(string profile,TimeSpan? timeout=null,TimeProvider? clock=null)=>CodexProcessClient.StartVerifiedAsync(
        Fixture,profile,Hash(Fixture),"test",NullLog.Instance,default,new AcceptFixture(),timeout,clock);
    [Fact] public async Task CorrectHashStillRejectsAnInvalidAuthenticodeSignature()
    {
        string root=Root();Directory.CreateDirectory(root);string path=Path.Combine(root,"unsigned.exe");File.WriteAllText(path,"not signed");
        try {await Assert.ThrowsAsync<InvalidDataException>(()=>CodexProcessClient.StartAsync(path,Path.Combine(root,"profile"),Hash(path),"test",NullLog.Instance,default));}
        finally {Directory.Delete(root,true);}
    }
    [Fact] public async Task ActualProcessClientSanitizesEnvironmentAndUsesPinnedServerArguments()
    {
        string root=Root();string? codex=Environment.GetEnvironmentVariable("CODEX_API_KEY"),openai=Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("CODEX_API_KEY","synthetic");Environment.SetEnvironmentVariable("OPENAI_API_KEY","synthetic");
            await using var client=await Start(root);var result=await client.RequestAsync("test/environment",null,default);
            Assert.Equal(root,result.GetProperty("profile").GetString());Assert.False(result.GetProperty("hasCodexKey").GetBoolean());Assert.False(result.GetProperty("hasOpenAiKey").GetBoolean());
            Assert.Equal(CodexContract.ServerArguments(),result.GetProperty("arguments").EnumerateArray().Select(a=>a.GetString()!).ToArray());
        }
        finally {Environment.SetEnvironmentVariable("CODEX_API_KEY",codex);Environment.SetEnvironmentVariable("OPENAI_API_KEY",openai);if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact] public async Task ActualProcessClientAssignsTheServerAndItsDescendantsToItsJob()
    {
        string root=Root();CodexProcessClient? client=null;Process? parent=null,child=null;
        try
        {
            client=await Start(root);var result=await client.RequestAsync("test/environment",null,default);
            parent=Process.GetProcessById(result.GetProperty("processId").GetInt32());
            result=await client.RequestAsync("test/spawn",null,default);child=Process.GetProcessById(result.GetProperty("processId").GetInt32());
            await client.DisposeAsync();client=null;
            await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(parent.HasExited);Assert.True(child.HasExited);
        }
        finally
        {
            if(client is not null)await client.DisposeAsync();
            foreach(var process in new[]{parent,child})if(process is not null){try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}process.Dispose();}
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
    [Fact] public async Task SilentRequestTimesOutAndDiscardsTheActualServer()
    {
        string root=Root();
        try
        {
            var clock=new FakeTimeProvider();
            await using var client=await Start(root,clock:clock);var info=await client.RequestAsync("test/environment",null,default);
            using var process=Process.GetProcessById(info.GetProperty("processId").GetInt32());
            var request=client.RequestAsync("test/hang",null,default);clock.Advance(TimeSpan.FromSeconds(25));
            await Assert.ThrowsAsync<TimeoutException>(()=>request.WaitAsync(TimeSpan.FromSeconds(6)));
            Assert.False(client.IsAlive);await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));Assert.True(process.HasExited);
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact] public async Task UnexpectedPlaintextCredentialsAreReportedAndNeverSilentlyDeleted()
    {
        string root=Root();
        try
        {
            await using var client=await Start(root);
            string auth=Path.Combine(root,"auth.json");await File.WriteAllTextAsync(auth,"synthetic sentinel");
            var error=await Assert.ThrowsAsync<CodexRpcException>(()=>client.RequestAsync("test/environment",null,default));
            Assert.True(error.CredentialStorageFailure);Assert.Equal("synthetic sentinel",await File.ReadAllTextAsync(auth));
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
