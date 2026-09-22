using ActionLedger.Web.Core;
using ActionLedger.Web.Core.Api;
using ActionLedger.Web.Core.Extraction;
using ActionLedger.Web.Features.Review.Data;
using Xunit;
using ApiReviewState = ActionLedger.Web.Core.Api.ReviewState;
using ReviewState = ActionLedger.Web.Core.Extraction.ReviewState;

namespace ActionLedger.Web.Tests;

/// <summary>
/// AD-14 — the Review feature's only door to the API. Run Detail's and the Review Screen's every
/// field crosses here onto web-owned records, AI order survives the crossing, and the decision
/// fields are the contract's, carried as-is.
/// </summary>
public sealed class ReviewServiceTests
{
    private static readonly Guid MeetingId = Guid.Parse("01999999-0000-7000-8000-00000000beef");

    private static readonly Guid RunId = Guid.Parse("01999999-0000-7000-8000-0000000000aa");

    private static readonly DateTimeOffset DecidedAt = new(2026, 9, 22, 14, 3, 0, TimeSpan.Zero);

    [Fact]
    public async Task Get_run_maps_every_FR6_field()
    {
        StubApiClient client = new();
        client.Run = new RunDetailDto
        {
            Id = RunId,
            MeetingId = MeetingId,
            MeetingNotesId = Guid.Empty,
            NotesSha256 = new string('a', 64),
            StartedByUserId = Guid.Empty,
            Provider = "LocalOpenAI",
            Model = "qwen2.5-7b-instruct",
            PromptVersion = "v1",
            SchemaVersion = "1",
            StartedAt = new DateTimeOffset(2026, 9, 22, 9, 15, 0, TimeSpan.Zero),
            DurationMs = 41_250,
            InputTokens = 1_812,
            OutputTokens = 406,
            Outcome = ExtractionOutcome.Failed,
            FailureReason = "The AI returned invalid output twice.",
            Warnings = ["Dropped: 'Nobody said this.'"],
            Proposals = [],
        };

        ReviewService service = new(client);

        ReviewOutcome<RunDetail> outcome = await service.GetRunAsync(RunId, TestContext.Current.CancellationToken);

        Assert.Null(outcome.Failure);
        Assert.Equal(RunId, client.LastGetExtractionRunId);

        RunDetail run = outcome.Value!;

        Assert.Equal(RunId, run.Id);
        Assert.Equal(MeetingId, run.MeetingId);
        Assert.Equal("LocalOpenAI", run.Provider);
        Assert.Equal("qwen2.5-7b-instruct", run.Model);
        Assert.Equal("v1", run.PromptVersion);
        Assert.Equal("1", run.SchemaVersion);
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 9, 15, 0, TimeSpan.Zero), run.StartedAt);
        Assert.Equal(41_250, run.DurationMs);
        Assert.Equal(1_812, run.InputTokens);
        Assert.Equal(406, run.OutputTokens);
        Assert.Equal(RunOutcome.Failed, run.Outcome);
        Assert.Equal("The AI returned invalid output twice.", run.FailureReason);
        Assert.Equal(["Dropped: 'Nobody said this.'"], run.Warnings);
        Assert.Empty(run.Proposals);
    }

    [Fact]
    public async Task Get_run_keeps_the_proposals_in_the_order_received()
    {
        StubApiClient client = new();

        // Deliberately not ordinal order: the API sends AI order, and the seam must not "fix" it
        // by re-sorting anything.
        client.Run.Proposals =
        [
            Proposal(1, "Book the range.", 0.62, isLowConfidence: true, ApiReviewState.Pending),
            Proposal(0, "Order the replacement scanners.", 0.91, isLowConfidence: false, ApiReviewState.Edited),
        ];

        ReviewService service = new(client);

        IReadOnlyList<RunProposal> proposals = (await service.GetRunAsync(RunId, TestContext.Current.CancellationToken)).Value!.Proposals;

        Assert.Equal(["Book the range.", "Order the replacement scanners."], proposals.Select(proposal => proposal.Description));
        Assert.Equal([1, 0], proposals.Select(proposal => proposal.Ordinal));

        // The server's flag, carried as-is — never recomputed from the confidence (AD-15).
        Assert.True(proposals[0].IsLowConfidence);
        Assert.False(proposals[1].IsLowConfidence);
        Assert.Equal(0.62, proposals[0].Confidence);

        Assert.Equal(ReviewState.Pending, proposals[0].ReviewState);
        Assert.Equal(ReviewState.Edited, proposals[1].ReviewState);
    }

    [Fact]
    public async Task Get_run_fills_the_decision_cells_from_the_decision_copy()
    {
        StubApiClient client = new();

        ProposedActionDto pending = Proposal(0, "Order the replacement scanners.", 0.91, isLowConfidence: false, ApiReviewState.Pending);
        ProposedActionDto rejected = Proposal(1, "Book the range.", 0.62, isLowConfidence: true, ApiReviewState.Rejected);
        rejected.DecidedByUserId = Guid.CreateVersion7();
        rejected.DecidedByDisplayName = "Dana Whitfield";
        rejected.DecidedAt = DecidedAt;
        rejected.RejectionReason = "discussion item, not an action";

        client.Run.Proposals = [pending, rejected];

        ReviewService service = new(client);

        IReadOnlyList<RunProposal> proposals = (await service.GetRunAsync(RunId, TestContext.Current.CancellationToken)).Value!.Proposals;

        Assert.Null(proposals[0].DecidedBy);
        Assert.Null(proposals[0].DecidedAt);
        Assert.Null(proposals[0].RejectionReason);

        Assert.Equal("Dana Whitfield", proposals[1].DecidedBy);
        Assert.Equal(DecidedAt, proposals[1].DecidedAt);
        Assert.Equal("discussion item, not an action", proposals[1].RejectionReason);
    }

    [Fact]
    public async Task Get_review_maps_the_run_the_meeting_and_every_proposal_field()
    {
        StubApiClient client = new();
        client.Run.Id = RunId;
        client.Run.MeetingId = MeetingId;
        client.Meeting.Title = "Office move planning";
        client.Meeting.Notes = new MeetingNotesDto
        {
            Id = Guid.Empty,
            Text = "Dana will call Bob.",
            Sha256 = new string('a', 64),
            SavedAt = DateTimeOffset.UnixEpoch,
        };

        Guid owner = Guid.CreateVersion7();
        Guid decider = Guid.CreateVersion7();
        Guid tracked = Guid.CreateVersion7();

        ProposedActionDto edited = Proposal(0, "Call Bob.", 0.55, isLowConfidence: true, ApiReviewState.Edited);
        edited.SuggestedOwner = "dana";
        edited.SuggestedOwnerUserId = owner;
        edited.SuggestedOwnerDisplayName = "Dana Whitfield";
        edited.SuggestedDueDate = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
        edited.DecidedByUserId = decider;
        edited.DecidedByDisplayName = "Priya Raman";
        edited.DecidedAt = DecidedAt;
        edited.TrackedActionId = tracked;
        edited.DecidedDescription = "Call Bob today.";
        edited.DecidedOwnerUserId = owner;
        edited.DecidedOwnerDisplayName = "Dana Whitfield";
        edited.DecidedDueDate = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
        edited.ExcerptStart = 0;
        edited.ExcerptLength = 18;

        ProposedActionDto unlocated = Proposal(1, "Book the range.", 0.91, isLowConfidence: false, ApiReviewState.Pending);

        client.Run.Proposals = [edited, unlocated];

        ReviewService service = new(client);

        ReviewOutcome<ReviewScreen> outcome = await service.GetReviewAsync(MeetingId, RunId, TestContext.Current.CancellationToken);

        Assert.Null(outcome.Failure);
        Assert.Equal(RunId, client.LastGetExtractionRunId);
        Assert.Equal(MeetingId, client.LastGetMeetingId);

        ReviewScreen screen = outcome.Value!;

        Assert.Equal(RunId, screen.RunId);
        Assert.Equal(MeetingId, screen.MeetingId);
        Assert.Equal("Office move planning", screen.MeetingTitle);
        Assert.Equal("Dana will call Bob.", screen.Notes);
        Assert.Equal("Fake", screen.Provider);
        Assert.Equal("fixture-catalog", screen.Model);
        Assert.Equal("v1", screen.PromptVersion);
        Assert.Equal(client.Run.StartedAt, screen.StartedAt);
        Assert.Equal(RunOutcome.Succeeded, screen.Outcome);

        Assert.Equal([0, 1], screen.Proposals.Select(proposal => proposal.Ordinal));

        ReviewProposal first = screen.Proposals[0];

        Assert.Equal("Call Bob.", first.Description);
        Assert.Equal("dana", first.SuggestedOwner);
        Assert.Equal(owner, first.SuggestedOwnerUserId);
        Assert.Equal("Dana Whitfield", first.SuggestedOwnerDisplayName);
        Assert.Equal(new DateOnly(2026, 10, 10), first.SuggestedDueDate);
        Assert.Equal(0.55, first.Confidence);
        Assert.True(first.IsLowConfidence);
        Assert.Equal("Call Bob.", first.SourceExcerpt);
        Assert.Equal(ReviewState.Edited, first.ReviewState);
        Assert.Equal(decider, first.DecidedByUserId);
        Assert.Equal("Priya Raman", first.DecidedByDisplayName);
        Assert.Equal(DecidedAt, first.DecidedAt);
        Assert.Null(first.RejectionReason);
        Assert.Equal(tracked, first.TrackedActionId);
        Assert.Equal("Call Bob today.", first.DecidedDescription);
        Assert.Equal(owner, first.DecidedOwnerUserId);
        Assert.Equal("Dana Whitfield", first.DecidedOwnerDisplayName);
        Assert.Equal(new DateOnly(2026, 10, 3), first.DecidedDueDate);
        Assert.Equal(new ExcerptRange(0, 18), first.Excerpt);

        // No span on the wire is no range, never an invented one.
        Assert.Null(screen.Proposals[1].Excerpt);
        Assert.Null(screen.Proposals[1].SuggestedDueDate);
    }

    [Fact]
    public async Task Get_review_of_a_meeting_without_notes_reads_empty_notes()
    {
        StubApiClient client = new();
        ReviewService service = new(client);

        ReviewOutcome<ReviewScreen> outcome = await service.GetReviewAsync(MeetingId, RunId, TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, outcome.Value!.Notes);
    }

    [Fact]
    public async Task Get_review_answers_404_when_the_meeting_is_missing()
    {
        StubApiClient client = new() { GetMeetingThrows = StubApiClient.Problem(404, "The resource was not found.") };
        ReviewService service = new(client);

        ReviewOutcome<ReviewScreen> outcome = await service.GetReviewAsync(MeetingId, RunId, TestContext.Current.CancellationToken);

        Assert.Null(outcome.Value);
        Assert.Equal(404, outcome.Failure?.StatusCode);
    }

    [Fact]
    public async Task Get_review_answers_404_when_the_run_is_missing_without_reading_the_meeting()
    {
        StubApiClient client = new() { GetExtractionRunThrows = StubApiClient.Problem(404, "The resource was not found.") };
        ReviewService service = new(client);

        ReviewOutcome<ReviewScreen> outcome = await service.GetReviewAsync(MeetingId, RunId, TestContext.Current.CancellationToken);

        Assert.Equal(404, outcome.Failure?.StatusCode);
        Assert.Equal(0, client.GetMeetingCalls);
    }

    [Theory]
    [InlineData(ApiReviewState.Pending, ReviewState.Pending)]
    [InlineData(ApiReviewState.Approved, ReviewState.Approved)]
    [InlineData(ApiReviewState.Edited, ReviewState.Edited)]
    [InlineData(ApiReviewState.Rejected, ReviewState.Rejected)]
    public async Task Each_review_state_maps_onto_its_web_owned_namesake(ApiReviewState wire, ReviewState expected)
    {
        StubApiClient client = new();
        client.Run.Proposals = [Proposal(0, "Order the replacement scanners.", 0.91, isLowConfidence: false, wire)];

        ReviewService service = new(client);

        RunProposal proposal = Assert.Single((await service.GetRunAsync(RunId, TestContext.Current.CancellationToken)).Value!.Proposals);

        Assert.Equal(expected, proposal.ReviewState);
    }

    [Theory]
    [InlineData(ExtractionOutcome.Succeeded)]
    [InlineData(ExtractionOutcome.Failed)]
    public async Task Start_run_posts_for_the_meeting_and_answers_the_new_run_whatever_its_outcome(ExtractionOutcome wire)
    {
        Guid newRun = Guid.CreateVersion7();

        StubApiClient client = new() { StartedRun = new RunDto { Id = newRun, Outcome = wire } };
        ReviewService service = new(client);

        ReviewOutcome<RunStarted> outcome = await service.StartRunAsync(MeetingId, TestContext.Current.CancellationToken);

        Assert.Null(outcome.Failure);
        Assert.Equal(newRun, outcome.Value!.Id);
        Assert.Equal(wire is ExtractionOutcome.Succeeded ? RunOutcome.Succeeded : RunOutcome.Failed, outcome.Value.Outcome);
        Assert.Equal(MeetingId, client.LastStartExtractionRunId);
    }

    [Fact]
    public async Task A_missing_run_becomes_a_404_failure()
    {
        StubApiClient client = new() { GetExtractionRunThrows = StubApiClient.Problem(404, "The resource was not found.") };
        ReviewService service = new(client);

        ReviewOutcome<RunDetail> outcome = await service.GetRunAsync(RunId, TestContext.Current.CancellationToken);

        Assert.Null(outcome.Value);
        Assert.Equal(404, outcome.Failure?.StatusCode);
    }

    [Fact]
    public async Task A_transport_failure_on_start_becomes_a_failure_with_no_status()
    {
        StubApiClient client = new() { StartExtractionRunThrows = new HttpRequestException("no route to host") };
        ReviewService service = new(client);

        ReviewOutcome<RunStarted> outcome = await service.StartRunAsync(MeetingId, TestContext.Current.CancellationToken);

        Assert.Equal(0, outcome.Failure?.StatusCode);
        Assert.Equal(Voice.UnexpectedFailureTitle, outcome.Failure?.Title);
    }

    [Fact]
    public async Task A_cancellation_the_caller_asked_for_still_propagates()
    {
        StubApiClient client = new() { GetExtractionRunThrows = new TaskCanceledException() };
        ReviewService service = new(client);

        using CancellationTokenSource source = new();
        await source.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() => service.GetRunAsync(RunId, source.Token));
    }

    // ---------------------------------------------------------------------------------------
    // Decisions
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Approve_sends_the_values_with_the_day_on_the_wire_and_no_reason()
    {
        Guid proposal = Guid.CreateVersion7();
        Guid owner = Guid.CreateVersion7();

        StubApiClient client = new() { DecidedState = ApiReviewState.Edited };
        ReviewService service = new(client);

        ReviewOutcome<ReviewState> outcome = await service.ApproveAsync(
            proposal,
            new ProposalValues("Call Bob today.", owner, new DateOnly(2026, 10, 3)),
            TestContext.Current.CancellationToken);

        Assert.Null(outcome.Failure);
        Assert.Equal(ReviewState.Edited, outcome.Value);

        (Guid id, DecideProposalCommand command) = Assert.Single(client.Decisions);

        Assert.Equal(proposal, id);
        Assert.Equal(ReviewVerb.Approve, command.Decision);
        Assert.Equal("Call Bob today.", command.Description);
        Assert.Equal(owner, command.OwnerUserId);
        Assert.Null(command.Reason);

        // Midnight at offset zero, so the converter's yyyy-MM-dd is the day that was chosen.
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero), command.DueDate);
        Assert.Equal(TimeSpan.Zero, command.DueDate!.Value.Offset);
    }

    [Fact]
    public async Task Approve_with_no_owner_and_no_date_sends_nulls()
    {
        StubApiClient client = new();
        ReviewService service = new(client);

        ReviewOutcome<ReviewState> outcome = await service.ApproveAsync(
            Guid.CreateVersion7(),
            new ProposalValues("Call Bob.", null, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(ReviewState.Approved, outcome.Value);

        DecideProposalCommand command = Assert.Single(client.Decisions).Command;

        Assert.Null(command.OwnerUserId);
        Assert.Null(command.DueDate);
    }

    [Theory]
    [InlineData("  dup  ", "dup")]
    [InlineData("discussion item, not an action", "discussion item, not an action")]
    [InlineData("   ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public async Task Reject_sends_the_reason_trimmed_and_a_blank_one_as_null(string? typed, string? sent)
    {
        Guid proposal = Guid.CreateVersion7();

        StubApiClient client = new() { DecidedState = ApiReviewState.Rejected };
        ReviewService service = new(client);

        ReviewOutcome<ReviewState> outcome = await service.RejectAsync(proposal, typed, TestContext.Current.CancellationToken);

        Assert.Equal(ReviewState.Rejected, outcome.Value);

        (Guid id, DecideProposalCommand command) = Assert.Single(client.Decisions);

        Assert.Equal(proposal, id);
        Assert.Equal(ReviewVerb.Reject, command.Decision);
        Assert.Equal(sent, command.Reason);
        Assert.Null(command.Description);
        Assert.Null(command.OwnerUserId);
        Assert.Null(command.DueDate);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(409)]
    public async Task A_refused_decision_becomes_a_failure_with_its_status(int status)
    {
        StubApiClient client = new() { DecideThrows = StubApiClient.Problem(status, "Refused.") };
        ReviewService service = new(client);

        ReviewOutcome<ReviewState> outcome = await service.RejectAsync(Guid.CreateVersion7(), null, TestContext.Current.CancellationToken);

        Assert.Equal(status, outcome.Failure?.StatusCode);
        Assert.Equal("Refused.", outcome.Failure?.Title);
    }

    [Fact]
    public async Task A_bare_500_or_a_transport_failure_on_approve_becomes_the_unexpected_failure()
    {
        StubApiClient client = new() { DecideThrows = StubApiClient.Bare(500) };
        ReviewService service = new(client);

        ReviewOutcome<ReviewState> outcome = await service.ApproveAsync(
            Guid.CreateVersion7(),
            new ProposalValues("Call Bob.", null, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(500, outcome.Failure?.StatusCode);
        Assert.Equal(Voice.UnexpectedFailureTitle, outcome.Failure?.Title);

        client.DecideThrows = new HttpRequestException("no route to host");

        outcome = await service.ApproveAsync(
            Guid.CreateVersion7(),
            new ProposalValues("Call Bob.", null, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, outcome.Failure?.StatusCode);
        Assert.Equal(Voice.UnexpectedFailureTitle, outcome.Failure?.Title);
    }

    private static ProposedActionDto Proposal(
        int ordinal,
        string description,
        double confidence,
        bool isLowConfidence,
        ApiReviewState state) => new()
    {
        Id = Guid.CreateVersion7(),
        Ordinal = ordinal,
        Description = description,
        SuggestedOwner = string.Empty,
        SuggestedDueDate = null,
        Confidence = confidence,
        SourceExcerpt = description,
        IsLowConfidence = isLowConfidence,
        SuggestedOwnerUserId = null,
        ReviewState = state,
    };
}
