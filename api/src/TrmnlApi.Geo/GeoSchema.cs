namespace TrmnlApi.Geo;

/// <summary>
/// The shape of <c>geo.sqlite</c>, kept here rather than in the builder so that the writer and
/// every reader are looking at one definition. The test fixture database is built from this too,
/// which is what makes a fixture-backed test a real test of the query and not of a mock.
/// </summary>
public static class GeoSchema
{
    /// <summary>Bumped whenever a reader would misread an older artifact.</summary>
    public const int Version = 1;

    /// <summary>
    /// R-tree tables carry the bounding boxes; the ordinary tables carry the payload. The
    /// subdivision polygons are the only geometry stored, and only the two or three blobs an
    /// R-tree query returns are ever decoded.
    /// </summary>
    public const string CreateTables = """
        CREATE TABLE meta (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        -- One row per Natural Earth 10m admin-1 feature. iso_3166_2 is the telemetry form
        -- ('US-MA', 'FR-59'); subdiv_name is what the display rule falls back to when the code
        -- is numeric. See SubdivisionLabel.
        CREATE TABLE admin1 (
            id          INTEGER PRIMARY KEY,
            iso_3166_2  TEXT NOT NULL,
            iso_a2      TEXT NOT NULL,
            admin_name  TEXT NOT NULL,
            subdiv_name TEXT NOT NULL,
            geom        BLOB NOT NULL
        );
        CREATE VIRTUAL TABLE admin1_bbox USING rtree(id, min_lon, max_lon, min_lat, max_lat);

        -- Country display names, derived from the admin-1 layer's own country column rather than
        -- from a hand-maintained ISO table. Used only to resolve a typed qualifier such as
        -- 'Munich, Germany' to a country code.
        CREATE TABLE country (
            iso_a2          TEXT PRIMARY KEY,
            name            TEXT NOT NULL,
            normalized_name TEXT NOT NULL
        );
        CREATE INDEX country_normalized ON country(normalized_name);

        -- GeoNames admin-1 division names, so 'Portland, Oregon' resolves the same way
        -- 'Portland, OR' does. GeoNames admin1 codes are 89% numeric, which is why the name
        -- has to be carried separately from the code.
        CREATE TABLE admin1_name (
            country         TEXT NOT NULL,
            code            TEXT NOT NULL,
            name            TEXT NOT NULL,
            normalized_name TEXT NOT NULL,
            PRIMARY KEY (country, code)
        );
        CREATE INDEX admin1_name_normalized ON admin1_name(normalized_name);

        -- GeoNames cities1000. admin1 is the GeoNames division code, not an ISO 3166-2 suffix.
        CREATE TABLE city (
            id              INTEGER PRIMARY KEY,
            name            TEXT NOT NULL,
            normalized_name TEXT NOT NULL,
            country         TEXT NOT NULL,
            admin1          TEXT,
            lat             REAL NOT NULL,
            lon             REAL NOT NULL,
            population      INTEGER NOT NULL
        );
        CREATE INDEX city_normalized ON city(normalized_name);
        CREATE VIRTUAL TABLE city_bbox USING rtree(id, min_lon, max_lon, min_lat, max_lat);

        -- The alternate names GeoNames ships inline on 82% of rows.
        CREATE TABLE city_alias (
            city_id         INTEGER NOT NULL,
            normalized_name TEXT NOT NULL
        );
        CREATE INDEX city_alias_normalized ON city_alias(normalized_name);

        -- Coordinates only, deliberately. The place names in the GeoNames postal files are not
        -- labels: 'CA M5V' is 'Downtown Toronto (CN Tower / King and Spadina / Railway Lands /
        -- ...)' and 'GB SW1A' is 'Westminster Abbey'.
        CREATE TABLE postal (
            country TEXT NOT NULL,
            code    TEXT NOT NULL,
            lat     REAL NOT NULL,
            lon     REAL NOT NULL
        );
        CREATE INDEX postal_code ON postal(code);
        """;
}
