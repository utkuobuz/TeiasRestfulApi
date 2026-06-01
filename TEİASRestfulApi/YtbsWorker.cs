using Microsoft.Extensions.Options;
using System.Linq;
using TEİASRestfulApi.DTOs;
using MySqlConnector;
using Dapper;

namespace TEİASRestfulApi
{
    public class YtbsWorker : BackgroundService
    {
        private readonly ILogger<YtbsWorker> _logger;
        private readonly YTBSClient _ytbsClient;
        private readonly YtbsSettings _settings;

        public YtbsWorker(ILogger<YtbsWorker> logger, YTBSClient ytbsClient, IOptions<YtbsSettings> settings)
        {
            _logger = logger;
            _ytbsClient = ytbsClient;
            _settings = settings.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("YTBS Çoklu Veri Aktarım Servisi başlatıldı.");
            _ytbsClient.SetServiceKey(_settings.ServiceKey);

            // Canlı ortam için periyot 15 dakika olarak ayarlandı
            using PeriodicTimer timer = new(TimeSpan.FromMinutes(15));

            // do-while sayesinde servis açılır açılmaz ilk veriyi bekletmeden atar, 
            // ardından 15 dakikada bir çalışmaya devam eder.
            do
            {
                try
                {
                    bool isLogged = await _ytbsClient.LoginAsync(_settings.KullaniciAdi, _settings.Sifre);

                    if (isLogged)
                    {
                        List<ScadaDbRow> okunanVeriler = GetScadaDataList();

                        if (okunanVeriler != null && okunanVeriler.Any())
                        {
                            var groupedData = okunanVeriler.GroupBy(x => x.BaglantiAnlasmasiSirketiLisansNo);

                            foreach (var group in groupedData)
                            {
                                var uretimVerisiPaketi = new AnlikUretimEkleRequest
                                {
                                    baglantiAnlasmasiSirketiLisansNo = group.Key,
                                    veri = group.Select(g => new UretimVeriItem
                                    {
                                        tarih = g.Tarih,
                                        saat = g.Saat,
                                        lisanssizSantralId = g.LisanssizSantralId,
                                        veriDeger = g.AktifGuc
                                    }).ToList()
                                };

                                _logger.LogInformation($"Lisans No: {group.Key} için {group.Count()} adet santral verisi gönderiliyor...");
                                await _ytbsClient.SendUretimVerisiAsync(uretimVerisiPaketi);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Veritabanından gönderilecek güncel SCADA verisi bulunamadı.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Döngü sırasında hata oluştu.");
                }

            } while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
        }

        private List<ScadaDbRow> GetScadaDataList()
        {
            using var connection = new MySqlConnection(_settings.ConnectionString);

            // Veritabanı sorgusu (Son 45 günü tarayacak şekilde ayarlı, canlıda da böyle kalabilir)
            string sql = @"
                SELECT
                    m.TEIAS_PLANT_ID AS LisanssizSantralId,
                    m.LICENSE_NO AS BaglantiAnlasmasiSirketiLisansNo,
                    CAST(REPLACE(d.VALUE, ',', '.') AS DECIMAL(10,4)) AS AktifGuc,
                    DATE_FORMAT(FROM_UNIXTIME(d.TIMESTAMP_S), '%Y-%m-%d') AS Tarih,
                    DATE_FORMAT(FROM_UNIXTIME(d.TIMESTAMP_S), '%H:%i') AS Saat
                FROM scada.TEIAS_Mapping m
                INNER JOIN scada.Zenon_Export_DATA d ON d.VAR = m.VAR_NAME
                INNER JOIN (
                    SELECT VAR, MAX(TIMESTAMP_S) AS MaxTime
                    FROM scada.Zenon_Export_DATA
                    WHERE TIMESTAMP_S >= UNIX_TIMESTAMP(NOW() - INTERVAL 45 DAY)
                    GROUP BY VAR
                ) latest ON d.VAR = latest.VAR AND d.TIMESTAMP_S = latest.MaxTime;";

            try
            {
                return connection.Query<ScadaDbRow>(sql).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MySQL'den SCADA verisi çekilirken bir hata oluştu.");
                return new List<ScadaDbRow>();
            }
        }
    }
}