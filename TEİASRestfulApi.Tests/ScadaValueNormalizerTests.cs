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
    public void Sanitize_flips_negative_and_caps_to_capacity()
    {
        Assert.Equal(0.4m, ScadaValueNormalizer.SanitizeNonNegativeCapped(-0.4m, 1m));
        Assert.Equal(0.999m, ScadaValueNormalizer.SanitizeNonNegativeCapped(1.5m, 0.999m));
        Assert.Equal(1.5m, ScadaValueNormalizer.SanitizeNonNegativeCapped(1.5m, null));
    }

    [Fact]
    public void Normalize_converts_kw_then_caps_in_mw()
    {
        Assert.Equal(0.5m, ScadaValueNormalizer.Normalize(850m, 0.5m, isKilowatt: true));
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
            x => x.MaxCapacity,
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
    public void ToApiValue_rounds_away_from_zero_to_4_decimals()
    {
        Assert.Equal(1.2346, ScadaValueNormalizer.ToApiValue(1.23455m));
    }
}
