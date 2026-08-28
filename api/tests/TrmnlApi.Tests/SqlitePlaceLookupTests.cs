using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TrmnlApi.Geo;

namespace TrmnlApi.Tests;

public class SqlitePlaceLookupTests : IDisposable
{
    private readonly GeoFixtureDatabase _fixture = new();

    [Fact]
    public void Find_PointInsideASubdivision_ReturnsItsCodeNameAndCity()
    {
        var place = Build().Find(42.36, -71.06);

        Assert.Equal("Boston", place.City);
        Assert.Equal("US-MA", place.SubdivisionCode);
        Assert.Equal("Massachusetts", place.SubdivisionName);
        Assert.Equal("US", place.CountryCode);
        // The short label is what the title bar shows, and "Boston, MA" fits where
        // "Boston, Massachusetts" did not.
        Assert.Equal("MA", place.ShortSubdivision);
    }

    [Fact]
    public void Find_PuertoRico_IsUsPr()
    {
        // The input Open-Meteo cannot answer at all. See the coverage table in
        // docs/geographic-telemetry.md.
        var place = Build().Find(17.98, -66.11);

        Assert.Equal("Guayama", place.City);
        Assert.Equal("US-PR", place.SubdivisionCode);
        Assert.Equal("PR", place.ShortSubdivision);
    }

    [Fact]
    public void Find_NumericSubdivisionCode_ShowsTheNameInstead()
    {
        var place = Build().Find(50.63, 3.06);

        Assert.Equal("FR-59", place.SubdivisionCode);
        Assert.Equal("Nord", place.ShortSubdivision);
    }

    [Fact]
    public void Find_BritishDistrict_ShowsTheNameInstead()
    {
        // "Cambridge, CAM" is not a place anyone recognises.
        var place = Build().Find(52.2, 0.12);

        Assert.Equal("GB-CAM", place.SubdivisionCode);
        Assert.Equal("Cambridgeshire", place.ShortSubdivision);
    }

    [Fact]
    public void Find_MidOcean_ReturnsNothingRatherThanInventingACountry()
    {
        var place = Build().Find(0.0, -140.0);

        Assert.True(place.IsEmpty);
        Assert.Null(place.CountryCode);
        Assert.Null(place.City);
    }

    [Fact]
    public void Find_JustOffACoastline_KeepsTheSubdivision()
    {
        // Coastlines are simplified, so a point a few kilometres outside a polygon is still in
        // that state.
        var place = Build().Find(42.0, -69.6);

        Assert.Equal("US-MA", place.SubdivisionCode);
    }

    [Fact]
    public void Find_WellOffshore_KeepsTheCountryButNotTheSubdivision()
    {
        // Roughly 120 km east of the fixture's Massachusetts box: far enough that naming the
        // state is a guess, close enough that naming the country is not.
        var place = Build().Find(42.0, -68.4);

        Assert.Equal("US", place.CountryCode);
        Assert.Equal("United States of America", place.Country);
        Assert.Null(place.SubdivisionCode);
        Assert.Null(place.ShortSubdivision);
    }

    [Fact]
    public void Find_NoCityInRange_StillReturnsTheSubdivision()
    {
        // Deep in the fixture's Texas box with no city within the radius. A label the caller can
        // use, without a city name that would be a lie.
        var place = Build().Find(30.5, -101.0);

        Assert.Null(place.City);
        Assert.Equal("US-TX", place.SubdivisionCode);
    }

    [Fact]
    public void Find_UsesTheMemoOnASecondLookupOfTheSameGridCell()
    {
        var lookup = Build();

        var first = lookup.Find(42.361, -71.058);
        // Same 0.01-degree cell, so this must not reach the database at all. Removing the file is
        // the only way to prove it did not.
        _fixture.Destroy();
        var second = lookup.Find(42.359, -71.062);

        Assert.Equal(first, second);
        Assert.Equal("Boston", second.City);
    }

    [Fact]
    public void Find_BrokenDatabase_ReturnsBlankRatherThanThrowing()
    {
        var lookup = Build();
        _fixture.Destroy();

        var place = lookup.Find(42.36, -71.06);

        Assert.True(place.IsEmpty);
    }

    private SqlitePlaceLookup Build(GeoOptions? options = null) => new(
        _fixture.Open(),
        new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }),
        Options.Create(options ?? new GeoOptions()),
        NullLogger<SqlitePlaceLookup>.Instance);

    public void Dispose() => _fixture.Dispose();
}
