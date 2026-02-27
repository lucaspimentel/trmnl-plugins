using WeatherProxy.Mappings;
using WeatherProxy.Models;
using WeatherProxy.Models.OpenMeteo;

namespace WeatherProxy.Services;

public class WeatherTransformer : IWeatherTransformer
{
    public WeatherResponse Transform(OpenMeteoResponse raw, int hours = 25, int days = 6)
    {
        var current = TransformCurrent(raw.Current);
        var hourly = TransformHourly(raw.Hourly, raw.Current.Time, raw.Daily, hours);
        var daily = TransformDaily(raw.Daily, days);

        return new WeatherResponse(current, hourly, daily);
    }

    internal static CurrentConditions TransformCurrent(OpenMeteoCurrent c)
    {
        var isDay = c.IsDay == 1;
        return new CurrentConditions(
            Time: c.Time,
            Temperature: (int)Math.Round(c.Temperature2m),
            ApparentTemperature: (int)Math.Round(c.ApparentTemperature),
            RelativeHumidity: c.RelativeHumidity2m,
            Precipitation: c.Precipitation,
            WeatherCode: c.WeatherCode,
            Condition: WmoCodeMap.GetCondition(c.WeatherCode),
            IconClass: WmoCodeMap.GetIconClass(c.WeatherCode, isDay),
            WindSpeed: (int)Math.Round(c.WindSpeed10m),
            WindDirectionDeg: c.WindDirection10m,
            WindDirection: WmoCodeMap.GetWindDirection(c.WindDirection10m),
            IsDay: isDay
        );
    }

    internal static HourlyForecast TransformHourly(OpenMeteoHourly hourly, string currentTime, OpenMeteoDaily daily, int hours)
    {
        var currentHour = currentTime[..13]; // "yyyy-MM-ddTHH"
        var startIndex = hourly.Time.FindIndex(t => t[..13] == currentHour);
        if (startIndex < 0) startIndex = 0;

        var count = Math.Min(hours, hourly.Time.Count - startIndex);
        var entries = new List<HourlyEntry>();
        for (int i = startIndex; i < startIndex + count; i++)
        {
            var time = hourly.Time[i];
            var isDay = IsNightHour(time, daily) == false;
            var wc = hourly.WeatherCode[i];
            var loopIndex = i - startIndex;

            var label = loopIndex == 0 ? "Now" : FormatHourLabel(time);

            entries.Add(new HourlyEntry(
                Time: time,
                Label: label,
                Temperature: (int)Math.Round(hourly.Temperature2m[i]),
                PrecipitationProbability: hourly.PrecipitationProbability[i] ?? 0,
                WeatherCode: wc,
                IconClass: WmoCodeMap.GetIconClass(wc, isDay),
                IsDay: isDay
            ));
        }

        return new HourlyForecast(entries);
    }

    internal static DailyForecast TransformDaily(OpenMeteoDaily daily, int days)
    {
        var count = Math.Min(days, daily.Time.Count);
        var entries = new List<DailyEntry>();
        for (int i = 0; i < count; i++)
        {
            var wc = daily.WeatherCode[i];
            entries.Add(new DailyEntry(
                Date: daily.Time[i],
                High: (int)Math.Round(daily.Temperature2mMax[i]),
                Low: (int)Math.Round(daily.Temperature2mMin[i]),
                WeatherCode: wc,
                Condition: WmoCodeMap.GetCondition(wc),
                IconClass: WmoCodeMap.GetIconClass(wc, isDay: true),
                PrecipitationProbability: daily.PrecipitationProbabilityMax[i] ?? 0,
                Sunrise: daily.Sunrise[i],
                Sunset: daily.Sunset[i]
            ));
        }

        return new DailyForecast(entries);
    }

    private static bool IsNightHour(string time, OpenMeteoDaily daily)
    {
        var date = time[..10];
        for (int i = 0; i < daily.Time.Count; i++)
        {
            if (daily.Time[i] == date)
            {
                return string.Compare(time, daily.Sunrise[i], StringComparison.Ordinal) < 0
                    || string.Compare(time, daily.Sunset[i], StringComparison.Ordinal) >= 0;
            }
        }
        return false;
    }

    private static string FormatHourLabel(string isoTime)
    {
        // isoTime format: "yyyy-MM-ddTHH:mm"
        var hourStr = isoTime[11..13];
        var h = int.Parse(hourStr);
        return h switch
        {
            0 => "12am",
            < 12 => $"{h}am",
            12 => "12pm",
            _ => $"{h - 12}pm"
        };
    }
}
