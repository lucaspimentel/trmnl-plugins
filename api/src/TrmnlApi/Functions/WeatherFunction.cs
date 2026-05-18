using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using TrmnlApi.Models;
using TrmnlApi.Services;

namespace TrmnlApi.Functions;

public class WeatherFunction(
    WeatherForecastOrchestrator orchestrator,
    TimeProvider timeProvider,
    ILogger<WeatherFunction> logger)
{
    private const int MaxHours = 25;
    private const int MaxDays = 6;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Function("forecast")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/forecast")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);

        if (!RequestValidator.TryParseCoordinates(query["latitude"], query["longitude"], out var latitude, out var longitude))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("latitude and longitude query parameters are required and must be valid numbers.", cancellationToken);
            return bad;
        }

        var unitsParam = query["units"];
        if (!RequestValidator.IsValidUnits(unitsParam))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync($"units must be 'imperial' or 'metric'.", cancellationToken);
            return bad;
        }
        var metric = unitsParam is "metric";

        if (!RequestValidator.TryParseRangeParam(query["hours"], 1, MaxHours, out var hours))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync($"hours must be an integer between 1 and {MaxHours}.", cancellationToken);
            return bad;
        }

        if (!RequestValidator.TryParseRangeParam(query["days"], 1, MaxDays, out var days))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync($"days must be an integer between 1 and {MaxDays}.", cancellationToken);
            return bad;
        }

        var requestedProvider = query["provider"];

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
            return req.CreateResponse((HttpStatusCode)499);
        }
        catch (ArgumentException)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync($"provider '{requestedProvider}' is not a known weather provider.", cancellationToken);
            return bad;
        }
        catch (UpstreamUnavailableException ex)
        {
            logger.LogError(
                ex,
                "All weather providers failed for {Latitude},{Longitude}",
                latitude.ToString("F1", CultureInfo.InvariantCulture),
                longitude.ToString("F1", CultureInfo.InvariantCulture));
            var error = req.CreateResponse(HttpStatusCode.BadGateway);
            await error.WriteStringAsync("Failed to fetch weather forecast from upstream provider.", cancellationToken);
            return error;
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

        if (query["fake"] is "true" or "1")
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
            Upstream: outcome.Upstream);

        weatherResponse = weatherResponse with { Meta = meta };

        var ok = req.CreateResponse(HttpStatusCode.OK);
        ok.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await ok.WriteStringAsync(JsonSerializer.Serialize(weatherResponse, JsonOptions), cancellationToken);
        return ok;
    }

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
