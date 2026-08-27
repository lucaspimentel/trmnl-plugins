namespace TrmnlApi.Services;

public class PlaceCacheOptions
{
    /// <summary>
    /// How long a resolved place is reused. Long, because places do not move: this window trades
    /// nothing but the speed at which a correction upstream is picked up.
    /// </summary>
    public TimeSpan HitTtl { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// How long a miss is remembered. Misses are the cheap case for someone to generate in bulk,
    /// so they have to be cached too or every repeat of the same junk does full work. Shorter than
    /// <see cref="HitTtl"/> only so that a place added upstream becomes reachable within a day.
    /// </summary>
    public TimeSpan MissTtl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Most lookups held at once, hits and misses together.
    /// </summary>
    /// <remarks>
    /// This bounds a cache whose keys are free text, so its real job is to be a ceiling rather
    /// than to fit a working set. It belongs to its own <c>MemoryCache</c> instance and must never
    /// share the forecast cache's budget: a place lookup that can evict forecasts turns free-text
    /// input into a way to empty the forecast cache. The same rule governs the reverse-geocoding
    /// memo in docs/geographic-telemetry.md, for the same reason.
    ///
    /// Entries are a few hundred bytes, so the default is well under a megabyte.
    /// </remarks>
    public int SizeLimit { get; set; } = 5000;
}
