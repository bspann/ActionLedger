using ActionLedger.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ActionLedger.Infrastructure.Persistence;

/// <summary>
/// AD-2's read seam over <see cref="AppDbContext"/>. Every query class in the solution composes
/// against this; it is also the only place EF Core's async operators are called on a read.
/// </summary>
/// <remarks>
/// <c>AsNoTracking</c> is applied here rather than left to each caller, so a read cannot become a
/// write by someone mutating a loaded entity and a later <c>CommitAsync</c> picking it up. Writes
/// go through <c>IUserRepository</c> and the tracked context.
/// </remarks>
internal sealed class ReadDb(AppDbContext context) : IReadDb
{
    public IQueryable<T> Query<T>()
        where T : class => context.Set<T>().AsNoTracking();

    public async Task<IReadOnlyList<T>> ToListAsync<T>(
        IQueryable<T> query,
        CancellationToken cancellationToken = default) =>
        await query.ToListAsync(cancellationToken);

    public Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default) =>
        query.CountAsync(cancellationToken);
}
