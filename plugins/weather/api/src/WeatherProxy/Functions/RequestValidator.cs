namespace WeatherProxy.Functions;

public static class RequestValidator
{
    public static bool TryParseCoordinates(string? lat, string? lon, out double latitude, out double longitude)
    {
        latitude = 0;
        longitude = 0;
        return double.TryParse(lat, System.Globalization.CultureInfo.InvariantCulture, out latitude)
            && double.TryParse(lon, System.Globalization.CultureInfo.InvariantCulture, out longitude);
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
