using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TrmnlApi.Models.OpenMeteo;

namespace TrmnlApi.Services;

public class OpenMeteoClient : IOpenMeteoClient
{
    public const string ApiKeySettingName = "OPEN_METEO_API_KEY";

    private const string FreeBaseUrl = "https://api.open-meteo.com/v1/forecast";
    private const string CustomerBaseUrl = "https://customer-api.open-meteo.com/v1/forecast";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public OpenMeteoClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;

        // Optional: when set, requests go to the paid customer API servers, which have
        // their own quota instead of the shared free-tier limits.
        var apiKey = configuration[ApiKeySettingName];
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
    }

    public async Task<OpenMeteoResponse> GetForecastAsync(double latitude, double longitude, bool metric = false, CancellationToken cancellationToken = default)
    {
        var tempUnit = metric ? "celsius" : "fahrenheit";
        var windUnit = metric ? "kmh" : "mph";
        var precipUnit = metric ? "mm" : "inch";

        var baseUrl = _apiKey is null ? FreeBaseUrl : CustomerBaseUrl;

        var url = $"{baseUrl}" +
                  $"?latitude={latitude}&longitude={longitude}" +
                  $"&current=temperature_2m,apparent_temperature,relative_humidity_2m,precipitation,weather_code,wind_speed_10m,wind_direction_10m,is_day" +
                  $"&hourly=temperature_2m,weather_code,precipitation_probability" +
                  $"&daily=temperature_2m_max,temperature_2m_min,weather_code,precipitation_probability_max,sunrise,sunset" +
                  $"&temperature_unit={tempUnit}&wind_speed_unit={windUnit}&precipitation_unit={precipUnit}" +
                  $"&timezone=auto&forecast_hours=25&forecast_days=6";

        if (_apiKey is not null)
        {
            url += $"&apikey={Uri.EscapeDataString(_apiKey)}";
        }

        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var snippet = body.Length > 500 ? body[..500] : body;
            throw new HttpRequestException(
                $"Open-Meteo returned {(int)response.StatusCode} {response.StatusCode}: {snippet}",
                inner: null,
                statusCode: response.StatusCode);
        }

        var result = await JsonSerializer.DeserializeAsync<OpenMeteoResponse>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            JsonOptions,
            cancellationToken);

        return result ?? throw new JsonException("Open-Meteo returned JSON null when an object was expected.");
    }
}
