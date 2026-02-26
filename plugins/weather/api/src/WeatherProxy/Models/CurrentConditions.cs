using System.Text.Json.Serialization;

namespace WeatherProxy.Models;

public record CurrentConditions(
    string Time,
    [property: JsonPropertyName("temperature_f")] int TemperatureF,
    [property: JsonPropertyName("temperature_c")] int TemperatureC,
    [property: JsonPropertyName("apparent_temperature_f")] int ApparentTemperatureF,
    [property: JsonPropertyName("apparent_temperature_c")] int ApparentTemperatureC,
    [property: JsonPropertyName("relative_humidity")] int RelativeHumidity,
    [property: JsonPropertyName("precipitation_in")] double PrecipitationIn,
    [property: JsonPropertyName("weather_code")] int WeatherCode,
    string Condition,
    [property: JsonPropertyName("icon_class")] string IconClass,
    [property: JsonPropertyName("wind_speed_mph")] int WindSpeedMph,
    [property: JsonPropertyName("wind_direction_deg")] double WindDirectionDeg,
    [property: JsonPropertyName("wind_direction")] string WindDirection,
    [property: JsonPropertyName("is_day")] bool IsDay
);
