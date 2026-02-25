using System.Text.Json.Serialization;

namespace WeatherProxy.Models;

public record HourlyEntry(
    string Time,
    string Label,
    [property: JsonPropertyName("temperature_f")] int TemperatureF,
    [property: JsonPropertyName("precipitation_probability")] int PrecipitationProbability,
    [property: JsonPropertyName("weather_code")] int WeatherCode,
    [property: JsonPropertyName("icon_class")] string IconClass,
    [property: JsonPropertyName("is_day")] bool IsDay
);
