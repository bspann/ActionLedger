using System.Globalization;
using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Common;

namespace ActionLedger.Domain.Extraction;

/// <summary>
/// AD-4 — one thing the AI proposed. It is immutable after creation except for its
/// <see cref="ReviewState"/> and its decision copy, which <see cref="Decide"/> writes once: the AI's
/// row and the human's row are never the same row, so none of the AI's values is ever rewritten to
/// record a decision.
/// </summary>
/// <remarks>
/// <para>
/// This is a child entity of the <see cref="ExtractionRun"/> aggregate, not a root, so it does not
/// inherit <c>AggregateRoot</c>: it has no domain events and no independent lifetime. Its
/// constructor is <c>internal</c> for the reason <c>MeetingNotes</c>' is — nothing outside the
/// aggregate may mint a proposal that belongs to no run. Its id is still a UUIDv7 made here (AD-10).
/// </para>
/// <para>
/// <see cref="Ordinal"/> is the zero-based index of the proposal in the provider's answer, and
/// <c>proposed_action(extraction_run_id, ordinal)</c> is unique (AD-20). Order is a stored fact
/// rather than an insertion accident: <c>IReadDb</c> is untracked and every read orders by it.
/// </para>
/// <para>
/// The length bounds below are the column lengths, and they are the committed extraction schema's
/// bounds. Domain cannot see <c>ExtractionOutputValidator</c> (AD-1), so
/// <c>ProposedActionShapeTests</c> in <c>Application.Tests</c> — which sees both rings — asserts
/// that these and the validator's constants agree, so a validator change cannot silently outgrow a
/// column.
/// </para>
/// </remarks>
public sealed class ProposedAction
{
    /// <summary><c>description.minLength</c> in the committed extraction schema.</summary>
    public const int DescriptionMinLength = 1;

    /// <summary><c>description.maxLength</c> in the committed extraction schema.</summary>
    public const int DescriptionMaxLength = 500;

    /// <summary><c>suggestedOwner.maxLength</c>. There is no minimum: an unstated owner is <c>""</c>.</summary>
    public const int SuggestedOwnerMaxLength = 100;

    /// <summary><c>sourceExcerpt.minLength</c> in the committed extraction schema.</summary>
    public const int SourceExcerptMinLength = 1;

    /// <summary><c>sourceExcerpt.maxLength</c> in the committed extraction schema.</summary>
    public const int SourceExcerptMaxLength = 1000;

    /// <summary><c>confidence.minimum</c> in the committed extraction schema. Inclusive.</summary>
    public const double ConfidenceMinimum = 0;

    /// <summary><c>confidence.maximum</c> in the committed extraction schema. Inclusive.</summary>
    public const double ConfidenceMaximum = 1;

    /// <summary>The longest rejection reason, after trimming. The <c>rejection_reason</c> column's length.</summary>
    public const int RejectionReasonMaxLength = 500;

    /// <summary>
    /// Only <see cref="ExtractionRun.AddProposals"/> reaches this. It is <c>internal</c> rather
    /// than public so nothing outside the aggregate can mint a proposal that belongs to no run —
    /// and therefore carries no ordinal, no revision, and no run metadata.
    /// </summary>
    /// <exception cref="DomainRuleException">The ordinal is negative, or a member is out of range.</exception>
    internal ProposedAction(Guid extractionRunId, int ordinal, ProposedActionDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        Id = Guid.CreateVersion7();
        ExtractionRunId = extractionRunId;
        Ordinal = ordinal < 0
            ? throw new DomainRuleException("A Proposed Action's ordinal cannot be negative.")
            : ordinal;
        Description = RequireText(draft.Description, "description", DescriptionMinLength, DescriptionMaxLength);
        SuggestedOwner = RequireOwner(draft.SuggestedOwner);
        SuggestedDueDate = draft.SuggestedDueDate;
        Confidence = RequireConfidence(draft.Confidence);
        SourceExcerpt = RequireText(draft.SourceExcerpt, "source excerpt", SourceExcerptMinLength, SourceExcerptMaxLength);

        // FR-10 — a proposal is created Pending. Decide is the only thing that ever changes it (AD-3).
        ReviewState = ReviewState.Pending;
    }

    /// <summary>Rehydration constructor for EF Core.</summary>
    private ProposedAction()
    {
        Description = string.Empty;
        SuggestedOwner = string.Empty;
        SourceExcerpt = string.Empty;
    }

    /// <summary>The proposal's identity: a UUIDv7 from <see cref="Guid.CreateVersion7()"/>.</summary>
    public Guid Id { get; private set; }

    /// <summary>The run that produced this proposal. <c>(extraction_run_id, ordinal)</c> is unique (AD-20).</summary>
    public Guid ExtractionRunId { get; private set; }

    /// <summary>
    /// The zero-based position of this proposal in the provider's answer. Every read orders by it,
    /// so "AI order" survives a query planner that returns rows in any order it likes.
    /// </summary>
    public int Ordinal { get; private set; }

    /// <summary>What is to be done, as the provider worded it. 1–500 characters.</summary>
    public string Description { get; private set; }

    /// <summary>
    /// The owner's name as free text — the name the notes used, or the empty string (AD-9, FR-15).
    /// The AI never writes a foreign key; the read model resolves this to a User id for display and
    /// the proposal keeps the free text either way.
    /// </summary>
    public string SuggestedOwner { get; private set; }

    /// <summary>The due date the notes stated, or <c>null</c>. Stored as a <c>date</c> (AD-10).</summary>
    public DateOnly? SuggestedDueDate { get; private set; }

    /// <summary>0–1 inclusive. The read model, and nowhere else, decides whether that is low (AD-15).</summary>
    public double Confidence { get; private set; }

    /// <summary>The sentence this proposal quotes. Verified against the notes before it got here.</summary>
    public string SourceExcerpt { get; private set; }

    /// <summary>
    /// Where this proposal stands in review. <see cref="Decide"/> is the only mutation path AD-3
    /// permits, and it moves a proposal out of <see cref="ReviewState.Pending"/> exactly once.
    /// </summary>
    public ReviewState ReviewState { get; private set; }

    /// <summary>
    /// Who decided, or <c>null</c> while Pending. A copy for fast reads: the ReviewDecision
    /// revision is the source of truth, and the two are written in the same call.
    /// </summary>
    public Guid? DecidedByUserId { get; private set; }

    /// <summary>When it was decided, in UTC, or <c>null</c> while Pending. A copy, like <see cref="DecidedByUserId"/>.</summary>
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>
    /// A rejection's trimmed reason, or <c>null</c> — while Pending, on an approval or edit, and on a
    /// rejection that gave none. A copy, like <see cref="DecidedByUserId"/>.
    /// </summary>
    public string? RejectionReason { get; private set; }

    /// <summary>
    /// AD-3 / AD-7 — decides this proposal: the only way its Review State changes, and the only way a
    /// <see cref="TrackedAction"/> comes into existence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every rule is checked before anything is written, so a refused call leaves the proposal
    /// Pending with a <c>null</c> decision copy.
    /// </para>
    /// <para>
    /// The revisions are the ReviewDecision (sequence 2, against this proposal) and then one
    /// FieldEdit per changed field (sequences 3, 4, 5…, against the Tracked Action), in the fixed
    /// order Description, OwnerUserId, DueDate. They share one instant, so the Audit Trail — one
    /// read over both targets by <c>OccurredAt</c> then <c>Sequence</c> — puts every FieldEdit
    /// after the decision. A Pending proposal has exactly its AiProposal revision by construction,
    /// which is why the decision's sequence needs no lookup.
    /// </para>
    /// </remarks>
    /// <param name="kind">
    /// The decision. <see cref="DecisionKind.Approved"/> requires the edits to equal the proposal;
    /// <see cref="DecisionKind.Edited"/> requires at least one field to differ.
    /// </param>
    /// <param name="edits">The values sent with the decision, and the pre-selected owner they are diffed against.</param>
    /// <param name="actorUserId">The User deciding, from <c>ICurrentUser</c> (AD-12).</param>
    /// <param name="now">The AD-7 shared instant, read once per request from <c>IClock</c>.</param>
    /// <returns>The Tracked Action, if one was created, and the revisions in order.</returns>
    /// <exception cref="DomainRuleException">
    /// The proposal is not Pending, the actor is missing, the kind contradicts the edits, the
    /// description is blank or too long, or the reason is misplaced or too long.
    /// </exception>
    public DecisionResult Decide(DecisionKind kind, DecisionEdits edits, Guid actorUserId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(edits);

        if (ReviewState != ReviewState.Pending)
        {
            throw new DomainRuleException($"This proposal was already decided: it is {ReviewState}.");
        }

        if (actorUserId == Guid.Empty)
        {
            throw new DomainRuleException("A decision must record the User who made it.");
        }

        DateTimeOffset stamped = now.ToUniversalTime();

        return kind switch
        {
            DecisionKind.Approved or DecisionKind.Edited => Accept(kind, edits, actorUserId, stamped),
            DecisionKind.Rejected => Reject(edits.Reason, actorUserId, stamped),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown decision kind."),
        };
    }

    private DecisionResult Accept(DecisionKind kind, DecisionEdits edits, Guid actorUserId, DateTimeOffset stamped)
    {
        string description = RequireText(edits.Description!, "description", DescriptionMinLength, DescriptionMaxLength);

        if (!string.IsNullOrWhiteSpace(edits.Reason))
        {
            throw new DomainRuleException("A reason is recorded only when a proposal is rejected.");
        }

        bool descriptionChanged = !string.Equals(description, Description, StringComparison.Ordinal);
        bool ownerChanged = edits.OwnerUserId != edits.ProposedOwnerUserId;
        bool dueDateChanged = edits.DueDate != SuggestedDueDate;
        bool anyChanged = descriptionChanged || ownerChanged || dueDateChanged;

        if (kind == DecisionKind.Approved && anyChanged)
        {
            throw new DomainRuleException("An approval takes the proposal as it stands; a changed field makes it an edit.");
        }

        if (kind == DecisionKind.Edited && !anyChanged)
        {
            throw new DomainRuleException("An edit must change the description, the owner, or the due date.");
        }

        TrackedAction tracked = new(Id, description, edits.OwnerUserId, edits.DueDate, kind, stamped);

        ReviewState decided = kind == DecisionKind.Approved ? ReviewState.Approved : ReviewState.Edited;

        int sequence = ActionRevision.FirstSequence + 1;

        List<ActionRevision> revisions =
        [
            ActionRevision.ReviewDecision(Id, sequence, nameof(ReviewState.Pending), decided.ToString(), actorUserId, stamped),
        ];

        if (descriptionChanged)
        {
            revisions.Add(ActionRevision.FieldEdit(
                tracked.Id, ++sequence, nameof(TrackedAction.Description), Description, description, actorUserId, stamped));
        }

        if (ownerChanged)
        {
            revisions.Add(ActionRevision.FieldEdit(
                tracked.Id, ++sequence, nameof(TrackedAction.OwnerUserId),
                FormatOwner(edits.ProposedOwnerUserId), FormatOwner(edits.OwnerUserId), actorUserId, stamped));
        }

        if (dueDateChanged)
        {
            revisions.Add(ActionRevision.FieldEdit(
                tracked.Id, ++sequence, nameof(TrackedAction.DueDate),
                FormatDate(SuggestedDueDate), FormatDate(edits.DueDate), actorUserId, stamped));
        }

        Record(decided, actorUserId, stamped, rejectionReason: null);

        return new DecisionResult(kind, tracked, revisions);
    }

    private DecisionResult Reject(string? reason, Guid actorUserId, DateTimeOffset stamped)
    {
        string? trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        if (trimmed is { Length: > RejectionReasonMaxLength })
        {
            throw new DomainRuleException($"A rejection reason cannot exceed {RejectionReasonMaxLength} characters.");
        }

        // FR-13 — the reason rides in NewValue behind a fixed prefix, because AD-7 fixes the
        // revision's columns and FR-12 fixes Field, OldValue and the state name.
        string newValue = trimmed is null
            ? nameof(ReviewState.Rejected)
            : $"{nameof(ReviewState.Rejected)}: {trimmed}";

        ActionRevision decision = ActionRevision.ReviewDecision(
            Id, ActionRevision.FirstSequence + 1, nameof(ReviewState.Pending), newValue, actorUserId, stamped);

        Record(ReviewState.Rejected, actorUserId, stamped, trimmed);

        return new DecisionResult(DecisionKind.Rejected, TrackedAction: null, [decision]);
    }

    private void Record(ReviewState decided, Guid actorUserId, DateTimeOffset stamped, string? rejectionReason)
    {
        ReviewState = decided;
        DecidedByUserId = actorUserId;
        DecidedAt = stamped;
        RejectionReason = rejectionReason;
    }

    private static string? FormatOwner(Guid? ownerUserId) =>
        ownerUserId?.ToString("D", CultureInfo.InvariantCulture);

    private static string? FormatDate(DateOnly? date) =>
        date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string RequireOwner(string suggestedOwner)
    {
        // Null is refused rather than coerced. `ExtractedProposal.SuggestedOwner` is "" and never
        // null, so a null here means the map above lost something, and quietly turning it into ""
        // would publish an unstated owner as an indistinguishable fact.
        if (suggestedOwner is null)
        {
            throw new DomainRuleException("A Proposed Action's suggested owner cannot be null; an unstated owner is an empty string.");
        }

        return suggestedOwner.Length > SuggestedOwnerMaxLength
            ? throw new DomainRuleException(
                $"A Proposed Action's suggested owner cannot exceed {SuggestedOwnerMaxLength} characters.")
            : suggestedOwner;
    }

    private static double RequireConfidence(double confidence) =>
        double.IsNaN(confidence) || confidence < ConfidenceMinimum || confidence > ConfidenceMaximum
            ? throw new DomainRuleException(
                $"A Proposed Action's confidence must be between {ConfidenceMinimum} and {ConfidenceMaximum} inclusive, was {confidence.ToString(CultureInfo.InvariantCulture)}.")
            : confidence;

    /// <summary>
    /// Guards presence and length, and stores the value untrimmed. The text is kept exactly as the
    /// provider returned it: the excerpt has to stay verbatim for the verifier's answer to keep
    /// meaning anything, so leading and trailing whitespace survives.
    /// </summary>
    /// <remarks>
    /// Presence is <see cref="string.IsNullOrWhiteSpace"/>, not <c>IsNullOrEmpty</c>. A description
    /// of three spaces satisfies the schema's <c>minLength</c>, and a row carrying one would ask a
    /// reviewer to approve a blank commitment. <c>ExtractionOutputValidator</c> refuses a blank
    /// description or excerpt first, so the provider's answer fails at the seam and becomes a
    /// persisted Failed run; this guard is the backstop for any other caller.
    /// </remarks>
    private static string RequireText(string value, string field, int minLength, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < minLength)
        {
            throw new DomainRuleException($"A Proposed Action's {field} is required.");
        }

        return value.Length > maxLength
            ? throw new DomainRuleException($"A Proposed Action's {field} cannot exceed {maxLength} characters.")
            : value;
    }
}
