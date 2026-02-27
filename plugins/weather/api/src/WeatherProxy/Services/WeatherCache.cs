using Microsoft.Extensions.Caching.Memory;
using WeatherProxy.Models;

namespace WeatherProxy.Services;

public class WeatherCache(IMemoryCache cache)
{
    private static readonly MemoryCacheEntryOptions CacheOptions = new MemoryCacheEntryOptions()
        .SetSlidingExpiration(TimeSpan.FromMinutes(5))
        .SetSize(1);

    public bool TryGet(double latitude, double longitude, bool metric, out WeatherResponse? response)
    {
        return cache.TryGetValue(CacheKey(latitude, longitude, metric), out response);
    }

    public void Set(double latitude, double longitude, bool metric, WeatherResponse response)
    {
        cache.Set(CacheKey(latitude, longitude, metric), response, CacheOptions);
    }

    private static string CacheKey(double latitude, double longitude, bool metric) =>
        $"weather:{latitude:F2}:{longitude:F2}:{(metric ? "metric" : "imperial")}";
}
