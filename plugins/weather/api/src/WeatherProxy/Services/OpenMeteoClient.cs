using System.Text.Json;
using WeatherProxy.Models.OpenMeteo;

namespace WeatherProxy.Services;

public class OpenMeteoClient(HttpClient httpClient) : IOpenMeteoClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<OpenMeteoResponse> GetForecastAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        var url = $"https://api.open-meteo.com/v1/forecast" +
                  $"?latitude={latitude}&longitude={longitude}" +
                  $"&current=temperature_2m,apparent_temperature,relative_humidity_2m,precipitation,weather_code,wind_speed_10m,wind_direction_10m,is_day" +
                  $"&hourly=temperature_2m,weather_code,precipitation_probability" +
                  $"&daily=temperature_2m_max,temperature_2m_min,weather_code,precipitation_probability_max,sunrise,sunset" +
                  $"&temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch" +
                  $"&timezone=auto&forecast_hours=25&forecast_days=5";

        var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await JsonSerializer.DeserializeAsync<OpenMeteoResponse>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            JsonOptions,
            cancellationToken);

        return result ?? throw new InvalidOperationException("Open-Meteo returned an empty response.");
    }
}
