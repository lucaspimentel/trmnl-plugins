namespace TrmnlApi.Geo;

/// <summary>What the bundled dataset made of a typed place.</summary>
/// <param name="Latitude">Snapped to the 0.01-degree cache grid, like the vendor resolver's.</param>
/// <param name="Longitude">Snapped to the 0.01-degree cache grid.</param>
/// <param name="CityName">The matched city's own name, or null when the input was a postal code.
/// Postal place names are unusable as labels, so a postal hit supplies coordinates only and the
/// label comes from the reverse lookup. See the postal table note in docs/geographic-telemetry.md.
/// </param>
public readonly record struct GeoMatch(double Latitude, double Longitude, string? CityName);

/// <summary>
/// Turns typed text into coordinates without calling a vendor. Must never throw: a miss and a
/// failure both return null, and the vendor geocoder is the fallback for both.
/// </summary>
public interface ILocalGeocoder
{
    GeoMatch? Find(string text);
}

/// <summary>Used when no dataset is configured. Every lookup misses, so the vendor serves.</summary>
public sealed class NullLocalGeocoder : ILocalGeocoder
{
    public GeoMatch? Find(string text) => null;
}
