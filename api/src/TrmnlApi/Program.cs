using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Http.Resilience;
using TrmnlApi.Endpoints;
using TrmnlApi.Observability;
using TrmnlApi.Providers;
using TrmnlApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddExceptionHandler<UnhandledExceptionLogger>();

builder.Services.Configure<WeatherCacheOptions>(builder.Configuration.GetSection("WeatherCache"));
// Sized from WeatherCacheOptions so the limit and the stale window that fills it are configured
// together: see WeatherCacheOptions.SizeLimit for why it tracks StaleTtl rather than the request
// rate. Bound here rather than through IOptions because the cache is built before options resolve.
var weatherCacheSizeLimit = builder.Configuration.GetSection("WeatherCache")
    .GetValue<int?>(nameof(WeatherCacheOptions.SizeLimit)) ?? new WeatherCacheOptions().SizeLimit;
builder.Services.AddMemoryCache(options => options.SizeLimit = weatherCacheSizeLimit);
builder.Services.AddHttpClient<IOpenMeteoClient, OpenMeteoClient>()
    .AddStandardResilienceHandler()
    .Configure((options, sp) => WeatherResilience.Configure(options, OpenMeteoProvider.ProviderName, ResilienceLogger(sp)));
builder.Services.AddHttpClient<IPirateWeatherClient, PirateWeatherClient>()
    .AddStandardResilienceHandler()
    .Configure((options, sp) => WeatherResilience.Configure(options, PirateWeatherProvider.ProviderName, ResilienceLogger(sp)));
// Standard resilience, but not WeatherResilience's configuration: that logs circuit transitions
// under a provider name, and geocoding is not a forecast provider. Its failure is reported to the
// caller on its own terms.
builder.Services.AddHttpClient<IOpenMeteoGeocodingClient, OpenMeteoGeocodingClient>()
    .AddStandardResilienceHandler();
builder.Services.AddHttpClient("TrmnlApi");
builder.Services.AddSingleton<IWeatherTransformer, WeatherTransformer>();
// Keyed so that only the providers named in WeatherProviders are ever constructed: a provider
// left out of the list must not need its API key configured.
builder.Services.AddKeyedSingleton<IWeatherProvider, PirateWeatherProvider>(PirateWeatherProvider.ProviderName);
builder.Services.AddKeyedSingleton<IWeatherProvider, OpenMeteoProvider>(OpenMeteoProvider.ProviderName);

var configuredProviders = ParseWeatherProviders(builder.Configuration["WeatherProviders"]);
builder.Services.AddSingleton<WeatherProviderResolver>(sp => new WeatherProviderResolver(
    ResolveConfiguredProviders(sp, configuredProviders),
    configuredProviders));
builder.Services.Configure<PlaceCacheOptions>(builder.Configuration.GetSection("PlaceCache"));
var placeCacheSizeLimit = builder.Configuration.GetSection("PlaceCache")
    .GetValue<int?>(nameof(PlaceCacheOptions.SizeLimit)) ?? new PlaceCacheOptions().SizeLimit;
builder.Services.AddSingleton(sp => new PlaceResolver(
    sp.GetRequiredService<IOpenMeteoGeocodingClient>(),
    // Its own MemoryCache, deliberately not the one AddMemoryCache registered: free-text input that
    // can evict forecasts is a way to empty the forecast cache. See PlaceCacheOptions.SizeLimit.
    new MemoryCache(new MemoryCacheOptions { SizeLimit = placeCacheSizeLimit }),
    sp.GetRequiredService<IOptions<PlaceCacheOptions>>(),
    sp.GetRequiredService<ILogger<PlaceResolver>>()));
builder.Services.AddSingleton<WeatherCache>();
builder.Services.AddSingleton<WeatherForecastOrchestrator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ForecastMetrics>();

var app = builder.Build();

// The handler registered above writes the response itself, so the branch is empty.
app.UseExceptionHandler(_ => { });

// Build the resolver eagerly so a missing provider API key fails at startup with a clear
// message instead of 500ing the first forecast request.
app.Services.GetRequiredService<WeatherProviderResolver>();

app.MapGet("/api/v1/forecast", WeatherEndpoint.Handle);
app.MapGet("/api/v2/forecast", WeatherV2Endpoint.Handle);
app.MapGet("/health", () => Results.Ok());
app.MapGet("/metrics", (ForecastMetrics metrics) => Results.Json(metrics.Snapshot(), WeatherEndpoint.JsonOptions));

app.Run();

// Category TrmnlApi.Services.WeatherResilience, which is what the shipping allowlist names.
static ILogger ResilienceLogger(IServiceProvider sp) =>
    sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(WeatherResilience));

static IEnumerable<IWeatherProvider> ResolveConfiguredProviders(IServiceProvider sp, IReadOnlyList<string> configuredOrder) =>
    configuredOrder.Select(name => sp.GetKeyedService<IWeatherProvider>(name)
        ?? throw new InvalidOperationException($"Configured weather provider '{name}' is not registered."));

static IReadOnlyList<string> ParseWeatherProviders(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw))
    {
        throw new InvalidOperationException("WeatherProviders configuration is required (comma-separated list of provider names).");
    }

    var names = raw.Split(',', StringSplitOptions.TrimEntries);
    var seen = new HashSet<string>(StringComparer.Ordinal);
    foreach (var name in names)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidOperationException("WeatherProviders contains an empty entry.");
        }
        if (!seen.Add(name))
        {
            throw new InvalidOperationException($"WeatherProviders contains duplicate entry '{name}'.");
        }
    }
    return names;
}
