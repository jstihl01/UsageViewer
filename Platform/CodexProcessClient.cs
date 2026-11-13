using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using UsageViewer.Core;

namespace UsageViewer.Windows;

public sealed class CodexProcessFactory(string executable, string profile, string expectedHash,
    string version, IEventLog log) : ICodexClientFactory
{
    public Task<ICodexClient> StartAsync(CancellationToken cancellation) =>
        Task.Run(async () => (ICodexClient)await CodexProcessClient.StartAsync(
            executable, profile, expectedHash, version, log, cancellation), cancellation);
}

public sealed class ProcessJob : IDisposable
{
    private readonly SafeFileHandle handle;
    public ProcessJob()
    {
        handle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var limits = new NativeMethods.ExtendedLimit { Basic = new() { Flags = 0x2000 } };
        if (!NativeMethods.SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf<NativeMethods.ExtendedLimit>()))
        { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
    }
    public void Assign(Process process)
    {
        if (!NativeMethods.AssignProcessToJobObject(handle, process.Handle))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Dispose() => handle.Dispose();
}

public static class BinaryTrust
{
    public static bool VerifySignature(string path)
    {
        IntPtr text = Marshal.StringToCoTaskMemUni(path);
        IntPtr file = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeMethods.TrustFile>());
        try
        {
            var info = new NativeMethods.TrustFile { Size=(uint)Marshal.SizeOf<NativeMethods.TrustFile>(), Path=text };
            Marshal.StructureToPtr(info,file,false);
            var data = new NativeMethods.TrustData
            {
                Size=(uint)Marshal.SizeOf<NativeMethods.TrustData>(), Ui=2, Choice=1, File=file,
                ProviderFlags=0x1000 // Cache-only certificate lookup: startup must also work offline.
            };
            var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
            return NativeMethods.WinVerifyTrust(new IntPtr(-1),ref action,ref data) == 0;
        }
        finally { Marshal.FreeCoTaskMem(file); Marshal.FreeCoTaskMem(text); }
    }
}

public sealed class CodexProcessClient : ICodexClient
{
    private readonly Process process;
    private readonly ProcessJob job;
    private readonly JsonRpcClient rpc;
    private readonly IEventLog log;
    private readonly CancellationTokenSource stopping = new();
    private readonly Task stderrTask;
    private int disposed;
    public bool IsAlive => disposed == 0 && !process.HasExited && rpc.IsAlive;
    public int ProcessId => process.Id;
    public event Action<string, JsonElement>? Notification
    { add => rpc.Notification += value; remove => rpc.Notification -= value; }

    private CodexProcessClient(Process process, ProcessJob job, IEventLog log)
    {
        this.process=process; this.job=job; this.log=log;
        rpc=new(process.StandardOutput.BaseStream,process.StandardInput.BaseStream,log);
        stderrTask=DrainAsync();
    }

    public static async Task<CodexProcessClient> StartAsync(string executable, string profile, string hash,
        string version, IEventLog log, CancellationToken cancellation)
    {
        Directory.CreateDirectory(profile);
        // Keep the image non-writable/non-replaceable between hashing and Process.Start.
        await using var file=new FileStream(executable,FileMode.Open,FileAccess.Read,FileShare.Read);
        string actual=Convert.ToHexString(await SHA256.HashDataAsync(file,cancellation));
        if (!actual.Equals(hash,StringComparison.OrdinalIgnoreCase) || !BinaryTrust.VerifySignature(executable))
            throw new InvalidDataException("El servidor de Codex no supera la verificación de integridad.");
        var job=new ProcessJob();
        var start=new ProcessStartInfo(executable)
        {
            UseShellExecute=false, CreateNoWindow=true, RedirectStandardInput=true,
            RedirectStandardOutput=true, RedirectStandardError=true, WorkingDirectory=profile,
            StandardInputEncoding=new UTF8Encoding(false), StandardOutputEncoding=Encoding.UTF8
        };
        start.ArgumentList.Add("--listen"); start.ArgumentList.Add("stdio://");
        start.ArgumentList.Add("--config"); start.ArgumentList.Add("cli_auth_credentials_store=\"keyring\"");
        // OAuth JSON can exceed Windows Credential Manager's 2,560 UTF-16-character limit.
        // The pinned official backend encrypts it with age; only its small random key enters keyring.
        // Keep keyring mandatory: an unavailable secure store must never fall back to plaintext.
        start.ArgumentList.Add("--config"); start.ArgumentList.Add("features.secret_auth_storage=true");
        start.Environment["CODEX_HOME"]=profile;
        start.Environment.Remove("CODEX_API_KEY"); start.Environment.Remove("OPENAI_API_KEY");
        Process? process=null; CodexProcessClient? client=null;
        try
        {
            process=Process.Start(start) ?? throw new IOException("No se pudo arrancar Codex.");
            job.Assign(process);
            client=new(process,job,log);
            await client.rpc.RequestAsync("initialize",new
            { clientInfo=new { name="usage_viewer",title="Usage Viewer",version } },cancellation);
            await client.rpc.NotifyAsync("initialized",cancellation);
            log.Write("server_started");
            return client;
        }
        catch
        {
            if (client is not null) await client.DisposeAsync();
            else
            {
                try { if (process is not null && !process.HasExited) process.Kill(true); } catch (Exception) { }
                process?.Dispose(); job.Dispose();
            }
            throw;
        }
    }

    public async Task<JsonElement> RequestAsync(string method,object? parameters,CancellationToken cancellation)
    {
        try { return await rpc.RequestAsync(method,parameters,cancellation); }
        catch (TimeoutException) { await DisposeAsync(); throw; }
    }

    private async Task DrainAsync()
    {
        try
        {
            await foreach (var line in BoundedLines.ReadAsync(process.StandardError.BaseStream,4096,stopping.Token,skipOversized:true))
                if (log is EventLog events) events.Stderr(line);
        }
        catch (Exception error) { if (!stopping.IsCancellationRequested) log.Write("server_stderr_failed",error:error); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed,1)!=0) return;
        try
        {
            try { process.StandardInput.Close(); } catch (Exception) { }
            Task stopRpc=rpc.DisposeAsync().AsTask();
            if (!process.HasExited)
            {
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (TimeoutException) { process.Kill(true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
            }
            log.Write("server_exited",code:process.ExitCode);
            await stopRpc.WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch (Exception error) { log.Write("server_shutdown_failed",error:error); }
        finally
        {
            stopping.Cancel(); job.Dispose();
            try { await stderrTask.WaitAsync(TimeSpan.FromSeconds(1)); } catch (Exception) { }
            process.Dispose();
        }
    }
}
