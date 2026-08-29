using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TrmnlApi.Geo;

/// <summary>
/// Forward geocoding over the bundled dataset: exact name or alias match, comma qualifiers
/// filtered against country code, country name, subdivision code and subdivision name, ranked by
/// population.
/// </summary>
/// <remarks>
/// Exact matching only. The vendor geocoder forgives some misspellings and this does not, which is
/// one of the reasons it stays wired up as the fallback rather than being deleted the day this
/// ships. See the geocoder retirement note in docs/geographic-telemetry.md.
/// </remarks>
public sealed class SqliteLocalGeocoder : ILocalGeocoder
{
    /// <summary>
    /// How close a city has to be to a postal centroid to speak for it, in kilometres.
    /// </summary>
    /// <remarks>
    /// This is the tiebreak that makes <c>75001</c> resolve to Paris rather than to Addison,
    /// Texas: rank the candidate codes by the largest population sitting on top of them. The
    /// radius has to be small for that to mean anything. At 60 km, Addison borrows Dallas and the
    /// comparison stops being about the postal code at all.
    /// </remarks>
    private const double PostalRankRadiusKm = 15;

    private readonly GeoDatabase _database;
    private readonly GeoOptions _options;
    private readonly ILogger<SqliteLocalGeocoder> _logger;

    public SqliteLocalGeocoder(GeoDatabase database, IOptions<GeoOptions> options, ILogger<SqliteLocalGeocoder> logger)
    {
        _database = database;
        _options = options.Value;
        _logger = logger;
    }

    public GeoMatch? Find(string text, string? preferredCountry = null)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > _options.MaxQueryLength)
        {
            return null;
        }

        try
        {
            // Everything before the first comma is the place; every later segment is a qualifier
            // the answer has to satisfy, so "Springfield, IL, US" narrows twice.
            var segments = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return null;
            }

            var subject = segments[0];
            var qualifiers = segments.Skip(1).ToArray();

            using var connection = _database.Connect();
            var resolved = qualifiers.Select(q => ResolveQualifier(connection, q)).ToArray();

            // Only a well-formed alpha-2 is honoured. Anything else - a blank, an "Auto", a
            // dropdown label that arrived unsplit - means no preference rather than an error,
            // because a setting nobody can see is a bad reason to refuse a forecast.
            var preference = preferredCountry is { Length: 2 } code && code.All(char.IsAsciiLetter)
                ? PostalJurisdictions.Accepting(code.ToUpperInvariant())
                : null;

            if (GeoText.LooksPostal(subject))
            {
                var postal = FindPostal(connection, subject, resolved, preference);
                if (postal is not null)
                {
                    return postal;
                }
            }

            return FindCity(connection, subject, resolved, preference);
        }
        catch (Exception ex)
        {
            // A miss, not an error: the vendor geocoder is the fallback for both, so a broken
            // dataset costs money rather than correctness.
            _logger.LogWarning(ex, "Local geocoding failed; falling back to the vendor geocoder.");
            return null;
        }
    }

    /// <summary>Everything one typed qualifier could have meant.</summary>
    private sealed record Qualifier(string Raw, HashSet<string> CountryCodes, HashSet<(string Country, string Code)> Subdivisions);

    private static Qualifier ResolveQualifier(SqliteConnection connection, string text)
    {
        var normalized = GeoText.Normalize(text);
        var countries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var subdivisions = new HashSet<(string, string)>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT iso_a2 FROM country WHERE normalized_name = $q";
            command.Parameters.AddWithValue("$q", normalized);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                countries.Add(reader.GetString(0));
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT country, code FROM admin1_name WHERE normalized_name = $q";
            command.Parameters.AddWithValue("$q", normalized);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                subdivisions.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        return new Qualifier(text.Trim(), countries, subdivisions);
    }

    private sealed record Candidate(string Name, string Country, string? Admin1, double Lat, double Lon, long Population);

    private static bool Matches(Qualifier qualifier, Candidate candidate)
    {
        if (qualifier.CountryCodes.Contains(candidate.Country))
        {
            return true;
        }

        if (candidate.Admin1 is not null && qualifier.Subdivisions.Contains((candidate.Country, candidate.Admin1)))
        {
            return true;
        }

        // The raw form covers the two-letter codes nobody spells out: "Munich, DE" against the
        // country column, "Portland, ME" against the subdivision column. Both are compared as
        // typed rather than resolved, because a code is already its own canonical form.
        return qualifier.Raw.Equals(candidate.Country, StringComparison.OrdinalIgnoreCase)
            || (candidate.Admin1 is not null && qualifier.Raw.Equals(candidate.Admin1, StringComparison.OrdinalIgnoreCase));
    }

    private GeoMatch? FindCity(
        SqliteConnection connection, string subject, IReadOnlyList<Qualifier> qualifiers, IReadOnlySet<string>? preferredCountry)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.name, c.country, c.admin1, c.lat, c.lon, c.population
            FROM city c
            WHERE c.normalized_name = $n
            UNION
            SELECT c.name, c.country, c.admin1, c.lat, c.lon, c.population
            FROM city c
            JOIN city_alias a ON a.city_id = c.id
            WHERE a.normalized_name = $n
            """;
        command.Parameters.AddWithValue("$n", GeoText.Normalize(subject));

        using var reader = command.ExecuteReader();
        Candidate? best = null;

        while (reader.Read())
        {
            var candidate = new Candidate(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                reader.GetInt64(5));

            // Every qualifier has to be satisfied. An unsatisfiable one is a miss rather than
            // something to ignore: quietly dropping "Munich, DE" would answer a question nobody
            // asked, and a miss still reaches the vendor geocoder.
            if (qualifiers.Any(q => !Matches(q, candidate)))
            {
                continue;
            }

            // Population is the whole ranking, which is what makes bare "Portland" mean Oregon
            // and bare "Cambridge" mean the English one, matching the vendor. A declared country
            // outranks it, and only ever as a tiebreak between matches that were all already
            // valid: a user in the US who types "Cambridge" means Massachusetts.
            if (best is null || Ranks(candidate, best, preferredCountry))
            {
                best = candidate;
            }
        }

        return best is null ? null : new GeoMatch(Snap(best.Lat), Snap(best.Lon), best.Name);

        static bool Ranks(Candidate candidate, Candidate best, IReadOnlySet<string>? preferredCountry)
        {
            if (preferredCountry is not null)
            {
                var candidateMatches = preferredCountry.Contains(candidate.Country);
                var bestMatches = preferredCountry.Contains(best.Country);
                if (candidateMatches != bestMatches)
                {
                    return candidateMatches;
                }
            }

            return candidate.Population > best.Population;
        }
    }

    private GeoMatch? FindPostal(
        SqliteConnection connection, string subject, IReadOnlyList<Qualifier> qualifiers, IReadOnlySet<string>? preferredCountry)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT country, lat, lon FROM postal WHERE code = $c";
        command.Parameters.AddWithValue("$c", GeoText.NormalizePostal(subject));

        var rows = new List<(string Country, double Lat, double Lon)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                rows.Add((reader.GetString(0), reader.GetDouble(1), reader.GetDouble(2)));
            }
        }

        if (rows.Count == 0)
        {
            return null;
        }

        // A qualifier narrows postal candidates when it can. When it cannot - a US state code
        // against a table that only knows countries - the code is specific enough on its own, so
        // the unfiltered ranking stands rather than turning a working input into a miss.
        var countries = qualifiers.SelectMany(q => q.CountryCodes.Append(q.Raw.ToUpperInvariant())).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filtered = qualifiers.Count == 0
            ? rows
            : rows.Where(r => countries.Contains(r.Country)).ToList();
        if (filtered.Count == 0)
        {
            filtered = rows;
        }

        // A declared country settles it outright, and this is the input that most needs settling:
        // a bare five-digit code is a real postal code in six countries at once, so ranking them
        // by the biggest city nearby sends 02180 to Seoul rather than to Stoneham, Massachusetts.
        // The set covers the declared country's own postal territories too - see
        // PostalJurisdictions, which exists because 00784 with the US declared answered Warsaw.
        if (preferredCountry is not null)
        {
            var preferred = filtered
                .Where(r => preferredCountry.Contains(r.Country))
                .ToList();
            if (preferred.Count > 0)
            {
                filtered = preferred;
            }
        }

        if (filtered.Count == 1)
        {
            return new GeoMatch(Snap(filtered[0].Lat), Snap(filtered[0].Lon), CityName: null);
        }

        var best = filtered[0];
        var bestPopulation = -1L;
        foreach (var row in filtered)
        {
            var population = LargestPopulationNearby(connection, row.Lat, row.Lon);
            if (population > bestPopulation)
            {
                bestPopulation = population;
                best = row;
            }
        }

        return new GeoMatch(Snap(best.Lat), Snap(best.Lon), CityName: null);
    }

    private static long LargestPopulationNearby(SqliteConnection connection, double latitude, double longitude)
    {
        var latPad = GeoDistance.LatitudeDegrees(PostalRankRadiusKm);
        var lonPad = GeoDistance.LongitudeDegrees(PostalRankRadiusKm, latitude);

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT MAX(c.population)
            FROM city_bbox b
            JOIN city c ON c.id = b.id
            WHERE b.min_lon >= $west AND b.max_lon <= $east
              AND b.min_lat >= $south AND b.max_lat <= $north
            """;
        command.Parameters.AddWithValue("$west", longitude - lonPad);
        command.Parameters.AddWithValue("$east", longitude + lonPad);
        command.Parameters.AddWithValue("$south", latitude - latPad);
        command.Parameters.AddWithValue("$north", latitude + latPad);

        return command.ExecuteScalar() is long population ? population : 0;
    }

    /// <summary>
    /// The same 0.01-degree snap the vendor resolver applies, so a local hit and a vendor hit for
    /// one place land on one forecast cache entry instead of two.
    /// </summary>
    private static double Snap(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
