using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TrmnlApi.Endpoints;
using TrmnlApi.Observability;
using TrmnlApi.Services;

namespace TrmnlApi.Tests;

// Exercises the real appsettings.json against a provider aliased "Datadog", which is how the
// tracer's direct log submission registers itself. Nothing here mocks the rules: a bad edit to that
// file fails these tests.
public class DatadogLogAllowlistTests
{
    private const string ForecastServedCategory = "TrmnlApi.Observability.ForecastServed";
    private const string EndpointCategory = "TrmnlApi.Endpoints.WeatherEndpoint";
    private const string V2EndpointCategory = "TrmnlApi.Endpoints.WeatherV2Endpoint";
    private const string PlaceResolverCategory = "TrmnlApi.Services.PlaceResolver";
    private const string OrchestratorCategory = "TrmnlApi.Services.WeatherForecastOrchestrator";
    private const string ResilienceCategory = "TrmnlApi.Services.WeatherResilience";
    private const string UnhandledCategory = "TrmnlApi.Observability.UnhandledExceptionLogger";

    [Theory]
    // The events we mean to ship.
    [InlineData(ForecastServedCategory, LogLevel.Information, true)]
    [InlineData(EndpointCategory, LogLevel.Error, true)]           // all providers failed, 502
    [InlineData(V2EndpointCategory, LogLevel.Error, true)]         // all providers failed, or the geocoder did
    [InlineData(PlaceResolverCategory, LogLevel.Warning, true)]    // a lookup turned away before it was made
    [InlineData(OrchestratorCategory, LogLevel.Warning, true)]     // provider failure / stale rescue
    [InlineData(ResilienceCategory, LogLevel.Warning, true)]       // circuit opened or closed
    [InlineData(UnhandledCategory, LogLevel.Error, true)]
    // Deliberately not shipped: the client-cancelled log shares the endpoint's category and is
    // excluded by level alone.
    [InlineData(EndpointCategory, LogLevel.Information, false)]
    [InlineData(V2EndpointCategory, LogLevel.Information, false)]
    [InlineData(OrchestratorCategory, LogLevel.Information, false)]
    // No framework log may reach Datadog, whatever its level.
    [InlineData("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Error, false)]
    [InlineData("Microsoft.AspNetCore.Server.Kestrel", LogLevel.Error, false)]
    [InlineData("System.Net.Http.HttpClient.IOpenMeteoClient.LogicalHandler", LogLevel.Error, false)]
    [InlineData("Polly", LogLevel.Warning, false)]
    public void Allowlist_ShipsOnlyTheChosenEvents(string category, LogLevel level, bool expectShipped)
    {
        var shipped = new List<string>();
        using var factory = BuildFactory(shipped);

        factory.CreateLogger(category).Log(level, "probe");

        Assert.Equal(expectShipped, shipped.Count == 1);
    }

    [Fact]
    public void Allowlist_CategoriesMatchTheRealTypes()
    {
        // The allowlist is a list of strings in a JSON file, so a namespace or class rename would
        // silently stop shipping the event it names. This is what catches that.
        Assert.Equal(ForecastServedCategory, typeof(ForecastServed).FullName);
        Assert.Equal(EndpointCategory, typeof(WeatherEndpoint).FullName);
        Assert.Equal(V2EndpointCategory, typeof(WeatherV2Endpoint).FullName);
        Assert.Equal(PlaceResolverCategory, typeof(PlaceResolver).FullName);
        Assert.Equal(OrchestratorCategory, typeof(WeatherForecastOrchestrator).FullName);
        Assert.Equal(ResilienceCategory, typeof(WeatherResilience).FullName);
        Assert.Equal(UnhandledCategory, typeof(UnhandledExceptionLogger).FullName);
    }

    [Fact]
    public void Allowlist_DeniesByDefault()
    {
        var shipped = new List<string>();
        using var factory = BuildFactory(shipped);

        factory.CreateLogger("Some.Category.Nobody.Thought.Of").LogCritical("probe");

        Assert.Empty(shipped);
    }

    private static ILoggerFactory BuildFactory(List<string> shipped)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.AddConfiguration(configuration.GetSection("Logging"));
            logging.AddProvider(new CapturingProvider(shipped));
        });
        return services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();
    }

    // The alias is what binds this to the Logging:Datadog section, exactly as the tracer's own
    // provider is bound.
    [ProviderAlias("Datadog")]
    private sealed class CapturingProvider(List<string> shipped) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, shipped);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, List<string> shipped) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                shipped.Add($"{category}: {formatter(state, exception)}");
        }
    }
}
