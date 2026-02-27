using System.Text.Json;
using WeatherProxy.Models.OpenMeteo;

namespace WeatherProxy.Services;

public class OpenMeteoClient(HttpClient httpClient) : IOpenMeteoClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<OpenMeteoResponse> GetForecastAsync(double latitude, double longitude, bool metric = false, CancellationToken cancellationToken = default)
    {
        var tempUnit = metric ? "celsius" : "fahrenheit";
        var windUnit = metric ? "kmh" : "mph";
        var precipUnit = metric ? "mm" : "inch";

        var url = $"https://api.open-meteo.com/v1/forecast" +
                  $"?latitude={latitude}&longitude={longitude}" +
                  $"&current=temperature_2m,apparent_temperature,relative_humidity_2m,precipitation,weather_code,wind_speed_10m,wind_direction_10m,is_day" +
                  $"&hourly=temperature_2m,weather_code,precipitation_probability" +
                  $"&daily=temperature_2m_max,temperature_2m_min,weather_code,precipitation_probability_max,sunrise,sunset" +
                  $"&temperature_unit={tempUnit}&wind_speed_unit={windUnit}&precipitation_unit={precipUnit}" +
                  $"&timezone=auto&forecast_hours=25&forecast_days=6";

        var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await JsonSerializer.DeserializeAsync<OpenMeteoResponse>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            JsonOptions,
            cancellationToken);

        return result ?? throw new InvalidOperationException("Open-Meteo returned an empty response.");
    }
}
