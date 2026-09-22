using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Users;
using ActionLedger.Domain.Users;

namespace ActionLedger.Application.Review;

/// <summary>
/// AD-9 — the roster <see cref="OwnerResolver.Match"/> is made against, read one way for both of
/// its callers: <see cref="ProposedActionReadModel"/>, which pre-selects an owner, and
/// <see cref="DecideProposalHandler"/>, which diffs the sent owner against that pre-selection and
/// refuses an owner who is not on it.
/// </summary>
/// <remarks>
/// System users are excluded, exactly as <c>UsersQueries.ListAsync</c> excludes them. A
/// pre-selection the owner picker cannot show is worse than no pre-selection: the picker is built
/// from that same roster, so matching the Seed identity would hand the web app an id it has no row
/// for. Keeping the rule here, once, is what stops the read side pre-selecting somebody the write
/// side would refuse.
/// </remarks>
public static class OwnerRoster
{
    /// <summary>Every non-system User, as the boundary shape.</summary>
    /// <param name="readDb">The read seam.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    public static Task<IReadOnlyList<UserSummaryDto>> ReadAsync(IReadDb readDb, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readDb);

        return readDb.ToListAsync(
            readDb.Query<User>()
                .Where(user => !user.IsSystem)
                .Select(user => new UserSummaryDto(user.Id, user.DisplayName, user.Role)),
            cancellationToken);
    }
}
