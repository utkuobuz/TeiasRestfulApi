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
    /// Zenon saatte bir bastığı için varsayılan 75'tir (üst sınır 120).
    /// Daha eski örnekler yok sayılır (stale plant gönderilmez).
    /// </summary>
    public int AnlikMaxAgeMinutes { get; set; } = 75;

    /// <summary>
    /// Saatlik FAIL/WARN özet maili. Alıcı listesi appsettings.Local.json içinde değiştirilir.
    /// </summary>
    public YtbsMailSettings Mail { get; set; } = new();
}

public class YtbsMailSettings
{
    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;

    /// <summary>
    /// Windows bazı ağlarda Gmail CRL/OCSP’ye ulaşamaz (iptal denetimi). Varsayılan kapalı.
    /// </summary>
    public bool CheckCertificateRevocation { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? From { get; set; }
    public string FromName { get; set; } = "YTBS Aktarım";
    public List<string> To { get; set; } = [];

    public IReadOnlyList<string> Recipients =>
        To.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public bool CanSend =>
        Enabled
        && Recipients.Count > 0
        && !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(UserName)
        && !string.IsNullOrWhiteSpace(Password)
        && !string.IsNullOrWhiteSpace(From ?? UserName);
}
