using TEİASRestfulApi;
using Xunit;

namespace TEİASRestfulApi.Tests;

public class YtbsTimeSlotsTests
{
    [Theory]
    [InlineData("2026-09-02 14:00:00", "2026-09-02 14:00:00")]
    [InlineData("2026-09-02 14:07:59", "2026-09-02 14:00:00")]
    [InlineData("2026-09-02 14:15:00", "2026-09-02 14:15:00")]
    [InlineData("2026-09-02 14:23:47", "2026-09-02 14:15:00")]
    [InlineData("2026-09-02 14:30:01", "2026-09-02 14:30:00")]
    [InlineData("2026-09-02 14:44:59", "2026-09-02 14:30:00")]
    [InlineData("2026-09-02 14:59:59", "2026-09-02 14:45:00")]
    public void GetCurrentQuarterStart_rounds_to_00_15_30_45(string nowText, string expectedText)
    {
        var now = DateTime.Parse(nowText);
        var expected = DateTime.Parse(expectedText);

        Assert.Equal(expected, YtbsTimeSlots.GetCurrentQuarterStart(now));
        Assert.Equal(expected.ToString("HH:mm"), YtbsTimeSlots.FormatSaat(expected));
    }

    [Fact]
    public void GetPreviousHourStart_uses_completed_hour_including_midnight()
    {
        var afternoon = DateTime.Parse("2026-09-02 15:05:00");
        Assert.Equal(DateTime.Parse("2026-09-02 14:00:00"), YtbsTimeSlots.GetPreviousHourStart(afternoon));
        Assert.Equal("14:00", YtbsTimeSlots.FormatSaat(YtbsTimeSlots.GetPreviousHourStart(afternoon)));

        var midnight = DateTime.Parse("2026-09-03 00:05:00");
        var previous = YtbsTimeSlots.GetPreviousHourStart(midnight);
        Assert.Equal(DateTime.Parse("2026-09-02 23:00:00"), previous);
        Assert.Equal("2026-09-02", YtbsTimeSlots.FormatTarih(previous));
        Assert.Equal("23:00", YtbsTimeSlots.FormatSaat(previous));
    }

    [Theory]
    [InlineData("2026-09-02 14:00:00", true)]
    [InlineData("2026-09-02 14:07:00", true)]
    [InlineData("2026-09-02 14:15:00", false)]
    [InlineData("2026-09-02 14:45:10", false)]
    public void IsHourlySlot_only_for_the_00_quarter(string nowText, bool expected)
    {
        Assert.Equal(expected, YtbsTimeSlots.IsHourlySlot(DateTime.Parse(nowText)));
    }

    [Fact]
    public void DelayUntilNextAlignedRun_runs_immediately_within_grace()
    {
        var now = DateTime.Parse("2026-09-02 14:15:10");
        Assert.Equal(TimeSpan.Zero, YtbsTimeSlots.DelayUntilNextAlignedRun(now, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void DelayUntilNextAlignedRun_waits_for_next_quarter_outside_grace()
    {
        var now = DateTime.Parse("2026-09-02 14:07:00");
        var delay = YtbsTimeSlots.DelayUntilNextAlignedRun(now, TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromMinutes(8), delay);
    }

    [Fact]
    public void DelayUntilNextQuarter_aligns_to_following_slot()
    {
        var now = DateTime.Parse("2026-09-02 14:15:20");
        Assert.Equal(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(40), YtbsTimeSlots.DelayUntilNextQuarter(now));
    }
}
