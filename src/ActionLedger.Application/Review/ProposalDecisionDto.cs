using ActionLedger.Domain.Extraction;

namespace ActionLedger.Application.Review;

/// <summary>
/// AD-13 — what <c>POST /api/v1/proposed-actions/{id}/decision</c> answers with: the decision the
/// server recorded, as the proposal's decision copy now says it.
/// </summary>
/// <param name="ProposedActionId">The proposal that was decided.</param>
/// <param name="ReviewState">
/// <c>Approved</c>, <c>Edited</c> or <c>Rejected</c>. The server chose between the first two by
/// diffing what was sent against the proposal.
/// </param>
/// <param name="TrackedActionId">The new Tracked Action, or <c>null</c> for a rejection.</param>
/// <param name="DecidedByUserId">The User who decided: the token's <c>sub</c>, never the body (AD-12).</param>
/// <param name="DecidedAt">When it was decided, in UTC (AD-15).</param>
public sealed record ProposalDecisionDto(
    Guid ProposedActionId,
    ReviewState ReviewState,
    Guid? TrackedActionId,
    Guid DecidedByUserId,
    DateTimeOffset DecidedAt);
