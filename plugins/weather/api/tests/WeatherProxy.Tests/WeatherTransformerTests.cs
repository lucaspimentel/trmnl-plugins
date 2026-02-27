using System.Text.Json;
using WeatherProxy.Models.OpenMeteo;
using WeatherProxy.Services;

namespace WeatherProxy.Tests;

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
        Assert.Equal("Now", result.Hourly.Entries[0].Label);
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

    [Fact]
    public void Transform_Hourly_LimitsEntryCount()
    {
        var raw = LoadFixture();
        var result = new WeatherTransformer().Transform(raw, hours: 12);

        Assert.Equal(12, result.Hourly.Entries.Count);
        Assert.Equal("Now", result.Hourly.Entries[0].Label);
    }

    [Fact]
    public void Transform_Daily_HasFiveDays()
    {
        var raw = LoadFixture();
        var result = new WeatherTransformer().Transform(raw);

        Assert.Equal(5, result.Daily.Entries.Count);
    }

    [Fact]
    public void Transform_Daily_LimitsEntryCount()
    {
        var raw = LoadFixture();
        var result = new WeatherTransformer().Transform(raw, days: 3);

        Assert.Equal(3, result.Daily.Entries.Count);
        Assert.Equal("2026-02-25", result.Daily.Entries[0].Date);
        Assert.Equal("2026-02-27", result.Daily.Entries[^1].Date);
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
}
