using TrmnlApi.Models.PirateWeather;

namespace TrmnlApi.Services;

public interface IPirateWeatherClient
{
    Task<PirateWeatherResponse> GetForecastAsync(double latitude, double longitude, bool metric = false, CancellationToken cancellationToken = default);
}
