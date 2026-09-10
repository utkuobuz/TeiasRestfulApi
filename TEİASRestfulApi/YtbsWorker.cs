using Microsoft.Extensions.Options;
using TEİASRestfulApi.DTOs;
using MySqlConnector;
using Dapper;

namespace TEİASRestfulApi
{
    public class YtbsWorker : BackgroundService
    {
        private readonly ILogger<YtbsWorker> _logger;
        private readonly YTBSClient _ytbsClient;
        private readonly IOptionsMonitor<YtbsSettings> _settingsMonitor;
        private readonly YtbsMailSender _mailSender;
        private readonly YtbsValueScaleStore _scaleStore;
        private readonly YtbsReportedLimitStore _reportedLimitStore;
        private readonly YtbsSlotDeliveryStore _deliveryStore;
        private readonly YtbsIssueBuffer _issues = new();

        public YtbsWorker(
            ILogger<YtbsWorker> logger,
            YTBSClient ytbsClient,
            IOptionsMonitor<YtbsSettings> settings,
            YtbsMailSender mailSender,
            YtbsValueScaleStore scaleStore,
            YtbsReportedLimitStore reportedLimitStore,
            YtbsSlotDeliveryStore deliveryStore)
        {
            _logger = logger;
            _ytbsClient = ytbsClient;
            _settingsMonitor = settings;
            _mailSender = mailSender;
            _scaleStore = scaleStore;
            _reportedLimitStore = reportedLimitStore;
            _deliveryStore = deliveryStore;
        }

        private YtbsSettings Settings => _settingsMonitor.CurrentValue;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("YTBS Çoklu Veri Aktarım Servisi başlatıldı.");
            _ytbsClient.SetServiceKey(Settings.ServiceKey);

            if (string.IsNullOrWhiteSpace(Settings.ConnectionString)
                || string.IsNullOrWhiteSpace(Settings.KullaniciAdi)
                || string.IsNullOrWhiteSpace(Settings.Sifre)
                || string.IsNullOrWhiteSpace(Settings.ServiceKey))
            {
                _logger.LogError("YtbsSettings eksik. appsettings.Local.json veya ortam değişkenlerini doldurun (ConnectionString, KullaniciAdi, Sifre, ServiceKey).");
                return;
            }

            bool isKilowatt = ScadaValueNormalizer.IsKilowatt(Settings.ActivePowerUnit);
            _logger.LogInformation(
                "Anlık MW: *.ActivePower. Saatlik MWh: *.ActiveEnergy.Exported.Hourly (15 dk kopyaları toplanmaz). Güç birimi: {Unit}. Anlık örnek azami yaşı: {MaxAge} dk.",
                isKilowatt ? "kW→MW" : "MW",
                Math.Clamp(Settings.AnlikMaxAgeMinutes, 5, 120));

            try
            {
                _logger.LogInformation("Açılış turu hemen gönderiliyor; sonraki turlar çeyrek saate kilitlenir.");
                await RunCycleAsync(isKilowatt);

                while (!stoppingToken.IsCancellationRequested)
                {
                    TimeSpan wait = YtbsTimeSlots.DelayUntilNextQuarter(DateTime.Now);
                    if (wait > TimeSpan.Zero)
                    {
                        _logger.LogInformation("Sonraki 15 dk dilimine hizalanıyor. Bekleme: {Delay}.", wait);
                        await Task.Delay(wait, stoppingToken);
                    }

                    await RunCycleAsync(isKilowatt);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Servis kapanışı
            }
            finally
            {
                await _ytbsClient.LogoutAsync();
            }
        }

        private async Task RunCycleAsync(bool isKilowatt)
        {
            DateTime now = DateTime.Now;
            YtbsApiSlot anlikSlot = YtbsTimeSlots.ToAnlikApiSlot(now);

            try
            {
                PlantNameDirectory names = LoadPlantNames();
                bool isLogged = await _ytbsClient.EnsureLoggedInAsync(Settings.KullaniciAdi!, Settings.Sifre!);
                if (!isLogged)
                {
                    _issues.Add(new YtbsIssue
                    {
                        Level = YtbsIssueLevel.Fail,
                        Channel = YtbsChannel.Sistem,
                        Kind = YtbsIssueKind.LoginFailed,
                        Slot = anlikSlot.Instant,
                        Reason = "TEİAŞ’a giriş yapılamadı; o turda hiç paket gitmedi."
                    });
                    if (YtbsTimeSlots.IsHourlySlot(now))
                    {
                        await FlushHourlyMailAsync(now);
                    }

                    return;
                }

                Dictionary<(int PlantId, YtbsChannel Channel), decimal> scales = LoadValueScales();
                Dictionary<int, decimal> reported = LoadReportedLimits();
                int anlikGonderilen = await SendAnlikAsync(anlikSlot, isKilowatt, names, scales, reported, reportIssues: true);
                int? saatlikGonderilen = null;

                if (YtbsTimeSlots.IsHourlySlot(now))
                {
                    saatlikGonderilen = await SendSaatlikAsync(YtbsTimeSlots.ToSaatlikApiSlot(now), isKilowatt, names, scales, reported, reportIssues: true);
                    await RetryBacklogAsync(now, isKilowatt, names, scales, reported);
                    LogKapsamOzeti(anlikGonderilen, saatlikGonderilen);
                    await FlushHourlyMailAsync(now);
                    return;
                }

                await RetryBacklogAsync(now, isKilowatt, names, scales, reported);
                LogKapsamOzeti(anlikGonderilen, saatlikGonderilen);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Döngü sırasında hata oluştu.");
            }
        }

        private async Task<int> SendAnlikAsync(
            YtbsApiSlot slot,
            bool isKilowatt,
            PlantNameDirectory names,
            Dictionary<(int PlantId, YtbsChannel Channel), decimal> scales,
            Dictionary<int, decimal> reported,
            bool reportIssues,
            IReadOnlySet<int>? onlyPlants = null)
        {
            List<ScadaDbRow> okunanAnlikVeriler = GetScadaDataList(slot.Instant);
            if (onlyPlants is { Count: > 0 })
            {
                okunanAnlikVeriler = okunanAnlikVeriler
                    .Where(x => onlyPlants.Contains(x.LisanssizSantralId))
                    .ToList();
            }

            if (okunanAnlikVeriler == null || !okunanAnlikVeriler.Any())
            {
                _logger.LogWarning(
                    "Anlık SCADA verisi yok veya son {MaxAge} dk içinde örnek gelmedi. Dilim {Tarih} {Saat} atlandı.",
                    Math.Clamp(Settings.AnlikMaxAgeMinutes, 5, 120),
                    slot.Tarih,
                    slot.Saat);
                if (reportIssues)
                {
                    RecordAllMappedMissing(YtbsChannel.Anlik, slot.Instant, names);
                }

                return 0;
            }

            if (reportIssues)
            {
                RecordMissingPlants(YtbsChannel.Anlik, slot.Instant, okunanAnlikVeriler.Select(x => x.LisanssizSantralId), names);
            }

            int gonderilen = 0;
            foreach (var group in okunanAnlikVeriler.GroupBy(x => x.BaglantiAnlasmasiSirketiLisansNo))
            {
                var uretimVerisiPaketi = new AnlikUretimEkleRequest
                {
                    baglantiAnlasmasiSirketiLisansNo = group.Key,
                    veri = BuildSubmittableItems(
                        YtbsChannel.Anlik,
                        group,
                        x => x.LisanssizSantralId,
                        x => x.AktifGuc,
                        x => x.MaxCapacity,
                        slot,
                        isKilowatt,
                        names,
                        scales,
                        reported,
                        reportIssues,
                        out List<int> belowMin,
                        out List<OverLimitSkip> overLimit)
                };
                MarkSkipped(YtbsChannel.Anlik, slot.Instant, belowMin);

                if (uretimVerisiPaketi.veri is not { Count: > 0 })
                {
                    continue;
                }

                gonderilen += await SendOrRecoverAsync(
                    YtbsChannel.Anlik,
                    slot,
                    uretimVerisiPaketi,
                    _ytbsClient.SendUretimVerisiAsync,
                    names,
                    reported,
                    reportIssues);
            }

            return gonderilen;
        }

        private async Task<int> SendSaatlikAsync(
            YtbsApiSlot slot,
            bool isKilowatt,
            PlantNameDirectory names,
            Dictionary<(int PlantId, YtbsChannel Channel), decimal> scales,
            Dictionary<int, decimal> reported,
            bool reportIssues,
            IReadOnlySet<int>? onlyPlants = null)
        {
            List<SaatlikUretimVeri> okunanSaatlikVeriler = GetSaatlikScadaDataList(slot.Instant);
            if (onlyPlants is { Count: > 0 })
            {
                okunanSaatlikVeriler = okunanSaatlikVeriler
                    .Where(x => onlyPlants.Contains(x.LisanssizSantralId))
                    .ToList();
            }

            if (okunanSaatlikVeriler == null || !okunanSaatlikVeriler.Any())
            {
                _logger.LogWarning(
                    "Saatlik SCADA verisi bulunamadı. Dilim {Tarih} {Saat}.",
                    slot.Tarih,
                    slot.Saat);
                if (reportIssues)
                {
                    RecordAllMappedMissing(YtbsChannel.Saatlik, slot.Instant, names);
                }

                return 0;
            }

            if (reportIssues)
            {
                RecordMissingPlants(YtbsChannel.Saatlik, slot.Instant, okunanSaatlikVeriler.Select(x => x.LisanssizSantralId), names);
            }

            int gonderilen = 0;
            foreach (var group in okunanSaatlikVeriler.GroupBy(x => x.BaglantiAnlasmasiSirketiLisansNo))
            {
                var saatlikUretimPaketi = new AnlikUretimEkleRequest
                {
                    baglantiAnlasmasiSirketiLisansNo = group.Key,
                    veri = BuildSubmittableItems(
                        YtbsChannel.Saatlik,
                        group,
                        x => x.LisanssizSantralId,
                        x => x.ToplamEnerjiMWh,
                        x => x.MaxCapacity,
                        slot,
                        isKilowatt: false,
                        names,
                        scales,
                        reported,
                        reportIssues,
                        out List<int> belowMin,
                        out List<OverLimitSkip> overLimit)
                };
                MarkSkipped(YtbsChannel.Saatlik, slot.Instant, belowMin);

                if (saatlikUretimPaketi.veri is not { Count: > 0 })
                {
                    continue;
                }

                gonderilen += await SendOrRecoverAsync(
                    YtbsChannel.Saatlik,
                    slot,
                    saatlikUretimPaketi,
                    _ytbsClient.SendSaatlikUretimVerisiAsync,
                    names,
                    reported,
                    reportIssues);
            }

            return gonderilen;
        }

        private List<UretimVeriItem> BuildSubmittableItems<T>(
            YtbsChannel kanal,
            IEnumerable<T> rows,
            Func<T, int> plantId,
            Func<T, decimal> rawValue,
            Func<T, decimal?> maxCapacity,
            YtbsApiSlot slot,
            bool isKilowatt,
            PlantNameDirectory names,
            Dictionary<(int PlantId, YtbsChannel Channel), decimal> scales,
            Dictionary<int, decimal> reported,
            bool recordIssues,
            out List<int> belowMin,
            out List<OverLimitSkip> skipped)
        {
            var rowList = rows.ToList();
            var items = ScadaValueNormalizer.BuildItems(rowList, plantId, rawValue, slot, isKilowatt);
            var caps = ScadaValueNormalizer.CapacityByPlant(rowList, plantId, maxCapacity);
            YtbsEffectiveLimit.ApplyReported(caps, reported);
            items = ScadaValueNormalizer.TakeAboveMinCapacity(items, caps, out belowMin);
            string kanalAdi = kanal == YtbsChannel.Anlik ? "ANLIK" : "SAATLİK";
            foreach (int plant in belowMin)
            {
                caps.TryGetValue(plant, out decimal? cap);
                _logger.LogInformation(
                    "[{Kanal}] 250 kW altı, gönderilmedi (mail yok). Id={Id} limit={Limit}",
                    kanalAdi,
                    plant,
                    cap);
            }

            foreach (var item in items)
            {
                caps.TryGetValue(item.lisanssizSantralId, out decimal? cap);
                scales.TryGetValue((item.lisanssizSantralId, kanal), out decimal stored);
                decimal? storedOrNull = scales.ContainsKey((item.lisanssizSantralId, kanal)) ? stored : null;
                double raw = item.veriDeger ?? 0;
                YtbsScaleDecision decision = YtbsValueScaler.Apply(raw, cap, storedOrNull);
                item.veriDeger = decision.Value;
                if (decision.Persist && decision.AppliedDivisor is { } divisor)
                {
                    try
                    {
                        _scaleStore.Upsert(Settings.ConnectionString!, item.lisanssizSantralId, kanal, divisor);
                        scales[(item.lisanssizSantralId, kanal)] = divisor;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "[{Kanal}] Bölen yazılamadı. Id={Id} bölen={Bolen}",
                            kanalAdi,
                            item.lisanssizSantralId,
                            divisor);
                    }

                    _logger.LogInformation(
                        "[{Kanal}] SCADA ölçeği düzeltildi. Id={Id} ham={Ham} bölen={Bolen} olcum={Olcum} limit={Limit}",
                        kanalAdi,
                        item.lisanssizSantralId,
                        raw,
                        divisor,
                        decision.Value,
                        cap);
                }
            }

            var kept = ScadaValueNormalizer.TakeWithinTeiasLimit(items, caps, out skipped);
            foreach (var skip in skipped)
            {
                _logger.LogWarning(
                    "[{Kanal}] Limit aşıldı (fiziksel müdahale), santral gönderilmedi. Id={Id} olcum={Olcum} limit={Limit}",
                    kanalAdi,
                    skip.PlantId,
                    skip.Value,
                    skip.Limit);
                if (!recordIssues)
                {
                    continue;
                }

                _issues.Add(new YtbsIssue
                {
                    Level = YtbsIssueLevel.Fail,
                    Channel = kanal,
                    Kind = YtbsIssueKind.OverLimitSkip,
                    Slot = slot.Instant,
                    PlantId = skip.PlantId,
                    PlantName = names.NameOf(skip.PlantId),
                    Value = skip.Value,
                    Limit = skip.Limit,
                    Reason = "Ölçüm ölçek sonrası hâlâ TEİAŞ limitinin üzerinde; fiziksel müdahale."
                });
            }

            return kept;
        }

        private async Task RetryBacklogAsync(
            DateTime now,
            bool isKilowatt,
            PlantNameDirectory names,
            Dictionary<(int PlantId, YtbsChannel Channel), decimal> scales,
            Dictionary<int, decimal> reported)
        {
            List<(int PlantId, string? LicenseNo)> live;
            HashSet<(YtbsChannel Channel, DateTime Instant, int PlantId)> resolved;
            Dictionary<(YtbsChannel Channel, DateTime Instant), DateTime> attempts;
            try
            {
                live = GetLiveMappedPlants();
                DateTime from = YtbsRetryPlanner.LookbackStart(now);
                resolved = _deliveryStore.LoadResolved(Settings.ConnectionString!, from, now);
                attempts = _deliveryStore.LoadAttempts(Settings.ConnectionString!, from, now);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "4 günlük tekrar listesi okunamadı.");
                return;
            }

            YtbsApiSlot anlikNow = YtbsTimeSlots.ToAnlikApiSlot(now);
            YtbsApiSlot saatlikNow = YtbsTimeSlots.ToSaatlikApiSlot(now);
            List<YtbsRetrySlot> unresolved = YtbsRetryPlanner.UnresolvedSlots(
                YtbsRetryPlanner.LookbackStart(now),
                anlikNow.Instant,
                saatlikNow.Instant,
                live.Select(p => p.PlantId).Distinct().ToList(),
                resolved);
            List<YtbsRetrySlot> due = YtbsRetryPlanner.PickDue(unresolved, attempts, now);
            if (due.Count == 0)
            {
                return;
            }

            _logger.LogInformation(
                "4 günlük tekrar: bekleyen={Bekleyen} bu tur={Tur}",
                unresolved.Count,
                due.Count);

            foreach (YtbsRetrySlot retry in due)
            {
                YtbsApiSlot slot = YtbsTimeSlots.ToIntervalEndSlot(retry.Instant);
                HashSet<int> need = live
                    .Select(p => p.PlantId)
                    .Where(id => !resolved.Contains((retry.Channel, retry.Instant, id)))
                    .ToHashSet();
                try
                {
                    if (retry.Channel == YtbsChannel.Anlik)
                    {
                        await SendAnlikAsync(slot, isKilowatt, names, scales, reported, reportIssues: false, need);
                    }
                    else
                    {
                        await SendSaatlikAsync(slot, isKilowatt, names, scales, reported, reportIssues: false, need);
                    }

                    _deliveryStore.TouchAttempt(Settings.ConnectionString!, retry.Channel, retry.Instant, now);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Tekrar gönderim hata. Kanal={Kanal} dilim={Tarih} {Saat}",
                        retry.Channel,
                        slot.Tarih,
                        slot.Saat);
                }
            }
        }

        private void MarkOk(YtbsChannel channel, DateTime instant, IEnumerable<UretimVeriItem>? items)
        {
            MarkDelivery(channel, instant, (items ?? []).Select(i => i.lisanssizSantralId), "Ok");
        }

        private void MarkSkipped(YtbsChannel channel, DateTime instant, IEnumerable<int> belowMin)
        {
            MarkDelivery(channel, instant, belowMin, "Skipped");
        }

        private void MarkDelivery(YtbsChannel channel, DateTime instant, IEnumerable<int> plantIds, string status)
        {
            try
            {
                _deliveryStore.Mark(Settings.ConnectionString!, channel, instant, plantIds, status);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TEIAS_SlotPlant yazılamadı. Durum={Status}", status);
            }
        }

        private async Task<int> SendOrRecoverAsync(
            YtbsChannel channel,
            YtbsApiSlot slot,
            AnlikUretimEkleRequest paket,
            Func<AnlikUretimEkleRequest, Task<YtbsSendResult>> send,
            PlantNameDirectory names,
            Dictionary<int, decimal> reported,
            bool reportIssues)
        {
            string kanalAdi = channel == YtbsChannel.Anlik ? "ANLIK MW" : "SAATLİK MWh";
            AnlikUretimEkleRequest remaining = paket;
            int gonderilen = 0;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (remaining.veri is not { Count: > 0 })
                {
                    return gonderilen;
                }

                LogPaketOzeti(kanalAdi, remaining, slot);
                YtbsSendResult result = await send(remaining);
                int adet = remaining.veri?.Count ?? 0;
                if (result.Ok)
                {
                    gonderilen += adet;
                    MarkOk(channel, slot.Instant, remaining.veri);
                    return gonderilen;
                }

                _logger.LogWarning(
                    "[{Kanal}] Lisans paketi reddedildi. Lisans={Lisans} adet={Adet} dilim={Tarih} {Saat}",
                    channel == YtbsChannel.Anlik ? "ANLIK" : "SAATLİK",
                    remaining.baglantiAnlasmasiSirketiLisansNo,
                    adet,
                    slot.Tarih,
                    slot.Saat);

                TeiasRejectDetail detail = TeiasErrorParser.Parse(result.ErrorBody);
                RememberLimits(detail, reported);
                HashSet<int> culprits = detail.CulpritIds.ToHashSet();
                if (culprits.Count == 0)
                {
                    if (reportIssues)
                    {
                        RecordPackageReject(channel, slot.Instant, remaining, result.ErrorBody, names);
                    }

                    return gonderilen;
                }

                RecordRejectedCulprits(channel, slot, remaining, detail, names, reportIssues);
                remaining = new AnlikUretimEkleRequest
                {
                    baglantiAnlasmasiSirketiLisansNo = remaining.baglantiAnlasmasiSirketiLisansNo,
                    veri = (remaining.veri ?? [])
                        .Where(v => !culprits.Contains(v.lisanssizSantralId))
                        .ToList()
                };

                if (remaining.veri is { Count: > 0 })
                {
                    _logger.LogInformation(
                        "[{Kanal}] Suçlular çıkarıldı, paket tekrar gönderiliyor. Lisans={Lisans} kalan={Kalan}",
                        channel == YtbsChannel.Anlik ? "ANLIK" : "SAATLİK",
                        remaining.baglantiAnlasmasiSirketiLisansNo,
                        remaining.veri.Count);
                }
            }

            if (reportIssues && remaining.veri is { Count: > 0 })
            {
                RecordPackageReject(channel, slot.Instant, remaining, "Paket üç denemede de reddedildi.", names);
            }

            return gonderilen;
        }

        private void RememberLimits(TeiasRejectDetail detail, Dictionary<int, decimal> reported)
        {
            foreach (var (plantId, limit) in detail.Limits)
            {
                reported[plantId] = limit;
                try
                {
                    _reportedLimitStore.Upsert(Settings.ConnectionString!, plantId, limit);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "TEİAŞ limiti yazılamadı. Id={Id} limit={Limit}", plantId, limit);
                }
            }
        }

        private void RecordRejectedCulprits(
            YtbsChannel channel,
            YtbsApiSlot slot,
            AnlikUretimEkleRequest paket,
            TeiasRejectDetail detail,
            PlantNameDirectory names,
            bool reportIssues)
        {
            if (!reportIssues)
            {
                return;
            }

            var invalid = new List<UretimVeriItem>();
            foreach (int id in detail.CulpritIds)
            {
                UretimVeriItem? item = paket.veri?.FirstOrDefault(v => v.lisanssizSantralId == id);
                if (detail.Limits.TryGetValue(id, out decimal limit))
                {
                    _issues.Add(new YtbsIssue
                    {
                        Level = YtbsIssueLevel.Fail,
                        Channel = channel,
                        Kind = YtbsIssueKind.OverLimitSkip,
                        Slot = slot.Instant,
                        PlantId = id,
                        PlantName = names.NameOf(id),
                        Value = item?.veriDeger,
                        Limit = limit,
                        Reason = "Ölçüm TEİAŞ limitinin üzerinde; fiziksel müdahale."
                    });
                    continue;
                }

                if (item is not null)
                {
                    invalid.Add(item);
                }
            }

            if (invalid.Count > 0)
            {
                RecordPackageReject(
                    channel,
                    slot.Instant,
                    new AnlikUretimEkleRequest
                    {
                        baglantiAnlasmasiSirketiLisansNo = paket.baglantiAnlasmasiSirketiLisansNo,
                        veri = invalid
                    },
                    string.Join(" ", detail.Messages.Where(m => !m.Contains("üst limitin", StringComparison.OrdinalIgnoreCase))),
                    names);
            }
        }

        private Dictionary<int, decimal> LoadReportedLimits()
        {
            try
            {
                return _reportedLimitStore.Load(Settings.ConnectionString!);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TEIAS_ReportedLimit okunamadı; bu turda yalnızca mapping limiti kullanılır.");
                return [];
            }
        }

        private Dictionary<(int PlantId, YtbsChannel Channel), decimal> LoadValueScales()
        {
            try
            {
                return _scaleStore.Load(Settings.ConnectionString!);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TEIAS_ValueScale okunamadı; bu turda yalnızca tahmin kullanılır.");
                return [];
            }
        }

        private void LogPaketOzeti(string kanal, AnlikUretimEkleRequest paket, YtbsApiSlot slot)
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
                slot.Tarih,
                slot.Saat);
        }

        private void LogKapsamOzeti(int anlikGonderilen, int? saatlikGonderilen)
        {
            MappingCoverageSnapshot kapsam = GetMappingCoverage();
            if (saatlikGonderilen is int saatlik)
            {
                _logger.LogInformation(
                    "[KAPSAM] aktif_santral={Aktif} aktif_var={Var} anlik_gonderilen={Anlik} saatlik_gonderilen={Saatlik} ornegi_yok={Eksik} pasif_santral={Pasif}",
                    kapsam.ActivePlantCount,
                    kapsam.ActiveVarCount,
                    anlikGonderilen,
                    saatlik,
                    kapsam.AnlikMissingPlantCount,
                    kapsam.PasifOnlyPlantCount);
                return;
            }

            _logger.LogInformation(
                "[KAPSAM] aktif_santral={Aktif} aktif_var={Var} anlik_gonderilen={Anlik} ornegi_yok={Eksik} pasif_santral={Pasif}",
                kapsam.ActivePlantCount,
                kapsam.ActiveVarCount,
                anlikGonderilen,
                kapsam.AnlikMissingPlantCount,
                kapsam.PasifOnlyPlantCount);
        }

        private MappingCoverageSnapshot GetMappingCoverage()
        {
            int maxAgeMinutes = Math.Clamp(Settings.AnlikMaxAgeMinutes, 5, 120);
            const string sql = """
                SELECT
                    (SELECT COUNT(DISTINCT TEIAS_PLANT_ID)
                     FROM scada.TEIAS_Mapping
                     WHERE VAR_NAME LIKE '%.ActivePower') AS ActivePlantCount,
                    (SELECT COUNT(*)
                     FROM scada.TEIAS_Mapping
                     WHERE VAR_NAME LIKE '%.ActivePower') AS ActiveVarCount,
                    (SELECT COUNT(DISTINCT p.TEIAS_PLANT_ID)
                     FROM scada.TEIAS_Mapping p
                     WHERE p.VAR_NAME LIKE '%.ActivePower\_PASIF' ESCAPE '\\'
                       AND NOT EXISTS (
                           SELECT 1
                           FROM scada.TEIAS_Mapping a
                           WHERE a.TEIAS_PLANT_ID = p.TEIAS_PLANT_ID
                             AND a.VAR_NAME LIKE '%.ActivePower'
                       )) AS PasifOnlyPlantCount,
                    (SELECT COUNT(DISTINCT m.TEIAS_PLANT_ID)
                     FROM scada.TEIAS_Mapping m
                     WHERE m.VAR_NAME LIKE '%.ActivePower'
                       AND NOT EXISTS (
                           SELECT 1
                           FROM scada.Zenon_Export_DATA d
                           WHERE d.VAR = m.VAR_NAME
                             AND d.TIMESTAMP_S >= UNIX_TIMESTAMP(NOW() - INTERVAL {0} MINUTE)
                       )) AS AnlikMissingPlantCount
                """;

            try
            {
                using var connection = new MySqlConnection(Settings.ConnectionString);
                return connection.QuerySingle<MappingCoverageSnapshot>(string.Format(sql, maxAgeMinutes));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kapsam özeti okunamadı.");
                return new MappingCoverageSnapshot();
            }
        }

        private List<ScadaDbRow> GetScadaDataList(DateTime windowEnd)
        {
            using var connection = new MySqlConnection(Settings.ConnectionString);
            int maxAgeMinutes = Math.Clamp(Settings.AnlikMaxAgeMinutes, 5, 120);
            DateTime windowStart = windowEnd.AddMinutes(-maxAgeMinutes);
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
                    WHERE TIMESTAMP_S >= UNIX_TIMESTAMP(@From)
                      AND TIMESTAMP_S <= UNIX_TIMESTAMP(@To)
                    GROUP BY VAR
                ) latest ON d.VAR = latest.VAR AND d.TIMESTAMP_S = latest.MaxTime
                WHERE {TeiasMappingRules.LiveActivePowerSqlPredicate}
                GROUP BY m.TEIAS_PLANT_ID, m.LICENSE_NO, m.MAX_CAPACITY;";

            try
            {
                return connection.Query<ScadaDbRow>(sql, new { From = windowStart, To = windowEnd }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MySQL'den anlık SCADA verisi çekilirken bir hata oluştu.");
                return new List<ScadaDbRow>();
            }
        }

        private List<SaatlikUretimVeri> GetSaatlikScadaDataList(DateTime windowEnd)
        {
            using var connection = new MySqlConnection(Settings.ConnectionString);
            DateTime windowStart = windowEnd.AddHours(-1);
            string sql = $@"
                SELECT
                    m.TEIAS_PLANT_ID AS LisanssizSantralId,
                    m.LICENSE_NO AS BaglantiAnlasmasiSirketiLisansNo,
                    m.MAX_CAPACITY AS MaxCapacity,
                    CAST(SUM(CAST(REPLACE(d.VALUE, ',', '.') AS DECIMAL(10,4))) AS DECIMAL(10,4)) AS ToplamEnerjiMWh
                FROM scada.TEIAS_Mapping m
                INNER JOIN scada.Zenon_Export_DATA d
                    ON d.VAR = {TeiasMappingRules.HourlyEnergyVarSql}
                INNER JOIN (
                    SELECT VAR, MAX(TIMESTAMP_S) AS MaxTime
                    FROM scada.Zenon_Export_DATA
                    WHERE TIMESTAMP_S >= UNIX_TIMESTAMP(@From)
                      AND TIMESTAMP_S <  UNIX_TIMESTAMP(@To)
                      AND VAR LIKE '%.ActiveEnergy.Exported.Hourly'
                    GROUP BY VAR
                ) latest ON d.VAR = latest.VAR AND d.TIMESTAMP_S = latest.MaxTime
                WHERE {TeiasMappingRules.LiveActivePowerSqlPredicate}
                GROUP BY m.TEIAS_PLANT_ID, m.LICENSE_NO, m.MAX_CAPACITY;";

            try
            {
                List<SaatlikUretimVeri> rows = connection.Query<SaatlikUretimVeri>(sql, new { From = windowStart, To = windowEnd }).ToList();
                int mapped = connection.ExecuteScalar<int>(
                    $@"SELECT COUNT(DISTINCT TEIAS_PLANT_ID)
                       FROM scada.TEIAS_Mapping m
                       WHERE {TeiasMappingRules.LiveActivePowerSqlPredicate}");
                if (rows.Count < mapped && DateTime.Now - windowEnd < TimeSpan.FromHours(2))
                {
                    _logger.LogWarning(
                        "Saatlik enerji VAR yok veya önceki saatte örnek yok. mapping_santral={Mapped} enerji_santral={Enerji}. ActivePower fallback yok.",
                        mapped,
                        rows.Count);
                }

                return rows;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MySQL'den Saatlik SCADA verisi çekilirken bir hata oluştu.");
                return new List<SaatlikUretimVeri>();
            }
        }

        private PlantNameDirectory LoadPlantNames()
        {
            try
            {
                return PlantNameDirectory.Load(Settings.ConnectionString);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Santral adları okunamadı; mailde ID kullanılacak.");
                return new PlantNameDirectory([]);
            }
        }

        private List<(int PlantId, string? LicenseNo)> GetLiveMappedPlants()
        {
            using var connection = new MySqlConnection(Settings.ConnectionString);
            return connection.Query<(int PlantId, string? LicenseNo)>(
                $@"SELECT DISTINCT m.TEIAS_PLANT_ID AS PlantId, m.LICENSE_NO AS LicenseNo
                   FROM scada.TEIAS_Mapping m
                   WHERE {TeiasMappingRules.LiveActivePowerSqlPredicate}").ToList();
        }

        private void RecordAllMappedMissing(YtbsChannel channel, DateTime slot, PlantNameDirectory names)
        {
            RecordMissingPlants(channel, slot, [], names);
        }

        private void RecordMissingPlants(
            YtbsChannel channel,
            DateTime slot,
            IEnumerable<int> presentIds,
            PlantNameDirectory names)
        {
            HashSet<int> present = presentIds.ToHashSet();
            List<(int PlantId, string? LicenseNo)> mapped;
            try
            {
                mapped = GetLiveMappedPlants();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Eksik santral listesi okunamadı.");
                return;
            }

            foreach (var plant in mapped)
            {
                if (present.Contains(plant.PlantId))
                {
                    continue;
                }

                _issues.Add(new YtbsIssue
                {
                    Level = YtbsIssueLevel.Warn,
                    Channel = channel,
                    Kind = YtbsIssueKind.NoSample,
                    Slot = slot,
                    LicenseNo = plant.LicenseNo,
                    PlantId = plant.PlantId,
                    PlantName = names.NameOf(plant.PlantId),
                    Reason = channel == YtbsChannel.Saatlik
                        ? "Saatlik enerji VAR yok veya önceki saatte örnek yok."
                        : "Son 75 dk içinde ActivePower örneği yok."
                });
            }
        }

        private void RecordPackageReject(
            YtbsChannel channel,
            DateTime slot,
            AnlikUretimEkleRequest paket,
            string? errorBody,
            PlantNameDirectory names)
        {
            TeiasRejectDetail detail = TeiasErrorParser.Parse(errorBody);
            var related = (paket.veri ?? [])
                .Select(v => names.RefOf(v.lisanssizSantralId))
                .GroupBy(p => p.Id)
                .Select(g => g.First())
                .OrderBy(p => p.Id)
                .ToList();

            string reason = detail.Messages.Count > 0
                ? string.Join(" ", detail.Messages)
                : "TEİAŞ lisans paketini reddetti.";

            if (detail.CulpritIds.Count > 0)
            {
                string culprits = string.Join(
                    ", ",
                    detail.CulpritIds.Select(id => $"{id} ({names.NameOf(id)})"));
                reason = $"{reason} Suçlu santral: {culprits}.";
            }

            _issues.Add(new YtbsIssue
            {
                Level = YtbsIssueLevel.Fail,
                Channel = channel,
                Kind = YtbsIssueKind.PackageRejected,
                Slot = slot,
                LicenseNo = paket.baglantiAnlasmasiSirketiLisansNo,
                PlantId = detail.CulpritIds.FirstOrDefault(),
                PlantName = detail.CulpritIds.Count > 0 ? names.NameOf(detail.CulpritIds[0]) : null,
                Reason = reason,
                RelatedPlants = related
            });
        }

        private async Task FlushHourlyMailAsync(DateTime now)
        {
            DateTime periodStart = YtbsTimeSlots.GetPreviousHourStart(now);
            List<YtbsIssue> hourIssues = _issues.DequeueHour(periodStart);
            YtbsHourlyReport? report = YtbsHourlyReportFormatter.Format(periodStart, hourIssues);
            if (report is null)
            {
                _logger.LogInformation(
                    "YTBS saatlik mail yok (sorun yok). Dönem {Baslangic}–{Bitis}.",
                    YtbsTimeSlots.FormatSaat(periodStart),
                    YtbsTimeSlots.FormatSaat(periodStart.AddHours(1)));
                return;
            }

            await _mailSender.SendAsync(report.Subject, report.Body);
        }
    }
}
