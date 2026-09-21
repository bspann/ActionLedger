using ActionLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ActionLedger.Infrastructure.Seed;

/// <summary>
/// AD-10 — one of exactly two places raw SQL is allowed, and the only one this story needs:
/// <c>pg_advisory_xact_lock</c> has no LINQ equivalent. (The other is
/// <c>OutboxRepository.ClaimBatchAsync</c>, which the webhook epic brings.)
/// </summary>
/// <remarks>
/// The lock is transaction-scoped: PostgreSQL releases it when the seeding transaction commits
/// or rolls back, so a crashed api leaves nothing held. Two api replicas starting at once
/// serialize here, and the second finds every row already present and writes nothing.
/// </remarks>
internal sealed class SeedRepository(AppDbContext context)
{
    /// <summary>
    /// The advisory lock key. Advisory locks share one 64-bit namespace across the database, so
    /// the value is arbitrary but must stay fixed: changing it would stop serializing against an
    /// older api still running.
    /// </summary>
    public const long AdvisoryLockKey = 0x4143_544E_4C47_5230L;

    /// <summary>
    /// Takes the seeding lock, waiting for any other seeder. Must be called inside an open
    /// transaction, or PostgreSQL releases the lock as soon as the statement returns.
    /// </summary>
    public async Task AcquireLockAsync(CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "pg_advisory_xact_lock is scoped to a transaction. Begin one before acquiring the seed lock.");
        }

        await context.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock({AdvisoryLockKey})",
            cancellationToken);
    }
}
