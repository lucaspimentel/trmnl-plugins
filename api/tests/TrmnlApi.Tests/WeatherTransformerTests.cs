using System.Text.Json;
using TrmnlApi.Models.OpenMeteo;
using TrmnlApi.Services;

namespace TrmnlApi.Tests;

public class WeatherTransformerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static OpenMeteoResponse LoadFixture()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "open-meteo-sample.json"));
        return JsonSerializer.Deserialize<OpenMeteoResponse>(json, JsonOptions)!;
    }

    [Fact]
    public void Transform_Current_RoundsTemperatures()
    {
        var raw = LoadFixture();
        var result = new WeatherTransformer().Transform(raw);

        Assert.Equal(42, result.Current.Temperature);       // 41.5 rounded
        Assert.Equal(38, result.Current.ApparentTemperature); // 37.8 rounded
    }

    [Fact]
    public void Transform_Current_MapsConditionAndIcon()
    {
        var raw = LoadFixture(); // weather_code = 2, is_day = 1
        var result = new WeatherTransformer().Transform(raw);

        Assert.Equal("Partly Cloudy", result.Current.Condition);
        Assert.Equal("wi-day-cloudy", result.Current.IconClass);
        Assert.True(result.Current.IsDay);
    }

    [Fact]
    public void Transform_Current_MapsWindDirection()
    {
        var raw = LoadFixture(); // wind_direction_10m = 225
        var result = new WeatherTransformer().Transform(raw);

        Assert.Equal(225, result.Current.WindDirectionDeg);
        Assert.Equal("SW", result.Current.WindDirection);
        Assert.Equal(12, result.Current.WindSpeed); // 11.6 rounded
    }

    [Fact]
    public void Transform_Hourly_SlicesFromCurrentHour()
    {
        var raw = LoadFixture(); // current.time = "2026-02-25T14:00"
        var result = new WeatherTransformer().Transform(raw);

        Assert.Equal(25, result.Hourly.Entries.Count);
        Assert.Equal("2026-02-25T14:00", result.Hourly.Entries[0].Time);
        Assert.Equal("2pm", result.Hourly.Entries[0].Label);
        Assert.Equal("2026-02-26T14:00", result.Hourly.Entries[^1].Time);
    }

    [Fact]
    public void Transform_Hourly_FormatsHourLabels()
    {
        var raw = LoadFixture();
        var result = new WeatherTransformer().Transform(raw);

        // Entry at index 1 = 15:00 → "3pm"
        Assert.Equal("3pm", result.Hourly.Entries[1].Label);
        // Entry at index 2 = 16:00 → "4pm"
        Assert.Equal("4pm", result.Hourly.Entries[2].Label);
        // Entry at index 10 = 00:00 → "12am"
        Assert.Equal("12am", result.Hourly.Entries[10].Label);
    }

    [Fact]
    public void Transform_Hourly_AssignsDayNightIconsCorrectly()
    {
        var raw = LoadFixture();
        var result = new WeatherTransformer().Transform(raw);

        // 14:00 is between sunrise 06:25 and sunset 17:28 → day, wc=2 → wi-day-cloudy
        Assert.True(result.Hourly.Entries[0].IsDay);
        Assert.Equal("wi-day-cloudy", result.Hourly.Entries[0].IconClass);

        // 20:00 is after sunset 17:28 → night, wc=0 → wi-night-clear
        var entry20 = result.Hourly.Entries.Single(e => e.Time == "2026-02-25T20:00");
        Assert.False(entry20.IsDay);
        Assert.Equal("wi-night-clear", entry20.IconClass);
    }

    // The transformer never truncates: the cache stores its output and is not keyed on
    // hours/days, so trimming here would cap every later request. See ForecastTrimmerTests.
    [Fact]
    public void Transform_Hourly_ReturnsEveryEntryFromCurrentHourOnward()
    {
        var raw = LoadFixture();
        var result = new WeatherTransformer().Transform(raw);

        var startIndex = raw.Hourly.Time.FindIndex(t => t.StartsWith("2026-02-25T14", StringComparison.Ordinal));
        Assert.Equal(raw.Hourly.Time.Count - startIndex, result.Hourly.Entries.Count);
        Assert.Equal("2pm", result.Hourly.Entries[0].Label);
    }

    [Fact]
    public void Transform_Daily_ReturnsEveryUpstreamEntry()
    {
        var raw = LoadFixture();
        var result = new WeatherTransformer().Transform(raw);

        Assert.Equal(raw.Daily.Time.Count, result.Daily.Entries.Count);
        Assert.Equal("2026-02-25", result.Daily.Entries[0].Date);
    }

    [Fact]
    public void Transform_Daily_RoundsTempsAndMapsCondition()
    {
        var raw = LoadFixture();
        var result = new WeatherTransformer().Transform(raw);

        var today = result.Daily.Entries[0];
        Assert.Equal("2026-02-25", today.Date);
        Assert.Equal(45, today.High);  // 45.2 rounded
        Assert.Equal(32, today.Low);   // 31.8 rounded
        Assert.Equal("Partly Cloudy", today.Condition);
        Assert.Equal("wi-day-cloudy", today.IconClass);
        Assert.Equal(20, today.PrecipitationProbability);
    }

    [Fact]
    public void Transform_Daily_PreservesSunriseSunset()
    {
        var raw = LoadFixture();
        var result = new WeatherTransformer().Transform(raw);

        Assert.Equal("2026-02-25T06:25", result.Daily.Entries[0].Sunrise);
        Assert.Equal("2026-02-25T17:28", result.Daily.Entries[0].Sunset);
    }

    // The tail of Open-Meteo's forecast window can arrive with nulls in place of numbers when
    // upstream has not computed that day yet. Any such day is dropped, not fatal to the request.
    [Theory]
    [InlineData("temperature_2m_max")]
    [InlineData("temperature_2m_min")]
    [InlineData("weather_code")]
    [InlineData("sunrise")]
    [InlineData("sunset")]
    public void Transform_Daily_SkipsDaysWithNullValues(string missingField)
    {
        var daily = new OpenMeteoDaily(
            Time: ["2026-02-25", "2026-02-26"],
            Temperature2mMax: [45.2, missingField == "temperature_2m_max" ? null : 46.0],
            Temperature2mMin: [31.8, missingField == "temperature_2m_min" ? null : 30.0],
            WeatherCode: [2, missingField == "weather_code" ? null : 0],
            PrecipitationProbabilityMax: [20, 10],
            Sunrise: ["2026-02-25T06:25", missingField == "sunrise" ? null : "2026-02-26T06:24"],
            Sunset: ["2026-02-25T17:28", missingField == "sunset" ? null : "2026-02-26T17:29"]);

        var result = WeatherTransformer.TransformDaily(daily);

        Assert.Single(result.Entries);
        Assert.Equal("2026-02-25", result.Entries[0].Date);
    }

    // A fully-populated day is unaffected by the null guard.
    [Fact]
    public void Transform_Daily_KeepsDaysWithAllValuesPresent()
    {
        var daily = new OpenMeteoDaily(
            Time: ["2026-02-25", "2026-02-26"],
            Temperature2mMax: [45.2, 46.0],
            Temperature2mMin: [31.8, 30.0],
            WeatherCode: [2, 0],
            PrecipitationProbabilityMax: [20, null],
            Sunrise: ["2026-02-25T06:25", "2026-02-26T06:24"],
            Sunset: ["2026-02-25T17:28", "2026-02-26T17:29"]);

        var result = WeatherTransformer.TransformDaily(daily);

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(46, result.Entries[1].High);
        Assert.Equal(0, result.Entries[1].PrecipitationProbability); // null probability defaults to 0
    }
}
