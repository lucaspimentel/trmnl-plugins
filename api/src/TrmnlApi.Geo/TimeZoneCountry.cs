using System.Reflection;

namespace TrmnlApi.Geo;

/// <summary>
/// The country a time zone lies in, read from the IANA time zone database.
/// </summary>
/// <remarks>
/// A bare postal code is a real code in dozens of countries and nothing in the string says which
/// one the caller is in: <c>02180</c> is Stoneham, Massachusetts, and also Helsinki, Vilnius,
/// Warsaw, Seoul and a village in Peru. Ranking those by population answers Seoul for everyone.
/// The caller's time zone answers it for each of them separately, and costs them nothing to set,
/// because their device already knows.
/// <para>
/// The table is <b>zone.tab</b>, not <c>zone1970.tab</c>, and that is deliberate rather than
/// careless. <c>zone1970.tab</c> lists the countries that have <i>agreed on civil time</i> since
/// 1970, so <c>Europe/Berlin</c> reads <c>DE,DK,NO,SE,SJ</c> and <c>Asia/Dubai</c> reads
/// <c>AE,OM,RE,SC,TF</c> - a true statement about clocks and a useless one about postal codes.
/// <c>zone.tab</c> gives exactly one country per zone and lists <c>Europe/Copenhagen</c> and
/// <c>Europe/Oslo</c> in their own right, which is the question being asked here. Upstream marks
/// it deprecated for general use; <b>do not "upgrade" it to the 1970 file</b>, which would silently
/// widen every European answer to its whole time zone.
/// </para>
/// <para>
/// The file omits deprecated zone <i>links</i>: <c>Europe/Kyiv</c> and <c>Asia/Kolkata</c> are
/// present, their old names <c>Europe/Kiev</c> and <c>Asia/Calcutta</c> are not. An unrecognised
/// zone yields null and therefore no preference, which is the same as not having been told - never
/// an error, and never a worse answer than before.
/// </para>
/// </remarks>
public static class TimeZoneCountry
{
    /// <summary>Longest zone name worth looking up; the longest real one is under 32.</summary>
    private const int MaxZoneLength = 64;

    /// <summary>
    /// Old zone names that upstream keeps only as links, and so are absent from the table.
    /// </summary>
    /// <remarks>
    /// TRMNL is a Rails application and this value comes from <c>ActiveSupport::TimeZone</c>, whose
    /// identifiers have lagged renames before. The cost of being wrong here is silent - the zone
    /// simply stops being recognised and the user quietly drops a tier - so the few renames that
    /// have actually happened in living memory are worth spelling out.
    /// </remarks>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Europe/Kiev"] = "Europe/Kyiv",                            // renamed 2022
        ["Asia/Calcutta"] = "Asia/Kolkata",                         // renamed 1993, still emitted
        ["Asia/Saigon"] = "Asia/Ho_Chi_Minh",
        ["Asia/Rangoon"] = "Asia/Yangon",
        ["Asia/Katmandu"] = "Asia/Kathmandu",
        ["Asia/Istanbul"] = "Europe/Istanbul",
        ["America/Godthab"] = "America/Nuuk",                       // renamed 2020
        ["America/Buenos_Aires"] = "America/Argentina/Buenos_Aires",
        ["Atlantic/Faeroe"] = "Atlantic/Faroe",
        ["Europe/Nicosia"] = "Asia/Nicosia"
    };

    private static readonly Lazy<Dictionary<string, string>> Zones = new(Load);

    /// <summary>
    /// The ISO 3166-1 alpha-2 the zone lies in, or null when the zone is missing or unrecognised.
    /// </summary>
    /// <remarks>
    /// <c>UTC</c> and <c>Etc/UTC</c> deliberately return null. They are what a user who has never
    /// set a time zone reports, they name no country, and no preference is the honest answer.
    /// </remarks>
    public static string? Parse(string? ianaZone)
    {
        // The value arrives on the query string, so it is filtered rather than trusted before it
        // is used to look anything up.
        if (string.IsNullOrWhiteSpace(ianaZone) || ianaZone.Length > MaxZoneLength)
        {
            return null;
        }

        var zone = ianaZone.Trim();
        if (Aliases.TryGetValue(zone, out var current))
        {
            zone = current;
        }

        return Zones.Value.TryGetValue(zone, out var country) ? country : null;
    }

    /// <summary>How many zones the embedded table yielded. For the test that it loaded at all.</summary>
    internal static int Count => Zones.Value.Count;

    private static Dictionary<string, string> Load()
    {
        // Zone names are case-sensitive upstream, but a caller relaying a device setting is not a
        // careful source, and no two zones differ only by case.
        var zones = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var stream = typeof(TimeZoneCountry).GetTypeInfo().Assembly
            .GetManifestResourceStream("TrmnlApi.Geo.zone.tab");
        if (stream is null)
        {
            // The resource is compiled in, so this cannot happen without the build being wrong.
            // Still not worth throwing for: the caller loses a tie-break, not a forecast.
            return zones;
        }

        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            // country <tab> coordinates <tab> zone [<tab> comments]
            var fields = line.Split('\t');
            if (fields.Length < 3 || fields[0].Length != 2 || fields[2].Length == 0)
            {
                continue;
            }

            zones[fields[2]] = fields[0].ToUpperInvariant();
        }

        return zones;
    }
}
