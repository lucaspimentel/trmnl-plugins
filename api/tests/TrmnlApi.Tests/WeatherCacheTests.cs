using Microsoft.Extensions.Caching.Memory;
using TrmnlApi.Models;
using TrmnlApi.Services;

namespace TrmnlApi.Tests;

public class WeatherCacheTests
{
    private static WeatherResponse SampleResponse() =>
        new(
            new CurrentConditions("", 0, 0, 0, 0, 0, "", "", 0, 0, "", true),
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
    public void TryGet_NoEntry_ReturnsFalse()
    {
        var (cache, _) = Build();

        var found = cache.TryGet(42.0, -71.0, metric: false, out var response, out var isFresh);

        Assert.False(found);
        Assert.Null(response);
        Assert.False(isFresh);
    }

    [Fact]
    public void TryGet_WithinFreshTtl_ReturnsFresh()
    {
        var (cache, clock) = Build();

        cache.Set(42.0, -71.0, metric: false, SampleResponse());
        clock.Advance(TimeSpan.FromMinutes(4));

        var found = cache.TryGet(42.0, -71.0, metric: false, out var response, out var isFresh);

        Assert.True(found);
        Assert.NotNull(response);
        Assert.True(isFresh);
    }

    [Fact]
    public void TryGet_AfterFreshTtl_ReturnsStale()
    {
        var (cache, clock) = Build();

        cache.Set(42.0, -71.0, metric: false, SampleResponse());
        clock.Advance(TimeSpan.FromMinutes(10));

        var found = cache.TryGet(42.0, -71.0, metric: false, out var response, out var isFresh);

        Assert.True(found);
        Assert.NotNull(response);
        Assert.False(isFresh);
    }

    [Fact]
    public void Set_DifferentUnits_KeysSeparately()
    {
        var (cache, _) = Build();

        cache.Set(42.0, -71.0, metric: false, SampleResponse());

        Assert.True(cache.TryGet(42.0, -71.0, metric: false, out _, out _));
        Assert.False(cache.TryGet(42.0, -71.0, metric: true, out _, out _));
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
