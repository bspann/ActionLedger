using ActionLedger.Application.Review;
using ActionLedger.Domain.Extraction;

namespace ActionLedger.Application.Extraction;

/// <summary>
/// AD-11 — what <c>POST /api/v1/meetings/{id}/runs</c> answers with, for Succeeded and Failed
/// alike. Both are 201: a failed extraction is not an HTTP error (Consistency Conventions, Errors
/// row).
/// </summary>
/// <remarks>
/// Deliberately thin. The caller follows the <c>Location</c> to <c>GET /api/v1/runs/{id}</c> for
/// the metadata and the proposals, so a Failed run and a Succeeded run take the same code path
/// through the client.
/// </remarks>
/// <param name="Id">The new run's id. The <c>Location</c> header resolves to it.</param>
/// <param name="Outcome">Whether the extractor produced a validated answer.</param>
public sealed record RunDto(Guid Id, ExtractionOutcome Outcome);

/// <summary>
/// AD-13 / FR-6, FR-9 — one Extraction Run as Run Detail reads it: every AD-6 field, the notes
/// identity the run read, and the proposals in AI order.
/// </summary>
/// <remarks>
/// Every AD-6 field is present on the representation whatever the outcome, and the token counts are
/// <c>int</c> rather than nullable so Run Detail renders "0" instead of a blank (FR-6).
/// <see cref="Warnings"/> is an array, empty rather than null on a clean run.
/// </remarks>
/// <param name="Id">The run's id.</param>
/// <param name="MeetingId">The Meeting whose notes were read.</param>
/// <param name="MeetingNotesId">The exact notes row that was read (AD-5).</param>
/// <param name="NotesSha256">The notes' own SHA-256, so two runs can be matched or told apart.</param>
/// <param name="StartedByUserId">The User who started the run, from the token's <c>sub</c> (AD-12).</param>
/// <param name="Provider">The configured <c>Ai:Provider</c>.</param>
/// <param name="Model">What the active provider reported as its model.</param>
/// <param name="PromptVersion">The prompt revision the catalog resolved.</param>
/// <param name="SchemaVersion">The committed extraction schema's top-level <c>version</c>.</param>
/// <param name="StartedAt">When the first provider call began, in UTC.</param>
/// <param name="DurationMs">Milliseconds across every attempt.</param>
/// <param name="InputTokens">Prompt tokens. <c>0</c> for the Fake provider, never omitted.</param>
/// <param name="OutputTokens">Completion tokens. <c>0</c> for the Fake provider, never omitted.</param>
/// <param name="Outcome">How the run ended.</param>
/// <param name="FailureReason">
/// Why it failed, or <c>null</c> when it did not. The extractor's own wording, trimmed and clipped
/// to 2,000 characters.
/// </param>
/// <param name="Warnings">One per dropped proposal, each carrying the dropped excerpt. Never null.</param>
/// <param name="Proposals">The kept proposals, in <c>Ordinal</c> order. Empty on a failed run.</param>
public sealed record RunDetailDto(
    Guid Id,
    Guid MeetingId,
    Guid MeetingNotesId,
    string NotesSha256,
    Guid StartedByUserId,
    string Provider,
    string Model,
    string PromptVersion,
    string SchemaVersion,
    DateTimeOffset StartedAt,
    int DurationMs,
    int InputTokens,
    int OutputTokens,
    ExtractionOutcome Outcome,
    string? FailureReason,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ProposedActionDto> Proposals);

/// <summary>
/// AD-13 — one row of Meeting Detail's run list, as <c>GET /api/v1/meetings/{id}/runs</c> answers
/// it. The spine names the two counts; everything else is the subset of AD-6 the row renders.
/// </summary>
/// <remarks>
/// Both counts are computed server-side in the same query as the row, so the list never needs a
/// second read per run and the web never counts proposals itself.
/// </remarks>
/// <param name="Id">The run's id. Run Detail is reached through it.</param>
/// <param name="StartedAt">When the first provider call began, in UTC. The list is newest first by this.</param>
/// <param name="PromptVersion">The prompt revision the catalog resolved.</param>
/// <param name="Provider">The configured <c>Ai:Provider</c> the run went through.</param>
/// <param name="Model">What the active provider reported as its model.</param>
/// <param name="Outcome">How the run ended.</param>
/// <param name="FailureReason">Why it failed, verbatim, or <c>null</c> when it did not.</param>
/// <param name="ProposalCount">Every kept proposal the run minted. <c>0</c> on a failed run.</param>
/// <param name="PendingCount">The proposals still <c>Pending</c>. Equal to the total until Epic 3 decides one.</param>
public sealed record RunSummaryDto(
    Guid Id,
    DateTimeOffset StartedAt,
    string PromptVersion,
    string Provider,
    string Model,
    ExtractionOutcome Outcome,
    string? FailureReason,
    int ProposalCount,
    int PendingCount);
