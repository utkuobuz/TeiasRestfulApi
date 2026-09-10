using TEİASRestfulApi;
using TEİASRestfulApi.DTOs;
using Xunit;

namespace TEİASRestfulApi.Tests;

public class ScadaValueNormalizerTests
{
    [Fact]
    public void IsKilowatt_accepts_kw_aliases()
    {
        Assert.True(ScadaValueNormalizer.IsKilowatt("kW"));
        Assert.True(ScadaValueNormalizer.IsKilowatt("KW"));
        Assert.True(ScadaValueNormalizer.IsKilowatt("kilowatt"));
        Assert.False(ScadaValueNormalizer.IsKilowatt("MW"));
        Assert.False(ScadaValueNormalizer.IsKilowatt(null));
    }

    [Fact]
    public void ToMegawatts_divides_only_when_unit_is_kw()
    {
        Assert.Equal(0.85m, ScadaValueNormalizer.ToMegawatts(850m, isKilowatt: true));
        Assert.Equal(0.85m, ScadaValueNormalizer.ToMegawatts(0.85m, isKilowatt: false));
    }

    [Fact]
    public void Sanitize_flips_negative_and_does_not_invent_a_cap()
    {
        Assert.Equal(0.4m, ScadaValueNormalizer.SanitizeNonNegative(-0.4m));
        Assert.Equal(1.5m, ScadaValueNormalizer.SanitizeNonNegative(1.5m));
    }

    [Fact]
    public void Normalize_converts_kw_without_clipping()
    {
        Assert.Equal(0.85m, ScadaValueNormalizer.Normalize(850m, isKilowatt: true));
    }

    [Fact]
    public void BuildItems_sums_same_plant_regardless_of_source_rows()
    {
        var slot = DateTime.Parse("2026-09-02 14:15:00");
        var rows = new[]
        {
            new ScadaDbRow { LisanssizSantralId = 10, AktifGuc = 0.20m, MaxCapacity = 1m },
            new ScadaDbRow { LisanssizSantralId = 10, AktifGuc = 0.15m, MaxCapacity = 1m },
            new ScadaDbRow { LisanssizSantralId = 11, AktifGuc = 0.40m, MaxCapacity = 1m }
        };

        var items = ScadaValueNormalizer.BuildItems(
            rows,
            x => x.LisanssizSantralId,
            x => x.AktifGuc,
            slot,
            isKilowatt: false);

        Assert.Equal(2, items.Count);
        var plant10 = items.Single(i => i.lisanssizSantralId == 10);
        Assert.Equal("2026-09-02", plant10.tarih);
        Assert.Equal("14:15", plant10.saat);
        Assert.Equal(0.35, plant10.veriDeger);
        Assert.Equal(0.4, items.Single(i => i.lisanssizSantralId == 11).veriDeger);
    }

    [Fact]
    public void BuildItems_midnight_uses_previous_calendar_day_and_2400()
    {
        var slot = DateTime.Parse("2026-09-03 00:00:00");
        var rows = new[]
        {
            new ScadaDbRow { LisanssizSantralId = 10, AktifGuc = 0.20m, MaxCapacity = 1m }
        };

        var items = ScadaValueNormalizer.BuildItems(
            rows,
            x => x.LisanssizSantralId,
            x => x.AktifGuc,
            slot,
            isKilowatt: false);

        Assert.Single(items);
        Assert.Equal("2026-09-02", items[0].tarih);
        Assert.Equal("24:00", items[0].saat);
        Assert.Equal(0.2, items[0].veriDeger);
    }

    [Fact]
    public void ToApiValue_rounds_away_from_zero_to_4_decimals()
    {
        Assert.Equal(1.2346, ScadaValueNormalizer.ToApiValue(1.23455m));
    }

    [Fact]
    public void ExceedsTeiasLimit_rejects_equal_or_above_keeps_null_capacity()
    {
        Assert.True(ScadaValueNormalizer.ExceedsTeiasLimit(0.783, 0.783m));
        Assert.True(ScadaValueNormalizer.ExceedsTeiasLimit(3.244, 1.100m));
        Assert.False(ScadaValueNormalizer.ExceedsTeiasLimit(0.782, 0.783m));
        Assert.False(ScadaValueNormalizer.ExceedsTeiasLimit(3.244, null));
    }

    [Fact]
    public void TakeWithinTeiasLimit_drops_only_the_over_limit_plant()
    {
        var items = new[]
        {
            new UretimVeriItem { lisanssizSantralId = 8427, veriDeger = 3.244 },
            new UretimVeriItem { lisanssizSantralId = 9856, veriDeger = 1.0 }
        };
        var caps = new Dictionary<int, decimal?> { [8427] = 1.100m, [9856] = 1.36m };

        var kept = ScadaValueNormalizer.TakeWithinTeiasLimit(items, caps, out var skipped);

        Assert.Single(kept);
        Assert.Equal(9856, kept[0].lisanssizSantralId);
        Assert.Equal(1.0, kept[0].veriDeger);
        Assert.Single(skipped);
        Assert.Equal(8427, skipped[0].PlantId);
        Assert.Equal(3.244, skipped[0].Value);
        Assert.Equal(1.100m, skipped[0].Limit);
    }
}
