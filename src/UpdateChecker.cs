using System.Net;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using UsageViewer.Core;

namespace UsageViewer;

internal sealed class UpdateChecker(HttpClient http,TimeProvider clock,IEventLog log)
{
    internal static readonly Uri Releases=new("https://github.com/jstihl01/UsageViewer/releases/latest");
    internal async Task<AppSettings> CheckAsync(AppSettings settings,Version current,CancellationToken cancellation)
    {
        if (settings.UpdateChecked is {} checkedAt && clock.GetUtcNow()-checkedAt>=TimeSpan.Zero &&
            clock.GetUtcNow()-checkedAt<TimeSpan.FromHours(24))
            return settings;
        var next=settings with { UpdateChecked=clock.GetUtcNow() };
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.github.com/repos/jstihl01/UsageViewer/releases/latest");
            request.Headers.UserAgent.ParseAdd("UsageViewer/"+current.ToString(3));
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            if (settings.UpdateETag is {} etag && EntityTagHeaderValue.TryParse(etag,out var entity))
                request.Headers.IfNoneMatch.Add(entity);
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            if (response.StatusCode==HttpStatusCode.NotModified) return next;
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength>131072) throw new InvalidDataException();
            await using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);
            using var buffer=new MemoryStream();
            byte[] bytes=new byte[4096]; int read;
            while ((read=await stream.ReadAsync(bytes,deadline.Token))>0)
            {
                if (buffer.Length+read>131072) throw new InvalidDataException();
                buffer.Write(bytes,0,read);
            }
            using var json=JsonDocument.Parse(buffer.ToArray());
            var root=json.RootElement;
            if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return next;
            string? tag=root.GetProperty("tag_name").GetString();
            if (!Version.TryParse(tag?.TrimStart('v'),out var available)) return next;
            return next with { UpdateETag=response.Headers.ETag?.ToString(),
                AvailableVersion=available>current?available.ToString():null };
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return next; }
        catch (Exception error) { log.Write("update_check_failed",error:error); return next; }
    }
}
