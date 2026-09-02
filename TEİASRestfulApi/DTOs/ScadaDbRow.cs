namespace TEİASRestfulApi.DTOs
{
    public class ScadaDbRow
    {
        public string VAR_NAME { get; set; }
        public int LisanssizSantralId { get; set; }
        public string BaglantiAnlasmasiSirketiLisansNo { get; set; }
        public decimal AktifGuc { get; set; }
        public string Tarih { get; set; } // SCADA'dan gelen gerçek tarih
        public string Saat { get; set; }  // SCADA'dan gelen gerçek saat
        public decimal? MaxCapacity { get; set; }
    }
}