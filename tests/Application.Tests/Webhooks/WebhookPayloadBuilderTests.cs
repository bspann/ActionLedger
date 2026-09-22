using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Webhooks;
using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Extraction;
using ActionLedger.Domain.Meetings;
using ActionLedger.Domain.Users;
using Xunit;

namespace ActionLedger.Application.Tests.Webhooks;

/// <summary>
/// AD-8 — every field of the <c>action.approved</c> payload, from the tracked entities and one read.
/// The read seam here is a list, so what is asserted is where each value comes from; that the one
/// query also translates to SQL is <c>OutboxPersistenceTests</c>' job.
/// </summary>
public sealed class WebhookPayloadBuilderTests
{
    private static readonly DateOnly Held = new(2026, 9, 22);

    private static readonly DateOnly Proposed = new(2026, 9, 25);

    private static readonly DateOnly Moved = new(2026, 10, 3);

    private static readonly DateTimeOffset Created = new(2026, 9, 22, 14, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset DecidedAt = new(2026, 9, 22, 15, 5, 12, TimeSpan.Zero);

    private static readonly User Decider = User.Register("olu", "Olu Officer", new string('h', 60), Role.ActionOfficer, Created);

    private static readonly User Owner = User.Register("dana", "Dana Whitfield", new string('h', 60), Role.ActionOfficer, Created);

    [Fact]
    public async Task An_approval_carries_every_field_the_integrator_contract_names()
    {
        (Meeting meeting, ExtractionRun run, ProposedAction proposal) = Pending();

        TrackedAction tracked = proposal.Decide(
            DecisionKind.Approved,
            new DecisionEdits(proposal.Description, Owner.Id, Proposed, ProposedOwnerUserId: Owner.Id, Reason: null),
            Decider.Id,
            DecidedAt).TrackedAction!;

        FakeReadDb reads = new(meeting, run, Decider, Owner);

        WebhookEventDto payload = await new WebhookPayloadBuilder().BuildAsync(
            Source(tracked, proposal), reads, TestContext.Current.CancellationToken);

        Assert.Equal(7, payload.EventId.Version);
        Assert.Equal("action.approved", payload.EventType);
        Assert.Equal(DecidedAt, payload.OccurredAt);

        Assert.Equal(
            new WebhookTrackedAction(
                tracked.Id, "Order the replacement scanners.", Owner.Id, "Dana Whitfield", Proposed,
                ActionStatus.Open, meeting.Id, "Weekly sync"),
            payload.TrackedAction);

        Assert.Equal(
            new WebhookProposedAction(
                proposal.Id, "Order the replacement scanners.", "Dana Whitfield", Proposed, 0.91, "Dana will order the scanners."),
            payload.ProposedAction);

        Assert.Equal(new WebhookReviewDecision(DecisionKind.Approved, Decider.Id, "Olu Officer", DecidedAt), payload.ReviewDecision);

        // "At most one query" — the committed facts are one read, not one per name.
        Assert.Equal(1, reads.Materializations);
    }

    [Fact]
    public async Task An_edit_carries_the_edited_values_on_the_action_and_the_ai_values_on_the_proposal()
    {
        (Meeting meeting, ExtractionRun run, ProposedAction proposal) = Pending();

        TrackedAction tracked = proposal.Decide(
            DecisionKind.Edited,
            new DecisionEdits("Book the movers.", Decider.Id, Moved, ProposedOwnerUserId: Owner.Id, Reason: null),
            Decider.Id,
            DecidedAt).TrackedAction!;

        WebhookEventDto payload = await new WebhookPayloadBuilder().BuildAsync(
            Source(tracked, proposal), new FakeReadDb(meeting, run, Decider, Owner), TestContext.Current.CancellationToken);

        Assert.Equal(DecisionKind.Edited, payload.ReviewDecision.Kind);

        Assert.Equal("Book the movers.", payload.TrackedAction.Description);
        Assert.Equal(Decider.Id, payload.TrackedAction.OwnerId);
        Assert.Equal("Olu Officer", payload.TrackedAction.OwnerName);
        Assert.Equal(Moved, payload.TrackedAction.DueDate);

        Assert.Equal("Order the replacement scanners.", payload.ProposedAction.Description);
        Assert.Equal("Dana Whitfield", payload.ProposedAction.SuggestedOwner);
        Assert.Equal(Proposed, payload.ProposedAction.SuggestedDueDate);
    }

    [Fact]
    public async Task An_unassigned_owner_has_no_id_and_no_name()
    {
        (Meeting meeting, ExtractionRun run, ProposedAction proposal) = Pending();

        TrackedAction tracked = proposal.Decide(
            DecisionKind.Approved,
            new DecisionEdits(proposal.Description, null, Proposed, ProposedOwnerUserId: null, Reason: null),
            Decider.Id,
            DecidedAt).TrackedAction!;

        WebhookEventDto payload = await new WebhookPayloadBuilder().BuildAsync(
            Source(tracked, proposal), new FakeReadDb(meeting, run, Decider, Owner), TestContext.Current.CancellationToken);

        Assert.Null(payload.TrackedAction.OwnerId);
        Assert.Null(payload.TrackedAction.OwnerName);
        Assert.Equal("Olu Officer", payload.ReviewDecision.ByUserName);
    }

    [Fact]
    public async Task Each_build_mints_its_own_version_7_event_id()
    {
        (Meeting meeting, ExtractionRun run, ProposedAction proposal) = Pending();

        TrackedAction tracked = proposal.Decide(
            DecisionKind.Approved,
            new DecisionEdits(proposal.Description, null, Proposed, null, null),
            Decider.Id,
            DecidedAt).TrackedAction!;

        WebhookPayloadBuilder builder = new();
        FakeReadDb reads = new(meeting, run, Decider);

        WebhookEventDto first = await builder.BuildAsync(Source(tracked, proposal), reads, TestContext.Current.CancellationToken);
        WebhookEventDto second = await builder.BuildAsync(Source(tracked, proposal), reads, TestContext.Current.CancellationToken);

        Assert.Equal(7, first.EventId.Version);
        Assert.Equal(7, second.EventId.Version);
        Assert.NotEqual(first.EventId, second.EventId);
    }

    [Fact]
    public async Task A_run_that_is_not_committed_is_an_invalid_operation()
    {
        (_, _, ProposedAction proposal) = Pending();

        TrackedAction tracked = proposal.Decide(
            DecisionKind.Approved,
            new DecisionEdits(proposal.Description, null, Proposed, null, null),
            Decider.Id,
            DecidedAt).TrackedAction!;

        await Assert.ThrowsAsync<InvalidOperationException>(() => new WebhookPayloadBuilder().BuildAsync(
            Source(tracked, proposal), new FakeReadDb(Decider), TestContext.Current.CancellationToken));
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static WebhookEventSource Source(TrackedAction tracked, ProposedAction proposal) =>
        new(tracked.DomainEvents.Single(), tracked, proposal);

    private static (Meeting Meeting, ExtractionRun Run, ProposedAction Proposal) Pending()
    {
        Meeting meeting = Meeting.Create("Weekly sync", Held, ["Dana Whitfield"], Guid.CreateVersion7(), Created);
        meeting.AttachNotes("Dana will order the scanners.", Created);

        ExtractionRun run = ExtractionRun.Start(
            meeting.Id, meeting.Notes!.Id, meeting.Notes.Sha256, Guid.CreateVersion7(),
            new ExtractionRunMetadata("Fake", "fixture-catalog", "v1", "1", Created, 87, 0, 0),
            ExtractionOutcome.Succeeded, null, warnings: null);

        run.AddProposals(
            [new ProposedActionDraft("Order the replacement scanners.", "Dana Whitfield", Proposed, 0.91, "Dana will order the scanners.")],
            Created);

        return (meeting, run, run.Proposals[0]);
    }

    /// <summary>The read seam over a list, counting how many queries were materialized.</summary>
    private sealed class FakeReadDb(params object[] rows) : IReadDb
    {
        public int Materializations { get; private set; }

        public IQueryable<T> Query<T>()
            where T : class => rows.OfType<T>().AsQueryable();

        public Task<IReadOnlyList<T>> ToListAsync<T>(
            IQueryable<T> query,
            CancellationToken cancellationToken = default)
        {
            Materializations++;

            return Task.FromResult<IReadOnlyList<T>>([.. query]);
        }

        public Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default)
        {
            Materializations++;

            return Task.FromResult(query.Count());
        }
    }
}
