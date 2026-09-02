using TEİASRestfulApi.DTOs;

namespace TEİASRestfulApi;

public static class ScadaValueNormalizer
{
    public static bool IsKilowatt(string? unit)
    {
        return string.Equals(unit?.Trim(), "kW", StringComparison.OrdinalIgnoreCase)
            || string.Equals(unit?.Trim(), "kilowatt", StringComparison.OrdinalIgnoreCase);
    }

    public static decimal ToMegawatts(decimal rawValue, bool isKilowatt)
    {
        return isKilowatt ? rawValue / 1000m : rawValue;
    }

    public static decimal SanitizeNonNegativeCapped(decimal valueMw, decimal? maxCapacityMw)
    {
        if (valueMw < 0)
        {
            valueMw *= -1m;
        }

        if (maxCapacityMw is { } max && valueMw > max)
        {
            valueMw = max;
        }

        return valueMw;
    }

    public static decimal Normalize(decimal rawValue, decimal? maxCapacityMw, bool isKilowatt)
    {
        return SanitizeNonNegativeCapped(ToMegawatts(rawValue, isKilowatt), maxCapacityMw);
    }

    public static double ToApiValue(decimal valueMw)
    {
        return Math.Round((double)valueMw, 4, MidpointRounding.AwayFromZero);
    }

    public static List<UretimVeriItem> BuildItems<T>(
        IEnumerable<T> rows,
        Func<T, int> plantId,
        Func<T, decimal> rawValue,
        Func<T, decimal?> maxCapacity,
        DateTime slot,
        bool isKilowatt)
    {
        string tarih = YtbsTimeSlots.FormatTarih(slot);
        string saat = YtbsTimeSlots.FormatSaat(slot);

        return rows
            .GroupBy(plantId)
            .Select(group =>
            {
                decimal toplam = group.Sum(rawValue);
                decimal normalized = Normalize(toplam, group.Select(maxCapacity).FirstOrDefault(), isKilowatt);
                return new UretimVeriItem
                {
                    lisanssizSantralId = group.Key,
                    tarih = tarih,
                    saat = saat,
                    veriDeger = ToApiValue(normalized)
                };
            })
            .ToList();
    }
}
