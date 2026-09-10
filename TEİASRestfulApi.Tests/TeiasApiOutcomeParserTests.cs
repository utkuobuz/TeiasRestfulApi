using TEİASRestfulApi;
using Xunit;

namespace TEİASRestfulApi.Tests;

public class TeiasApiOutcomeParserTests
{
    [Fact]
    public void Http_200_empty_body_is_success()
    {
        TeiasApiOutcome o = TeiasApiOutcomeParser.FromResponse(true, "");
        Assert.True(o.Ok);
    }

    [Fact]
    public void Http_200_gecerli_false_is_failure()
    {
        const string body = """{"gecerli":false,"basarili":false,"mesaj":["üst limit"]}""";
        TeiasApiOutcome o = TeiasApiOutcomeParser.FromResponse(true, body);
        Assert.False(o.Ok);
        Assert.Contains(o.Messages, m => m.Contains("üst limit"));
    }

    [Fact]
    public void Http_200_basarili_true_is_success()
    {
        const string body = """{"gecerli":true,"basarili":true,"mesaj":[]}""";
        TeiasApiOutcome o = TeiasApiOutcomeParser.FromResponse(true, body);
        Assert.True(o.Ok);
    }

    [Fact]
    public void Http_412_is_failure_even_with_true_flags()
    {
        const string body = """{"gecerli":true,"basarili":true}""";
        TeiasApiOutcome o = TeiasApiOutcomeParser.FromResponse(false, body);
        Assert.False(o.Ok);
    }

    [Fact]
    public void Http_200_non_json_is_success()
    {
        TeiasApiOutcome o = TeiasApiOutcomeParser.FromResponse(true, "OK");
        Assert.True(o.Ok);
    }
}
