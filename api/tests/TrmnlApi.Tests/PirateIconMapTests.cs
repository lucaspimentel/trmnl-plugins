using TrmnlApi.Mappings;

namespace TrmnlApi.Tests;

public class PirateIconMapTests
{
    [Theory]
    [InlineData("clear-day", "Clear", "wi-day-sunny")]
    [InlineData("clear-night", "Clear", "wi-night-clear")]
    [InlineData("partly-cloudy-day", "Partly Cloudy", "wi-day-cloudy")]
    [InlineData("partly-cloudy-night", "Partly Cloudy", "wi-night-partly-cloudy")]
    [InlineData("cloudy", "Cloudy", "wi-wmo4680-3")]
    [InlineData("rain", "Rain", "wi-wmo4680-63")]
    [InlineData("snow", "Snow", "wi-wmo4680-71")]
    [InlineData("sleet", "Sleet", "wi-wmo4680-67")]
    [InlineData("fog", "Fog", "wi-wmo4680-45")]
    [InlineData("wind", "Windy", "wi-strong-wind")]
    [InlineData("not-a-real-icon", "Unknown", "wi-na")]
    public void Mapping(string icon, string expectedCondition, string expectedIconClass)
    {
        Assert.Equal(expectedCondition, PirateIconMap.GetCondition(icon));
        Assert.Equal(expectedIconClass, PirateIconMap.GetIconClass(icon));
    }
}
