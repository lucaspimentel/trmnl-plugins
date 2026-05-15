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
builder.Services.AddKeyedSingleton<IWeatherProvider, OpenMeteoProvider>(OpenMeteoProvider.ProviderName);
builder.Services.AddKeyedSingleton<IWeatherProvider, PirateWeatherProvider>(PirateWeatherProvider.ProviderName);
builder.Services.AddSingleton<WeatherProviderResolver>();
builder.Services.AddSingleton<WeatherCache>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Build().Run();
