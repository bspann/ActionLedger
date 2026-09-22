namespace ActionLedger.Domain.Actions;

/// <summary>
/// AD-7 — what kind of change an <see cref="ActionRevision"/> records. The kind is what tells a
/// reader of the Audit Trail whether a row is the AI's original proposal, a human's decision, a
/// field edit, or a status change.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AiProposal"/> is written by <c>ExtractionRun.AddProposals</c>, and
/// <see cref="ReviewDecision"/> and <see cref="FieldEdit"/> by Story 3.1's
/// <c>ProposedAction.Decide</c>. <c>StatusChange</c> arrives with <c>TrackedAction.Transition</c>.
/// The enum gains those members with the stories that write them, so nothing publishes a state
/// nothing can reach.
/// </para>
/// <para>Stored as a string and serialized as a PascalCase string (Consistency Conventions, Enums row).</para>
/// </remarks>
public enum RevisionKind
{
    /// <summary>
    /// The AI's original proposal, minted inside <c>ExtractionRun.AddProposals</c> with a
    /// <c>null</c> actor — the AI is not a User — and the proposal as JSON in the new value.
    /// </summary>
    AiProposal,

    /// <summary>
    /// A human's decision on a proposal: <c>ReviewState</c> from <c>Pending</c> to the new state,
    /// with a rejection's reason carried as <c>Rejected: {reason}</c>. Targets the proposal.
    /// </summary>
    ReviewDecision,

    /// <summary>
    /// One field a human changed. <c>ProposedAction.Decide</c> writes one per field an edit
    /// changed, against the new Tracked Action, after the ReviewDecision.
    /// </summary>
    FieldEdit,
}
