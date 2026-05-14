namespace TrmnlApi.Mappings;

public static class PirateIconMap
{
    public static string GetCondition(string icon) => icon switch
    {
        "clear-day" or "clear-night" => "Clear",
        "partly-cloudy-day" or "partly-cloudy-night" => "Partly Cloudy",
        "cloudy" => "Cloudy",
        "rain" => "Rain",
        "snow" => "Snow",
        "sleet" => "Sleet",
        "wind" => "Windy",
        "fog" => "Fog",
        _ => "Unknown"
    };

    public static string GetIconClass(string icon) => icon switch
    {
        "clear-day" => "wi-day-sunny",
        "clear-night" => "wi-night-clear",
        "partly-cloudy-day" => "wi-day-cloudy",
        "partly-cloudy-night" => "wi-night-partly-cloudy",
        "cloudy" => "wi-wmo4680-3",
        "rain" => "wi-wmo4680-63",
        "snow" => "wi-wmo4680-71",
        "sleet" => "wi-wmo4680-67",
        "fog" => "wi-wmo4680-45",
        "wind" => "wi-strong-wind",
        _ => "wi-na"
    };
}
