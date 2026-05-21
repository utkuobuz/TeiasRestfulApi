# ⚡ TEİAŞ YTBS SCADA Entegrasyon Servisi

![.NET Version](https://img.shields.io/badge/.NET-10.0-blueviolet)
![Platform](https://img.shields.io/badge/Platform-Windows%20Server-blue)
![License](https://img.shields.io/badge/License-MIT-green)

**TEİAŞ Yük Tevzi Bilgi Sistemi (YTBS)** ile endüstriyel **Zenon SCADA** sistemleri arasında tam otomatik, kesintisiz ve güvenli bir köprü kurmak üzere .NET 10 Worker Service mimarisiyle geliştirilmiş arka plan servisidir.

Bu servis, sahadaki yüzlerce (örn: 443 adet) lisanssız güneş enerjisi santralinin (GES) üretim verilerini SCADA veritabanından okur, **Bağlantı Anlaşması Lisans Numaralarına** göre gruplar ve TEİAŞ'ın belirlediği katı RESTful API kurallarına uygun olarak her 15 dakikada bir merkeze iletir.

---

## 🚀 Öne Çıkan Özellikler

* **Tam Otomatik Döngü:** `PeriodicTimer` kullanılarak bellek sızıntısı olmadan (memory-leak free) hassas 15 dakikalık periyotlarla çalışır.
* **Akıllı Token Yönetimi:** Her veri gönderiminden önce TEİAŞ `yetkilendirme/login` servisi ile konuşarak güncel bir *Jeton (Auth Token)* alır ve HTTP başlıklarına dinamik olarak ekler.
* **Dinamik Filo Gruplama:** Veritabanından gelen yüzlerce santral bilgisini, TEİAŞ'ın istediği formatta "Bağlantı Anlaşması Lisans Numarası"na (`baglantiAnlasmasiSirketiLisansNo`) göre otomatik olarak gruplar ve paketler halinde gönderir.
* **Windows Service Uyumu:** Windows Server ortamlarında arka planda (Background Service) çalışmaya tam uyumludur. Oturum açmaya gerek kalmadan sunucu başlangıcında otomatik devreye girer.
* **Merkezi Yapılandırma:** Kullanıcı adı, şifre ve statik `SERVICE_KEY` gibi hassas veriler doğrudan `appsettings.json` üzerinden güvenle yönetilir.

---

## 🛠️ Kullanılan Teknolojiler

* **C# & .NET 10** (Worker Service Mimarisi)
* **Dapper** (SCADA SQL Veritabanı işlemleri için yüksek performanslı Micro-ORM)
* **IHttpClientFactory** (Performanslı ve güvenli HTTP istek yönetimi)
* **Microsoft.Extensions.Hosting.WindowsServices** (Windows Hizmetleri Entegrasyonu)

---

## ⚙️ Kurulum ve Yapılandırma

### 1. Gereksinimler
* Çalıştırılacak sunucuda **.NET 10 Runtime** kurulu olmalıdır (Self-Contained publish alınırsa buna da gerek kalmaz).
* SCADA veritabanına (SQL Server) ağ erişimi.

### 2. Ayarlar (appsettings.json)
Projeyi çalıştırmadan önce kök dizindeki `appsettings.json` dosyasını kendi TEİAŞ YTBS bilgilerinizle doldurunuz:

```json
{
  "YtbsSettings": {
    "ServiceKey": "YTBS_PORTALINDAN_ALINAN_STATIK_SERVICE_KEY",
    "KullaniciAdi": "kullanici.adi",
    "Sifre": "Sifre123*",
    "BaseUrl": "[https://ytbsws.teias.gov.tr/ytbs-webservis/rest/](https://ytbsws.teias.gov.tr/ytbs-webservis/rest/)"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

(Not: Service Key, YTBS portalında Sistem Yönetimi -> Sistem Parametresi -> YTBS Web Servis menüsünden temin edilmelidir.)

## 💻 Windows Servisi Olarak Kurulum
Bu uygulama Windows Server 2019/2022 üzerinde bir hizmet (service) olarak çalışacak şekilde tasarlanmıştır.

1. Projeyi Release modunda derleyin (Publish).

2. Çıktı klasörünü sunucuda uygun bir dizine (Örn: C:\Yazilimlar\TeiasScadaServis) taşıyın.

3. Yönetici yetkileriyle (Run as Administrator) bir PowerShell veya CMD penceresi açın.

4. Aşağıdaki komutla servisi Windows'a kaydedin:
```bash
sc.exe create "TeiasScadaAktarim" binpath= "C:\Yazilimlar\TeiasScadaServis\TEİASRestfulApi.exe" start= auto
```

5. Servisi başlatın
```bash
sc.exe start "TeiasScadaAktarim"
```

## 📁 Proje Klasör Yapısı
. /DTOs: TEİAŞ'ın JSON formatına birebir uyan Veri Transfer Objeleri (Request/Response modelleri) ve SCADA SQL eşleştirme sınıfları.

. /Services/YTBSClient.cs: TEİAŞ sunucularıyla iletişim kuran, Header yönetimini ve HTTP POST işlemlerini üstlenen ana motor.

. YtbsWorker.cs: Sistemin beyni. 15 dakikada bir uyanıp, veritabanından (Zenon SCADA) okuma yapan, veriyi gruplayan ve YTBSClient'a teslim eden background servisi.

. Program.cs: Dependency Injection (DI) ayarlarının, loglamanın ve Windows Service yapılandırmasının yapıldığı başlangıç noktası.

## 🤝 Katkıda Bulunma
Bu proje, endüstriyel veri entegrasyonu standartlarına uygun olarak geliştirilmiştir. Geliştirmelere ve Pull Request (PR) gönderimlerine açıktır. Veritabanı sorgu detayları (SCADA Tablo yapıları) firmalara özel olduğu için GetScadaDataList metodu içerisinde Dapper entegrasyonu için şablon bırakılmıştır.
