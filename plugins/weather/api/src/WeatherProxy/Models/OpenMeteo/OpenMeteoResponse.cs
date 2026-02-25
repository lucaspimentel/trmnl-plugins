using System.Text.Json.Serialization;

namespace WeatherProxy.Models.OpenMeteo;

public record OpenMeteoResponse(
    double Latitude,
    double Longitude,
    string Timezone,
    [property: JsonPropertyName("current")] OpenMeteoCurrent Current,
    [property: JsonPropertyName("hourly")] OpenMeteoHourly Hourly,
    [property: JsonPropertyName("daily")] OpenMeteoDaily Daily
);

public record OpenMeteoCurrent(
    string Time,
    [property: JsonPropertyName("temperature_2m")] double Temperature2m,
    [property: JsonPropertyName("apparent_temperature")] double ApparentTemperature,
    [property: JsonPropertyName("relative_humidity_2m")] int RelativeHumidity2m,
    [property: JsonPropertyName("precipitation")] double Precipitation,
    [property: JsonPropertyName("weather_code")] int WeatherCode,
    [property: JsonPropertyName("wind_speed_10m")] double WindSpeed10m,
    [property: JsonPropertyName("wind_direction_10m")] int WindDirection10m,
    [property: JsonPropertyName("is_day")] int IsDay
);

public record OpenMeteoHourly(
    [property: JsonPropertyName("time")] List<string> Time,
    [property: JsonPropertyName("temperature_2m")] List<double> Temperature2m,
    [property: JsonPropertyName("weather_code")] List<int> WeatherCode,
    [property: JsonPropertyName("precipitation_probability")] List<int?> PrecipitationProbability
);

public record OpenMeteoDaily(
    [property: JsonPropertyName("time")] List<string> Time,
    [property: JsonPropertyName("temperature_2m_max")] List<double> Temperature2mMax,
    [property: JsonPropertyName("temperature_2m_min")] List<double> Temperature2mMin,
    [property: JsonPropertyName("weather_code")] List<int> WeatherCode,
    [property: JsonPropertyName("precipitation_probability_max")] List<int?> PrecipitationProbabilityMax,
    [property: JsonPropertyName("sunrise")] List<string> Sunrise,
    [property: JsonPropertyName("sunset")] List<string> Sunset
);
