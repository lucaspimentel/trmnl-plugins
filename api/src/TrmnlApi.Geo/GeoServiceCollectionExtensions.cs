using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TrmnlApi.Geo;

/// <summary>
/// Opens the dataset once for both the geocoder and the lookup. A singleton rather than an open
/// inside each registration, so a missing artifact is one log line and not two.
/// </summary>
public sealed class GeoDatabaseHolder
{
    public GeoDatabaseHolder(IOptions<GeoOptions> options, ILogger<GeoDatabaseHolder> logger)
    {
        var path = options.Value.DatabasePath;
        try
        {
            Database = GeoDatabase.TryOpen(path);
            if (Database is null)
            {
                logger.LogWarning("No geo dataset at {Path}; locations will not be shown.", path);
            }
        }
        catch (Exception ex)
        {
            // Logged and downgraded rather than thrown. A service that serves forecasts without a
            // location beats one that will not boot without a bundled download.
            logger.LogError(ex, "Geo dataset at {Path} could not be opened; locations will not be shown.", path);
            Database = null;
        }
    }

    public GeoDatabase? Database { get; }
}

public static class GeoServiceCollectionExtensions
{
    /// <summary>
    /// Registers the bundled-dataset geocoder and place lookup, or the null implementations when
    /// no dataset is present. <see cref="GeoOptions"/> must already be configured.
    /// </summary>
    public static IServiceCollection AddTrmnlGeo(this IServiceCollection services)
    {
        services.AddSingleton<GeoDatabaseHolder>();

        services.AddSingleton<IPlaceLookup>(sp =>
        {
            var database = sp.GetRequiredService<GeoDatabaseHolder>().Database;
            if (database is null)
            {
                return new NullPlaceLookup();
            }

            var options = sp.GetRequiredService<IOptions<GeoOptions>>();
            return new SqlitePlaceLookup(
                database,
                // Its own MemoryCache, deliberately not the one AddMemoryCache registered: see
                // GeoOptions.CacheSizeLimit.
                new MemoryCache(new MemoryCacheOptions { SizeLimit = options.Value.CacheSizeLimit }),
                options,
                sp.GetRequiredService<ILogger<SqlitePlaceLookup>>());
        });

        services.AddSingleton<ILocalGeocoder>(sp =>
        {
            var database = sp.GetRequiredService<GeoDatabaseHolder>().Database;
            return database is null
                ? new NullLocalGeocoder()
                : new SqliteLocalGeocoder(
                    database,
                    sp.GetRequiredService<IOptions<GeoOptions>>(),
                    sp.GetRequiredService<ILogger<SqliteLocalGeocoder>>());
        });

        return services;
    }
}
