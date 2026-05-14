using TrmnlApi.Models;

namespace TrmnlApi.Providers;

public interface IWeatherProvider
{
    string Name { get; }

    Task<WeatherResponse> GetForecastAsync(
        double latitude,
        double longitude,
        bool metric,
        CancellationToken cancellationToken = default);
}
