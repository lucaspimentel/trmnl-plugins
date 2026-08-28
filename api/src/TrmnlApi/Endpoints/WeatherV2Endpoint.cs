using Datadog.Trace;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TrmnlApi.Mappings;
using TrmnlApi.Models;
using TrmnlApi.Observability;
using TrmnlApi.Services;

namespace TrmnlApi.Endpoints;

/// <summary>
/// Takes one free-form <c>place</c> instead of two numeric fields, and returns the place it
/// resolved to alongside the forecast.
/// </summary>
/// <remarks>
/// Every failure a device can see is returned as HTTP 200 with a populated <c>error</c> object.
/// TRMNL counts polling failures and eventually stops refreshing a plugin until someone resets it
/// by hand, and a mistyped place fails on every poll forever, so a status code would turn a typo
/// into a dead plugin. A non-2xx from here means the API itself broke. See docs/place-input.md.
/// <para>
/// Shares the resolver, orchestrator, cache, trimmer and provider chain with v1: the divergence is
/// meant to stay at parameter parsing and response serialization.
/// </para>
/// </remarks>
public class WeatherV2Endpoint
{
    private const int MaxHours = ForecastLimits.MaxHours;
    private const int MaxDays = ForecastLimits.MaxDays;
    private const int DefaultDays = 6;

    /// <summary>Longest echo of a user's input in an error message.</summary>
    private const int MaxQuotedLength = 80;

    /// <summary>How old the stale test scenario claims its forecast is.</summary>
    private static readonly TimeSpan StaleScenarioAge = TimeSpan.FromHours(6);

    private const string TagInputKind = "weather.input_kind";
    private const string TagErrorCode = "weather.error_code";

    public static async Task<IResult> Handle(
        HttpRequest req,
        PlaceResolver placeResolver,
        WeatherForecastOrchestrator orchestrator,
        TimeProvider timeProvider,
        ForecastMetrics metrics,
        ILogger<WeatherV2Endpoint> logger,
        ILogger<ForecastServed> servedLogger,
        CancellationToken cancellationToken)
    {
        var query = req.Query;

        // The request's own span, not one of ours: see RequestSpan. Two failures never reach the
        // orchestrator at all - an unset place and one that resolves to nothing - so this is also
        // the only span guaranteed to exist for every error below.
        var span = RequestSpan.Current;

        var unitsParam = query["units"].FirstOrDefault();
        if (!RequestValidator.IsValidUnits(unitsParam))
        {
            return RequestInvalid(span, RequestValidator.UnitsMessage);
        }
        var metric = unitsParam is "metric";

        if (!RequestValidator.TryParseRangeParam(query["hours"].FirstOrDefault(), 1, MaxHours, MaxHours, out var hours))
        {
            return RequestInvalid(span, $"hours must be an integer between 1 and {MaxHours}.");
        }

        if (!RequestValidator.TryParseRangeParam(query["days"].FirstOrDefault(), 1, MaxDays, DefaultDays, out var days))
        {
            return RequestInvalid(span, $"days must be an integer between 1 and {MaxDays}.");
        }

        var use24Hour = query["time_format"].FirstOrDefault() is "24h";

        // A display preference rather than a data one, but custom field values are unreadable
        // from Liquid, so the response body is the only way one can reach the template.
        var showPlace = query["show_place"].FirstOrDefault() is not "no";
        var requestedProvider = query["provider"].FirstOrDefault();

        var placeParam = query["place"].FirstOrDefault();

        // A sentinel in the place field selects a canned result. It rides in a custom field the
        // plugin already forwards, so stepping through these on a real screen is typing in the
        // plugin's settings rather than editing and re-pushing polling_url. See TestScenarios.
        var scenario = TestScenarios.Parse(placeParam);
        if (scenario is not null)
        {
            span?.SetTag(TestScenarios.SpanTag, scenario.Name);

            var canned = TestScenarioResult(span, logger, scenario);
            if (canned is not null)
            {
                return canned;
            }

            // The rest need a forecast to alter, so they stand in a fixed location and let the
            // ordinary path fetch one, then change a single detail of the result further down.
            placeParam = TestScenarios.Location;
        }

        var input = PlaceInput.Parse(
            placeParam,
            query["latitude"].FirstOrDefault(),
            query["longitude"].FirstOrDefault());

        // The measurement that decides whether the reverse-geocoding work in
        // docs/geographic-telemetry.md is worth building at all. Four values, all bounded.
        span?.SetTag(TagInputKind, input.Kind);

        double latitude;
        double longitude;
        Place? place = null;

        switch (input)
        {
            case PlaceInput.Missing:
                return Error(span, WeatherErrors.PlaceMissing);

            case PlaceInput.Invalid:
                return Error(
                    span,
                    WeatherErrors.PlaceInvalid(
                        Quote(placeParam ?? $"{query["latitude"].FirstOrDefault()}, {query["longitude"].FirstOrDefault()}")));

            case PlaceInput.Coordinates coordinates:
                latitude = coordinates.Latitude;
                longitude = coordinates.Longitude;
                break;

            case PlaceInput.Query typed:
                try
                {
                    place = await placeResolver.ResolveAsync(typed.Text, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return ClientGone(span, logger);
                }
                catch (HttpRequestException ex)
                {
                    // An outage, not a miss. Telling someone their correct input was not found
                    // would have them retype an address that was never the problem.
                    logger.LogError(ex, "Place lookup failed upstream.");
                    return Unavailable(span);
                }

                if (place is null)
                {
                    return Error(span, WeatherErrors.PlaceNotFound(Quote(typed.Text)));
                }

                latitude = place.Latitude;
                longitude = place.Longitude;
                break;

            default:
                throw new InvalidOperationException($"Unhandled place input {input.GetType().Name}.");
        }

        ForecastOutcome outcome;
        try
        {
            outcome = await orchestrator.GetAsync(requestedProvider, latitude, longitude, metric, hours, days, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ClientGone(span, logger);
        }
        catch (ArgumentException)
        {
            return RequestInvalid(span, $"provider '{requestedProvider}' is not a known weather provider.");
        }
        catch (UpstreamUnavailableException ex)
        {
            metrics.RecordUpstreamFailure();
            logger.LogError(
                ex,
                "All weather providers failed for {Latitude},{Longitude}",
                CoarseCoordinate.ToTag(latitude),
                CoarseCoordinate.ToTag(longitude));
            return Unavailable(span);
        }

        var weatherResponse = ForecastTrimmer.Trim(outcome.Response, hours, days);

        if (use24Hour)
        {
            weatherResponse = weatherResponse with
            {
                Hourly = new HourlyForecast(
                    weatherResponse.Hourly.Entries
                        .Select(e => e with { Label = HourLabel.Format(e.Time, use24Hour: true) })
                        .ToList())
            };
        }

        if (scenario?.Kind is TestScenarioKind.FakePrecipitation)
        {
            weatherResponse = TestScenarios.FakePrecipitation(weatherResponse);
        }

        var servedAt = timeProvider.GetUtcNow();

        var cacheStatus = outcome.CacheStatus;
        var fetchedAt = outcome.FetchedAt;
        if (scenario?.Kind is TestScenarioKind.StaleForecast)
        {
            // Backdated rather than actually aged: a stale serve otherwise needs every provider to
            // fail against a cache entry old enough to have fallen out of its fresh window.
            cacheStatus = WeatherForecastOrchestrator.CacheStaleServed;
            fetchedAt = servedAt - StaleScenarioAge;
        }

        weatherResponse = weatherResponse with
        {
            Place = showPlace ? place : null,
            Meta = new Meta(
                Cache: cacheStatus,
                Provider: outcome.WinningProvider,
                RequestedProvider: outcome.RequestedProvider,
                FetchedAt: fetchedAt,
                DataTime: weatherResponse.Current.Time,
                ServedAt: servedAt,
                AgeSeconds: (long)(servedAt - fetchedAt).TotalSeconds,
                TimeFormat: use24Hour ? "24h" : "12h",
                Upstream: outcome.Upstream)
        };

        metrics.RecordServed(cacheStatus, outcome.WinningProvider);
        // Its own category, not this class's: see TrmnlApi.Observability.ForecastServed.
        servedLogger.LogInformation(
            "Served forecast for {Latitude},{Longitude} cache={CacheStatus} provider={Provider} requested={RequestedProvider}",
            CoarseCoordinate.ToTag(latitude),
            CoarseCoordinate.ToTag(longitude),
            cacheStatus,
            outcome.WinningProvider,
            outcome.RequestedProvider);

        return Results.Json(weatherResponse, WeatherEndpoint.JsonOptions);
    }

    private static IResult Error(ISpan? span, ErrorInfo error)
    {
        // Error rate and error tracking read the span, not the status code, so a response that
        // deliberately carries a 200 still has to be counted as the failure it is. Setting this on
        // the entry span is what makes it count natively: without it, every v2 failure would read
        // as a clean success.
        if (span is not null)
        {
            span.Error = true;
            span.SetTag(Tags.ErrorType, error.Code);
            span.SetTag(Tags.ErrorMsg, error.Message);
            // Faceted separately from ErrorType so a dashboard can group on it without parsing.
            span.SetTag(TagErrorCode, error.Code);
        }

        return Results.Json(new ErrorResponse(error), WeatherEndpoint.JsonOptions);
    }

    private static IResult RequestInvalid(ISpan? span, string message) =>
        Error(span, WeatherErrors.RequestInvalid(message));

    private static IResult Unavailable(ISpan? span) =>
        Error(span, WeatherErrors.WeatherUnavailable);

    /// <summary>
    /// The scenarios that need no forecast. Returns null for the two that do, leaving them to the
    /// ordinary path so what reaches the screen is a real response with one detail changed.
    /// </summary>
    private static IResult? TestScenarioResult(ISpan? span, ILogger logger, TestScenario scenario) =>
        scenario.Kind switch
        {
            TestScenarioKind.Error => Error(span, scenario.Error!),
            TestScenarioKind.ClientGone => ClientGone(span, logger),
            TestScenarioKind.UpstreamFailure => Results.Text(WeatherEndpoint.UpstreamFailureMessage, statusCode: 502),
            // Thrown rather than returned, so the response really does come from the handler that
            // serves an unplanned 500 - which is the only part of it worth previewing.
            TestScenarioKind.ServerError => throw new InvalidOperationException("Deliberate failure from the 500 test scenario."),
            _ => null
        };

    // Nobody is left to render anything, so this is the one case that keeps a status code, and the
    // one failure that is not the service's fault. Deliberately not tagged as a span error.
    private static IResult ClientGone(ISpan? span, ILogger logger)
    {
        span?.SetTag(TagErrorCode, "client_cancelled");
        logger.LogInformation("Client cancelled the forecast request.");
        return Results.StatusCode(499);
    }

    private static string Quote(string value)
    {
        var collapsed = value.Trim();
        var clipped = collapsed.Length > MaxQuotedLength
            ? collapsed[..MaxQuotedLength] + "..."
            : collapsed;
        return $"\"{clipped}\"";
    }
}
