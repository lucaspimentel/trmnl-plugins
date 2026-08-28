using TrmnlApi.Models;

namespace TrmnlApi.Endpoints;

/// <summary>What a test scenario asks the endpoint to do.</summary>
public enum TestScenarioKind
{
    /// <summary>Return <see cref="TestScenario.Error"/> as an ordinary v2 error response.</summary>
    Error,

    /// <summary>Return 499, as though the device had hung up mid-request.</summary>
    ClientGone,

    /// <summary>Throw, so the response comes from the real unhandled-exception path.</summary>
    ServerError,

    /// <summary>Return v1's 502, which is what a hard upstream failure looks like there.</summary>
    UpstreamFailure,

    /// <summary>Fetch a real forecast, then report it as a stale cache hit.</summary>
    StaleForecast,

    /// <summary>Fetch a real forecast, then fill it with the randomized precipitation from v1's <c>fake</c>.</summary>
    FakePrecipitation
}

/// <param name="Name">The word after <c>test:</c> that selected this scenario.</param>
/// <param name="Error">Set only when <paramref name="Kind"/> is <see cref="TestScenarioKind.Error"/>.</param>
public record TestScenario(string Name, TestScenarioKind Kind, ErrorInfo? Error = null);

/// <summary>
/// Lets a caller ask v2 for a specific result by putting <c>test:&lt;name&gt;</c> in <c>place</c>.
/// </summary>
/// <remarks>
/// Every failure v2 can show on a screen is an HTTP 200 carrying an <c>error</c> object, so all of
/// them are renderable - but most were previously only reachable by breaking something. Two could
/// not be produced on purpose at all: <c>weather_unavailable</c> needs every provider down, and a
/// stale serve needs a cache entry to have aged out.
/// <para>
/// The sentinel rides in <c>place</c> rather than a query parameter of its own because <c>place</c>
/// is a custom field the plugin already forwards verbatim. Selecting a scenario is therefore typing
/// into the plugin's settings, with no edit to <c>polling_url</c>, no push, and no revert - which is
/// the whole point, since these are meant to be stepped through one at a time while watching a
/// screen. A colon cannot appear in a place name, so nothing a real user types collides.
/// </para>
/// <para>
/// v1 has no <c>place</c> parameter and keeps its own <c>fake</c> flag instead. Forked plugins still
/// poll v1, so it stays exactly as it is; only <see cref="FakePrecipitation"/> is shared, so the two
/// cannot drift.
/// </para>
/// </remarks>
public static class TestScenarios
{
    /// <summary>Marks a place value as a scenario request rather than somewhere on Earth.</summary>
    public const string Prefix = "test:";

    /// <summary>Span tag naming the scenario, so a test poll is filterable and never mistaken for real traffic.</summary>
    public const string SpanTag = "weather.test_scenario";

    /// <summary>
    /// Where the forecast-shaped scenarios pretend to be. Fixed rather than taken from the caller so
    /// the screen looks the same every time, and so the scenario cannot be the thing that fails.
    /// </summary>
    public const string Location = "42.36,-71.06";

    private static readonly Dictionary<string, TestScenario> All = new[]
    {
        ErrorScenario(ErrorCodes.PlaceMissing, WeatherErrors.PlaceMissing),
        ErrorScenario(ErrorCodes.PlaceInvalid, WeatherErrors.PlaceInvalid("\"171.05, 42.35\"")),
        ErrorScenario(ErrorCodes.PlaceNotFound, WeatherErrors.PlaceNotFound("\"Nowherefordshire\"")),
        ErrorScenario(ErrorCodes.RequestInvalid, WeatherErrors.RequestInvalid(RequestValidator.UnitsMessage)),
        ErrorScenario(ErrorCodes.WeatherUnavailable, WeatherErrors.WeatherUnavailable),
        new TestScenario("stale", TestScenarioKind.StaleForecast),
        new TestScenario("precipitation", TestScenarioKind.FakePrecipitation),
        new TestScenario("499", TestScenarioKind.ClientGone),
        new TestScenario("500", TestScenarioKind.ServerError),
        new TestScenario("502", TestScenarioKind.UpstreamFailure)
    }.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The scenario names, for the error shown when someone asks for one that does not exist.</summary>
    public static string Names { get; } = string.Join(", ", All.Keys.Order(StringComparer.Ordinal));

    /// <summary>
    /// Returns the scenario <paramref name="place"/> asks for, or null if it is an ordinary place.
    /// An unrecognized name after the prefix is itself a scenario - a <c>request_invalid</c> naming
    /// the ones that exist - because silently geocoding "test:stale" would be a confusing way to
    /// learn about a typo.
    /// </summary>
    public static TestScenario? Parse(string? place)
    {
        if (place is null || !place.TrimStart().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = place.Trim()[Prefix.Length..].Trim();

        return All.TryGetValue(name, out var scenario)
            ? scenario
            : new TestScenario(
                name,
                TestScenarioKind.Error,
                WeatherErrors.RequestInvalid($"No test scenario named '{name}'. Try one of: {Names}."));
    }

    /// <summary>
    /// Replaces every precipitation probability with a random one, and flattens the last two daily
    /// highs toward their lows, so a forecast exercises the parts of a layout that a calm week
    /// leaves empty.
    /// </summary>
    /// <remarks>
    /// Shared with v1's <c>fake</c> query parameter, whose output this must keep matching.
    /// </remarks>
    public static WeatherResponse FakePrecipitation(WeatherResponse response)
    {
        var hourly = response.Hourly.Entries
            .Select(e => e with { PrecipitationProbability = Random.Shared.Next(0, 100) })
            .ToList();

        var daily = response.Daily.Entries
            .Select(e => e with { PrecipitationProbability = Random.Shared.Next(0, 100) })
            .ToList();

        // A real provider always returns a week or so. A caller who trimmed to fewer than two days
        // gets the precipitation without the flattening rather than an exception.
        if (daily.Count >= 2)
        {
            var last = daily[^1];
            daily[^1] = last with { High = last.Low };

            var secondLast = daily[^2];
            daily[^2] = secondLast with { High = secondLast.Low + 2 };
        }

        return response with
        {
            Hourly = new HourlyForecast(hourly),
            Daily = new DailyForecast(daily)
        };
    }

    private static TestScenario ErrorScenario(string code, ErrorInfo error) =>
        new(code, TestScenarioKind.Error, error);
}
