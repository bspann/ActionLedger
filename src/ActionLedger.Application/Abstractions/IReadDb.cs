namespace ActionLedger.Application.Abstractions;

/// <summary>
/// AD-2 — the read seam. Queries compose over <see cref="Query{T}"/> and materialize through
/// <see cref="ToListAsync{T}"/> and <see cref="CountAsync{T}"/>; writes go through a repository
/// and <see cref="IUnitOfWork"/> instead.
/// </summary>
/// <remarks>
/// <para>
/// The materialization members exist because <c>ToListAsync</c> and <c>CountAsync</c> are EF Core
/// extension methods, and AD-1 Rule 3 keeps <c>Microsoft.EntityFrameworkCore</c> out of this ring
/// entirely — namespace as well as package. Without them a query class would have to enumerate
/// synchronously or reach for EF, and both are wrong. The implementation in Infrastructure is
/// where the provider's async operators are called, and it is the only place they appear.
/// </para>
/// <para>
/// Reads never track. The implementation applies <c>AsNoTracking</c>, so nothing loaded through
/// this port can be mutated into a write by accident.
/// </para>
/// </remarks>
public interface IReadDb
{
    /// <summary>An untracked query over every row of <typeparamref name="T"/>.</summary>
    IQueryable<T> Query<T>()
        where T : class;

    /// <summary>Materializes a composed query.</summary>
    Task<IReadOnlyList<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default);

    /// <summary>Counts the rows a composed query matches, without materializing them.</summary>
    Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default);
}
