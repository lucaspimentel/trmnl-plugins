using WeatherProxy.Functions;

namespace WeatherProxy.Tests;

public class RequestValidatorTests
{
    [Theory]
    [InlineData("42.36", "-71.06", true, 42.36, -71.06)]   // valid coords
    [InlineData("0", "0", true, 0.0, 0.0)]                  // zero coords
    [InlineData("-90", "180", true, -90.0, 180.0)]          // boundary values
    [InlineData("1.5", "2.5", true, 1.5, 2.5)]              // decimals
    [InlineData(null, "-71.06", false, 0.0, 0.0)]           // null lat
    [InlineData("42.36", null, false, 42.36, 0.0)]           // null lon (lat parsed before failure)
    [InlineData("abc", "-71.06", false, 0.0, 0.0)]          // non-numeric lat
    [InlineData("42.36", "xyz", false, 42.36, 0.0)]         // non-numeric lon (lat parsed before failure)
    [InlineData("", "-71.06", false, 0.0, 0.0)]             // empty lat
    public void TryParseCoordinates_ReturnsExpected(string? lat, string? lon, bool expectedResult, double expectedLat, double expectedLon)
    {
        var result = RequestValidator.TryParseCoordinates(lat, lon, out var latitude, out var longitude);

        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedLat, latitude);
        Assert.Equal(expectedLon, longitude);
    }

    [Theory]
    [InlineData(null, true)]          // null → allowed (uses default)
    [InlineData("imperial", true)]    // valid
    [InlineData("metric", true)]      // valid
    [InlineData("", false)]           // empty string → invalid
    [InlineData("Imperial", false)]   // case-sensitive
    [InlineData("Metric", false)]     // case-sensitive
    [InlineData("celsius", false)]    // unrecognized value
    public void IsValidUnits_ReturnsExpected(string? units, bool expected)
    {
        Assert.Equal(expected, RequestValidator.IsValidUnits(units));
    }

    [Theory]
    // null → defaults to max, returns true
    [InlineData(null, 1, 25, true, 25)]
    [InlineData(null, 1, 6, true, 6)]
    // valid boundary values
    [InlineData("1", 1, 25, true, 1)]
    [InlineData("25", 1, 25, true, 25)]
    [InlineData("1", 1, 6, true, 1)]
    [InlineData("6", 1, 6, true, 6)]
    // values within range
    [InlineData("12", 1, 25, true, 12)]
    [InlineData("3", 1, 6, true, 3)]
    // below min
    [InlineData("0", 1, 25, false, 0)]
    [InlineData("0", 1, 6, false, 0)]
    // above max
    [InlineData("26", 1, 25, false, 26)]
    [InlineData("7", 1, 6, false, 7)]
    // non-numeric
    [InlineData("abc", 1, 25, false, 0)]
    // empty string
    [InlineData("", 1, 25, false, 0)]
    // decimal (not an integer)
    [InlineData("1.5", 1, 25, false, 0)]
    public void TryParseRangeParam_ReturnsExpected(string? value, int min, int max, bool expectedResult, int expectedValue)
    {
        var result = RequestValidator.TryParseRangeParam(value, min, max, out var parsed);

        Assert.Equal(expectedResult, result);
        if (expectedResult)
            Assert.Equal(expectedValue, parsed);
    }
}
