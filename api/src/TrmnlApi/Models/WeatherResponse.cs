namespace TrmnlApi.Models;

public record WeatherResponse(
    CurrentConditions Current,
    HourlyForecast Hourly,
    DailyForecast Daily,
    Meta? Meta = null
);

public record HourlyForecast(List<HourlyEntry> Entries);

public record DailyForecast(List<DailyEntry> Entries);
