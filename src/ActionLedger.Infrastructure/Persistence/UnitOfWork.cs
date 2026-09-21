using ActionLedger.Application.Abstractions;

namespace ActionLedger.Infrastructure.Persistence;

/// <summary>
/// AD-20 — <see cref="IUnitOfWork.CommitAsync"/> is <c>AppDbContext.SaveChangesAsync</c>, and
/// nothing else in the solution calls it. Repositories add and load; a handler commits once.
/// </summary>
internal sealed class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    public Task<int> CommitAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
