using Microsoft.Data.Sqlite;

namespace TrmnlApi.Geo;

/// <summary>
/// Hands out read-only connections to the bundled dataset.
/// </summary>
/// <remarks>
/// A connection per lookup rather than one shared open connection: <c>SqliteConnection</c> is not
/// safe to use from several requests at once, and Microsoft.Data.Sqlite pools by connection
/// string, so opening one is a dictionary lookup rather than a file open.
/// </remarks>
public sealed class GeoDatabase
{
    private readonly string _connectionString;

    private GeoDatabase(string connectionString) => _connectionString = connectionString;

    /// <summary>
    /// Returns null when no dataset is configured or the file is not there. Callers register the
    /// null implementations in that case rather than failing to start: a service that will not
    /// boot without a 100 MB download is a worse outage than one that shows no location.
    /// </summary>
    public static GeoDatabase? TryOpen(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();

        // Prove it opens and carries the schema this build expects, at startup rather than on the
        // first request, so a mispackaged artifact is a log line at boot and not a silent blank
        // label forever after.
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = 'schema_version'";
        var version = command.ExecuteScalar() as string;
        if (version != GeoSchema.Version.ToString())
        {
            throw new InvalidOperationException(
                $"Geo dataset at '{path}' reports schema version '{version}', expected {GeoSchema.Version}.");
        }

        return new GeoDatabase(connectionString);
    }

    public SqliteConnection Connect()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
