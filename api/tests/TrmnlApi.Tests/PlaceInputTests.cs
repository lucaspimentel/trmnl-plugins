using TrmnlApi.Endpoints;

namespace TrmnlApi.Tests;

public class PlaceInputTests
{
    [Theory]
    [InlineData("42.35843\t-71.05977", 42.35843, -71.05977)]  // tab separated, as pasted from a map
    [InlineData("42.35,-71.05", 42.35, -71.05)]               // comma, no space
    [InlineData("42.35, -71.05", 42.35, -71.05)]              // comma and space
    [InlineData("  42.35 , -71.05  ", 42.35, -71.05)]         // padded on every side
    [InlineData("42.35 71.05", 42.35, 71.05)]                 // space only, both positive
    [InlineData("42,35", 42.0, 35.0)]                         // European decimal reads as a pair, not one coordinate
    [InlineData("0 0", 0.0, 0.0)]                             // null island
    [InlineData("-90 180", -90.0, 180.0)]                     // range boundary
    [InlineData("1e1 2e1", 10.0, 20.0)]                       // exponent notation still parses
    public void Parse_DetectsCoordinates(string place, double expectedLat, double expectedLon)
    {
        var input = PlaceInput.Parse(place, null, null);

        var coordinates = Assert.IsType<PlaceInput.Coordinates>(input);
        Assert.Equal(expectedLat, coordinates.Latitude);
        Assert.Equal(expectedLon, coordinates.Longitude);
        Assert.Equal("coordinates", input.Kind);
    }

    [Theory]
    [InlineData("Boston, MA", "Boston, MA")]              // name with a comma
    [InlineData("02180", "02180")]                        // postal code: one token
    [InlineData("SW1A 1AA", "SW1A 1AA")]                  // two tokens, neither numeric
    [InlineData("  Boston  ", "Boston")]                  // trimmed
    [InlineData("New   York", "New York")]                // internal whitespace collapsed
    [InlineData("Portland\tOR", "Portland OR")]           // tabs collapse to a single space
    [InlineData("42.35", "42.35")]                        // one number is not a pair
    [InlineData("42.35 -71.05 100", "42.35 -71.05 100")]  // three numbers are not a pair
    [InlineData("42.35 north", "42.35 north")]            // only one token is numeric
    public void Parse_DetectsQuery(string place, string expectedText)
    {
        var input = PlaceInput.Parse(place, null, null);

        var query = Assert.IsType<PlaceInput.Query>(input);
        Assert.Equal(expectedText, query.Text);
        Assert.Equal("place", input.Kind);
    }

    [Theory]
    [InlineData("-171.05, 42.35")]  // swapped order, caught only because the longitude exceeds 90
    [InlineData("100, 20")]         // latitude out of range
    [InlineData("42.35, 200")]      // longitude out of range
    [InlineData("NaN NaN")]         // parses as numbers, sits outside every range
    public void Parse_RejectsOutOfRangePairs(string place)
    {
        var input = PlaceInput.Parse(place, null, null);

        Assert.IsType<PlaceInput.Invalid>(input);
        Assert.Equal("invalid", input.Kind);
    }

    [Theory]
    [InlineData("Boston", "42.35", "-71.05")]  // place beats saved coordinates
    [InlineData("Boston", null, null)]
    public void Parse_PrefersPlaceOverCoordinates(string place, string? latitude, string? longitude)
    {
        var input = PlaceInput.Parse(place, latitude, longitude);

        Assert.Equal("Boston", Assert.IsType<PlaceInput.Query>(input).Text);
    }

    [Theory]
    [InlineData(null)]      // absent
    [InlineData("")]        // present but empty
    [InlineData("   ")]     // whitespace counts as blank
    [InlineData("\t")]
    public void Parse_FallsBackToCoordinateParams_WhenPlaceIsBlank(string? place)
    {
        var input = PlaceInput.Parse(place, "42.35", "-71.05");

        var coordinates = Assert.IsType<PlaceInput.Coordinates>(input);
        Assert.Equal(42.35, coordinates.Latitude);
        Assert.Equal(-71.05, coordinates.Longitude);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", "", "")]
    [InlineData("   ", null, "   ")]
    public void Parse_ReportsMissing_WhenNothingWasSupplied(string? place, string? latitude, string? longitude)
    {
        var input = PlaceInput.Parse(place, latitude, longitude);

        Assert.IsType<PlaceInput.Missing>(input);
        Assert.Equal("missing", input.Kind);
    }

    [Theory]
    [InlineData("42.35", null)]      // longitude never saved
    [InlineData(null, "-71.05")]     // latitude never saved
    [InlineData("abc", "-71.05")]    // not a number
    [InlineData("100", "-71.05")]    // out of range
    [InlineData("-171.05", "42.35")] // swapped, in the legacy parameters too
    public void Parse_ReportsInvalid_WhenCoordinateParamsAreUnusable(string? latitude, string? longitude)
    {
        var input = PlaceInput.Parse(null, latitude, longitude);

        Assert.IsType<PlaceInput.Invalid>(input);
    }
}
