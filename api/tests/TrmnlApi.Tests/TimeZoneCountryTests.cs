using TrmnlApi.Geo;

namespace TrmnlApi.Tests;

public class TimeZoneCountryTests
{
    [Fact]
    public void TheEmbeddedTableLoads()
    {
        // The most important test here. Every other path in this feature fails soft, so a renamed
        // resource, a .csproj typo or a truncated file would not break anything visibly - it would
        // silently stop every user's time zone from counting, and look exactly like nobody having
        // one. Assert the table is populated, not merely that a lookup works.
        Assert.True(TimeZoneCountry.Count > 300, $"only {TimeZoneCountry.Count} zones loaded");
    }

    [Theory]
    // The zone the first real TRMNL request carried, and the case this whole feature was built for.
    [InlineData("America/New_York", "US")]
    [InlineData("America/Los_Angeles", "US")]
    // Berlin is DE alone. In zone1970.tab it reads "DE,DK,NO,SE,SJ", because those countries have
    // kept the same clock since 1970 - true about time, useless about postal codes. This row is
    // what pins the choice of zone.tab; if someone switches files, it fails.
    [InlineData("Europe/Berlin", "DE")]
    [InlineData("Europe/Copenhagen", "DK")]
    [InlineData("Europe/Warsaw", "PL")]
    [InlineData("Asia/Seoul", "KR")]
    // A territory in its own right, which is what lets a Puerto Rico ZIP work from its own zone.
    [InlineData("America/Puerto_Rico", "PR")]
    // A caller relaying a device setting is not a careful source.
    [InlineData("america/new_york", "US")]
    [InlineData("  Europe/Madrid  ", "ES")]
    // Renamed zones, which a Rails app may still be emitting.
    [InlineData("Europe/Kiev", "UA")]
    [InlineData("Asia/Calcutta", "IN")]
    public void Parse_AKnownZone_ReturnsItsCountry(string zone, string expected) =>
        Assert.Equal(expected, TimeZoneCountry.Parse(zone));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    // What a user who never set a time zone reports. It names no country, and falling through to
    // no preference is the wanted answer rather than a gap to be filled.
    [InlineData("UTC")]
    [InlineData("Etc/UTC")]
    [InlineData("Not/AZone")]
    // The value comes off the query string, so the unusable cases are not all well-meaning.
    [InlineData("America/New_York\nSomething else entirely")]
    [InlineData("US")]
    public void Parse_AnUnusableZone_ReturnsNull(string? zone) =>
        Assert.Null(TimeZoneCountry.Parse(zone));

    [Fact]
    public void Parse_AnAbsurdlyLongZone_ReturnsNullWithoutLookingItUp() =>
        Assert.Null(TimeZoneCountry.Parse(new string('a', 5000)));
}
