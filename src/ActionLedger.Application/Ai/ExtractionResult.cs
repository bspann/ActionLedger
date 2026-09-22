namespace ActionLedger.Application.Ai;

/// <summary>
/// AD-6 — what every Extraction Run records about the call that produced it, whatever its outcome.
/// </summary>
/// <remarks>
/// Every member is populated and non-null, including on a failed run: a run that cannot be compared
/// to another run is not reproducible, and "we do not know how long it took" is not an answer Run
/// Detail can render. Tokens are <c>int</c> and never nullable — the Fake provider reports
/// <c>0</c>, which Run Detail shows as "0" rather than as a blank (FR-6). The extractor measures
/// <see cref="StartedAt"/> and <see cref="DurationMs"/> because it is the only code that sees both
/// ends of the provider call.
/// </remarks>
/// <param name="Provider">The configured <c>Ai:Provider</c> — <c>Fake</c>, <c>LocalOpenAI</c> or <c>AzureOpenAI</c>.</param>
/// <param name="Model">What the active factory reports as its model. <c>fixture-catalog</c> for the Fake.</param>
/// <param name="PromptVersion">The prompt the catalog resolved, for example <c>v1</c> (AD-6).</param>
/// <param name="SchemaVersion">The committed schema's own top-level <c>version</c> (AD-6).</param>
/// <param name="StartedAt">When the first provider call began, in UTC.</param>
/// <param name="DurationMs">Milliseconds across every attempt, at most two.</param>
/// <param name="InputTokens">Prompt tokens summed over every attempt. <c>0</c> for the Fake.</param>
/// <param name="OutputTokens">Completion tokens summed over every attempt. <c>0</c> for the Fake.</param>
public sealed record ExtractionMetrics(
    string Provider,
    string Model,
    string PromptVersion,
    string SchemaVersion,
    DateTimeOffset StartedAt,
    int DurationMs,
    int InputTokens,
    int OutputTokens);

/// <summary>
/// One validated proposal. Everything here has passed strict deserialization and
/// <see cref="ExtractionOutputValidator"/>, so the date really is a date and the lengths really are
/// in range.
/// </summary>
/// <param name="Description">What is to be done. 1–500 characters.</param>
/// <param name="SuggestedOwner">The name the notes used, or the empty string. Never <c>null</c>.</param>
/// <param name="SuggestedDueDate">The due date, or <c>null</c> when the notes stated none.</param>
/// <param name="Confidence">0–1 inclusive. The Review Screen flags this against <c>Ai:LowConfidenceThreshold</c>.</param>
/// <param name="SourceExcerpt">The sentence the proposal quotes, exactly as the provider returned it.</param>
public sealed record ExtractedProposal(
    string Description,
    string SuggestedOwner,
    DateOnly? SuggestedDueDate,
    double Confidence,
    string SourceExcerpt);

/// <summary>
/// A proposal <see cref="ExcerptVerifier"/> refused, paired with the warning that says why.
/// </summary>
/// <remarks>
/// AD-11 carries drops in the result rather than discarding them, so Story 6.2's scorer reports a
/// drop without re-running the filter, and Run Detail can expand a warning to the excerpt that
/// caused it.
/// </remarks>
/// <param name="Proposal">The proposal as the provider returned it.</param>
/// <param name="Warning">The recorded warning, carrying the dropped excerpt's text verbatim.</param>
public sealed record DroppedProposal(ExtractedProposal Proposal, string Warning);

/// <summary>
/// AD-11 — what <c>IActionExtractor.ExtractAsync</c> answers with. Succeeded or Failed, never a
/// thrown exception for a provider or validation failure.
/// </summary>
/// <remarks>
/// <para>
/// Both outcomes carry <see cref="Metrics"/>, because Story 2.5 persists a Failed run exactly as it
/// persists a succeeded one and the controller returns 201 for both. A failed extraction is not an
/// HTTP error.
/// </para>
/// <para>
/// <see cref="Kept"/> and <see cref="Dropped"/> travel together so nothing downstream re-filters
/// (AD-11, FR-5). A failed result has neither: validation is all-or-nothing per response, so there
/// is no half-accepted answer to report.
/// </para>
/// </remarks>
public sealed record ExtractionResult
{
    private ExtractionResult(
        bool succeeded,
        IReadOnlyList<ExtractedProposal> kept,
        IReadOnlyList<DroppedProposal> dropped,
        IReadOnlyList<string> warnings,
        string? failureReason,
        ExtractionMetrics metrics)
    {
        IsSucceeded = succeeded;
        Kept = kept;
        Dropped = dropped;
        Warnings = warnings;
        FailureReason = failureReason;
        Metrics = metrics;
    }

    /// <summary>Whether the run produced a validated answer.</summary>
    public bool IsSucceeded { get; }

    /// <summary>The proposals that passed validation and whose excerpts verified, in provider order.</summary>
    public IReadOnlyList<ExtractedProposal> Kept { get; }

    /// <summary>The proposals validation passed but <see cref="ExcerptVerifier"/> refused.</summary>
    public IReadOnlyList<DroppedProposal> Dropped { get; }

    /// <summary>One warning per drop, each carrying the dropped excerpt verbatim. Empty on a clean run.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Why the run failed, or <c>null</c> when it did not. Shown to a human verbatim.</summary>
    public string? FailureReason { get; }

    /// <summary>The AD-6 metadata, populated whatever the outcome.</summary>
    public ExtractionMetrics Metrics { get; }

    /// <summary>A run that produced a validated answer, however many proposals survived the filter.</summary>
    /// <param name="kept">The proposals to review, in the order the provider returned them.</param>
    /// <param name="dropped">The proposals whose excerpts could not be found in the notes.</param>
    /// <param name="metrics">The AD-6 metadata for the call.</param>
    /// <param name="warnings">One warning per drop.</param>
    public static ExtractionResult Succeeded(
        IReadOnlyList<ExtractedProposal> kept,
        IReadOnlyList<DroppedProposal> dropped,
        ExtractionMetrics metrics,
        IReadOnlyList<string> warnings) =>
        new(succeeded: true, kept, dropped, warnings, failureReason: null, metrics);

    /// <summary>
    /// A run that did not produce a validated answer after both attempts. Zero kept, zero dropped.
    /// </summary>
    /// <param name="reason">What went wrong, naming the failure. Persisted and shown verbatim.</param>
    /// <param name="metrics">The AD-6 metadata for the attempts that were made.</param>
    public static ExtractionResult Failed(string reason, ExtractionMetrics metrics) =>
        new(succeeded: false, kept: [], dropped: [], warnings: [], reason, metrics);
}
