namespace TrmnlApi.Services;

public class WeatherCacheOptions
{
    public TimeSpan FreshTtl { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan StaleTtl { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Most forecasts held at once, one per (provider, coordinate, unit system).
    /// </summary>
    /// <remarks>
    /// This has to cover every entry alive within <see cref="StaleTtl"/>, not the request rate:
    /// entries stay resident for the full stale window so they remain available as the fallback
    /// when providers fail. Sizing it to the fresh window instead evicts exactly the entries the
    /// stale path exists to serve, turning a degraded-but-served response into a 502.
    ///
    /// Budget roughly 4 KB per entry, so the default is a few MB.
    /// </remarks>
    public int SizeLimit { get; set; } = 2000;
}
