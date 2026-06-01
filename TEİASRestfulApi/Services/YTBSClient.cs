using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TEİASRestfulApi.DTOs;

public class YTBSClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<YTBSClient> _logger; // Logger eklendi
    private string _serviceKey = "";
    private string _currentJeton = "";

    public YTBSClient(HttpClient httpClient, ILogger<YTBSClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = new Uri("https://ytbsws.teias.gov.tr/ytbs-webservis/rest/");
    }

    public void SetServiceKey(string serviceKey)
    {
        _serviceKey = serviceKey;
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        var loginData = new LoginRequest { kullaniciAdi = username, sifre = password };
        var content = new StringContent(JsonSerializer.Serialize(loginData), Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("SERVICE_KEY", _serviceKey);

        var response = await _httpClient.PostAsync("yetkilendirme/login", content);

        if (response.IsSuccessStatusCode)
        {
            var resultJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<LoginResponse>(resultJson);

            if (result != null && result.basarili == true)
            {
                _currentJeton = result.jeton;
                return true;
            }
        }
        else
        {
            _logger.LogError($"TEİAŞ Login Hatası: {response.StatusCode}");
        }
        return false;
    }

    public async Task SendUretimVerisiAsync(AnlikUretimEkleRequest requestData)
    {
        if (string.IsNullOrEmpty(_currentJeton)) throw new Exception("Önce Login olmalısınız!");

        var content = new StringContent(JsonSerializer.Serialize(requestData), Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("SERVICE_KEY", _serviceKey);
        _httpClient.DefaultRequestHeaders.Add("AUTH_TOKEN", _currentJeton);

        var response = await _httpClient.PostAsync("veritoplama/anliklisanssizsantralarz/ekle", content);

        if (response.IsSuccessStatusCode)
        {
            // Console yerine Logger kullanıyoruz
            _logger.LogInformation($"Veri başarıyla gönderildi. Lisans No: {requestData.baglantiAnlasmasiSirketiLisansNo}");
        }
        else
        {
            // Hata detayını sisteme kaydediyoruz
            string errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError($"Veri Gönderim Hatası: {response.StatusCode} - Detay: {errorContent}");
        }
    }
}