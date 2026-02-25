using WeatherProxy.Models.OpenMeteo;

namespace WeatherProxy.Services;

public interface IOpenMeteoClient
{
    Task<OpenMeteoResponse> GetForecastAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}
