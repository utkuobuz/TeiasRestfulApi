using Microsoft.Extensions.Options;
using TEİASRestfulApi.DTOs;
using MySqlConnector;
using Dapper;

namespace TEİASRestfulApi
{
    public class YtbsWorker : BackgroundService
    {
        private static readonly TimeSpan StartupQuarterGrace = TimeSpan.FromSeconds(30);

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

            if (string.IsNullOrWhiteSpace(_settings.ConnectionString)
                || string.IsNullOrWhiteSpace(_settings.KullaniciAdi)
                || string.IsNullOrWhiteSpace(_settings.Sifre)
                || string.IsNullOrWhiteSpace(_settings.ServiceKey))
            {
                _logger.LogError("YtbsSettings eksik. appsettings.Local.json veya ortam değişkenlerini doldurun (ConnectionString, KullaniciAdi, Sifre, ServiceKey).");
                return;
            }

            bool isKilowatt = ScadaValueNormalizer.IsKilowatt(_settings.ActivePowerUnit);
            _logger.LogInformation(
                "ActivePower birimi: {Unit}. Anlık örnek azami yaşı: {MaxAge} dk.",
                isKilowatt ? "kW→MW" : "MW",
                Math.Clamp(_settings.AnlikMaxAgeMinutes, 5, 120));

            try
            {
                TimeSpan initialDelay = YtbsTimeSlots.DelayUntilNextAlignedRun(DateTime.Now, StartupQuarterGrace);
                if (initialDelay > TimeSpan.Zero)
                {
                    _logger.LogInformation("Sonraki 15 dk dilimine hizalanıyor. Bekleme: {Delay}.", initialDelay);
                    await Task.Delay(initialDelay, stoppingToken);
                }

                while (!stoppingToken.IsCancellationRequested)
                {
                    await RunCycleAsync(isKilowatt);

                    TimeSpan wait = YtbsTimeSlots.DelayUntilNextQuarter(DateTime.Now);
                    if (wait > TimeSpan.Zero)
                    {
                        await Task.Delay(wait, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Servis kapanışı
            }
        }

        private async Task RunCycleAsync(bool isKilowatt)
        {
            DateTime now = DateTime.Now;
            DateTime quarterSlot = YtbsTimeSlots.GetCurrentQuarterStart(now);

            try
            {
                bool isLogged = await _ytbsClient.LoginAsync(_settings.KullaniciAdi!, _settings.Sifre!);
                if (!isLogged)
                {
                    return;
                }

                await SendAnlikAsync(quarterSlot, isKilowatt);

                if (YtbsTimeSlots.IsHourlySlot(now))
                {
                    await SendSaatlikAsync(YtbsTimeSlots.GetPreviousHourStart(now), isKilowatt);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Döngü sırasında hata oluştu.");
            }
        }

        private async Task SendAnlikAsync(DateTime quarterSlot, bool isKilowatt)
        {
            List<ScadaDbRow> okunanAnlikVeriler = GetScadaDataList();
            if (okunanAnlikVeriler == null || !okunanAnlikVeriler.Any())
            {
                _logger.LogWarning(
                    "Anlık SCADA verisi yok veya son {MaxAge} dk içinde örnek gelmedi. Dilim {Tarih} {Saat} atlandı.",
                    Math.Clamp(_settings.AnlikMaxAgeMinutes, 5, 120),
                    YtbsTimeSlots.FormatTarih(quarterSlot),
                    YtbsTimeSlots.FormatSaat(quarterSlot));
                return;
            }

            foreach (var group in okunanAnlikVeriler.GroupBy(x => x.BaglantiAnlasmasiSirketiLisansNo))
            {
                var uretimVerisiPaketi = new AnlikUretimEkleRequest
                {
                    baglantiAnlasmasiSirketiLisansNo = group.Key,
                    veri = ScadaValueNormalizer.BuildItems(
                        group,
                        x => x.LisanssizSantralId,
                        x => x.AktifGuc,
                        x => x.MaxCapacity,
                        quarterSlot,
                        isKilowatt)
                };

                LogPaketOzeti("ANLIK MW", uretimVerisiPaketi, quarterSlot);
                await _ytbsClient.SendUretimVerisiAsync(uretimVerisiPaketi);
            }
        }

        private async Task SendSaatlikAsync(DateTime hourSlot, bool isKilowatt)
        {
            List<SaatlikUretimVeri> okunanSaatlikVeriler = GetSaatlikScadaDataList();
            if (okunanSaatlikVeriler == null || !okunanSaatlikVeriler.Any())
            {
                _logger.LogWarning(
                    "Saatlik SCADA verisi bulunamadı. Dilim {Tarih} {Saat}.",
                    YtbsTimeSlots.FormatTarih(hourSlot),
                    YtbsTimeSlots.FormatSaat(hourSlot));
                return;
            }

            foreach (var group in okunanSaatlikVeriler.GroupBy(x => x.BaglantiAnlasmasiSirketiLisansNo))
            {
                var saatlikUretimPaketi = new AnlikUretimEkleRequest
                {
                    baglantiAnlasmasiSirketiLisansNo = group.Key,
                    veri = ScadaValueNormalizer.BuildItems(
                        group,
                        x => x.LisanssizSantralId,
                        x => x.ToplamEnerjiMWh,
                        x => x.MaxCapacity,
                        hourSlot,
                        isKilowatt)
                };

                LogPaketOzeti("SAATLİK MWh", saatlikUretimPaketi, hourSlot);
                await _ytbsClient.SendSaatlikUretimVerisiAsync(saatlikUretimPaketi);
            }
        }

        private void LogPaketOzeti(string kanal, AnlikUretimEkleRequest paket, DateTime slot)
        {
            var degerler = paket.veri?
                .Select(v => v.veriDeger)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList() ?? [];

            _logger.LogInformation(
                "[{Kanal}] Lisans {Lisans} adet={Adet} min={Min} max={Max} dilim={Tarih} {Saat}",
                kanal,
                paket.baglantiAnlasmasiSirketiLisansNo,
                degerler.Count,
                degerler.Count > 0 ? degerler.Min() : (double?)null,
                degerler.Count > 0 ? degerler.Max() : (double?)null,
                YtbsTimeSlots.FormatTarih(slot),
                YtbsTimeSlots.FormatSaat(slot));
        }

        private List<ScadaDbRow> GetScadaDataList()
        {
            using var connection = new MySqlConnection(_settings.ConnectionString);
            int maxAgeMinutes = Math.Clamp(_settings.AnlikMaxAgeMinutes, 5, 120);
            string sql = $@"
                SELECT
                    m.TEIAS_PLANT_ID AS LisanssizSantralId,
                    m.LICENSE_NO AS BaglantiAnlasmasiSirketiLisansNo,
                    m.MAX_CAPACITY AS MaxCapacity,
                    CAST(SUM(CAST(REPLACE(d.VALUE, ',', '.') AS DECIMAL(10,4))) AS DECIMAL(10,4)) AS AktifGuc
                FROM scada.TEIAS_Mapping m
                INNER JOIN scada.Zenon_Export_DATA d ON d.VAR = m.VAR_NAME
                INNER JOIN (
                    SELECT VAR, MAX(TIMESTAMP_S) AS MaxTime
                    FROM scada.Zenon_Export_DATA
                    WHERE TIMESTAMP_S >= UNIX_TIMESTAMP(NOW() - INTERVAL {maxAgeMinutes} MINUTE)
                    GROUP BY VAR
                ) latest ON d.VAR = latest.VAR AND d.TIMESTAMP_S = latest.MaxTime
                GROUP BY m.TEIAS_PLANT_ID, m.LICENSE_NO, m.MAX_CAPACITY;";

            try
            {
                return connection.Query<ScadaDbRow>(sql).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MySQL'den anlık SCADA verisi çekilirken bir hata oluştu.");
                return new List<ScadaDbRow>();
            }
        }

        private List<SaatlikUretimVeri> GetSaatlikScadaDataList()
        {
            using var connection = new MySqlConnection(_settings.ConnectionString);
            string sql = @"
                SELECT
                    TEIAS_PLANT_ID AS LisanssizSantralId,
                    LICENSE_NO AS BaglantiAnlasmasiSirketiLisansNo,
                    MAX_CAPACITY AS MaxCapacity,
                    CAST(SUM(VarOrtMw) AS DECIMAL(10,4)) AS ToplamEnerjiMWh
                FROM (
                    SELECT
                        m.TEIAS_PLANT_ID,
                        m.LICENSE_NO,
                        m.MAX_CAPACITY,
                        AVG(CAST(REPLACE(d.VALUE, ',', '.') AS DECIMAL(10,4))) AS VarOrtMw
                    FROM scada.TEIAS_Mapping m
                    INNER JOIN scada.Zenon_Export_DATA d ON d.VAR = m.VAR_NAME
                    WHERE d.TIMESTAMP_S >= UNIX_TIMESTAMP(DATE_FORMAT(NOW() - INTERVAL 1 HOUR, '%Y-%m-%d %H:00:00'))
                      AND d.TIMESTAMP_S <  UNIX_TIMESTAMP(DATE_FORMAT(NOW(), '%Y-%m-%d %H:00:00'))
                    GROUP BY m.TEIAS_PLANT_ID, m.LICENSE_NO, m.MAX_CAPACITY, m.VAR_NAME
                ) varOrt
                GROUP BY TEIAS_PLANT_ID, LICENSE_NO, MAX_CAPACITY;";

            try
            {
                return connection.Query<SaatlikUretimVeri>(sql).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MySQL'den Saatlik SCADA verisi çekilirken bir hata oluştu.");
                return new List<SaatlikUretimVeri>();
            }
        }
    }
}
