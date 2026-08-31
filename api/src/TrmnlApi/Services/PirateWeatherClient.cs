using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TrmnlApi.Models.PirateWeather;

namespace TrmnlApi.Services;

public class PirateWeatherClient : IPirateWeatherClient
{
    public const string ApiKeySettingName = "PIRATE_WEATHER_API_KEY";

    /// <summary>
    /// Header the API key travels in. Pirate Weather's primary documented form puts the key in the
    /// URL path, which is unusable here: the tracer names client spans after the path, so every
    /// call published the key to APM. It leaked in both environments from the day tracing was
    /// enabled until 2026-08-31.
    /// </summary>
    /// <remarks>
    /// Header auth is documented in prose only (docs/API.md in Pirate-Weather/pirateweather) and is
    /// contradicted a few lines earlier by "Request headers are not parsed by the API", which is
    /// false. It is also absent from their OpenAPI spec, which declares the key as
    /// <c>in: path</c> and no securitySchemes at all. So nothing upstream promises to keep this
    /// working: <c>PirateWeatherClientTests</c> pins it, and a 401 in production is the signal that
    /// it went away.
    /// </remarks>
    public const string ApiKeyHeaderName = "apikey";

    /// <summary>
    /// Stands in for the key in the URL path. The path segment is still required (the gateway
    /// routes on the URL shape), but its value is ignored when the header is present. Verified: a
    /// placeholder path with no header, or with a wrong header, is a 401, so this is not an auth
    /// bypass. It is deliberately readable, because it shows up in span resource names.
    /// </summary>
    public const string PathKeyPlaceholder = "header-auth";

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
        var url = $"https://api.pirateweather.net/forecast/{PathKeyPlaceholder}/{latitude},{longitude}?units={units}&exclude=minutely,alerts,flags";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(ApiKeyHeaderName, _apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);

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
