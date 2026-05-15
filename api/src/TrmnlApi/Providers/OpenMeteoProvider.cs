using TrmnlApi.Models;
using TrmnlApi.Services;

namespace TrmnlApi.Providers;

public class OpenMeteoProvider(
    IOpenMeteoClient client,
    IWeatherTransformer transformer) : IWeatherProvider
{
    public const string ProviderName = "open-meteo";

    public string Name => ProviderName;

    public async Task<WeatherResponse> GetForecastAsync(
        double latitude,
        double longitude,
        bool metric,
        CancellationToken cancellationToken = default)
    {
        var raw = await client.GetForecastAsync(latitude, longitude, metric, cancellationToken);
        return transformer.Transform(raw);
    }
}
