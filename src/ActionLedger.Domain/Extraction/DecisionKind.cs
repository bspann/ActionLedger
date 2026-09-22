namespace ActionLedger.Domain.Extraction;

/// <summary>
/// What a reviewer decided about a <see cref="ProposedAction"/>. Each kind names the
/// <see cref="ReviewState"/> it moves the proposal to.
/// </summary>
/// <remarks>
/// The client never chooses between <see cref="Approved"/> and <see cref="Edited"/>. Story 3.2's
/// handler works it out from the edits, and <see cref="ProposedAction.Decide"/> refuses a kind the
/// edits contradict.
/// </remarks>
public enum DecisionKind
{
    /// <summary>Accepted exactly as proposed. Creates a Tracked Action.</summary>
    Approved,

    /// <summary>Accepted with a changed description, owner, or due date. Creates a Tracked Action.</summary>
    Edited,

    /// <summary>Refused, with an optional reason. Creates nothing.</summary>
    Rejected,
}
