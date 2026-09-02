using System;
using System.Collections.Generic;
using System.Text;

namespace TEİASRestfulApi.DTOs
{
    // "veri" dizisinin (array) içindeki her bir ölçüm modeli
    public class UretimVeriItem
    {
        public string? tarih { get; set; }
        public string? saat { get; set; }
        public int lisanssizSantralId { get; set; }
        public double? veriDeger { get; set; } // YTBS API'si double bekliyor - yuvarlama ile precision hatasını engelliyoruz
    }

    public class SaatlikUretimVeri
    {
        public int LisanssizSantralId { get; set; }
        public string Tarih { get; set; }
        public string Saat { get; set; }
        public decimal ToplamEnerjiMWh { get; set; }
        public decimal? MaxCapacity { get; set; } // BURASI: double yerine decimal yapıldı
        public string BaglantiAnlasmasiSirketiLisansNo { get; set; }
    }
}
