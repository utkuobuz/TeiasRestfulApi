using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using TEİASRestfulApi;
using Xunit;

namespace TEİASRestfulApi.Tests;

public class YtbsMailSenderTests
{
    [Fact]
    public void AcceptWhenRevocationOffline_allows_clean_chain()
    {
        Assert.True(YtbsMailSender.AcceptWhenRevocationOffline(this, null, null, SslPolicyErrors.None));
    }

    [Fact]
    public void AcceptWhenRevocationOffline_rejects_name_mismatch()
    {
        Assert.False(
            YtbsMailSender.AcceptWhenRevocationOffline(
                this,
                null,
                null,
                SslPolicyErrors.RemoteCertificateNameMismatch));
    }
}
