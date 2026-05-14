using Microsoft.Extensions.DependencyInjection;
using TrmnlApi.Models;
using TrmnlApi.Providers;

namespace TrmnlApi.Tests;

public class WeatherProviderResolverTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Resolve_NullOrEmptyName_ReturnsDefaultProvider(string? name)
    {
        var defaultProvider = new FakeProvider("open-meteo");
        var resolver = BuildResolver(("open-meteo", defaultProvider));

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

    private static WeatherProviderResolver BuildResolver(params (string key, IWeatherProvider provider)[] providers)
    {
        var services = new ServiceCollection();
        foreach (var (key, provider) in providers)
        {
            services.AddKeyedSingleton(key, provider);
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
}
