using ActionLedger.Application.Abstractions;

namespace ActionLedger.Infrastructure.Time;

/// <summary>AD-15 — the running host's clock. The only place the wall clock is read.</summary>
internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
