namespace TrmnlApi.Geo;

/// <summary>
/// What the caller told us about where they are, and which of the things they told us was used.
/// </summary>
/// <remarks>
/// The layering lives here rather than being spelled out at each call site, so that the log line
/// naming the source cannot drift away from the ranking that actually happened. Both the geocoder
/// and the endpoint ask this one question.
/// <para>
/// A hint is always a tie-break and never a filter: every level below only reorders matches that
/// were already valid, and none may turn a working input into a miss.
/// </para>
/// </remarks>
public static class CountryHint
{
    /// <summary>The Country dropdown. What they chose beats what their clock implies.</summary>
    public const string Declared = "declared";

    /// <summary>The caller's IANA time zone, which their device supplies without being asked.</summary>
    public const string TimeZone = "tz";

    /// <summary>Neither. A postal code then falls back to <see cref="HomeRegion"/>.</summary>
    public const string None = "none";

    /// <summary>
    /// The countries to prefer and the name of the signal they came from. The set is null when
    /// nothing usable was supplied.
    /// </summary>
    public static (IReadOnlySet<string>? Countries, string Source) Resolve(
        string? declaredCountry,
        string? timeZone)
    {
        // Anything unreadable - a blank, the dropdown's "Auto" - means no preference rather than
        // an error, because a setting nobody can see is a bad reason to refuse a forecast.
        // CountryPreference also accepts the dropdown's slugified label, which is what actually
        // arrives from the plugin.
        if (CountryPreference.Parse(declaredCountry) is { } declared)
        {
            return (PostalJurisdictions.Accepting(declared), Declared);
        }

        if (TimeZoneCountry.Parse(timeZone) is { } inferred)
        {
            return (PostalJurisdictions.Accepting(inferred), TimeZone);
        }

        return (null, None);
    }
}
