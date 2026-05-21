using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TEİASRestfulApi.DTOs;

public class YTBSClient
{
    private readonly HttpClient _httpClient;
    private string _serviceKey = "";
    private string _currentJeton = "";

    public YTBSClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://ytbsws.teias.gov.tr/ytbs-webservis/rest/");
    }

    // Worker'dan ServiceKey'i almak için küçük bir metod
    public void SetServiceKey(string serviceKey)
    {
        _serviceKey = serviceKey;
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        var loginData = new LoginRequest { kullaniciAdi = username, sifre = password };
        var content = new StringContent(JsonSerializer.Serialize(loginData), Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Clear();
        // Dokümana göre doğru Header ismi: SERVICE_KEY 
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
        return false;
    }

    // Parametre tipini yeni Request modelimizle değiştirdik
    public async Task SendUretimVerisiAsync(AnlikUretimEkleRequest requestData)
    {
        if (string.IsNullOrEmpty(_currentJeton)) throw new Exception("Önce Login olmalısınız!");

        var content = new StringContent(JsonSerializer.Serialize(requestData), Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Clear();
        // Dokümana göre doğru Header isimleri 
        _httpClient.DefaultRequestHeaders.Add("SERVICE_KEY", _serviceKey);
        _httpClient.DefaultRequestHeaders.Add("AUTH_TOKEN", _currentJeton);

        var response = await _httpClient.PostAsync("veritoplama/anliklisanssizsantralarz/ekle", content);

        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine("Veri başarıyla gönderildi.");
        }
        else
        {
            Console.WriteLine($"Hata: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
        }
    }
}