using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Users;
using ActionLedger.Domain.Extraction;
using ActionLedger.Domain.Users;

namespace ActionLedger.Application.Review;

/// <summary>
/// AD-9 / AD-15 — the single query class that produces <see cref="ProposedActionDto"/>, for Run
/// Detail today and for the Epic 3 Review Screen when it arrives. It is the one place
/// <c>isLowConfidence</c> and <c>suggestedOwnerUserId</c> are computed.
/// </summary>
/// <remarks>
/// <para>
/// The query projects the persisted columns, materializes, and only then sets the two derived
/// values. That is not an oversight: <see cref="OwnerResolver.Match"/> is pure C# with
/// <see cref="StringComparer.OrdinalIgnoreCase"/> semantics EF cannot translate, and
/// <see cref="IExtractionSettings"/> is an Application port with no SQL meaning. Doing it here,
/// once, is what stops Run Detail and the Review Screen ever disagreeing.
/// </para>
/// <para>
/// Ordering is by <c>Ordinal</c> and nothing else. <see cref="IReadDb"/> is untracked and a query
/// planner may return rows in any order it likes, so "AI order" has to be asked for.
/// </para>
/// </remarks>
public sealed class ProposedActionReadModel(IReadDb readDb, IExtractionSettings settings)
{
    /// <summary>One run's proposals, in AI order, with both derived values set.</summary>
    /// <param name="extractionRunId">The run whose proposals to read.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    public async Task<IReadOnlyList<ProposedActionDto>> ForRunAsync(
        Guid extractionRunId,
        CancellationToken cancellationToken = default)
    {
        IQueryable<ProposedActionDto> query = readDb.Query<ProposedAction>()
            .Where(proposal => proposal.ExtractionRunId == extractionRunId)
            .OrderBy(proposal => proposal.Ordinal)
            .Select(proposal => new ProposedActionDto(
                proposal.Id,
                proposal.Ordinal,
                proposal.Description,
                proposal.SuggestedOwner,
                proposal.SuggestedDueDate,
                proposal.Confidence,
                proposal.SourceExcerpt,
                // Placeholders. Both are set below, in memory, for the reason in the remarks.
                false,
                null,
                proposal.ReviewState));

        IReadOnlyList<ProposedActionDto> proposals = await readDb.ToListAsync(query, cancellationToken);

        if (proposals.Count == 0)
        {
            // A failed run has none, and a succeeded run may have kept nothing. Neither is worth a
            // roster query.
            return [];
        }

        IReadOnlyList<UserSummaryDto> roster = await RosterAsync(cancellationToken);

        double threshold = settings.LowConfidenceThreshold;

        return
        [
            .. proposals.Select(proposal => proposal with
            {
                // Strictly below (AD-15). A proposal exactly at the threshold is not flagged: the
                // threshold is the first confidence that is *not* low.
                IsLowConfidence = proposal.Confidence < threshold,
                SuggestedOwnerUserId = OwnerResolver.Match(proposal.SuggestedOwner, roster),
            }),
        ];
    }

    /// <summary>
    /// The roster the match is made against, read once for the whole page.
    /// </summary>
    /// <remarks>
    /// System users are excluded, exactly as <c>UsersQueries.ListAsync</c> excludes them. A
    /// pre-selection the owner picker cannot show is worse than no pre-selection: the picker is
    /// built from that same roster, so matching the Seed identity would hand the web app an id it
    /// has no row for.
    /// </remarks>
    private Task<IReadOnlyList<UserSummaryDto>> RosterAsync(CancellationToken cancellationToken) =>
        readDb.ToListAsync(
            readDb.Query<User>()
                .Where(user => !user.IsSystem)
                .Select(user => new UserSummaryDto(user.Id, user.DisplayName, user.Role)),
            cancellationToken);
}
