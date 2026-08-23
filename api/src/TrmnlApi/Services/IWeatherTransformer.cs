using TrmnlApi.Models;
using TrmnlApi.Models.OpenMeteo;

namespace TrmnlApi.Services;

public interface IWeatherTransformer
{
    WeatherResponse Transform(OpenMeteoResponse raw);
}
