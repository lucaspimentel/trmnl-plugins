namespace TrmnlApi.Geo;

/// <summary>
/// Territories whose postal codes are administered as part of another country's system, and which
/// GeoNames nevertheless files under their own alpha-2.
/// </summary>
/// <remarks>
/// This exists because of a real wrong answer. <c>00784</c> is a Puerto Rico ZIP, and GeoNames
/// files it under <c>PR</c>. Someone in Caguas who sets their country to the United States - which
/// is where they live, and whose postal system issued that code - matched no row at all, so the
/// ranking fell through to population and answered <b>Warsaw</b>, which also has a 00784. Declaring
/// your country correctly and getting another continent is worse than not declaring it.
/// <para>
/// The relationship is one-directional. Declaring the sovereign accepts its territories, because a
/// resident of one may reasonably name either. Declaring the territory keeps only the territory,
/// because someone who picked <c>PR</c> specifically has been more precise, not less.
/// </para>
/// <para>
/// Membership here is about **which postal system issues the code**, not about sovereignty in any
/// broader sense, and the list is deliberately limited to territories that share or are numbered
/// within the sovereign's postal range. Places with a genuinely separate postal administration are
/// left out even where a political claim exists, because including them would make the lookup take
/// a position it has no business taking and would not help anyone find their weather.
/// </para>
/// </remarks>
public static class PostalJurisdictions
{
    private static readonly Dictionary<string, string[]> Dependents = new(StringComparer.OrdinalIgnoreCase)
    {
        // ZIP space: PR 006-009, VI 008, GU/MP/AS 967-969.
        ["US"] = ["PR", "VI", "GU", "AS", "MP"],
        // The French 97xxx and 98xxx ranges.
        ["FR"] = ["GP", "MQ", "GF", "RE", "YT", "PM", "BL", "MF", "NC", "PF", "WF"],
        ["NL"] = ["AW", "CW", "SX", "BQ"],
        ["DK"] = ["FO", "GL"],
        ["FI"] = ["AX"],
        ["NO"] = ["SJ"],
        ["AU"] = ["CC", "CX", "NF"],
        ["GB"] = ["GG", "JE", "IM"],
        ["NZ"] = ["CK", "NU", "TK"]
    };

    /// <summary>
    /// The set of country codes a declared country should accept: itself, plus any territory whose
    /// postal codes its own system issues.
    /// </summary>
    public static IReadOnlySet<string> Accepting(string alpha2)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { alpha2 };
        if (Dependents.TryGetValue(alpha2, out var dependents))
        {
            set.UnionWith(dependents);
        }

        return set;
    }
}
