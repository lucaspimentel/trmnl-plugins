namespace TrmnlApi.GeoDataBuilder;

/// <summary>
/// Territories stored with their outline and no label, because naming them would take a side.
/// </summary>
/// <remarks>
/// Natural Earth files Crimea and Sevastopol under Russia, carrying Ukraine's own ISO 3166-2 codes
/// while naming the country Russia. Whichever of those two answers a weather screen printed, it
/// would be making a claim it has no business making, and the alternative on offer is only to
/// believe a different map.
/// <para>
/// Deleting the features outright is not the same as declining to answer. The lookup falls through
/// to the nearest neighbouring polygon when nothing contains the point, so a deleted Crimea would
/// quietly hand Kerch to Krasnodar Krai, four kilometres across the strait, and the rest of the
/// peninsula to Kherson - the same claim as before, arrived at by accident and wrong about the
/// subdivision as well. So the geometry stays and the labels go. A point here matches its own
/// outline, and the screen shows the nearest city with no state and no country under it.
/// </para>
/// <para>
/// Keyed by Natural Earth's <c>iso_3166_2</c> value, which is the one attribute on these two
/// features that is not itself in dispute.
/// </para>
/// </remarks>
public static class ContestedTerritories
{
    private static readonly HashSet<string> Unattributed = new(StringComparer.OrdinalIgnoreCase)
    {
        "UA-43", // Crimea
        "UA-40"  // Sevastopol
    };

    /// <summary>Whether this feature keeps its geometry but loses every code and name.</summary>
    public static bool IsUnattributed(string isoSubdivision) => Unattributed.Contains(isoSubdivision);
}
