using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly.Timeout;
using TrmnlApi.Models;
using TrmnlApi.Providers;

namespace TrmnlApi.Services;

public sealed record ForecastOutcome(
    WeatherResponse Response,
    string WinningProvider,
    string RequestedProvider,
    string CacheStatus,
    DateTimeOffset FetchedAt,
    Upstream? Upstream);

public sealed class UpstreamUnavailableException(Upstream upstream, Exception? inner)
    : Exception($"All weather providers failed. First failure: {upstream.Status} {upstream.Error}", inner)
{
    public Upstream Upstream { get; } = upstream;
}

public class WeatherForecastOrchestrator(
    WeatherProviderResolver resolver,
    WeatherCache cache,
    TimeProvider timeProvider,
    ILogger<WeatherForecastOrchestrator> logger)
{
    public const string CacheFreshFetch = "fresh_fetch";
    public const string CacheFreshHit = "fresh_hit";
    public const string CacheStaleServed = "stale_served";

    public async Task<ForecastOutcome> GetAsync(
        string? requestedName,
        double latitude,
        double longitude,
        bool metric,
        CancellationToken cancellationToken)
    {
        var chain = resolver.ResolveChain(requestedName);
        if (chain.Count == 0)
        {
            throw new InvalidOperationException("Resolved provider chain is empty.");
        }

        var requestedProvider = chain[0].Name;
        Upstream? firstFailure = null;
        Exception? firstFailureException = null;
        (CachedForecast Forecast, string Provider)? staleFallback = null;

        foreach (var provider in chain)
        {
            var cached = cache.TryGet(provider.Name, latitude, longitude, metric);
            if (cached is { IsFresh: true })
            {
                return new ForecastOutcome(
                    cached.Response,
                    provider.Name,
                    requestedProvider,
                    CacheFreshHit,
                    cached.FetchedAt,
                    firstFailure);
            }

            if (cached is not null)
            {
                staleFallback ??= (cached, provider.Name);
            }

            try
            {
                var response = await provider.GetForecastAsync(latitude, longitude, metric, cancellationToken);
                cache.Set(provider.Name, latitude, longitude, metric, response);
                return new ForecastOutcome(
                    response,
                    provider.Name,
                    requestedProvider,
                    CacheFreshFetch,
                    timeProvider.GetUtcNow(),
                    firstFailure);
            }
            catch (Exception ex) when (IsTransient(ex, cancellationToken))
            {
                logger.LogWarning(ex, "{Provider} fetch failed for {Latitude},{Longitude}", provider.Name, latitude, longitude);
                if (firstFailure is null)
                {
                    firstFailure = BuildUpstreamFromException(ex);
                    firstFailureException = ex;
                }
            }
        }

        if (staleFallback is { } stale)
        {
            logger.LogWarning("All providers failed for {Latitude},{Longitude}; serving stale cache from {Provider}",
                latitude, longitude, stale.Provider);
            return new ForecastOutcome(
                stale.Forecast.Response,
                stale.Provider,
                requestedProvider,
                CacheStaleServed,
                stale.Forecast.FetchedAt,
                firstFailure);
        }

        throw new UpstreamUnavailableException(firstFailure!, firstFailureException);
    }

    private static bool IsTransient(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or JsonException or IOException or TimeoutRejectedException ||
        (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static Upstream BuildUpstreamFromException(Exception ex) => ex switch
    {
        HttpRequestException httpEx => new Upstream(httpEx.StatusCode is null ? null : (int)httpEx.StatusCode, httpEx.Message),
        JsonException jsonEx => new Upstream(200, jsonEx.Message),
        TimeoutRejectedException => new Upstream((int)HttpStatusCode.GatewayTimeout, "upstream timed out after retries"),
        _ => new Upstream(null, ex.Message)
    };
}
