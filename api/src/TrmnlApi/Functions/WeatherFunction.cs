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
    IOpenMeteoClient openMeteoClient,
    IWeatherTransformer transformer,
    WeatherCache cache,
    TimeProvider timeProvider,
    ILogger<WeatherFunction> logger)
{
    private const int MaxHours = 25;
    private const int MaxDays = 6;

    private const string CacheFreshFetch = "fresh_fetch";
    private const string CacheFreshHit = "fresh_hit";
    private const string CacheStaleServed = "stale_served";

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

        var cached = cache.TryGet(latitude, longitude, metric);

        WeatherResponse weatherResponse;
        string cacheStatus;
        DateTimeOffset fetchedAt;
        Upstream? upstream;

        if (cached is { IsFresh: true })
        {
            weatherResponse = cached.Response;
            cacheStatus = CacheFreshHit;
            fetchedAt = cached.FetchedAt;
            upstream = null;
        }
        else
        {
            logger.LogInformation("Cache {Status} for {Latitude},{Longitude},{Units} — fetching from Open-Meteo",
                cached is null ? "miss" : "stale", latitude, longitude, metric ? "metric" : "imperial");

            try
            {
                var raw = await openMeteoClient.GetForecastAsync(latitude, longitude, metric, cancellationToken);
                weatherResponse = transformer.Transform(raw);
                cache.Set(latitude, longitude, metric, weatherResponse);
                cacheStatus = CacheFreshFetch;
                fetchedAt = timeProvider.GetUtcNow();
                upstream = new Upstream(200, null);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                if (cached is not null)
                {
                    logger.LogWarning(ex, "Open-Meteo fetch failed for {Latitude},{Longitude}; serving stale cache", latitude, longitude);
                    weatherResponse = cached.Response;
                    cacheStatus = CacheStaleServed;
                    fetchedAt = cached.FetchedAt;
                    upstream = BuildUpstreamFromException(ex);
                }
                else
                {
                    logger.LogError(ex, "Failed to fetch Open-Meteo forecast for {Latitude},{Longitude}", latitude, longitude);
                    var error = req.CreateResponse(HttpStatusCode.BadGateway);
                    await error.WriteStringAsync("Failed to fetch weather forecast from upstream provider.", cancellationToken);
                    return error;
                }
            }
        }

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
            Cache: cacheStatus,
            FetchedAt: fetchedAt,
            DataTime: weatherResponse.Current.Time,
            ServedAt: servedAt,
            AgeSeconds: (long)(servedAt - fetchedAt).TotalSeconds,
            Upstream: upstream);

        weatherResponse = weatherResponse with { Meta = meta };

        var ok = req.CreateResponse(HttpStatusCode.OK);
        ok.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await ok.WriteStringAsync(JsonSerializer.Serialize(weatherResponse, JsonOptions), cancellationToken);
        return ok;
    }

    private static Upstream BuildUpstreamFromException(Exception ex) => ex switch
    {
        HttpRequestException httpEx => new Upstream(httpEx.StatusCode is null ? null : (int)httpEx.StatusCode, httpEx.Message),
        JsonException jsonEx => new Upstream(200, jsonEx.Message),
        _ => new Upstream(null, ex.Message)
    };

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
