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

        var unitsParam = query["units"].FirstOrDefault();
        if (!RequestValidator.IsValidUnits(unitsParam))
        {
            return RequestInvalid("units must be 'imperial' or 'metric'.");
        }
        var metric = unitsParam is "metric";

        if (!RequestValidator.TryParseRangeParam(query["hours"].FirstOrDefault(), 1, MaxHours, MaxHours, out var hours))
        {
            return RequestInvalid($"hours must be an integer between 1 and {MaxHours}.");
        }

        if (!RequestValidator.TryParseRangeParam(query["days"].FirstOrDefault(), 1, MaxDays, DefaultDays, out var days))
        {
            return RequestInvalid($"days must be an integer between 1 and {MaxDays}.");
        }

        var use24Hour = query["time_format"].FirstOrDefault() is "24h";
        var requestedProvider = query["provider"].FirstOrDefault();

        var placeParam = query["place"].FirstOrDefault();
        var input = PlaceInput.Parse(
            placeParam,
            query["latitude"].FirstOrDefault(),
            query["longitude"].FirstOrDefault());

        double latitude;
        double longitude;
        Place? place = null;

        switch (input)
        {
            case PlaceInput.Missing:
                return Error(
                    ErrorCodes.PlaceMissing,
                    "No location is set.",
                    "Open this plugin's settings and enter a city, postal code, or coordinates.");

            case PlaceInput.Invalid:
                return Error(
                    ErrorCodes.PlaceInvalid,
                    $"{Quote(placeParam ?? $"{query["latitude"].FirstOrDefault()}, {query["longitude"].FirstOrDefault()}")} is not a location.",
                    "If you pasted coordinates, check the order: latitude first, then longitude.");

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
                    return ClientGone(logger);
                }
                catch (HttpRequestException ex)
                {
                    // An outage, not a miss. Telling someone their correct input was not found
                    // would have them retype an address that was never the problem.
                    logger.LogError(ex, "Place lookup failed upstream.");
                    return Unavailable();
                }

                if (place is null)
                {
                    return Error(
                        ErrorCodes.PlaceNotFound,
                        $"No place matches {Quote(typed.Text)}.",
                        "Try adding a state or country, as in Portland, ME.");
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
            return ClientGone(logger);
        }
        catch (ArgumentException)
        {
            return RequestInvalid($"provider '{requestedProvider}' is not a known weather provider.");
        }
        catch (UpstreamUnavailableException ex)
        {
            metrics.RecordUpstreamFailure();
            logger.LogError(
                ex,
                "All weather providers failed for {Latitude},{Longitude}",
                CoarseCoordinate.ToTag(latitude),
                CoarseCoordinate.ToTag(longitude));
            return Unavailable();
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

        var servedAt = timeProvider.GetUtcNow();
        weatherResponse = weatherResponse with
        {
            Place = place,
            Meta = new Meta(
                Cache: outcome.CacheStatus,
                Provider: outcome.WinningProvider,
                RequestedProvider: outcome.RequestedProvider,
                FetchedAt: outcome.FetchedAt,
                DataTime: weatherResponse.Current.Time,
                ServedAt: servedAt,
                AgeSeconds: (long)(servedAt - outcome.FetchedAt).TotalSeconds,
                TimeFormat: use24Hour ? "24h" : "12h",
                Upstream: outcome.Upstream)
        };

        metrics.RecordServed(outcome.CacheStatus, outcome.WinningProvider);
        // Its own category, not this class's: see TrmnlApi.Observability.ForecastServed.
        servedLogger.LogInformation(
            "Served forecast for {Latitude},{Longitude} cache={CacheStatus} provider={Provider} requested={RequestedProvider}",
            CoarseCoordinate.ToTag(latitude),
            CoarseCoordinate.ToTag(longitude),
            outcome.CacheStatus,
            outcome.WinningProvider,
            outcome.RequestedProvider);

        return Results.Json(weatherResponse, WeatherEndpoint.JsonOptions);
    }

    private static IResult Error(string code, string message, string hint) =>
        Results.Json(new ErrorResponse(new ErrorInfo(code, message, hint)), WeatherEndpoint.JsonOptions);

    private static IResult RequestInvalid(string message) =>
        Error(ErrorCodes.RequestInvalid, message, "This is a plugin configuration problem, not something the screen's settings can fix.");

    private static IResult Unavailable() =>
        Error(
            ErrorCodes.WeatherUnavailable,
            "Weather is temporarily unavailable.",
            "This usually clears on its own by the next refresh.");

    // Nobody is left to render anything, so this is the one case that keeps a status code.
    private static IResult ClientGone(ILogger logger)
    {
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
