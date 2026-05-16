namespace TrmnlApi.Providers;

public class WeatherProviderResolver
{
    public const string DefaultName = PirateWeatherProvider.ProviderName;

    private readonly IReadOnlyList<IWeatherProvider> _all;
    private readonly Dictionary<string, IWeatherProvider> _byName;

    public WeatherProviderResolver(IEnumerable<IWeatherProvider> providers)
    {
        _all = providers.ToList();
        _byName = _all.ToDictionary(p => p.Name, StringComparer.Ordinal);
    }

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
        var chain = new List<IWeatherProvider>(_all.Count) { primary };
        foreach (var p in _all)
        {
            if (p.Name != primary.Name)
            {
                chain.Add(p);
            }
        }
        return chain;
    }
}
