using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;
using TrmnlApi.Models;
using TrmnlApi.Providers;
using TrmnlApi.Services;

namespace TrmnlApi.Tests;

public class WeatherForecastOrchestratorTests
{
    private const string Primary = "pirate-weather";
    private const string Secondary = "open-meteo";

    [Fact]
    public async Task GetAsync_FirstProviderFreshCache_ReturnsCachedWithoutCallingUpstream()
    {
        var first = new StubProvider(Primary) { Response = MakeResponse("first") };
        var second = new StubProvider(Secondary) { Response = MakeResponse("second") };
        var (orchestrator, cache, clock) = Build(first, second);
        cache.Set(Primary, 1, 2, false, MakeResponse("cached-primary"));
        clock.Advance(TimeSpan.FromMinutes(2));

        var outcome = await orchestrator.GetAsync(Primary, 1, 2, false, 24, 5, CancellationToken.None);

        Assert.Equal("cached-primary", outcome.Response.Current.Condition);
        Assert.Equal(Primary, outcome.WinningProvider);
        Assert.Equal(Primary, outcome.RequestedProvider);
        Assert.Equal(WeatherForecastOrchestrator.CacheFreshHit, outcome.CacheStatus);
        Assert.Null(outcome.Upstream);
        Assert.Equal(0, first.CallCount);
        Assert.Equal(0, second.CallCount);
    }

    [Fact]
    public async Task GetAsync_FirstProviderSucceeds_ReturnsItsResponse()
    {
        var first = new StubProvider(Primary) { Response = MakeResponse("first-fetch") };
        var second = new StubProvider(Secondary) { Response = MakeResponse("second-fetch") };
        var (orchestrator, _, _) = Build(first, second);

        var outcome = await orchestrator.GetAsync(Primary, 1, 2, false, 24, 5, CancellationToken.None);

        Assert.Equal("first-fetch", outcome.Response.Current.Condition);
        Assert.Equal(Primary, outcome.WinningProvider);
        Assert.Equal(WeatherForecastOrchestrator.CacheFreshFetch, outcome.CacheStatus);
        Assert.Null(outcome.Upstream);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(0, second.CallCount);
    }

    public static IEnumerable<object[]> TransientExceptions => new[]
    {
        new object[] { new HttpRequestException("upstream is down", inner: null, statusCode: HttpStatusCode.ServiceUnavailable) },
        new object[] { new JsonException("bad payload") },
        new object[] { new IOException("connection reset") },
        new object[] { new TimeoutRejectedException("timed out") },
        new object[] { new TaskCanceledException("provider-side cancel") },
        new object[] { new BrokenCircuitException("circuit open") }
    };

    [Theory]
    [MemberData(nameof(TransientExceptions))]
    public async Task GetAsync_FirstProviderTransientFailure_FallsBackToSecondary(Exception failure)
    {
        var first = new StubProvider(Primary) { Failure = failure };
        var second = new StubProvider(Secondary) { Response = MakeResponse("fallback-fetch") };
        var (orchestrator, _, _) = Build(first, second);

        var outcome = await orchestrator.GetAsync(Primary, 1, 2, false, 24, 5, CancellationToken.None);

        Assert.Equal("fallback-fetch", outcome.Response.Current.Condition);
        Assert.Equal(Secondary, outcome.WinningProvider);
        Assert.Equal(Primary, outcome.RequestedProvider);
        Assert.Equal(WeatherForecastOrchestrator.CacheFreshFetch, outcome.CacheStatus);
        Assert.NotNull(outcome.Upstream);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(1, second.CallCount);
    }

    [Fact]
    public async Task GetAsync_FirstFails_SecondHasFreshCache_ReturnsFreshHitFromSecond()
    {
        var first = new StubProvider(Primary) { Failure = new HttpRequestException("boom") };
        var second = new StubProvider(Secondary) { Response = MakeResponse("never-called") };
        var (orchestrator, cache, clock) = Build(first, second);
        cache.Set(Secondary, 1, 2, false, MakeResponse("cached-secondary"));
        clock.Advance(TimeSpan.FromMinutes(2));

        var outcome = await orchestrator.GetAsync(Primary, 1, 2, false, 24, 5, CancellationToken.None);

        Assert.Equal("cached-secondary", outcome.Response.Current.Condition);
        Assert.Equal(Secondary, outcome.WinningProvider);
        Assert.Equal(WeatherForecastOrchestrator.CacheFreshHit, outcome.CacheStatus);
        Assert.NotNull(outcome.Upstream);
        Assert.Equal("boom", outcome.Upstream!.Error);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(0, second.CallCount);
    }

    [Fact]
    public async Task GetAsync_AllFail_FallsBackToStaleCacheFromRequestedProvider()
    {
        var first = new StubProvider(Primary) { Failure = new HttpRequestException("boom") };
        var second = new StubProvider(Secondary) { Failure = new HttpRequestException("also down") };
        var (orchestrator, cache, clock) = Build(first, second);
        cache.Set(Primary, 1, 2, false, MakeResponse("stale-primary"));
        cache.Set(Secondary, 1, 2, false, MakeResponse("stale-secondary"));
        clock.Advance(TimeSpan.FromMinutes(30)); // beyond FreshTtl (10m default)

        var outcome = await orchestrator.GetAsync(Primary, 1, 2, false, 24, 5, CancellationToken.None);

        Assert.Equal("stale-primary", outcome.Response.Current.Condition);
        Assert.Equal(Primary, outcome.WinningProvider);
        Assert.Equal(WeatherForecastOrchestrator.CacheStaleServed, outcome.CacheStatus);
        Assert.NotNull(outcome.Upstream);
        Assert.Equal("boom", outcome.Upstream!.Error);
    }

    [Fact]
    public async Task GetAsync_AllFail_ServesFreshestStaleEntry_NotFirstFound()
    {
        var first = new StubProvider(Primary) { Failure = new HttpRequestException("boom") };
        var second = new StubProvider(Secondary) { Failure = new HttpRequestException("also down") };
        var (orchestrator, cache, clock) = Build(first, second);
        cache.Set(Primary, 1, 2, false, MakeResponse("stale-primary"));       // older
        clock.Advance(TimeSpan.FromMinutes(5));
        cache.Set(Secondary, 1, 2, false, MakeResponse("stale-secondary"));   // newer
        clock.Advance(TimeSpan.FromMinutes(30)); // both > FreshTtl (10m), both < StaleTtl (3h)

        var outcome = await orchestrator.GetAsync(Primary, 1, 2, false, 24, 5, CancellationToken.None);

        Assert.Equal("stale-secondary", outcome.Response.Current.Condition);
        Assert.Equal(Secondary, outcome.WinningProvider);
        Assert.Equal(WeatherForecastOrchestrator.CacheStaleServed, outcome.CacheStatus);
    }

    [Fact]
    public async Task GetAsync_AllFail_EveryStaleEntryPastStaleTtl_Throws()
    {
        var first = new StubProvider(Primary) { Failure = new HttpRequestException("boom") };
        var second = new StubProvider(Secondary) { Failure = new HttpRequestException("also down") };
        var (orchestrator, cache, clock) = Build(first, second);
        cache.Set(Primary, 1, 2, false, MakeResponse("stale-primary"));
        cache.Set(Secondary, 1, 2, false, MakeResponse("stale-secondary"));
        clock.Advance(TimeSpan.FromHours(3)); // beyond StaleTtl (2h default), so both entries are gone

        var ex = await Assert.ThrowsAsync<UpstreamUnavailableException>(
            () => orchestrator.GetAsync(Primary, 1, 2, false, 24, 5, CancellationToken.None));
        Assert.Equal("boom", ex.Upstream.Error);
    }

    [Fact]
    public async Task GetAsync_AllFail_NoStaleCacheAnywhere_Throws()
    {
        var first = new StubProvider(Primary) { Failure = new HttpRequestException("boom") };
        var second = new StubProvider(Secondary) { Failure = new HttpRequestException("also down") };
        var (orchestrator, _, _) = Build(first, second);

        var ex = await Assert.ThrowsAsync<UpstreamUnavailableException>(
            () => orchestrator.GetAsync(Primary, 1, 2, false, 24, 5, CancellationToken.None));
        Assert.Equal("boom", ex.Upstream.Error);
    }

    [Fact]
    public async Task GetAsync_NonTransientException_BubblesUpWithoutFallback()
    {
        var first = new StubProvider(Primary) { Failure = new InvalidOperationException("config missing") };
        var second = new StubProvider(Secondary) { Response = MakeResponse("never") };
        var (orchestrator, _, _) = Build(first, second);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.GetAsync(Primary, 1, 2, false, 24, 5, CancellationToken.None));
        Assert.Equal(1, first.CallCount);
        Assert.Equal(0, second.CallCount);
    }

    [Fact]
    public async Task GetAsync_ClientCancellation_DoesNotTriggerFallback()
    {
        var first = new StubProvider(Primary) { Failure = new TaskCanceledException("client cancelled") };
        var second = new StubProvider(Secondary) { Response = MakeResponse("never") };
        var (orchestrator, _, _) = Build(first, second);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => orchestrator.GetAsync(Primary, 1, 2, false, 24, 5, cts.Token));
        Assert.Equal(1, first.CallCount);
        Assert.Equal(0, second.CallCount);
    }

    [Fact]
    public async Task GetAsync_CoordinatesInSameGridCell_ShareOneCacheEntry()
    {
        var first = new StubProvider(Primary) { Response = MakeResponse("never-called") };
        var (orchestrator, cache, clock) = Build(first);
        cache.Set(Primary, 42.3649, -71.0612, false, MakeResponse("cached-cell"));
        clock.Advance(TimeSpan.FromMinutes(2));

        // Differs from the primed entry only past the second decimal, so it lands in the same cell.
        var outcome = await orchestrator.GetAsync(Primary, 42.3601, -71.0648, false, 24, 5, CancellationToken.None);

        Assert.Equal("cached-cell", outcome.Response.Current.Condition);
        Assert.Equal(WeatherForecastOrchestrator.CacheFreshHit, outcome.CacheStatus);
        Assert.Equal(0, first.CallCount);
    }

    [Theory]
    [InlineData(42.3649, -71.0648, 42.36, -71.06)]  // rounds down
    [InlineData(42.3651, -71.0651, 42.37, -71.07)]  // rounds up
    [InlineData(42.365, -71.065, 42.37, -71.07)]    // midpoint, away from zero in both signs
    [InlineData(42.36, -71.06, 42.36, -71.06)]      // already at two decimals
    public async Task GetAsync_PassesGridAlignedCoordinatesToProvider(
        double latitude, double longitude, double expectedLatitude, double expectedLongitude)
    {
        var first = new StubProvider(Primary) { Response = MakeResponse("fetched") };
        var (orchestrator, _, _) = Build(first);

        await orchestrator.GetAsync(Primary, latitude, longitude, false, 24, 5, CancellationToken.None);

        Assert.Equal(1, first.CallCount);
        Assert.Equal(expectedLatitude, first.LastLatitude);
        Assert.Equal(expectedLongitude, first.LastLongitude);
    }

    private static (WeatherForecastOrchestrator orchestrator, WeatherCache cache, TestClock clock) Build(
        params StubProvider[] providers)
    {
        var clock = new TestClock();
        var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10, Clock = clock });
        var cache = new WeatherCache(memoryCache, Options.Create(new WeatherCacheOptions()), clock);
        var resolver = new WeatherProviderResolver(providers, providers.Select(p => p.Name).ToList());
        var orchestrator = new WeatherForecastOrchestrator(
            resolver, cache, clock, NullLogger<WeatherForecastOrchestrator>.Instance);
        return (orchestrator, cache, clock);
    }

    private static WeatherResponse MakeResponse(string marker) => new(
        Current: new CurrentConditions("", 0, 0, 0, 0, marker, "", 0, 0, "", true),
        Hourly: new HourlyForecast([]),
        Daily: new DailyForecast([]));

    private sealed class StubProvider(string name) : IWeatherProvider
    {
        public string Name { get; } = name;
        public WeatherResponse? Response { get; set; }
        public Exception? Failure { get; set; }
        public int CallCount { get; private set; }
        public double LastLatitude { get; private set; }
        public double LastLongitude { get; private set; }

        public Task<WeatherResponse> GetForecastAsync(double latitude, double longitude, bool metric, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastLatitude = latitude;
            LastLongitude = longitude;
            if (Failure is not null)
            {
                return Task.FromException<WeatherResponse>(Failure);
            }
            return Task.FromResult(Response!);
        }
    }

}
