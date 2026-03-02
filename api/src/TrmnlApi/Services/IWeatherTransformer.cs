using TrmnlApi.Models;
using TrmnlApi.Models.OpenMeteo;

namespace TrmnlApi.Services;

public interface IWeatherTransformer
{
    WeatherResponse Transform(OpenMeteoResponse raw, int hours = 25, int days = 6);
}
