using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Time.Testing;
using UsageViewer.Core;
using Xunit;

namespace UsageViewer.Windows.Tests;

public class UpdateTests
{
    [Fact] public async Task UpdateLookupIsPublicBoundedAndCached()
    {
        var time=new FakeTimeProvider();
        var handler=new Handler(_=>new(HttpStatusCode.OK)
        {Content=new StringContent("""{"draft":false,"prerelease":false,"tag_name":"v1.2.0"}""")});
        using var http=new HttpClient(handler);
        var checker=new UpdateChecker(http,time,NullLog.Instance);
        var settings=await checker.CheckAsync(new(),new(1,1,0),default);
        Assert.Equal("1.2.0",settings.AvailableVersion);
        Assert.Null(handler.Authorization);
        await checker.CheckAsync(settings,new(1,1,0),default);
        Assert.Equal(1,handler.Calls);
        time.Advance(TimeSpan.FromHours(24));
        await checker.CheckAsync(settings,new(1,1,0),default);
        Assert.Equal(2,handler.Calls);
    }
    [Fact] public async Task FailureDoesNotChangeSessionOrRetryEveryMinute()
    {
        var time=new FakeTimeProvider();
        var handler=new Handler(_=>throw new HttpRequestException("untrusted"));
        using var http=new HttpClient(handler);
        var checker=new UpdateChecker(http,time,NullLog.Instance);
        var settings=await checker.CheckAsync(new(),new(1,1,0),default);
        Assert.Null(settings.AvailableVersion);
        time.Advance(TimeSpan.FromMinutes(1));
        await checker.CheckAsync(settings,new(1,1,0),default);
        Assert.Equal(1,handler.Calls);
    }
    [Theory]
    [InlineData("""{"draft":true,"prerelease":false,"tag_name":"v2.0.0"}""")]
    [InlineData("""{"draft":false,"prerelease":true,"tag_name":"v2.0.0"}""")]
    [InlineData("""{"draft":false,"prerelease":false,"tag_name":"v1.0.15"}""")]
    [InlineData("""{"draft":false,"prerelease":false,"tag_name":"invalid"}""")]
    public async Task DraftsPrereleasesOlderAndInvalidVersionsAreNotOffered(string json)
    {
        using var http=new HttpClient(new Handler(_=>new(HttpStatusCode.OK){Content=new StringContent(json)}));
        var result=await new UpdateChecker(http,new FakeTimeProvider(),NullLog.Instance).CheckAsync(new(),new(1,1,0),default);
        Assert.Null(result.AvailableVersion);
    }
    [Fact] public async Task LargeReplyIsRejected()
    {
        using var http=new HttpClient(new Handler(_=>new(HttpStatusCode.OK){Content=new StringContent(new string('x',131073))}));
        var result=await new UpdateChecker(http,new FakeTimeProvider(),NullLog.Instance).CheckAsync(new(),new(1,1,0),default);
        Assert.Null(result.AvailableVersion);
    }
    [Fact] public async Task FutureDatedCacheDoesNotDisableUpdatesForever()
    {
        var time=new FakeTimeProvider();
        var handler=new Handler(_=>new(HttpStatusCode.NotModified));
        using var http=new HttpClient(handler);
        await new UpdateChecker(http,time,NullLog.Instance).CheckAsync(new(UpdateChecked:time.GetUtcNow().AddYears(1)),new(1,1,0),default);
        Assert.Equal(1,handler.Calls);
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
    {
        public int Calls;public string? Authorization;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {Calls++;Authorization=request.Headers.Authorization?.ToString();return Task.FromResult(respond(request));}
    }
}
