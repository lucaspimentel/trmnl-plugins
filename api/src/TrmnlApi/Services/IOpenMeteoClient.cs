using TrmnlApi.Models.OpenMeteo;

namespace TrmnlApi.Services;

public interface IOpenMeteoClient
{
    Task<OpenMeteoResponse> GetForecastAsync(double latitude, double longitude, bool metric = false, CancellationToken cancellationToken = default);
}
