using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TrmnlApi.Geo;

namespace TrmnlApi.Tests;

/// <summary>
/// Every case here is one the vendor geocoder was measured against. The ones marked as vendor
/// failures are the reason this exists: a Puerto Rico user had no working input form at all, and
/// the plugin's own placeholder taught a two-letter pattern that only worked inside the US.
/// </summary>
public class SqliteLocalGeocoderTests : IDisposable
{
    private readonly GeoFixtureDatabase _fixture = new();

    [Fact]
    public void Find_PostalCode_ResolvesWhereTheVendorReturnedNothing()
    {
        var match = Build().Find("00784");

        Assert.NotNull(match);
        Assert.Equal(17.98, match.Value.Latitude);
        Assert.Equal(-66.11, match.Value.Longitude);
        // Postal place names are unusable as labels, so the label comes from the reverse lookup.
        Assert.Null(match.Value.CityName);
    }

    [Fact]
    public void Find_AmbiguousPostalCode_TakesTheMoreProminentOne()
    {
        // 75001 is both the first arrondissement of Paris and Addison, Texas. Ranking by the
        // largest population sitting on the code is what separates them.
        var match = Build().Find("75001");

        Assert.NotNull(match);
        Assert.Equal(48.86, match.Value.Latitude);
        Assert.Equal(2.34, match.Value.Longitude);
    }

    [Fact]
    public void Find_PostalCodeWithACountryQualifier_TakesThatCountry()
    {
        var match = Build().Find("75001, US");

        Assert.NotNull(match);
        Assert.Equal(32.96, match.Value.Latitude);
    }

    [Theory]
    // Two-letter country qualifiers, which the vendor rejects outright.
    [InlineData("Munich, DE", "Munich", 48.14, 11.58)]
    [InlineData("Toronto, CA", "Toronto", 43.7, -79.42)]
    // Spelled-out country names, which the vendor does accept.
    [InlineData("Munich, Germany", "Munich", 48.14, 11.58)]
    // Bare names rank by population, which is how the vendor picks too.
    [InlineData("Portland", "Portland", 45.52, -122.68)]
    [InlineData("Boston", "Boston", 42.36, -71.06)]
    // A US state qualifier, by code and spelled out.
    [InlineData("Portland, ME", "Portland", 43.66, -70.26)]
    [InlineData("Portland, Oregon", "Portland", 45.52, -122.68)]
    // Puerto Rico is its own GeoNames country, so "PR" reads as one.
    [InlineData("Guayama, PR", "Guayama", 17.98, -66.11)]
    public void Find_ResolvesTheCasesMeasuredAgainstTheVendor(string input, string name, double latitude, double longitude)
    {
        var match = Build().Find(input);

        Assert.NotNull(match);
        Assert.Equal(name, match.Value.CityName);
        Assert.Equal(latitude, match.Value.Latitude);
        Assert.Equal(longitude, match.Value.Longitude);
    }

    [Fact]
    public void Find_MatchesAnAlternateName()
    {
        var match = Build().Find("Muenchen");

        Assert.Equal("Munich", match?.CityName);
    }

    [Fact]
    public void Find_IgnoresCaseAndDiacritics()
    {
        var match = Build().Find("MÜNCHEN");

        Assert.Equal("Munich", match?.CityName);
    }

    [Fact]
    public void Find_UnsatisfiableQualifier_Misses()
    {
        // A miss rather than a shrug. Quietly dropping the qualifier would answer a question
        // nobody asked, and a miss still reaches the vendor geocoder.
        Assert.Null(Build().Find("Boston, FR"));
    }

    [Theory]
    [InlineData("Nowhereville")]
    [InlineData("Bostn")]           // No typo tolerance here; the vendor absorbs these.
    [InlineData("")]
    public void Find_NoMatch_ReturnsNull(string input)
    {
        Assert.Null(Build().Find(input));
    }

    [Fact]
    public void Find_OverlongInput_IsTurnedAwayWithoutQuerying()
    {
        Assert.Null(Build().Find(new string('x', 200)));
    }

    [Fact]
    public void Find_BrokenDatabase_MissesRatherThanThrowing()
    {
        var geocoder = Build();
        _fixture.Destroy();

        Assert.Null(geocoder.Find("Boston"));
    }

    [Fact]
    public void Find_SnapsToTheForecastCacheGrid()
    {
        // The same snap the vendor resolver applies, so one place is one cache entry however it
        // was typed.
        var match = Build().Find("Boston");

        Assert.Equal(42.36, match!.Value.Latitude);   // 42.35843 rounds away from zero
        Assert.Equal(-71.06, match.Value.Longitude);  // -71.05977 likewise
    }

    private SqliteLocalGeocoder Build() => new(
        _fixture.Open(),
        Options.Create(new GeoOptions()),
        NullLogger<SqliteLocalGeocoder>.Instance);

    public void Dispose() => _fixture.Dispose();
}
