using ActionLedger.Domain.Common;
using ActionLedger.Domain.Extraction;

namespace ActionLedger.Domain.Actions;

/// <summary>
/// AD-3 / AD-4 — the work a human agreed to track. Only a decision creates one: it is the human's
/// row, and the proposal it came from — the AI's row — is never rewritten to become it.
/// </summary>
/// <remarks>
/// <para>
/// There is no public constructor and no factory. <see cref="ProposedAction.Decide"/> is the only
/// creator, which is how "only Approve or Edit-and-Approve creates a Tracked Action" is a fact of
/// the type rather than a convention a handler has to remember. No endpoint can mint one either,
/// because nothing outside the Domain can.
/// </para>
/// <para>
/// <see cref="OwnerUserId"/> is exactly the owner the decision sent, <c>null</c> meaning
/// Unassigned. The proposal's free-text suggested owner is never copied here; the Application
/// ring's owner match is a pre-selection for the reviewer, not an assignment.
/// </para>
/// <para>
/// Epic 4 adds <c>Transition</c> and <c>Edit</c>. Their revisions target this row and must number
/// on from its current maximum sequence, because <c>Decide</c> numbers its FieldEdits on from the
/// ReviewDecision so the Audit Trail sorts them after it.
/// </para>
/// </remarks>
public sealed class TrackedAction : AggregateRoot
{
    /// <summary>The longest description, the same bound the proposal's description has.</summary>
    public const int DescriptionMaxLength = ProposedAction.DescriptionMaxLength;

    /// <summary>
    /// Only <see cref="ProposedAction.Decide"/> reaches this, having already validated every value.
    /// Raises <see cref="TrackedActionCreated"/> in the same breath.
    /// </summary>
    internal TrackedAction(
        Guid proposedActionId,
        string description,
        Guid? ownerUserId,
        DateOnly? dueDate,
        DecisionKind kind,
        DateTimeOffset createdAt)
    {
        ProposedActionId = proposedActionId;
        Description = description;
        OwnerUserId = ownerUserId;
        DueDate = dueDate;
        Status = ActionStatus.Open;
        CreatedAt = createdAt;

        Raise(new TrackedActionCreated(Id, proposedActionId, kind) { OccurredAt = createdAt });
    }

    /// <summary>Rehydration constructor for EF Core.</summary>
    private TrackedAction()
    {
        Description = string.Empty;
    }

    /// <summary>The proposal this was decided from. <c>tracked_actions(proposed_action_id)</c> is unique (AD-20).</summary>
    public Guid ProposedActionId { get; private set; }

    /// <summary>What is to be done: the proposal's wording, or the reviewer's edit of it. 1–500 characters.</summary>
    public string Description { get; private set; }

    /// <summary>The User who owns this work, or <c>null</c> for Unassigned.</summary>
    public Guid? OwnerUserId { get; private set; }

    /// <summary>When it is due, or <c>null</c>. Stored as a <c>date</c> (AD-10).</summary>
    public DateOnly? DueDate { get; private set; }

    /// <summary>Where the work stands. Always <see cref="ActionStatus.Open"/> until Epic 4.</summary>
    public ActionStatus Status { get; private set; }

    /// <summary>The decision's instant, in UTC, from <c>IClock</c> (AD-15).</summary>
    public DateTimeOffset CreatedAt { get; private set; }
}
