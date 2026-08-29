using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TrmnlApi.Geo;

/// <summary>
/// Reverse geocoding over the bundled dataset: an R-tree query for the handful of subdivision
/// polygons whose bounding box covers the point, an exact point-in-polygon test on those, and a
/// separate nearest-city query for the label.
/// </summary>
/// <remarks>
/// Nothing here throws and nothing here waits. Every failure - a corrupt blob, a missing table, a
/// lookup that outruns its budget - returns <see cref="GeoPlace.Empty"/>, because the caller is
/// serving a forecast and the place block is decoration on it.
/// </remarks>
public sealed class SqlitePlaceLookup : IPlaceLookup
{
    private readonly GeoDatabase _database;
    private readonly IMemoryCache _cache;
    private readonly GeoOptions _options;
    private readonly ILogger<SqlitePlaceLookup> _logger;

    /// <param name="cache">
    /// Must be an instance of this lookup's own, never the one <c>AddMemoryCache</c> registers:
    /// see <see cref="GeoOptions.CacheSizeLimit"/>.
    /// </param>
    public SqlitePlaceLookup(
        GeoDatabase database,
        IMemoryCache cache,
        IOptions<GeoOptions> options,
        ILogger<SqlitePlaceLookup> logger)
    {
        _database = database;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public GeoPlace Find(double latitude, double longitude)
    {
        // The 0.01-degree grid the forecast cache already keys on, packed into one long. Reading
        // at this precision is deliberate and safe: what leaks is bounded by what is emitted, and
        // every emitted surface coarsens to 0.1 degrees first.
        var latE2 = (int)Math.Round(latitude * 100, MidpointRounding.AwayFromZero);
        var lonE2 = (int)Math.Round(longitude * 100, MidpointRounding.AwayFromZero);
        var key = ((latE2 + 9000) * 36001L) + lonE2 + 18000;

        if (_cache.TryGetValue(key, out GeoPlace cached))
        {
            return cached;
        }

        var place = Lookup(latE2 / 100.0, lonE2 / 100.0);

        _cache.Set(
            key,
            place,
            new MemoryCacheEntryOptions().SetAbsoluteExpiration(_options.CacheTtl).SetSize(1));

        return place;
    }

    private GeoPlace Lookup(double latitude, double longitude)
    {
        var elapsed = Stopwatch.StartNew();

        try
        {
            using var connection = _database.Connect();

            var subdivision = FindSubdivision(connection, latitude, longitude);

            // Out of budget after the polygon work: return what is already known rather than
            // spending more on the nicest part of the answer.
            var city = elapsed.Elapsed < _options.TimeBudget
                ? FindNearestCity(connection, latitude, longitude)
                : null;

            return new GeoPlace(
                City: city,
                SubdivisionCode: subdivision.Code,
                SubdivisionName: subdivision.Name,
                CountryCode: subdivision.CountryCode,
                Country: subdivision.Country);
        }
        catch (Exception ex)
        {
            // Coordinates are not logged here at all, coarse or otherwise: the message is about
            // the dataset, and a failure that fires on every request would emit a location stream.
            _logger.LogWarning(ex, "Reverse geocoding failed; serving the forecast without a place.");
            return GeoPlace.Empty;
        }
    }

    private readonly record struct Subdivision(string? Code, string? Name, string? CountryCode, string? Country);

    private const string SelectSubdivision = """
        SELECT a.iso_3166_2, a.iso_a2, a.admin_name, a.subdiv_name, a.geom
        FROM admin1_bbox b
        JOIN admin1 a ON a.id = b.id
        WHERE b.max_lon >= $west AND b.min_lon <= $east
          AND b.max_lat >= $south AND b.min_lat <= $north
        """;

    private Subdivision FindSubdivision(SqliteConnection connection, double latitude, double longitude)
    {
        // Pass one: bounding boxes that actually cover the point, tested exactly.
        using (var command = connection.CreateCommand())
        {
            command.CommandText = SelectSubdivision;
            Bind(command, longitude, longitude, latitude, latitude);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (PolygonBlob.Contains(ReadGeometry(reader), longitude, latitude))
                {
                    return Read(reader);
                }
            }
        }

        // Pass two: nothing contains the point, so it is at sea, on a simplified coastline, or
        // genuinely nowhere. Widen the search and take the nearest feature by bounding box.
        var latPad = GeoDistance.LatitudeDegrees(_options.CountryRadiusKm);
        var lonPad = GeoDistance.LongitudeDegrees(_options.CountryRadiusKm, latitude);

        var bestDistance = double.MaxValue;
        Subdivision best = default;

        foreach (var (west, east) in LongitudeRanges(longitude - lonPad, longitude + lonPad))
        {
            using var command = connection.CreateCommand();
            command.CommandText = SelectSubdivision;
            Bind(command, west, east, latitude - latPad, latitude + latPad);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                // Distance to the polygon itself. Ranking by bounding box instead would hand
                // every mid-ocean point in the Pacific to Kiribati, whose one feature straddles
                // the antimeridian and boxes in a third of the planet.
                var distance = PolygonBlob.DistanceKm(ReadGeometry(reader), longitude, latitude);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = Read(reader);
                }
            }
        }

        if (bestDistance > _options.CountryRadiusKm)
        {
            // Mid-ocean. A blank record, never an invented country.
            return default;
        }

        // A near miss keeps the subdivision - a coastal city sitting a few kilometres outside
        // a simplified polygon is still in its own state. A distant one keeps the country
        // only, which is the weaker claim the distance can still support.
        return bestDistance <= _options.CityRadiusKm
            ? best
            : new Subdivision(null, null, best.CountryCode, best.Country);

        static void Bind(SqliteCommand command, double west, double east, double south, double north)
        {
            command.Parameters.AddWithValue("$west", west);
            command.Parameters.AddWithValue("$east", east);
            command.Parameters.AddWithValue("$south", south);
            command.Parameters.AddWithValue("$north", north);
        }

        // Every column is nullable. A territory Natural Earth has no ISO assignment for keeps its
        // names and loses its invented codes; a disputed one is stored with its geometry and
        // nothing else, so it matches here and comes back unnamed. See GeoSchema.
        static Subdivision Read(SqliteDataReader reader) => new(
            Code: reader.IsDBNull(0) ? null : reader.GetString(0),
            Name: reader.IsDBNull(3) ? null : reader.GetString(3),
            CountryCode: reader.IsDBNull(1) ? null : reader.GetString(1),
            Country: reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    /// <summary>
    /// The one or two longitude ranges a search box covers, split when it runs off the end of the
    /// world.
    /// </summary>
    /// <remarks>
    /// Every stored box has a longitude in [-180, 180], so a padded search box that reaches past
    /// either end matches nothing on the far side unless it is asked for separately. Without the
    /// split, a point at 179.9W finds no country at all - not a wrong answer, but a blank one, for
    /// everyone in Fiji, Kiribati and the eastern edge of New Zealand.
    /// </remarks>
    private static (double West, double East)[] LongitudeRanges(double west, double east) =>
        west < -180.0 ? [(-180.0, east), (west + 360.0, 180.0)]
        : east > 180.0 ? [(west, 180.0), (-180.0, east - 360.0)]
        : [(west, east)];

    private static byte[] ReadGeometry(SqliteDataReader reader) => reader.GetFieldValue<byte[]>(4);

    private string? FindNearestCity(SqliteConnection connection, double latitude, double longitude)
    {
        var latPad = GeoDistance.LatitudeDegrees(_options.CityRadiusKm);
        var lonPad = GeoDistance.LongitudeDegrees(_options.CityRadiusKm, latitude);

        string? best = null;
        var bestDistance = _options.CityRadiusKm;

        foreach (var (west, east) in LongitudeRanges(longitude - lonPad, longitude + lonPad))
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT c.name, c.lat, c.lon
                FROM city_bbox b
                JOIN city c ON c.id = b.id
                WHERE b.min_lon >= $west AND b.max_lon <= $east
                  AND b.min_lat >= $south AND b.max_lat <= $north
                """;
            command.Parameters.AddWithValue("$west", west);
            command.Parameters.AddWithValue("$east", east);
            command.Parameters.AddWithValue("$south", latitude - latPad);
            command.Parameters.AddWithValue("$north", latitude + latPad);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var distance = GeoDistance.Haversine(latitude, longitude, reader.GetDouble(1), reader.GetDouble(2));
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = reader.GetString(0);
                }
            }
        }

        return best;
    }
}
