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

builder.Services.AddMemoryCache(options => options.SizeLimit = 200);
builder.Services.Configure<WeatherCacheOptions>(builder.Configuration.GetSection("WeatherCache"));
builder.Services.AddHttpClient<IOpenMeteoClient, OpenMeteoClient>()
    .AddStandardResilienceHandler();
builder.Services.AddHttpClient<IPirateWeatherClient, PirateWeatherClient>()
    .AddStandardResilienceHandler();
builder.Services.AddHttpClient("TrmnlApi");
builder.Services.AddSingleton<IWeatherTransformer, WeatherTransformer>();
// Registration order defines the fallback order: requested provider first, then the others in this order.
builder.Services.AddSingleton<IWeatherProvider, PirateWeatherProvider>();
builder.Services.AddSingleton<IWeatherProvider, OpenMeteoProvider>();
builder.Services.AddSingleton<WeatherProviderResolver>();
builder.Services.AddSingleton<WeatherCache>();
builder.Services.AddSingleton<WeatherForecastOrchestrator>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Build().Run();
