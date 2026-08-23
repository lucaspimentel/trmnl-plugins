using TrmnlApi.Models;
using TrmnlApi.Services;

namespace TrmnlApi.Tests;

public class ForecastTrimmerTests
{
    private static WeatherResponse Build(int hourlyCount, int dailyCount)
    {
        var hourly = Enumerable.Range(0, hourlyCount)
            .Select(i => new HourlyEntry($"2026-02-25T{i:D2}:00", $"{i}h", 40, 0, "wi-day-sunny", true))
            .ToList();

        var daily = Enumerable.Range(0, dailyCount)
            .Select(i => new DailyEntry($"2026-02-{25 + i:D2}", 50, 30, "Clear", "wi-day-sunny", 0, "06:30", "17:35"))
            .ToList();

        var current = new CurrentConditions("2026-02-25T14:00", 40, 38, 50, 0, "Clear", "wi-day-sunny", 5, 270, "W", true);
        return new WeatherResponse(current, new HourlyForecast(hourly), new DailyForecast(daily));
    }

    [Theory]
    [InlineData(12, 3)]
    [InlineData(1, 1)]
    [InlineData(24, 13)]
    public void Trim_ReducesBothCollectionsToRequestedCounts(int hours, int days)
    {
        var result = ForecastTrimmer.Trim(Build(25, 14), hours, days);

        Assert.Equal(hours, result.Hourly.Entries.Count);
        Assert.Equal(days, result.Daily.Entries.Count);
    }

    [Fact]
    public void Trim_KeepsTheEarliestEntries()
    {
        var result = ForecastTrimmer.Trim(Build(25, 14), hours: 2, days: 3);

        Assert.Equal("0h", result.Hourly.Entries[0].Label);
        Assert.Equal("1h", result.Hourly.Entries[^1].Label);
        Assert.Equal("2026-02-25", result.Daily.Entries[0].Date);
        Assert.Equal("2026-02-27", result.Daily.Entries[^1].Date);
    }

    [Fact]
    public void Trim_ReturnsSameInstanceWhenNothingToRemove()
    {
        var response = Build(25, 14);

        Assert.Same(response, ForecastTrimmer.Trim(response, hours: 25, days: 14));
    }

    // A request may ask for more than the provider supplied: Pirate Weather caps its daily
    // block around 8 entries no matter what `days` was requested.
    [Fact]
    public void Trim_AsksForMoreThanAvailable_ReturnsWhatIsThere()
    {
        var result = ForecastTrimmer.Trim(Build(25, 8), hours: 25, days: 14);

        Assert.Equal(8, result.Daily.Entries.Count);
        Assert.Equal(25, result.Hourly.Entries.Count);
    }

    [Fact]
    public void Trim_LeavesCurrentConditionsUntouched()
    {
        var response = Build(25, 14);
        var result = ForecastTrimmer.Trim(response, hours: 3, days: 2);

        Assert.Equal(response.Current, result.Current);
    }
}
