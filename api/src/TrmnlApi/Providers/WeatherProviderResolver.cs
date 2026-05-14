using Microsoft.Extensions.DependencyInjection;

namespace TrmnlApi.Providers;

public class WeatherProviderResolver(IServiceProvider services)
{
    public const string DefaultName = "open-meteo";

    public IWeatherProvider Resolve(string? name)
    {
        var key = string.IsNullOrEmpty(name) ? DefaultName : name;
        return services.GetKeyedService<IWeatherProvider>(key)
            ?? throw new ArgumentException($"Unknown weather provider: '{key}'.", nameof(name));
    }
}
