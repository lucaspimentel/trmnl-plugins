namespace TrmnlApi.Geo;

/// <summary>Distances on a sphere, close enough for a radius test.</summary>
public static class GeoDistance
{
    public const double EarthRadiusKm = 6371.0;

    /// <summary>Kilometres per degree of latitude, and of longitude at the equator.</summary>
    public const double KmPerDegree = EarthRadiusKm * Math.PI / 180.0;

    public static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2))
            + (Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
    }

    /// <summary>
    /// How many degrees of longitude a given distance spans at this latitude. Clamped, because the
    /// factor runs away at the poles and an unbounded box would ask the R-tree for the whole world.
    /// </summary>
    public static double LongitudeDegrees(double km, double latitude)
    {
        var scale = Math.Cos(ToRadians(Math.Clamp(latitude, -89.0, 89.0)));
        return km / (KmPerDegree * Math.Max(scale, 0.02));
    }

    public static double LatitudeDegrees(double km) => km / KmPerDegree;

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
