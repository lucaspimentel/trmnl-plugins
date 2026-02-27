using System.Text.Json.Serialization;

namespace WeatherProxy.Models;

public record DailyEntry(
    string Date,
    int High,
    int Low,
    [property: JsonPropertyName("weather_code")] int WeatherCode,
    string Condition,
    [property: JsonPropertyName("icon_class")] string IconClass,
    [property: JsonPropertyName("precipitation_probability")] int PrecipitationProbability,
    string Sunrise,
    string Sunset
);
