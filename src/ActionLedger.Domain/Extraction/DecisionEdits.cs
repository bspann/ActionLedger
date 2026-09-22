namespace ActionLedger.Domain.Extraction;

/// <summary>
/// The values a reviewer sent with a decision, mirroring Story 3.2's request body, plus the owner
/// the Application ring pre-selected.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ProposedOwnerUserId"/> is the <c>OwnerResolver.Match</c> result for the proposal's
/// free-text suggested owner. Domain cannot see users, so the handler supplies it, and it is the
/// baseline the owner is diffed against: keeping the pre-selected owner is not an edit.
/// </para>
/// <para>
/// A rejection reads only <see cref="Reason"/>; an approval or edit refuses one.
/// </para>
/// </remarks>
/// <param name="Description">The description to track. Ignored on a rejection.</param>
/// <param name="OwnerUserId">The owner to track, verbatim; <c>null</c> is Unassigned. Ignored on a rejection.</param>
/// <param name="DueDate">The due date to track, or <c>null</c>. Ignored on a rejection.</param>
/// <param name="ProposedOwnerUserId">The pre-selected owner match, or <c>null</c> when nobody matched.</param>
/// <param name="Reason">A rejection's optional reason. Refused on an approval or edit.</param>
public sealed record DecisionEdits(
    string? Description,
    Guid? OwnerUserId,
    DateOnly? DueDate,
    Guid? ProposedOwnerUserId,
    string? Reason);
