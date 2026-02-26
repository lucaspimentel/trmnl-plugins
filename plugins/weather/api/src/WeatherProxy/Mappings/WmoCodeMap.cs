namespace WeatherProxy.Mappings;

public static class WmoCodeMap
{
    public static string GetCondition(int code) => code switch
    {
        0 => "Clear",
        1 => "Mainly Clear",
        2 => "Partly Cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        >= 51 and <= 55 => "Drizzle",
        >= 61 and <= 65 => "Rain",
        66 or 67 => "Freezing Rain",
        >= 71 and <= 77 => "Snow",
        >= 80 and <= 82 => "Showers",
        85 or 86 => "Snow Showers",
        >= 95 and <= 99 => "Thunderstorm",
        _ => "Unknown"
    };

    public static string GetIconClass(int code, bool isDay) => code switch
    {
        0 => isDay ? "wi-day-sunny" : "wi-night-clear",
        1 => isDay ? "wi-day-sunny-overcast" : "wi-night-partly-cloudy",
        2 => isDay ? "wi-day-cloudy" : "wi-night-cloudy",
        3 => "wi-wmo4680-3",
        45 => "wi-wmo4680-45",
        48 => "wi-wmo4680-48",
        >= 51 and <= 57 => $"wi-wmo4680-{code}",
        >= 61 and <= 67 => $"wi-wmo4680-{code}",
        >= 71 and <= 77 => $"wi-wmo4680-{code}",
        >= 80 and <= 86 => $"wi-wmo4680-{code}",
        >= 95 and <= 99 => $"wi-wmo4680-{code}",
        _ => "wi-na"
    };

    public static string GetWindDirection(double degrees) => (int)degrees switch
    {
        >= 338 or < 23 => "N",
        >= 23 and < 68 => "NE",
        >= 68 and < 113 => "E",
        >= 113 and < 158 => "SE",
        >= 158 and < 203 => "S",
        >= 203 and < 248 => "SW",
        >= 248 and < 293 => "W",
        _ => "NW"
    };
}
