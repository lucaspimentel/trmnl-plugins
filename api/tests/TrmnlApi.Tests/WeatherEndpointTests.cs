using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TrmnlApi.Endpoints;
using TrmnlApi.Models;
using TrmnlApi.Observability;
using TrmnlApi.Providers;
using TrmnlApi.Services;

namespace TrmnlApi.Tests;

public class WeatherEndpointTests
{
    private const string Primary = "pirate-weather";
    private const string Secondary = "open-meteo";

    [Fact]
    public async Task Handle_BothProvidersDown_StaleCacheStillAlive_ServesStaleWithoutError()
    {
        var (execute, cache, clock, metrics) = Build(
            new StubProvider(Primary) { Failure = new HttpRequestException("boom") },
            new StubProvider(Secondary) { Failure = new HttpRequestException("also down") });
        cache.Set(Primary, 42.0, -71.0, false, MakeResponse("stale-primary"));
        clock.Advance(TimeSpan.FromMinutes(30)); // stale, but inside the 2h StaleTtl

        var (status, body) = await execute();

        Assert.Equal(200, status);
        Assert.Contains("\"cache\":\"stale_served\"", body, StringComparison.Ordinal);
        Assert.Contains("\"provider\":\"pirate-weather\"", body, StringComparison.Ordinal);
        Assert.Contains("\"upstream\"", body, StringComparison.Ordinal);
        Assert.Equal(0, metrics.Snapshot().UpstreamFailures);
        Assert.Equal(1, metrics.Snapshot().StaleServed);
    }

    [Fact]
    public async Task Handle_BothProvidersDown_StaleCacheExpired_Returns502WithPlainTextBody()
    {
        var (execute, cache, clock, metrics) = Build(
            new StubProvider(Primary) { Failure = new HttpRequestException("boom") },
            new StubProvider(Secondary) { Failure = new HttpRequestException("also down") });
        cache.Set(Primary, 42.0, -71.0, false, MakeResponse("stale-primary"));
        clock.Advance(TimeSpan.FromHours(3)); // past StaleTtl, so nothing is left to serve

        var (status, body) = await execute();

        Assert.Equal(502, status);
        Assert.Equal("Failed to fetch weather forecast from upstream provider.", body);
        Assert.Equal(1, metrics.Snapshot().UpstreamFailures);
        Assert.Equal(0, metrics.Snapshot().Served);
    }

    [Fact]
    public async Task Handle_BothProvidersDown_NothingEverCached_Returns502WithPlainTextBody()
    {
        var (execute, _, _, metrics) = Build(
            new StubProvider(Primary) { Failure = new HttpRequestException("boom") },
            new StubProvider(Secondary) { Failure = new HttpRequestException("also down") });

        var (status, body) = await execute();

        Assert.Equal(502, status);
        Assert.Equal("Failed to fetch weather forecast from upstream provider.", body);
        Assert.Equal(1, metrics.Snapshot().UpstreamFailures);
    }

    [Fact]
    public async Task Handle_Success_EmitsNoPlaceKey()
    {
        // v1 is frozen for the forks that copied it. Nullable fields added for v2 must stay
        // invisible here, which is what DefaultIgnoreCondition.WhenWritingNull buys.
        var (execute, _, _, _) = Build(new StubProvider(Primary) { Response = MakeResponse("live") });

        var (status, body) = await execute();

        Assert.Equal(200, status);
        Assert.DoesNotContain("\"place\"", body, StringComparison.Ordinal);
    }

    private static (Func<Task<(int Status, string Body)>> Execute, WeatherCache Cache, TestClock Clock, ForecastMetrics Metrics) Build(
        params StubProvider[] providers)
    {
        var clock = new TestClock();
        var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10, Clock = clock });
        var cache = new WeatherCache(memoryCache, Options.Create(new WeatherCacheOptions()), clock);
        var resolver = new WeatherProviderResolver(providers, providers.Select(p => p.Name).ToList());
        var orchestrator = new WeatherForecastOrchestrator(
            resolver, cache, clock, NullLogger<WeatherForecastOrchestrator>.Instance);
        var metrics = new ForecastMetrics(clock);

        // IResult.ExecuteAsync resolves ILoggerFactory off the request services.
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();

        async Task<(int, string)> Execute()
        {
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.QueryString = new QueryString("?latitude=42.0&longitude=-71.0");
            var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            var result = await WeatherEndpoint.Handle(
                context.Request,
                orchestrator,
                clock,
                metrics,
                NullLogger<WeatherEndpoint>.Instance,
                NullLogger<ForecastServed>.Instance,
                CancellationToken.None);

            await result.ExecuteAsync(context);
            return (context.Response.StatusCode, Encoding.UTF8.GetString(responseBody.ToArray()));
        }

        return (Execute, cache, clock, metrics);
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

        public Task<WeatherResponse> GetForecastAsync(double latitude, double longitude, bool metric, CancellationToken cancellationToken = default) =>
            Failure is not null
                ? Task.FromException<WeatherResponse>(Failure)
                : Task.FromResult(Response!);
    }
}
