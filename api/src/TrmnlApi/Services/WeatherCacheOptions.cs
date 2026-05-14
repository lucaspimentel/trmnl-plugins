namespace TrmnlApi.Services;

public class WeatherCacheOptions
{
    public TimeSpan FreshTtl { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan StaleTtl { get; set; } = TimeSpan.FromHours(2);
}
