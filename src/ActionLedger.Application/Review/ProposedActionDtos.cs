using ActionLedger.Domain.Extraction;

namespace ActionLedger.Application.Review;

/// <summary>
/// AD-13 — one proposal as both Run Detail and the Epic 3 Review Screen read it. There is one
/// producer of this shape, <see cref="ProposedActionReadModel"/>, so the two screens cannot
/// disagree about whether a proposal is flagged or whose name was matched.
/// </summary>
/// <remarks>
/// <see cref="IsLowConfidence"/> and <see cref="SuggestedOwnerUserId"/> are derived server-side and
/// carried on the wire (AD-15): the web never recomputes either. The decision fields AD-13 also
/// lists — <c>decidedByUserId</c>, <c>decidedByDisplayName</c>, <c>decidedAt</c>,
/// <c>rejectionReason</c>, <c>trackedActionId</c> — are stored since Story 3.1 and written since
/// Story 3.2, but are published here only with the Review Screen that renders them (Story 3.4).
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
    ReviewState ReviewState);
