using TEİASRestfulApi;
using TEİASRestfulApi.DTOs;
using Xunit;

namespace TEİASRestfulApi.Tests;

public class YtbsValueScalerTests
{
    [Fact]
    public void Below_0250_is_silent_min_capacity()
    {
        Assert.True(YtbsValueScaler.IsBelowMinCapacity(0.200m));
        Assert.True(YtbsValueScaler.IsBelowMinCapacity(0.249m));
        Assert.False(YtbsValueScaler.IsBelowMinCapacity(0.250m));
        Assert.False(YtbsValueScaler.IsBelowMinCapacity(0.310m));
        Assert.False(YtbsValueScaler.IsBelowMinCapacity(null));
    }

    [Fact]
    public void TakeAboveMinCapacity_drops_gelal_keeps_siblings()
    {
        var items = new[]
        {
            new UretimVeriItem { lisanssizSantralId = 7334, veriDeger = 1.494 },
            new UretimVeriItem { lisanssizSantralId = 8306, veriDeger = 1.2 }
        };
        var caps = new Dictionary<int, decimal?> { [7334] = 0.200m, [8306] = 2.420m };

        var kept = ScadaValueNormalizer.TakeAboveMinCapacity(items, caps, out List<int> dropped);

        Assert.Single(dropped);
        Assert.Equal(7334, dropped[0]);
        Assert.Single(kept);
        Assert.Equal(8306, kept[0].lisanssizSantralId);
    }

    [Fact]
    public void Agar_ratio_below_4_is_real_over_limit_no_scale()
    {
        YtbsScaleDecision d = YtbsValueScaler.Apply(0.655, 0.310m, storedDivisor: null);
        Assert.Equal(0.655, d.Value);
        Assert.Null(d.AppliedDivisor);
        Assert.False(d.Persist);
        Assert.True(d.OverLimit);
    }

    [Fact]
    public void User_example_127_over_0284_divides_by_10()
    {
        YtbsScaleDecision d = YtbsValueScaler.Apply(1.27, 0.284m, storedDivisor: null);
        Assert.Equal(0.127, d.Value);
        Assert.Equal(10m, d.AppliedDivisor);
        Assert.True(d.Persist);
        Assert.False(d.OverLimit);
    }

    [Fact]
    public void Dilovasi_hourly_divides_by_1000()
    {
        YtbsScaleDecision d = YtbsValueScaler.Apply(129.6, 0.366m, storedDivisor: null);
        Assert.Equal(0.1296, d.Value);
        Assert.Equal(1000m, d.AppliedDivisor);
        Assert.True(d.Persist);
        Assert.False(d.OverLimit);
    }

    [Fact]
    public void Omafil_hourly_divides_by_1e6()
    {
        YtbsScaleDecision d = YtbsValueScaler.Apply(568117.69, 0.686m, storedDivisor: null);
        Assert.Equal(0.5681, d.Value);
        Assert.Equal(1_000_000m, d.AppliedDivisor);
        Assert.True(d.Persist);
        Assert.False(d.OverLimit);
    }

    [Fact]
    public void Stored_divisor_is_reused_without_persist()
    {
        YtbsScaleDecision d = YtbsValueScaler.Apply(129.6, 0.366m, storedDivisor: 1000m);
        Assert.Equal(0.1296, d.Value);
        Assert.Equal(1000m, d.AppliedDivisor);
        Assert.False(d.Persist);
        Assert.False(d.OverLimit);
    }

    [Fact]
    public void Null_capacity_does_not_scale()
    {
        YtbsScaleDecision d = YtbsValueScaler.Apply(676.224, null, storedDivisor: null);
        Assert.Equal(676.224, d.Value);
        Assert.False(d.Persist);
        Assert.False(d.OverLimit);
    }

    [Fact]
    public void Already_under_limit_does_not_scale()
    {
        YtbsScaleDecision d = YtbsValueScaler.Apply(0.20, 0.310m, storedDivisor: null);
        Assert.Equal(0.20, d.Value);
        Assert.False(d.Persist);
        Assert.False(d.OverLimit);
    }

    [Fact]
    public void Kubra_slight_over_stays_fail_no_scale()
    {
        YtbsScaleDecision d = YtbsValueScaler.Apply(2.023, 1.858m, storedDivisor: null);
        Assert.Equal(2.023, d.Value);
        Assert.True(d.OverLimit);
        Assert.False(d.Persist);
    }

    [Fact]
    public void TakeWithinTeiasLimit_keeps_sibling_when_agar_fails()
    {
        var items = new[]
        {
            new UretimVeriItem { lisanssizSantralId = 11551, veriDeger = 0.655 },
            new UretimVeriItem { lisanssizSantralId = 11550, veriDeger = 0.05 }
        };
        var caps = new Dictionary<int, decimal?> { [11551] = 0.310m, [11550] = 0.500m };

        var kept = ScadaValueNormalizer.TakeWithinTeiasLimit(items, caps, out var skipped);

        Assert.Single(kept);
        Assert.Equal(11550, kept[0].lisanssizSantralId);
        Assert.Single(skipped);
        Assert.Equal(11551, skipped[0].PlantId);
    }
}
