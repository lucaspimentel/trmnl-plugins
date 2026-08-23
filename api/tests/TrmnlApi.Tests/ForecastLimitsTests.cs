using TrmnlApi.Functions;
using TrmnlApi.Services;

namespace TrmnlApi.Tests;

public class ForecastLimitsTests
{
    // How many days a caller may request and how many we fetch from Open-Meteo are
    // separate decisions, but they are not independent: asking for more than we fetch
    // silently returns fewer entries than requested, which is invisible in the response.
    [Fact]
    public void RequestedDayCap_NeverExceedsWhatIsFetchedFromUpstream()
    {
        Assert.True(
            WeatherEndpoint.MaxDays <= OpenMeteoClient.MaxForecastDays,
            $"WeatherEndpoint.MaxDays ({WeatherEndpoint.MaxDays}) exceeds " +
            $"OpenMeteoClient.MaxForecastDays ({OpenMeteoClient.MaxForecastDays}); " +
            "requests near the cap would return fewer days than asked for.");
    }
}
