using System.Text.Json;

namespace UsageViewer;

internal record Cycle(double Used, int Minutes, DateTimeOffset Reset)
{
    public double Elapsed(DateTimeOffset now) =>
        Math.Clamp(1 - (Reset - now).TotalMinutes / Minutes, 0, 1);
}

internal record Limits(Cycle? Short, Cycle? Weekly, bool Stale = false)
{
    public static Limits Empty { get; } = new(null, null);
    public Limits MarkStale() => this with { Stale = true };

    public static Limits Parse(JsonElement result)
    {
        if (!(result.TryGetProperty("rateLimitsByLimitId", out var byId) &&
              byId.ValueKind == JsonValueKind.Object &&
              byId.TryGetProperty("codex", out var snapshot)))
        {
            if (!result.TryGetProperty("rateLimits", out snapshot) ||
                snapshot.ValueKind != JsonValueKind.Object ||
                (snapshot.TryGetProperty("limitId", out var id) &&
                 id.ValueKind != JsonValueKind.Null && id.GetString() != "codex"))
                return Empty;
        }

        var primary = ReadCycle(snapshot, "primary");
        var secondary = ReadCycle(snapshot, "secondary");
        return new(Find(300), Find(10080));

        Cycle? Find(int minutes) => primary?.Minutes == minutes ? primary :
            secondary?.Minutes == minutes ? secondary : null;
    }

    private static Cycle? ReadCycle(JsonElement snapshot, string key)
    {
        if (snapshot.ValueKind != JsonValueKind.Object ||
            !snapshot.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("windowDurationMins", out var duration) ||
            duration.ValueKind != JsonValueKind.Number || !duration.TryGetInt32(out int minutes) ||
            !value.TryGetProperty("usedPercent", out var percent) || percent.ValueKind != JsonValueKind.Number ||
            !percent.TryGetDouble(out double used) || !double.IsFinite(used) ||
            !value.TryGetProperty("resetsAt", out var reset) || reset.ValueKind != JsonValueKind.Number ||
            !reset.TryGetInt64(out long seconds) || seconds is <= 0 or > 253402300799)
            return null;
        return new(Math.Clamp(used / 100, 0, 1), minutes, DateTimeOffset.FromUnixTimeSeconds(seconds));
    }
}

internal enum Ink { Consumed, Remaining, Margin, Overlap }
internal enum Pace { Ahead, Behind, Even }
internal record Arc(double Start, double End, Ink Ink);

internal static class Ring
{
    public static IEnumerable<Arc> Arcs(Cycle? cycle, DateTimeOffset now)
    {
        if (cycle is null || cycle.Reset <= now)
            yield break;
        double elapsed = cycle.Elapsed(now);
        double used = cycle.Used;
        double consumedEnd = Math.Min(used, elapsed);
        if (consumedEnd > 0)
            yield return new(0, consumedEnd, Ink.Consumed);
        if (used < elapsed)
            yield return new(used, elapsed, Ink.Margin);
        if (used > elapsed)
            yield return new(elapsed, used, Ink.Overlap);
        if (Math.Max(used, elapsed) < 1)
            yield return new(Math.Max(used, elapsed), 1, Ink.Remaining);
    }

    public static Pace? GetPace(Cycle? cycle, DateTimeOffset now)
    {
        if (cycle is null || cycle.Reset <= now) return null;
        double difference = cycle.Used - cycle.Elapsed(now);
        return Math.Abs(difference) < 0.000001 ? Pace.Even :
            difference > 0 ? Pace.Ahead : Pace.Behind;
    }

    public static string? PaceLabel(Pace? pace) => pace switch
    {
        Pace.Ahead => "Por delante del tiempo",
        Pace.Behind => "Por detrás del tiempo",
        _ => null
    };

    public static Ink? PaceInk(Pace? pace) => pace switch
    {
        Pace.Behind => Ink.Margin,
        Pace.Ahead => Ink.Overlap,
        _ => null
    };
}
