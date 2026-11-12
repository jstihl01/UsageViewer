using System.Text.Json;
using UsageViewer;

var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
var reset = start.AddHours(5);
var now = start.AddHours(2);

CheckArcs(.2, now, new(0, .2, Ink.Consumed), new(.2, .4, Ink.Margin), new(.4, 1, Ink.Remaining));
CheckArcs(.4, now, new(0, .4, Ink.Consumed), new(.4, 1, Ink.Remaining));
CheckArcs(.6, now, new(0, .4, Ink.Consumed), new(.4, .6, Ink.Overlap), new(.6, 1, Ink.Remaining));
CheckArcs(0, now, new(0, .4, Ink.Margin), new(.4, 1, Ink.Remaining));
CheckArcs(1, now, new(0, .4, Ink.Consumed), new(.4, 1, Ink.Overlap));
CheckArcs(0, start, new Arc(0, 1, Ink.Remaining));
CheckArcs(1, start, new Arc(0, 1, Ink.Overlap));
CheckArcs(1, reset);
Require(!Ring.Arcs(null, now).Any(), "missing cycle has no arcs");
Require(Ring.GetPace(new(.4, 300, reset), now) == Pace.Even, "equal pace");
Require(Ring.GetPace(new(.6, 300, reset), now) == Pace.Ahead, "ahead of elapsed time");
Require(Ring.GetPace(new(.2, 300, reset), now) == Pace.Behind, "behind elapsed time");
Require(Ring.PaceInk(Pace.Ahead) == Ink.Overlap, "ahead label uses overlap color");
Require(Ring.PaceInk(Pace.Behind) == Ink.Margin, "behind label uses margin color");
Require(Ring.PaceInk(Pace.Even) is null && Ring.PaceInk(null) is null, "even or unknown pace has no accent");
Require(Ring.PaceLabel(Pace.Even) is null, "even pace has no label");
Require(new Cycle(0, 300, reset).Elapsed(start.AddDays(-1)) == 0, "clock before start");

using var response = JsonDocument.Parse("""
    {"rateLimits":{"limitId":"other"},"rateLimitsByLimitId":{"codex":{
    "primary":{"windowDurationMins":10080,"usedPercent":21,"resetsAt":1767830400},
    "secondary":{"windowDurationMins":300,"usedPercent":40,"resetsAt":1767243600}}}}
    """);
var limits = Limits.Parse(response.RootElement);
Require(limits.Short?.Reset == reset && limits.Short.Used == .4, "short cycle selected by duration");
Require(limits.Weekly?.Used == .21, "weekly cycle selected by duration");
using var partial = JsonDocument.Parse("""
    {"rateLimits":{"limitId":"codex","primary":{"windowDurationMins":300,"usedPercent":100,"resetsAt":1767243600},"secondary":null}}
    """);
var shortOnly = Limits.Parse(partial.RootElement);
Require(shortOnly.Short?.Used == 1 && shortOnly.Weekly is null, "missing weekly quota");
var stale = shortOnly.MarkStale();
Require(stale.Stale && stale.Short == shortOnly.Short, "network failure preserves last reading");
Require(!Ring.Arcs(stale.Short, reset).Any(), "expired reading hides arcs");

var schedule = new RefreshSchedule();
schedule.Started(reset.AddSeconds(-15));
Require(!schedule.IsDue(reset.AddSeconds(-1), limits), "wait until reset");
Require(schedule.IsDue(reset, limits), "refresh at reset");
schedule.Started(reset);
Require(!schedule.IsDue(reset.AddSeconds(1), limits), "avoid retrying an unconfirmed reset every second");
Require(schedule.IsDue(reset.AddHours(2), limits), "refresh when clock advances on resume");

Console.WriteLine("OK: arcs, quota parsing, stale readings, reset and resume scheduling.");
if (args.Length > 0)
{
    using var server = await CodexConnection.StartAsync(Path.GetFullPath(args[0]), CancellationToken.None);
    var account = await server.RequestAsync("account/read", new { refreshToken = false }, CancellationToken.None);
    Require(account.GetProperty("account").ValueKind == JsonValueKind.Null, "isolated profile starts signed out");
    Console.WriteLine("OK: pinned server initialization and signed-out account read over stdio.");
}

void CheckArcs(double used, DateTimeOffset time, params Arc[] expected)
{
    var arcs = Ring.Arcs(new Cycle(used, 300, reset), time).ToArray();
    Require(arcs.Length == expected.Length, "arc count");
    for (int index = 0; index < arcs.Length; index++)
    {
        var actual = arcs[index];
        var target = expected[index];
        Require(actual.Ink == target.Ink && Math.Abs(actual.Start - target.Start) < 1e-9 &&
            Math.Abs(actual.End - target.End) < 1e-9, "arc boundaries and color");
    }
}

static void Require(bool result, string description)
{
    if (!result) throw new InvalidOperationException(description);
}
