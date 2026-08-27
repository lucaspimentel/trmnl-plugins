using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TrmnlApi.Models.OpenMeteo;

namespace TrmnlApi.Services;

/// <summary>
/// Forward geocoding: turns what a user typed into coordinates. Mirrors
/// <see cref="OpenMeteoClient"/>, including its free/customer host switch, because the same
/// subscription covers both endpoints.
/// </summary>
public class OpenMeteoGeocodingClient : IOpenMeteoGeocodingClient
{
    private const string FreeBaseUrl = "https://geocoding-api.open-meteo.com/v1/search";
    private const string CustomerBaseUrl = "https://customer-geocoding-api.open-meteo.com/v1/search";

    /// <summary>
    /// Prefix of every GeoNames code for a populated place: PPL, PPLA, PPLA2, PPLC and friends.
    /// A postal code resolves to a plain PPL, so this keeps them.
    /// </summary>
    private const string PopulatedPlacePrefix = "PPL";

    /// <summary>
    /// More than the one result we return, because the ranking mixes populated places in with
    /// headlands and airports: a search for "Portland, ME" puts the city first but a different
    /// query might not, and filtering needs something left to choose from.
    /// </summary>
    private const int SearchCount = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public OpenMeteoGeocodingClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;

        var apiKey = configuration[OpenMeteoClient.ApiKeySettingName];
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
    }

    public async Task<OpenMeteoGeocodingResult?> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var baseUrl = _apiKey is null ? FreeBaseUrl : CustomerBaseUrl;

        var url = $"{baseUrl}" +
                  $"?name={Uri.EscapeDataString(query)}" +
                  $"&count={SearchCount}&language=en&format=json";

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
                $"Open-Meteo geocoding returned {(int)response.StatusCode} {response.StatusCode}: {snippet}",
                inner: null,
                statusCode: response.StatusCode);
        }

        var result = await JsonSerializer.DeserializeAsync<OpenMeteoGeocodingResponse>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            JsonOptions,
            cancellationToken);

        // Results is null on a miss, and null again if the body was literally "null". Both mean
        // "nothing matched", so neither is worth distinguishing to a caller.
        return result?.Results?.FirstOrDefault(IsPopulatedPlace);
    }

    // Ordinal: these are fixed GeoNames identifiers, not text in anyone's language.
    private static bool IsPopulatedPlace(OpenMeteoGeocodingResult result) =>
        result.FeatureCode?.StartsWith(PopulatedPlacePrefix, StringComparison.Ordinal) == true;
}
