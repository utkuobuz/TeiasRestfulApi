# TEİAŞ YTBS SCADA Entegrasyon Servisi

**.NET 10** Worker Service. Zenon SCADA (MySQL) üzerindeki lisanssız GES üretimini TEİAŞ YTBS REST API’sine iki kanalda iletir:

| Kanal | Periyot | Birim | Endpoint |
|---|---|---|---|
| Anlık arz | Her çeyrek (`:00 :15 :30 :45`) | Anlık güç (MW) | `veritoplama/anliklisanssizsantralarz/ekle` |
| Saatlik üretim | Her saat başı (`:00`) | Önceki saatin enerjisi (MWh) | `veritoplama/saatliklisanssizsantraluretim/ekle` |

Her iki kanal da `Zenon_Export_DATA` içindeki `*.ActivePower` değişkenlerini `TEIAS_Mapping` üzerinden okur. Saatlik değer, aynı santrale bağlı her `VAR` için saatin ortalaması alınıp toplanarak MWh yaklaşımı üretir (ortalama MW × 1 saat ≈ MWh).

## Davranış

* Zamanlayıcı duvar saatine kilitlidir; servis açılınca bir sonraki (veya 30 sn içindeki) çeyreği bekler.
* 15 dakikalık `saat` alanı her zaman `00 / 15 / 30 / 45` olarak gider.
* Anlık sorguda yalnızca son **75 dakika** içinde örneği olan santraller gönderilir. Zenon `Zenon_Export_DATA` tablosuna saatte bir (`HH:00`) bastığı için pencere bir saatlik dump’ı kaçırmayacak kadar geniştir. Daha eski örnekler atlanır (sıfır basılmaz).
* Aynı `TEIAS_PLANT_ID` altındaki birden fazla `ActivePower` (trafo/inverter) timestamp’ten bağımsız toplanır.
* Saatlik etiket, tamamlanmış saatin başıdır (`15:05` → `14:00`; `00:05` → bir önceki gün `23:00`).
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
    "AnlikMaxAgeMinutes": 75
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

* `YtbsWorker.cs` — çeyrek saat hizası, SCADA okuma, paketleme
* `YtbsTimeSlots.cs` / `ScadaValueNormalizer.cs` — dilim ve birim kuralları
* `Services/YTBSClient.cs` — login ve POST
* `TEİASRestfulApi.Tests` — dilim, birim ve agregasyon testleri

```bash
dotnet test TEİASRestfulApi.slnx
```
