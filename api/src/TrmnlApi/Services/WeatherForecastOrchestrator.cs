using System.Globalization;
using System.Net;
using System.Text.Json;
using Datadog.Trace;
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
    public const string CacheAllFailed = "all_failed";

    public async Task<ForecastOutcome> GetAsync(
        string? requestedName,
        double latitude,
        double longitude,
        bool metric,
        int hours,
        int days,
        CancellationToken cancellationToken)
    {
        using var scope = Tracer.Instance.StartActive("weather.forecast");
        var span = scope.Span;
        span.SetTag(Tags.SpanKind, SpanKinds.Internal);
        span.SetTag("weather.coord", string.Create(CultureInfo.InvariantCulture, $"{latitude:F1},{longitude:F1}"));
        span.SetTag("weather.units", metric ? "metric" : "imperial");
        span.SetTag("weather.hours", hours.ToString(CultureInfo.InvariantCulture));
        span.SetTag("weather.days", days.ToString(CultureInfo.InvariantCulture));

        var chain = resolver.ResolveChain(requestedName);
        if (chain.Count == 0)
        {
            throw new InvalidOperationException("Resolved provider chain is empty.");
        }

        var requestedProvider = chain[0].Name;
        span.ResourceName = requestedProvider;
        span.SetTag("weather.requested_provider", requestedProvider);

        Upstream? firstFailure = null;
        Exception? firstFailureException = null;
        (CachedForecast Forecast, string Provider)? staleFallback = null;

        foreach (var provider in chain)
        {
            var cached = cache.TryGet(provider.Name, latitude, longitude, metric);
            if (cached is { IsFresh: true })
            {
                return TagOutcome(span, new ForecastOutcome(
                    cached.Response,
                    provider.Name,
                    requestedProvider,
                    CacheFreshHit,
                    cached.FetchedAt,
                    firstFailure));
            }

            if (cached is not null)
            {
                staleFallback ??= (cached, provider.Name);
            }

            try
            {
                var response = await provider.GetForecastAsync(latitude, longitude, metric, cancellationToken);
                cache.Set(provider.Name, latitude, longitude, metric, response);
                return TagOutcome(span, new ForecastOutcome(
                    response,
                    provider.Name,
                    requestedProvider,
                    CacheFreshFetch,
                    timeProvider.GetUtcNow(),
                    firstFailure));
            }
            catch (Exception ex) when (IsTransient(ex, cancellationToken))
            {
                logger.LogWarning(
                    ex,
                    "{Provider} fetch failed for {Latitude},{Longitude}",
                    provider.Name,
                    latitude.ToString("F1", CultureInfo.InvariantCulture),
                    longitude.ToString("F1", CultureInfo.InvariantCulture));
                if (firstFailure is null)
                {
                    firstFailure = BuildUpstreamFromException(ex);
                    firstFailureException = ex;
                }
            }
        }

        if (staleFallback is { } stale)
        {
            logger.LogWarning(
                "All providers failed for {Latitude},{Longitude}; serving stale cache from {Provider}",
                latitude.ToString("F1", CultureInfo.InvariantCulture),
                longitude.ToString("F1", CultureInfo.InvariantCulture),
                stale.Provider);
            return TagOutcome(span, new ForecastOutcome(
                stale.Forecast.Response,
                stale.Provider,
                requestedProvider,
                CacheStaleServed,
                stale.Forecast.FetchedAt,
                firstFailure));
        }

        var failure = new UpstreamUnavailableException(firstFailure!, firstFailureException);
        span.SetException(failure);
        span.SetTag("weather.cache_status", CacheAllFailed);
        TagFirstFailure(span, firstFailure!);
        throw failure;
    }

    private ForecastOutcome TagOutcome(ISpan span, ForecastOutcome outcome)
    {
        span.SetTag("weather.winning_provider", outcome.WinningProvider);
        span.SetTag("weather.cache_status", outcome.CacheStatus);
        span.SetTag("weather.fallback", outcome.WinningProvider != outcome.RequestedProvider ? "true" : "false");
        span.SetTag("weather.age_seconds", (timeProvider.GetUtcNow() - outcome.FetchedAt).TotalSeconds.ToString("F0", CultureInfo.InvariantCulture));
        if (outcome.Upstream is { } u)
        {
            TagFirstFailure(span, u);
        }
        return outcome;
    }

    private static void TagFirstFailure(ISpan span, Upstream upstream)
    {
        span.SetTag("weather.first_failure.status", upstream.Status?.ToString(CultureInfo.InvariantCulture));
        span.SetTag("weather.first_failure.error", upstream.Error);
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
