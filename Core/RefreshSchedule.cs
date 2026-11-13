namespace UsageViewer.Core;

public sealed class RefreshSchedule(TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private long checkedAt;
    private DateTimeOffset checkedWall;
    private TimeSpan interval;
    private bool started;
    private int failures;
    private sealed record PendingReset(DateTimeOffset Reset,long Since);
    private PendingReset? shortWaiting,weeklyWaiting;
    private bool forced;

    public bool IsDue(Limits limits) => forced || !started ||
        clock.GetElapsedTime(checkedAt) >= interval ||
        Crossed(limits.Short) || Crossed(limits.Weekly);

    private bool Crossed(Cycle? cycle) =>
        cycle?.Reset is { } reset && reset > checkedWall && reset <= clock.GetUtcNow();

    public void Force() => forced = true;
    public void Started()
    {
        started = true;
        forced = false;
        checkedAt = clock.GetTimestamp();
        checkedWall = clock.GetUtcNow();
        interval = TimeSpan.FromSeconds(60);
    }

    public void Succeeded(Limits limits)
    {
        failures = 0;
        bool shortRetry=RetryReset(limits.Short,ref shortWaiting);
        bool weeklyRetry=RetryReset(limits.Weekly,ref weeklyWaiting);
        interval = shortRetry || weeklyRetry ?
            TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(60);
    }

    private bool RetryReset(Cycle? cycle,ref PendingReset? pending)
    {
        if(cycle?.Reset is not {} reset || !cycle.Expired(clock.GetUtcNow()))
        {pending=null;return false;}
        if(pending?.Reset!=reset)pending=new(reset,clock.GetTimestamp());
        return clock.GetElapsedTime(pending!.Since)<TimeSpan.FromMinutes(1);
    }

    public void Failed()
    {
        int[] minutes = [1, 2, 5, 15];
        interval = TimeSpan.FromMinutes(minutes[Math.Min(failures++, minutes.Length - 1)]);
    }
}
