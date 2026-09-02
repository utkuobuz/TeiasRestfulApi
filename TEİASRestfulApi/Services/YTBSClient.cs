using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using Microsoft.Extensions.Options;
using TEİASRestfulApi.DTOs;

namespace TEİASRestfulApi;

public class InvariantDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.GetDouble();
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        writer.WriteRawValue(value.ToString(CultureInfo.InvariantCulture), false);
    }
}

public class InvariantNullableDoubleConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.Null ? null : reader.GetDouble();
    }

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteRawValue(value.Value.ToString(CultureInfo.InvariantCulture), false);
        else
            writer.WriteNullValue();
    }
}

public class YTBSClient
{
    private const string DefaultBaseUrl = "https://ytbsws.teias.gov.tr/ytbs-webservis/rest/";

    private readonly HttpClient _httpClient;
    private readonly ILogger<YTBSClient> _logger;
    private string _serviceKey = "";
    private string _currentJeton = "";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false,
        Converters =
        {
            new InvariantDoubleConverter(),
            new InvariantNullableDoubleConverter()
        }
    };

    public YTBSClient(HttpClient httpClient, ILogger<YTBSClient> logger, IOptions<YtbsSettings> settings)
    {
        _httpClient = httpClient;
        _logger = logger;

        string baseUrl = string.IsNullOrWhiteSpace(settings.Value.BaseUrl)
            ? DefaultBaseUrl
            : settings.Value.BaseUrl.Trim();
        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }

        _httpClient.BaseAddress = new Uri(baseUrl);
        _serviceKey = settings.Value.ServiceKey ?? "";
    }

    public void SetServiceKey(string? serviceKey)
    {
        if (!string.IsNullOrWhiteSpace(serviceKey))
        {
            _serviceKey = serviceKey;
        }
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        var loginData = new LoginRequest { kullaniciAdi = username, sifre = password };
        var json = JsonSerializer.Serialize(loginData, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("SERVICE_KEY", _serviceKey);

        var response = await _httpClient.PostAsync("yetkilendirme/login", content);
        var resultJson = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            var result = JsonSerializer.Deserialize<LoginResponse>(resultJson);

            if (result != null && result.basarili == true)
            {
                _currentJeton = result.jeton ?? "";
                return true;
            }

            _logger.LogError("TEİAŞ login yanıtı başarısız. Gövde: {Body}", resultJson);
            return false;
        }

        _logger.LogError("TEİAŞ Login Hatası: {Status} - Detay: {Body}", response.StatusCode, resultJson);
        return false;
    }

    public async Task SendUretimVerisiAsync(AnlikUretimEkleRequest requestData)
    {
        await SendPaketAsync("veritoplama/anliklisanssizsantralarz/ekle", "ANLIK", requestData);
    }

    public async Task SendSaatlikUretimVerisiAsync(AnlikUretimEkleRequest requestData)
    {
        await SendPaketAsync("veritoplama/saatliklisanssizsantraluretim/ekle", "SAATLİK", requestData);
    }

    private async Task SendPaketAsync(string endpoint, string kanal, AnlikUretimEkleRequest requestData)
    {
        if (string.IsNullOrEmpty(_currentJeton))
        {
            throw new InvalidOperationException("Önce Login olmalısınız!");
        }

        var json = JsonSerializer.Serialize(requestData, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogDebug("[{Kanal}] İstek gövdesi: {Json}", kanal, json);

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("SERVICE_KEY", _serviceKey);
        _httpClient.DefaultRequestHeaders.Add("AUTH_TOKEN", _currentJeton);

        var response = await _httpClient.PostAsync(endpoint, content);
        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation(
                "[{Kanal}] Veri başarıyla gönderildi. Lisans No: {Lisans}",
                kanal,
                requestData.baglantiAnlasmasiSirketiLisansNo);
            return;
        }

        string errorContent = await response.Content.ReadAsStringAsync();
        _logger.LogError(
            "[{Kanal}] Veri Gönderim Hatası: {Status} - Detay: {Detay}",
            kanal,
            response.StatusCode,
            errorContent);
    }
}
