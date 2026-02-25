using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WeatherProxy.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddMemoryCache(options => options.SizeLimit = 100);
builder.Services.AddHttpClient<IOpenMeteoClient, OpenMeteoClient>();
builder.Services.AddSingleton<IWeatherTransformer, WeatherTransformer>();
builder.Services.AddSingleton<WeatherCache>();

builder.Build().Run();
