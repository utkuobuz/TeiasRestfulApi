namespace TEİASRestfulApi;

public static class YtbsTimeSlots
{
    public static DateTime GetCurrentQuarterStart(DateTime now)
    {
        int minute = now.Minute / 15 * 15;
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, minute, 0, now.Kind);
    }

    public static DateTime GetNextQuarterStart(DateTime now)
    {
        return GetCurrentQuarterStart(now).AddMinutes(15);
    }

    public static DateTime GetPreviousHourStart(DateTime now)
    {
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Kind).AddHours(-1);
    }

    public static bool IsHourlySlot(DateTime now)
    {
        return GetCurrentQuarterStart(now).Minute == 0;
    }

    public static TimeSpan DelayUntilNextAlignedRun(DateTime now, TimeSpan startupGrace)
    {
        var current = GetCurrentQuarterStart(now);
        if (now - current <= startupGrace)
        {
            return TimeSpan.Zero;
        }

        var delay = GetNextQuarterStart(now) - now;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    public static TimeSpan DelayUntilNextQuarter(DateTime now)
    {
        var delay = GetNextQuarterStart(now) - now;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    public static string FormatTarih(DateTime slot) => slot.ToString("yyyy-MM-dd");

    public static string FormatSaat(DateTime slot) => slot.ToString("HH:mm");
}
