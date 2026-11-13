using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using UsageViewer.Core;
using Xunit;

namespace UsageViewer.Core.Tests;

public class DomainTests
{
    private static readonly DateTimeOffset Start=DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private static LimitsParseResult Parse(string json)
    { using var doc=JsonDocument.Parse(json); return QuotaParser.Parse(doc.RootElement); }
    [Theory]
    [InlineData(.2,SegmentKind.Margin,.2,.4)]
    [InlineData(.6,SegmentKind.Overlap,.4,.6)]
    [InlineData(0,SegmentKind.Margin,0,.4)]
    [InlineData(1,SegmentKind.Overlap,.4,1)]
    public void ArcsReflectPace(double used,SegmentKind kind,double first,double last)
    {
        var arcs=Ring.Arcs(new(used,300,Start.AddHours(5)),Start.AddHours(2)).ToArray();
        var colored=Assert.Single(arcs,x=>x.Kind is SegmentKind.Margin or SegmentKind.Overlap);
        Assert.Equal(kind,colored.Kind);
        Assert.Equal(first,colored.Start,8); Assert.Equal(last,colored.End,8);
        Assert.Equal(1,arcs.Sum(x=>x.End-x.Start),8);
    }
    [Fact] public void EqualAndNearlyEqualPaceHaveNoColoredSliver()
    {
        foreach(double used in new[]{.4,.4+Ring.EqualityTolerance/2})
        {
            var cycle=new Cycle(used,300,Start.AddHours(5));
            Assert.Equal(Pace.Even,Ring.GetPace(cycle,Start.AddHours(2)));
            Assert.DoesNotContain(Ring.Arcs(cycle,Start.AddHours(2)),x=>x.Kind is SegmentKind.Margin or SegmentKind.Overlap);
        }
    }
    [Fact] public void MissingResetRetainsPercentageWithoutInventingTime()
    {
        var cycle=new Cycle(.3,300,null);
        Assert.Null(cycle.Elapsed(Start)); Assert.Null(Ring.GetPace(cycle,Start));
        Assert.Equal(new[]{new Arc(0,.3,SegmentKind.Consumed),new Arc(.3,1,SegmentKind.Remaining)},Ring.Arcs(cycle,Start));
    }
    [Fact] public void ExpiredOrMissingQuotaHasNoArcs()
    {
        Assert.Empty(Ring.Arcs(null,Start));
        Assert.Empty(Ring.Arcs(new(1,300,Start),Start));
        Assert.Equal(0,new Cycle(0,300,Start.AddHours(5)).Elapsed(Start.AddDays(-1)));
    }
    [Theory]
    [InlineData(0,0,SegmentKind.Remaining)]
    [InlineData(1,0,SegmentKind.Overlap)]
    [InlineData(0,5,SegmentKind.Margin)]
    [InlineData(1,5,SegmentKind.Consumed)]
    public void FullCircleExtremes(double used,int elapsedHours,SegmentKind expected)
    {
        var reset=Start.AddHours(5).AddMilliseconds(1);
        var now=Start.AddHours(elapsedHours);
        var cycle=new Cycle(used,300,reset);
        var arcs=Ring.Arcs(cycle,now).ToArray();
        Assert.Contains(arcs,x=>x.Kind==expected);
        Assert.Equal(1,arcs.Sum(x=>x.End-x.Start),6);
    }
    [Theory]
    [InlineData("other",ParseKind.NoMatchingWindows)]
    [InlineData("codex",ParseKind.Valid)]
    public void FallbackBucketIsFiltered(string bucket,ParseKind expected)
    {
        var result=Parse(JsonSerializer.Serialize(new { rateLimits=new { limitId=bucket,
            primary=new { usedPercent=30,windowDurationMins=300,resetsAt=(long?)null } } }));
        Assert.Equal(expected,result.Kind);
    }
    [Fact] public void MultiBucketSelectionUsesDurationNotPosition()
    {
        var result=Parse("""{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":20,"windowDurationMins":10080,"resetsAt":1767830400},"secondary":{"usedPercent":40,"windowDurationMins":300,"resetsAt":1767243600}}}}""");
        Assert.Equal(ParseKind.Valid,result.Kind);
        Assert.Equal(.4,result.Limits.Short!.Used); Assert.Equal(.2,result.Limits.Weekly!.Used);
    }
    [Theory]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":1e999,"windowDurationMins":300}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":"30","windowDurationMins":300}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":30,"windowDurationMins":"300"}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":30,"windowDurationMins":300,"resetsAt":0}}}""")]
    [InlineData("""{"rateLimits":{"primary":{"usedPercent":30,"windowDurationMins":300,"resetsAt":253402300800}}}""")]
    [InlineData("""{"rateLimits":{"limitId":42}}""")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"rateLimits":{}}""")]
    [InlineData("""{"rateLimitsByLimitId":[],"rateLimits":null}""")]
    public void MalformedPayloadIsNotSuccessfulEmptyData(string input)=>Assert.Equal(ParseKind.Invalid,Parse(input).Kind);
    [Theory] [InlineData(105,1)] [InlineData(-3,0)]
    public void PercentageIsClamped(double input,double expected)
    {
        var result=Parse(JsonSerializer.Serialize(new { rateLimits=new {
            primary=new { usedPercent=input,windowDurationMins=300 } } }));
        Assert.Equal(expected,result.Limits.Short!.Used);
    }
    [Fact] public void ValidAbsentAndDifferentWindowsAreExplicit()
    {
        Assert.Equal(ParseKind.NoMatchingWindows,Parse("""{"rateLimits":{"primary":null,"secondary":null}}""").Kind);
        Assert.Equal(ParseKind.NoMatchingWindows,Parse("""{"rateLimits":{"primary":{"usedPercent":30,"windowDurationMins":60}}}""").Kind);
    }
    [Fact] public void PollIsDueAtSixtySecondsNotBefore()
    {
        var time=new FakeTimeProvider(Start); var s=new RefreshSchedule(time);
        var far=new Limits(new(0,300,Start.AddHours(5)),new(0,10080,Start.AddDays(7)));
        Assert.True(s.IsDue(far)); s.Started(); s.Succeeded(far);
        time.Advance(TimeSpan.FromSeconds(59)); Assert.False(s.IsDue(far));
        time.Advance(TimeSpan.FromSeconds(1)); Assert.True(s.IsDue(far));
    }
    [Fact] public void WeeklyResetTriggersIndependently()
    {
        var time=new FakeTimeProvider(Start); var s=new RefreshSchedule(time);
        var limits=new Limits(new(0,300,Start.AddHours(5)),new(0,10080,Start.AddSeconds(20)));
        s.Started(); s.Succeeded(limits);
        time.Advance(TimeSpan.FromSeconds(19)); Assert.False(s.IsDue(limits));
        time.Advance(TimeSpan.FromSeconds(1)); Assert.True(s.IsDue(limits));
    }
    [Fact] public void WallClockChangesDoNotStopMonotonicPolling()
    {
        var time=new AdjustableClock(Start); var s=new RefreshSchedule(time);
        var far=new Limits(new(0,300,Start.AddHours(5)),null);
        s.Started();s.Succeeded(far);time.Wall=Start.AddHours(-5);time.Seconds=60;
        Assert.True(s.IsDue(far));
        s.Started();s.Succeeded(far);time.Wall=Start.AddHours(6);
        Assert.True(s.IsDue(far));
    }
    [Fact] public void ExpiredResetGetsShortRetriesThenReturnsToNormal()
    {
        var time=new FakeTimeProvider(Start); var s=new RefreshSchedule(time);
        var old=new Limits(new(.8,300,Start.AddSeconds(-1)),null);
        s.Started();s.Succeeded(old);
        time.Advance(TimeSpan.FromSeconds(4));Assert.False(s.IsDue(old));
        time.Advance(TimeSpan.FromSeconds(1));Assert.True(s.IsDue(old));
        time.Advance(TimeSpan.FromSeconds(60));s.Started();s.Succeeded(old);
        time.Advance(TimeSpan.FromSeconds(5));Assert.False(s.IsDue(old));
        time.Advance(TimeSpan.FromSeconds(55));Assert.True(s.IsDue(old));
    }
    [Fact] public void NewWeeklyResetGetsItsOwnRetryMinuteWhileShortResetIsStillPending()
    {
        var time=new FakeTimeProvider(Start);var schedule=new RefreshSchedule(time);
        var limits=new Limits(new(.8,300,Start.AddSeconds(-1)),new(.5,10080,Start.AddMinutes(2)));
        schedule.Started();schedule.Succeeded(limits);
        time.Advance(TimeSpan.FromMinutes(1));schedule.Started();schedule.Succeeded(limits);
        time.Advance(TimeSpan.FromMinutes(1));Assert.True(schedule.IsDue(limits));
        schedule.Started();schedule.Succeeded(limits);
        time.Advance(TimeSpan.FromSeconds(4));Assert.False(schedule.IsDue(limits));
        time.Advance(TimeSpan.FromSeconds(1));Assert.True(schedule.IsDue(limits));
    }
    [Fact] public void FailureBackoffAndExplicitForceAreBounded()
    {
        var time=new FakeTimeProvider(Start);var s=new RefreshSchedule(time);
        foreach(int minutes in new[]{1,2,5,15,15})
        {
            s.Started();s.Failed();
            time.Advance(TimeSpan.FromMinutes(minutes)-TimeSpan.FromSeconds(1));Assert.False(s.IsDue(Limits.Empty));
            time.Advance(TimeSpan.FromSeconds(1));Assert.True(s.IsDue(Limits.Empty));
        }
        s.Started();s.Force();Assert.True(s.IsDue(Limits.Empty));
    }
    private sealed class AdjustableClock(DateTimeOffset initial):TimeProvider
    {
        public DateTimeOffset Wall=initial;public long Seconds;
        public override long TimestampFrequency=>1;
        public override long GetTimestamp()=>Seconds;
        public override DateTimeOffset GetUtcNow()=>Wall;
    }
}
