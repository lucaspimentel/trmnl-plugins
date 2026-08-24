namespace TrmnlApi.Services;

/// <summary>
/// The forecast size the API is built around, shared by the endpoint that validates
/// requests and the providers that fetch from upstream. Providers request exactly these
/// limits rather than upstream's maximum, so nothing is fetched that a caller could
/// never receive.
///
/// The tradeoff is that there is no slack for upstream's trailing incomplete days:
/// WeatherTransformer skips a day that arrives with nulls in place of values, so while
/// Open-Meteo has published the last day of the window without computing it, a request
/// at the cap comes back one or two entries short.
/// </summary>
public static class ForecastLimits
{
    /// <summary>Most hours a caller may request, and the most any provider fetches.</summary>
    public const int MaxHours = 25;

    /// <summary>Most days a caller may request, and the most any provider fetches.</summary>
    public const int MaxDays = 14;
}
