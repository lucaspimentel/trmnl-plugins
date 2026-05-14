using TrmnlApi.Models;
using TrmnlApi.Models.OpenMeteo;
using TrmnlApi.Providers;
using TrmnlApi.Services;

namespace TrmnlApi.Tests;

public class OpenMeteoProviderTests
{
    [Fact]
    public void Name_ReturnsOpenMeteo()
    {
        var provider = new OpenMeteoProvider(new StubClient(), new StubTransformer());

        Assert.Equal("open-meteo", provider.Name);
    }

    [Fact]
    public async Task GetForecastAsync_PassesArgumentsToClientAndReturnsTransformerOutput()
    {
        var rawResponse = MakeEmptyRaw();
        var expected = MakeEmptyWeatherResponse();
        var client = new StubClient { Response = rawResponse };
        var transformer = new StubTransformer { Result = expected };
        var provider = new OpenMeteoProvider(client, transformer);

        using var cts = new CancellationTokenSource();
        var result = await provider.GetForecastAsync(12.5, -34.75, metric: true, cts.Token);

        Assert.Same(expected, result);
        Assert.Equal(1, client.CallCount);
        Assert.Equal(12.5, client.LastLatitude);
        Assert.Equal(-34.75, client.LastLongitude);
        Assert.True(client.LastMetric);
        Assert.Equal(cts.Token, client.LastCancellationToken);
        Assert.Same(rawResponse, transformer.LastInput);
    }

    private static OpenMeteoResponse MakeEmptyRaw() => new(
        Latitude: 0,
        Longitude: 0,
        Timezone: "UTC",
        Current: new OpenMeteoCurrent(
            Time: "2026-01-01T00:00",
            Temperature2m: 0, ApparentTemperature: 0, RelativeHumidity2m: 0,
            Precipitation: 0, WeatherCode: 0, WindSpeed10m: 0, WindDirection10m: 0, IsDay: 1),
        Hourly: new OpenMeteoHourly([], [], [], []),
        Daily: new OpenMeteoDaily([], [], [], [], [], [], []));

    private static WeatherResponse MakeEmptyWeatherResponse() => new(
        Current: new CurrentConditions(
            Time: "2026-01-01T00:00",
            Temperature: 0, ApparentTemperature: 0, RelativeHumidity: 0,
            Precipitation: 0, Condition: "Clear", IconClass: "wi-day-sunny",
            WindSpeed: 0, WindDirectionDeg: 0, WindDirection: "N", IsDay: true),
        Hourly: new HourlyForecast([]),
        Daily: new DailyForecast([]));

    private sealed class StubClient : IOpenMeteoClient
    {
        public OpenMeteoResponse Response { get; set; } = default!;
        public int CallCount { get; private set; }
        public double LastLatitude { get; private set; }
        public double LastLongitude { get; private set; }
        public bool LastMetric { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<OpenMeteoResponse> GetForecastAsync(double latitude, double longitude, bool metric = false, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastLatitude = latitude;
            LastLongitude = longitude;
            LastMetric = metric;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(Response);
        }
    }

    private sealed class StubTransformer : IWeatherTransformer
    {
        public WeatherResponse Result { get; set; } = default!;
        public OpenMeteoResponse? LastInput { get; private set; }

        public WeatherResponse Transform(OpenMeteoResponse raw, int hours = 25, int days = 6)
        {
            LastInput = raw;
            return Result;
        }
    }
}
