using TrmnlApi.Mappings;
using TrmnlApi.Models;
using TrmnlApi.Models.OpenMeteo;

namespace TrmnlApi.Services;

public class WeatherTransformer : IWeatherTransformer
{
    public WeatherResponse Transform(OpenMeteoResponse raw)
    {
        var current = TransformCurrent(raw.Current);
        var hourly = TransformHourly(raw.Hourly, raw.Current.Time, raw.Daily);
        var daily = TransformDaily(raw.Daily);

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
            Condition: WmoCodeMap.GetCondition(c.WeatherCode),
            IconClass: WmoCodeMap.GetIconClass(c.WeatherCode, isDay),
            WindSpeed: (int)Math.Round(c.WindSpeed10m),
            WindDirectionDeg: c.WindDirection10m,
            WindDirection: WmoCodeMap.GetWindDirection(c.WindDirection10m),
            IsDay: isDay
        );
    }

    internal static HourlyForecast TransformHourly(OpenMeteoHourly hourly, string currentTime, OpenMeteoDaily daily)
    {
        var currentHour = currentTime[..13];
        var startIndex = hourly.Time.FindIndex(t => t.StartsWith(currentHour, StringComparison.Ordinal));
        if (startIndex < 0) startIndex = 0;

        var entries = new List<HourlyEntry>();
        for (int i = startIndex; i < hourly.Time.Count; i++)
        {
            // Same as daily: an hour past the model horizon arrives with nulls in place of
            // numbers. Skip it rather than inventing a temperature or condition for it.
            if (hourly.Temperature2m[i] is not double temperature ||
                hourly.WeatherCode[i] is not int wc)
            {
                continue;
            }

            var time = hourly.Time[i];
            var isDay = IsNightHour(time, daily) == false;

            entries.Add(new HourlyEntry(
                Time: time,
                Label: HourLabel.Format(time),
                Temperature: (int)Math.Round(temperature),
                PrecipitationProbability: hourly.PrecipitationProbability[i] ?? 0,
                IconClass: WmoCodeMap.GetIconClass(wc, isDay),
                IsDay: isDay
            ));
        }

        return new HourlyForecast(entries);
    }

    internal static DailyForecast TransformDaily(OpenMeteoDaily daily)
    {
        var entries = new List<DailyEntry>();
        for (int i = 0; i < daily.Time.Count; i++)
        {
            // The last day or two of Open-Meteo's window can arrive before its values are
            // computed, sending nulls in place of numbers across the whole day. Skip days
            // missing anything required rather than failing the entire forecast.
            if (daily.Temperature2mMax[i] is not double high ||
                daily.Temperature2mMin[i] is not double low ||
                daily.WeatherCode[i] is not int wc ||
                daily.Sunrise[i] is not string sunrise ||
                daily.Sunset[i] is not string sunset)
            {
                continue;
            }

            entries.Add(new DailyEntry(
                Date: daily.Time[i],
                High: (int)Math.Round(high),
                Low: (int)Math.Round(low),
                Condition: WmoCodeMap.GetCondition(wc),
                IconClass: WmoCodeMap.GetIconClass(wc, isDay: true),
                PrecipitationProbability: daily.PrecipitationProbabilityMax[i] ?? 0,
                Sunrise: sunrise,
                Sunset: sunset
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
                // Fall through to the daytime default when upstream has not sent this day's
                // sun times yet, rather than reading a missing value as "night".
                if (daily.Sunrise[i] is not string sunrise || daily.Sunset[i] is not string sunset)
                {
                    break;
                }

                return string.Compare(time, sunrise, StringComparison.Ordinal) < 0
                    || string.Compare(time, sunset, StringComparison.Ordinal) >= 0;
            }
        }
        return false;
    }

}
