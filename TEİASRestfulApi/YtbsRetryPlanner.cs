namespace TEİASRestfulApi;

public readonly record struct YtbsRetrySlot(YtbsChannel Channel, DateTime Instant);

public static class YtbsRetryPlanner
{
    public const int LookbackDays = 4;
    public const int MaxSlotsPerCycle = 12;
    public static readonly TimeSpan AttemptCooldown = TimeSpan.FromMinutes(15);

    public static DateTime LookbackStart(DateTime now) => now.AddDays(-LookbackDays);

    public static IEnumerable<DateTime> AnlikInstants(DateTime fromExclusive, DateTime lastInclusive)
    {
        DateTime first = YtbsTimeSlots.GetCurrentQuarterStart(fromExclusive);
        if (first < fromExclusive)
        {
            first = first.AddMinutes(15);
        }

        for (DateTime t = first; t <= lastInclusive; t = t.AddMinutes(15))
        {
            yield return t;
        }
    }

    public static IEnumerable<DateTime> SaatlikInstants(DateTime fromExclusive, DateTime lastInclusive)
    {
        DateTime first = YtbsTimeSlots.GetCompletedHourEnd(fromExclusive);
        if (first < fromExclusive)
        {
            first = first.AddHours(1);
        }

        for (DateTime t = first; t <= lastInclusive; t = t.AddHours(1))
        {
            yield return t;
        }
    }

    public static List<YtbsRetrySlot> UnresolvedSlots(
        DateTime fromExclusive,
        DateTime lastAnlikInclusive,
        DateTime lastSaatlikInclusive,
        IReadOnlyCollection<int> livePlantIds,
        IReadOnlySet<(YtbsChannel Channel, DateTime Instant, int PlantId)> resolved)
    {
        var due = new List<YtbsRetrySlot>();
        if (livePlantIds.Count == 0)
        {
            return due;
        }

        AppendUnresolved(due, YtbsChannel.Anlik, AnlikInstants(fromExclusive, lastAnlikInclusive), livePlantIds, resolved);
        AppendUnresolved(due, YtbsChannel.Saatlik, SaatlikInstants(fromExclusive, lastSaatlikInclusive), livePlantIds, resolved);
        return due;
    }

    public static List<YtbsRetrySlot> PickDue(
        IEnumerable<YtbsRetrySlot> unresolved,
        IReadOnlyDictionary<(YtbsChannel Channel, DateTime Instant), DateTime> lastAttempt,
        DateTime now,
        int budget = MaxSlotsPerCycle)
    {
        return unresolved
            .OrderBy(s => s.Instant)
            .ThenBy(s => s.Channel)
            .Where(s => !lastAttempt.TryGetValue((s.Channel, s.Instant), out DateTime attempted)
                || now - attempted >= AttemptCooldown)
            .Take(Math.Max(0, budget))
            .ToList();
    }

    private static void AppendUnresolved(
        List<YtbsRetrySlot> due,
        YtbsChannel channel,
        IEnumerable<DateTime> instants,
        IReadOnlyCollection<int> livePlantIds,
        IReadOnlySet<(YtbsChannel Channel, DateTime Instant, int PlantId)> resolved)
    {
        foreach (DateTime instant in instants)
        {
            bool missing = livePlantIds.Any(id => !resolved.Contains((channel, instant, id)));
            if (missing)
            {
                due.Add(new YtbsRetrySlot(channel, instant));
            }
        }
    }
}
