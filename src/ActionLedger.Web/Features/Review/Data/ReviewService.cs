using ActionLedger.Web.Core.Api;
using ActionLedger.Web.Core.Errors;
using ActionLedger.Web.Core.Extraction;
using ApiReviewState = ActionLedger.Web.Core.Api.ReviewState;
using ReviewState = ActionLedger.Web.Core.Extraction.ReviewState;

namespace ActionLedger.Web.Features.Review.Data;

/// <summary>
/// AD-14 — the Review feature's HTTP seam, built on the <c>MeetingsService</c> shape. Run Detail
/// reads a run and starts another one through it, and the Review Screen reads a run with its
/// Meeting and decides its proposals. Nothing generated leaves this file.
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
    /// The Review Screen: one run, with its proposals in AI order, and the Meeting whose notes it
    /// read. A 404 from either GET is the outcome's 404.
    /// </summary>
    /// <remarks>
    /// The Meeting is read by the route's id, not the run's: a run of another Meeting is answered
    /// here anyway, carrying its own <see cref="ReviewScreen.MeetingId"/>, and the page refuses it.
    /// The excerpt offsets are the server's (AD-15); nothing here locates an excerpt.
    /// </remarks>
    public Task<ReviewOutcome<ReviewScreen>> GetReviewAsync(
        Guid meetingId,
        Guid runId,
        CancellationToken cancellationToken = default) =>
        CallAsync(
            async token =>
            {
                RunDetailDto run = await client.GetExtractionRunAsync(runId, token).ConfigureAwait(false);
                MeetingDetailDto meeting = await client.GetMeetingAsync(meetingId, token).ConfigureAwait(false);

                return new ReviewScreen(
                    run.Id,
                    run.MeetingId,
                    meeting.Title,
                    meeting.Notes?.Text ?? string.Empty,
                    run.Provider,
                    run.Model,
                    run.PromptVersion,
                    run.StartedAt,
                    ToOutcome(run.Outcome),
                    [.. run.Proposals.Select(ToReviewProposal)]);
            },
            cancellationToken);

    /// <summary>
    /// Starts another run against the Meeting's notes and answers the new run's id and outcome.
    /// Run Detail opens the new run's detail either way; the Review Screen opens its review when it
    /// succeeded. No timeout (spine :192).
    /// </summary>
    public Task<ReviewOutcome<RunStarted>> StartRunAsync(
        Guid meetingId,
        CancellationToken cancellationToken = default) =>
        CallAsync(
            async token =>
            {
                RunDto run = await client.StartExtractionRunAsync(meetingId, token).ConfigureAwait(false);

                return new RunStarted(run.Id, ToOutcome(run.Outcome));
            },
            cancellationToken);

    /// <summary>
    /// Approves a proposal with the values the reviewer settled on. The server alone decides
    /// whether that is Approved or Edited, by diffing them against the proposal; the answer is the
    /// Review State it recorded. No actor and no time are sent: both are the server's (AD-12).
    /// </summary>
    public Task<ReviewOutcome<ReviewState>> ApproveAsync(
        Guid proposalId,
        ProposalValues values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        return DecideAsync(
            proposalId,
            new DecideProposalCommand
            {
                Decision = ReviewVerb.Approve,
                Description = values.Description,
                OwnerUserId = values.OwnerUserId,
                DueDate = ToWire(values.DueDate),
                Reason = null,
            },
            cancellationToken);
    }

    /// <summary>
    /// Rejects a proposal. The reason is optional: it goes trimmed, and a blank one goes as
    /// <c>null</c>, which the Review Screen reads back as "No reason given".
    /// </summary>
    public Task<ReviewOutcome<ReviewState>> RejectAsync(
        Guid proposalId,
        string? reason,
        CancellationToken cancellationToken = default) =>
        DecideAsync(
            proposalId,
            new DecideProposalCommand
            {
                Decision = ReviewVerb.Reject,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            },
            cancellationToken);

    /// <summary>
    /// The one decision POST. <c>ProposalDecisionDto</c> carries no display names, so only the
    /// state crosses back; the page refreshes the run for the rest (AD-15).
    /// </summary>
    private Task<ReviewOutcome<ReviewState>> DecideAsync(
        Guid proposalId,
        DecideProposalCommand command,
        CancellationToken cancellationToken) =>
        CallAsync(
            async token =>
            {
                ProposalDecisionDto decision =
                    await client.DecideProposedActionAsync(proposalId, command, token).ConfigureAwait(false);

                return ToReviewState(decision.ReviewState);
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

    /// <summary>The decision cells are the proposal's decision copy, with the decider's display name.</summary>
    private static RunProposal ToProposal(ProposedActionDto proposal) =>
        new(
            proposal.Id,
            proposal.Ordinal,
            proposal.Description,
            proposal.Confidence,
            proposal.IsLowConfidence,
            ToReviewState(proposal.ReviewState),
            proposal.DecidedByDisplayName,
            proposal.DecidedAt,
            proposal.RejectionReason);

    private static ReviewProposal ToReviewProposal(ProposedActionDto proposal) =>
        new(
            proposal.Id,
            proposal.Ordinal,
            proposal.Description,
            proposal.SuggestedOwner,
            proposal.SuggestedOwnerUserId,
            proposal.SuggestedOwnerDisplayName,
            ToDate(proposal.SuggestedDueDate),
            proposal.Confidence,
            proposal.IsLowConfidence,
            proposal.SourceExcerpt,
            ToReviewState(proposal.ReviewState),
            proposal.DecidedByUserId,
            proposal.DecidedByDisplayName,
            proposal.DecidedAt,
            proposal.RejectionReason,
            proposal.TrackedActionId,
            proposal.DecidedDescription,
            proposal.DecidedOwnerUserId,
            proposal.DecidedOwnerDisplayName,
            ToDate(proposal.DecidedDueDate),
            proposal.ExcerptStart is { } start && proposal.ExcerptLength is { } length ? new ExcerptRange(start, length) : null);

    /// <summary>
    /// The generator types a <c>format: date</c> as a <see cref="DateTimeOffset"/>; the calendar day
    /// is what the server sent, so the time of day and offset are discarded.
    /// </summary>
    private static DateOnly? ToDate(DateTimeOffset? value) =>
        value is { } date ? DateOnly.FromDateTime(date.Date) : null;

    /// <summary>
    /// The inverse of <see cref="ToDate"/>: midnight at offset zero, so the day the converter
    /// writes as <c>yyyy-MM-dd</c> is the day that was chosen, whatever the browser's time zone.
    /// </summary>
    private static DateTimeOffset? ToWire(DateOnly? value) =>
        value is { } date ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;

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
/// <param name="DecidedBy">The decider's display name, or <c>null</c> while Pending.</param>
/// <param name="DecidedAt">When the decision was made, or <c>null</c> while Pending.</param>
/// <param name="RejectionReason">Why it was rejected, or <c>null</c> — including a rejection that gave no reason.</param>
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

/// <summary>A run that was just started: its id and whether it succeeded.</summary>
public sealed record RunStarted(Guid Id, RunOutcome Outcome);

/// <summary>
/// AD-14 — the Review Screen: one run's metadata and proposals, with the Meeting it read, in
/// web-owned types.
/// </summary>
/// <param name="MeetingId">The run's Meeting, which the route's meeting id must match.</param>
/// <param name="MeetingTitle">The route Meeting's title, for the meta line's link.</param>
/// <param name="Notes">The Meeting's notes, verbatim, or the empty string if it has none.</param>
/// <param name="Proposals">The kept proposals, in AI order.</param>
public sealed record ReviewScreen(
    Guid RunId,
    Guid MeetingId,
    string MeetingTitle,
    string Notes,
    string Provider,
    string Model,
    string PromptVersion,
    DateTimeOffset StartedAt,
    RunOutcome Outcome,
    IReadOnlyList<ReviewProposal> Proposals);

/// <summary>
/// One Review Screen proposal card. Every derived value — the flag, the owner match and its name,
/// the excerpt range — is the server's (AD-15).
/// </summary>
/// <param name="SuggestedOwner">The owner as the notes named them, verbatim, or the empty string.</param>
/// <param name="SuggestedOwnerDisplayName">The matched User's display name, or <c>null</c> when nobody matched.</param>
/// <param name="DecidedByDisplayName">The decider's display name, or <c>null</c> while Pending.</param>
/// <param name="TrackedActionId">The Tracked Action an approval or edit created, or <c>null</c>.</param>
/// <param name="DecidedDescription">The Tracked Action's description, or <c>null</c>.</param>
/// <param name="DecidedOwnerDisplayName">The Tracked Action's owner, or <c>null</c> for Unassigned or no Tracked Action.</param>
/// <param name="DecidedDueDate">The Tracked Action's due date, or <c>null</c>.</param>
/// <param name="Excerpt">Where the Source Excerpt sits in the notes, or <c>null</c> when the server did not find it.</param>
public sealed record ReviewProposal(
    Guid Id,
    int Ordinal,
    string Description,
    string SuggestedOwner,
    Guid? SuggestedOwnerUserId,
    string? SuggestedOwnerDisplayName,
    DateOnly? SuggestedDueDate,
    double Confidence,
    bool IsLowConfidence,
    string SourceExcerpt,
    ReviewState ReviewState,
    Guid? DecidedByUserId,
    string? DecidedByDisplayName,
    DateTimeOffset? DecidedAt,
    string? RejectionReason,
    Guid? TrackedActionId,
    string? DecidedDescription,
    Guid? DecidedOwnerUserId,
    string? DecidedOwnerDisplayName,
    DateOnly? DecidedDueDate,
    ExcerptRange? Excerpt);

/// <summary>
/// The values an Approve sends: the proposal's own for a plain Approve, or the edited ones.
/// </summary>
/// <param name="Description">The Tracked Action's description, sent as typed.</param>
/// <param name="OwnerUserId">The owner, or <c>null</c> for Unassigned.</param>
/// <param name="DueDate">The due date, or <c>null</c> for none.</param>
public sealed record ProposalValues(string Description, Guid? OwnerUserId, DateOnly? DueDate);

/// <summary>A range of UTF-16 code units in the notes, as the server located it.</summary>
public sealed record ExcerptRange(int Start, int Length);

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
