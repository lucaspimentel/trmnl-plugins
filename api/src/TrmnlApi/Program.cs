using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using TrmnlApi.Functions;
using TrmnlApi.Providers;
using TrmnlApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Railway injects PORT; fall back to the .NET default (8080) when unset.
if (Environment.GetEnvironmentVariable("PORT") is { } port)
{
    builder.WebHost.UseUrls($"http://*:{port}");
}

builder.Services.AddMemoryCache(options => options.SizeLimit = 200);
builder.Services.Configure<WeatherCacheOptions>(builder.Configuration.GetSection("WeatherCache"));
builder.Services.AddHttpClient<IOpenMeteoClient, OpenMeteoClient>()
    .AddStandardResilienceHandler(WeatherResilience.Configure);
builder.Services.AddHttpClient<IPirateWeatherClient, PirateWeatherClient>()
    .AddStandardResilienceHandler(WeatherResilience.Configure);
builder.Services.AddHttpClient("TrmnlApi");
builder.Services.AddSingleton<IWeatherTransformer, WeatherTransformer>();
// Registration order defines the fallback order: requested provider first, then the others in this order.
builder.Services.AddSingleton<IWeatherProvider, PirateWeatherProvider>();
builder.Services.AddSingleton<IWeatherProvider, OpenMeteoProvider>();

var configuredProviders = ParseWeatherProviders(builder.Configuration["WeatherProviders"]);
builder.Services.AddSingleton<WeatherProviderResolver>(sp => new WeatherProviderResolver(
    sp.GetRequiredService<IEnumerable<IWeatherProvider>>(),
    configuredProviders));
builder.Services.AddSingleton<WeatherCache>();
builder.Services.AddSingleton<WeatherForecastOrchestrator>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

app.MapGet("/api/v1/forecast", WeatherEndpoint.Handle);
app.MapGet("/health", () => Results.Ok());

app.Run();

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
