namespace TEİASRestfulApi;

/// <summary>
/// TEİAŞ <c>tarih</c>/<c>saat</c> etiketi. <see cref="Instant"/> dilim bitişinin duvar saati
/// (gece yarısı <c>00:00</c> → önceki gün <c>24:00</c>).
/// </summary>
public readonly record struct YtbsApiSlot(string Tarih, string Saat, DateTime Instant)
{
    public override string ToString() => $"{Tarih} {Saat}";
}

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

    /// <summary>
    /// Tamamlanmış saatin takvim başlangıcı (SQL penceresi / saatlik mail dönemi).
    /// TEİAŞ <c>saat</c> etiketi değildir; o için <see cref="ToSaatlikApiSlot"/>.
    /// </summary>
    public static DateTime GetPreviousHourStart(DateTime now)
    {
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Kind).AddHours(-1);
    }

    /// <summary>
    /// Tamamlanmış saatin bitişi (08:05 → 08:00; 00:05 → 00:00).
    /// </summary>
    public static DateTime GetCompletedHourEnd(DateTime now)
    {
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Kind);
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

    /// <summary>
    /// Anlık MW: son tamamlanmış 15 dk diliminin bitişi.
    /// 12:17 → 12:15; 12:00 → 12:00; 00:00–00:14 → önceki gün 24:00.
    /// </summary>
    public static YtbsApiSlot ToAnlikApiSlot(DateTime now)
    {
        return ToIntervalEndSlot(GetCurrentQuarterStart(now));
    }

    /// <summary>
    /// Saatlik MWh: tamamlanmış saatin bitişi.
    /// 08:05 → 08:00; 00:05 → önceki gün 24:00. SQL penceresi değişmez.
    /// </summary>
    public static YtbsApiSlot ToSaatlikApiSlot(DateTime now)
    {
        return ToIntervalEndSlot(GetCompletedHourEnd(now));
    }

    /// <summary>
    /// Dilim bitişi <c>00:00</c> ise TEİAŞ günü bir gün geri, saat <c>24:00</c>.
    /// </summary>
    public static YtbsApiSlot ToIntervalEndSlot(DateTime intervalEnd)
    {
        if (intervalEnd.TimeOfDay == TimeSpan.Zero)
        {
            DateTime previousDay = intervalEnd.Date.AddDays(-1);
            return new YtbsApiSlot(previousDay.ToString("yyyy-MM-dd"), "24:00", intervalEnd);
        }

        return new YtbsApiSlot(
            intervalEnd.ToString("yyyy-MM-dd"),
            intervalEnd.ToString("HH:mm"),
            intervalEnd);
    }

    public static string FormatTarih(DateTime slot) => slot.ToString("yyyy-MM-dd");

    public static string FormatSaat(DateTime slot) => slot.ToString("HH:mm");
}
