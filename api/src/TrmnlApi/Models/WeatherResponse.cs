namespace TrmnlApi.Models;

public record WeatherResponse(
    CurrentConditions Current,
    HourlyForecast Hourly,
    DailyForecast Daily,
    bool Stale = false
);

public record HourlyForecast(List<HourlyEntry> Entries);

public record DailyForecast(List<DailyEntry> Entries);
