using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Users;
using ActionLedger.Domain.Common;
using ActionLedger.Domain.Extraction;

namespace ActionLedger.Application.Review;

/// <summary>
/// AD-3 / AD-20 — the one write use case behind <c>POST /api/v1/proposed-actions/{id}/decision</c>.
/// It decides a proposal through <see cref="ProposedAction.Decide"/>, the only mutation path, and
/// commits the proposal's new state, the Tracked Action (if any) and the revisions together.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The server chooses Approved or Edited.</strong> The client sends only a verb. An Approve
/// becomes <see cref="DecisionKind.Approved"/> when the description, due date and owner all equal
/// the proposal's — the owner compared with <see cref="OwnerResolver.Match"/>'s pre-selection, so
/// keeping the pre-selected owner is not an edit — and <see cref="DecisionKind.Edited"/> otherwise.
/// The match is only ever a baseline for that diff (AD-9): the Tracked Action's owner is exactly
/// the <c>ownerUserId</c> that was sent, <c>null</c> meaning Unassigned.
/// </para>
/// <para>
/// <strong>Bad input is a 400 before <c>Decide</c> runs.</strong> <c>Decide</c> guards its inputs
/// with <see cref="DomainRuleException"/>, which the Api answers with 409. A blank description is
/// a bad request, not a state conflict, so every input rule with a 400 meaning is checked here
/// first, and only "already decided" reaches the domain's 409.
/// </para>
/// <para>
/// <strong>Losing a race is a 409 too.</strong> Two reviewers who both loaded the Pending proposal
/// both get past <c>Decide</c>; the proposal's <c>xmin</c> token, or the
/// <c>tracked_actions(proposed_action_id)</c> unique index, refuses the second commit, and
/// Infrastructure turns either into <see cref="ConcurrencyConflictException"/>.
/// </para>
/// </remarks>
public sealed class DecideProposalHandler(
    IExtractionRunRepository runs,
    IActionRepository actions,
    IActionRevisionRepository revisions,
    IReadDb readDb,
    ICurrentUser currentUser,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    /// <summary>Decides one proposal and returns the decision the server recorded.</summary>
    /// <param name="proposedActionId">The proposal from the route.</param>
    /// <param name="command">The verb and the values it applies to.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <exception cref="NotFoundException">No proposal has that id.</exception>
    /// <exception cref="ValidationFailedException">A sent value breaks a rule for its verb.</exception>
    /// <exception cref="DomainRuleException">The proposal was already decided.</exception>
    /// <exception cref="ConcurrencyConflictException">Another decision on it committed first.</exception>
    public async Task<ProposalDecisionDto> HandleAsync(
        Guid proposedActionId,
        DecideProposalCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The whole run, so the proposal is a tracked child of its aggregate and its xmin is
        // checked when this commits (AD-20).
        ExtractionRun run = await runs.FindByProposedActionIdAsync(proposedActionId, cancellationToken)
            ?? throw new NotFoundException("Proposed Action", proposedActionId);

        ProposedAction proposal = run.Proposals.Single(candidate => candidate.Id == proposedActionId);

        (DecisionKind kind, DecisionEdits edits) = command.Decision switch
        {
            ReviewVerb.Approve => await ApprovalAsync(proposal, command, cancellationToken),
            ReviewVerb.Reject => Rejection(command),
            // The serializer answers an omitted verb, and the JSON converter an unknown name; an
            // undefined number is the one shape that gets this far.
            _ => throw new ValidationFailedException("A decision is required: Approve or Reject."),
        };

        // AD-12 / AD-15 — each read once. Neither ever comes from the body.
        Guid actorUserId = currentUser.UserId;
        DateTimeOffset now = clock.UtcNow;

        // Throws DomainRuleException — a 409 — when the proposal is no longer Pending.
        DecisionResult result = proposal.Decide(kind, edits, actorUserId, now);

        if (result.TrackedAction is not null)
        {
            actions.Add(result.TrackedAction);
        }

        revisions.AddRange(result.Revisions);

        // AD-20 — exactly one commit, covering the proposal, the Tracked Action and the revisions.
        await unitOfWork.CommitAsync(cancellationToken);

        return new ProposalDecisionDto(
            proposal.Id,
            proposal.ReviewState,
            result.TrackedAction?.Id,
            proposal.DecidedByUserId!.Value,
            proposal.DecidedAt!.Value);
    }

    /// <summary>
    /// Validates an Approve and derives its kind. Every refusal here would otherwise reach
    /// <c>Decide</c> and come back as a 409, or — for an unknown owner — as a foreign-key failure.
    /// </summary>
    private async Task<(DecisionKind Kind, DecisionEdits Edits)> ApprovalAsync(
        ProposedAction proposal,
        DecideProposalCommand command,
        CancellationToken cancellationToken)
    {
        string? description = command.Description;

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ValidationFailedException("A description is required to approve a proposal.");
        }

        if (description.Length > ProposedAction.DescriptionMaxLength)
        {
            throw new ValidationFailedException(
                $"A description cannot exceed {ProposedAction.DescriptionMaxLength} characters.");
        }

        if (!string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new ValidationFailedException("A reason is recorded only when a proposal is rejected.");
        }

        IReadOnlyList<UserSummaryDto> roster = await OwnerRoster.ReadAsync(readDb, cancellationToken);

        if (command.OwnerUserId is Guid owner && !roster.Any(user => user.Id == owner))
        {
            throw new ValidationFailedException("The owner must be a User on the roster, or null for Unassigned.");
        }

        // AD-9 — the baseline the owner is diffed against, and nothing more. It is never assigned.
        Guid? match = OwnerResolver.Match(proposal.SuggestedOwner, roster);

        // Ordinal and untrimmed, exactly as Decide compares, so the two can never disagree about
        // whether this is an edit.
        bool unchanged =
            string.Equals(description, proposal.Description, StringComparison.Ordinal)
            && command.DueDate == proposal.SuggestedDueDate
            && command.OwnerUserId == match;

        return (
            unchanged ? DecisionKind.Approved : DecisionKind.Edited,
            new DecisionEdits(description, command.OwnerUserId, command.DueDate, ProposedOwnerUserId: match, Reason: null));
    }

    /// <summary>
    /// Validates a Reject. Anything else that was sent is ignored, exactly as <c>Decide</c> ignores
    /// it. The length is measured trimmed, because that is what <c>Decide</c> stores and measures.
    /// </summary>
    private static (DecisionKind Kind, DecisionEdits Edits) Rejection(DecideProposalCommand command)
    {
        if (command.Reason is not null && command.Reason.Trim().Length > ProposedAction.RejectionReasonMaxLength)
        {
            throw new ValidationFailedException(
                $"A rejection reason cannot exceed {ProposedAction.RejectionReasonMaxLength} characters.");
        }

        return (DecisionKind.Rejected, new DecisionEdits(null, null, null, null, command.Reason));
    }
}
