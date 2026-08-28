namespace TrmnlApi.Geo;

/// <summary>
/// Reverse geocoding against the bundled dataset: coordinates in, a label and telemetry out.
/// </summary>
/// <remarks>
/// Implementations must never throw and never block for longer than
/// <see cref="GeoOptions.TimeBudget"/>. A missing label is a cosmetic loss; a failed or slow
/// forecast is not.
/// </remarks>
public interface IPlaceLookup
{
    GeoPlace Find(double latitude, double longitude);
}

/// <summary>Used when no dataset is configured. Every answer is blank.</summary>
public sealed class NullPlaceLookup : IPlaceLookup
{
    public GeoPlace Find(double latitude, double longitude) => GeoPlace.Empty;
}
