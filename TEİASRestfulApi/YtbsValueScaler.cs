namespace TEİASRestfulApi;

public readonly record struct YtbsScaleDecision(
    double Value,
    decimal? AppliedDivisor,
    bool Persist,
    bool OverLimit);

public static class YtbsValueScaler
{
    public const decimal MinReportableCapacityMw = 0.250m;
    public const decimal MinScaleRatio = 4m;

    public static readonly decimal[] DecadeDivisors = [10m, 100m, 1_000m, 10_000m, 100_000m, 1_000_000m];

    public static bool IsBelowMinCapacity(decimal? maxCapacity)
    {
        return maxCapacity is { } cap && cap < MinReportableCapacityMw;
    }

    /// <summary>
    /// Kayıtlı bölen varsa onu kullanır. Yoksa oran &gt;= 4 ise en küçük 10^n
    /// (değer/bölen &lt; limit) seçilir ve persist edilir. Oran &lt; 4 gerçek aşımdır.
    /// </summary>
    public static YtbsScaleDecision Apply(double value, decimal? maxCapacity, decimal? storedDivisor)
    {
        if (maxCapacity is null)
        {
            return new YtbsScaleDecision(value, null, Persist: false, OverLimit: false);
        }

        decimal cap = maxCapacity.Value;
        if (storedDivisor is { } stored && stored > 0)
        {
            double scaled = Divide(value, stored);
            return new YtbsScaleDecision(scaled, stored, Persist: false, OverLimit: Exceeds(scaled, cap));
        }

        if (!Exceeds(value, cap))
        {
            return new YtbsScaleDecision(value, null, Persist: false, OverLimit: false);
        }

        decimal ratio = (decimal)value / cap;
        if (ratio < MinScaleRatio)
        {
            return new YtbsScaleDecision(value, null, Persist: false, OverLimit: true);
        }

        foreach (decimal divisor in DecadeDivisors)
        {
            double scaled = Divide(value, divisor);
            if (!Exceeds(scaled, cap))
            {
                return new YtbsScaleDecision(scaled, divisor, Persist: true, OverLimit: false);
            }
        }

        return new YtbsScaleDecision(value, null, Persist: false, OverLimit: true);
    }

    private static double Divide(double value, decimal divisor)
    {
        return ScadaValueNormalizer.ToApiValue((decimal)value / divisor);
    }

    private static bool Exceeds(double value, decimal cap)
    {
        return (decimal)value >= cap;
    }
}
