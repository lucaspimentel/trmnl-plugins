using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TrmnlApi.Models.PirateWeather;

namespace TrmnlApi.Services;

public class PirateWeatherClient : IPirateWeatherClient
{
    public const string ApiKeySettingName = "PIRATE_WEATHER_API_KEY";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public PirateWeatherClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration[ApiKeySettingName]
            ?? throw new InvalidOperationException($"{ApiKeySettingName} is not configured.");
    }

    public async Task<PirateWeatherResponse> GetForecastAsync(double latitude, double longitude, bool metric = false, CancellationToken cancellationToken = default)
    {
        var units = metric ? "si" : "us";
        var url = $"https://api.pirateweather.net/forecast/{_apiKey}/{latitude},{longitude}?units={units}&exclude=minutely,alerts,flags";

        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var snippet = body.Length > 500 ? body[..500] : body;
            throw new HttpRequestException(
                $"Pirate Weather returned {(int)response.StatusCode} {response.StatusCode}: {snippet}",
                inner: null,
                statusCode: response.StatusCode);
        }

        var result = await JsonSerializer.DeserializeAsync<PirateWeatherResponse>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            JsonOptions,
            cancellationToken);

        return result ?? throw new JsonException("Pirate Weather returned JSON null when an object was expected.");
    }
}
