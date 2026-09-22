using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Review;
using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Extraction;
using ActionLedger.Domain.Meetings;
using ActionLedger.Domain.Users;
using Xunit;

namespace ActionLedger.Application.Tests.Review;

/// <summary>
/// AD-9 and AD-15 — the one class that produces <see cref="ProposedActionDto"/>, and therefore the
/// one place <c>isLowConfidence</c> and <c>suggestedOwnerUserId</c> are decided. What is asserted
/// here is that both are decided <em>here</em>, from the port's threshold and the roster, and that
/// reads come back in AI order.
/// </summary>
public sealed class ProposedActionReadModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 14, 30, 0, TimeSpan.Zero);

    private const string NotesSha256 = "9f2c1d5e8a4b7c0d3e6f9a2b5c8d1e4f7a0b3c6d9e2f5a8b1c4d7e0f3a6b9c2d";

    private static readonly ExtractionRunMetadata Metrics = new(
        "Fake", "fixture-catalog", "v1", "1", Now, DurationMs: 12, InputTokens: 0, OutputTokens: 0);

    private static readonly User Dana = User.Register(
        "dana", "Dana Whitfield", new string('h', 60), Role.ActionOfficer, Now);

    private static readonly User Priya = User.Register(
        "priya", "Priya Raman", new string('h', 60), Role.Lead, Now);

    // ---------------------------------------------------------------------------------------
    // Order
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Proposals_come_back_in_ordinal_order_whatever_order_the_rows_arrive_in()
    {
        ExtractionRun run = RunWith(
            Draft("First.", "Dana Whitfield", 0.9),
            Draft("Second.", string.Empty, 0.9),
            Draft("Third.", string.Empty, 0.9));

        // The rows are handed to the read model back-to-front. IReadDb is untracked and a planner
        // may return rows in any order it likes, so "AI order" has to be asked for.
        IReadOnlyList<ProposedActionDto> proposals = await ReadAsync(
            run,
            rows: [.. run.Proposals.Reverse()]);

        Assert.Equal([0, 1, 2], proposals.Select(proposal => proposal.Ordinal));
        Assert.Equal(["First.", "Second.", "Third."], proposals.Select(proposal => proposal.Description));
    }

    [Fact]
    public async Task A_run_with_no_proposals_reads_as_an_empty_list()
    {
        ExtractionRun run = RunWith();

        Assert.Empty(await ReadAsync(run));
    }

    [Fact]
    public async Task Another_runs_proposals_are_not_this_runs()
    {
        ExtractionRun mine = RunWith(Draft("Mine.", string.Empty, 0.9));
        ExtractionRun theirs = RunWith(Draft("Theirs.", string.Empty, 0.9));

        ProposedActionReadModel model = Over(
            [.. mine.Proposals, .. theirs.Proposals],
            threshold: 0.70);

        IReadOnlyList<ProposedActionDto> proposals = await model.ForRunAsync(mine.Id, TestContext.Current.CancellationToken);

        Assert.Equal("Mine.", Assert.Single(proposals).Description);
    }

    // ---------------------------------------------------------------------------------------
    // The low-confidence flag
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0.69, true)]
    [InlineData(0.6999, true)]
    [InlineData(0.0, true)]
    [InlineData(0.70, false)]
    [InlineData(0.71, false)]
    [InlineData(1.0, false)]
    public async Task The_flag_is_a_strict_comparison_against_the_threshold(double confidence, bool expected)
    {
        ExtractionRun run = RunWith(Draft("Do the thing.", string.Empty, confidence));

        ProposedActionDto proposal = Assert.Single(await ReadAsync(run, threshold: 0.70));

        // AD-15 — strictly below. A proposal exactly at the threshold is not flagged: the
        // threshold is the first confidence that is *not* low.
        Assert.Equal(expected, proposal.IsLowConfidence);
    }

    [Fact]
    public async Task The_threshold_is_read_from_the_settings_port_rather_than_hard_coded()
    {
        ExtractionRun run = RunWith(
            Draft("Below the non-default threshold.", string.Empty, 0.80),
            Draft("Above the non-default threshold.", string.Empty, 0.96));

        // 0.95 is deliberately nothing like the 0.70 default: a hard-coded 0.70 would flag neither.
        IReadOnlyList<ProposedActionDto> proposals = await ReadAsync(run, threshold: 0.95);

        Assert.True(proposals[0].IsLowConfidence);
        Assert.False(proposals[1].IsLowConfidence);
    }

    [Fact]
    public async Task The_confidence_itself_is_published_unchanged_beside_the_flag()
    {
        ExtractionRun run = RunWith(Draft("Do the thing.", string.Empty, 0.6234));

        ProposedActionDto proposal = Assert.Single(await ReadAsync(run, threshold: 0.70));

        // The flag is a convenience, not a replacement: FR-14's UI shows the score too.
        Assert.Equal(0.6234, proposal.Confidence);
        Assert.True(proposal.IsLowConfidence);
    }

    // ---------------------------------------------------------------------------------------
    // The owner pre-selection
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_suggestion_that_matches_a_user_carries_that_users_id()
    {
        ExtractionRun run = RunWith(Draft("Order the scanners.", "dana whitfield", 0.9));

        ProposedActionDto proposal = Assert.Single(await ReadAsync(run, users: [Dana, Priya]));

        Assert.Equal(Dana.Id, proposal.SuggestedOwnerUserId);

        // FR-15 — and the free text survives verbatim, whatever the match said.
        Assert.Equal("dana whitfield", proposal.SuggestedOwner);
    }

    [Theory]
    [InlineData("Facilities")]
    [InlineData("")]
    public async Task A_suggestion_that_matches_nobody_carries_a_null_id_and_keeps_its_text(string suggestion)
    {
        ExtractionRun run = RunWith(Draft("Order the scanners.", suggestion, 0.9));

        ProposedActionDto proposal = Assert.Single(await ReadAsync(run, users: [Dana, Priya]));

        Assert.Null(proposal.SuggestedOwnerUserId);
        Assert.Equal(suggestion, proposal.SuggestedOwner);
    }

    [Fact]
    public async Task A_system_user_is_never_pre_selected()
    {
        User seed = User.RegisterSystem("seed", "Seed", new string('h', 60), Now);

        ExtractionRun run = RunWith(Draft("Order the scanners.", "Seed", 0.9));

        // The owner picker is built from the same roster, which excludes system identities. A
        // pre-selection it cannot show would be an id the web app has no row for.
        Assert.Null(Assert.Single(await ReadAsync(run, users: [Dana, seed])).SuggestedOwnerUserId);
    }

    [Fact]
    public async Task Every_proposal_is_resolved_against_the_same_roster()
    {
        ExtractionRun run = RunWith(
            Draft("Order the scanners.", "Dana Whitfield", 0.9),
            Draft("Book the range.", "PRIYA RAMAN", 0.9),
            Draft("Resurface the lot.", "Facilities", 0.9));

        IReadOnlyList<ProposedActionDto> proposals = await ReadAsync(run, users: [Dana, Priya]);

        Assert.Equal([Dana.Id, Priya.Id, null], proposals.Select(proposal => proposal.SuggestedOwnerUserId));
    }

    // ---------------------------------------------------------------------------------------
    // The rest of the shape
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Every_published_member_is_the_stored_value()
    {
        ExtractionRun run = RunWith(new ProposedActionDraft(
            "Order the replacement scanners.",
            "Dana Whitfield",
            new DateOnly(2026, 9, 25),
            0.91,
            "Dana will order the replacement scanners."));

        ProposedActionDto proposal = Assert.Single(await ReadAsync(run, users: [Dana]));
        ProposedAction stored = Assert.Single(run.Proposals);

        Assert.Equal(stored.Id, proposal.Id);
        Assert.Equal(0, proposal.Ordinal);
        Assert.Equal("Order the replacement scanners.", proposal.Description);
        Assert.Equal(new DateOnly(2026, 9, 25), proposal.SuggestedDueDate);
        Assert.Equal("Dana will order the replacement scanners.", proposal.SourceExcerpt);
        Assert.Equal(ReviewState.Pending, proposal.ReviewState);
    }

    [Fact]
    public async Task A_proposal_with_no_due_date_publishes_null_rather_than_a_default_date()
    {
        ExtractionRun run = RunWith(Draft("Do the thing.", string.Empty, 0.9));

        Assert.Null(Assert.Single(await ReadAsync(run)).SuggestedDueDate);
    }

    // ---------------------------------------------------------------------------------------
    // Story 3.4 — names, the decision copy, the Tracked Action's values, and the excerpt span
    // ---------------------------------------------------------------------------------------

    private const string Notes = "Office move.\nDana will order the scanners.\nPriya will book the range.";

    [Fact]
    public async Task A_pending_proposal_carries_its_owners_name_its_span_and_no_decision()
    {
        (Meeting meeting, ExtractionRun run) = NotedRun(
            new ProposedActionDraft("Order the scanners.", "dana whitfield", null, 0.9, "Dana will order the scanners."));

        ProposedActionDto proposal = Assert.Single(await ReadAllAsync(run, meeting, [], [Dana, Priya]));

        Assert.Equal("Dana Whitfield", proposal.SuggestedOwnerDisplayName);
        Assert.Equal(13, proposal.ExcerptStart);
        Assert.Equal("Dana will order the scanners", Notes.Substring(proposal.ExcerptStart!.Value, proposal.ExcerptLength!.Value));

        Assert.Null(proposal.DecidedByUserId);
        Assert.Null(proposal.DecidedByDisplayName);
        Assert.Null(proposal.DecidedAt);
        Assert.Null(proposal.RejectionReason);
        Assert.Null(proposal.TrackedActionId);
        Assert.Null(proposal.DecidedDescription);
        Assert.Null(proposal.DecidedOwnerUserId);
        Assert.Null(proposal.DecidedOwnerDisplayName);
        Assert.Null(proposal.DecidedDueDate);
    }

    [Fact]
    public async Task An_approved_proposal_carries_the_decision_and_the_tracked_actions_values()
    {
        (Meeting meeting, ExtractionRun run) = NotedRun(
            new ProposedActionDraft("Order the scanners.", "Dana Whitfield", new DateOnly(2026, 9, 25), 0.9, "Dana will order the scanners."));

        ProposedAction stored = Assert.Single(run.Proposals);
        DecisionResult result = stored.Decide(
            DecisionKind.Approved,
            new DecisionEdits("Order the scanners.", Dana.Id, new DateOnly(2026, 9, 25), Dana.Id, null),
            Priya.Id,
            Now);

        ProposedActionDto proposal = Assert.Single(await ReadAllAsync(run, meeting, [result.TrackedAction!], [Dana, Priya]));

        Assert.Equal(ReviewState.Approved, proposal.ReviewState);
        Assert.Equal(Priya.Id, proposal.DecidedByUserId);
        Assert.Equal("Priya Raman", proposal.DecidedByDisplayName);
        Assert.Equal(Now, proposal.DecidedAt);
        Assert.Null(proposal.RejectionReason);
        Assert.Equal(result.TrackedAction!.Id, proposal.TrackedActionId);
        Assert.Equal("Order the scanners.", proposal.DecidedDescription);
        Assert.Equal(Dana.Id, proposal.DecidedOwnerUserId);
        Assert.Equal("Dana Whitfield", proposal.DecidedOwnerDisplayName);
        Assert.Equal(new DateOnly(2026, 9, 25), proposal.DecidedDueDate);
        Assert.NotNull(proposal.ExcerptStart);
    }

    [Fact]
    public async Task An_edited_proposal_carries_the_edited_values_and_keeps_the_proposed_ones()
    {
        (Meeting meeting, ExtractionRun run) = NotedRun(
            new ProposedActionDraft("Book the range.", "Facilities", new DateOnly(2026, 10, 10), 0.55, "Priya will book the range."));

        DecisionResult result = Assert.Single(run.Proposals).Decide(
            DecisionKind.Edited,
            new DecisionEdits("Book the range for Friday.", Priya.Id, new DateOnly(2026, 10, 3), null, null),
            Dana.Id,
            Now);

        ProposedActionDto proposal = Assert.Single(await ReadAllAsync(run, meeting, [result.TrackedAction!], [Dana, Priya]));

        Assert.Equal(ReviewState.Edited, proposal.ReviewState);
        Assert.Equal("Book the range.", proposal.Description);
        Assert.Equal("Facilities", proposal.SuggestedOwner);
        Assert.Null(proposal.SuggestedOwnerDisplayName);
        Assert.Equal(new DateOnly(2026, 10, 10), proposal.SuggestedDueDate);

        Assert.Equal("Dana Whitfield", proposal.DecidedByDisplayName);
        Assert.Equal("Book the range for Friday.", proposal.DecidedDescription);
        Assert.Equal(Priya.Id, proposal.DecidedOwnerUserId);
        Assert.Equal("Priya Raman", proposal.DecidedOwnerDisplayName);
        Assert.Equal(new DateOnly(2026, 10, 3), proposal.DecidedDueDate);
        Assert.Equal("Priya will book the range", Notes.Substring(proposal.ExcerptStart!.Value, proposal.ExcerptLength!.Value));
    }

    [Fact]
    public async Task A_rejected_proposal_carries_the_decision_and_reason_and_no_tracked_values()
    {
        (Meeting meeting, ExtractionRun run) = NotedRun(
            new ProposedActionDraft("Book the range.", string.Empty, null, 0.9, "Priya will book the range."));

        Assert.Single(run.Proposals).Decide(
            DecisionKind.Rejected,
            new DecisionEdits(null, null, null, null, "  discussion item, not an action "),
            Dana.Id,
            Now);

        ProposedActionDto proposal = Assert.Single(await ReadAllAsync(run, meeting, [], [Dana, Priya]));

        Assert.Equal(ReviewState.Rejected, proposal.ReviewState);
        Assert.Equal(Dana.Id, proposal.DecidedByUserId);
        Assert.Equal("Dana Whitfield", proposal.DecidedByDisplayName);
        Assert.Equal(Now, proposal.DecidedAt);
        Assert.Equal("discussion item, not an action", proposal.RejectionReason);
        Assert.Null(proposal.TrackedActionId);
        Assert.Null(proposal.DecidedDescription);
        Assert.Null(proposal.DecidedOwnerUserId);
        Assert.Null(proposal.DecidedOwnerDisplayName);
        Assert.Null(proposal.DecidedDueDate);
    }

    [Fact]
    public async Task An_approval_to_unassigned_has_a_null_owner_and_no_owner_name()
    {
        (Meeting meeting, ExtractionRun run) = NotedRun(
            new ProposedActionDraft("Book the range.", "Facilities", null, 0.9, "Priya will book the range."));

        DecisionResult result = Assert.Single(run.Proposals).Decide(
            DecisionKind.Approved,
            new DecisionEdits("Book the range.", null, null, null, null),
            Dana.Id,
            Now);

        ProposedActionDto proposal = Assert.Single(await ReadAllAsync(run, meeting, [result.TrackedAction!], [Dana, Priya]));

        Assert.Equal(result.TrackedAction!.Id, proposal.TrackedActionId);
        Assert.Null(proposal.DecidedOwnerUserId);
        Assert.Null(proposal.DecidedOwnerDisplayName);
        Assert.Null(proposal.DecidedDueDate);
    }

    [Fact]
    public async Task A_system_user_decider_is_still_named()
    {
        User seed = User.RegisterSystem("seed", "Seed", new string('h', 60), Now);

        (Meeting meeting, ExtractionRun run) = NotedRun(
            new ProposedActionDraft("Book the range.", string.Empty, null, 0.9, "Priya will book the range."));

        Assert.Single(run.Proposals).Decide(
            DecisionKind.Rejected,
            new DecisionEdits(null, null, null, null, null),
            seed.Id,
            Now);

        ProposedActionDto proposal = Assert.Single(await ReadAllAsync(run, meeting, [], [Dana, seed]));

        // The roster excludes system users for pre-selection; attribution does not.
        Assert.Equal("Seed", proposal.DecidedByDisplayName);
    }

    [Fact]
    public async Task An_excerpt_not_in_the_notes_has_no_span()
    {
        (Meeting meeting, ExtractionRun run) = NotedRun(
            new ProposedActionDraft("Resurface the lot.", string.Empty, null, 0.9, "Marcus will resurface the lot."));

        ProposedActionDto proposal = Assert.Single(await ReadAllAsync(run, meeting, [], [Dana]));

        Assert.Null(proposal.ExcerptStart);
        Assert.Null(proposal.ExcerptLength);
    }

    [Fact]
    public async Task Another_meetings_notes_are_not_searched()
    {
        (Meeting meeting, ExtractionRun run) = NotedRun(
            new ProposedActionDraft("Order the scanners.", string.Empty, null, 0.9, "Dana will order the scanners."));

        // The run's notes id points at nothing on file; a meeting whose notes do contain the
        // sentence must not be used in their place.
        ExtractionRun orphan = ExtractionRun.Start(
            meeting.Id, Guid.CreateVersion7(), NotesSha256, Guid.CreateVersion7(), Metrics,
            ExtractionOutcome.Succeeded, failureReason: null, warnings: null);
        orphan.AddProposals([new ProposedActionDraft("Order the scanners.", string.Empty, null, 0.9, "Dana will order the scanners.")], Now);

        ProposedActionDto proposal = Assert.Single(await ReadAllAsync(orphan, meeting, [], [Dana], [run]));

        Assert.Null(proposal.ExcerptStart);
    }

    // ---------------------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------------------

    private static ProposedActionDraft Draft(string description, string suggestedOwner, double confidence) =>
        new(description, suggestedOwner, null, confidence, $"Somebody will handle: {description}");

    private static ExtractionRun RunWith(params ProposedActionDraft[] drafts)
    {
        ExtractionRun run = ExtractionRun.Start(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            NotesSha256,
            Guid.CreateVersion7(),
            Metrics,
            ExtractionOutcome.Succeeded,
            failureReason: null,
            warnings: null);

        run.AddProposals(drafts, Now);

        return run;
    }

    private static (Meeting Meeting, ExtractionRun Run) NotedRun(params ProposedActionDraft[] drafts)
    {
        Meeting meeting = Meeting.Create("Office move", new DateOnly(2026, 9, 22), null, Guid.CreateVersion7(), Now);
        MeetingNotes notes = meeting.AttachNotes(Notes, Now);

        ExtractionRun run = ExtractionRun.Start(
            meeting.Id,
            notes.Id,
            notes.Sha256,
            Guid.CreateVersion7(),
            Metrics,
            ExtractionOutcome.Succeeded,
            failureReason: null,
            warnings: null);

        run.AddProposals(drafts, Now);

        return (meeting, run);
    }

    private static Task<IReadOnlyList<ProposedActionDto>> ReadAllAsync(
        ExtractionRun run,
        Meeting meeting,
        IReadOnlyList<TrackedAction> tracked,
        IReadOnlyList<User> users,
        IReadOnlyList<ExtractionRun>? otherRuns = null) =>
        new ProposedActionReadModel(
            new FakeReadDb([
                .. run.Proposals,
                run,
                .. otherRuns ?? [],
                meeting,
                .. tracked,
                .. users]),
            new FakeExtractionSettings(0.70))
            .ForRunAsync(run.Id, TestContext.Current.CancellationToken);

    private static Task<IReadOnlyList<ProposedActionDto>> ReadAsync(
        ExtractionRun run,
        double threshold = 0.70,
        IReadOnlyList<User>? users = null,
        IReadOnlyList<ProposedAction>? rows = null) =>
        Over([.. rows ?? run.Proposals], threshold, users)
            .ForRunAsync(run.Id, TestContext.Current.CancellationToken);

    private static ProposedActionReadModel Over(
        IReadOnlyList<ProposedAction> proposals,
        double threshold,
        IReadOnlyList<User>? users = null) =>
        new(
            new FakeReadDb([.. proposals, .. users ?? []]),
            new FakeExtractionSettings(threshold));

    /// <summary>AD-16 — the one way this ring learns the threshold.</summary>
    private sealed class FakeExtractionSettings(double threshold) : IExtractionSettings
    {
        public double LowConfidenceThreshold { get; } = threshold;
    }

    /// <summary>
    /// The read seam over a list. LINQ to Objects composes the same <c>Where</c>/<c>OrderBy</c>/
    /// <c>Select</c> the provider translates, so what is asserted here is the query the class
    /// builds; that it also translates to SQL is the Infrastructure suite's job.
    /// </summary>
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
