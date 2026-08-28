using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TrmnlApi.Mappings;
using TrmnlApi.Models;
using TrmnlApi.Observability;
using TrmnlApi.Services;

namespace TrmnlApi.Endpoints;

public class WeatherEndpoint
{
    private const int MaxHours = ForecastLimits.MaxHours;
    internal const int MaxDays = ForecastLimits.MaxDays;

    // Days a caller gets when the parameter is omitted. Deliberately below MaxDays:
    // most layouts show about a week, and fetching the full 14 for every unparameterized
    // caller would widen the response for no benefit.
    private const int DefaultDays = 6;

    /// <summary>
    /// v1's body when every provider failed. Shared so the v2 test scenario that reproduces this
    /// response cannot drift from the real one.
    /// </summary>
    internal const string UpstreamFailureMessage = "Failed to fetch weather forecast from upstream provider.";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<IResult> Handle(
        HttpRequest req,
        WeatherForecastOrchestrator orchestrator,
        TimeProvider timeProvider,
        ForecastMetrics metrics,
        ILogger<WeatherEndpoint> logger,
        ILogger<ForecastServed> servedLogger,
        CancellationToken cancellationToken)
    {
        var query = req.Query;

        if (!RequestValidator.TryParseCoordinates(query["latitude"].FirstOrDefault(), query["longitude"].FirstOrDefault(), out var latitude, out var longitude))
        {
            return BadRequest("latitude and longitude query parameters are required and must be valid numbers.");
        }

        if (!RequestValidator.AreCoordinatesInRange(latitude, longitude))
        {
            return BadRequest("latitude must be between -90 and 90, longitude must be between -180 and 180.");
        }

        var unitsParam = query["units"].FirstOrDefault();
        if (!RequestValidator.IsValidUnits(unitsParam))
        {
            return BadRequest(RequestValidator.UnitsMessage);
        }
        var metric = unitsParam is "metric";

        if (!RequestValidator.TryParseRangeParam(query["hours"].FirstOrDefault(), 1, MaxHours, MaxHours, out var hours))
        {
            return BadRequest($"hours must be an integer between 1 and {MaxHours}.");
        }

        if (!RequestValidator.TryParseRangeParam(query["days"].FirstOrDefault(), 1, MaxDays, DefaultDays, out var days))
        {
            return BadRequest($"days must be an integer between 1 and {MaxDays}.");
        }
        var use24Hour = query["time_format"].FirstOrDefault() is "24h";


        var requestedProvider = query["provider"].FirstOrDefault();

        ForecastOutcome outcome;
        try
        {
            outcome = await orchestrator.GetAsync(requestedProvider, latitude, longitude, metric, hours, days, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "Client cancelled forecast request for {Latitude},{Longitude}",
                CoarseCoordinate.ToTag(latitude),
                CoarseCoordinate.ToTag(longitude));
            return Results.StatusCode(499);
        }
        catch (ArgumentException)
        {
            return BadRequest($"provider '{requestedProvider}' is not a known weather provider.");
        }
        catch (UpstreamUnavailableException ex)
        {
            metrics.RecordUpstreamFailure();
            logger.LogError(
                ex,
                "All weather providers failed for {Latitude},{Longitude}",
                CoarseCoordinate.ToTag(latitude),
                CoarseCoordinate.ToTag(longitude));
            return Results.Text(UpstreamFailureMessage, statusCode: 502);
        }

        var weatherResponse = outcome.Response;

        weatherResponse = ForecastTrimmer.Trim(weatherResponse, hours, days);

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


        if (query["fake"].FirstOrDefault() is "true" or "1")
        {
            weatherResponse = TestScenarios.FakePrecipitation(weatherResponse);
        }

        var servedAt = timeProvider.GetUtcNow();
        var meta = new Meta(
            Cache: outcome.CacheStatus,
            Provider: outcome.WinningProvider,
            RequestedProvider: outcome.RequestedProvider,
            FetchedAt: outcome.FetchedAt,
            DataTime: weatherResponse.Current.Time,
            ServedAt: servedAt,
            AgeSeconds: (long)(servedAt - outcome.FetchedAt).TotalSeconds,
            TimeFormat: use24Hour ? "24h" : "12h",
            Upstream: outcome.Upstream);

        weatherResponse = weatherResponse with { Meta = meta };

        metrics.RecordServed(outcome.CacheStatus, outcome.WinningProvider);
        // Its own category, not this class's: see TrmnlApi.Observability.ForecastServed.
        servedLogger.LogInformation(
            "Served forecast for {Latitude},{Longitude} cache={CacheStatus} provider={Provider} requested={RequestedProvider}",
            CoarseCoordinate.ToTag(latitude),
            CoarseCoordinate.ToTag(longitude),
            outcome.CacheStatus,
            outcome.WinningProvider,
            outcome.RequestedProvider);

        return Results.Json(weatherResponse, JsonOptions);
    }

    private static IResult BadRequest(string message) => Results.Text(message, statusCode: 400);
}
