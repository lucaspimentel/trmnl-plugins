using WeatherProxy.Models;
using WeatherProxy.Models.OpenMeteo;

namespace WeatherProxy.Services;

public interface IWeatherTransformer
{
    WeatherResponse Transform(OpenMeteoResponse raw, int hours = 25, int days = 6);
}
