using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TrmnlApi.Providers;
using TrmnlApi.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddMemoryCache(options => options.SizeLimit = 100);
builder.Services.Configure<WeatherCacheOptions>(builder.Configuration.GetSection("WeatherCache"));
builder.Services.AddHttpClient<IOpenMeteoClient, OpenMeteoClient>()
    .AddStandardResilienceHandler();
builder.Services.AddHttpClient<IPirateWeatherClient, PirateWeatherClient>()
    .AddStandardResilienceHandler();
builder.Services.AddHttpClient("TrmnlApi");
builder.Services.AddSingleton<IWeatherTransformer, WeatherTransformer>();
// Each weather provider must be registered three ways for both name-based lookup and fallback to work:
//   1. As a concrete singleton, so the keyed and unkeyed delegates below share one instance.
//   2. As AddKeyedSingleton<IWeatherProvider>, so WeatherProviderResolver.Resolve(name) finds it.
//   3. As AddSingleton<IWeatherProvider>, so WeatherProviderResolver.ResolveChain enumerates it
//      (keyed services are NOT included in IEnumerable<IWeatherProvider>).
// The unkeyed registration order below defines the fallback order for non-requested providers.
builder.Services.AddSingleton<OpenMeteoProvider>();
builder.Services.AddSingleton<PirateWeatherProvider>();
builder.Services.AddKeyedSingleton<IWeatherProvider>(OpenMeteoProvider.ProviderName,
    (sp, _) => sp.GetRequiredService<OpenMeteoProvider>());
builder.Services.AddKeyedSingleton<IWeatherProvider>(PirateWeatherProvider.ProviderName,
    (sp, _) => sp.GetRequiredService<PirateWeatherProvider>());
builder.Services.AddSingleton<IWeatherProvider>(sp => sp.GetRequiredService<PirateWeatherProvider>());
builder.Services.AddSingleton<IWeatherProvider>(sp => sp.GetRequiredService<OpenMeteoProvider>());
builder.Services.AddSingleton<WeatherProviderResolver>();
builder.Services.AddSingleton<WeatherCache>();
builder.Services.AddSingleton<WeatherForecastOrchestrator>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Build().Run();
