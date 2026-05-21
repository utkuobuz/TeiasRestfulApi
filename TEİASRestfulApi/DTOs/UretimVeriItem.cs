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
        public double? veriDeger { get; set; } // "deger" yerine "veriDeger" yapıldı
    }
}
