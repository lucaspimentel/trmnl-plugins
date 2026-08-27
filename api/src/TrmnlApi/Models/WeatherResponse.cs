namespace TrmnlApi.Models;

/// <param name="Place">
/// Where the forecast is for. Populated by v2 when the caller named a place; null everywhere else,
/// which keeps v1's response byte-identical because the serializer is configured to drop nulls.
/// </param>
public record WeatherResponse(
    CurrentConditions Current,
    HourlyForecast Hourly,
    DailyForecast Daily,
    Meta? Meta = null,
    Place? Place = null
);

public record HourlyForecast(List<HourlyEntry> Entries);

public record DailyForecast(List<DailyEntry> Entries);
