using Microsoft.Extensions.Caching.Memory;
using WeatherProxy.Models;

namespace WeatherProxy.Services;

public class WeatherCache(IMemoryCache cache)
{
    private static readonly MemoryCacheEntryOptions CacheOptions = new MemoryCacheEntryOptions()
        .SetSlidingExpiration(TimeSpan.FromMinutes(5))
        .SetSize(1);

    public bool TryGet(double latitude, double longitude, out WeatherResponse? response)
    {
        return cache.TryGetValue(CacheKey(latitude, longitude), out response);
    }

    public void Set(double latitude, double longitude, WeatherResponse response)
    {
        cache.Set(CacheKey(latitude, longitude), response, CacheOptions);
    }

    private static string CacheKey(double latitude, double longitude) =>
        $"weather:{latitude:F2}:{longitude:F2}";
}
