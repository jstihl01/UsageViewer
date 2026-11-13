using System.Text.Json;

namespace UsageViewer.Core;

public sealed record Cycle(double Used, int Minutes, DateTimeOffset? Reset)
{
    public bool Expired(DateTimeOffset now) => Reset is { } reset && reset <= now;
    public double? Elapsed(DateTimeOffset now) => Reset is { } reset ?
        Math.Clamp(1 - (reset - now).TotalMinutes / Minutes, 0, 1) : null;
}

public sealed record Limits(Cycle? Short, Cycle? Weekly, bool Stale = false)
{
    public static Limits Empty { get; } = new(null, null);
    public Limits MarkStale() => this with { Stale = true };
}

public enum ParseKind { Valid, NoMatchingWindows, Invalid }
public sealed record LimitsParseResult(ParseKind Kind, Limits Limits);

public static class QuotaParser
{
    public static LimitsParseResult Parse(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object) return Invalid();
        if (result.TryGetProperty("rateLimitsByLimitId",out var map) &&
            map.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null)) return Invalid();
        JsonElement snapshot;
        if (result.TryGetProperty("rateLimitsByLimitId", out var byId) &&
            byId.ValueKind == JsonValueKind.Object && byId.TryGetProperty("codex", out snapshot))
        { }
        else if (!result.TryGetProperty("rateLimits", out snapshot))
            return Invalid();
        if (snapshot.ValueKind == JsonValueKind.Null)
            return new(ParseKind.NoMatchingWindows, Limits.Empty);
        if (snapshot.ValueKind != JsonValueKind.Object) return Invalid();
        if (!snapshot.TryGetProperty("primary",out _) && !snapshot.TryGetProperty("secondary",out _)) return Invalid();
        if (snapshot.TryGetProperty("limitId", out var id) && id.ValueKind != JsonValueKind.Null)
        {
            if (id.ValueKind != JsonValueKind.String) return Invalid();
            if (id.GetString() != "codex") return new(ParseKind.NoMatchingWindows, Limits.Empty);
        }
        bool invalid = false;
        var primary = Read("primary");
        var secondary = Read("secondary");
        if (invalid) return Invalid();
        Cycle? Find(int minutes) => primary?.Minutes == minutes ? primary :
            secondary?.Minutes == minutes ? secondary : null;
        var limits = new Limits(Find(300), Find(10080));
        return new(limits.Short is null && limits.Weekly is null ?
            ParseKind.NoMatchingWindows : ParseKind.Valid, limits);

        Cycle? Read(string name)
        {
            if (!snapshot.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
                return null;
            if (value.ValueKind != JsonValueKind.Object ||
                !value.TryGetProperty("windowDurationMins", out var duration) ||
                !duration.TryGetInt32Safe(out int minutes) || minutes <= 0 ||
                !value.TryGetProperty("usedPercent", out var percent) ||
                percent.ValueKind != JsonValueKind.Number || !percent.TryGetDouble(out double used) ||
                !double.IsFinite(used))
            { invalid = true; return null; }
            DateTimeOffset? reset = null;
            if (value.TryGetProperty("resetsAt", out var time) && time.ValueKind != JsonValueKind.Null)
            {
                if (time.ValueKind != JsonValueKind.Number || !time.TryGetInt64(out long seconds) ||
                    seconds is <= 0 or > 253402300799)
                { invalid = true; return null; }
                reset = DateTimeOffset.FromUnixTimeSeconds(seconds);
            }
            return new(Math.Clamp(used / 100, 0, 1), minutes, reset);
        }
        static LimitsParseResult Invalid() => new(ParseKind.Invalid, Limits.Empty);
    }

    private static bool TryGetInt32Safe(this JsonElement element, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }
}

public enum SegmentKind { Consumed, Remaining, Margin, Overlap }
public enum Pace { Ahead, Behind, Even }
public sealed record Arc(double Start, double End, SegmentKind Kind);

public static class Ring
{
    public const double EqualityTolerance = 0.000001;

    public static IEnumerable<Arc> Arcs(Cycle? cycle, DateTimeOffset now)
    {
        if (cycle is null || cycle.Expired(now)) yield break;
        double used = cycle.Used;
        if (cycle.Elapsed(now) is not { } elapsed)
        {
            if (used > 0) yield return new(0, used, SegmentKind.Consumed);
            if (used < 1) yield return new(used, 1, SegmentKind.Remaining);
            yield break;
        }
        if (Math.Abs(used - elapsed) < EqualityTolerance) elapsed = used;
        if (Math.Min(used, elapsed) > 0) yield return new(0, Math.Min(used, elapsed), SegmentKind.Consumed);
        if (used < elapsed) yield return new(used, elapsed, SegmentKind.Margin);
        if (used > elapsed) yield return new(elapsed, used, SegmentKind.Overlap);
        if (Math.Max(used, elapsed) < 1) yield return new(Math.Max(used, elapsed), 1, SegmentKind.Remaining);
    }

    public static Pace? GetPace(Cycle? cycle, DateTimeOffset now)
    {
        if (cycle is null || cycle.Expired(now) || cycle.Elapsed(now) is not { } elapsed) return null;
        double difference = cycle.Used - elapsed;
        return Math.Abs(difference) < EqualityTolerance ? Pace.Even : difference > 0 ? Pace.Ahead : Pace.Behind;
    }
}
