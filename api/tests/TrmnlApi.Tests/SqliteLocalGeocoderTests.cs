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

    [Fact]
    public void Find_AmbiguousPostalCode_PrefersTheDeclaredCountry()
    {
        // The dropdown the user set, rather than a qualifier they typed. Without it 75001 is
        // Paris on population; with US declared it is Addison, Texas.
        var match = Build().Find("75001", preferredCountry: "US");

        Assert.NotNull(match);
        Assert.Equal(32.96, match.Value.Latitude);
    }

    [Theory]
    [InlineData("us")]
    [InlineData("Us")]
    public void Find_DeclaredCountry_IsCaseInsensitive(string country)
    {
        var match = Build().Find("75001", preferredCountry: country);

        Assert.NotNull(match);
        Assert.Equal(32.96, match.Value.Latitude);
    }

    [Theory]
    // An install that predates the setting, a fork that never had it, and the dropdown's own
    // "Auto" reaching the API unsplit. None of them may change the answer or cause an error.
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Auto")]
    [InlineData("US - United States")]
    public void Find_WithoutAUsableCountry_RanksByPopulationAsBefore(string? country)
    {
        var match = Build().Find("75001", preferredCountry: country);

        Assert.NotNull(match);
        Assert.Equal(48.86, match.Value.Latitude);
    }

    [Fact]
    public void Find_DeclaredCountryTheCodeIsNotIn_KeepsThePopulationWinner()
    {
        // A preference, not a filter. 75001 exists in neither Germany nor anywhere else in the
        // fixture bar France and the US, and a German user still deserves an answer.
        var match = Build().Find("75001", preferredCountry: "DE");

        Assert.NotNull(match);
        Assert.Equal(48.86, match.Value.Latitude);
    }

    [Fact]
    public void Find_TypedQualifierBeatsTheDeclaredCountry()
    {
        // What the user typed this time outranks what they set once.
        var match = Build().Find("75001, FR", preferredCountry: "US");

        Assert.NotNull(match);
        Assert.Equal(48.86, match.Value.Latitude);
    }

    [Fact]
    public void Find_CityName_PrefersTheDeclaredCountry()
    {
        // Bare "Boston" is Massachusetts on population, and stays that way for everyone who has
        // not said otherwise. A user in the UK means Lincolnshire.
        var match = Build().Find("Boston", preferredCountry: "GB");

        Assert.NotNull(match);
        Assert.Equal("Boston", match.Value.CityName);
        Assert.Equal(52.98, match.Value.Latitude);
    }

    [Fact]
    public void Find_CityName_WithoutADeclaredCountry_StillRanksByPopulation()
    {
        var match = Build().Find("Boston");

        Assert.NotNull(match);
        Assert.Equal(42.36, match.Value.Latitude);
    }

    [Fact]
    public void Find_CityNotInTheDeclaredCountry_StillResolves()
    {
        // The regression that would matter most: a preference must never send a plain, correct
        // name to the vendor just because it is abroad.
        var match = Build().Find("Munich", preferredCountry: "US");

        Assert.NotNull(match);
        Assert.Equal("Munich", match.Value.CityName);
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
