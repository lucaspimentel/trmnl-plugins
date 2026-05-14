using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TrmnlApi.Models;

namespace TrmnlApi.Services;

public class WeatherCache(IMemoryCache cache, IOptions<WeatherCacheOptions> options, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly WeatherCacheOptions _options = options.Value;

    public CachedForecast? TryGet(string provider, double latitude, double longitude, bool metric)
    {
        if (cache.TryGetValue(CacheKey(provider, latitude, longitude, metric), out CacheEntry? entry) && entry is not null)
        {
            var isFresh = _timeProvider.GetUtcNow() - entry.FetchedAt < _options.FreshTtl;
            return new CachedForecast(entry.Response, entry.FetchedAt, isFresh);
        }

        return null;
    }

    public void Set(string provider, double latitude, double longitude, bool metric, WeatherResponse response)
    {
        var entryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(_options.StaleTtl)
            .SetSize(1);
        cache.Set(CacheKey(provider, latitude, longitude, metric), new CacheEntry(response, _timeProvider.GetUtcNow()), entryOptions);
    }

    private static string CacheKey(string provider, double latitude, double longitude, bool metric) =>
        $"weather:{provider}:{latitude:F2}:{longitude:F2}:{(metric ? "metric" : "imperial")}";

    private sealed record CacheEntry(WeatherResponse Response, DateTimeOffset FetchedAt);
}

public sealed record CachedForecast(WeatherResponse Response, DateTimeOffset FetchedAt, bool IsFresh);
