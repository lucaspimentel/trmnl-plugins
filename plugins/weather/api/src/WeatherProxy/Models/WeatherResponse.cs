namespace WeatherProxy.Models;

public record WeatherResponse(
    CurrentConditions Current,
    HourlyForecast Hourly,
    DailyForecast Daily
);

public record HourlyForecast(List<HourlyEntry> Entries);

public record DailyForecast(List<DailyEntry> Entries);
