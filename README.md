# TEİAŞ YTBS SCADA Entegrasyon Servisi

**.NET 10** Worker Service. Zenon SCADA (MySQL) üzerindeki lisanssız GES üretimini TEİAŞ YTBS REST API’sine iki kanalda iletir:

| Kanal | Periyot | Birim | Endpoint |
|---|---|---|---|
| Anlık arz | Her çeyrek (`:00 :15 :30 :45`) | Anlık güç (MW) | `veritoplama/anliklisanssizsantralarz/ekle` |
| Saatlik üretim | Her saat başı (`:00`) | Önceki saatin enerjisi (MWh) | `veritoplama/saatliklisanssizsantraluretim/ekle` |

Birim tabloda yazmaz; **hangi VAR + hangi endpoint** ayırır. `TEIAS_Mapping` yalnızca `*.ActivePower` tutar. Saatlik enerji adı kodda türetilir: `.ActivePower` → `.ActiveEnergy.Exported.Hourly`. `ActiveEnergy.Exported` ve `ReactivePower` YTBS’e gitmez.

| Kanal | VAR | Hesap |
|---|---|---|
| Anlık MW | `*.ActivePower` | Son 75 dk en taze güç; santralde SUM |
| Saatlik MWh | `*.ActiveEnergy.Exported.Hourly` | Önceki saatte her VAR’ın **son** örneği (15 dk kopyaları toplanmaz); inverter’lar SUM. Enerji yoksa ActivePower’a düşülmez |

## Davranış

* Açılışta bir tur hemen gider; sonraki turlar duvar saati çeyreğine (`:00 :15 :30 :45`) kilitlidir.
* TEİAŞ `saat` etiketi **dilim bitişidir** (`00:15 … 23:45, 24:00`). `00:00` gönderilmez.
* Anlık: 12:17 → `12:15`; 12:00 → `12:00`; 00:00–00:14 → önceki takvim günü + `24:00`.
* Saatlik: SQL hâlâ önceki duvar saatini okur (08:05 → 07:00–08:00 enerji); TEİAŞ etiketi saatin **bitişidir** (`08:00`). 00:05 → önceki gün `24:00`.
* Anlık sorguda yalnızca son **75 dakika** içinde `ActivePower` örneği olan santraller gönderilir. 15 dk dump’ta pencere hâlâ güvenli. Daha eski örnekler atlanır.
* Aynı `TEIAS_PLANT_ID` altındaki birden fazla inverter timestamp’ten bağımsız toplanır (MW: güç, MWh: saatlik enerji).
* `*.ActivePower_PASIF` gönderime girmez. Saatlik kanal güç VAR’ını MWh diye kullanmaz.
* `MAX_CAPACITY < 0,250` (250 kW altı, örn. GELAL 7334) paketten çıkar; mailde yok.
* SCADA yanlış 10’luk basamak: oran `ölçüm/limit >= 4` ise en küçük `10^n` değeri limitin altına indirir; bölen `TEIAS_ValueScale` tablosuna yazılır (anlık/saatlik ayrı). Oran `< 4` gerçek aşımdır.
* Ölçek sonrası veya TEİAŞ 412 sonrası limit aşılan santral paketten çıkar, mailde **FAIL** (fiziksel müdahale); kardeşler aynı turda tekrar gönderilir. Değer tıraşlanmaz. `MAX_CAPACITY` 412’den yazılmaz; TEİAŞ limiti `TEIAS_ReportedLimit` tablosuna alınır. Değer limitin altına düşünce santral yeniden gider (kara liste yok).
* HTTP 200 yetmez; gövdede `basarili` veya `gecerli` false ise paket reddedilmiş sayılır.
* Jeton 50 dk’ya kadar yeniden kullanılır; süresi dolunca veya servis kapanınca `yetkilendirme/logout`.
* Son 4 günde TEİAŞ’a yazılmamış dilim (sunucu kapalı, login yok, paket reddi, geç gelen SCADA) sonraki turlarda tekrar denenir; tur başına en fazla 12 dilim. Limit/250 kW elemesi tekrar edilmez.
* Her döngü `[KAPSAM]` özeti yazar: aktif santral, gönderilen, örneği yok, PASİF. Lisans reddi `adet` ile loglanır.
* Saat başında, o saatteki 4 anlık tur + saatlik turda FAIL/WARN varsa alıcılara özet mail gider. Sorun yoksa mail yok. Alıcılar `YtbsSettings:Mail:To` (appsettings.Local.json); yeniden derleme gerekmez.
* `ActivePowerUnit` `kW` ise gönderimden önce değer 1000’e bölünür. Varsayılan `MW`’dir.

## Yapılandırma

Sırlar `appsettings.json` içinde tutulmaz. Kopyalayın:

```bash
copy TEİASRestfulApi\appsettings.Local.json.example TEİASRestfulApi\appsettings.Local.json
```

`appsettings.Local.json` git’e girmez ve publish paketine kopyalanmaz. Sunucuda exe’nin yanına koyun veya ortam değişkeni kullanın:

```
YtbsSettings__ServiceKey
YtbsSettings__KullaniciAdi
YtbsSettings__Sifre
YtbsSettings__ConnectionString
YtbsSettings__ActivePowerUnit
YtbsSettings__AnlikMaxAgeMinutes
YtbsSettings__Mail__Password
YtbsSettings__Mail__To__0
```

Gmail normal hesap şifresi çalışmaz; [uygulama şifresi](https://myaccount.google.com/apppasswords) gerekir. `Mail:To` listesini değiştirmek yeter.

Santral adları `scada.TEIAS_PlantName` tablosundan okunur:

```bash
py -3 tools\ytbs_plant_names.py
py -3 tools\ytbs_plant_names.py --excel "YENİ_TEIAS.xlsx"
```

Örnek (değerler yerelde doldurulur):

```json
{
  "YtbsSettings": {
    "ServiceKey": "YTBS_PORTALINDAN_ALINAN_SERVICE_KEY",
    "KullaniciAdi": "kullanici.adi",
    "Sifre": "Sifre",
    "BaseUrl": "https://ytbsws.teias.gov.tr/ytbs-webservis/rest/",
    "ConnectionString": "Server=127.0.0.1;Port=3306;Database=scada;Uid=USER;Pwd=PASSWORD;",
    "ActivePowerUnit": "MW",
    "AnlikMaxAgeMinutes": 75,
    "Mail": {
      "Enabled": true,
      "Host": "smtp.gmail.com",
      "Port": 587,
      "UseStartTls": true,
      "UserName": "utkuobuz@gmail.com",
      "Password": "GMAIL_UYGULAMA_SIFRESI",
      "From": "utkuobuz@gmail.com",
      "FromName": "YTBS Aktarım",
      "To": [
        "utkuobuz@gmail.com",
        "scada@yesilpano.com"
      ]
    }
  }
}
```

Zenon `ActivePower` kW basıyorsa `ActivePowerUnit` değerini `kW` yapın. Aksi halde TEİAŞ’a giden MW değerleri ~1000 kat büyük olur ve `MAX_CAPACITY` tıraşı eğriyi bozar.

## Windows Servisi

Self-contained publish sonrası:

```bash
sc.exe create "TeiasScadaAktarim" binpath= "C:\Services\TeiasService\TEİASRestfulApi.exe" start= auto
sc.exe start "TeiasScadaAktarim"
```

Publish edilen klasöre `appsettings.Local.json` koyun; aksi halde servis sırlar eksik diye çıkış yapar.

## Proje yapısı

* `YtbsWorker.cs` — çeyrek saat hizası, SCADA okuma, paketleme, kapsam özeti
* `YtbsValueScaler.cs` / `Reporting/YtbsValueScaleStore.cs` — 250 kW altı eleme, 10^n ölçek, `TEIAS_ValueScale`
* `YtbsRetryPlanner.cs` / `Reporting/YtbsSlotDeliveryStore.cs` — 4 günlük kaçan dilim, `TEIAS_SlotPlant`
* `YtbsEffectiveLimit.cs` / `Reporting/YtbsReportedLimitStore.cs` — 412’den öğrenilen TEİAŞ limiti, kardeş paket tekrarı
* `TeiasMappingRules.cs` — `ActivePower` / PASİF / saatlik enerji VAR türetimi
* `YtbsTimeSlots.cs` / `ScadaValueNormalizer.cs` — dilim ve birim kuralları
* `Services/YTBSClient.cs` — login ve POST (reddedilen pakette lisans + adet)
* `Services/YtbsMailSender.cs` / `Reporting/` — saatlik FAIL/WARN özet maili
* `tools/ytbs_coverage.py` — Excel ∩ mapping ∩ Zenon boşluk CSV/SQL
* `tools/ytbs_reverify.py` — yeni TEİAŞ Excel doğrulama
* `tools/ytbs_plant_names.py` — Excel/CSV → `TEIAS_PlantName`
* `TEİASRestfulApi.Tests` — dilim, birim, agregasyon, mapping kuralları

```bash
dotnet test TEİASRestfulApi.slnx
py -3 tools\ytbs_coverage.py
py -3 tools\ytbs_reverify.py --excel "YENİ_TEIAS.xlsx"
```

Deploy ve 73 santral kontrolü: [../ScadaDocs/YTBS_DEPLOY_VERIFY.md](../ScadaDocs/YTBS_DEPLOY_VERIFY.md). Zenon iş emri: [../ScadaDocs/YTBS_ZENON_IS_EMRI.md](../ScadaDocs/YTBS_ZENON_IS_EMRI.md). Elle `sorgula` denemesi: [../ScadaDocs/YTBS_SORGULA_DENEME.md](../ScadaDocs/YTBS_SORGULA_DENEME.md).
