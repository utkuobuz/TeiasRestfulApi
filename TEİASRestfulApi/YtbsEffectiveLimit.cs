namespace TEİASRestfulApi;

public static class YtbsEffectiveLimit
{
    /// <summary>
    /// TEİAŞ’tan öğrenilen limit varsa mapping ile birlikte en sıkı (küçük) olan kullanılır.
    /// Santral yasaklanmaz; her dilimde ölçüm bu limite karşı yeniden bakılır.
    /// </summary>
    public static decimal? Combine(decimal? mappingCapacity, decimal? reportedLimit)
    {
        if (mappingCapacity is { } mapping && reportedLimit is { } reported)
        {
            return Math.Min(mapping, reported);
        }

        return reportedLimit ?? mappingCapacity;
    }

    public static void ApplyReported(
        Dictionary<int, decimal?> caps,
        IReadOnlyDictionary<int, decimal> reported)
    {
        foreach (int plantId in caps.Keys.Concat(reported.Keys).Distinct().ToList())
        {
            caps.TryGetValue(plantId, out decimal? mapping);
            decimal? learned = reported.TryGetValue(plantId, out decimal limit) ? limit : null;
            caps[plantId] = Combine(mapping, learned);
        }
    }
}
