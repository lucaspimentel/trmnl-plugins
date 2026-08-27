using System.Text.Json.Serialization;

namespace TrmnlApi.Models.OpenMeteo;

/// <summary>
/// A forward-geocoding search response.
/// </summary>
/// <param name="Results">
/// Null when nothing matched. Open-Meteo omits the key entirely on a miss rather than returning an
/// empty array, so this has to stay nullable: a query for "zzzzqqqq" comes back as
/// <c>{"generationtime_ms": 0.1}</c> and nothing else.
/// </param>
public record OpenMeteoGeocodingResponse(
    [property: JsonPropertyName("results")] List<OpenMeteoGeocodingResult>? Results
);

/// <param name="Admin1">
/// A display name ("Massachusetts"), not an ISO 3166-2 code. Open-Meteo carries only a GeoNames
/// admin1_id alongside it, so nothing here can populate a subdivision code.
/// </param>
/// <param name="FeatureCode">
/// The GeoNames feature class. Only the PPL* codes are populated places: a search can just as
/// easily return a headland (CAPE), an island (ISL) or an airport (AIRP).
/// </param>
public record OpenMeteoGeocodingResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude,
    [property: JsonPropertyName("country")] string? Country,
    [property: JsonPropertyName("country_code")] string? CountryCode,
    [property: JsonPropertyName("admin1")] string? Admin1,
    [property: JsonPropertyName("feature_code")] string? FeatureCode
);
