using ActionLedger.Web.Core.Api;
using ActionLedger.Web.Core.Errors;
using ActionLedger.Web.Core.Extraction;
using ApiReviewState = ActionLedger.Web.Core.Api.ReviewState;
using ReviewState = ActionLedger.Web.Core.Extraction.ReviewState;

namespace ActionLedger.Web.Features.Review.Data;

/// <summary>
/// AD-14 — the Review feature's HTTP seam, built on the <c>MeetingsService</c> shape. Run Detail
/// reads a run and starts another one through it; the Epic 3 Review Screen will join it. Nothing
/// generated leaves this file.
/// </summary>
/// <remarks>
/// It keeps its own <see cref="CallAsync{T}"/> and <see cref="ReviewOutcome{T}"/> rather than
/// borrowing the Meetings feature's, because a feature reaching into another feature's Data folder
/// is the cross-feature coupling the folder layout exists to prevent.
/// </remarks>
public sealed class ReviewService(IActionLedgerApiClient client)
{
    /// <summary>One run, with every FR-6 field and its proposals in the order the AI returned them.</summary>
    public Task<ReviewOutcome<RunDetail>> GetRunAsync(
        Guid runId,
        CancellationToken cancellationToken = default) =>
        CallAsync(
            async token =>
            {
                RunDetailDto run = await client.GetExtractionRunAsync(runId, token).ConfigureAwait(false);

                // The server's order is the order: the API reads proposals by ordinal, and this
                // keeps whatever sequence arrived rather than re-sorting it.
                return new RunDetail(
                    run.Id,
                    run.MeetingId,
                    run.Provider,
                    run.Model,
                    run.PromptVersion,
                    run.SchemaVersion,
                    run.StartedAt,
                    run.DurationMs,
                    run.InputTokens,
                    run.OutputTokens,
                    ToOutcome(run.Outcome),
                    run.FailureReason,
                    [.. run.Warnings],
                    [.. run.Proposals.Select(ToProposal)]);
            },
            cancellationToken);

    /// <summary>
    /// Starts another run against the Meeting's notes and answers the new run's id, whatever its
    /// outcome — Run Detail navigates to it either way. No timeout (spine :192).
    /// </summary>
    public Task<ReviewOutcome<Guid>> StartRunAsync(
        Guid meetingId,
        CancellationToken cancellationToken = default) =>
        CallAsync(
            async token =>
            {
                RunDto run = await client.StartExtractionRunAsync(meetingId, token).ConfigureAwait(false);

                return run.Id;
            },
            cancellationToken);

    /// <summary>The same catch arms <c>MeetingsService</c> documents.</summary>
    private static async Task<ReviewOutcome<T>> CallAsync<T>(
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        try
        {
            return ReviewOutcome<T>.Succeeded(await call(cancellationToken).ConfigureAwait(false));
        }
        catch (ApiException exception)
        {
            return ReviewOutcome<T>.Failed(ApiFailures.From(exception));
        }
        catch (HttpRequestException exception)
        {
            return ReviewOutcome<T>.Failed(ApiFailures.From(exception));
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return ReviewOutcome<T>.Failed(ApiFailures.From(exception));
        }
    }

    /// <summary>
    /// The decision slots are <c>null</c> because the contract does not carry them yet:
    /// <c>ProposedActionDto</c> defers them to Story 3.1, which changes only this mapping.
    /// </summary>
    private static RunProposal ToProposal(ProposedActionDto proposal) =>
        new(
            proposal.Id,
            proposal.Ordinal,
            proposal.Description,
            proposal.Confidence,
            proposal.IsLowConfidence,
            ToReviewState(proposal.ReviewState),
            DecidedBy: null,
            DecidedAt: null,
            RejectionReason: null);

    private static RunOutcome ToOutcome(ExtractionOutcome outcome) =>
        outcome switch
        {
            ExtractionOutcome.Succeeded => RunOutcome.Succeeded,
            ExtractionOutcome.Failed => RunOutcome.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "An outcome the contract does not publish."),
        };

    private static ReviewState ToReviewState(ApiReviewState state) =>
        state switch
        {
            ApiReviewState.Pending => ReviewState.Pending,
            ApiReviewState.Approved => ReviewState.Approved,
            ApiReviewState.Edited => ReviewState.Edited,
            ApiReviewState.Rejected => ReviewState.Rejected,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "A review state the contract does not publish."),
        };
}

/// <summary>
/// AD-14 — one Extraction Run as Run Detail renders it: every FR-6 / AD-6 field, in web-owned
/// types. The token counts are <c>int</c>, so the page renders "0" rather than a blank.
/// </summary>
/// <param name="MeetingId">The Meeting the run read, which the route's meeting id must match.</param>
/// <param name="FailureReason">The server's reason, verbatim, or <c>null</c> when the run did not fail.</param>
/// <param name="Warnings">One per dropped proposal, each carrying the dropped excerpt. Never null.</param>
/// <param name="Proposals">The kept proposals, in AI order.</param>
public sealed record RunDetail(
    Guid Id,
    Guid MeetingId,
    string Provider,
    string Model,
    string PromptVersion,
    string SchemaVersion,
    DateTimeOffset StartedAt,
    int DurationMs,
    int InputTokens,
    int OutputTokens,
    RunOutcome Outcome,
    string? FailureReason,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<RunProposal> Proposals);

/// <summary>
/// One Run Detail proposal row. <see cref="IsLowConfidence"/> is the server's flag, never a
/// threshold compared here (AD-15).
/// </summary>
/// <param name="DecidedBy">The decider's display name. Always <c>null</c> until Story 3.1 publishes it.</param>
/// <param name="DecidedAt">When the decision was made. Always <c>null</c> until Story 3.1.</param>
/// <param name="RejectionReason">Why it was rejected. Always <c>null</c> until Story 3.1.</param>
public sealed record RunProposal(
    Guid Id,
    int Ordinal,
    string Description,
    double Confidence,
    bool IsLowConfidence,
    ReviewState ReviewState,
    string? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? RejectionReason);

/// <summary>
/// AD-14 — what every <see cref="ReviewService"/> call returns, mirroring <c>MeetingOutcome</c>.
/// Callers branch on <see cref="Failure"/>, never on <see cref="Value"/>: for
/// <c>ReviewOutcome&lt;Guid&gt;</c> a failure still carries <c>Guid.Empty</c>.
/// </summary>
public sealed record ReviewOutcome<T>
{
    private ReviewOutcome(T? value, ApiFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    /// <summary>The result, or <c>null</c> when the call did not succeed.</summary>
    public T? Value { get; }

    /// <summary>The failure, or <c>null</c> when the call succeeded.</summary>
    public ApiFailure? Failure { get; }

    public static ReviewOutcome<T> Succeeded(T value) => new(value, null);

    public static ReviewOutcome<T> Failed(ApiFailure failure) => new(default, failure);
}
