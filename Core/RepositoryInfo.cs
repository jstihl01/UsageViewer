using System.Reflection;

namespace UsageViewer.Core;

internal static class RepositoryInfo
{
    internal static readonly string Url = typeof(RepositoryInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "RepositoryUrl").Value!.TrimEnd('/');
    internal static readonly string Api = "https://api.github.com/repos/"+new Uri(Url).AbsolutePath.Trim('/');
}
