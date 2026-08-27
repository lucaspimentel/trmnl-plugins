using System.Globalization;

namespace TrmnlApi.Endpoints;

/// <summary>
/// What a v2 caller asked for, after the raw query string has been read but before anything has
/// been geocoded. Parsing is pure: it makes no network call and never decides <em>where</em> a
/// place is, only what kind of thing the caller typed.
/// </summary>
/// <remarks>
/// v2 takes a single free-form <c>place</c> parameter. A coordinate pair is one of the things that
/// parameter accepts, so a value is treated as coordinates only when it splits into exactly two
/// tokens that both parse as invariant-culture numbers and both fall in range. Everything else is a
/// name for the geocoder to resolve. Parsing invariant-culture only is what makes the split safe:
/// were a comma accepted as a decimal separator, "42,35" would be both one coordinate and two.
/// </remarks>
public abstract record PlaceInput
{
    private PlaceInput()
    {
    }

    /// <summary>Value for the <c>weather.input_kind</c> span tag.</summary>
    public abstract string Kind { get; }

    /// <summary>The caller typed, or had saved, a usable coordinate pair. No geocoding call needed.</summary>
    public sealed record Coordinates(double Latitude, double Longitude) : PlaceInput
    {
        public override string Kind => "coordinates";
    }

    /// <summary>The caller typed something that has to be resolved by the geocoder.</summary>
    /// <param name="Text">The input with surrounding and repeated whitespace collapsed, otherwise
    /// exactly as typed: an error message has to quote it back, because the plugin cannot read its
    /// own custom field from a template.</param>
    public sealed record Query(string Text) : PlaceInput
    {
        public override string Kind => "place";
    }

    /// <summary>Neither a place nor a coordinate pair was supplied. Maps to <c>place_missing</c>.</summary>
    public sealed record Missing : PlaceInput
    {
        public override string Kind => "missing";
    }

    /// <summary>
    /// Two numbers were supplied but at least one is out of range. Maps to <c>place_invalid</c>.
    /// </summary>
    /// <remarks>
    /// This catches the swapped-order mistake only when the longitude exceeds 90, as in
    /// "-171.05, 42.35". A swap whose longitude is small enough to pass for a latitude, such as
    /// "-71.05, 42.35", is a perfectly valid point in the Southern Ocean and cannot be detected
    /// here or anywhere else: see the coordinate-order hazard in docs/place-input.md.
    /// </remarks>
    public sealed record Invalid : PlaceInput
    {
        public override string Kind => "invalid";
    }

    private static readonly char[] TokenSeparators = [',', ' ', '\t', '\n', '\r', '\f', '\v'];

    /// <summary>
    /// Turns the three raw query parameters into one outcome. <paramref name="place"/> wins whenever
    /// it holds anything but whitespace; the coordinate parameters are the transition affordance for
    /// installs that upgraded with coordinates already saved, so they are consulted only when
    /// <paramref name="place"/> is absent or blank.
    /// </summary>
    public static PlaceInput Parse(string? place, string? latitude, string? longitude)
    {
        if (!string.IsNullOrWhiteSpace(place))
        {
            return ParsePlace(place);
        }

        if (string.IsNullOrWhiteSpace(latitude) && string.IsNullOrWhiteSpace(longitude))
        {
            return new Missing();
        }

        return RequestValidator.TryParseCoordinates(latitude, longitude, out var lat, out var lon)
            && RequestValidator.AreCoordinatesInRange(lat, lon)
                ? new Coordinates(lat, lon)
                : new Invalid();
    }

    private static PlaceInput ParsePlace(string place)
    {
        var tokens = place.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 2
            && TryParseNumber(tokens[0], out var lat)
            && TryParseNumber(tokens[1], out var lon))
        {
            return RequestValidator.AreCoordinatesInRange(lat, lon)
                ? new Coordinates(lat, lon)
                : new Invalid();
        }

        return new Query(string.Join(' ', place.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
    }

    // NumberStyles.Float, not the default: the default adds AllowThousands, and a token that
    // survived the comma split has no business carrying a group separator anyway.
    private static bool TryParseNumber(string token, out double value)
        => double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
