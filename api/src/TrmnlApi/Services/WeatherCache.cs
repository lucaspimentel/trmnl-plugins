using Microsoft.Extensions.Caching.Memory;
using TrmnlApi.Models;

namespace TrmnlApi.Services;

public class WeatherCache(IMemoryCache cache, TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan FreshTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StaleTtl = TimeSpan.FromHours(1);

    private static readonly MemoryCacheEntryOptions CacheOptions = new MemoryCacheEntryOptions()
        .SetAbsoluteExpiration(StaleTtl)
        .SetSize(1);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public CachedForecast? TryGet(double latitude, double longitude, bool metric)
    {
        if (cache.TryGetValue(CacheKey(latitude, longitude, metric), out CacheEntry? entry) && entry is not null)
        {
            var isFresh = _timeProvider.GetUtcNow() - entry.FetchedAt < FreshTtl;
            return new CachedForecast(entry.Response, entry.FetchedAt, isFresh);
        }

        return null;
    }

    public void Set(double latitude, double longitude, bool metric, WeatherResponse response)
    {
        cache.Set(CacheKey(latitude, longitude, metric), new CacheEntry(response, _timeProvider.GetUtcNow()), CacheOptions);
    }

    private static string CacheKey(double latitude, double longitude, bool metric) =>
        $"weather:{latitude:F2}:{longitude:F2}:{(metric ? "metric" : "imperial")}";

    private sealed record CacheEntry(WeatherResponse Response, DateTimeOffset FetchedAt);
}

public sealed record CachedForecast(WeatherResponse Response, DateTimeOffset FetchedAt, bool IsFresh);
