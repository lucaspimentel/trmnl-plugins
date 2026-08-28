using System.Globalization;
using Microsoft.Data.Sqlite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Esri;
using NetTopologySuite.Simplify;
using TrmnlApi.Geo;

namespace TrmnlApi.GeoDataBuilder;

/// <summary>
/// Writes <c>geo.sqlite</c>. Everything here trims: the artifact is 60-120 MB if the upstream
/// columns are carried through unfiltered, and only a handful of them are ever read.
/// </summary>
public sealed class GeoDatabaseWriter : IDisposable
{
    private readonly SqliteConnection _connection;

    public GeoDatabaseWriter(string path)
    {
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());
        _connection.Open();
        Execute("PRAGMA journal_mode = OFF; PRAGMA synchronous = OFF;");
    }

    public void CreateSchema()
    {
        Execute(GeoSchema.CreateTables);
        Execute("INSERT INTO meta (key, value) VALUES ('schema_version', $v)", ("$v", GeoSchema.Version.ToString()));
        Execute("INSERT INTO meta (key, value) VALUES ('built_utc', $v)", ("$v", DateTime.UtcNow.ToString("O")));
    }

    public record Admin1Stats(int Features, long Points, int Countries);

    public Admin1Stats WriteAdmin1(string shapefile, double tolerance)
    {
        using var transaction = _connection.BeginTransaction();

        using var insert = Command("""
            INSERT INTO admin1 (id, iso_3166_2, iso_a2, admin_name, subdiv_name, geom)
            VALUES ($id, $iso, $a2, $admin, $subdiv, $geom)
            """);
        using var insertBox = Command(
            "INSERT INTO admin1_bbox (id, min_lon, max_lon, min_lat, max_lat) VALUES ($id, $w, $e, $s, $n)");

        var id = 0;
        var points = 0L;
        var countries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var feature in Shapefile.ReadAllFeatures(shapefile))
        {
            var iso = Attribute(feature, "iso_3166_2");
            var admin = Attribute(feature, "admin");
            var name = Attribute(feature, "name");

            // One of the 4,596 features carries no ISO code. Without it there is nothing to tag
            // and nothing to display, so it is not worth its geometry.
            if (iso is null || feature.Geometry is null)
            {
                continue;
            }

            var dash = iso.IndexOf('-');
            var a2 = Attribute(feature, "iso_a2") is { Length: 2 } value && value != "-9"
                ? value
                : dash > 0 ? iso[..dash] : iso;

            var rings = ExtractRings(Simplify(feature.Geometry, tolerance));
            if (rings.Count == 0)
            {
                continue;
            }

            points += rings.Sum(r => r.Count);
            var envelope = feature.Geometry.EnvelopeInternal;

            id++;
            Run(insert,
                ("$id", id),
                ("$iso", iso),
                ("$a2", a2.ToUpperInvariant()),
                ("$admin", admin ?? a2),
                ("$subdiv", name ?? iso),
                ("$geom", PolygonBlob.Encode(rings)));
            Run(insertBox,
                ("$id", id),
                ("$w", envelope.MinX), ("$e", envelope.MaxX),
                ("$s", envelope.MinY), ("$n", envelope.MaxY));

            if (admin is not null)
            {
                countries[a2.ToUpperInvariant()] = admin;
            }
        }

        using (var insertCountry = Command(
            "INSERT OR REPLACE INTO country (iso_a2, name, normalized_name) VALUES ($a2, $name, $norm)"))
        {
            foreach (var (a2, name) in countries)
            {
                Run(insertCountry, ("$a2", a2), ("$name", name), ("$norm", GeoText.Normalize(name)));
            }
        }

        transaction.Commit();
        return new Admin1Stats(id, points, countries.Count);
    }

    public int WriteAdmin1Names(string path)
    {
        using var transaction = _connection.BeginTransaction();
        using var insert = Command("""
            INSERT OR REPLACE INTO admin1_name (country, code, name, normalized_name)
            VALUES ($country, $code, $name, $norm)
            """);

        var rows = 0;
        foreach (var line in File.ReadLines(path))
        {
            // US.MA <tab> Massachusetts <tab> Massachusetts <tab> 6254926
            var fields = line.Split('\t');
            if (fields.Length < 2)
            {
                continue;
            }

            var dot = fields[0].IndexOf('.');
            if (dot <= 0 || dot == fields[0].Length - 1)
            {
                continue;
            }

            Run(insert,
                ("$country", fields[0][..dot]),
                ("$code", fields[0][(dot + 1)..]),
                ("$name", fields[1]),
                ("$norm", GeoText.Normalize(fields[1])));
            rows++;
        }

        transaction.Commit();
        return rows;
    }

    public record CityStats(int Cities, int Aliases);

    public CityStats WriteCities(string path)
    {
        using var transaction = _connection.BeginTransaction();
        using var insert = Command("""
            INSERT INTO city (id, name, normalized_name, country, admin1, lat, lon, population)
            VALUES ($id, $name, $norm, $country, $admin1, $lat, $lon, $pop)
            """);
        using var insertBox = Command(
            "INSERT INTO city_bbox (id, min_lon, max_lon, min_lat, max_lat) VALUES ($id, $lon, $lon, $lat, $lat)");
        using var insertAlias = Command(
            "INSERT INTO city_alias (city_id, normalized_name) VALUES ($id, $norm)");

        var cities = 0;
        var aliases = 0;

        foreach (var line in File.ReadLines(path))
        {
            var f = line.Split('\t');
            if (f.Length < 15
                || !double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                || !double.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            {
                continue;
            }

            var name = f[1];
            var normalized = GeoText.Normalize(name);
            _ = long.TryParse(f[14], NumberStyles.Integer, CultureInfo.InvariantCulture, out var population);

            cities++;
            var id = cities;
            Run(insert,
                ("$id", id),
                ("$name", name),
                ("$norm", normalized),
                ("$country", f[8]),
                ("$admin1", string.IsNullOrEmpty(f[10]) ? null : f[10]),
                ("$lat", lat), ("$lon", lon),
                ("$pop", population));
            // A degenerate box per city. The R-tree is what makes the nearest-city query a range
            // scan rather than a distance calculation over 170,000 rows.
            Run(insertBox, ("$id", id), ("$lat", lat), ("$lon", lon));

            var seen = new HashSet<string>(StringComparer.Ordinal) { normalized };
            foreach (var alternate in f[3].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var alias = GeoText.Normalize(alternate);
                if (alias.Length == 0 || !seen.Add(alias))
                {
                    continue;
                }

                Run(insertAlias, ("$id", id), ("$norm", alias));
                aliases++;
            }
        }

        transaction.Commit();
        return new CityStats(cities, aliases);
    }

    public int WritePostal(string path)
    {
        using var transaction = _connection.BeginTransaction();
        using var insert = Command("INSERT INTO postal (country, code, lat, lon) VALUES ($country, $code, $lat, $lon)");

        var rows = 0;
        var seen = new HashSet<(string, string)>();

        foreach (var line in File.ReadLines(path))
        {
            var f = line.Split('\t');
            if (f.Length < 11
                || !double.TryParse(f[9], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                || !double.TryParse(f[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            {
                continue;
            }

            var code = GeoText.NormalizePostal(f[1]);
            // One row per country and code. The file carries a row per delivery area, and their
            // centroids agree to well inside the 0.01-degree grid the coordinates are snapped to.
            if (code.Length == 0 || !seen.Add((f[0], code)))
            {
                continue;
            }

            Run(insert, ("$country", f[0]), ("$code", code), ("$lat", lat), ("$lon", lon));
            rows++;
        }

        transaction.Commit();
        return rows;
    }

    /// <summary>Compacts the file. Skipping this costs tens of megabytes on a download everyone pays for.</summary>
    public void Finish() => Execute("ANALYZE; VACUUM;");

    private static Geometry Simplify(Geometry geometry, double tolerance)
    {
        if (tolerance <= 0)
        {
            return geometry;
        }

        try
        {
            var simplified = TopologyPreservingSimplifier.Simplify(geometry, tolerance);
            return simplified.IsEmpty ? geometry : simplified;
        }
        catch (Exception)
        {
            // Self-intersecting input. Better a fat polygon than a missing subdivision.
            return geometry;
        }
    }

    private static List<IReadOnlyList<(double Lon, double Lat)>> ExtractRings(Geometry geometry)
    {
        var rings = new List<IReadOnlyList<(double, double)>>();

        for (var i = 0; i < geometry.NumGeometries; i++)
        {
            if (geometry.GetGeometryN(i) is not Polygon polygon || polygon.IsEmpty)
            {
                continue;
            }

            // Outer and inner rings go in undifferentiated: PolygonBlob.Contains uses even-odd
            // parity, which handles holes without being told which is which.
            Add(polygon.ExteriorRing);
            foreach (var hole in polygon.InteriorRings)
            {
                Add(hole);
            }
        }

        return rings;

        void Add(LineString ring)
        {
            var coordinates = ring.Coordinates;
            if (coordinates.Length < 4)
            {
                return;
            }

            var points = new (double, double)[coordinates.Length];
            for (var i = 0; i < coordinates.Length; i++)
            {
                points[i] = (coordinates[i].X, coordinates[i].Y);
            }
            rings.Add(points);
        }
    }

    private static string? Attribute(NetTopologySuite.Features.IFeature feature, string name)
    {
        if (!feature.Attributes.Exists(name))
        {
            return null;
        }

        var value = feature.Attributes[name]?.ToString()?.Trim();
        return string.IsNullOrEmpty(value) || value == "-99" ? null : value;
    }

    private SqliteCommand Command(string sql)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    private static void Run(SqliteCommand command, params (string Name, object? Value)[] parameters)
    {
        command.Parameters.Clear();
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        command.ExecuteNonQuery();
    }

    private void Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql);
        Run(command, parameters);
    }

    public void Dispose() => _connection.Dispose();
}
