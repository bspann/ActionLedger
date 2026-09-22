using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Meetings;
using ActionLedger.Application.Review;
using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Extraction;
using ActionLedger.Domain.Meetings;
using ActionLedger.Domain.Users;
using ActionLedger.Infrastructure.Persistence;
using ActionLedger.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ActionLedger.Infrastructure.Tests;

/// <summary>
/// AD-4, AD-7 and AD-20 for Stories 3.1 and 3.2 against a real PostgreSQL 18: the migration
/// creates <c>tracked_actions</c> and the proposal's decision copy as specified, a decision's
/// proposal, Tracked Action and revisions round-trip through <c>AppDbContext</c> — directly and
/// through <see cref="DecideProposalHandler"/> — and the Meeting List counts what was tracked.
/// </summary>
[Collection(PostgresFixture.CollectionName)]
public sealed class TrackedActionPersistenceTests(PostgresFixture postgres)
{
    private static readonly DateOnly Held = new(2026, 9, 22);

    private static readonly DateTimeOffset Created = new(2026, 9, 22, 14, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset DecidedAt = new(2026, 9, 22, 15, 5, 12, TimeSpan.Zero);

    private static readonly DateOnly Proposed = new(2026, 9, 25);

    private static readonly ExtractionRunMetadata Metrics = new(
        "Fake", "fixture-catalog", "v1", "1", Created, DurationMs: 87, InputTokens: 0, OutputTokens: 0);

    // ---------------------------------------------------------------------------------------
    // The schema
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task The_migration_creates_tracked_actions_with_snake_case_columns()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await MigratedDatabaseAsync(nameof(The_migration_creates_tracked_actions_with_snake_case_columns), cancellationToken);

        Dictionary<string, (string Type, string Nullable)> columns = await ColumnsAsync(connectionString, "tracked_actions", cancellationToken);

        // The exact set, so a column added without a decision behind it reddens here.
        Assert.Equal(
            ["created_at", "description", "due_date", "id", "owner_user_id", "proposed_action_id", "status"],
            columns.Keys.Order(StringComparer.Ordinal));

        Assert.Equal(("uuid", "NO"), columns["id"]);
        Assert.Equal(("uuid", "NO"), columns["proposed_action_id"]);
        Assert.Equal(("character varying", "NO"), columns["description"]);

        // Null is Unassigned, and no due date is a fact rather than a gap.
        Assert.Equal(("uuid", "YES"), columns["owner_user_id"]);
        Assert.Equal(("date", "YES"), columns["due_date"]);

        // Enums are strings (AD-10) and instants are timestamptz.
        Assert.Equal(("character varying", "NO"), columns["status"]);
        Assert.Equal(("timestamp with time zone", "NO"), columns["created_at"]);
    }

    [Fact]
    public async Task The_migration_adds_the_decision_copy_to_proposed_actions_as_nullable_columns()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await MigratedDatabaseAsync(nameof(The_migration_adds_the_decision_copy_to_proposed_actions_as_nullable_columns), cancellationToken);

        Dictionary<string, (string Type, string Nullable)> columns = await ColumnsAsync(connectionString, "proposed_actions", cancellationToken);

        Assert.Equal(("uuid", "YES"), columns["decided_by_user_id"]);
        Assert.Equal(("timestamp with time zone", "YES"), columns["decided_at"]);
        Assert.Equal(("character varying", "YES"), columns["rejection_reason"]);

        // The decider is a stamp, like `started_by_user_id`, not a relationship.
        Assert.Empty(await ForeignKeysAsync(connectionString, "proposed_actions", "decided_by_user_id", cancellationToken));
    }

    [Fact]
    public async Task The_tracked_action_concurrency_token_is_the_system_column()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await postgres.CreateDatabaseAsync(nameof(The_tracked_action_concurrency_token_is_the_system_column), cancellationToken);

        await using ServiceProvider services = TestHost.Build(connectionString, TestHost.SeedingOff);
        await TestHost.MigrateAsync(services, cancellationToken);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        IProperty? token = scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Model
            .FindEntityType(typeof(TrackedAction))!
            .FindProperty(TrackedActionConfiguration.ConcurrencyTokenProperty);

        Assert.NotNull(token);
        Assert.True(token.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, token.ValueGenerated);

        Dictionary<string, (string Type, string Nullable)> columns = await ColumnsAsync(connectionString, "tracked_actions", cancellationToken);

        // information_schema never lists system columns, so this cannot fail on its own. PostgreSQL
        // already has a system column by that name, so a real one would have failed the
        // migration's CREATE TABLE outright — that is what proves the token is bound to it.
        Assert.DoesNotContain(TrackedActionConfiguration.ConcurrencyTokenProperty, columns.Keys);
    }

    [Fact]
    public async Task The_proposed_action_index_is_unique_and_the_list_indexes_are_not()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await MigratedDatabaseAsync(nameof(The_proposed_action_index_is_unique_and_the_list_indexes_are_not), cancellationToken);

        string? unique = await IndexDefinitionAsync(connectionString, TrackedActionConfiguration.ProposedActionIndexName, cancellationToken);

        // AD-20 — one Tracked Action per proposal, whatever raced past the proposal's token.
        Assert.NotNull(unique);
        Assert.Contains("UNIQUE", unique, StringComparison.Ordinal);
        Assert.Contains("(proposed_action_id)", unique, StringComparison.Ordinal);

        string? dueAndStatus = await IndexDefinitionAsync(connectionString, TrackedActionConfiguration.DueDateAndStatusIndexName, cancellationToken);

        Assert.NotNull(dueAndStatus);
        Assert.DoesNotContain("UNIQUE", dueAndStatus, StringComparison.Ordinal);
        Assert.Contains("(due_date, status)", dueAndStatus, StringComparison.Ordinal);

        string? owner = await IndexDefinitionAsync(connectionString, TrackedActionConfiguration.OwnerIndexName, cancellationToken);

        Assert.NotNull(owner);
        Assert.DoesNotContain("UNIQUE", owner, StringComparison.Ordinal);
        Assert.Contains("(owner_user_id)", owner, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("proposed_action_id", "proposed_actions")]
    [InlineData("owner_user_id", "users")]
    public async Task Both_foreign_keys_restrict_deletes(string column, string principal)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await MigratedDatabaseAsync($"tracked_fk_{column}", cancellationToken);

        (string Principal, string OnDelete) foreignKey =
            Assert.Single(await ForeignKeysAsync(connectionString, "tracked_actions", column, cancellationToken));

        Assert.Equal(principal, foreignKey.Principal);

        // 'r' is RESTRICT in pg_constraint.confdeltype: an audited record never loses its proposal
        // or its owner from under it.
        Assert.Equal("r", foreignKey.OnDelete);
    }

    [Fact]
    public async Task A_second_tracked_action_for_one_proposal_is_refused_by_the_database()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(nameof(A_second_tracked_action_for_one_proposal_is_refused_by_the_database), cancellationToken);

        ProposedAction proposal = await SavedPendingProposalAsync(services, cancellationToken);
        User actor = await SavedUserAsync(services, "officer", "Olu Officer", cancellationToken);

        await DecideAndSaveAsync(
            services, proposal.Id, DecisionKind.Approved,
            new DecisionEdits("Order the replacement scanners.", null, Proposed, null, null),
            actor.Id, cancellationToken);

        // The aggregate cannot decide twice, so a writer that reached the table another way is the
        // only way to reach the index.
        await using NpgsqlConnection connection = new(connectionString: await ConnectionStringAsync(services));
        await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand insert = new(
            """
            INSERT INTO tracked_actions (id, proposed_action_id, description, status, created_at)
            VALUES (@id, @proposal, 'A second one.', 'Open', now())
            """,
            connection);

        insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
        insert.Parameters.AddWithValue("proposal", proposal.Id);

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync(cancellationToken));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refused.SqlState);
    }

    [Theory]
    [InlineData(DecisionKind.Rejected)]
    [InlineData(DecisionKind.Approved)]
    public async Task A_second_decision_on_a_proposal_loaded_before_the_first_committed_is_a_conflict(DecisionKind second)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync($"second_decision_{second}", cancellationToken);

        ProposedAction proposal = await SavedPendingProposalAsync(services, cancellationToken);
        User actor = await SavedUserAsync(services, "officer", "Olu Officer", cancellationToken);

        // Both scopes load the Pending proposal before either commits: two officers on one screen.
        await using AsyncServiceScope first = services.CreateAsyncScope();
        await using AsyncServiceScope late = services.CreateAsyncScope();

        ProposedAction firstCopy = await LoadProposalAsync(first, proposal.Id, cancellationToken);
        ProposedAction lateCopy = await LoadProposalAsync(late, proposal.Id, cancellationToken);

        await CommitDecisionAsync(
            first, firstCopy.Decide(DecisionKind.Rejected, new DecisionEdits(null, null, null, null, null), actor.Id, DecidedAt),
            cancellationToken);

        DecisionEdits edits = second == DecisionKind.Approved
            ? new DecisionEdits("Order the replacement scanners.", null, Proposed, null, null)
            : new DecisionEdits(null, null, null, null, "Duplicate.");

        DecisionResult lateResult = lateCopy.Decide(second, edits, actor.Id, DecidedAt.AddSeconds(1));

        // The proposal's xmin is what refuses this — Reject-vs-Reject never reaches the
        // tracked_actions unique index.
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => CommitDecisionAsync(late, lateResult, cancellationToken));

        await using AsyncServiceScope check = services.CreateAsyncScope();
        AppDbContext context = check.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(1, await context.ActionRevisions.CountAsync(revision => revision.Kind == RevisionKind.ReviewDecision, cancellationToken));
        // The winner was a rejection, so a losing approval's Tracked Action must not have landed either.
        Assert.Equal(0, await context.TrackedActions.CountAsync(cancellationToken));
        Assert.Equal(
            ReviewState.Rejected,
            (await context.Set<ProposedAction>().AsNoTracking().SingleAsync(cancellationToken)).ReviewState);
    }

    // ---------------------------------------------------------------------------------------
    // The round trip
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task An_edited_decision_round_trips_its_tracked_action_revisions_and_decision_copy()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(nameof(An_edited_decision_round_trips_its_tracked_action_revisions_and_decision_copy), cancellationToken);

        ProposedAction proposal = await SavedPendingProposalAsync(services, cancellationToken);
        User actor = await SavedUserAsync(services, "officer", "Olu Officer", cancellationToken);
        User priya = await SavedUserAsync(services, "priya", "Priya Raman", cancellationToken);

        DecisionResult result = await DecideAndSaveAsync(
            services, proposal.Id, DecisionKind.Edited,
            new DecisionEdits("Book movers", priya.Id, new DateOnly(2026, 10, 2), ProposedOwnerUserId: null, Reason: null),
            actor.Id, cancellationToken);

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        AppDbContext context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        TrackedAction stored = await context.TrackedActions.AsNoTracking().SingleAsync(cancellationToken);

        Assert.Equal(result.TrackedAction!.Id, stored.Id);
        Assert.Equal(proposal.Id, stored.ProposedActionId);
        Assert.Equal("Book movers", stored.Description);
        Assert.Equal(priya.Id, stored.OwnerUserId);
        Assert.Equal(new DateOnly(2026, 10, 2), stored.DueDate);
        Assert.Equal(ActionStatus.Open, stored.Status);
        Assert.Equal(DecidedAt, stored.CreatedAt);

        ProposedAction decided = await context.Set<ProposedAction>().AsNoTracking().SingleAsync(cancellationToken);

        Assert.Equal(ReviewState.Edited, decided.ReviewState);
        Assert.Equal(actor.Id, decided.DecidedByUserId);
        Assert.Equal(DecidedAt, decided.DecidedAt);
        Assert.Null(decided.RejectionReason);

        // The AI's values are untouched: the edit lives on the Tracked Action.
        Assert.Equal("Order the replacement scanners.", decided.Description);

        // The Audit Trail's read over both targets, in its documented order.
        IReadOnlyList<ActionRevision> trail = await AuditTrailAsync(context, proposal.Id, stored.Id, cancellationToken);

        Assert.Equal(
            [
                (RevisionKind.AiProposal, 1, (string?)null),
                (RevisionKind.ReviewDecision, 2, "ReviewState"),
                (RevisionKind.FieldEdit, 3, "Description"),
                (RevisionKind.FieldEdit, 4, "OwnerUserId"),
                (RevisionKind.FieldEdit, 5, "DueDate"),
            ],
            trail.Select(revision => (revision.Kind, revision.Sequence, revision.Field)));

        AssertCopyAgreesWithDecision(decided, trail[1]);
    }

    [Theory]
    [InlineData(DecisionKind.Approved, null)]
    [InlineData(DecisionKind.Rejected, null)]
    [InlineData(DecisionKind.Rejected, "  not an action ")]
    public async Task The_decision_copy_agrees_with_the_review_decision_revision(DecisionKind kind, string? reason)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(
            $"decision_copy_{kind}_{(reason is null ? "bare" : "reason")}",
            cancellationToken);

        ProposedAction proposal = await SavedPendingProposalAsync(services, cancellationToken);
        User actor = await SavedUserAsync(services, "officer", "Olu Officer", cancellationToken);

        DecisionEdits edits = kind == DecisionKind.Approved
            ? new DecisionEdits("Order the replacement scanners.", null, Proposed, null, null)
            : new DecisionEdits(null, null, null, null, reason);

        await DecideAndSaveAsync(services, proposal.Id, kind, edits, actor.Id, cancellationToken);

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        AppDbContext context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        ProposedAction decided = await context.Set<ProposedAction>().AsNoTracking().SingleAsync(cancellationToken);

        ActionRevision decision = await context.ActionRevisions
            .AsNoTracking()
            .SingleAsync(revision => revision.Kind == RevisionKind.ReviewDecision, cancellationToken);

        AssertCopyAgreesWithDecision(decided, decision);

        Assert.Equal(kind == DecisionKind.Approved ? 1 : 0, await context.TrackedActions.CountAsync(cancellationToken));

        if (kind == DecisionKind.Rejected)
        {
            Assert.Equal(reason?.Trim(), decided.RejectionReason);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Through the handler (Story 3.2)
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(DecisionKind.Approved)]
    [InlineData(DecisionKind.Edited)]
    [InlineData(DecisionKind.Rejected)]
    public async Task A_decision_committed_by_the_handler_keeps_the_copy_agreeing_with_its_revision(DecisionKind kind)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync($"handler_copy_{kind}", cancellationToken);

        ProposedAction proposal = await SavedPendingProposalAsync(services, cancellationToken);
        User actor = await SavedUserAsync(services, "officer", "Olu Officer", cancellationToken);

        DecideProposalCommand command = kind switch
        {
            // The proposal's own values, with no owner: its "Dana Whitfield" matches nobody here.
            DecisionKind.Approved => new() { Decision = ReviewVerb.Approve, Description = "Order the replacement scanners.", DueDate = Proposed },
            DecisionKind.Edited => new() { Decision = ReviewVerb.Approve, Description = "Book movers", OwnerUserId = actor.Id, DueDate = Proposed },
            _ => new() { Decision = ReviewVerb.Reject, Reason = "  not an action " },
        };

        ProposalDecisionDto answer;

        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            answer = await HandlerIn(scope, actor.Id).HandleAsync(proposal.Id, command, cancellationToken);
        }

        await using AsyncServiceScope check = services.CreateAsyncScope();
        AppDbContext context = check.ServiceProvider.GetRequiredService<AppDbContext>();

        ProposedAction decided = await context.Set<ProposedAction>().AsNoTracking().SingleAsync(cancellationToken);

        ActionRevision decision = await context.ActionRevisions
            .AsNoTracking()
            .SingleAsync(revision => revision.Kind == RevisionKind.ReviewDecision, cancellationToken);

        AssertCopyAgreesWithDecision(decided, decision);

        Assert.Equal(answer.ReviewState, decided.ReviewState);
        Assert.Equal(actor.Id, decided.DecidedByUserId);
        Assert.Equal(DecidedAt, decided.DecidedAt);

        TrackedAction? tracked = await context.TrackedActions.AsNoTracking().SingleOrDefaultAsync(cancellationToken);

        Assert.Equal(answer.TrackedActionId, tracked?.Id);
        Assert.Equal(kind == DecisionKind.Rejected, tracked is null);

        if (kind == DecisionKind.Rejected)
        {
            Assert.Equal("not an action", decided.RejectionReason);
        }
        else
        {
            Assert.Equal(kind == DecisionKind.Edited ? ReviewState.Edited : ReviewState.Approved, decided.ReviewState);
            Assert.Equal(kind == DecisionKind.Edited ? actor.Id : null, tracked!.OwnerUserId);
        }
    }

    [Fact]
    public async Task The_meeting_list_counts_each_meetings_tracked_actions_in_sql()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(nameof(The_meeting_list_counts_each_meetings_tracked_actions_in_sql), cancellationToken);

        User actor = await SavedUserAsync(services, "officer", "Olu Officer", cancellationToken);

        // Two meetings, two proposals each; only the busy one's are decided, one each way.
        (Guid busy, IReadOnlyList<Guid> busyProposals) = await SavedMeetingWithProposalsAsync(services, "Busy sync", 3, cancellationToken);
        (Guid quiet, _) = await SavedMeetingWithProposalsAsync(services, "Quiet sync", 2, cancellationToken);

        await DecideThroughHandlerAsync(services, actor.Id, busyProposals[0], new() { Decision = ReviewVerb.Approve, Description = "Scanners 0", DueDate = Proposed }, cancellationToken);
        await DecideThroughHandlerAsync(services, actor.Id, busyProposals[1], new() { Decision = ReviewVerb.Approve, Description = "Edited", DueDate = null }, cancellationToken);
        await DecideThroughHandlerAsync(services, actor.Id, busyProposals[2], new() { Decision = ReviewVerb.Reject }, cancellationToken);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        // The real read seam, so the correlated count through proposal and run is translated by
        // Npgsql rather than evaluated by LINQ to Objects.
        MeetingsQueries queries = new(scope.ServiceProvider.GetRequiredService<IReadDb>());

        IReadOnlyList<MeetingSummaryDto> items = (await queries.ListAsync(null, null, cancellationToken)).Items;

        Assert.Equal(2, items.Single(meeting => meeting.Id == busy).TrackedActionCount);
        Assert.Equal(0, items.Single(meeting => meeting.Id == quiet).TrackedActionCount);
        Assert.Equal(1, items.Single(meeting => meeting.Id == busy).RunCount);
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The handler over the real ports in <paramref name="scope"/>, with the actor a token would
    /// have carried and the instant a clock would have read.
    /// </summary>
    private static DecideProposalHandler HandlerIn(AsyncServiceScope scope, Guid actorUserId) =>
        new(
            scope.ServiceProvider.GetRequiredService<IExtractionRunRepository>(),
            scope.ServiceProvider.GetRequiredService<IActionRepository>(),
            scope.ServiceProvider.GetRequiredService<IActionRevisionRepository>(),
            scope.ServiceProvider.GetRequiredService<IReadDb>(),
            new FixedCurrentUser(actorUserId),
            new FixedClock(DecidedAt),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>());

    private static async Task DecideThroughHandlerAsync(
        IServiceProvider services,
        Guid actorUserId,
        Guid proposalId,
        DecideProposalCommand command,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        await HandlerIn(scope, actorUserId).HandleAsync(proposalId, command, cancellationToken);
    }

    private static async Task<(Guid MeetingId, IReadOnlyList<Guid> ProposalIds)> SavedMeetingWithProposalsAsync(
        IServiceProvider services,
        string title,
        int count,
        CancellationToken cancellationToken)
    {
        Meeting meeting = Meeting.Create(title, Held, ["Dana Whitfield"], Guid.CreateVersion7(), Created);
        meeting.AttachNotes("Dana will order the scanners.", Created);

        ExtractionRun run = ExtractionRun.Start(
            meeting.Id, meeting.Notes!.Id, meeting.Notes.Sha256, Guid.CreateVersion7(),
            Metrics, ExtractionOutcome.Succeeded, null, warnings: null);

        IReadOnlyList<ActionRevision> revisions = run.AddProposals(
            [.. Enumerable.Range(0, count).Select(index => new ProposedActionDraft($"Scanners {index}", "Facilities", Proposed, 0.91, "Dana will order the scanners."))],
            Created);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IMeetingRepository>().Add(meeting);
        scope.ServiceProvider.GetRequiredService<IExtractionRunRepository>().Add(run);
        scope.ServiceProvider.GetRequiredService<IActionRevisionRepository>().AddRange(revisions);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        return (meeting.Id, [.. run.Proposals.Select(proposal => proposal.Id)]);
    }

    private sealed class FixedCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId { get; } = userId;

        public string DisplayName => "Olu Officer";

        public Role Role => Role.ActionOfficer;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    /// <summary>
    /// The agreement Story 3.2 relies on: the proposal's fast-read copy says what the ReviewDecision
    /// revision — the source of truth — says, including a rejection's reason behind its prefix.
    /// </summary>
    private static void AssertCopyAgreesWithDecision(ProposedAction decided, ActionRevision decision)
    {
        Assert.Equal(RevisionTargetType.ProposedAction, decision.TargetType);
        Assert.Equal(decided.Id, decision.TargetId);
        Assert.Equal(decided.DecidedByUserId, decision.ActorUserId);
        Assert.Equal(decided.DecidedAt, decision.OccurredAt);

        string expected = decided.RejectionReason is null
            ? decided.ReviewState.ToString()
            : $"{decided.ReviewState}: {decided.RejectionReason}";

        Assert.Equal(expected, decision.NewValue);
    }

    private static async Task<IReadOnlyList<ActionRevision>> AuditTrailAsync(
        AppDbContext context,
        Guid proposalId,
        Guid trackedActionId,
        CancellationToken cancellationToken) =>
        await context.ActionRevisions
            .AsNoTracking()
            .Where(revision =>
                (revision.TargetType == RevisionTargetType.ProposedAction && revision.TargetId == proposalId)
                || (revision.TargetType == RevisionTargetType.TrackedAction && revision.TargetId == trackedActionId))
            .OrderBy(revision => revision.OccurredAt)
            .ThenBy(revision => revision.Sequence)
            .ToListAsync(cancellationToken);

    private static async Task<ProposedAction> LoadProposalAsync(AsyncServiceScope scope, Guid proposalId, CancellationToken cancellationToken)
    {
        Guid runId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<ProposedAction>()
            .Where(proposal => proposal.Id == proposalId)
            .Select(proposal => proposal.ExtractionRunId)
            .SingleAsync(cancellationToken);

        ExtractionRun? run = await scope.ServiceProvider
            .GetRequiredService<IExtractionRunRepository>()
            .FindByIdAsync(runId, cancellationToken);

        return run!.Proposals.Single(proposal => proposal.Id == proposalId);
    }

    private static async Task CommitDecisionAsync(AsyncServiceScope scope, DecisionResult result, CancellationToken cancellationToken)
    {
        if (result.TrackedAction is not null)
        {
            scope.ServiceProvider.GetRequiredService<IActionRepository>().Add(result.TrackedAction);
        }

        scope.ServiceProvider.GetRequiredService<IActionRevisionRepository>().AddRange(result.Revisions);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Loads the run, decides its proposal, and commits the whole result in one unit of work — the
    /// shape <see cref="DecideProposalHandler"/> has, without its validation, so a test can reach
    /// the database with a result the handler would have refused.
    /// </summary>
    private static async Task<DecisionResult> DecideAndSaveAsync(
        IServiceProvider services,
        Guid proposalId,
        DecisionKind kind,
        DecisionEdits edits,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        DecisionResult result = (await LoadProposalAsync(scope, proposalId, cancellationToken))
            .Decide(kind, edits, actorUserId, DecidedAt);

        await CommitDecisionAsync(scope, result, cancellationToken);

        return result;
    }

    private static async Task<ProposedAction> SavedPendingProposalAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        Meeting meeting = Meeting.Create("Weekly sync", Held, ["Dana Whitfield"], Guid.CreateVersion7(), Created);
        meeting.AttachNotes("Dana will order the scanners.", Created);

        ExtractionRun run = ExtractionRun.Start(
            meeting.Id, meeting.Notes!.Id, meeting.Notes.Sha256, Guid.CreateVersion7(),
            Metrics, ExtractionOutcome.Succeeded, null, warnings: null);

        IReadOnlyList<ActionRevision> revisions = run.AddProposals(
            [new ProposedActionDraft("Order the replacement scanners.", "Dana Whitfield", Proposed, 0.91, "Dana will order the scanners.")],
            Created);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IMeetingRepository>().Add(meeting);
        scope.ServiceProvider.GetRequiredService<IExtractionRunRepository>().Add(run);
        scope.ServiceProvider.GetRequiredService<IActionRevisionRepository>().AddRange(revisions);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        return run.Proposals[0];
    }

    private static async Task<User> SavedUserAsync(
        IServiceProvider services,
        string username,
        string displayName,
        CancellationToken cancellationToken)
    {
        User user = User.Register(username, displayName, new string('h', 60), Role.ActionOfficer, Created);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUserRepository>().Add(user);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        return user;
    }

    private static async Task<string> ConnectionStringAsync(IServiceProvider services)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        return scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString()!;
    }

    private async Task<string> MigratedDatabaseAsync(string name, CancellationToken cancellationToken)
    {
        string connectionString = await postgres.CreateDatabaseAsync(name, cancellationToken);

        await using ServiceProvider services = TestHost.Build(connectionString, TestHost.SeedingOff);
        await TestHost.MigrateAsync(services, cancellationToken);

        return connectionString;
    }

    private async Task<ServiceProvider> MigratedHostAsync(string name, CancellationToken cancellationToken)
    {
        string connectionString = await postgres.CreateDatabaseAsync(name, cancellationToken);

        ServiceProvider services = TestHost.Build(connectionString, TestHost.SeedingOff);

        await TestHost.MigrateAsync(services, cancellationToken);

        return services;
    }

    private static async Task<string?> IndexDefinitionAsync(string connectionString, string index, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand command = new(
            """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public' AND tablename = 'tracked_actions' AND indexname = @name
            """,
            connection);

        command.Parameters.AddWithValue("name", index);

        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task<List<(string Principal, string OnDelete)>> ForeignKeysAsync(
        string connectionString,
        string table,
        string column,
        CancellationToken cancellationToken)
    {
        List<(string, string)> foreignKeys = [];

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand command = new(
            """
            SELECT c.confrelid::regclass::text, c.confdeltype::text
            FROM pg_constraint c
            JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY (c.conkey)
            WHERE c.contype = 'f' AND c.conrelid = @table::regclass AND a.attname = @column
            """,
            connection);

        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("column", column);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            foreignKeys.Add((reader.GetString(0), reader.GetString(1)));
        }

        return foreignKeys;
    }

    private static async Task<Dictionary<string, (string Type, string Nullable)>> ColumnsAsync(
        string connectionString,
        string table,
        CancellationToken cancellationToken)
    {
        Dictionary<string, (string, string)> columns = new(StringComparer.Ordinal);

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand command = new(
            """
            SELECT column_name, data_type, is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = @table
            """,
            connection);

        command.Parameters.AddWithValue("table", table);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            columns[reader.GetString(0)] = (reader.GetString(1), reader.GetString(2));
        }

        return columns;
    }
}
