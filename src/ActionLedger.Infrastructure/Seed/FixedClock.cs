using ActionLedger.Application.Abstractions;

namespace ActionLedger.Infrastructure.Seed;

/// <summary>
/// AD-21 — the clock the seeder writes with, so a seeded database is the same on every machine
/// and on every run. The instant is a constant, not "now", which is what makes the idempotence
/// assertion "nothing changed" checkable rather than approximate.
/// </summary>
/// <param name="utcNow">The instant every seeded write is stamped with.</param>
public sealed class FixedClock(DateTimeOffset utcNow) : IClock
{
    /// <summary>
    /// The instant the demo data is pinned to: midnight UTC on the demo date. Story 6.1's
    /// Meetings, runs, and decisions hang off this same value, so their relative dates stay put.
    /// </summary>
    public static readonly DateTimeOffset SeedInstant = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A clock pinned to <see cref="SeedInstant"/>.</summary>
    public static FixedClock AtSeedInstant() => new(SeedInstant);

    public DateTimeOffset UtcNow { get; } = utcNow.ToUniversalTime();
}
