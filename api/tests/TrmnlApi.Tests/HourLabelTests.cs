using TrmnlApi.Mappings;

namespace TrmnlApi.Tests;

public class HourLabelTests
{
    [Theory]
    [InlineData("2026-02-25T00:00", "12am")]
    [InlineData("2026-02-25T09:00", "9am")]
    [InlineData("2026-02-25T12:00", "12pm")]
    [InlineData("2026-02-25T15:00", "3pm")]
    [InlineData("2026-02-25T23:00", "11pm")]
    public void Format_Default_Is12Hour(string iso, string expected)
        => Assert.Equal(expected, HourLabel.Format(iso));

    [Theory]
    [InlineData("2026-02-25T00:00", "00:00")]
    [InlineData("2026-02-25T09:00", "09:00")]
    [InlineData("2026-02-25T12:00", "12:00")]
    [InlineData("2026-02-25T15:00", "15:00")]
    [InlineData("2026-02-25T23:00", "23:00")]
    public void Format_24Hour_Returns24HourLabel(string iso, string expected)
        => Assert.Equal(expected, HourLabel.Format(iso, use24Hour: true));
}
