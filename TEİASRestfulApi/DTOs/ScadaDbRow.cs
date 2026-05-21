namespace TEİASRestfulApi.DTOs
{
    public class ScadaDbRow
    {
        public int LisanssizSantralId { get; set; }
        public string? BaglantiAnlasmasiSirketiLisansNo { get; set; }
        public double? AktifGuc { get; set; } // SCADA'dan okunan MW değeri
    }
}