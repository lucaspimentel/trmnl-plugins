namespace TrmnlApi.Providers;

public class WeatherProviderResolver
{
    private readonly IReadOnlyList<IWeatherProvider> _ordered;
    private readonly Dictionary<string, IWeatherProvider> _byName;
    private readonly Dictionary<string, IReadOnlyList<IWeatherProvider>> _chainsByName;

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
        _chainsByName = ordered.ToDictionary(
            primary => primary.Name,
            primary => BuildChain(ordered, primary),
            StringComparer.Ordinal);
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
        var key = string.IsNullOrEmpty(name) ? DefaultName : name;
        return _chainsByName.TryGetValue(key, out var chain)
            ? chain
            : throw new ArgumentException($"Unknown weather provider: '{key}'.", nameof(name));
    }

    private static IReadOnlyList<IWeatherProvider> BuildChain(IReadOnlyList<IWeatherProvider> ordered, IWeatherProvider primary)
    {
        var chain = new List<IWeatherProvider>(ordered.Count) { primary };
        foreach (var p in ordered)
        {
            if (p.Name != primary.Name)
            {
                chain.Add(p);
            }
        }
        return chain;
    }
}
