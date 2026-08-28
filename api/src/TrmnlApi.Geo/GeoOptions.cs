namespace TrmnlApi.Geo;

public class GeoOptions
{
    /// <summary>
    /// Path to the bundled <c>geo.sqlite</c>. Blank, or a path that does not exist, disables local
    /// geocoding and the place lookup entirely: the vendor geocoder serves every query and no
    /// place block is built. That is the behaviour before this dataset existed, which is what
    /// makes a missing artifact a degradation rather than an outage.
    /// </summary>
    public string DatabasePath { get; set; } = "geo.sqlite";

    /// <summary>
    /// How long a single lookup may take before it gives up and returns a blank place. A label is
    /// a nicety; a forecast is the product, and nothing here may delay one.
    /// </summary>
    public TimeSpan TimeBudget { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How far the nearest city may be and still be used as the on-screen label, in kilometres.
    /// Tight, because a name is a claim about where the forecast is.
    /// </summary>
    public double CityRadiusKm { get; set; } = 60;

    /// <summary>
    /// How far outside every subdivision polygon a point may sit and still take that
    /// subdivision's country, in kilometres. Looser than <see cref="CityRadiusKm"/> on purpose:
    /// coastlines are simplified, so a point a few kilometres offshore is still in that country,
    /// and a country code is a much weaker claim than a city name.
    /// </summary>
    public double CountryRadiusKm { get; set; } = 200;

    /// <summary>
    /// Longest input the local geocoder will look up. Matches the vendor resolver's own limit so
    /// the two paths turn away the same strings.
    /// </summary>
    public int MaxQueryLength { get; set; } = 120;

    /// <summary>How long a reverse-geocoded place is memoized. Places do not move.</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Most reverse lookups held at once.</summary>
    /// <remarks>
    /// This memo belongs to its own <c>MemoryCache</c> and must never share the forecast cache's
    /// budget, for the same reason <c>PlaceCacheOptions.SizeLimit</c> gives: a lookup driven by
    /// caller input that can evict forecasts is a way to empty the forecast cache. The key is a
    /// packed 0.01-degree grid cell, so unlike the free-text place memo its cardinality is bounded
    /// by geography rather than by imagination.
    /// </remarks>
    public int CacheSizeLimit { get; set; } = 20000;
}
