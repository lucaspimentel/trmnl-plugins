using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TrmnlApi.Models;

namespace TrmnlApi.Services;

/// <summary>
/// Turns a typed place into coordinates, memoizing both outcomes.
/// </summary>
/// <remarks>
/// The endpoint this serves is anonymous and unthrottled, and free text means unbounded key
/// cardinality, so the memo is as much a spending limit as a latency win: without it, repeating one
/// junk string is enough to keep buying geocoding quota. See the quota and abuse section of
/// docs/place-input.md.
/// </remarks>
public sealed class PlaceResolver
{
    /// <summary>
    /// Longest input that will be looked up. Comfortably above the longest real query - the Welsh
    /// village with the 58-letter name fits with its country - and short enough that a padded
    /// string never reaches the geocoder or the memo.
    /// </summary>
    internal const int MaxQueryLength = 120;

    private readonly IOpenMeteoGeocodingClient _client;
    private readonly IMemoryCache _cache;
    private readonly PlaceCacheOptions _options;
    private readonly ILogger<PlaceResolver> _logger;

    /// <param name="cache">
    /// Must be an instance of this resolver's own, never the one <c>AddMemoryCache</c> registers:
    /// see <see cref="PlaceCacheOptions.SizeLimit"/>.
    /// </param>
    public PlaceResolver(
        IOpenMeteoGeocodingClient client,
        IMemoryCache cache,
        IOptions<PlaceCacheOptions> options,
        ILogger<PlaceResolver> logger)
    {
        _client = client;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a place name or postal code. Throws <see cref="HttpRequestException"/> when the
    /// geocoder is unreachable, which the caller must not report as a miss.
    /// </summary>
    public async Task<Place?> ResolveAsync(string query, CancellationToken cancellationToken = default)
    {
        // Over-length input is turned away before it can reach either the geocoder or the memo.
        // Letting it into the memo would be its own problem: junk keys evict real ones.
        if (query.Length > MaxQueryLength)
        {
            _logger.LogWarning("Rejected a place lookup of {Length} characters without calling the geocoder.", query.Length);
            return null;
        }

        // Casefolded, so "boston" and "Boston" are one entry. The query arrives already trimmed
        // and whitespace-collapsed from PlaceInput.
        var key = $"place:{query.ToLowerInvariant()}";

        if (_cache.TryGetValue(key, out CacheEntry? entry) && entry is not null)
        {
            return entry.Place;
        }

        var result = await _client.SearchAsync(query, cancellationToken);

        var place = result is null
            ? null
            : new Place(
                Name: result.Name,
                Admin1: result.Admin1,
                Country: result.Country,
                CountryCode: result.CountryCode,
                // Snap to the same 0.01 degree grid the forecast cache keys on, before the
                // coordinates are used or shown. Resolving after the cache lookup would fragment
                // it by input form; resolving before means everyone who typed "Boston" converges
                // on one entry. WeatherForecastOrchestrator snaps again, idempotently.
                Latitude: Math.Round(result.Latitude, 2, MidpointRounding.AwayFromZero),
                Longitude: Math.Round(result.Longitude, 2, MidpointRounding.AwayFromZero));

        var entryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(place is null ? _options.MissTtl : _options.HitTtl)
            .SetSize(1);
        _cache.Set(key, new CacheEntry(place), entryOptions);

        return place;
    }

    // A record rather than the Place itself, so that a cached miss is a present entry holding null
    // instead of being indistinguishable from having never looked.
    private sealed record CacheEntry(Place? Place);
}
