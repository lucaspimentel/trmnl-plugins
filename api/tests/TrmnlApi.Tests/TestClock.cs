using Microsoft.Extensions.Internal;

namespace TrmnlApi.Tests;

/// <summary>
/// Also implements <see cref="ISystemClock"/> so it can be handed to <c>MemoryCacheOptions.Clock</c>,
/// which is what makes absolute expiration (the StaleTtl ceiling) observable in tests.
/// </summary>
internal sealed class TestClock : TimeProvider, ISystemClock
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public DateTimeOffset UtcNow => _now;
    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}
