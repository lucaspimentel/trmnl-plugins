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
        // A Puerto Rico ZIP, which the vendor geocoder does not resolve at all. The country is
        // declared because the bare code is genuinely ambiguous with a Warsaw postcode - this
        // test used to pass without it only because the fixture was missing the Polish row.
        var match = Build().Find("00784", preferredCountry: "US");

        Assert.NotNull(match);
        Assert.Equal(17.98, match.Value.Latitude);
        Assert.Equal(-66.11, match.Value.Longitude);
        // Postal place names are unusable as labels, so the label comes from the reverse lookup.
        Assert.Null(match.Value.CityName);
    }

    [Fact]
    public void Find_PostalCodeSharedWithABiggerCity_StillRanksByPopulationWhenNothingIsDeclared()
    {
        // The honest bare-code answer, and what staging returns: Warsaw outranks Caguas.
        var match = Build().Find("00784");

        Assert.NotNull(match);
        Assert.Equal(52.21, match.Value.Latitude);
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
    // "Auto". None of them may change the answer or cause an error.
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Auto")]
    public void Find_WithoutAUsableCountry_RanksByPopulationAsBefore(string? country)
    {
        var match = Build().Find("75001", preferredCountry: country);

        Assert.NotNull(match);
        Assert.Equal(48.86, match.Value.Latitude);
    }

    [Fact]
    public void Find_DropdownLabelInsteadOfACode_IsStillHonoured()
    {
        // What the plugin actually sent. Rejecting it served a user who had chosen their country
        // as though they had not.
        var match = Build().Find("75001", preferredCountry: "US - United States of America");

        Assert.NotNull(match);
        Assert.Equal(32.96, match.Value.Latitude);
    }

    [Fact]
    public void Find_DeclaredCountryTheCodeIsNotIn_KeepsThePopulationWinner()
    {
        // A preference, not a filter. 75001 exists in neither Germany nor anywhere else in the
        // fixture bar France and the US, and a German user still deserves an answer. Germany is
        // skipped rather than applied, so the region floor decides - and it holds both France and
        // the US, so population settles it exactly as before.
        var match = Build().Find("75001", preferredCountry: "DE");

        Assert.NotNull(match);
        Assert.Equal(48.86, match.Value.Latitude);
        Assert.Equal(CountryHint.None, match.Value.Hint);
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
    public void Find_PostalCodeOfADeclaredCountrysTerritory_StaysInThatTerritory()
    {
        // 00784 is a Puerto Rico ZIP and a Warsaw postcode. Someone in Caguas who declares the
        // United States - which issued their ZIP - must not be sent to Poland.
        var match = Build().Find("00784", preferredCountry: "US");

        Assert.NotNull(match);
        Assert.Equal(17.98, match.Value.Latitude);
    }

    [Fact]
    public void Find_PostalCodeWithTheTerritoryItselfDeclared_AlsoWorks()
    {
        var match = Build().Find("00784", preferredCountry: "PR");

        Assert.NotNull(match);
        Assert.Equal(17.98, match.Value.Latitude);
    }

    [Fact]
    public void Find_DeclaringATerritory_DoesNotWidenToTheSovereign()
    {
        // The relationship is one-directional. Declaring PR is more precise than declaring US, so
        // it must not start accepting mainland matches: 75001 exists in the US but not in PR, so
        // PR is skipped and the region floor, which holds both candidates, leaves the population
        // ranking to settle it.
        var match = Build().Find("75001", preferredCountry: "PR");

        Assert.NotNull(match);
        Assert.Equal(48.86, match.Value.Latitude);
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

    [Fact]
    public void Find_ABarePostalCode_TakesTheCountryTheCallersTimeZoneIsIn()
    {
        // The bug this shipped for. 02180 is Stoneham, Massachusetts, and also a real code in
        // Finland, Lithuania, Poland, Peru and Korea. Ranked by population it is Seoul, which is
        // what a user in Stoneham with nothing set actually saw on their screen.
        var match = Build().Find("02180", timeZone: "America/New_York");

        Assert.NotNull(match);
        Assert.Equal(42.48, match.Value.Latitude);
    }

    [Theory]
    // A preference, not a rule about the United States. Korea is outside the fallback region and
    // still wins when asked for; Lithuania is the smallest of the four candidates and loses on
    // population to every one of them, so it can only be arriving from the time zone.
    [InlineData("Asia/Seoul", 37.6)]
    [InlineData("Europe/Vilnius", 54.66)]
    public void Find_ABarePostalCode_TakesAnyCallersTimeZoneJustTheSame(string zone, double expected)
    {
        var match = Build().Find("02180", timeZone: zone);

        Assert.NotNull(match);
        Assert.Equal(expected, match.Value.Latitude);
    }

    [Fact]
    public void Find_ADeclaredCountry_OutranksTheTimeZone()
    {
        // What you chose beats what your clock implies.
        var match = Build().Find("02180", preferredCountry: "FI", timeZone: "America/New_York");

        Assert.NotNull(match);
        Assert.Equal(60.2, match.Value.Latitude);
        Assert.Equal(CountryHint.Declared, match.Value.Hint);
    }

    [Fact]
    public void Find_ADeclaredCountryTheCodeIsNotIn_FallsThroughToTheTimeZone()
    {
        // The bug. A declared country whose set matches no candidate used to be chosen anyway,
        // consuming the slot without contributing: the intersection emptied, every candidate
        // survived, and population answered Seoul - the exact outcome the time zone exists to
        // prevent. Germany has no 02180, so the time zone must get its turn.
        var match = Build().Find("02180", preferredCountry: "DE", timeZone: "America/New_York");

        Assert.NotNull(match);
        Assert.Equal(42.48, match.Value.Latitude);
        // And the reported hint names the level that did the work, not the one that was set.
        Assert.Equal(CountryHint.TimeZone, match.Value.Hint);
    }

    [Fact]
    public void Find_ADeclaredCountryTheCodeIsNotIn_WithNoTimeZone_ReachesTheRegionFloor()
    {
        // The chain runs all the way down: a stale dropdown value with no time zone behind it
        // leaves the caller no worse off than having set nothing at all.
        var match = Build().Find("02180", preferredCountry: "DE");

        Assert.NotNull(match);
        Assert.Equal(60.2, match.Value.Latitude);
        // The floors report "none": they are a guess about the audience, not something the
        // caller told us, and the hint facet counts what callers said.
        Assert.Equal(CountryHint.None, match.Value.Hint);
    }

    [Fact]
    public void Find_ZipPlusFourAgainstADeclaredCountryTheCodeIsNotIn_StillMeansTheUnitedStates()
    {
        // The same skip, one level lower: Germany matches nothing, so the shape of the input gets
        // to speak. Before, the declared country suppressed the ZIP+4 floor and answered Seoul.
        var match = Build().Find("02180-1234", preferredCountry: "DE");

        Assert.NotNull(match);
        Assert.Equal(42.48, match.Value.Latitude);
    }

    [Fact]
    public void Find_ACityNameWithADeclaredCountryItIsNotIn_KeepsThePopulationWinner()
    {
        // Names get the caller's signals and no floors, so a skipped level leaves population in
        // charge. The invariant either way: a hint may never turn a working input into a miss.
        var match = Build().Find("Boston", preferredCountry: "KI");

        Assert.NotNull(match);
        Assert.Equal(42.36, match.Value.Latitude);
        Assert.Equal(CountryHint.None, match.Value.Hint);
    }

    [Fact]
    public void Find_ATypedQualifier_OutranksTheTimeZone()
    {
        // And what you typed this time beats both.
        var match = Build().Find("02180, KR", timeZone: "America/New_York");

        Assert.NotNull(match);
        Assert.Equal(37.6, match.Value.Latitude);
    }

    [Fact]
    public void Find_WithNoHintAtAll_PrefersWhereTheUsersAreOverTheBiggestCity()
    {
        // An install predating both settings. Seoul is ten million people and outranks everything
        // on population; the region preference drops it, and Helsinki wins on population among
        // what is left. Not the right answer for a user in Stoneham - only a time zone fixes that
        // - but no longer a different continent from every user we have.
        var match = Build().Find("02180");

        Assert.NotNull(match);
        Assert.Equal(60.2, match.Value.Latitude);
    }

    [Fact]
    public void Find_APostalCodeFoundOnlyOutsideThatRegion_StillResolves()
    {
        // The invariant that governs every preference here: it breaks ties between matches that
        // were already valid and may never turn a working input into a miss.
        var match = Build().Find("06236");

        Assert.NotNull(match);
        Assert.Equal(37.5, match.Value.Latitude);
    }

    [Fact]
    public void Find_AnUnknownTimeZone_IsIgnoredRatherThanFatal()
    {
        var match = Build().Find("02180", timeZone: "Mars/Olympus");

        Assert.NotNull(match);
        Assert.Equal(60.2, match.Value.Latitude);
    }

    [Fact]
    public void Find_ACityName_IsNotTouchedByTheRegionPreference()
    {
        // Why the region preference is postal-only. Santiago de Chile is fifty times the size of
        // Santiago de Compostela, and only Chile is outside the region: preferring the region for
        // names would answer Spain for everyone, which is plainly worse than population.
        var match = Build().Find("Santiago");

        Assert.NotNull(match);
        Assert.Equal(-33.46, match.Value.Latitude);
    }

    [Fact]
    public void Find_ATerritorysPostalCode_ReachesItFromTheSovereignsTimeZone()
    {
        // The time zone expands through PostalJurisdictions exactly as a declared country does, so
        // 00784 from New York is Guayama and not Warsaw - the same failure, one signal weaker.
        var match = Build().Find("00784", timeZone: "America/New_York");

        Assert.NotNull(match);
        Assert.Equal(17.98, match.Value.Latitude);
    }

    [Fact]
    public void Find_ZipPlusFour_ResolvesAndMeansTheUnitedStates()
    {
        // No country in the source data writes NNNNN-NNNN, so this shape names its own country
        // with no hint of any kind. It also used to miss outright: the full form normalized to
        // nine digits and matched no row at all.
        var match = Build().Find("02180-1234");

        Assert.NotNull(match);
        Assert.Equal(42.48, match.Value.Latitude);
    }

    [Fact]
    public void Find_ZipPlusFourAgainstADeclaredCountry_KeepsTheDeclaredOne()
    {
        var match = Build().Find("02180-1234", preferredCountry: "KR");

        Assert.NotNull(match);
        Assert.Equal(37.6, match.Value.Latitude);
    }

    private SqliteLocalGeocoder Build() => new(
        _fixture.Open(),
        Options.Create(new GeoOptions()),
        NullLogger<SqliteLocalGeocoder>.Instance);

    public void Dispose() => _fixture.Dispose();
}
