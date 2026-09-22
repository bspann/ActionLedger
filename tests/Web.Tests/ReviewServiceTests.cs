using ActionLedger.Web.Core;
using ActionLedger.Web.Core.Api;
using ActionLedger.Web.Core.Extraction;
using ActionLedger.Web.Features.Review.Data;
using Xunit;
using ApiReviewState = ActionLedger.Web.Core.Api.ReviewState;
using ReviewState = ActionLedger.Web.Core.Extraction.ReviewState;

namespace ActionLedger.Web.Tests;

/// <summary>
/// AD-14 — the Review feature's only door to the API. Run Detail's every field crosses here onto
/// web-owned records, AI order survives the crossing, and the decision slots stay empty until the
/// contract carries them.
/// </summary>
public sealed class ReviewServiceTests
{
    private static readonly Guid MeetingId = Guid.Parse("01999999-0000-7000-8000-00000000beef");

    private static readonly Guid RunId = Guid.Parse("01999999-0000-7000-8000-0000000000aa");

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
    public async Task Get_run_keeps_the_proposals_in_the_order_received_with_empty_decision_slots()
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

        // Story 3.1 adds these to the contract; until then they are null, not invented.
        Assert.All(proposals, proposal =>
        {
            Assert.Null(proposal.DecidedBy);
            Assert.Null(proposal.DecidedAt);
            Assert.Null(proposal.RejectionReason);
        });
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

        ReviewOutcome<Guid> outcome = await service.StartRunAsync(MeetingId, TestContext.Current.CancellationToken);

        Assert.Null(outcome.Failure);
        Assert.Equal(newRun, outcome.Value);
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

        ReviewOutcome<Guid> outcome = await service.StartRunAsync(MeetingId, TestContext.Current.CancellationToken);

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
