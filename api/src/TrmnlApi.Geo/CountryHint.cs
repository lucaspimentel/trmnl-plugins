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
/// <para>
/// The levels are tried in order and a level whose set intersects no candidate is <b>skipped</b>
/// rather than consumed. A declared country that matches nothing used to end the search here, so
/// the caller's time zone was never consulted and ranking fell through to population - which is
/// precisely what the time zone was added to prevent. See <see cref="Candidates"/>.
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
    /// Every signal the caller supplied, strongest first, so a level that turns out to match
    /// nothing can be skipped for the next one. Empty when nothing usable was supplied.
    /// </summary>
    /// <remarks>
    /// Only the caller's own signals appear here. The postal-only floors - a ZIP+4's implied
    /// United States, and <see cref="HomeRegion"/> - are appended by the lookup that knows the
    /// input was postal, and report <see cref="None"/>, because they are facts about the input or
    /// guesses about the audience rather than something the caller told us.
    /// </remarks>
    public static IReadOnlyList<(IReadOnlySet<string> Countries, string Source)> Candidates(
        string? declaredCountry,
        string? timeZone)
    {
        var levels = new List<(IReadOnlySet<string>, string)>(2);

        // Anything unreadable - a blank, the dropdown's "Auto" - means no preference rather than
        // an error, because a setting nobody can see is a bad reason to refuse a forecast.
        // CountryPreference also accepts the dropdown's slugified label, which is what actually
        // arrives from the plugin.
        if (CountryPreference.Parse(declaredCountry) is { } declared)
        {
            levels.Add((PostalJurisdictions.Accepting(declared), Declared));
        }

        if (TimeZoneCountry.Parse(timeZone) is { } inferred)
        {
            levels.Add((PostalJurisdictions.Accepting(inferred), TimeZone));
        }

        return levels;
    }

    /// <summary>
    /// The strongest signal the caller supplied and the name of it. The set is null when nothing
    /// usable was supplied.
    /// </summary>
    /// <remarks>
    /// This answers "what did they tell us", which is all the coordinate paths can report: nothing
    /// was ranked, so no level can have matched or missed. A lookup that ranks candidates must use
    /// <see cref="Candidates"/> and report the level that actually settled it.
    /// </remarks>
    public static (IReadOnlySet<string>? Countries, string Source) Resolve(
        string? declaredCountry,
        string? timeZone)
    {
        var levels = Candidates(declaredCountry, timeZone);
        return levels.Count == 0 ? (null, None) : (levels[0].Countries, levels[0].Source);
    }
}
