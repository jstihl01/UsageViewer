namespace UsageViewer;

internal sealed class RefreshSchedule
{
    private DateTimeOffset nextPoll;
    private DateTimeOffset lastCheck;

    public bool IsDue(DateTimeOffset now, Limits limits) =>
        now >= nextPoll || Crossed(limits.Short, now) || Crossed(limits.Weekly, now);

    public void Started(DateTimeOffset now)
    {
        lastCheck = now;
        nextPoll = now.AddMinutes(1);
    }

    private bool Crossed(Cycle? cycle, DateTimeOffset now) =>
        cycle is not null && cycle.Reset > lastCheck && cycle.Reset <= now;
}
