using WeatherProxy.Models;
using WeatherProxy.Models.OpenMeteo;

namespace WeatherProxy.Services;

public interface IWeatherTransformer
{
    WeatherResponse Transform(OpenMeteoResponse raw);
}
