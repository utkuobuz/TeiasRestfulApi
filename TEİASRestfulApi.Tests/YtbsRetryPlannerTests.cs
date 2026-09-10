using TEİASRestfulApi;
using Xunit;

namespace TEİASRestfulApi.Tests;

public class YtbsRetryPlannerTests
{
    [Fact]
    public void Anlik_first_slot_is_next_quarter_after_cutoff()
    {
        DateTime from = DateTime.Parse("2026-09-06 14:03:00");
        DateTime last = DateTime.Parse("2026-09-06 15:00:00");
        List<DateTime> slots = YtbsRetryPlanner.AnlikInstants(from, last).ToList();
        Assert.Equal(DateTime.Parse("2026-09-06 14:15:00"), slots[0]);
        Assert.Equal(DateTime.Parse("2026-09-06 15:00:00"), slots[^1]);
        Assert.Equal(4, slots.Count);
    }

    [Fact]
    public void Saatlik_includes_completed_hour_on_exact_cutoff()
    {
        DateTime from = DateTime.Parse("2026-09-06 14:00:00");
        DateTime last = DateTime.Parse("2026-09-06 16:00:00");
        List<DateTime> slots = YtbsRetryPlanner.SaatlikInstants(from, last).ToList();
        Assert.Equal(new[]
        {
            DateTime.Parse("2026-09-06 14:00:00"),
            DateTime.Parse("2026-09-06 15:00:00"),
            DateTime.Parse("2026-09-06 16:00:00")
        }, slots);
    }

    [Fact]
    public void Unresolved_skips_slot_when_every_live_plant_is_resolved()
    {
        DateTime slot = DateTime.Parse("2026-09-10 01:00:00");
        var resolved = new HashSet<(YtbsChannel, DateTime, int)>
        {
            (YtbsChannel.Anlik, slot, 1),
            (YtbsChannel.Saatlik, slot, 1)
        };

        List<YtbsRetrySlot> due = YtbsRetryPlanner.UnresolvedSlots(
            slot.AddMinutes(-1),
            slot,
            slot,
            [1],
            resolved);

        Assert.Empty(due);
    }

    [Fact]
    public void Unresolved_keeps_slot_when_one_plant_missing()
    {
        DateTime slot = DateTime.Parse("2026-09-10 01:00:00");
        var resolved = new HashSet<(YtbsChannel, DateTime, int)>
        {
            (YtbsChannel.Anlik, slot, 1)
        };

        List<YtbsRetrySlot> due = YtbsRetryPlanner.UnresolvedSlots(
            slot.AddMinutes(-1),
            slot,
            slot,
            [1, 11069],
            resolved);

        Assert.Contains(due, s => s.Channel == YtbsChannel.Anlik && s.Instant == slot);
        Assert.Contains(due, s => s.Channel == YtbsChannel.Saatlik && s.Instant == slot);
    }

    [Fact]
    public void PickDue_respects_cooldown_and_budget()
    {
        DateTime now = DateTime.Parse("2026-09-10 14:00:00");
        var unresolved = new[]
        {
            new YtbsRetrySlot(YtbsChannel.Anlik, now.AddHours(-2)),
            new YtbsRetrySlot(YtbsChannel.Anlik, now.AddHours(-1)),
            new YtbsRetrySlot(YtbsChannel.Saatlik, now.AddHours(-1))
        };
        var attempts = new Dictionary<(YtbsChannel, DateTime), DateTime>
        {
            [(YtbsChannel.Anlik, now.AddHours(-2))] = now.AddMinutes(-5)
        };

        List<YtbsRetrySlot> due = YtbsRetryPlanner.PickDue(unresolved, attempts, now, budget: 1);

        Assert.Single(due);
        Assert.Equal(now.AddHours(-1), due[0].Instant);
        Assert.Equal(YtbsChannel.Anlik, due[0].Channel);
    }
}
