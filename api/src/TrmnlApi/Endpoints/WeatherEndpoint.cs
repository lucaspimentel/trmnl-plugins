using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TrmnlApi.Mappings;
using TrmnlApi.Models;
using TrmnlApi.Services;

namespace TrmnlApi.Functions;

public class WeatherEndpoint
{
    private const int MaxHours = 25;
    private const int MaxDays = 14;

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
            return BadRequest("units must be 'imperial' or 'metric'.");
        }
        var metric = unitsParam is "metric";

        if (!RequestValidator.TryParseRangeParam(query["hours"].FirstOrDefault(), 1, MaxHours, out var hours))
        {
            return BadRequest($"hours must be an integer between 1 and {MaxHours}.");
        }

        if (!RequestValidator.TryParseRangeParam(query["days"].FirstOrDefault(), 1, MaxDays, out var days))
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
                latitude.ToString("F1", CultureInfo.InvariantCulture),
                longitude.ToString("F1", CultureInfo.InvariantCulture));
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
                latitude.ToString("F1", CultureInfo.InvariantCulture),
                longitude.ToString("F1", CultureInfo.InvariantCulture));
            return Results.Text("Failed to fetch weather forecast from upstream provider.", statusCode: 502);
        }

        var weatherResponse = outcome.Response;

        if (hours < MaxHours || days < MaxDays)
        {
            weatherResponse = weatherResponse with
            {
                Hourly = new HourlyForecast(weatherResponse.Hourly.Entries.Take(hours).ToList()),
                Daily = new DailyForecast(weatherResponse.Daily.Entries.Take(days).ToList())
            };
        }

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
            weatherResponse = FakePrecipitation(weatherResponse);
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
        logger.LogInformation(
            "Served forecast for {Latitude},{Longitude} cache={CacheStatus} provider={Provider} requested={RequestedProvider}",
            latitude.ToString("F1", CultureInfo.InvariantCulture),
            longitude.ToString("F1", CultureInfo.InvariantCulture),
            outcome.CacheStatus,
            outcome.WinningProvider,
            outcome.RequestedProvider);

        return Results.Json(weatherResponse, JsonOptions);
    }

    private static IResult BadRequest(string message) => Results.Text(message, statusCode: 400);

    private static WeatherResponse FakePrecipitation(WeatherResponse response)
    {
        var hourly = response.Hourly.Entries
            .Select(e => e with { PrecipitationProbability = Random.Shared.Next(0, 100) })
            .ToList();

        var daily = response.Daily.Entries
            .Select(e => e with { PrecipitationProbability = Random.Shared.Next(0, 100) })
            .ToList();

        var last = daily[^1];
        daily[^1] = last with { High = last.Low };

        var secondLast = daily[^2];
        daily[^2] = secondLast with { High = secondLast.Low + 2 };

        return response with
        {
            Hourly = new HourlyForecast(hourly),
            Daily = new DailyForecast(daily)
        };
    }
}
