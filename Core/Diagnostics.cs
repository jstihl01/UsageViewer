using System.Text.Json;
using System.Text.RegularExpressions;
using System.ComponentModel;

namespace UsageViewer.Core;

public interface IEventLog
{
    void Write(string name, string? method = null, Exception? error = null, int? code = null);
    void Stderr(string line) => Write("server_stderr_redacted");
}

public sealed class NullLog : IEventLog
{
    public static NullLog Instance { get; } = new();
    public void Write(string name, string? method = null, Exception? error = null, int? code = null) { }
}

public sealed class EventLog(string directory, string version, int maxBytes = 1_048_576, TimeProvider? time = null) : IEventLog
{
    private readonly object sync = new();
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private sealed record Repetition(long Since, int Count);
    private readonly Dictionary<(string Name,string? Method,string? Type,int? Code), Repetition> repetitions = new();
    private static readonly HashSet<string> Methods =
        ["initialize", "initialized", "account/read", "account/logout", "account/login/start",
         "account/login/cancel", "account/login/completed", "account/updated",
         "account/rateLimits/read", "account/rateLimits/updated"];

    public void Write(string name, string? method = null, Exception? error = null, int? code = null)
    {
        // Only schema-controlled fields: exception messages, payloads, URLs and paths never enter the log.
        if (!Regex.IsMatch(name, @"^[a-zA-Z0-9_.-]{1,80}$")) name = "redacted_event";
        if (method is not null && !Methods.Contains(method)) method = "unknown";
        try
        {
            lock (sync)
            {
                code ??= error is Win32Exception native ? native.NativeErrorCode : error is CodexRpcException rpc ? rpc.Code : error?.HResult;
                var key = (name, method, error?.GetType().Name, code);
                if (repetitions.TryGetValue(key, out var repeated))
                {
                    if (clock.GetElapsedTime(repeated.Since) < TimeSpan.FromMinutes(1))
                    { repetitions[key] = repeated with { Count = repeated.Count + 1 }; return; }
                    if (repeated.Count > 0) Append("repeated", method, key.Item3, code, name, repeated.Count);
                }
                if (repetitions.Count >= 256) repetitions.Clear();
                repetitions[key] = new(clock.GetTimestamp(), 0);
                Append(name, method, key.Item3, code);
            }
        }
        catch (Exception) { /* Logging must not take down the application. */ }
    }

    private void Append(string name,string? method,string? errorType,int? code,string? repeatedEvent=null,int? count=null)
    {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "usage-viewer.log");
                string line = JsonSerializer.Serialize(new
                { utc = clock.GetUtcNow(), version, name, method, errorType, code, repeatedEvent, count }) + Environment.NewLine;
                if (File.Exists(path) && new FileInfo(path).Length + line.Length * 4 > maxBytes)
                {
                    File.Delete(Path.Combine(directory, "usage-viewer.2.log"));
                    if (File.Exists(Path.Combine(directory, "usage-viewer.1.log")))
                        File.Move(Path.Combine(directory, "usage-viewer.1.log"), Path.Combine(directory, "usage-viewer.2.log"));
                    File.Move(path, Path.Combine(directory, "usage-viewer.1.log"), true);
                }
                File.AppendAllText(path, line);
    }

    public void Stderr(string line)
    {
        string category = line.Contains("keyring", StringComparison.OrdinalIgnoreCase) ? "server_credential_store" :
            line.Contains("permission", StringComparison.OrdinalIgnoreCase) ? "server_permission" :
            line.Contains("network", StringComparison.OrdinalIgnoreCase) ? "server_network" : "server_stderr_redacted";
        Write(category);
    }
}
