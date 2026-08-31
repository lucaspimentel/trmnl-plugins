using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TrmnlLegacyProxy;

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

// Fail at startup rather than per request. A missing origin is a deployment mistake, and a proxy
// that starts and then 500s every call is harder to notice than one that never starts.
var origin = builder.Configuration["FORECAST_ORIGIN"]
             ?? throw new InvalidOperationException(
                 "FORECAST_ORIGIN is required: scheme and host to forward to, no trailing slash.");

builder.Services
    .AddHttpClient(ForecastProxy.HttpClientName, client =>
    {
        client.BaseAddress = new Uri(origin, UriKind.Absolute);

        // Above the backend's own 10s total request timeout, so its resilience policy decides the
        // outcome instead of this proxy cutting the call short and reporting a different failure.
        client.Timeout = TimeSpan.FromSeconds(15);
    })
    // No retry handler here on purpose. The backend already retries and falls back between
    // providers; a second layer would multiply upstream load exactly when upstream is struggling.
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        AllowAutoRedirect = false
    });

builder.Build().Run();
