namespace ActionLedger.Domain.Actions;

/// <summary>
/// AD-7 — what kind of change an <see cref="ActionRevision"/> records. The kind is what tells a
/// reader of the Audit Trail whether a row is the AI's original proposal, a human's decision, a
/// field edit, or a status change.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="AiProposal"/> is written in Story 2.5. <c>ReviewDecision</c> and the first
/// <c>FieldEdit</c> rows arrive with Story 3.2's <c>ProposedAction.Decide</c>, and
/// <c>StatusChange</c> with <c>TrackedAction.Transition</c>. The enum gains those members with the
/// stories that write them, so nothing publishes a state nothing can reach.
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
}
