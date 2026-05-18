using TrmnlApi.Functions;

namespace TrmnlApi.Tests;

public class RequestValidatorTests
{
    [Theory]
    [InlineData("42.36", "-71.06", true, 42.36, -71.06)]   // valid coords
    [InlineData("0", "0", true, 0.0, 0.0)]                  // zero coords
    [InlineData("-90", "180", true, -90.0, 180.0)]          // boundary values
    [InlineData("90", "-180", true, 90.0, -180.0)]          // opposite boundary values
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
    [InlineData(0.0, 0.0, true)]                // origin
    [InlineData(42.36, -71.06, true)]           // typical coords
    [InlineData(90.0, 180.0, true)]             // upper boundaries
    [InlineData(-90.0, -180.0, true)]           // lower boundaries
    [InlineData(90.0001, 0.0, false)]           // lat just above max
    [InlineData(-90.0001, 0.0, false)]          // lat just below min
    [InlineData(0.0, 180.0001, false)]          // lon just above max
    [InlineData(0.0, -180.0001, false)]         // lon just below min
    [InlineData(91.0, 0.0, false)]              // lat well above range
    [InlineData(0.0, 200.0, false)]             // lon well above range
    [InlineData(double.NaN, 0.0, false)]        // NaN lat
    [InlineData(0.0, double.NaN, false)]        // NaN lon
    [InlineData(double.PositiveInfinity, 0.0, false)] // +Inf lat
    [InlineData(double.NegativeInfinity, 0.0, false)] // -Inf lat
    public void AreCoordinatesInRange_ReturnsExpected(double latitude, double longitude, bool expected)
    {
        Assert.Equal(expected, RequestValidator.AreCoordinatesInRange(latitude, longitude));
    }

    [Theory]
    [InlineData(null, true)]          // null → allowed (uses default)
    [InlineData("imperial", true)]    // valid
    [InlineData("metric", true)]      // valid
    [InlineData("", true)]            // empty string → allowed (uses default)
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
    // empty string → defaults to max, returns true
    [InlineData("", 1, 25, true, 25)]
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
