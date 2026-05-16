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
    public void Resolve_NullOrEmptyName_ReturnsDefaultProvider(string? name)
    {
        var defaultProvider = new FakeProvider("pirate-weather");
        var resolver = BuildResolver(("pirate-weather", defaultProvider));

        Assert.Same(defaultProvider, resolver.Resolve(name));
    }

    [Fact]
    public void Resolve_KnownName_ReturnsMatchingProvider()
    {
        var openMeteo = new FakeProvider("open-meteo");
        var pirate = new FakeProvider("pirate-weather");
        var resolver = BuildResolver(("open-meteo", openMeteo), ("pirate-weather", pirate));

        Assert.Same(pirate, resolver.Resolve("pirate-weather"));
        Assert.Same(openMeteo, resolver.Resolve("open-meteo"));
    }

    [Fact]
    public void Resolve_UnknownName_ThrowsArgumentException()
    {
        var resolver = BuildResolver(("open-meteo", new FakeProvider("open-meteo")));

        var ex = Assert.Throws<ArgumentException>(() => resolver.Resolve("does-not-exist"));
        Assert.Contains("does-not-exist", ex.Message);
    }

    [Fact]
    public void ResolveChain_RequestedFirstThenRemainingInRegistrationOrder()
    {
        var openMeteo = new FakeProvider("open-meteo");
        var pirate = new FakeProvider("pirate-weather");
        // Registration order: pirate first, then open-meteo.
        var resolver = BuildResolver(("pirate-weather", pirate), ("open-meteo", openMeteo));

        var chain = resolver.ResolveChain("open-meteo");

        Assert.Equal(2, chain.Count);
        Assert.Same(openMeteo, chain[0]);
        Assert.Same(pirate, chain[1]);
    }

    [Fact]
    public void ResolveChain_DefaultRequest_PutsDefaultFirst()
    {
        var openMeteo = new FakeProvider("open-meteo");
        var pirate = new FakeProvider("pirate-weather");
        var resolver = BuildResolver(("open-meteo", openMeteo), ("pirate-weather", pirate));

        var chain = resolver.ResolveChain(null);

        Assert.Equal(2, chain.Count);
        Assert.Equal(WeatherProviderResolver.DefaultName, chain[0].Name);
    }

    [Fact]
    public void ResolveChain_UnknownName_ThrowsArgumentException()
    {
        var resolver = BuildResolver(("open-meteo", new FakeProvider("open-meteo")));

        Assert.Throws<ArgumentException>(() => resolver.ResolveChain("does-not-exist"));
    }

    // Mirrors the keyed + enumerable registrations in Program.cs so a key/type mismatch
    // between Program.cs and a provider's ProviderName is caught at test time.
    [Fact]
    public void Resolve_UsingProductionKeyedRegistrations_ReturnsCorrectConcreteType()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOpenMeteoClient, StubOpenMeteoClient>();
        services.AddSingleton<IWeatherTransformer, StubWeatherTransformer>();
        services.AddSingleton<IPirateWeatherClient, StubPirateWeatherClient>();
        services.AddSingleton<OpenMeteoProvider>();
        services.AddSingleton<PirateWeatherProvider>();
        services.AddKeyedSingleton<IWeatherProvider>(OpenMeteoProvider.ProviderName,
            (sp, _) => sp.GetRequiredService<OpenMeteoProvider>());
        services.AddKeyedSingleton<IWeatherProvider>(PirateWeatherProvider.ProviderName,
            (sp, _) => sp.GetRequiredService<PirateWeatherProvider>());
        services.AddSingleton<IWeatherProvider>(sp => sp.GetRequiredService<PirateWeatherProvider>());
        services.AddSingleton<IWeatherProvider>(sp => sp.GetRequiredService<OpenMeteoProvider>());
        services.AddSingleton<WeatherProviderResolver>();

        var resolver = services.BuildServiceProvider().GetRequiredService<WeatherProviderResolver>();

        Assert.IsType<OpenMeteoProvider>(resolver.Resolve(OpenMeteoProvider.ProviderName));
        Assert.IsType<PirateWeatherProvider>(resolver.Resolve(PirateWeatherProvider.ProviderName));

        var chain = resolver.ResolveChain(OpenMeteoProvider.ProviderName);
        Assert.Equal(2, chain.Count);
        Assert.IsType<OpenMeteoProvider>(chain[0]);
        Assert.IsType<PirateWeatherProvider>(chain[1]);
    }

    private static WeatherProviderResolver BuildResolver(params (string key, IWeatherProvider provider)[] providers)
    {
        var services = new ServiceCollection();
        foreach (var (key, provider) in providers)
        {
            services.AddKeyedSingleton(key, provider);
            // Also register for IEnumerable<IWeatherProvider> so ResolveChain can enumerate.
            services.AddSingleton(provider);
        }
        services.AddSingleton<WeatherProviderResolver>();
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
