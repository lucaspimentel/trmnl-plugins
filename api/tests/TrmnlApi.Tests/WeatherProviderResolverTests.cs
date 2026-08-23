using Microsoft.Extensions.DependencyInjection;
using TrmnlApi.Models;
using TrmnlApi.Models.OpenMeteo;
using TrmnlApi.Models.PirateWeather;
using TrmnlApi.Providers;
using TrmnlApi.Services;

namespace TrmnlApi.Tests;

public class WeatherProviderResolverTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Resolve_NullOrEmptyName_ReturnsFirstConfiguredProvider(string? name)
    {
        var pirate = new FakeProvider("pirate-weather");
        var openMeteo = new FakeProvider("open-meteo");
        var resolver = new WeatherProviderResolver([openMeteo, pirate], ["pirate-weather", "open-meteo"]);

        Assert.Same(pirate, resolver.Resolve(name));
    }

    [Fact]
    public void Resolve_KnownName_ReturnsMatchingProvider()
    {
        var openMeteo = new FakeProvider("open-meteo");
        var pirate = new FakeProvider("pirate-weather");
        var resolver = new WeatherProviderResolver([openMeteo, pirate], ["pirate-weather", "open-meteo"]);

        Assert.Same(pirate, resolver.Resolve("pirate-weather"));
        Assert.Same(openMeteo, resolver.Resolve("open-meteo"));
    }

    [Fact]
    public void Resolve_UnknownName_ThrowsArgumentException()
    {
        var resolver = new WeatherProviderResolver([new FakeProvider("open-meteo")], ["open-meteo"]);

        var ex = Assert.Throws<ArgumentException>(() => resolver.Resolve("does-not-exist"));
        Assert.Contains("does-not-exist", ex.Message);
    }

    [Fact]
    public void Resolve_NameRegisteredButNotConfigured_ThrowsArgumentException()
    {
        var openMeteo = new FakeProvider("open-meteo");
        var pirate = new FakeProvider("pirate-weather");
        // Both providers registered in DI, but only pirate-weather is configured.
        var resolver = new WeatherProviderResolver([openMeteo, pirate], ["pirate-weather"]);

        Assert.Throws<ArgumentException>(() => resolver.Resolve("open-meteo"));
    }

    [Fact]
    public void ResolveChain_FollowsConfiguredOrderNotRegistrationOrder()
    {
        var openMeteo = new FakeProvider("open-meteo");
        var pirate = new FakeProvider("pirate-weather");
        // Registered pirate-first, but configured open-meteo-first.
        var resolver = new WeatherProviderResolver([pirate, openMeteo], ["open-meteo", "pirate-weather"]);

        var chain = resolver.ResolveChain(null);

        Assert.Equal(2, chain.Count);
        Assert.Same(openMeteo, chain[0]);
        Assert.Same(pirate, chain[1]);
    }

    [Fact]
    public void ResolveChain_RequestedFirstThenRemainingInConfiguredOrder()
    {
        var openMeteo = new FakeProvider("open-meteo");
        var pirate = new FakeProvider("pirate-weather");
        var resolver = new WeatherProviderResolver([openMeteo, pirate], ["pirate-weather", "open-meteo"]);

        var chain = resolver.ResolveChain("open-meteo");

        Assert.Equal(2, chain.Count);
        Assert.Same(openMeteo, chain[0]);
        Assert.Same(pirate, chain[1]);
    }

    [Fact]
    public void ResolveChain_DefaultRequest_PutsFirstConfiguredProviderFirst()
    {
        var openMeteo = new FakeProvider("open-meteo");
        var pirate = new FakeProvider("pirate-weather");
        var resolver = new WeatherProviderResolver([openMeteo, pirate], ["pirate-weather", "open-meteo"]);

        var chain = resolver.ResolveChain(null);

        Assert.Equal(2, chain.Count);
        Assert.Equal("pirate-weather", chain[0].Name);
    }

    [Fact]
    public void ResolveChain_ExcludesProvidersNotInConfiguredList()
    {
        var openMeteo = new FakeProvider("open-meteo");
        var pirate = new FakeProvider("pirate-weather");
        // Both registered, only pirate configured.
        var resolver = new WeatherProviderResolver([openMeteo, pirate], ["pirate-weather"]);

        var chain = resolver.ResolveChain(null);

        Assert.Single(chain);
        Assert.Same(pirate, chain[0]);
    }

    [Fact]
    public void ResolveChain_UnknownName_ThrowsArgumentException()
    {
        var resolver = new WeatherProviderResolver([new FakeProvider("open-meteo")], ["open-meteo"]);

        Assert.Throws<ArgumentException>(() => resolver.ResolveChain("does-not-exist"));
    }

    [Fact]
    public void Constructor_EmptyConfiguredOrder_Throws()
    {
        var pirate = new FakeProvider("pirate-weather");

        Assert.Throws<ArgumentException>(() => new WeatherProviderResolver([pirate], []));
    }

    [Fact]
    public void Constructor_ConfiguredNameNotRegistered_Throws()
    {
        var pirate = new FakeProvider("pirate-weather");

        var ex = Assert.Throws<InvalidOperationException>(() => new WeatherProviderResolver([pirate], ["open-meteo"]));
        Assert.Contains("open-meteo", ex.Message);
    }

    // Mirrors Program.cs registrations so a key/type mismatch between Program.cs
    // and a provider's ProviderName is caught at test time.
    [Fact]
    public void Resolve_UsingProductionRegistrations_ReturnsCorrectConcreteType()
    {
        var resolver = BuildProductionResolver([PirateWeatherProvider.ProviderName, OpenMeteoProvider.ProviderName]);

        Assert.IsType<OpenMeteoProvider>(resolver.Resolve(OpenMeteoProvider.ProviderName));
        Assert.IsType<PirateWeatherProvider>(resolver.Resolve(PirateWeatherProvider.ProviderName));

        var chain = resolver.ResolveChain(OpenMeteoProvider.ProviderName);
        Assert.Equal(2, chain.Count);
        Assert.IsType<OpenMeteoProvider>(chain[0]);
        Assert.IsType<PirateWeatherProvider>(chain[1]);
    }

    // A provider left out of WeatherProviders must never be constructed: its client may throw
    // for a missing API key that the deployment deliberately does not configure.
    [Theory]
    [InlineData(OpenMeteoProvider.ProviderName)]
    [InlineData(PirateWeatherProvider.ProviderName)]
    public void Resolve_UnconfiguredProviderIsNeverConstructed(string configuredProvider)
    {
        var resolver = BuildProductionResolver([configuredProvider], throwOnUnconfiguredClient: true);

        var chain = resolver.ResolveChain(null);

        Assert.Single(chain);
        Assert.Equal(configuredProvider, chain[0].Name);
    }

    // Mirrors the keyed registrations in Program.cs. When throwOnUnconfiguredClient is set, each
    // provider's client throws on construction, so resolving one that was not configured fails
    // the test rather than passing silently.
    private static WeatherProviderResolver BuildProductionResolver(
        IReadOnlyList<string> configuredOrder,
        bool throwOnUnconfiguredClient = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWeatherTransformer, StubWeatherTransformer>();

        var failOpenMeteo = throwOnUnconfiguredClient && !configuredOrder.Contains(OpenMeteoProvider.ProviderName);
        var failPirate = throwOnUnconfiguredClient && !configuredOrder.Contains(PirateWeatherProvider.ProviderName);

        if (failOpenMeteo)
        {
            services.AddSingleton<IOpenMeteoClient>(_ => throw new InvalidOperationException("open-meteo client must not be constructed."));
        }
        else
        {
            services.AddSingleton<IOpenMeteoClient, StubOpenMeteoClient>();
        }

        if (failPirate)
        {
            services.AddSingleton<IPirateWeatherClient>(_ => throw new InvalidOperationException("pirate-weather client must not be constructed."));
        }
        else
        {
            services.AddSingleton<IPirateWeatherClient, StubPirateWeatherClient>();
        }

        services.AddKeyedSingleton<IWeatherProvider, PirateWeatherProvider>(PirateWeatherProvider.ProviderName);
        services.AddKeyedSingleton<IWeatherProvider, OpenMeteoProvider>(OpenMeteoProvider.ProviderName);
        services.AddSingleton<WeatherProviderResolver>(sp => new WeatherProviderResolver(
            configuredOrder.Select(name => sp.GetRequiredKeyedService<IWeatherProvider>(name)),
            configuredOrder));

        return services.BuildServiceProvider().GetRequiredService<WeatherProviderResolver>();
    }

    private sealed class FakeProvider(string name) : IWeatherProvider
    {
        public string Name { get; } = name;

        public Task<WeatherResponse> GetForecastAsync(double latitude, double longitude, bool metric, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubOpenMeteoClient : IOpenMeteoClient
    {
        public Task<OpenMeteoResponse> GetForecastAsync(double latitude, double longitude, bool metric = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubPirateWeatherClient : IPirateWeatherClient
    {
        public Task<PirateWeatherResponse> GetForecastAsync(double latitude, double longitude, bool metric = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubWeatherTransformer : IWeatherTransformer
    {
        public WeatherResponse Transform(OpenMeteoResponse raw, int hours = 25, int days = 6) =>
            throw new NotSupportedException();
    }
}
