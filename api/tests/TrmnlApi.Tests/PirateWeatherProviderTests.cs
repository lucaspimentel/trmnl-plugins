using System.Text.Json;
using TrmnlApi.Models.PirateWeather;
using TrmnlApi.Providers;

namespace TrmnlApi.Tests;

public class PirateWeatherProviderTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static PirateWeatherResponse LoadFixture()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pirate-weather-sample.json"));
        return JsonSerializer.Deserialize<PirateWeatherResponse>(json, JsonOptions)!;
    }

    [Fact]
    public void Name_ReturnsPirateWeather()
    {
        var provider = new PirateWeatherProvider(client: null!);
        Assert.Equal("pirate-weather", provider.Name);
    }

    [Fact]
    public void Transform_Current_RoundsTemperatures()
    {
        var result = PirateWeatherProvider.Transform(LoadFixture());
        // Fixture currently: temperature=55.45, apparentTemperature=50.45
        Assert.Equal(55, result.Current.Temperature);
        Assert.Equal(50, result.Current.ApparentTemperature);
    }

    [Fact]
    public void Transform_Current_ConvertsHumidityToPercent()
    {
        var result = PirateWeatherProvider.Transform(LoadFixture());
        // Fixture humidity=0.74 → 74
        Assert.Equal(74, result.Current.RelativeHumidity);
    }

    [Fact]
    public void Transform_Current_MapsConditionAndIcon()
    {
        var result = PirateWeatherProvider.Transform(LoadFixture());
        // Fixture icon=partly-cloudy-night
        Assert.Equal("Partly Cloudy", result.Current.Condition);
        Assert.Equal("wi-night-partly-cloudy", result.Current.IconClass);
    }

    [Fact]
    public void Transform_Current_MapsWindDirection()
    {
        var result = PirateWeatherProvider.Transform(LoadFixture());
        // Fixture windBearing=176 (south sector 158..203)
        Assert.Equal(176, result.Current.WindDirectionDeg);
        Assert.Equal("S", result.Current.WindDirection);
    }

    [Fact]
    public void Transform_Current_FormatsTimeInLocalTimezone()
    {
        var result = PirateWeatherProvider.Transform(LoadFixture());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$", result.Current.Time);
    }

    [Fact]
    public void Transform_Hourly_SlicesToCurrentHourPlus24()
    {
        var result = PirateWeatherProvider.Transform(LoadFixture());
        Assert.Equal(25, result.Hourly.Entries.Count);
        Assert.Equal("Now", result.Hourly.Entries[0].Label);
        Assert.Matches(@"^\d{1,2}(am|pm)$", result.Hourly.Entries[1].Label);
    }

    [Fact]
    public void Transform_Hourly_ConvertsPrecipProbabilityToPercent()
    {
        var raw = LoadFixture();
        var result = PirateWeatherProvider.Transform(raw);
        var first = result.Hourly.Entries[0];
        var expectedPercent = (int)Math.Round(raw.Hourly.Data[0].PrecipProbability * 100);
        Assert.Equal(expectedPercent, first.PrecipitationProbability);
    }

    [Fact]
    public void Transform_Daily_LimitsToSixEntries()
    {
        var result = PirateWeatherProvider.Transform(LoadFixture());
        Assert.Equal(6, result.Daily.Entries.Count);
    }

    [Fact]
    public void Transform_Daily_ForcesDayIconVariant()
    {
        var result = PirateWeatherProvider.Transform(LoadFixture());
        // Daily[0] in fixture is "partly-cloudy-day" → should stay daytime
        Assert.Equal("wi-day-cloudy", result.Daily.Entries[0].IconClass);
        Assert.DoesNotContain("night", result.Daily.Entries[0].IconClass);
    }

    [Fact]
    public void Transform_Daily_FormatsSunriseSunsetInLocalTimezone()
    {
        var result = PirateWeatherProvider.Transform(LoadFixture());
        var d = result.Daily.Entries[0];
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$", d.Sunrise);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$", d.Sunset);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", d.Date);
    }

    [Fact]
    public void Transform_Daily_RoundsHighAndLow()
    {
        var raw = LoadFixture();
        var result = PirateWeatherProvider.Transform(raw);
        var first = result.Daily.Entries[0];
        Assert.Equal((int)Math.Round(raw.Daily.Data[0].TemperatureHigh), first.High);
        Assert.Equal((int)Math.Round(raw.Daily.Data[0].TemperatureLow), first.Low);
    }

    [Fact]
    public void Transform_HonorsHoursAndDaysLimits()
    {
        var result = PirateWeatherProvider.Transform(LoadFixture(), hours: 10, days: 3);
        Assert.Equal(10, result.Hourly.Entries.Count);
        Assert.Equal(3, result.Daily.Entries.Count);
    }

    [Fact]
    public async Task GetForecastAsync_DelegatesToClient()
    {
        var stub = new StubClient { Response = LoadFixture() };
        var provider = new PirateWeatherProvider(stub);

        var result = await provider.GetForecastAsync(42.36, -71.06, metric: false);

        Assert.Equal(1, stub.CallCount);
        Assert.Equal(42.36, stub.LastLatitude);
        Assert.Equal(-71.06, stub.LastLongitude);
        Assert.False(stub.LastMetric);
        Assert.NotNull(result);
        Assert.Equal(25, result.Hourly.Entries.Count);
    }

    private sealed class StubClient : Services.IPirateWeatherClient
    {
        public PirateWeatherResponse Response { get; set; } = default!;
        public int CallCount { get; private set; }
        public double LastLatitude { get; private set; }
        public double LastLongitude { get; private set; }
        public bool LastMetric { get; private set; }

        public Task<PirateWeatherResponse> GetForecastAsync(double latitude, double longitude, bool metric = false, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastLatitude = latitude;
            LastLongitude = longitude;
            LastMetric = metric;
            return Task.FromResult(Response);
        }
    }
}
