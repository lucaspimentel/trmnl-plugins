using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using WeatherProxy.Models;
using WeatherProxy.Services;

namespace WeatherProxy.Functions;

public class WeatherFunction(
    IOpenMeteoClient openMeteoClient,
    IWeatherTransformer transformer,
    WeatherCache cache,
    ILogger<WeatherFunction> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Function("forecast")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "forecast")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        if (!TryParseCoordinates(req, out var latitude, out var longitude))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("latitude and longitude query parameters are required and must be valid numbers.", cancellationToken);
            return bad;
        }

        if (!cache.TryGet(latitude, longitude, out var weatherResponse) || weatherResponse is null)
        {
            logger.LogInformation("Cache miss for {Latitude},{Longitude} — fetching from Open-Meteo", latitude, longitude);
            var raw = await openMeteoClient.GetForecastAsync(latitude, longitude, cancellationToken);
            weatherResponse = transformer.Transform(raw);
            cache.Set(latitude, longitude, weatherResponse);
        }

        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (query["fake"] is "true" or "1")
        {
            weatherResponse = FakePrecipitation(weatherResponse);
        }

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

        // Last day: identical high/low → zero-width bar, labels outside
        var last = daily[^1];
        daily[^1] = last with { HighF = last.LowF };

        // Second-to-last day: high/low 2° apart → narrow bar, labels outside
        var secondLast = daily[^2];
        daily[^2] = secondLast with { HighF = secondLast.LowF + 2 };

        return response with
        {
            Hourly = new HourlyForecast(hourly),
            Daily = new DailyForecast(daily)
        };
    }

    private static bool TryParseCoordinates(HttpRequestData req, out double latitude, out double longitude)
    {
        latitude = 0;
        longitude = 0;
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        return double.TryParse(query["latitude"], System.Globalization.CultureInfo.InvariantCulture, out latitude)
            && double.TryParse(query["longitude"], System.Globalization.CultureInfo.InvariantCulture, out longitude);
    }
}
