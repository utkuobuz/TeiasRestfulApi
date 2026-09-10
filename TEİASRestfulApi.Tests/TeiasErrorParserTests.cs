using TEİASRestfulApi;
using Xunit;

namespace TEİASRestfulApi.Tests;

public class TeiasErrorParserTests
{
    [Fact]
    public void Parse_invalid_id_and_over_limit_messages()
    {
        const string body = """
            {"gecerli":false,"basarili":false,"mesaj":["Lisanssız santral ID bilgisi (11027) geçersizdir.","Lisanssız santral ID: 26666 üretimi için girilen değer (536,192 MWh) üst limitin (1,112 MWh) üstündedir."]}
            """;

        TeiasRejectDetail detail = TeiasErrorParser.Parse(body);

        Assert.Equal(new[] { 11027, 26666 }, detail.CulpritIds);
        Assert.Equal(2, detail.Messages.Count);
        Assert.Contains("11027", detail.Messages[0]);
        Assert.Equal(1.112m, detail.Limits[26666]);
        Assert.False(detail.Limits.ContainsKey(11027));
    }

    [Fact]
    public void Parse_reads_turkish_limit_from_hourly_mail_example()
    {
        const string body = """
            {"gecerli":false,"basarili":false,"mesaj":["Lisanssız santral ID: 2343 üretimi için girilen değer (1,216 MWh) üst limitin (1,045 MWh) üstündedir.","Lisanssız santral ID: 4018 üretimi için girilen değer (1,304 MWh) üst limitin (1,100 MWh) üstündedir."]}
            """;

        TeiasRejectDetail detail = TeiasErrorParser.Parse(body);

        Assert.Equal(new[] { 2343, 4018 }, detail.CulpritIds);
        Assert.Equal(1.045m, detail.Limits[2343]);
        Assert.Equal(1.100m, detail.Limits[4018]);
    }

    [Fact]
    public void Parse_empty_body_returns_nothing()
    {
        TeiasRejectDetail detail = TeiasErrorParser.Parse(null);
        Assert.Empty(detail.CulpritIds);
        Assert.Empty(detail.Messages);
    }
}
