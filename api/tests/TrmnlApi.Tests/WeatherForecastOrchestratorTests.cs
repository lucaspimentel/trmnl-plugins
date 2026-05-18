using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        new object[] { new TaskCanceledException("provider-side cancel") }
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

    private static (WeatherForecastOrchestrator orchestrator, WeatherCache cache, TestClock clock) Build(
        params StubProvider[] providers)
    {
        var clock = new TestClock();
        var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var cache = new WeatherCache(memoryCache, Options.Create(new WeatherCacheOptions()), clock);
        var resolver = new WeatherProviderResolver(providers);
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

        public Task<WeatherResponse> GetForecastAsync(double latitude, double longitude, bool metric, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (Failure is not null)
            {
                return Task.FromException<WeatherResponse>(Failure);
            }
            return Task.FromResult(Response!);
        }
    }

}
