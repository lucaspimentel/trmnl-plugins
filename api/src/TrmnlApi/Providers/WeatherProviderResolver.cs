namespace TrmnlApi.Providers;

public class WeatherProviderResolver
{
    private readonly IReadOnlyList<IWeatherProvider> _ordered;
    private readonly Dictionary<string, IWeatherProvider> _byName;

    public WeatherProviderResolver(IEnumerable<IWeatherProvider> providers, IReadOnlyList<string> configuredOrder)
    {
        if (configuredOrder is null || configuredOrder.Count == 0)
        {
            throw new ArgumentException("Configured provider order must contain at least one provider name.", nameof(configuredOrder));
        }

        var available = providers.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var ordered = new List<IWeatherProvider>(configuredOrder.Count);
        foreach (var name in configuredOrder)
        {
            if (!available.TryGetValue(name, out var provider))
            {
                throw new InvalidOperationException($"Configured weather provider '{name}' is not registered.");
            }
            ordered.Add(provider);
        }

        _ordered = ordered;
        _byName = ordered.ToDictionary(p => p.Name, StringComparer.Ordinal);
    }

    public string DefaultName => _ordered[0].Name;

    public IWeatherProvider Resolve(string? name)
    {
        var key = string.IsNullOrEmpty(name) ? DefaultName : name;
        return _byName.TryGetValue(key, out var provider)
            ? provider
            : throw new ArgumentException($"Unknown weather provider: '{key}'.", nameof(name));
    }

    public IReadOnlyList<IWeatherProvider> ResolveChain(string? name)
    {
        var primary = Resolve(name);
        var chain = new List<IWeatherProvider>(_ordered.Count) { primary };
        foreach (var p in _ordered)
        {
            if (p.Name != primary.Name)
            {
                chain.Add(p);
            }
        }
        return chain;
    }
}
