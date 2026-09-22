namespace ActionLedger.Domain.Extraction;

/// <summary>
/// One proposal as the extractor produced it, on its way into
/// <see cref="ExtractionRun.AddProposals"/>.
/// </summary>
/// <remarks>
/// <para>
/// It mirrors the Application ring's <c>ExtractedProposal</c> member for member and deliberately is
/// not that type: AD-1 forbids Domain referencing Application, so the aggregate's input shape has
/// to be its own. The handler maps one onto the other in the one place that does it.
/// </para>
/// <para>
/// These same five members — and nothing else — are what FR-21 (<c>prd.md:321</c>) puts in the
/// AiProposal revision's new-value JSON, which is why the aggregate serializes <em>this</em> rather
/// than the entity it mints: <c>Id</c>, <c>Ordinal</c> and <c>ReviewState</c> are the run's
/// bookkeeping, not the proposal the AI made.
/// </para>
/// </remarks>
/// <param name="Description">What is to be done, as the provider worded it.</param>
/// <param name="SuggestedOwner">The name the notes used, or the empty string. Never <c>null</c> (FR-15).</param>
/// <param name="SuggestedDueDate">The due date, or <c>null</c> when the notes stated none.</param>
/// <param name="Confidence">0–1 inclusive. The read model flags it against <c>Ai:LowConfidenceThreshold</c>.</param>
/// <param name="SourceExcerpt">The sentence the proposal quotes, verified against the notes by the extractor.</param>
public sealed record ProposedActionDraft(
    string Description,
    string SuggestedOwner,
    DateOnly? SuggestedDueDate,
    double Confidence,
    string SourceExcerpt);
