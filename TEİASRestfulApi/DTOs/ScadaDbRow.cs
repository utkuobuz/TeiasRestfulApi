namespace TEİASRestfulApi.DTOs
{
    public class ScadaDbRow
    {
        public int LisanssizSantralId { get; set; }
        public string BaglantiAnlasmasiSirketiLisansNo { get; set; }
        public double AktifGuc { get; set; }
        public string Tarih { get; set; } // SCADA'dan gelen gerçek tarih
        public string Saat { get; set; }  // SCADA'dan gelen gerçek saat
    }
}