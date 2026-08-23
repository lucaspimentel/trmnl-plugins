using TrmnlApi.Models;

namespace TrmnlApi.Services;

/// <summary>
/// Trims a forecast down to the entry counts a single request asked for.
///
/// This runs per request, after the cache. Providers deliberately transform and cache
/// everything upstream returns, because the cache is keyed on
/// (provider, latitude, longitude, metric) and not on hours/days: a response trimmed
/// before caching would become the ceiling for every later request at that location.
/// </summary>
public static class ForecastTrimmer
{
    public static WeatherResponse Trim(WeatherResponse response, int hours, int days)
    {
        if (hours >= response.Hourly.Entries.Count && days >= response.Daily.Entries.Count)
        {
            return response;
        }

        return response with
        {
            Hourly = new HourlyForecast(response.Hourly.Entries.Take(hours).ToList()),
            Daily = new DailyForecast(response.Daily.Entries.Take(days).ToList())
        };
    }
}
