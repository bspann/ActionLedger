using System.Globalization;
using ActionLedger.Domain.Common;

namespace ActionLedger.Domain.Extraction;

/// <summary>
/// AD-4 — one thing the AI proposed. It is immutable after creation except for its
/// <see cref="ReviewState"/> and the decision fields Story 3.1 adds: the AI's row and the human's
/// row are never the same row, so nothing here is ever rewritten to record a decision.
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

        // FR-10 — a proposal is created Pending. Nothing in this story changes it; Story 3.2's
        // Decide is the only thing that ever will (AD-3).
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
    /// Where this proposal stands in review. Always <see cref="ReviewState.Pending"/> in this
    /// story; Story 3.2's <c>Decide</c> is the only mutation path AD-3 permits.
    /// </summary>
    public ReviewState ReviewState { get; private set; }

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
