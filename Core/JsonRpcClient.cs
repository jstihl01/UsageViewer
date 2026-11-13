using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace UsageViewer.Core;

public sealed class JsonRpcClient : ICodexClient
{
    public const int MaximumMessageBytes = 1_048_576;
    private readonly Stream input, output;
    private readonly IEventLog log;
    private readonly TimeProvider clock;
    private readonly TimeSpan timeout;
    private readonly CancellationTokenSource stopping = new();
    private readonly SemaphoreSlim writer = new(1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
    private readonly Task reader;
    private int requestId;
    private int disposed, aborted;
    public event Action<string, JsonElement>? Notification;
    public bool IsAlive => !stopping.IsCancellationRequested && !reader.IsCompleted;

    public JsonRpcClient(Stream input, Stream output, IEventLog? log = null,
        TimeProvider? clock = null, TimeSpan? timeout = null)
    {
        this.input = input; this.output = output;
        this.log = log ?? NullLog.Instance;
        this.clock = clock ?? TimeProvider.System;
        this.timeout = timeout ?? TimeSpan.FromSeconds(25);
        reader = Task.Run(ReadAsync);
    }

    public async Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellation)
    {
        if (!IsAlive) throw new IOException("Conexión cerrada.");
        int id = Interlocked.Increment(ref requestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        using var deadline = new CancellationTokenSource(timeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, stopping.Token, deadline.Token);
        try
        {
            if (!IsAlive) throw new IOException("Conexión cerrada.");
            await SendAsync(parameters is null ? (object)new { id, method } :
                new { id, method, @params = parameters }, linked.Token).WaitAsync(linked.Token);
            return await completion.Task.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested &&
            !cancellation.IsCancellationRequested && !stopping.IsCancellationRequested)
        {
            log.Write("rpc_timeout", method);
            Abort(new TimeoutException("Codex no respondió a tiempo."));
            throw new TimeoutException("Codex no respondió a tiempo.");
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested && !cancellation.IsCancellationRequested)
        { throw new IOException("Se interrumpió la conexión con Codex."); }
        catch (IOException error) { Abort(error); throw; }
        finally { pending.TryRemove(id, out _); }
    }

    public Task NotifyAsync(string method, CancellationToken cancellation) =>
        SendAsync(new { method }, cancellation);

    private async Task SendAsync(object message, CancellationToken cancellation)
    {
        await writer.WaitAsync(cancellation);
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message) + "\n");
            await output.WriteAsync(bytes, cancellation);
            await output.FlushAsync(cancellation);
        }
        finally { writer.Release(); }
    }

    private async Task ReadAsync()
    {
        try
        {
            await foreach (string line in BoundedLines.ReadAsync(input, MaximumMessageBytes, stopping.Token))
            {
                JsonDocument document;
                try { document = JsonDocument.Parse(line); }
                catch (JsonException) { log.Write("rpc_invalid_json"); continue; }
                using (document)
                {
                    var message = document.RootElement;
                    if (message.ValueKind != JsonValueKind.Object) { log.Write("rpc_invalid_message"); continue; }
                    bool hasId = message.TryGetProperty("id", out var id);
                    if (message.TryGetProperty("method", out var method))
                    {
                        if (method.ValueKind != JsonValueKind.String) { log.Write("rpc_invalid_method"); continue; }
                        if (hasId)
                        {
                            using var responseDeadline = new CancellationTokenSource(timeout, clock);
                            using var responseStop = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token, responseDeadline.Token);
                            await SendAsync(new { id = id.Clone(), error = new { code = -32601, message = "Method not found" } }, responseStop.Token);
                        }
                        else
                        {
                            var parameters = message.TryGetProperty("params", out var value) ? value.Clone() :
                                JsonSerializer.SerializeToElement(new { });
                            try { Notification?.Invoke(method.GetString()!, parameters); }
                            catch (Exception error) { log.Write("rpc_notification_handler_failed", method.GetString(), error); }
                        }
                    }
                    else if (hasId && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out int number) &&
                             pending.TryGetValue(number, out var completion))
                    {
                        if (message.TryGetProperty("result", out var result) && !message.TryGetProperty("error", out _))
                            completion.TrySetResult(result.Clone());
                        else if (message.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                        {
                            int code = error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number &&
                                c.TryGetInt32(out int n) ? n : 0;
                            string text = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ?
                                m.GetString()! : "";
                            log.Write("rpc_error", code: code);
                            completion.TrySetException(new CodexRpcException(code, text));
                        }
                        else completion.TrySetException(new IOException("Respuesta de Codex no válida."));
                    }
                }
            }
        }
        catch (Exception error) { if (!stopping.IsCancellationRequested) log.Write("rpc_reader_failed", error: error); }
        finally { Abort(new IOException("Se interrumpió la conexión con Codex.")); }
    }

    private void Abort(Exception error)
    {
        if (Interlocked.Exchange(ref aborted, 1) != 0) return;
        foreach (var completion in pending.Values) completion.TrySetException(error);
        stopping.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Abort(new IOException("Conexión cerrada."));
        try { await reader.WaitAsync(TimeSpan.FromSeconds(2)); } catch (Exception) { }
    }
}

public static class BoundedLines
{
    public static async IAsyncEnumerable<string> ReadAsync(Stream stream, int maximum,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation,
        bool skipOversized = false)
    {
        byte[] buffer = new byte[8192];
        using var line = new MemoryStream();
        bool discarding = false;
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellation)) > 0)
        {
            int start = 0;
            for (int i = 0; i < count; i++)
            {
                if (buffer[i] != 10) continue;
                if (!discarding && line.Length + i - start > maximum)
                {
                    if (!skipOversized) throw new InvalidDataException("Mensaje demasiado grande.");
                    discarding = true;
                }
                if (!discarding)
                {
                    line.Write(buffer, start, i - start);
                    yield return Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r');
                }
                line.SetLength(0);
                discarding = false;
                start = i + 1;
            }
            if (!discarding && line.Length + count - start > maximum)
            {
                if (!skipOversized) throw new InvalidDataException("Mensaje demasiado grande.");
                line.SetLength(0);
                discarding = true;
            }
            if (!discarding) line.Write(buffer, start, count - start);
        }
        if (!discarding && line.Length > 0)
            yield return Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r');
    }
}
