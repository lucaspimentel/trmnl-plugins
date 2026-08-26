using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TrmnlApi.Models;
using TrmnlApi.Services;

namespace TrmnlApi.Tests;

public class WeatherCacheTests
{
    private static WeatherResponse SampleResponse() =>
        new(
            new CurrentConditions("", 0, 0, 0, 0, "", "", 0, 0, "", true),
            new HourlyForecast([]),
            new DailyForecast([]));

    private static (WeatherCache cache, TestClock clock) Build(WeatherCacheOptions? options = null)
    {
        var clock = new TestClock();
        var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10, Clock = clock });
        var cache = new WeatherCache(memoryCache, Options.Create(options ?? new WeatherCacheOptions()), clock);
        return (cache, clock);
    }

    /// <summary>
    /// Builds a cache whose memory limit comes from the same options the app uses, so a test can
    /// exercise the real relationship between SizeLimit and the number of live entries.
    /// </summary>
    private static (WeatherCache cache, TestClock clock) BuildSizedFromOptions(WeatherCacheOptions options)
    {
        var clock = new TestClock();
        var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.SizeLimit, Clock = clock });
        var cache = new WeatherCache(memoryCache, Options.Create(options), clock);
        return (cache, clock);
    }

    [Fact]
    public void Entries_stay_resident_for_the_whole_stale_window_at_realistic_load()
    {
        // The stale path is only useful if the entry is still there when providers fail, and
        // entries live for StaleTtl, so the cache has to hold every coordinate seen in that window.
        // Production sees ~223 distinct coordinates per hour over a 2h stale window; the default
        // SizeLimit must cover that with room to grow, or the oldest entries are evicted and the
        // fallback they exist for is gone. Sizing to one hour's worth would silently drop half.
        var options = new WeatherCacheOptions();
        var (cache, _) = BuildSizedFromOptions(options);

        const int coordinatesPerHour = 223;
        var liveEntries = coordinatesPerHour * (int)Math.Ceiling(options.StaleTtl.TotalHours);

        for (var i = 0; i < liveEntries; i++)
        {
            cache.Set("open-meteo", 40.0 + (i * 0.01), -71.0 - (i * 0.01), metric: false, SampleResponse());
        }

        // The first coordinate cached is the one most at risk, and the one an outage would need.
        Assert.NotNull(cache.TryGet("open-meteo", 40.0, -71.0, metric: false));

        var resident = 0;
        for (var i = 0; i < liveEntries; i++)
        {
            if (cache.TryGet("open-meteo", 40.0 + (i * 0.01), -71.0 - (i * 0.01), metric: false) is not null)
            {
                resident++;
            }
        }

        Assert.Equal(liveEntries, resident);
    }

    [Fact]
    public void Undersized_cache_stops_admitting_new_coordinates()
    {
        // Documents the failure this guards against. A full MemoryCache does not evict to make
        // room synchronously, it refuses the write: compaction happens later on a background
        // thread. So once the limit is reached, a coordinate that is not already cached never gets
        // in while older entries hold the slots. That user's requests always miss, always hit
        // upstream, and have no stale entry to fall back on when providers fail.
        var (cache, _) = BuildSizedFromOptions(new WeatherCacheOptions { SizeLimit = 200 });

        for (var i = 0; i < 446; i++)
        {
            cache.Set("open-meteo", 40.0 + (i * 0.01), -71.0 - (i * 0.01), metric: false, SampleResponse());
        }

        // A coordinate written well after the cache filled up was never admitted.
        Assert.Null(cache.TryGet("open-meteo", 40.0 + (445 * 0.01), -71.0 - (445 * 0.01), metric: false));
    }

    [Fact]
    public void TryGet_CultureChangedBetweenSetAndGet_StillHits()
    {
        // The key is built with string interpolation, which would otherwise use the ambient
        // culture: under a comma-decimal culture "42.36" formats as "42,36", so an entry written
        // under one culture would be invisible to a read under another.
        var (cache, _) = Build();
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            cache.Set("open-meteo", 42.36, -71.06, metric: false, SampleResponse());

            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var entry = cache.TryGet("open-meteo", 42.36, -71.06, metric: false);

            Assert.NotNull(entry);
            Assert.True(entry.IsFresh);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void TryGet_NoEntry_ReturnsNull()
    {
        var (cache, _) = Build();

        var entry = cache.TryGet("open-meteo", 42.0, -71.0, metric: false);

        Assert.Null(entry);
    }

    [Fact]
    public void TryGet_WithinFreshTtl_ReturnsFresh()
    {
        var (cache, clock) = Build();
        var setAt = clock.GetUtcNow();

        cache.Set("open-meteo", 42.0, -71.0, metric: false, SampleResponse());
        clock.Advance(TimeSpan.FromMinutes(4));

        var entry = cache.TryGet("open-meteo", 42.0, -71.0, metric: false);

        Assert.NotNull(entry);
        Assert.True(entry.IsFresh);
        Assert.Equal(setAt, entry.FetchedAt);
    }

    [Fact]
    public void TryGet_AfterFreshTtl_ReturnsStale()
    {
        var (cache, clock) = Build();

        cache.Set("open-meteo", 42.0, -71.0, metric: false, SampleResponse());
        clock.Advance(TimeSpan.FromMinutes(10));

        var entry = cache.TryGet("open-meteo", 42.0, -71.0, metric: false);

        Assert.NotNull(entry);
        Assert.False(entry.IsFresh);
    }

    [Fact]
    public void TryGet_AfterStaleTtl_ReturnsNull()
    {
        var (cache, clock) = Build();

        cache.Set("open-meteo", 42.0, -71.0, metric: false, SampleResponse());
        clock.Advance(TimeSpan.FromHours(3)); // beyond the 2h StaleTtl ceiling

        Assert.Null(cache.TryGet("open-meteo", 42.0, -71.0, metric: false));
    }

    [Fact]
    public void Set_DifferentUnits_KeysSeparately()
    {
        var (cache, _) = Build();

        cache.Set("open-meteo", 42.0, -71.0, metric: false, SampleResponse());

        Assert.NotNull(cache.TryGet("open-meteo", 42.0, -71.0, metric: false));
        Assert.Null(cache.TryGet("open-meteo", 42.0, -71.0, metric: true));
    }

    [Fact]
    public void Set_DifferentProviders_KeySeparately()
    {
        var (cache, _) = Build();

        cache.Set("open-meteo", 42.0, -71.0, metric: false, SampleResponse());

        Assert.NotNull(cache.TryGet("open-meteo", 42.0, -71.0, metric: false));
        Assert.Null(cache.TryGet("pirate-weather", 42.0, -71.0, metric: false));
    }

}
