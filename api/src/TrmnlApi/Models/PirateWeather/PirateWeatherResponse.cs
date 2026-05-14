namespace TrmnlApi.Models.PirateWeather;

public record PirateWeatherResponse(
    double Latitude,
    double Longitude,
    string Timezone,
    PirateCurrently Currently,
    PirateHourly Hourly,
    PirateDaily Daily
);

public record PirateCurrently(
    long Time,
    string Summary,
    string Icon,
    double Temperature,
    double ApparentTemperature,
    double Humidity,
    double PrecipIntensity,
    double PrecipProbability,
    double WindSpeed,
    double WindBearing
);

public record PirateHourly(List<PirateHourlyEntry> Data);

public record PirateHourlyEntry(
    long Time,
    string Icon,
    double Temperature,
    double PrecipProbability
);

public record PirateDaily(List<PirateDailyEntry> Data);

public record PirateDailyEntry(
    long Time,
    string Icon,
    long SunriseTime,
    long SunsetTime,
    double TemperatureHigh,
    double TemperatureLow,
    double PrecipProbability
);
