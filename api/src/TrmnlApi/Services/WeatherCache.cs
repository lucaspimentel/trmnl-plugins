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

    public bool TryGet(double latitude, double longitude, bool metric, out WeatherResponse? response, out bool isFresh)
    {
        if (cache.TryGetValue(CacheKey(latitude, longitude, metric), out CacheEntry? entry) && entry is not null)
        {
            response = entry.Response;
            isFresh = _timeProvider.GetUtcNow() - entry.CachedAt < FreshTtl;
            return true;
        }

        response = null;
        isFresh = false;
        return false;
    }

    public void Set(double latitude, double longitude, bool metric, WeatherResponse response)
    {
        cache.Set(CacheKey(latitude, longitude, metric), new CacheEntry(response, _timeProvider.GetUtcNow()), CacheOptions);
    }

    private static string CacheKey(double latitude, double longitude, bool metric) =>
        $"weather:{latitude:F2}:{longitude:F2}:{(metric ? "metric" : "imperial")}";

    private sealed record CacheEntry(WeatherResponse Response, DateTimeOffset CachedAt);
}
