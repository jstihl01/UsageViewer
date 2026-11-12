using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace UsageViewer;

internal sealed class CodexConnection : IDisposable
{
    private readonly Process process;
    private readonly CancellationTokenSource stopping = new();
    private readonly SemaphoreSlim writer = new(1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
    private readonly Task reader;
    private int requestId;
    private bool disposed;

    private CodexConnection(Process process)
    {
        this.process = process;
        reader = ReadAsync();
        _ = DrainErrorsAsync();
    }

    public event Action<string, JsonElement>? Notification;
    public bool IsAlive => !disposed && !reader.IsCompleted && !process.HasExited;

    public static async Task<CodexConnection> StartAsync(string profile, CancellationToken cancellation)
    {
        Directory.CreateDirectory(profile);
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "codex", "codex-app-server.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = profile
        };
        start.ArgumentList.Add("--listen");
        start.ArgumentList.Add("stdio://");
        start.ArgumentList.Add("--config");
        start.ArgumentList.Add("cli_auth_credentials_store=\"file\"");
        start.Environment["CODEX_HOME"] = profile;
        start.Environment.Remove("CODEX_API_KEY");
        start.Environment.Remove("OPENAI_API_KEY");
        var connection = new CodexConnection(Process.Start(start) ??
            throw new IOException("No se pudo iniciar el servidor de Codex."));
        try
        {
            await connection.RequestAsync("initialize", new
            {
                clientInfo = new { name = "usage_viewer", title = "Usage Viewer", version = "1.0.15" }
            }, cancellation);
            await connection.SendAsync(new { method = "initialized" }, cancellation);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public async Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellation)
    {
        int id = Interlocked.Increment(ref requestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation, stopping.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            await SendAsync(parameters is null ? new { id, method } :
                (object)new { id, method, @params = parameters }, deadline.Token);
            return await completion.Task.WaitAsync(deadline.Token);
        }
        finally
        {
            pending.TryRemove(id, out _);
        }
    }

    private async Task SendAsync(object message, CancellationToken cancellation)
    {
        await writer.WaitAsync(cancellation);
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), cancellation);
            await process.StandardInput.FlushAsync(cancellation);
        }
        finally
        {
            writer.Release();
        }
    }

    private async Task ReadAsync()
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync(stopping.Token).ConfigureAwait(false) is { } line)
            {
                using var document = JsonDocument.Parse(line);
                var message = document.RootElement;
                if (message.TryGetProperty("id", out var id))
                {
                    if (id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out int number) ||
                        !pending.TryGetValue(number, out var completion))
                        continue;
                    if (message.TryGetProperty("result", out var result))
                        completion.TrySetResult(result.Clone());
                    else
                        completion.TrySetException(new IOException("Codex no pudo completar la solicitud."));
                }
                else if (message.TryGetProperty("method", out var method) &&
                         message.TryGetProperty("params", out var parameters))
                {
                    Notification?.Invoke(method.GetString() ?? "", parameters.Clone());
                }
            }
        }
        catch (Exception error) when (error is IOException or JsonException or OperationCanceledException or InvalidOperationException)
        {
        }
        finally
        {
            foreach (var completion in pending.Values)
                completion.TrySetException(new IOException("Se interrumpió la conexión con Codex."));
        }
    }

    private async Task DrainErrorsAsync()
    {
        try
        {
            await process.StandardError.BaseStream.CopyToAsync(Stream.Null, stopping.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or InvalidOperationException)
        {
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        stopping.Cancel();
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
        process.Dispose();
    }
}
