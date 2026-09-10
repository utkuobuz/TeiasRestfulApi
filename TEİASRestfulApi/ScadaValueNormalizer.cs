using TEİASRestfulApi.DTOs;

namespace TEİASRestfulApi;

public readonly record struct OverLimitSkip(int PlantId, double? Value, decimal Limit);

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

    public static decimal SanitizeNonNegative(decimal valueMw)
    {
        return valueMw < 0 ? -valueMw : valueMw;
    }

    public static decimal Normalize(decimal rawValue, bool isKilowatt)
    {
        return SanitizeNonNegative(ToMegawatts(rawValue, isKilowatt));
    }

    public static double ToApiValue(decimal valueMw)
    {
        return Math.Round((double)valueMw, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// TEİAŞ üst limiti dahil ve üzerini reddeder (0,783 == 0,783 → 412).
    /// Ölçümü tıraşlamayız; bu santral paketten çıkar, diğerleri gider.
    /// </summary>
    public static bool ExceedsTeiasLimit(double? value, decimal? maxCapacity)
    {
        if (value is null || maxCapacity is null)
        {
            return false;
        }

        return (decimal)value.Value >= maxCapacity.Value;
    }

    public static List<UretimVeriItem> TakeAboveMinCapacity(
        IEnumerable<UretimVeriItem> items,
        IReadOnlyDictionary<int, decimal?> capacityByPlant,
        out List<int> dropped)
    {
        dropped = [];
        var kept = new List<UretimVeriItem>();
        foreach (var item in items)
        {
            capacityByPlant.TryGetValue(item.lisanssizSantralId, out decimal? cap);
            if (YtbsValueScaler.IsBelowMinCapacity(cap))
            {
                dropped.Add(item.lisanssizSantralId);
                continue;
            }

            kept.Add(item);
        }

        return kept;
    }

    public static List<UretimVeriItem> TakeWithinTeiasLimit(
        IEnumerable<UretimVeriItem> items,
        IReadOnlyDictionary<int, decimal?> capacityByPlant,
        out List<OverLimitSkip> skipped)
    {
        skipped = [];
        var kept = new List<UretimVeriItem>();
        foreach (var item in items)
        {
            capacityByPlant.TryGetValue(item.lisanssizSantralId, out decimal? cap);
            if (ExceedsTeiasLimit(item.veriDeger, cap) && cap is { } limit)
            {
                skipped.Add(new OverLimitSkip(item.lisanssizSantralId, item.veriDeger, limit));
                continue;
            }

            kept.Add(item);
        }

        return kept;
    }

    public static Dictionary<int, decimal?> CapacityByPlant<T>(
        IEnumerable<T> rows,
        Func<T, int> plantId,
        Func<T, decimal?> maxCapacity)
    {
        return rows
            .GroupBy(plantId)
            .ToDictionary(g => g.Key, g => g.Select(maxCapacity).FirstOrDefault());
    }

    public static List<UretimVeriItem> BuildItems<T>(
        IEnumerable<T> rows,
        Func<T, int> plantId,
        Func<T, decimal> rawValue,
        DateTime slot,
        bool isKilowatt)
    {
        return BuildItems(rows, plantId, rawValue, YtbsTimeSlots.ToIntervalEndSlot(slot), isKilowatt);
    }

    public static List<UretimVeriItem> BuildItems<T>(
        IEnumerable<T> rows,
        Func<T, int> plantId,
        Func<T, decimal> rawValue,
        YtbsApiSlot slot,
        bool isKilowatt)
    {
        return rows
            .GroupBy(plantId)
            .Select(group =>
            {
                decimal toplam = group.Sum(rawValue);
                return new UretimVeriItem
                {
                    lisanssizSantralId = group.Key,
                    tarih = slot.Tarih,
                    saat = slot.Saat,
                    veriDeger = ToApiValue(Normalize(toplam, isKilowatt))
                };
            })
            .ToList();
    }
}
