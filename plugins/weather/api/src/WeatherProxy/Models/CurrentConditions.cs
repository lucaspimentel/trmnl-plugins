using System.Text.Json.Serialization;

namespace WeatherProxy.Models;

public record CurrentConditions(
    string Time,
    int Temperature,
    [property: JsonPropertyName("apparent_temperature")] int ApparentTemperature,
    [property: JsonPropertyName("relative_humidity")] int RelativeHumidity,
    double Precipitation,
    [property: JsonPropertyName("weather_code")] int WeatherCode,
    string Condition,
    [property: JsonPropertyName("icon_class")] string IconClass,
    [property: JsonPropertyName("wind_speed")] int WindSpeed,
    [property: JsonPropertyName("wind_direction_deg")] double WindDirectionDeg,
    [property: JsonPropertyName("wind_direction")] string WindDirection,
    [property: JsonPropertyName("is_day")] bool IsDay
);
