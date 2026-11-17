using System.Text.Json;

namespace UsageViewer.Core;

// Contract reviewed against the pinned official server. Updates must also pass ContractTests.
public static class CodexContract
{
    public const string Version = "0.167.0";
    public const string LegacyService = "Codex Auth";
    public const string SecretsService = "codex";
    public const string LegacyPrefix = "cli|";
    public const string SecretsPrefix = "secrets|";
    public static string EncryptedAuthPath(string profile) => Path.Combine(profile, "secrets", "codex_auth.age");
    public static IReadOnlyList<string> ServerArguments() =>
        ["--listen", "stdio://", "--config", "cli_auth_credentials_store=\"keyring\"",
         "--config", "features.secret_auth_storage=true",
         "--config", "analytics.enabled=false",
         "--config", "otel.exporter=\"none\"",
         "--config", "otel.trace_exporter=\"none\"",
         "--config", "otel.metrics_exporter=\"none\"",
         "--config", "check_for_update_on_startup=false",
         "--config", "features.plugins=false",
         "--config", "features.plugin_discovery_prefetch=false",
         "--config", "features.recommended_plugins=false",
         "--config", "features.apps=false",
         "--config", "features.remote_models=false"];

    public sealed record Account(string Type, string? Email);
    public static Account? ReadAccount(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("account", out var account))
            throw new IOException("La respuesta de cuenta no es válida.");
        if (account.ValueKind == JsonValueKind.Null) return null;
        if (account.ValueKind != JsonValueKind.Object || !account.TryGetProperty("type", out var type) ||
            type.ValueKind != JsonValueKind.String) throw new IOException("La respuesta de cuenta no es válida.");
        return new(type.GetString()!, account.TryGetProperty("email", out var email) &&
            email.ValueKind == JsonValueKind.String ? email.GetString() : null);
    }
}
