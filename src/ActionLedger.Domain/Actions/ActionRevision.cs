using ActionLedger.Domain.Common;

namespace ActionLedger.Domain.Actions;

/// <summary>
/// ADR-004 / AD-7 — the append-only audit row. It is its own persistence root with no navigation
/// from any aggregate, and it has no mutator of any kind: no setter, no method, no port member that
/// updates or deletes one.
/// </summary>
/// <remarks>
/// <para>
/// Revisions are created only inside an aggregate method — <c>ExtractionRun.AddProposals</c> and
/// <c>ProposedAction.Decide</c>, and later <c>TrackedAction.Transition</c> and
/// <c>TrackedAction.Edit</c>. That is why the constructor and every factory are
/// <c>internal</c>: an interceptor cannot know the actor or the kind, and a caller outside the
/// aggregate could write a row the aggregate never agreed to.
/// </para>
/// <para>
/// Each aggregate method receives <c>now</c> once and stamps every revision it produces with that
/// same instant, so a single call's rows never straddle a second boundary and the Audit Trail's
/// ordering — <see cref="OccurredAt"/> then <see cref="Sequence"/> — is stable.
/// </para>
/// <para>
/// <see cref="ActorUserId"/> is nullable because the AI is not a User. A <c>null</c> actor on an
/// <see cref="RevisionKind.AiProposal"/> row is the fact that the row records, not a missing value:
/// <c>ActionRevisionDto.actorDisplayName</c> is documented as "null means AI" (AD-13).
/// </para>
/// </remarks>
public sealed class ActionRevision : AggregateRoot
{
    /// <summary>The longest field name a <c>FieldEdit</c> revision can name.</summary>
    public const int FieldMaxLength = 100;

    /// <summary>The field a <see cref="RevisionKind.ReviewDecision"/> revision names.</summary>
    public const string ReviewStateField = "ReviewState";

    /// <summary>
    /// The first sequence number, which a brand-new target's first revision takes — with one
    /// exception: the FieldEdits a decision writes against the Tracked Action it just created
    /// continue from the ReviewDecision's sequence (3, 4, 5…) instead of starting here, so the
    /// Audit Trail sorts them after the decision.
    /// </summary>
    public const int FirstSequence = 1;

    private ActionRevision(
        RevisionTargetType targetType,
        Guid targetId,
        int sequence,
        RevisionKind kind,
        string? field,
        string? oldValue,
        string? newValue,
        Guid? actorUserId,
        DateTimeOffset occurredAt)
    {
        TargetType = targetType;
        TargetId = targetId;
        Sequence = sequence;
        Kind = kind;
        Field = field;
        OldValue = oldValue;
        NewValue = newValue;
        ActorUserId = actorUserId;
        OccurredAt = occurredAt;
    }

    /// <summary>Rehydration constructor for EF Core.</summary>
    private ActionRevision()
    {
    }

    /// <summary>What kind of row this revision audits. Paired with <see cref="TargetId"/>.</summary>
    public RevisionTargetType TargetType { get; private set; }

    /// <summary>The id of the row this revision audits. Paired with <see cref="TargetType"/>.</summary>
    public Guid TargetId { get; private set; }

    /// <summary>
    /// Strictly increasing per <c>(TargetType, TargetId)</c>, assigned by the aggregate from the
    /// count it was given. It breaks ties between revisions that share an instant.
    /// </summary>
    public int Sequence { get; private set; }

    /// <summary>What kind of change this row records.</summary>
    public RevisionKind Kind { get; private set; }

    /// <summary>
    /// The field that changed: the edited field's name for a <c>FieldEdit</c>,
    /// <see cref="ReviewStateField"/> for a <c>ReviewDecision</c>, and <c>null</c> for an <c>AiProposal</c>.
    /// </summary>
    public string? Field { get; private set; }

    /// <summary>The value before the change. <c>null</c> when there was no before.</summary>
    public string? OldValue { get; private set; }

    /// <summary>The value after the change. For <see cref="RevisionKind.AiProposal"/>, the proposal as JSON.</summary>
    public string? NewValue { get; private set; }

    /// <summary>
    /// The User who made the change, or <c>null</c> when the AI did. Never a value a request body
    /// supplied (AD-12).
    /// </summary>
    public Guid? ActorUserId { get; private set; }

    /// <summary>When the change happened, in UTC — the one instant its aggregate method was handed.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>
    /// The AD-7 AiProposal revision: one per proposal, minted inside
    /// <c>ExtractionRun.AddProposals</c>.
    /// </summary>
    /// <remarks>
    /// The actor is <c>null</c> — the AI is not a User — the field and old value are <c>null</c>
    /// because there was no prior value to change, and the sequence is
    /// <see cref="FirstSequence"/>: the target is brand new, so this is its first revision by
    /// construction rather than by a count somebody had to look up.
    /// </remarks>
    /// <param name="proposedActionId">The proposal this revision records.</param>
    /// <param name="proposalJson">The proposal as JSON: the five FR-21 fields and nothing else.</param>
    /// <param name="occurredAt">The one instant every revision from this call shares.</param>
    internal static ActionRevision AiProposal(Guid proposedActionId, string proposalJson, DateTimeOffset occurredAt) =>
        new(
            RevisionTargetType.ProposedAction,
            proposedActionId,
            FirstSequence,
            RevisionKind.AiProposal,
            field: null,
            oldValue: null,
            newValue: proposalJson,
            actorUserId: null,
            occurredAt.ToUniversalTime());

    /// <summary>
    /// The AD-7 ReviewDecision revision: one per decision, minted inside
    /// <c>ProposedAction.Decide</c> against the proposal it decides.
    /// </summary>
    /// <param name="proposedActionId">The proposal that was decided.</param>
    /// <param name="sequence">The proposal's next sequence.</param>
    /// <param name="oldValue">The Review State the proposal left.</param>
    /// <param name="newValue">The Review State it entered, with a rejection's reason appended.</param>
    /// <param name="actorUserId">The User who decided (AD-12).</param>
    /// <param name="occurredAt">The one instant every revision from this call shares.</param>
    internal static ActionRevision ReviewDecision(
        Guid proposedActionId,
        int sequence,
        string oldValue,
        string newValue,
        Guid actorUserId,
        DateTimeOffset occurredAt) =>
        new(
            RevisionTargetType.ProposedAction,
            proposedActionId,
            sequence,
            RevisionKind.ReviewDecision,
            ReviewStateField,
            oldValue,
            newValue,
            actorUserId,
            occurredAt.ToUniversalTime());

    /// <summary>
    /// The AD-7 FieldEdit revision: one per field a human changed, minted inside
    /// <c>ProposedAction.Decide</c> against the Tracked Action the edit created.
    /// </summary>
    /// <param name="trackedActionId">The Tracked Action whose field this records.</param>
    /// <param name="sequence">The next sequence in the decision's run of revisions.</param>
    /// <param name="field">The changed field's name.</param>
    /// <param name="oldValue">The value before the change, as text; <c>null</c> when there was none.</param>
    /// <param name="newValue">The value after the change, as text; <c>null</c> when it was cleared.</param>
    /// <param name="actorUserId">The User who made the change (AD-12).</param>
    /// <param name="occurredAt">The one instant every revision from this call shares.</param>
    internal static ActionRevision FieldEdit(
        Guid trackedActionId,
        int sequence,
        string field,
        string? oldValue,
        string? newValue,
        Guid actorUserId,
        DateTimeOffset occurredAt) =>
        new(
            RevisionTargetType.TrackedAction,
            trackedActionId,
            sequence,
            RevisionKind.FieldEdit,
            field,
            oldValue,
            newValue,
            actorUserId,
            occurredAt.ToUniversalTime());
}
