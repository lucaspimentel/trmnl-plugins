using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
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

        var ok = req.CreateResponse(HttpStatusCode.OK);
        ok.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await ok.WriteStringAsync(JsonSerializer.Serialize(weatherResponse, JsonOptions), cancellationToken);
        return ok;
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
