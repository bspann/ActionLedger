using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Extraction;
using ActionLedger.Application.Review;
using ActionLedger.Domain.Extraction;
using ActionLedger.Domain.Users;
using Xunit;

namespace ActionLedger.Application.Tests.Extraction;

/// <summary>
/// FR-6 and FR-9 — Run Detail's read. Every AD-6 field is on the representation whatever the
/// outcome, and the proposals come from the one read model AD-9 names rather than from a second
/// projection that could drift from it.
/// </summary>
public sealed class RunsQueriesTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 9, 22, 9, 15, 42, TimeSpan.Zero);

    private static readonly DateTimeOffset Now = new(2026, 9, 22, 14, 30, 0, TimeSpan.Zero);

    private const string NotesSha256 = "9f2c1d5e8a4b7c0d3e6f9a2b5c8d1e4f7a0b3c6d9e2f5a8b1c4d7e0f3a6b9c2d";

    private static readonly ExtractionRunMetadata Metrics = new(
        "Fake", "fixture-catalog", "v1", "1", StartedAt, DurationMs: 87, InputTokens: 0, OutputTokens: 0);

    private static readonly User Dana = User.Register(
        "dana", "Dana Whitfield", new string('h', 60), Role.ActionOfficer, Now);

    [Fact]
    public async Task A_succeeded_run_publishes_every_AD6_field_and_its_proposals_in_ai_order()
    {
        Guid meetingId = Guid.CreateVersion7();
        Guid notesId = Guid.CreateVersion7();
        Guid actor = Guid.CreateVersion7();

        ExtractionRun run = Start(meetingId, notesId, actor, ExtractionOutcome.Succeeded, null, []);

        run.AddProposals(
            [
                new("Order the replacement scanners.", "Dana Whitfield", new DateOnly(2026, 9, 25), 0.91, "Dana will order the scanners."),
                new("Book the range.", "Facilities", null, 0.62, "Someone should book the range."),
            ],
            Now);

        RunDetailDto detail = await Over(run).GetAsync(run.Id, TestContext.Current.CancellationToken);

        Assert.Equal(run.Id, detail.Id);
        Assert.Equal(meetingId, detail.MeetingId);
        Assert.Equal(notesId, detail.MeetingNotesId);
        Assert.Equal(NotesSha256, detail.NotesSha256);
        Assert.Equal(actor, detail.StartedByUserId);

        Assert.Equal("Fake", detail.Provider);
        Assert.Equal("fixture-catalog", detail.Model);
        Assert.Equal("v1", detail.PromptVersion);
        Assert.Equal("1", detail.SchemaVersion);
        Assert.Equal(StartedAt, detail.StartedAt);
        Assert.Equal(87, detail.DurationMs);

        // FR-6 — tokens are 0 for the Fake and are published as 0, not omitted.
        Assert.Equal(0, detail.InputTokens);
        Assert.Equal(0, detail.OutputTokens);

        Assert.Equal(ExtractionOutcome.Succeeded, detail.Outcome);
        Assert.Null(detail.FailureReason);
        Assert.Empty(detail.Warnings);

        Assert.Equal([0, 1], detail.Proposals.Select(proposal => proposal.Ordinal));

        // The derived values arrive through ProposedActionReadModel, so composing it rather than
        // reprojecting is what this asserts.
        Assert.Equal(Dana.Id, detail.Proposals[0].SuggestedOwnerUserId);
        Assert.False(detail.Proposals[0].IsLowConfidence);
        Assert.Null(detail.Proposals[1].SuggestedOwnerUserId);
        Assert.True(detail.Proposals[1].IsLowConfidence);
        Assert.All(detail.Proposals, proposal => Assert.Equal(ReviewState.Pending, proposal.ReviewState));
    }

    [Fact]
    public async Task A_failed_run_publishes_its_reason_its_metrics_and_no_proposals()
    {
        const string Reason = "The provider did not answer within Ai:CallTimeoutSeconds.";

        ExtractionRun run = Start(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            ExtractionOutcome.Failed, Reason, []);

        RunDetailDto detail = await Over(run).GetAsync(run.Id, TestContext.Current.CancellationToken);

        Assert.Equal(ExtractionOutcome.Failed, detail.Outcome);
        Assert.Equal(Reason, detail.FailureReason);
        Assert.Empty(detail.Proposals);

        // Still every metric: a failed run has to be as comparable as a succeeded one (AD-6).
        Assert.Equal("v1", detail.PromptVersion);
        Assert.Equal(87, detail.DurationMs);
    }

    [Fact]
    public async Task Warnings_are_published_as_an_array_in_the_order_they_were_recorded()
    {
        string[] warnings = ["Dropped: 'Marcus will resurface the lot.'", "Dropped: 'Nobody said this.'"];

        ExtractionRun run = Start(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            ExtractionOutcome.Succeeded, null, warnings);

        RunDetailDto detail = await Over(run).GetAsync(run.Id, TestContext.Current.CancellationToken);

        Assert.Equal(warnings, detail.Warnings);
    }

    [Fact]
    public async Task Reading_a_run_that_is_not_there_is_a_not_found() =>
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Over().GetAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken));

    // ---------------------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------------------

    private static ExtractionRun Start(
        Guid meetingId,
        Guid notesId,
        Guid actor,
        ExtractionOutcome outcome,
        string? failureReason,
        IReadOnlyList<string> warnings) =>
        ExtractionRun.Start(meetingId, notesId, NotesSha256, actor, Metrics, outcome, failureReason, warnings);

    private static RunsQueries Over(params ExtractionRun[] runs)
    {
        FakeReadDb readDb = new([.. runs, .. runs.SelectMany(run => run.Proposals), Dana]);

        return new RunsQueries(readDb, new ProposedActionReadModel(readDb, new FakeExtractionSettings(0.70)));
    }

    private sealed class FakeExtractionSettings(double threshold) : IExtractionSettings
    {
        public double LowConfidenceThreshold { get; } = threshold;
    }

    /// <summary>The read seam over a list; LINQ to Objects composes what the provider translates.</summary>
    private sealed class FakeReadDb(params object[] rows) : IReadDb
    {
        public IQueryable<T> Query<T>()
            where T : class => rows.OfType<T>().AsQueryable();

        public Task<IReadOnlyList<T>> ToListAsync<T>(
            IQueryable<T> query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>([.. query]);

        public Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default) =>
            Task.FromResult(query.Count());
    }
}
