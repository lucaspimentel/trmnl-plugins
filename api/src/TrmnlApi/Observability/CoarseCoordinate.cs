using System.Globalization;

namespace TrmnlApi.Observability;

/// <summary>
/// The single place a coordinate is coarsened for telemetry. Coordinates are PII, so nothing
/// observable - span tag or log message - may carry one finer than ~11 km.
/// </summary>
/// <remarks>
/// Two rounding hazards make this worth centralising rather than repeating
/// <c>ToString("F1")</c> at each call site:
/// <list type="number">
/// <item>
/// <c>ToString("F1")</c> does not round away from zero. It formats whatever the double holds, so
/// a value sitting exactly on an F1 midpoint formats down while <see cref="Math.Round(double, int,
/// MidpointRounding)"/> rounds up: -71.05 formats as -71.0 but rounds to -71.1. That affects 5% of
/// coordinates on the F2 grid the cache keys on.
/// </item>
/// <item>
/// Rounding once (raw to F1) does not agree with rounding twice (raw to F2, then F1), because the
/// intermediate snap can cross the F1 midpoint: 42.3451 goes to 42.3 directly but to 42.4 via
/// 42.35. That affects a further 5% of realistic coordinates. The orchestrator snaps to F2 before
/// use, so every telemetry surface has to follow the same two-step path or it will disagree with
/// the span tags on one request in twenty.
/// </list>
/// </remarks>
public static class CoarseCoordinate
{
    /// <summary>Cache grid: 0.01 degrees, ~1.1 km. Matches WeatherCache's key precision.</summary>
    private const int CacheDigits = 2;

    /// <summary>Telemetry grid: 0.1 degrees, ~11 km.</summary>
    private const int TelemetryDigits = 1;

    /// <summary>
    /// Coarsens a raw coordinate to the ~11 km telemetry grid, taking the same route as the
    /// orchestrator: snap to the cache grid, then coarsen.
    /// </summary>
    public static double Round(double value) =>
        RoundSnapped(Math.Round(value, CacheDigits, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Coarsens a coordinate that is already snapped to the cache grid. Use this inside the
    /// orchestrator, where the snap has happened; use <see cref="Round"/> everywhere else.
    /// </summary>
    public static double RoundSnapped(double snapped) =>
        Math.Round(snapped, TelemetryDigits, MidpointRounding.AwayFromZero);

    /// <summary>Formats an already-coarsened coordinate. Never pass a raw value.</summary>
    public static string Format(double coarsened) =>
        coarsened.ToString("F1", CultureInfo.InvariantCulture);

    /// <summary>Coarsens and formats a raw coordinate in one step, for log message arguments.</summary>
    public static string ToTag(double value) => Format(Round(value));

    /// <summary>Coarsens and formats a coordinate already snapped to the cache grid.</summary>
    public static string SnappedToTag(double snapped) => Format(RoundSnapped(snapped));
}
