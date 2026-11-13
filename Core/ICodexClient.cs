using System.Text.Json;

namespace UsageViewer.Core;

public interface ICodexClient : IAsyncDisposable
{
    bool IsAlive { get; }
    event Action<string, JsonElement>? Notification;
    Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellation);
}

public interface ICodexClientFactory
{
    Task<ICodexClient> StartAsync(CancellationToken cancellation);
}

public sealed class CodexRpcException(int code, string remoteMessage) : IOException("Codex rechazó la solicitud.")
{
    public int Code { get; } = code;
    public string RemoteMessage { get; } = remoteMessage;
    public bool CredentialStorageFailure => IsCredentialStorageFailure(RemoteMessage);
    public static bool IsCredentialStorageFailure(string message) =>
        message.Contains("keyring",StringComparison.OrdinalIgnoreCase) ||
        message.Contains("encrypted auth storage",StringComparison.OrdinalIgnoreCase) ||
        message.Contains("persist_failed",StringComparison.OrdinalIgnoreCase);
    public bool AuthenticationFailure => Code is 401 or 403 or -32001 ||
        RemoteMessage.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
        RemoteMessage.Contains("authentication", StringComparison.OrdinalIgnoreCase);
}
