using ActionLedger.Domain.Common;

namespace ActionLedger.Domain.Actions;

/// <summary>
/// ADR-004 / AD-7 — the append-only audit row. It is its own persistence root with no navigation
/// from any aggregate, and it has no mutator of any kind: no setter, no method, no port member that
/// updates or deletes one.
/// </summary>
/// <remarks>
/// <para>
/// Revisions are created only inside an aggregate method — in this story
/// <c>ExtractionRun.AddProposals</c>, and later <c>ProposedAction.Decide</c>,
/// <c>TrackedAction.Transition</c> and <c>TrackedAction.Edit</c>. That is why the constructor is
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
    /// <summary>The longest field name a <c>FieldEdit</c> revision can name. Story 3.2's business.</summary>
    public const int FieldMaxLength = 100;

    /// <summary>The first sequence number. A brand-new target has no prior revisions.</summary>
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

    /// <summary>The field that changed, for a <c>FieldEdit</c>. <c>null</c> on every other kind.</summary>
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
}
