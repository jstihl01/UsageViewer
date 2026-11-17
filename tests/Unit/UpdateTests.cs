using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Time.Testing;
using UsageViewer.Core;
using Xunit;

namespace UsageViewer.Core.Tests;

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
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task LargeValidUpdateReplyIsRejectedWithOrWithoutContentLength(bool knownLength)
    {
        string json="{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v9.0.0\",\"padding\":\""+new string('x',200000)+"\"}";
        using var http=new HttpClient(new Handler(_=>new(HttpStatusCode.OK){Content=knownLength?new StringContent(json):new ChunkedContent(json)}));
        var result=await new UpdateChecker(http,new FakeTimeProvider(),NullLog.Instance).CheckAsync(new(),new(1,1,0),default);
        Assert.Null(result.AvailableVersion);
    }
    [Fact] public async Task UpdateFailureRetriesInOneHourWithoutConsumingTheSuccessfulDailyCheck()
    {
        var time=new FakeTimeProvider();var handler=new Handler(_=>throw new HttpRequestException());
        using var http=new HttpClient(handler);var checker=new UpdateChecker(http,time,NullLog.Instance);
        var result=await checker.CheckAsync(new(),new(1,1,0),default);Assert.Null(result.UpdateChecked);
        time.Advance(TimeSpan.FromMinutes(59));result=await checker.CheckAsync(result,new(1,1,0),default);Assert.Equal(1,handler.Calls);
        time.Advance(TimeSpan.FromMinutes(1));await checker.CheckAsync(result,new(1,1,0),default);Assert.Equal(2,handler.Calls);
    }
    [Theory] [InlineData(403)] [InlineData(500)]
    public async Task HttpErrorsPreserveTheLastUpdateAndUseFailureBackoff(int status)
    {
        using var http=new HttpClient(new Handler(_=>new((HttpStatusCode)status)));
        var result=await new UpdateChecker(http,new FakeTimeProvider(),NullLog.Instance).CheckAsync(new(AvailableVersion:"1.2.0"),new(1,1,0),default);
        Assert.Null(result.UpdateChecked);Assert.Equal("1.2.0",result.AvailableVersion);Assert.NotNull(result.UpdateAttempted);
    }
    [Fact] public async Task UpdateLookupCanBeDisabledAndSendsConditionalEtag()
    {
        var handler=new Handler(request=>{Assert.Equal("\"previous\"",request.Headers.IfNoneMatch.Single().Tag);return new(HttpStatusCode.NotModified);});
        using var http=new HttpClient(handler);var checker=new UpdateChecker(http,new FakeTimeProvider(),NullLog.Instance);
        await checker.CheckAsync(new(CheckForUpdates:false),new(1,1,0),default);Assert.Equal(0,handler.Calls);
        var result=await checker.CheckAsync(new(UpdateETag:"\"previous\""),new(1,1,0),default);
        Assert.NotNull(result.UpdateChecked);Assert.Equal(1,handler.Calls);
    }
    [Fact] public async Task UpdateDeadlineCancelsTheHttpRequestAfterFiveSeconds()
    {
        var time=new FakeTimeProvider();var handler=new HangingHandler();
        using var http=new HttpClient(handler);var checker=new UpdateChecker(http,time,NullLog.Instance);
        var request=checker.CheckAsync(new(),new(1,1,0),default);await handler.Entered.Task;
        time.Advance(TimeSpan.FromSeconds(4));Assert.False(request.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));var result=await request.WaitAsync(TimeSpan.FromSeconds(3));Assert.Null(result.UpdateChecked);
    }
    private sealed class ChunkedContent(string text):HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream,System.Net.TransportContext? context)=>stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(text)).AsTask();
        protected override bool TryComputeLength(out long length){length=0;return false;}
    }
    private sealed class HangingHandler:HttpMessageHandler
    {
        public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {Entered.SetResult();await Task.Delay(Timeout.InfiniteTimeSpan,cancellationToken);throw new InvalidOperationException();}
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
