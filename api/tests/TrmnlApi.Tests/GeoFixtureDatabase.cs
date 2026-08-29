using Microsoft.Data.Sqlite;
using TrmnlApi.Geo;

namespace TrmnlApi.Tests;

/// <summary>
/// A handful of rows in the real schema, written with the real packed-geometry encoder.
/// </summary>
/// <remarks>
/// Small enough to read, and still a genuine test of the queries: the R-tree, the blob format and
/// the SQL are the same ones the shipped artifact uses, so a schema change that breaks a reader
/// breaks these tests. The subdivisions are rectangles rather than real coastlines - the thing
/// under test is the lookup, not Natural Earth's outline of Massachusetts.
/// </remarks>
public sealed class GeoFixtureDatabase : IDisposable
{
    private readonly string _path;

    public GeoFixtureDatabase()
    {
        _path = Path.Combine(Path.GetTempPath(), $"geo-fixture-{Guid.NewGuid():N}.sqlite");

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());
        connection.Open();

        Execute(connection, GeoSchema.CreateTables);
        Execute(connection, $"INSERT INTO meta (key, value) VALUES ('schema_version', '{GeoSchema.Version}')");

        // id, ISO 3166-2, country code, country name, subdivision name, and a bounding rectangle.
        AddSubdivision(connection, 1, "US-MA", "US", "United States of America", "Massachusetts", -73.5, -69.9, 41.2, 42.9);
        AddSubdivision(connection, 2, "US-PR", "US", "United States of America", "Puerto Rico", -67.3, -65.2, 17.9, 18.6);
        // Numeric code: the display rule has to fall back to the name here, or the screen reads
        // "Lille, 59".
        AddSubdivision(connection, 3, "FR-59", "FR", "France", "Nord", 2.0, 4.3, 50.0, 51.1);
        // A GB district, which is why GB is on the name-first list.
        AddSubdivision(connection, 4, "GB-CAM", "GB", "United Kingdom", "Cambridgeshire", -0.5, 0.5, 52.0, 52.7);
        AddSubdivision(connection, 5, "US-OR", "US", "United States of America", "Oregon", -124.6, -116.5, 42.0, 46.3);
        AddSubdivision(connection, 6, "US-TX", "US", "United States of America", "Texas", -106.7, -93.5, 25.8, 36.5);

        // Natural Earth ships Kiribati as a single feature whose islands sit either side of the
        // antimeridian, so its bounding box spans most of the planet while its land is specks.
        // Anything that ranks candidates by bounding box hands it every point in the Pacific.
        // Its ISO 3166-2 is null because Natural Earth's own value for it, 'KI-X01~', is invented
        // and the builder drops those. The country code is real.
        AddScatteredSubdivision(
            connection, 7, null, "KI", "Kiribati", "Kiribati",
            [
                (179.5, 179.9, -0.2, 0.2),
                (-157.6, -157.2, 1.7, 2.1)
            ]);

        // Natural Earth gives Somaliland no ISO code in either column, storing '-99-X11~' and a
        // country of '-1'. Both are dropped, and only the names are left to label it with.
        AddScatteredSubdivision(
            connection, 8, null, null, "Somaliland", "Somaliland",
            [(43.0, 49.0, 8.0, 11.5)]);

        // A disputed territory as the builder stores it: geometry, and not one word of attribution.
        // Its neighbour sits close enough that deleting the outline instead would hand every point
        // inside it to that neighbour. See ContestedTerritories in the builder.
        AddSubdivision(connection, 9, null, null, null, null, 30.0, 32.0, 20.0, 22.0);
        AddSubdivision(connection, 10, "XA-N", "XA", "Northland", "Northshire", 30.0, 32.0, 22.1, 24.0);
        AddCity(connection, 14, "Disputown", "XA", "N", 21.0, 31.0, 400000);

        foreach (var (code, name) in new[]
                 {
                     ("US", "United States of America"), ("FR", "France"), ("GB", "United Kingdom"),
                     ("DE", "Germany"), ("CA", "Canada"), ("PR", "Puerto Rico"), ("KI", "Kiribati")
                 })
        {
            Execute(connection,
                "INSERT INTO country (iso_a2, name, normalized_name) VALUES ($a, $n, $z)",
                ("$a", code), ("$n", name), ("$z", GeoText.Normalize(name)));
        }

        foreach (var (country, code, name) in new[]
                 {
                     ("US", "MA", "Massachusetts"), ("US", "ME", "Maine"), ("US", "OR", "Oregon"),
                     ("US", "TX", "Texas"), ("FR", "B4", "Hauts-de-France"), ("DE", "02", "Bavaria")
                 })
        {
            Execute(connection,
                "INSERT INTO admin1_name (country, code, name, normalized_name) VALUES ($c, $k, $n, $z)",
                ("$c", country), ("$k", code), ("$n", name), ("$z", GeoText.Normalize(name)));
        }

        AddCity(connection, 1, "Boston", "US", "MA", 42.35843, -71.05977, 617594);
        AddCity(connection, 2, "Guayama", "PR", "05", 17.98411, -66.11324, 21044);
        AddCity(connection, 3, "Munich", "DE", "02", 48.13743, 11.57549, 1260391, "Muenchen", "Munchen");
        AddCity(connection, 4, "Toronto", "CA", "08", 43.70011, -79.4163, 2600000);
        AddCity(connection, 5, "Portland", "US", "OR", 45.52345, -122.67621, 583776);
        AddCity(connection, 6, "Portland", "US", "ME", 43.66147, -70.25533, 66194);
        AddCity(connection, 7, "Paris", "FR", "11", 48.85341, 2.3488, 2138551);
        AddCity(connection, 8, "Addison", "US", "TX", 32.96179, -96.82918, 15022);
        AddCity(connection, 9, "Lille", "FR", "B4", 50.63297, 3.05858, 228328);
        AddCity(connection, 10, "Cambridge", "GB", "ENG", 52.2, 0.11667, 158434);
        // Close enough to Addison to be inside the postal ranking radius, and far short of Paris.
        AddCity(connection, 11, "Plano", "US", "TX", 33.01984, -96.69889, 287677);
        // The same name in two countries, which is what a declared country has to settle. Both
        // are real: Boston, Lincolnshire is the one Boston, Massachusetts was named after.
        AddCity(connection, 12, "Boston", "GB", "ENG", 52.97633, -0.02664, 45339);

        AddPostal(connection, "FR", "75001", 48.8592, 2.3417);
        AddPostal(connection, "US", "75001", 32.9618, -96.8292);
        AddPostal(connection, "PR", "00784", 17.9839, -66.1136);
        // The same code in Warsaw. GeoNames files Puerto Rico under PR, not US, so a US user
        // typing their own ZIP matched nothing and fell through to the biggest city on the code.
        AddPostal(connection, "PL", "00784", 52.2054, 21.0245);
        AddCity(connection, 13, "Warsaw", "PL", "MZ", 52.2298, 21.0118, 1790658);
    }

    public GeoDatabase Open() => GeoDatabase.TryOpen(_path)!;

    private static void AddSubdivision(
        SqliteConnection connection, int id, string? iso, string? a2, string? country, string? name,
        double west, double east, double south, double north)
    {
        var ring = new List<(double, double)>
        {
            (west, south), (east, south), (east, north), (west, north), (west, south)
        };

        Execute(connection,
            """
            INSERT INTO admin1 (id, iso_3166_2, iso_a2, admin_name, subdiv_name, geom)
            VALUES ($id, $iso, $a2, $admin, $name, $geom)
            """,
            ("$id", id), ("$iso", (object?)iso ?? DBNull.Value), ("$a2", (object?)a2 ?? DBNull.Value),
            ("$admin", (object?)country ?? DBNull.Value), ("$name", (object?)name ?? DBNull.Value),
            ("$geom", PolygonBlob.Encode([ring])));

        Execute(connection,
            "INSERT INTO admin1_bbox (id, min_lon, max_lon, min_lat, max_lat) VALUES ($id, $w, $e, $s, $n)",
            ("$id", id), ("$w", west), ("$e", east), ("$s", south), ("$n", north));
    }

    /// <summary>
    /// A subdivision made of several separate islands, with the single bounding box that covers
    /// all of them - the shape that makes bounding-box ranking wrong.
    /// </summary>
    private static void AddScatteredSubdivision(
        SqliteConnection connection, int id, string? iso, string? a2, string country, string name,
        (double West, double East, double South, double North)[] parts)
    {
        var rings = parts
            .Select(p => (IReadOnlyList<(double, double)>)new List<(double, double)>
            {
                (p.West, p.South), (p.East, p.South), (p.East, p.North), (p.West, p.North), (p.West, p.South)
            })
            .ToList();

        Execute(connection,
            """
            INSERT INTO admin1 (id, iso_3166_2, iso_a2, admin_name, subdiv_name, geom)
            VALUES ($id, $iso, $a2, $admin, $name, $geom)
            """,
            ("$id", id), ("$iso", (object?)iso ?? DBNull.Value), ("$a2", (object?)a2 ?? DBNull.Value),
            ("$admin", country), ("$name", name),
            ("$geom", PolygonBlob.Encode(rings)));

        Execute(connection,
            "INSERT INTO admin1_bbox (id, min_lon, max_lon, min_lat, max_lat) VALUES ($id, $w, $e, $s, $n)",
            ("$id", id),
            ("$w", parts.Min(p => p.West)), ("$e", parts.Max(p => p.East)),
            ("$s", parts.Min(p => p.South)), ("$n", parts.Max(p => p.North)));
    }

    private static void AddCity(
        SqliteConnection connection, int id, string name, string country, string admin1,
        double lat, double lon, long population, params string[] aliases)
    {
        Execute(connection,
            """
            INSERT INTO city (id, name, normalized_name, country, admin1, lat, lon, population)
            VALUES ($id, $name, $norm, $country, $admin1, $lat, $lon, $pop)
            """,
            ("$id", id), ("$name", name), ("$norm", GeoText.Normalize(name)),
            ("$country", country), ("$admin1", admin1), ("$lat", lat), ("$lon", lon), ("$pop", population));

        Execute(connection,
            "INSERT INTO city_bbox (id, min_lon, max_lon, min_lat, max_lat) VALUES ($id, $lon, $lon, $lat, $lat)",
            ("$id", id), ("$lat", lat), ("$lon", lon));

        foreach (var alias in aliases)
        {
            Execute(connection,
                "INSERT INTO city_alias (city_id, normalized_name) VALUES ($id, $norm)",
                ("$id", id), ("$norm", GeoText.Normalize(alias)));
        }
    }

    private static void AddPostal(SqliteConnection connection, string country, string code, double lat, double lon) =>
        Execute(connection,
            "INSERT INTO postal (country, code, lat, lon) VALUES ($c, $k, $lat, $lon)",
            ("$c", country), ("$k", GeoText.NormalizePostal(code)), ("$lat", lat), ("$lon", lon));

    private static void Execute(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Removes the file, so that a test can prove a second lookup never touched it.
    /// </summary>
    /// <remarks>
    /// The connection pool holds the handle open, so it has to be emptied first: on Windows a
    /// delete against an open file fails rather than being deferred.
    /// </remarks>
    public void Destroy()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    public void Dispose()
    {
        try
        {
            Destroy();
        }
        catch (IOException)
        {
            // A leaked temp file is not worth failing a test run over.
        }
    }
}
