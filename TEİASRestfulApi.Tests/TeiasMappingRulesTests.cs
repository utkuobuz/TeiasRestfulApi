using TEİASRestfulApi;
using Xunit;

namespace TEİASRestfulApi.Tests;

public class TeiasMappingRulesTests
{
    [Theory]
    [InlineData("BARTINOSB.GES.AYME.ActivePower", true)]
    [InlineData("AMASYAOSB.GES.ALBA.ActivePower", true)]
    [InlineData("MALATYAOSB.GES.FOO.ActivePower_PASIF", false)]
    [InlineData("MALATYAOSB.GES.FOO.ActiveEnergy.Exported.Hourly", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsLiveActivePowerVar_excludes_pasif_and_energy(string? varName, bool expected)
    {
        Assert.Equal(expected, TeiasMappingRules.IsLiveActivePowerVar(varName));
    }

    [Fact]
    public void IsPasifActivePowerVar_matches_suffix_only()
    {
        Assert.True(TeiasMappingRules.IsPasifActivePowerVar("X.GES.Y.ActivePower_PASIF"));
        Assert.False(TeiasMappingRules.IsPasifActivePowerVar("X.GES.Y.ActivePower"));
    }

    [Fact]
    public void LiveActivePowerSqlPredicate_does_not_use_unescaped_underscore_wildcard_for_pasif()
    {
        Assert.Equal("m.VAR_NAME LIKE '%.ActivePower'", TeiasMappingRules.LiveActivePowerSqlPredicate);
        Assert.DoesNotContain("_PASIF", TeiasMappingRules.LiveActivePowerSqlPredicate);
    }

    [Fact]
    public void ToHourlyEnergyVar_rewrites_only_live_active_power()
    {
        Assert.Equal(
            "DILOVASIOSB.GES.AYDINSAGLIKLIGIDA.ActiveEnergy.Exported.Hourly",
            TeiasMappingRules.ToHourlyEnergyVar("DILOVASIOSB.GES.AYDINSAGLIKLIGIDA.ActivePower"));
        Assert.Null(TeiasMappingRules.ToHourlyEnergyVar("X.GES.Y.ActivePower_PASIF"));
        Assert.Null(TeiasMappingRules.ToHourlyEnergyVar("X.GES.Y.ActiveEnergy.Exported"));
        Assert.Null(TeiasMappingRules.ToHourlyEnergyVar("X.GES.Y.ReactivePower"));
    }

    [Fact]
    public void Hourly_and_ignored_vars_never_count_as_live_power()
    {
        const string hourly = "X.GES.Y.ActiveEnergy.Exported.Hourly";
        const string exported = "X.GES.Y.ActiveEnergy.Exported";
        const string reactive = "X.GES.Y.ReactivePower";

        Assert.True(TeiasMappingRules.IsHourlyEnergyVar(hourly));
        Assert.False(TeiasMappingRules.IsHourlyEnergyVar(exported));
        Assert.False(TeiasMappingRules.IsLiveActivePowerVar(hourly));
        Assert.False(TeiasMappingRules.IsLiveActivePowerVar(exported));
        Assert.False(TeiasMappingRules.IsLiveActivePowerVar(reactive));
        Assert.True(TeiasMappingRules.IsIgnoredYtbsVar(exported));
        Assert.True(TeiasMappingRules.IsIgnoredYtbsVar(reactive));
        Assert.False(TeiasMappingRules.IsIgnoredYtbsVar(hourly));
    }

    [Fact]
    public void LatestValueByVar_keeps_one_hourly_copy_not_four()
    {
        var copies = new[]
        {
            ("P.ActiveEnergy.Exported.Hourly", DateTime.Parse("2026-09-07 14:00:00"), 1.10m),
            ("P.ActiveEnergy.Exported.Hourly", DateTime.Parse("2026-09-07 14:15:00"), 1.12m),
            ("P.ActiveEnergy.Exported.Hourly", DateTime.Parse("2026-09-07 14:30:00"), 1.18m),
            ("P.ActiveEnergy.Exported.Hourly", DateTime.Parse("2026-09-07 14:45:00"), 1.20m),
            ("Q.ActiveEnergy.Exported.Hourly", DateTime.Parse("2026-09-07 14:45:00"), 0.40m),
        };

        var latest = TeiasMappingRules.LatestValueByVar(copies);

        Assert.Equal(2, latest.Count);
        Assert.Equal(1.20m, latest["P.ActiveEnergy.Exported.Hourly"]);
        Assert.Equal(0.40m, latest["Q.ActiveEnergy.Exported.Hourly"]);
        Assert.NotEqual(1.10m + 1.12m + 1.18m + 1.20m, latest["P.ActiveEnergy.Exported.Hourly"]);
    }

    [Fact]
    public void HourlyEnergyVarSql_does_not_join_on_active_power_name()
    {
        Assert.Contains(".ActiveEnergy.Exported.Hourly", TeiasMappingRules.HourlyEnergyVarSql);
        Assert.DoesNotContain("d.VAR = m.VAR_NAME", TeiasMappingRules.HourlyEnergyVarSql);
    }
}
