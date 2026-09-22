using ActionLedger.Domain.Extraction;

namespace ActionLedger.Application.Review;

/// <summary>
/// AD-13 — one proposal as both Run Detail and the Epic 3 Review Screen read it. There is one
/// producer of this shape, <see cref="ProposedActionReadModel"/>, so the two screens cannot
/// disagree about whether a proposal is flagged or whose name was matched.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IsLowConfidence"/>, <see cref="SuggestedOwnerUserId"/> and the excerpt span are
/// derived server-side and carried on the wire (AD-15): the web never recomputes any of them.
/// </para>
/// <para>
/// The decision fields are the proposal's decision copy. The <c>Decided*</c> values and
/// <see cref="TrackedActionId"/> are the Tracked Action's current values, so they are <c>null</c>
/// for Pending and Rejected proposals, which have none. In Epic 3 they equal the values at decision
/// time; the FieldEdit revisions, not this shape, are the audit record.
/// </para>
/// </remarks>
/// <param name="Id">The proposal's id.</param>
/// <param name="Ordinal">The zero-based position in the provider's answer. Reads come back in this order.</param>
/// <param name="Description">What is to be done, as the provider worded it.</param>
/// <param name="SuggestedOwner">The name the notes used, verbatim, or the empty string (FR-15).</param>
/// <param name="SuggestedDueDate">The due date the notes stated, or <c>null</c>.</param>
/// <param name="Confidence">0–1 inclusive, as the provider reported it.</param>
/// <param name="SourceExcerpt">The sentence this proposal quotes, verified against the notes.</param>
/// <param name="IsLowConfidence">
/// <c>Confidence &lt; Ai:LowConfidenceThreshold</c>, computed once in the read model (AD-15). A
/// proposal exactly at the threshold is not flagged.
/// </param>
/// <param name="SuggestedOwnerUserId">
/// The User <see cref="OwnerResolver"/> matched, or <c>null</c> when the suggestion is blank,
/// matches nobody, or matches more than one. Never an assignment — only a pre-selection (AD-9).
/// </param>
/// <param name="ReviewState">Where the proposal stands. <c>Pending</c> until a decision moves it, once.</param>
/// <param name="SuggestedOwnerDisplayName">The display name of the User <see cref="SuggestedOwnerUserId"/> names, or <c>null</c>.</param>
/// <param name="DecidedByUserId">Who decided, or <c>null</c> while Pending.</param>
/// <param name="DecidedByDisplayName">The decider's display name, system users included, or <c>null</c> while Pending.</param>
/// <param name="DecidedAt">When it was decided, in UTC, or <c>null</c> while Pending.</param>
/// <param name="RejectionReason">A rejection's reason, or <c>null</c> — including on a rejection that gave none.</param>
/// <param name="TrackedActionId">The Tracked Action an approval or edit created, or <c>null</c>.</param>
/// <param name="DecidedDescription">The Tracked Action's description, or <c>null</c>.</param>
/// <param name="DecidedOwnerUserId">The Tracked Action's owner, or <c>null</c> — for no Tracked Action, or for Unassigned.</param>
/// <param name="DecidedOwnerDisplayName">That owner's display name, or <c>null</c>.</param>
/// <param name="DecidedDueDate">The Tracked Action's due date, or <c>null</c>.</param>
/// <param name="ExcerptStart">
/// The UTF-16 offset in the run's notes where <see cref="SourceExcerpt"/> starts, by
/// <c>ExcerptLocator</c>'s normalized match, or <c>null</c> when it is not found.
/// </param>
/// <param name="ExcerptLength">The span's length in UTF-16 code units, or <c>null</c> with <see cref="ExcerptStart"/>.</param>
public sealed record ProposedActionDto(
    Guid Id,
    int Ordinal,
    string Description,
    string SuggestedOwner,
    DateOnly? SuggestedDueDate,
    double Confidence,
    string SourceExcerpt,
    bool IsLowConfidence,
    Guid? SuggestedOwnerUserId,
    ReviewState ReviewState,
    string? SuggestedOwnerDisplayName,
    Guid? DecidedByUserId,
    string? DecidedByDisplayName,
    DateTimeOffset? DecidedAt,
    string? RejectionReason,
    Guid? TrackedActionId,
    string? DecidedDescription,
    Guid? DecidedOwnerUserId,
    string? DecidedOwnerDisplayName,
    DateOnly? DecidedDueDate,
    int? ExcerptStart,
    int? ExcerptLength);
