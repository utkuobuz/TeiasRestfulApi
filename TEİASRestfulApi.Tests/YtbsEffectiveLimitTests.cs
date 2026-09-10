using TEİASRestfulApi;
using Xunit;

namespace TEİASRestfulApi.Tests;

public class YtbsEffectiveLimitTests
{
    [Fact]
    public void Combine_uses_reported_when_mapping_is_null()
    {
        Assert.Equal(1.045m, YtbsEffectiveLimit.Combine(null, 1.045m));
    }

    [Fact]
    public void Combine_uses_mapping_when_reported_is_null()
    {
        Assert.Equal(2.42m, YtbsEffectiveLimit.Combine(2.42m, null));
    }

    [Fact]
    public void Combine_takes_the_stricter_limit()
    {
        Assert.Equal(0.310m, YtbsEffectiveLimit.Combine(0.655m, 0.310m));
        Assert.Equal(0.310m, YtbsEffectiveLimit.Combine(0.310m, 1.000m));
    }

    [Fact]
    public void ApplyReported_does_not_ban_a_plant_after_value_recovers()
    {
        var caps = new Dictionary<int, decimal?> { [2343] = null, [8427] = 1.100m };
        var reported = new Dictionary<int, decimal> { [2343] = 1.045m };

        YtbsEffectiveLimit.ApplyReported(caps, reported);

        Assert.Equal(1.045m, caps[2343]);
        Assert.Equal(1.100m, caps[8427]);
        Assert.False(ScadaValueNormalizer.ExceedsTeiasLimit(1.000, caps[2343]));
        Assert.True(ScadaValueNormalizer.ExceedsTeiasLimit(1.216, caps[2343]));
    }
}
