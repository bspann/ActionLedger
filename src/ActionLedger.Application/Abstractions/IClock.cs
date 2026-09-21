namespace ActionLedger.Application.Abstractions;

/// <summary>
/// AD-15 — the only time source. Nothing below the Api calls <see cref="DateTimeOffset.UtcNow"/>
/// directly, so "today" is one value per request and every rule that depends on it is testable.
/// </summary>
/// <remarks>
/// <c>SystemClock</c> serves the running host; <c>FixedClock</c> serves the seeder, so a seeded
/// database is byte-identical on every run (AD-21).
/// </remarks>
public interface IClock
{
    /// <summary>The current instant, in UTC.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Today's date in UTC, derived from <see cref="UtcNow"/> so the two cannot disagree.</summary>
    DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}
