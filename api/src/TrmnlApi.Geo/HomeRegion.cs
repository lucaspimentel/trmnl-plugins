namespace TrmnlApi.Geo;

/// <summary>
/// Where this plugin's users mostly are, used to settle an ambiguous postal code when there is
/// nothing better to go on.
/// </summary>
/// <remarks>
/// This is the weakest signal in the chain and the only one that is a guess about the caller
/// rather than a fact about them. It applies only when the request carries neither a Country
/// setting nor a usable time zone, which today means a fork or an install predating both.
/// <para>
/// It exists because the alternative is measurably worse. Ranking colliding codes by population
/// alone answers with a country outside this set <b>half the time</b> on a sample of shared US
/// ZIPs, because a five-digit code near a dense metropolis outranks the same code anywhere else.
/// Narrowing to this set first cannot make a US or EU caller's answer worse - the population
/// winner, if it was already in the set, is still the winner within it - and it cannot turn any
/// caller's valid code into a miss, because an empty intersection keeps every candidate.
/// </para>
/// <para>
/// The cost is real and falls on everyone else: a caller in Seoul or Mexico City with no time zone
/// gets a US or EU answer for their own postal code. That is a deliberate trade against the
/// measured distribution of users, not a claim about places, which is why the membership below is
/// listed once with this reason attached rather than defended country by country.
/// </para>
/// <para>
/// Postal codes only. Applied to city names it would flip bare <c>Santiago</c> from Chile to Spain,
/// and names do not need it: a name usually has one dominant bearer, which is exactly what
/// population ranking finds.
/// </para>
/// </remarks>
public static class HomeRegion
{
    // The European Union, the rest of the EEA, Switzerland and the United Kingdom, plus the
    // United States. Territories are not listed: PostalJurisdictions adds each member's own
    // postal territories below, which is how PR, GP, AX and the rest get in.
    private static readonly string[] Members =
    [
        "AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR", "DE", "GR", "HU", "IE", "IT",
        "LV", "LT", "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE",
        "IS", "LI", "NO", "CH", "GB",
        "US"
    ];

    /// <summary>The countries a postal code may come from before population decides.</summary>
    public static IReadOnlySet<string> Countries { get; } = Build();

    private static HashSet<string> Build()
    {
        var countries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in Members)
        {
            countries.UnionWith(PostalJurisdictions.Accepting(member));
        }

        return countries;
    }
}
