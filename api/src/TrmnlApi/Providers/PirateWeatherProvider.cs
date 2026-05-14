using TrmnlApi.Mappings;
using TrmnlApi.Models;
using TrmnlApi.Models.PirateWeather;
using TrmnlApi.Services;

namespace TrmnlApi.Providers;

public class PirateWeatherProvider(IPirateWeatherClient client) : IWeatherProvider
{
    public string Name => "pirate-weather";

    public async Task<WeatherResponse> GetForecastAsync(double latitude, double longitude, bool metric, CancellationToken cancellationToken = default)
    {
        var raw = await client.GetForecastAsync(latitude, longitude, metric, cancellationToken);
        return Transform(raw);
    }

    public static WeatherResponse Transform(PirateWeatherResponse raw, int hours = 25, int days = 6)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(raw.Timezone);
        var daily = TransformDaily(raw.Daily, tz, days);
        var current = TransformCurrent(raw.Currently, tz, daily);
        var hourly = TransformHourly(raw.Hourly, raw.Currently.Time, tz, daily, hours);
        return new WeatherResponse(current, hourly, daily);
    }

    internal static CurrentConditions TransformCurrent(PirateCurrently c, TimeZoneInfo tz, DailyForecast daily)
    {
        var time = FormatLocalTime(c.Time, tz);
        var isDay = !IsNightHour(time, daily);
        return new CurrentConditions(
            Time: time,
            Temperature: (int)Math.Round(c.Temperature),
            ApparentTemperature: (int)Math.Round(c.ApparentTemperature),
            RelativeHumidity: (int)Math.Round(c.Humidity * 100),
            Precipitation: c.PrecipIntensity,
            Condition: PirateIconMap.GetCondition(c.Icon),
            IconClass: PirateIconMap.GetIconClass(c.Icon),
            WindSpeed: (int)Math.Round(c.WindSpeed),
            WindDirectionDeg: c.WindBearing,
            WindDirection: WmoCodeMap.GetWindDirection(c.WindBearing),
            IsDay: isDay
        );
    }

    internal static HourlyForecast TransformHourly(PirateHourly hourly, long currentTime, TimeZoneInfo tz, DailyForecast daily, int hours)
    {
        var currentHour = currentTime - (currentTime % 3600);
        var startIndex = hourly.Data.FindIndex(h => h.Time == currentHour);
        if (startIndex < 0) startIndex = 0;
        var count = Math.Min(hours, hourly.Data.Count - startIndex);

        var entries = new List<HourlyEntry>();
        for (int i = startIndex; i < startIndex + count; i++)
        {
            var pe = hourly.Data[i];
            var time = FormatLocalTime(pe.Time, tz);
            var isDay = !IsNightHour(time, daily);
            var loopIndex = i - startIndex;
            var label = loopIndex == 0 ? "Now" : FormatHourLabel(time);

            entries.Add(new HourlyEntry(
                Time: time,
                Label: label,
                Temperature: (int)Math.Round(pe.Temperature),
                PrecipitationProbability: (int)Math.Round(pe.PrecipProbability * 100),
                IconClass: PirateIconMap.GetIconClass(pe.Icon),
                IsDay: isDay
            ));
        }

        return new HourlyForecast(entries);
    }

    internal static DailyForecast TransformDaily(PirateDaily daily, TimeZoneInfo tz, int days)
    {
        var count = Math.Min(days, daily.Data.Count);
        var entries = new List<DailyEntry>();
        for (int i = 0; i < count; i++)
        {
            var d = daily.Data[i];
            // Pirate sometimes returns -night suffixed icons for the daily summary
            // (especially for "today" late in the evening). Force the day variant
            // so the daily column always renders with a daytime icon.
            var dayIcon = d.Icon.Replace("-night", "-day");
            entries.Add(new DailyEntry(
                Date: FormatLocalDate(d.Time, tz),
                High: (int)Math.Round(d.TemperatureHigh),
                Low: (int)Math.Round(d.TemperatureLow),
                Condition: PirateIconMap.GetCondition(dayIcon),
                IconClass: PirateIconMap.GetIconClass(dayIcon),
                PrecipitationProbability: (int)Math.Round(d.PrecipProbability * 100),
                Sunrise: FormatLocalTime(d.SunriseTime, tz),
                Sunset: FormatLocalTime(d.SunsetTime, tz)
            ));
        }

        return new DailyForecast(entries);
    }

    private static bool IsNightHour(string time, DailyForecast daily)
    {
        var date = time[..10];
        foreach (var entry in daily.Entries)
        {
            if (entry.Date == date)
            {
                return string.Compare(time, entry.Sunrise, StringComparison.Ordinal) < 0
                    || string.Compare(time, entry.Sunset, StringComparison.Ordinal) >= 0;
            }
        }
        return false;
    }

    private static string FormatLocalTime(long unixSeconds, TimeZoneInfo tz)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime, tz);
        return local.ToString("yyyy-MM-dd'T'HH:mm");
    }

    private static string FormatLocalDate(long unixSeconds, TimeZoneInfo tz)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime, tz);
        return local.ToString("yyyy-MM-dd");
    }

    private static string FormatHourLabel(string isoTime)
    {
        var h = int.Parse(isoTime[11..13]);
        return h switch
        {
            0 => "12am",
            < 12 => $"{h}am",
            12 => "12pm",
            _ => $"{h - 12}pm"
        };
    }
}
