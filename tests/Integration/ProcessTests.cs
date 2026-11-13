using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using UsageViewer.Core;
using UsageViewer.Windows;
using Xunit;

namespace UsageViewer.Integration.Tests;

public sealed class RealServerFactAttribute:FactAttribute
{
    public RealServerFactAttribute()
    {
        if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("USAGEVIEWER_CODEX_BINARY")))
            Skip="SKIP: servidor real; definir USAGEVIEWER_CODEX_BINARY para ejecutar la integración.";
    }
}
public class ProcessTests
{
    private const string Hash="d781e2c96caeae19f5c8280814d778b92d4958bcca86064d6bec09d789ae7e22";
    [RealServerFact]
    public async Task SignedPinnedServerInitializesAndUsesIsolatedWindowsCredentialStore()
    {
        string root=Path.Combine(Path.GetTempPath(),"UsageViewer-"+Guid.NewGuid().ToString("N"));
        string a=Path.Combine(root,"a"),b=Path.Combine(root,"b");
        string executable=Environment.GetEnvironmentVariable("USAGEVIEWER_CODEX_BINARY")!;
        CodexProcessClient? first=null,second=null;
        try
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(90));
            first=await CodexProcessClient.StartAsync(executable,a,Hash,"1.1.0",NullLog.Instance,deadline.Token);
            second=await CodexProcessClient.StartAsync(executable,b,Hash,"1.1.0",NullLog.Instance,deadline.Token);
            var read=await first.RequestAsync("account/read",new{refreshToken=false},deadline.Token);
            Assert.Equal(JsonValueKind.Null,read.GetProperty("account").ValueKind);
            string longCredential="UsageViewer-test-"+new string('x',16000)+Guid.NewGuid().ToString("N");
            await first.RequestAsync("account/login/start",new{type="apiKey",apiKey=longCredential},deadline.Token);
            read=await first.RequestAsync("account/read",new{refreshToken=false},deadline.Token);
            Assert.Equal("apiKey",read.GetProperty("account").GetProperty("type").GetString());
            Assert.False(File.Exists(Path.Combine(a,"auth.json")));
            Assert.True(ContainsKey(CredentialCleanup.SecretsAccountKey(a)));
            string cipher=Path.Combine(a,"secrets","codex_auth.age");
            Assert.True(File.Exists(cipher));
            string encrypted=System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(cipher));
            Assert.StartsWith("age-encryption.org/v1",encrypted);
            Assert.DoesNotContain(longCredential,encrypted);
            var other=await second.RequestAsync("account/read",new{refreshToken=false},deadline.Token);
            Assert.Equal(JsonValueKind.Null,other.GetProperty("account").ValueKind);
            Assert.NotEqual(CredentialCleanup.AccountKey(a),CredentialCleanup.AccountKey(b));
            Assert.NotEqual(CredentialCleanup.SecretsAccountKey(a),CredentialCleanup.SecretsAccountKey(b));
            await second.RequestAsync("account/login/start",new{type="apiKey",apiKey="UsageViewer-test-"+Guid.NewGuid().ToString("N")},deadline.Token);
            await first.DisposeAsync();first=null;
            first=await CodexProcessClient.StartAsync(executable,a,Hash,"1.1.1",NullLog.Instance,deadline.Token);
            read=await first.RequestAsync("account/read",new{refreshToken=false},deadline.Token);
            Assert.Equal("apiKey",read.GetProperty("account").GetProperty("type").GetString());
            await first.DisposeAsync();first=null;
            await second.DisposeAsync();second=null;
            CredentialCleanup.DeleteOwned(a);
            Assert.False(ContainsKey(CredentialCleanup.SecretsAccountKey(a)));
            first=await CodexProcessClient.StartAsync(executable,a,Hash,"1.1.0",NullLog.Instance,deadline.Token);
            read=await first.RequestAsync("account/read",new{refreshToken=false},deadline.Token);
            Assert.Equal(JsonValueKind.Null,read.GetProperty("account").ValueKind);
            second=await CodexProcessClient.StartAsync(executable,b,Hash,"1.1.0",NullLog.Instance,deadline.Token);
            other=await second.RequestAsync("account/read",new{refreshToken=false},deadline.Token);
            Assert.Equal("apiKey",other.GetProperty("account").GetProperty("type").GetString());
        }
        finally
        {
            foreach(var client in new[]{first,second})
            {
                if(client is null)continue;
                try { using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await client.RequestAsync("account/logout",null,cancellation.Token); } catch(Exception) { }
                await client.DisposeAsync();
            }
            foreach(var profile in new[]{a,b})
            {try{if(Directory.Exists(profile))CredentialCleanup.DeleteOwned(profile);}catch(Exception){}}
            DeleteTestRoot(root);
        }
    }
    [RealServerFact] public async Task LogoutRemovesEncryptedCredentialsAcrossServerRestart()
    {
        string root=Path.Combine(Path.GetTempPath(),"UsageViewer-"+Guid.NewGuid().ToString("N"));
        string profile=Path.Combine(root,"profile");
        string executable=Environment.GetEnvironmentVariable("USAGEVIEWER_CODEX_BINARY")!;
        CodexProcessClient? client=null;
        try
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(90));
            client=await CodexProcessClient.StartAsync(executable,profile,Hash,"1.1.1",NullLog.Instance,deadline.Token);
            await client.RequestAsync("account/login/start",new{type="apiKey",apiKey=new string('x',16000)},deadline.Token);
            await client.RequestAsync("account/logout",null,deadline.Token);
            await client.DisposeAsync();client=null;
            client=await CodexProcessClient.StartAsync(executable,profile,Hash,"1.1.1",NullLog.Instance,deadline.Token);
            var result=await client.RequestAsync("account/read",new{refreshToken=false},deadline.Token);
            Assert.Equal(JsonValueKind.Null,result.GetProperty("account").ValueKind);
            Assert.False(File.Exists(Path.Combine(profile,"auth.json")));
        }
        finally
        {
            if(client is not null)await client.DisposeAsync();
            if(Directory.Exists(profile))CredentialCleanup.DeleteOwned(profile);
            DeleteTestRoot(root);
        }
    }
    [RealServerFact] public async Task EncryptionStorageFailureNeverFallsBackToPlaintext()
    {
        string root=Path.Combine(Path.GetTempPath(),"UsageViewer-"+Guid.NewGuid().ToString("N"));
        string profile=Path.Combine(root,"profile");
        Directory.CreateDirectory(profile);
        // A file occupying the encrypted directory is a real write failure without changing OS permissions.
        string blocker=Path.Combine(profile,"secrets");
        await File.WriteAllTextAsync(blocker,"test-blocker");
        CodexProcessClient? client=null;
        try
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(90));
            client=await CodexProcessClient.StartAsync(Environment.GetEnvironmentVariable("USAGEVIEWER_CODEX_BINARY")!,
                profile,Hash,"1.1.1",NullLog.Instance,deadline.Token);
            await Assert.ThrowsAsync<CodexRpcException>(()=>client.RequestAsync("account/login/start",
                new{type="apiKey",apiKey=new string('x',16000)},deadline.Token));
            Assert.False(File.Exists(Path.Combine(profile,"auth.json")));
            Assert.Equal("test-blocker",await File.ReadAllTextAsync(blocker));
        }
        finally
        {
            if(client is not null)await client.DisposeAsync();
            File.Delete(blocker);
            CredentialCleanup.DeleteOwned(profile);
            DeleteTestRoot(root);
        }
    }
    private static bool ContainsKey(string account)
    {
        if(!NativeMethods.CredEnumerate(null,0,out uint count,out IntPtr entries))
        {
            int error=Marshal.GetLastWin32Error();
            if(error==1168)return false;
            throw new Win32Exception(error);
        }
        try
        {
            for(int i=0;i<count;i++)
            {
                var credential=Marshal.PtrToStructure<NativeMethods.Credential>(Marshal.ReadIntPtr(entries,i*IntPtr.Size));
                if(credential.Type==1&&(Marshal.PtrToStringUni(credential.TargetName)??"").Contains(account,StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
        finally{NativeMethods.CredFree(entries);}
    }
    [Fact] public async Task ChangedBinaryIsRejectedBeforeItCanRun()
    {
        string root=Path.Combine(Path.GetTempPath(),"UsageViewer-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string fake=Path.Combine(root,"not-a-server.exe");await File.WriteAllTextAsync(fake,"invalid");
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(()=>CodexProcessClient.StartAsync(
                fake,Path.Combine(root,"profile"),Hash,"1.1.0",NullLog.Instance,default));
        }
        finally{DeleteTestRoot(root);}
    }
    [Fact] public async Task KillingParentAlsoTerminatesItsJobChild()
    {
        string? fixture=Environment.GetEnvironmentVariable("USAGEVIEWER_PROCESS_FIXTURE");
        Assert.False(string.IsNullOrEmpty(fixture),"El build debe proporcionar el proceso de prueba.");
        using var parent=Process.Start(new ProcessStartInfo(fixture!)
        {ArgumentList={"--parent"},UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true})!;
        Process? child=null;
        try
        {
            string? text=await parent.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(int.TryParse(text,out int id));
            child=Process.GetProcessById(id);
            parent.Kill();await parent.WaitForExitAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(child.HasExited);
        }
        finally
        {
            if(!parent.HasExited)parent.Kill(true);
            if(child is not null){try{if(!child.HasExited)child.Kill();}catch(InvalidOperationException){}child.Dispose();}
        }
    }
    private static void DeleteTestRoot(string root)
    {
        string absolute=Path.GetFullPath(root);
        if(!absolute.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||
            !Path.GetFileName(absolute).StartsWith("UsageViewer-"))throw new InvalidOperationException("Ruta temporal no válida.");
        if(Directory.Exists(absolute))
        {
            // Codex's private skills cache can contain read-only Git pack files on a fresh machine.
            // Do not follow reparse points while clearing attributes in this generated test directory.
            var options=new EnumerationOptions {RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint};
            foreach(string path in Directory.EnumerateFiles(absolute,"*",options))
                File.SetAttributes(path,File.GetAttributes(path)&~FileAttributes.ReadOnly);
            Directory.Delete(absolute,true);
        }
    }
}
