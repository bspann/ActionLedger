using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Review;
using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Common;
using ActionLedger.Domain.Extraction;
using ActionLedger.Domain.Users;
using Xunit;

namespace ActionLedger.Application.Tests.Review;

/// <summary>
/// AD-3, AD-9, AD-12 and AD-20 at the use-case level: the server alone chooses Approved or Edited,
/// the owner is exactly what was sent, bad input is a 400 before the domain's 409 can answer it,
/// and the Tracked Action and revisions are staged before the one commit.
/// </summary>
/// <remarks>
/// AD-18 — the ports are in-memory fakes here; <c>DecisionEndpointTests</c> and
/// <c>TrackedActionPersistenceTests</c> prove the same rows against a real database.
/// </remarks>
public sealed class DecideProposalHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 15, 5, 12, TimeSpan.Zero);

    private static readonly DateOnly Proposed = new(2026, 9, 25);

    private static readonly Guid Actor = Guid.CreateVersion7();

    private static readonly ExtractionRunMetadata Metrics = new(
        "Fake", "fixture-catalog", "v1", "1", Now, DurationMs: 12, InputTokens: 0, OutputTokens: 0);

    private static readonly User Dana = User.Register("dana", "Dana Whitfield", new string('h', 60), Role.ActionOfficer, Now);

    private static readonly User Priya = User.Register("priya", "Priya Raman", new string('h', 60), Role.Lead, Now);

    private static readonly User Seed = User.RegisterSystem("seed", "Seed", new string('h', 60), Now);

    private const string Description = "Order the replacement scanners.";

    // ---------------------------------------------------------------------------------------
    // Approve — the server chooses the kind
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Approving_the_proposal_as_it_stands_is_approved_and_owned_by_the_match()
    {
        Harness harness = Harness.For("Dana Whitfield");

        ProposalDecisionDto answer = await harness.DecideAsync(Approve(Description, Dana.Id, Proposed));

        Assert.Equal(ReviewState.Approved, answer.ReviewState);

        TrackedAction tracked = Assert.Single(harness.Actions.Added);

        Assert.Equal(tracked.Id, answer.TrackedActionId);
        Assert.Equal(harness.Proposal.Id, answer.ProposedActionId);
        Assert.Equal(Dana.Id, tracked.OwnerUserId);
        Assert.Equal(Description, tracked.Description);
        Assert.Equal(Proposed, tracked.DueDate);

        ActionRevision decision = Assert.Single(harness.Revisions.Added);

        Assert.Equal(RevisionKind.ReviewDecision, decision.Kind);
        Assert.Equal(nameof(ReviewState.Approved), decision.NewValue);
    }

    [Fact]
    public async Task Keeping_an_unmatched_owner_unassigned_is_approved()
    {
        Harness harness = Harness.For("Facilities");

        ProposalDecisionDto answer = await harness.DecideAsync(Approve(Description, null, Proposed));

        Assert.Equal(ReviewState.Approved, answer.ReviewState);
        Assert.Null(Assert.Single(harness.Actions.Added).OwnerUserId);
    }

    [Fact]
    public async Task A_changed_due_date_is_an_edit_with_its_field_edit_after_the_decision()
    {
        Harness harness = Harness.For("Dana Whitfield");
        DateOnly moved = new(2026, 10, 2);

        ProposalDecisionDto answer = await harness.DecideAsync(Approve(Description, Dana.Id, moved));

        Assert.Equal(ReviewState.Edited, answer.ReviewState);
        Assert.Equal(moved, Assert.Single(harness.Actions.Added).DueDate);

        Assert.Equal(
            [(RevisionKind.ReviewDecision, (string?)"ReviewState"), (RevisionKind.FieldEdit, "DueDate")],
            harness.Revisions.Added.Select(revision => (revision.Kind, revision.Field)));
    }

    [Theory]
    [InlineData(" Order the replacement scanners.")]
    [InlineData("order the replacement scanners.")]
    [InlineData("Book movers")]
    public async Task Any_difference_in_the_description_is_an_edit(string description)
    {
        Harness harness = Harness.For("Dana Whitfield");

        ProposalDecisionDto answer = await harness.DecideAsync(Approve(description, Dana.Id, Proposed));

        // Ordinal and untrimmed, as Decide compares: a changed case or a leading space is a change.
        Assert.Equal(ReviewState.Edited, answer.ReviewState);
        Assert.Equal(description, Assert.Single(harness.Actions.Added).Description);
    }

    [Fact]
    public async Task Clearing_a_matched_owner_is_an_edit_and_leaves_the_action_unassigned()
    {
        Harness harness = Harness.For("Dana Whitfield");

        ProposalDecisionDto answer = await harness.DecideAsync(Approve(Description, null, Proposed));

        Assert.Equal(ReviewState.Edited, answer.ReviewState);

        // AD-9 — the match is a baseline for the diff, never an assignment.
        Assert.Null(Assert.Single(harness.Actions.Added).OwnerUserId);
        Assert.Contains(harness.Revisions.Added, revision => revision.Field == nameof(TrackedAction.OwnerUserId));
    }

    [Fact]
    public async Task Choosing_an_owner_where_nobody_matched_is_an_edit_owned_by_the_choice()
    {
        Harness harness = Harness.For("Facilities");

        ProposalDecisionDto answer = await harness.DecideAsync(Approve(Description, Priya.Id, Proposed));

        Assert.Equal(ReviewState.Edited, answer.ReviewState);
        Assert.Equal(Priya.Id, Assert.Single(harness.Actions.Added).OwnerUserId);
    }

    [Fact]
    public async Task Choosing_a_different_owner_than_the_match_is_owned_by_the_choice()
    {
        Harness harness = Harness.For("Dana Whitfield");

        ProposalDecisionDto answer = await harness.DecideAsync(Approve(Description, Priya.Id, Proposed));

        Assert.Equal(ReviewState.Edited, answer.ReviewState);
        Assert.Equal(Priya.Id, Assert.Single(harness.Actions.Added).OwnerUserId);
    }

    // ---------------------------------------------------------------------------------------
    // Reject
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_rejection_creates_nothing_and_records_the_trimmed_reason()
    {
        Harness harness = Harness.For("Dana Whitfield");

        ProposalDecisionDto answer = await harness.DecideAsync(new DecideProposalCommand
        {
            Decision = ReviewVerb.Reject,
            Reason = "  dup ",
            // Ignored on a rejection, exactly as Decide ignores them — even an owner nobody has.
            Description = "   ",
            OwnerUserId = Guid.CreateVersion7(),
            DueDate = new DateOnly(2030, 1, 1),
        });

        Assert.Equal(ReviewState.Rejected, answer.ReviewState);
        Assert.Null(answer.TrackedActionId);
        Assert.Empty(harness.Actions.Added);

        Assert.Equal("dup", harness.Proposal.RejectionReason);
        Assert.Equal("Rejected: dup", Assert.Single(harness.Revisions.Added).NewValue);
    }

    // ---------------------------------------------------------------------------------------
    // One commit, and who and when
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Both_adds_are_staged_before_the_one_commit()
    {
        Harness harness = Harness.For("Dana Whitfield");

        await harness.DecideAsync(Approve("Book movers", null, null));

        // AD-20 — staging after the commit would write nothing at all.
        Assert.Equal(["clock read", "action added", "revisions added", "commit"], harness.Journal.Entries);
        Assert.Equal(1, harness.Commits.Count);
    }

    [Fact]
    public async Task A_rejection_stages_only_its_revision_before_the_one_commit()
    {
        Harness harness = Harness.For("Dana Whitfield");

        await harness.DecideAsync(new DecideProposalCommand { Decision = ReviewVerb.Reject });

        Assert.Equal(["clock read", "revisions added", "commit"], harness.Journal.Entries);
    }

    [Fact]
    public async Task The_decider_and_the_instant_come_from_the_ports_read_once()
    {
        Harness harness = Harness.For("Dana Whitfield");

        ProposalDecisionDto answer = await harness.DecideAsync(Approve(Description, Dana.Id, Proposed));

        Assert.Equal(Actor, answer.DecidedByUserId);
        Assert.Equal(Now, answer.DecidedAt);
        Assert.Equal(Actor, harness.Proposal.DecidedByUserId);
        Assert.Equal(Now, harness.Proposal.DecidedAt);
        Assert.All(harness.Revisions.Added, revision => Assert.Equal((Actor, Now), (revision.ActorUserId, revision.OccurredAt)));
        Assert.Equal(Now, Assert.Single(harness.Actions.Added).CreatedAt);

        Assert.Equal(1, harness.Clock.Reads);
    }

    [Fact]
    public async Task A_lost_race_at_commit_surfaces_as_a_conflict()
    {
        Harness harness = Harness.For("Dana Whitfield");
        harness.Commits.Throws = new ConcurrencyConflictException("Someone else decided it first.");

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            harness.DecideAsync(Approve(Description, Dana.Id, Proposed)));
    }

    // ---------------------------------------------------------------------------------------
    // Refusals — each writes nothing
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unknown_proposal_is_not_found_and_writes_nothing()
    {
        Harness harness = Harness.For("Dana Whitfield");

        await Assert.ThrowsAsync<NotFoundException>(() =>
            harness.Handler.HandleAsync(Guid.CreateVersion7(), Approve(Description, Dana.Id, Proposed), TestContext.Current.CancellationToken));

        AssertNothingWritten(harness);
    }

    public static TheoryData<string, DecideProposalCommand> BadInput() => new()
    {
        { "blank description", Approve("   ", null, Proposed) },
        { "missing description", Approve(null, null, Proposed) },
        { "501-character description", Approve(new string('d', ProposedAction.DescriptionMaxLength + 1), null, Proposed) },
        { "reason on an approval", Approve(Description, Dana.Id, Proposed) with { Reason = "Because." } },
        { "owner nobody has", Approve(Description, Guid.CreateVersion7(), Proposed) },
        { "system owner", Approve(Description, Seed.Id, Proposed) },
        { "501-character reason", new DecideProposalCommand { Decision = ReviewVerb.Reject, Reason = new string('r', ProposedAction.RejectionReasonMaxLength + 1) } },
        { "undefined verb", new DecideProposalCommand { Decision = (ReviewVerb)7 } },
    };

    [Theory]
    [MemberData(nameof(BadInput))]
    public async Task Bad_input_is_a_validation_failure_that_writes_nothing(string because, DecideProposalCommand command)
    {
        Harness harness = Harness.For("Dana Whitfield");

        await Assert.ThrowsAsync<ValidationFailedException>(() => harness.DecideAsync(command));

        AssertNothingWritten(harness);
        Assert.True(harness.Proposal.ReviewState == ReviewState.Pending, because);
    }

    [Fact]
    public async Task A_padded_reason_within_the_limit_once_trimmed_is_accepted()
    {
        Harness harness = Harness.For("Dana Whitfield");
        string padded = $"  {new string('r', ProposedAction.RejectionReasonMaxLength)}  ";

        ProposalDecisionDto answer = await harness.DecideAsync(new DecideProposalCommand { Decision = ReviewVerb.Reject, Reason = padded });

        Assert.Equal(ReviewState.Rejected, answer.ReviewState);
    }

    [Fact]
    public async Task Deciding_a_decided_proposal_is_the_domains_conflict_and_writes_nothing_more()
    {
        Harness harness = Harness.For("Dana Whitfield");
        await harness.DecideAsync(new DecideProposalCommand { Decision = ReviewVerb.Reject });

        Harness again = harness.Fresh();

        await Assert.ThrowsAsync<DomainRuleException>(() => again.DecideAsync(Approve(Description, Dana.Id, Proposed)));

        // The clock was read — Decide is what refuses, and it takes the instant — but nothing was
        // staged and nothing committed.
        Assert.Empty(again.Actions.Added);
        Assert.Empty(again.Revisions.Added);
        Assert.Equal(0, again.Commits.Count);
    }

    [Fact]
    public async Task Bad_input_on_a_decided_proposal_is_still_a_validation_failure()
    {
        Harness harness = Harness.For("Dana Whitfield");
        await harness.DecideAsync(new DecideProposalCommand { Decision = ReviewVerb.Reject });

        Harness again = harness.Fresh();

        // The spec's order of outcomes: 404, then 400, then 409.
        await Assert.ThrowsAsync<ValidationFailedException>(() => again.DecideAsync(Approve("  ", null, null)));

        AssertNothingWritten(again);
    }

    // ---------------------------------------------------------------------------------------
    // Helpers and fakes
    // ---------------------------------------------------------------------------------------

    private static DecideProposalCommand Approve(string? description, Guid? ownerUserId, DateOnly? dueDate) => new()
    {
        Decision = ReviewVerb.Approve,
        Description = description,
        OwnerUserId = ownerUserId,
        DueDate = dueDate,
    };

    private static void AssertNothingWritten(Harness harness)
    {
        Assert.Empty(harness.Actions.Added);
        Assert.Empty(harness.Revisions.Added);
        Assert.Equal(0, harness.Commits.Count);
        Assert.Equal(0, harness.Clock.Reads);
    }

    /// <summary>The handler with every port faked, over one run holding one Pending proposal.</summary>
    private sealed class Harness
    {
        private readonly ExtractionRun _run;

        private Harness(ExtractionRun run)
        {
            _run = run;

            Clock = new CountingClock(Now, Journal);
            Commits = new CountingUnitOfWork(Journal);
            Actions = new RecordingActionRepository(Journal);
            Revisions = new RecordingRevisionRepository(Journal);

            Handler = new DecideProposalHandler(
                new FakeRunRepository(run),
                Actions,
                Revisions,
                new FakeReadDb(Dana, Priya, Seed),
                new FakeCurrentUser(Actor),
                Clock,
                Commits);
        }

        public ProposedAction Proposal => _run.Proposals[0];

        public DecideProposalHandler Handler { get; }

        public Journal Journal { get; } = new();

        public CountingClock Clock { get; }

        public CountingUnitOfWork Commits { get; }

        public RecordingActionRepository Actions { get; }

        public RecordingRevisionRepository Revisions { get; }

        public static Harness For(string suggestedOwner)
        {
            ExtractionRun run = ExtractionRun.Start(
                Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', 64), Actor,
                Metrics, ExtractionOutcome.Succeeded, null, warnings: null);

            run.AddProposals(
                [new ProposedActionDraft(Description, suggestedOwner, Proposed, 0.91, "Dana will order the scanners.")],
                Now);

            return new Harness(run);
        }

        /// <summary>A second request against the same, already-decided run, with clean recorders.</summary>
        public Harness Fresh() => new(_run);

        public Task<ProposalDecisionDto> DecideAsync(DecideProposalCommand command) =>
            Handler.HandleAsync(Proposal.Id, command, TestContext.Current.CancellationToken);
    }

    /// <summary>What happened, in the order it happened.</summary>
    private sealed class Journal
    {
        private readonly List<string> _entries = [];

        public IReadOnlyList<string> Entries => _entries;

        public void Record(string what) => _entries.Add(what);
    }

    private sealed class FakeRunRepository(ExtractionRun run) : IExtractionRunRepository
    {
        public void Add(ExtractionRun added) => throw new InvalidOperationException("A decision never adds a run.");

        public Task<ExtractionRun?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ExtractionRun?>(run.Id == id ? run : null);

        public Task<ExtractionRun?> FindByProposedActionIdAsync(Guid proposedActionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ExtractionRun?>(run.Proposals.Any(proposal => proposal.Id == proposedActionId) ? run : null);
    }

    private sealed class RecordingActionRepository(Journal journal) : IActionRepository
    {
        private readonly List<TrackedAction> _added = [];

        public IReadOnlyList<TrackedAction> Added => _added;

        public void Add(TrackedAction trackedAction)
        {
            _added.Add(trackedAction);
            journal.Record("action added");
        }
    }

    /// <summary>AD-7 — append and ordered read only, in memory.</summary>
    private sealed class RecordingRevisionRepository(Journal journal) : IActionRevisionRepository
    {
        private readonly List<ActionRevision> _added = [];

        public IReadOnlyList<ActionRevision> Added => _added;

        public void AddRange(IReadOnlyList<ActionRevision> revisions)
        {
            _added.AddRange(revisions);
            journal.Record("revisions added");
        }

        public Task<IReadOnlyList<ActionRevision>> ListForTargetAsync(
            RevisionTargetType targetType,
            Guid targetId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActionRevision>>(
                [.. _added.Where(revision => revision.TargetType == targetType && revision.TargetId == targetId)]);
    }

    /// <summary>AD-12 — the actor a token would have carried.</summary>
    private sealed class FakeCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId { get; } = userId;

        public string DisplayName => "Olu Officer";

        public Role Role => Role.ActionOfficer;
    }

    /// <summary>AD-15 — the only time source, fixed and counted.</summary>
    private sealed class CountingClock(DateTimeOffset now, Journal journal) : IClock
    {
        public int Reads { get; private set; }

        public DateTimeOffset UtcNow
        {
            get
            {
                Reads++;
                journal.Record("clock read");

                return now;
            }
        }
    }

    /// <summary>AD-20 — one commit per use case, counted and journalled.</summary>
    private sealed class CountingUnitOfWork(Journal journal) : IUnitOfWork
    {
        public int Count { get; private set; }

        /// <summary>Thrown instead of committing, when set — a lost race, or a unique index.</summary>
        public Exception? Throws { get; set; }

        public Task<int> CommitAsync(CancellationToken cancellationToken = default)
        {
            Count++;
            journal.Record("commit");

            return Throws is null ? Task.FromResult(0) : Task.FromException<int>(Throws);
        }
    }

    /// <summary>The read seam over a list, serving the roster.</summary>
    private sealed class FakeReadDb(params object[] rows) : IReadDb
    {
        public IQueryable<T> Query<T>()
            where T : class => rows.OfType<T>().AsQueryable();

        public Task<IReadOnlyList<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>([.. query]);

        public Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default) =>
            Task.FromResult(query.Count());
    }
}
