using Microsoft.Extensions.Caching.Memory;
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

    private static (WeatherCache cache, TestClock clock) Build()
    {
        var clock = new TestClock();
        var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var cache = new WeatherCache(memoryCache, clock);
        return (cache, clock);
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

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
