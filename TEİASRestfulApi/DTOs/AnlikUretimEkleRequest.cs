using System;
using System.Collections.Generic;
using System.Text;

namespace TEİASRestfulApi.DTOs
{
    // TEİAŞ'ın beklediği ana JSON objesi
    public class AnlikUretimEkleRequest
        {
            public string? baglantiAnlasmasiSirketiLisansNo { get; set; }
            public List<UretimVeriItem>? veri { get; set; }
        }
}
