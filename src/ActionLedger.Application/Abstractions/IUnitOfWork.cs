namespace ActionLedger.Application.Abstractions;

/// <summary>
/// AD-20 — one commit per use case. A handler modifies whatever roots the use case touches and
/// calls <see cref="CommitAsync"/> exactly once, at the end; the audit rows and outbox rows that
/// the same change produced are part of that commit.
/// </summary>
/// <remarks>
/// Implemented over <c>AppDbContext.SaveChangesAsync</c>. Repositories never commit — they add
/// roots and load them, and nothing else. A repository that saves is the bug this port exists to
/// make visible.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Writes everything the use case changed, in one transaction.</summary>
    /// <returns>The number of rows written.</returns>
    /// <exception cref="ConcurrencyConflictException">
    /// Another writer got there first, or a unique index rejected the write. Infrastructure
    /// translates the provider's exception so no EF Core type reaches Application (AD-1).
    /// </exception>
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
