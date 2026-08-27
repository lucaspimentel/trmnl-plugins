using TrmnlApi.Models.OpenMeteo;

namespace TrmnlApi.Services;

public interface IOpenMeteoGeocodingClient
{
    /// <summary>
    /// Resolves a place name or postal code to a populated place, or null when nothing matched.
    /// Throws <see cref="HttpRequestException"/> when the geocoder itself fails, which is a
    /// different thing from a miss and must not be reported to the caller as one.
    /// </summary>
    Task<OpenMeteoGeocodingResult?> SearchAsync(string query, CancellationToken cancellationToken = default);
}
