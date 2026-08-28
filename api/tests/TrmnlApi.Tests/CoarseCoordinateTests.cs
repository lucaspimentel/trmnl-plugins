using System.Globalization;
using TrmnlApi.Observability;
using Xunit;

namespace TrmnlApi.Tests;

public class CoarseCoordinateTests
{
    [Theory]
    // Midpoints on the cache grid, where ToString("F1") formats down but AwayFromZero rounds up.
    // -71.05 is a real Boston longitude, which is how this surfaced.
    [InlineData(-71.05, "-71.1")]
    [InlineData(12.25, "12.3")]
    [InlineData(42.35, "42.4")]
    [InlineData(-0.05, "-0.1")]
    // Raw coordinates whose cache-grid snap crosses an F1 midpoint: a single round gives 42.3.
    [InlineData(42.3451, "42.4")]
    [InlineData(42.3499, "42.4")]
    [InlineData(-71.0451, "-71.1")]
    // Ordinary values, unaffected by either hazard.
    [InlineData(51.5074, "51.5")]
    [InlineData(-0.1278, "-0.1")]
    [InlineData(42.3401, "42.3")]
    public void ToTag_coarsens_raw_coordinates_to_the_telemetry_grid(double raw, string expected) =>
        Assert.Equal(expected, CoarseCoordinate.ToTag(raw));

    [Fact]
    public void ToTag_never_disagrees_with_the_orchestrator_path()
    {
        // Every telemetry surface must agree with the span tags. The endpoint logs raw coordinates
        // while the orchestrator logs cache-grid-snapped ones, so the raw path has to reproduce the
        // snap or the two disagree on ~5% of requests.
        var rng = new Random(42);
        for (var i = 0; i < 200_000; i++)
        {
            var raw = rng.NextDouble() * 180 - 90;
            var snapped = Math.Round(raw, 2, MidpointRounding.AwayFromZero);
            Assert.Equal(CoarseCoordinate.SnappedToTag(snapped), CoarseCoordinate.ToTag(raw));
        }
    }

    [Fact]
    public void Coarsening_is_idempotent()
    {
        // Re-coarsening an already-coarsened value must not shift it, or a tag built from a rounded
        // local would drift from one built from the raw coordinate.
        for (var i = -9000; i <= 9000; i++)
        {
            var coarsened = CoarseCoordinate.Round(i / 100.0);
            Assert.Equal(CoarseCoordinate.Format(coarsened), CoarseCoordinate.Format(CoarseCoordinate.Round(coarsened)));
        }
    }

    [Fact]
    public void Round_stays_within_half_a_cell_plus_the_snap_of_the_input()
    {
        // Guards the PII property: coarsening must not move a coordinate far from where the caller
        // actually is. The bound is half a telemetry cell (0.05) plus half a cache cell (0.005),
        // because the snap to the cache grid happens first and can itself push across an F1
        // midpoint - that is the double-rounding behaviour ToTag deliberately reproduces.
        const double maxShift = 0.055 + 1e-9;
        var rng = new Random(7);
        for (var i = 0; i < 100_000; i++)
        {
            var raw = rng.NextDouble() * 180 - 90;
            var coarsened = CoarseCoordinate.Round(raw);
            Assert.True(
                Math.Abs(coarsened - raw) <= maxShift,
                $"{coarsened} moved more than {maxShift} from {raw}");
        }
    }

    [Fact]
    public void Format_is_culture_invariant()
    {
        // A comma decimal separator would corrupt the coordinate tags, which are read back as
        // strings and grouped on, so the format must stay culture-invariant.
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("42.4", CoarseCoordinate.ToTag(42.35));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(42.36, -71.06, "42.4,-71.1")]
    [InlineData(0, 0, "0.0,0.0")]
    [InlineData(-33.87, 151.21, "-33.9,151.2")]
    public void FormatPair_joins_the_two_coarsened_values(double latitude, double longitude, string expected)
    {
        Assert.Equal(expected, CoarseCoordinate.FormatPair(
            CoarseCoordinate.Round(latitude),
            CoarseCoordinate.Round(longitude)));
    }

    [Fact]
    public void FormatPair_agrees_with_the_separate_tags()
    {
        // weather.coord and weather.latitude/longitude sit on the same span, so a request that
        // disagreed between them would make the pair untrustworthy for grouping.
        var rng = new Random(11);
        for (var i = 0; i < 10_000; i++)
        {
            var latitude = CoarseCoordinate.Round(rng.NextDouble() * 180 - 90);
            var longitude = CoarseCoordinate.Round(rng.NextDouble() * 360 - 180);
            var pair = CoarseCoordinate.FormatPair(latitude, longitude);
            Assert.Equal(
                $"{CoarseCoordinate.Format(latitude)},{CoarseCoordinate.Format(longitude)}",
                pair);
        }
    }

    [Fact]
    public void FormatPair_is_culture_invariant()
    {
        // Under a comma-decimal culture an interpolated pair would read "42,4,-71,1", which cannot
        // be split back into two coordinates and would silently become a different facet value.
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("42.4,-71.1", CoarseCoordinate.FormatPair(
                CoarseCoordinate.Round(42.36),
                CoarseCoordinate.Round(-71.06)));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }
}
