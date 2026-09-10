using TEİASRestfulApi;
using Xunit;

namespace TEİASRestfulApi.Tests;

public class PlantNameDirectoryTests
{
    [Fact]
    public void NameOf_uses_table_then_fallback()
    {
        var names = new PlantNameDirectory(new Dictionary<int, string> { [11027] = "GÜR KALIP 3 GES" });
        Assert.Equal("GÜR KALIP 3 GES", names.NameOf(11027));
        Assert.Equal("Santral 999", names.NameOf(999));
    }

    [Fact]
    public void FromVarName_strips_power_suffix()
    {
        Assert.Equal(
            "MERZIFONOSB.GES.GURKALIP3",
            PlantNameDirectory.FromVarName("MERZIFONOSB.GES.GURKALIP3.ActivePower"));
        Assert.Equal(
            "X.GES.Y",
            PlantNameDirectory.FromVarName("X.GES.Y.ActivePower_PASIF"));
    }
}
