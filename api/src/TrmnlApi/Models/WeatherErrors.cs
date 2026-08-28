namespace TrmnlApi.Models;

/// <summary>
/// Every <see cref="ErrorInfo"/> v2 can return, in one place.
/// </summary>
/// <remarks>
/// These strings are rendered verbatim on a screen, so they are worth reading together rather than
/// finding one at a time at the call sites that produce them. Keeping them here also lets the test
/// scenarios in <c>TestScenarios</c> hand back the real error rather than a lookalike: a message
/// that only appears in the test path would be the one thing a preview could not check.
/// </remarks>
public static class WeatherErrors
{
    public static ErrorInfo PlaceMissing { get; } = new(
        ErrorCodes.PlaceMissing,
        "No location is set.",
        "Open this plugin's settings and enter a city, postal code, or coordinates.");

    public static ErrorInfo WeatherUnavailable { get; } = new(
        ErrorCodes.WeatherUnavailable,
        "Weather is temporarily unavailable.",
        "This usually clears on its own by the next refresh.");

    /// <param name="quoted">The caller's input, already quoted and clipped.</param>
    public static ErrorInfo PlaceInvalid(string quoted) => new(
        ErrorCodes.PlaceInvalid,
        $"{quoted} is not a location.",
        "If you pasted coordinates, check the order: latitude first, then longitude.");

    /// <param name="quoted">The caller's input, already quoted and clipped.</param>
    public static ErrorInfo PlaceNotFound(string quoted) => new(
        ErrorCodes.PlaceNotFound,
        $"No place matches {quoted}.",
        "Try adding a state or country, as in Portland, ME.");

    public static ErrorInfo RequestInvalid(string message) => new(
        ErrorCodes.RequestInvalid,
        message,
        "This is a plugin configuration problem, not something the screen's settings can fix.");
}
