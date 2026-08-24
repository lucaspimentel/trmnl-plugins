using TrmnlApi.Services;

namespace TrmnlApi.Tests;

public class ForecastLimitsTests
{
    // The endpoint and the providers share one day cap, so raising it means asking
    // Open-Meteo for more than it will ever return: requests near the cap would then
    // silently come back with fewer days than asked for.
    [Fact]
    public void DayCap_NeverExceedsWhatUpstreamCanReturn()
    {
        Assert.True(
            ForecastLimits.MaxDays <= OpenMeteoClient.MaxForecastDays,
            $"ForecastLimits.MaxDays ({ForecastLimits.MaxDays}) exceeds " +
            $"OpenMeteoClient.MaxForecastDays ({OpenMeteoClient.MaxForecastDays}); " +
            "requests near the cap would return fewer days than asked for.");
    }
}
