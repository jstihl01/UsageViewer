using System.Net;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using UsageViewer.Core;

namespace UsageViewer.Core;

internal sealed class UpdateChecker(HttpClient http,TimeProvider clock,IEventLog log)
{
    internal static readonly Uri Releases=new(RepositoryInfo.Url+"/releases/latest");
    internal async Task<AppSettings> CheckAsync(AppSettings settings,Version current,CancellationToken cancellation)
    {
        if (!settings.CheckForUpdates) return settings;
        if (settings.UpdateChecked is {} checkedAt && clock.GetUtcNow()-checkedAt>=TimeSpan.Zero &&
            clock.GetUtcNow()-checkedAt<TimeSpan.FromHours(24))
            return settings;
        if (settings.UpdateAttempted is {} attempted && clock.GetUtcNow()-attempted>=TimeSpan.Zero &&
            clock.GetUtcNow()-attempted<TimeSpan.FromHours(1)) return settings;
        var next=settings with { UpdateAttempted=clock.GetUtcNow() };
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,RepositoryInfo.Api+"/releases/latest");
            request.Headers.UserAgent.ParseAdd("UsageViewer/"+current.ToString(3));
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            if (settings.UpdateETag is {} etag && EntityTagHeaderValue.TryParse(etag,out var entity))
                request.Headers.IfNoneMatch.Add(entity);
            using var timer=new CancellationTokenSource(TimeSpan.FromSeconds(5),clock);
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation,timer.Token);
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            if (response.StatusCode==HttpStatusCode.NotModified) return next with { UpdateChecked=clock.GetUtcNow() };
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
            next=next with { UpdateChecked=clock.GetUtcNow() };
            var root=json.RootElement;
            if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return next;
            string? tag=root.GetProperty("tag_name").GetString();
            if (!Version.TryParse(tag?.TrimStart('v'),out var available)) return next;
            return next with { UpdateETag=response.Headers.ETag?.ToString(),
                AvailableVersion=available>current?available.ToString():null };
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return settings; }
        catch (Exception error) { log.Write("update_check_failed",error:error); return next; }
    }
}
