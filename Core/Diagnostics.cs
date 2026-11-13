using System.Text.Json;
using System.Text.RegularExpressions;

namespace UsageViewer.Core;

public interface IEventLog
{
    void Write(string name, string? method = null, Exception? error = null, int? code = null);
}

public sealed class NullLog : IEventLog
{
    public static NullLog Instance { get; } = new();
    public void Write(string name, string? method = null, Exception? error = null, int? code = null) { }
}

public sealed class EventLog(string directory, string version, int maxBytes = 1_048_576) : IEventLog
{
    private readonly object sync = new();
    private readonly Queue<string> stderr = new();
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
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "usage-viewer.log");
                string line = JsonSerializer.Serialize(new
                {
                    utc = DateTimeOffset.UtcNow, version, name, method,
                    errorType = error?.GetType().Name, code
                }) + Environment.NewLine;
                if (File.Exists(path) && new FileInfo(path).Length + line.Length * 4 > maxBytes)
                {
                    File.Delete(Path.Combine(directory, "usage-viewer.2.log"));
                    if (File.Exists(Path.Combine(directory, "usage-viewer.1.log")))
                        File.Move(Path.Combine(directory, "usage-viewer.1.log"), Path.Combine(directory, "usage-viewer.2.log"));
                    File.Move(path, Path.Combine(directory, "usage-viewer.1.log"), true);
                }
                File.AppendAllText(path, line);
            }
        }
        catch (Exception) { /* Logging must not take down the application. */ }
    }

    public void Stderr(string line)
    {
        string category = line.Contains("keyring", StringComparison.OrdinalIgnoreCase) ? "server_credential_store" :
            line.Contains("permission", StringComparison.OrdinalIgnoreCase) ? "server_permission" :
            line.Contains("network", StringComparison.OrdinalIgnoreCase) ? "server_network" : "server_stderr_redacted";
        lock (sync)
        {
            stderr.Enqueue(category);
            while (stderr.Count > 50) stderr.Dequeue();
        }
        Write(category);
    }
}
