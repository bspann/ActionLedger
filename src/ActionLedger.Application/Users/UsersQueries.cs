using ActionLedger.Application.Abstractions;
using ActionLedger.Domain.Users;

namespace ActionLedger.Application.Users;

/// <summary>
/// AD-2 — the first <c>&lt;Feature&gt;Queries</c>: reads are per-feature query classes over
/// <see cref="IReadDb"/>, never handlers and never repositories. Every later list copies this.
/// </summary>
public sealed class UsersQueries(IReadDb readDb)
{
    /// <summary>
    /// The roster of people who can sign in, paged. System users are excluded — the web app's
    /// owner picker and the roster it loads after login both read this.
    /// </summary>
    /// <param name="page">The 1-based page, clamped by <see cref="Paging.Normalize"/>.</param>
    /// <param name="pageSize">Items per page, clamped by <see cref="Paging.Normalize"/>.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    public async Task<PagedResult<UserSummaryDto>> ListAsync(
        int? page,
        int? pageSize,
        CancellationToken cancellationToken = default)
    {
        (int normalizedPage, int normalizedPageSize) = Paging.Normalize(page, pageSize);

        IQueryable<User> roster = readDb.Query<User>().Where(user => !user.IsSystem);

        // Counted before the window is applied, so `total` is every matching row rather than the
        // length of this page (Consistency Conventions, Paging row).
        int total = await readDb.CountAsync(roster, cancellationToken);

        // Paging.Normalize puts a ceiling on pageSize but not on page, so the offset is computed
        // wide and compared before it is narrowed: `page=2000000000&pageSize=200` would otherwise
        // overflow int, send PostgreSQL a negative OFFSET, and 500 on an anonymous-reachable read.
        long offset = (long)(normalizedPage - Paging.FirstPage) * normalizedPageSize;

        if (offset >= total)
        {
            // Past the end is an empty page that still reports the true total, not an error.
            return new PagedResult<UserSummaryDto>([], normalizedPage, normalizedPageSize, total);
        }

        IQueryable<UserSummaryDto> window = roster
            // Paging without a total order is non-deterministic. Display name is what the roster
            // is read by; the id breaks ties so two people sharing a name still page stably.
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Id)
            // Narrowing is safe: offset < total, and total is an int.
            .Skip((int)offset)
            .Take(normalizedPageSize)
            .Select(user => new UserSummaryDto(user.Id, user.DisplayName, user.Role));

        IReadOnlyList<UserSummaryDto> items = await readDb.ToListAsync(window, cancellationToken);

        return new PagedResult<UserSummaryDto>(items, normalizedPage, normalizedPageSize, total);
    }
}
