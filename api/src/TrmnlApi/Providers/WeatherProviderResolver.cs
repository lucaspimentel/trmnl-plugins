using Microsoft.Extensions.DependencyInjection;

namespace TrmnlApi.Providers;

public class WeatherProviderResolver(IServiceProvider services, IEnumerable<IWeatherProvider> allProviders)
{
    public const string DefaultName = PirateWeatherProvider.ProviderName;

    private readonly IReadOnlyList<IWeatherProvider> _allProviders = allProviders.ToList();

    public IWeatherProvider Resolve(string? name)
    {
        var key = string.IsNullOrEmpty(name) ? DefaultName : name;
        return services.GetKeyedService<IWeatherProvider>(key)
            ?? throw new ArgumentException($"Unknown weather provider: '{key}'.", nameof(name));
    }

    public IReadOnlyList<IWeatherProvider> ResolveChain(string? name)
    {
        var primary = Resolve(name);
        var chain = new List<IWeatherProvider>(_allProviders.Count) { primary };
        foreach (var p in _allProviders)
        {
            if (!string.Equals(p.Name, primary.Name, StringComparison.Ordinal))
            {
                chain.Add(p);
            }
        }
        return chain;
    }
}
