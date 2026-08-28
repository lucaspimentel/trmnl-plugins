namespace TrmnlApi.Geo;

/// <summary>
/// What the bundled dataset knows about a coordinate pair. Every field is optional, because a
/// point in the middle of the Pacific genuinely has no answer and inventing one is worse than
/// showing nothing.
/// </summary>
/// <param name="City">Nearest populated place, within a tight radius. Null when the closest one is
/// too far away to be an honest label for where the forecast is.</param>
/// <param name="SubdivisionCode">Full ISO 3166-2 code, for example <c>US-MA</c> or <c>FR-59</c>.
/// This is the telemetry form: numeric is correct here and unreadable on screen.</param>
/// <param name="SubdivisionName">Subdivision display name, for example <c>Massachusetts</c> or
/// <c>Nord</c>.</param>
/// <param name="CountryCode">ISO 3166-1 alpha-2.</param>
/// <param name="Country">Country display name.</param>
public readonly record struct GeoPlace(
    string? City,
    string? SubdivisionCode,
    string? SubdivisionName,
    string? CountryCode,
    string? Country)
{
    /// <summary>The answer when the dataset has nothing, the lookup failed, or it ran out of time.</summary>
    public static GeoPlace Empty => default;

    public bool IsEmpty => City is null && SubdivisionCode is null && CountryCode is null;

    /// <summary>
    /// The short subdivision label to put on screen. See <see cref="SubdivisionLabel"/> for why
    /// this is not simply the code.
    /// </summary>
    public string? ShortSubdivision => SubdivisionLabel.Short(SubdivisionCode, SubdivisionName);
}
