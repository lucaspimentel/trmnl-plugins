using System.Globalization;

namespace TrmnlApi.Functions;

public static class RequestValidator
{
    public static bool TryParseCoordinates(string? lat, string? lon, out double latitude, out double longitude)
    {
        latitude = 0;
        longitude = 0;
        return double.TryParse(lat, CultureInfo.InvariantCulture, out latitude)
            && latitude is >= -90 and <= 90
            && double.TryParse(lon, CultureInfo.InvariantCulture, out longitude)
            && longitude is >= -180 and <= 180;
    }

    public static bool IsValidUnits(string? units)
        => string.IsNullOrEmpty(units) || units is "imperial" or "metric";

    public static bool TryParseRangeParam(string? value, int min, int max, out int result)
    {
        if (string.IsNullOrEmpty(value))
        {
            result = max;
            return true;
        }
        if (!int.TryParse(value, out result) || result < min || result > max)
            return false;
        return true;
    }
}
