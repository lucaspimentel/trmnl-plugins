namespace TrmnlApi.Models;

/// <summary>
/// What v2 returns instead of a forecast. Always with HTTP 200: TRMNL counts polling failures and
/// eventually stops refreshing a plugin altogether until someone resets it by hand, and most of
/// these failures are permanent by construction, so a status code would walk a mistyped place into
/// a dead plugin. See docs/place-input.md for the full argument.
/// </summary>
public record ErrorResponse(ErrorInfo Error);

/// <param name="Code">
/// Stable, and the thing a template branches on. Adding a code is a compatible change; changing
/// the meaning of one is not, because a forked plugin's conditionals outlive this codebase.
/// </param>
/// <param name="Message">Rendered on the screen. Quotes back what the user typed where there is
/// anything to quote, because nothing else in an error response carries their input.</param>
/// <param name="Hint">What to do about it. Shown where the layout has room for a second line.</param>
public record ErrorInfo(string Code, string Message, string Hint);

/// <summary>The <see cref="ErrorInfo.Code"/> values. Kept together so the set stays surveyable.</summary>
public static class ErrorCodes
{
    /// <summary>Neither a place nor a coordinate pair was supplied.</summary>
    public const string PlaceMissing = "place_missing";

    /// <summary>Two numbers were supplied but they are not a point on Earth.</summary>
    public const string PlaceInvalid = "place_invalid";

    /// <summary>The geocoder matched nothing, or nothing that is a populated place.</summary>
    public const string PlaceNotFound = "place_not_found";

    /// <summary>
    /// A parameter the plugin itself supplies, rather than the user, was rejected. Reaching this
    /// means the plugin is misconfigured, which is exactly why it is worth saying on screen.
    /// </summary>
    public const string RequestInvalid = "request_invalid";

    /// <summary>
    /// Temporary, and the only code here that is. Covers both every weather provider failing with
    /// no usable cache entry left and the geocoder being unreachable: the second is deliberately
    /// not <see cref="PlaceNotFound"/>, which would tell someone to retype an input that was fine.
    /// </summary>
    public const string WeatherUnavailable = "weather_unavailable";
}
