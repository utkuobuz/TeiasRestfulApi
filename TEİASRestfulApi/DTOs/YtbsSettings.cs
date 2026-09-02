namespace TEİASRestfulApi.DTOs;

public class YtbsSettings
{
    public string? ServiceKey { get; set; }
    public string? KullaniciAdi { get; set; }
    public string? Sifre { get; set; }
    public string? BaseUrl { get; set; }
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// Zenon ActivePower birimi. "MW" (varsayılan) veya "kW".
    /// kW ise gönderimden önce 1000'e bölünür.
    /// </summary>
    public string ActivePowerUnit { get; set; } = "MW";

    /// <summary>
    /// Anlık gönderimde son örneğin en fazla kaç dakika eski olabileceği.
    /// Daha eski örnekler yok sayılır (stale plant gönderilmez).
    /// </summary>
    public int AnlikMaxAgeMinutes { get; set; } = 20;
}
