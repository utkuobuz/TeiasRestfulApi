using Microsoft.Extensions.Options;
using System.Linq;
using TEİASRestfulApi.DTOs;

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

            using PeriodicTimer timer = new(TimeSpan.FromMinutes(15));

            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    bool isLogged = await _ytbsClient.LoginAsync(_settings.KullaniciAdi, _settings.Sifre);

                    if (isLogged)
                    {
                        // 1. Veritabanından tüm 443 santralin verisini liste olarak al
                        List<ScadaDbRow> okunanVeriler = GetScadaDataList();

                        if (okunanVeriler != null && okunanVeriler.Any())
                        {
                            // 2. Verileri "Bağlantı Anlaşması Lisans Numarasına" göre GRUPLA
                            var groupedData = okunanVeriler.GroupBy(x => x.BaglantiAnlasmasiSirketiLisansNo);

                            // 3. Her bir lisans grubu için ayrı bir paket oluştur ve gönder
                            foreach (var group in groupedData)
                            {
                                var uretimVerisiPaketi = new AnlikUretimEkleRequest
                                {
                                    baglantiAnlasmasiSirketiLisansNo = group.Key,
                                    veri = group.Select(g => new UretimVeriItem
                                    {
                                        tarih = DateTime.Now.ToString("yyyy-MM-dd"),
                                        saat = DateTime.Now.ToString("HH:mm"),
                                        lisanssizSantralId = g.LisanssizSantralId,
                                        veriDeger = g.AktifGuc
                                    }).ToList()
                                };

                                _logger.LogInformation($"Lisans No: {group.Key} için {group.Count()} adet santral verisi gönderiliyor...");

                                await _ytbsClient.SendUretimVerisiAsync(uretimVerisiPaketi);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Döngü sırasında hata oluştu.");
                }
            }
        }

        // SCADA SQL veritabanından 443 santralin tamamını okuyacak metod
        private List<ScadaDbRow> GetScadaDataList()
        {
            // Şimdilik test edebilmemiz için "Mock" (sahte) bir liste dönüyoruz.
            // SCADA ekibinden IP ve Tablo bilgisi geldiğinde burayı Dapper SQL kodlarıyla değiştireceğiz.
            return new List<ScadaDbRow>
            {
                new ScadaDbRow { LisanssizSantralId = 37402, BaglantiAnlasmasiSirketiLisansNo = "ED-OSB/1813-6/1277", AktifGuc = 1.45 },
                new ScadaDbRow { LisanssizSantralId = 10983, BaglantiAnlasmasiSirketiLisansNo = "ED-OSB/1813-6/1277", AktifGuc = 0.88 },
                new ScadaDbRow { LisanssizSantralId = 45134, BaglantiAnlasmasiSirketiLisansNo = "ED-OSB/1398-4/1018", AktifGuc = 0.77 }
            };
        }
    }
}