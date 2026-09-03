namespace TrmnlApi.Geo;

/// <summary>What the bundled dataset made of a typed place.</summary>
/// <param name="Latitude">Snapped to the 0.01-degree cache grid, like the vendor resolver's.</param>
/// <param name="Longitude">Snapped to the 0.01-degree cache grid.</param>
/// <param name="CityName">The matched city's own name, or null when the input was a postal code.
/// Postal place names are unusable as labels, so a postal hit supplies coordinates only and the
/// label comes from the reverse lookup. See the postal table note in docs/geographic-telemetry.md.
/// </param>
/// <param name="Hint">
/// Which <see cref="CountryHint"/> level actually settled the ranking, so the reported hint names
/// the signal that did the work rather than the strongest one supplied. <see cref="CountryHint.None"/>
/// when nothing the caller supplied matched a candidate, which is also what the postal-only floors
/// report.
/// </param>
public readonly record struct GeoMatch(
    double Latitude,
    double Longitude,
    string? CityName,
    string Hint = CountryHint.None);

/// <summary>
/// Turns typed text into coordinates without calling a vendor. Must never throw: a miss and a
/// failure both return null, and the vendor geocoder is the fallback for both.
/// </summary>
public interface ILocalGeocoder
{
    /// <param name="preferredCountry">
    /// ISO 3166-1 alpha-2 of the country the user says they are in, or null when they have not
    /// said. A preference and never a filter: it breaks ties between equally valid matches and
    /// must not turn a match the caller would otherwise have got into a miss. Matching no
    /// candidate means it is skipped, not that it silences the weaker signals below it. See
    /// <c>SqliteLocalGeocoder</c>.
    /// </param>
    /// <param name="timeZone">
    /// The caller's IANA time zone, used the same way and only when <paramref name="preferredCountry"/>
    /// says nothing <b>or matches no candidate</b>. It settles the ambiguity for a user who has set
    /// nothing at all, which is most of them. See <see cref="CountryHint"/>.
    /// </param>
    GeoMatch? Find(string text, string? preferredCountry = null, string? timeZone = null);
}

/// <summary>Used when no dataset is configured. Every lookup misses, so the vendor serves.</summary>
public sealed class NullLocalGeocoder : ILocalGeocoder
{
    public GeoMatch? Find(string text, string? preferredCountry = null, string? timeZone = null) => null;
}
