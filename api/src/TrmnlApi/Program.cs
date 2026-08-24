using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using TrmnlApi.Endpoints;
using TrmnlApi.Providers;
using TrmnlApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache(options => options.SizeLimit = 200);
builder.Services.Configure<WeatherCacheOptions>(builder.Configuration.GetSection("WeatherCache"));
builder.Services.AddHttpClient<IOpenMeteoClient, OpenMeteoClient>()
    .AddStandardResilienceHandler(WeatherResilience.Configure);
builder.Services.AddHttpClient<IPirateWeatherClient, PirateWeatherClient>()
    .AddStandardResilienceHandler(WeatherResilience.Configure);
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
builder.Services.AddSingleton<WeatherCache>();
builder.Services.AddSingleton<WeatherForecastOrchestrator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ForecastMetrics>();

var app = builder.Build();

// Build the resolver eagerly so a missing provider API key fails at startup with a clear
// message instead of 500ing the first forecast request.
app.Services.GetRequiredService<WeatherProviderResolver>();

app.MapGet("/api/v1/forecast", WeatherEndpoint.Handle);
app.MapGet("/health", () => Results.Ok());
app.MapGet("/metrics", (ForecastMetrics metrics) => Results.Json(metrics.Snapshot(), WeatherEndpoint.JsonOptions));

app.Run();

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
