namespace UsageViewer;

internal static class StartupPolicy
{
    internal static bool ShowPanel(bool firstRunCompleted,IEnumerable<string> arguments)=>
        !firstRunCompleted&&!arguments.Contains("--background",StringComparer.OrdinalIgnoreCase);
}
