using WeatherProxy.Mappings;

namespace WeatherProxy.Tests;

public class WmoCodeMapTests
{
    [Theory]
    [InlineData(0, "Clear")]
    [InlineData(1, "Mainly Clear")]
    [InlineData(2, "Partly Cloudy")]
    [InlineData(3, "Overcast")]
    [InlineData(45, "Fog")]
    [InlineData(48, "Fog")]
    [InlineData(51, "Drizzle")]
    [InlineData(53, "Drizzle")]
    [InlineData(55, "Drizzle")]
    [InlineData(61, "Rain")]
    [InlineData(63, "Rain")]
    [InlineData(65, "Rain")]
    [InlineData(66, "Freezing Rain")]
    [InlineData(67, "Freezing Rain")]
    [InlineData(71, "Snow")]
    [InlineData(75, "Snow")]
    [InlineData(77, "Snow")]
    [InlineData(80, "Showers")]
    [InlineData(81, "Showers")]
    [InlineData(82, "Showers")]
    [InlineData(85, "Snow Showers")]
    [InlineData(86, "Snow Showers")]
    [InlineData(95, "Thunderstorm")]
    [InlineData(96, "Thunderstorm")]
    [InlineData(99, "Thunderstorm")]
    [InlineData(100, "Unknown")]
    public void GetCondition_ReturnsExpectedLabel(int code, string expected)
    {
        Assert.Equal(expected, WmoCodeMap.GetCondition(code));
    }

    [Theory]
    [InlineData(0, true, "wi-day-sunny")]
    [InlineData(0, false, "wi-night-clear")]
    [InlineData(1, true, "wi-day-sunny-overcast")]
    [InlineData(1, false, "wi-night-partly-cloudy")]
    [InlineData(2, true, "wi-day-cloudy")]
    [InlineData(2, false, "wi-night-cloudy")]
    [InlineData(3, true, "wi-wmo4680-3")]
    [InlineData(3, false, "wi-wmo4680-3")]
    [InlineData(45, true, "wi-wmo4680-45")]
    [InlineData(48, true, "wi-wmo4680-48")]
    [InlineData(51, true, "wi-wmo4680-51")]
    [InlineData(55, true, "wi-wmo4680-55")]
    [InlineData(61, true, "wi-wmo4680-61")]
    [InlineData(67, true, "wi-wmo4680-67")]
    [InlineData(71, true, "wi-wmo4680-71")]
    [InlineData(77, true, "wi-wmo4680-77")]
    [InlineData(80, true, "wi-wmo4680-80")]
    [InlineData(86, true, "wi-wmo4680-86")]
    [InlineData(95, true, "wi-wmo4680-95")]
    [InlineData(99, true, "wi-wmo4680-99")]
    [InlineData(100, true, "wi-na")]
    public void GetIconClass_ReturnsExpectedClass(int code, bool isDay, string expected)
    {
        Assert.Equal(expected, WmoCodeMap.GetIconClass(code, isDay));
    }

    [Theory]
    [InlineData(0, "N")]
    [InlineData(22, "N")]
    [InlineData(338, "N")]
    [InlineData(359, "N")]
    [InlineData(23, "NE")]
    [InlineData(45, "NE")]
    [InlineData(67, "NE")]
    [InlineData(68, "E")]
    [InlineData(90, "E")]
    [InlineData(112, "E")]
    [InlineData(113, "SE")]
    [InlineData(135, "SE")]
    [InlineData(157, "SE")]
    [InlineData(158, "S")]
    [InlineData(180, "S")]
    [InlineData(202, "S")]
    [InlineData(203, "SW")]
    [InlineData(225, "SW")]
    [InlineData(247, "SW")]
    [InlineData(248, "W")]
    [InlineData(270, "W")]
    [InlineData(292, "W")]
    [InlineData(293, "NW")]
    [InlineData(315, "NW")]
    [InlineData(337, "NW")]
    public void GetWindDirection_ReturnsExpectedCompass(int degrees, string expected)
    {
        Assert.Equal(expected, WmoCodeMap.GetWindDirection(degrees));
    }
}
