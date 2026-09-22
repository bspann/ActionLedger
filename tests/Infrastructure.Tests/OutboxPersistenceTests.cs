using System.Data.Common;
using System.Text.Json;
using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Review;
using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Extraction;
using ActionLedger.Domain.Meetings;
using ActionLedger.Domain.Users;
using ActionLedger.Domain.Webhooks;
using ActionLedger.Infrastructure.Persistence;
using ActionLedger.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ActionLedger.Infrastructure.Tests;

/// <summary>
/// AD-8, AD-20 and NFR-3 for Story 3.3 against a real PostgreSQL 18: the migration creates the
/// subscription and outbox tables as specified, an approval committed through
/// <see cref="DecideProposalHandler"/> writes one Pending outbox row per active matching
/// subscription in the same transaction, and a failure after the outbox insert leaves neither the
/// Tracked Action nor the row behind.
/// </summary>
[Collection(PostgresFixture.CollectionName)]
public sealed class OutboxPersistenceTests(PostgresFixture postgres)
{
    private static readonly DateOnly Held = new(2026, 9, 22);

    private static readonly DateTimeOffset Created = new(2026, 9, 22, 14, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset DecidedAt = new(2026, 9, 22, 15, 5, 12, TimeSpan.Zero);

    /// <summary>What the context's own clock reads, apart from the decision's, so the two are told apart.</summary>
    private static readonly DateTimeOffset CommittedAt = new(2026, 9, 22, 15, 5, 13, TimeSpan.Zero);

    private static readonly DateOnly Proposed = new(2026, 9, 25);

    private static readonly DateOnly Moved = new(2026, 10, 3);

    private static readonly ExtractionRunMetadata Metrics = new(
        "Fake", "fixture-catalog", "v1", "1", Created, DurationMs: 87, InputTokens: 0, OutputTokens: 0);

    // ---------------------------------------------------------------------------------------
    // The schema
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task The_migration_creates_webhook_subscriptions_with_a_text_array_of_event_types()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await MigratedDatabaseAsync("outbox_schema_subscriptions", cancellationToken);

        Dictionary<string, (string Type, string Udt, string Nullable)> columns =
            await ColumnsAsync(connectionString, "webhook_subscriptions", cancellationToken);

        Assert.Equal(["event_types", "id", "is_active", "secret", "url"], columns.Keys.Order(StringComparer.Ordinal));

        Assert.Equal(("uuid", "uuid", "NO"), columns["id"]);
        Assert.Equal(("character varying", "varchar", "NO"), columns["url"]);
        Assert.Equal(("character varying", "varchar", "NO"), columns["secret"]);
        Assert.Equal(("boolean", "bool", "NO"), columns["is_active"]);

        // text[] — information_schema says ARRAY and names the element type through udt_name.
        Assert.Equal(("ARRAY", "_text", "NO"), columns["event_types"]);
    }

    [Fact]
    public async Task The_migration_creates_outbox_messages_with_a_jsonb_payload_and_a_string_state()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await MigratedDatabaseAsync("outbox_schema_messages", cancellationToken);

        Dictionary<string, (string Type, string Udt, string Nullable)> columns =
            await ColumnsAsync(connectionString, "outbox_messages", cancellationToken);

        // The ARCHITECTURE.md ERD, exactly: a column added without a decision behind it reddens here.
        Assert.Equal(
            [
                "attempt_count", "created_at", "event_id", "event_type", "id", "last_error",
                "last_status_code", "next_attempt_at", "payload", "state", "subscription_id",
            ],
            columns.Keys.Order(StringComparer.Ordinal));

        Assert.Equal(("uuid", "uuid", "NO"), columns["id"]);
        Assert.Equal(("uuid", "uuid", "NO"), columns["subscription_id"]);
        Assert.Equal(("uuid", "uuid", "NO"), columns["event_id"]);
        Assert.Equal(("character varying", "varchar", "NO"), columns["event_type"]);
        Assert.Equal(("jsonb", "jsonb", "NO"), columns["payload"]);
        Assert.Equal(("character varying", "varchar", "NO"), columns["state"]);
        Assert.Equal(("integer", "int4", "NO"), columns["attempt_count"]);
        Assert.Equal(("timestamp with time zone", "timestamptz", "NO"), columns["next_attempt_at"]);
        Assert.Equal(("integer", "int4", "YES"), columns["last_status_code"]);
        Assert.Equal(("character varying", "varchar", "YES"), columns["last_error"]);
        Assert.Equal(("timestamp with time zone", "timestamptz", "NO"), columns["created_at"]);

        // information_schema never lists system columns; a real `xmin` would have failed CREATE TABLE.
        Assert.DoesNotContain(OutboxMessageConfiguration.ConcurrencyTokenProperty, columns.Keys);
    }

    [Fact]
    public async Task The_claim_index_covers_state_then_next_attempt_and_is_not_unique()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await MigratedDatabaseAsync("outbox_schema_index", cancellationToken);

        string? definition = await IndexDefinitionAsync(
            connectionString, "outbox_messages", OutboxMessageConfiguration.StateAndNextAttemptIndexName, cancellationToken);

        Assert.NotNull(definition);
        Assert.Equal("ix_outbox_messages_state_next_attempt_at", OutboxMessageConfiguration.StateAndNextAttemptIndexName);
        Assert.DoesNotContain("UNIQUE", definition, StringComparison.Ordinal);
        Assert.Contains("(state, next_attempt_at)", definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_only_foreign_key_is_the_subscription_and_it_restricts_deletes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await MigratedDatabaseAsync("outbox_schema_fk", cancellationToken);

        (string Column, string Principal, string OnDelete) foreignKey =
            Assert.Single(await ForeignKeysAsync(connectionString, "outbox_messages", cancellationToken));

        // No FK to tracked_actions: the ERD has no such column, and the payload names the action.
        Assert.Equal(("subscription_id", "webhook_subscriptions", "r"), foreignKey);
    }

    [Fact]
    public async Task The_outbox_concurrency_token_is_the_system_column()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync("outbox_schema_xmin", cancellationToken);
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        IModel model = scope.ServiceProvider.GetRequiredService<AppDbContext>().Model;

        IProperty? token = model
            .FindEntityType(typeof(OutboxMessage))!
            .FindProperty(OutboxMessageConfiguration.ConcurrencyTokenProperty);

        Assert.NotNull(token);
        Assert.True(token.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, token.ValueGenerated);

        // Nothing updates a subscription, so it carries no token (AD-20's four roots).
        Assert.Null(model.FindEntityType(typeof(WebhookSubscription))!.FindProperty("xmin"));
    }

    // ---------------------------------------------------------------------------------------
    // The pipeline, through the handler
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task An_approval_writes_one_pending_row_per_matching_subscription_sharing_one_event_id()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync("outbox_two_subscriptions", cancellationToken);

        (ProposedAction proposal, Meeting meeting) = await SavedPendingProposalAsync(services, cancellationToken);
        User actor = await SavedUserAsync(services, "officer", "Olu Officer", cancellationToken);
        User owner = await SavedUserAsync(services, "dana", "Dana Whitfield", cancellationToken);

        WebhookSubscription first = await SavedSubscriptionAsync(services, ["action.approved"], isActive: true, cancellationToken);
        WebhookSubscription second = await SavedSubscriptionAsync(services, ["other.event", "action.approved"], isActive: true, cancellationToken);

        // "Dana Whitfield" is the pre-selected owner, so keeping her is a plain Approve.
        ProposalDecisionDto answer = await DecideThroughHandlerAsync(
            services, actor.Id, proposal.Id,
            new() { Decision = ReviewVerb.Approve, Description = "Order the replacement scanners.", OwnerUserId = owner.Id, DueDate = Proposed },
            cancellationToken);

        IReadOnlyList<OutboxMessage> rows = await OutboxRowsAsync(services, cancellationToken);

        Assert.Equal(2, rows.Count);
        Assert.Equal(
            new[] { first.Id, second.Id }.Order(),
            rows.Select(row => row.SubscriptionId).Order());

        Assert.Single(rows.Select(row => row.EventId).Distinct());
        Assert.Equal(2, rows.Select(row => row.Id).Distinct().Count());
        Assert.Single(rows.Select(row => row.Payload).Distinct());

        Assert.All(rows, row =>
        {
            Assert.Equal("action.approved", row.EventType);
            Assert.Equal(OutboxState.Pending, row.State);
            Assert.Equal(0, row.AttemptCount);
            Assert.Equal(CommittedAt, row.NextAttemptAt);
            Assert.Equal(CommittedAt, row.CreatedAt);
            Assert.Null(row.LastStatusCode);
            Assert.Null(row.LastError);
        });

        using JsonDocument payload = JsonDocument.Parse(rows[0].Payload);
        JsonElement root = payload.RootElement;

        Assert.Equal(
            ["eventId", "eventType", "occurredAt", "proposedAction", "reviewDecision", "trackedAction"],
            root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

        Assert.Equal(rows[0].EventId, root.GetProperty("eventId").GetGuid());
        Assert.Equal("action.approved", root.GetProperty("eventType").GetString());
        Assert.Equal(DecidedAt, root.GetProperty("occurredAt").GetDateTimeOffset());

        JsonElement tracked = root.GetProperty("trackedAction");

        Assert.Equal(answer.TrackedActionId, tracked.GetProperty("id").GetGuid());
        Assert.Equal("Order the replacement scanners.", tracked.GetProperty("description").GetString());
        Assert.Equal(owner.Id, tracked.GetProperty("ownerId").GetGuid());
        Assert.Equal("Dana Whitfield", tracked.GetProperty("ownerName").GetString());
        Assert.Equal("2026-09-25", tracked.GetProperty("dueDate").GetString());
        Assert.Equal("Open", tracked.GetProperty("status").GetString());
        Assert.Equal(meeting.Id, tracked.GetProperty("meetingId").GetGuid());
        Assert.Equal("Weekly sync", tracked.GetProperty("meetingTitle").GetString());

        JsonElement proposed = root.GetProperty("proposedAction");

        Assert.Equal(proposal.Id, proposed.GetProperty("id").GetGuid());
        Assert.Equal("Order the replacement scanners.", proposed.GetProperty("description").GetString());
        Assert.Equal("Dana Whitfield", proposed.GetProperty("suggestedOwner").GetString());
        Assert.Equal("2026-09-25", proposed.GetProperty("suggestedDueDate").GetString());
        Assert.Equal(0.91, proposed.GetProperty("confidence").GetDouble());
        Assert.Equal("Dana will order the scanners.", proposed.GetProperty("sourceExcerpt").GetString());

        JsonElement decision = root.GetProperty("reviewDecision");

        Assert.Equal("Approved", decision.GetProperty("kind").GetString());
        Assert.Equal(actor.Id, decision.GetProperty("byUserId").GetGuid());
        Assert.Equal("Olu Officer", decision.GetProperty("byUserName").GetString());
        Assert.Equal(DecidedAt, decision.GetProperty("at").GetDateTimeOffset());

        // The subscription's secret never rides in a payload.
        Assert.DoesNotContain(first.Secret, rows[0].Payload, StringComparison.Ordinal);
        Assert.DoesNotContain(second.Secret, rows[0].Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_edit_and_approve_carries_the_edited_action_and_the_original_proposal()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync("outbox_edited", cancellationToken);

        (ProposedAction proposal, _) = await SavedPendingProposalAsync(services, cancellationToken);
        User actor = await SavedUserAsync(services, "officer", "Olu Officer", cancellationToken);
        await SavedSubscriptionAsync(services, ["action.approved"], isActive: true, cancellationToken);

        await DecideThroughHandlerAsync(
            services, actor.Id, proposal.Id,
            new() { Decision = ReviewVerb.Approve, Description = "Book movers", OwnerUserId = actor.Id, DueDate = Moved },
            cancellationToken);

        OutboxMessage row = Assert.Single(await OutboxRowsAsync(services, cancellationToken));

        using JsonDocument payload = JsonDocument.Parse(row.Payload);
        JsonElement root = payload.RootElement;

        Assert.Equal("Edited", root.GetProperty("reviewDecision").GetProperty("kind").GetString());

        Assert.Equal("Book movers", root.GetProperty("trackedAction").GetProperty("description").GetString());
        Assert.Equal(actor.Id, root.GetProperty("trackedAction").GetProperty("ownerId").GetGuid());
        Assert.Equal("Olu Officer", root.GetProperty("trackedAction").GetProperty("ownerName").GetString());
        Assert.Equal("2026-10-03", root.GetProperty("trackedAction").GetProperty("dueDate").GetString());

        Assert.Equal("Order the replacement scanners.", root.GetProperty("proposedAction").GetProperty("description").GetString());
        Assert.Equal("Dana Whitfield", root.GetProperty("proposedAction").GetProperty("suggestedOwner").GetString());
        Assert.Equal("2026-09-25", root.GetProperty("proposedAction").GetProperty("suggestedDueDate").GetString());
    }

    [Fact]
    public async Task A_rejection_writes_no_outbox_row()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync("outbox_rejected", cancellationToken);

        (ProposedAction proposal, _) = await SavedPendingProposalAsync(services, cancellationToken);
        User actor = await SavedUserAsync(services, "officer", "Olu Officer", cancellationToken);
        await SavedSubscriptionAsync(services, ["action.approved"], isActive: true, cancellationToken);

        await DecideThroughHandlerAsync(
            services, actor.Id, proposal.Id, new() { Decision = ReviewVerb.Reject, Reason = "not an action" }, cancellationToken);

        Assert.Empty(await OutboxRowsAsync(services, cancellationToken));
    }

    [Fact]
    public async Task An_inactive_or_non_matching_subscription_gets_no_row()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync("outbox_inactive_nonmatching", cancellationToken);

        (ProposedAction proposal, _) = await SavedPendingProposalAsync(services, cancellationToken);
        User actor = await SavedUserAsync(services, "officer", "Olu Officer", cancellationToken);

        await SavedSubscriptionAsync(services, ["action.approved"], isActive: false, cancellationToken);
        await SavedSubscriptionAsync(services, ["other.event"], isActive: true, cancellationToken);

        await DecideThroughHandlerAsync(
            services, actor.Id, proposal.Id,
            new() { Decision = ReviewVerb.Approve, Description = "Order the replacement scanners.", DueDate = Proposed },
            cancellationToken);

        Assert.Empty(await OutboxRowsAsync(services, cancellationToken));
        Assert.Equal(1, await CountAsync(services, "tracked_actions", cancellationToken));
    }

    [Fact]
    public async Task An_approval_with_no_subscriptions_commits_with_no_rows()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync("outbox_no_subscriptions", cancellationToken);

        (ProposedAction proposal, _) = await SavedPendingProposalAsync(services, cancellationToken);
        User actor = await SavedUserAsync(services, "officer", "Olu Officer", cancellationToken);

        ProposalDecisionDto answer = await DecideThroughHandlerAsync(
            services, actor.Id, proposal.Id,
            new() { Decision = ReviewVerb.Approve, Description = "Order the replacement scanners.", DueDate = Proposed },
            cancellationToken);

        Assert.NotNull(answer.TrackedActionId);
        Assert.Equal(1, await CountAsync(services, "tracked_actions", cancellationToken));
        Assert.Empty(await OutboxRowsAsync(services, cancellationToken));
    }

    [Fact]
    public async Task An_unassigned_owner_is_null_in_the_payload()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync("outbox_unassigned", cancellationToken);

        (ProposedAction proposal, _) = await SavedPendingProposalAsync(services, cancellationToken);
        User actor = await SavedUserAsync(services, "officer", "Olu Officer", cancellationToken);
        await SavedSubscriptionAsync(services, ["action.approved"], isActive: true, cancellationToken);

        // Nobody on this roster is "Dana Whitfield", so no owner is pre-selected and null is a plain Approve.
        await DecideThroughHandlerAsync(
            services, actor.Id, proposal.Id,
            new() { Decision = ReviewVerb.Approve, Description = "Order the replacement scanners.", DueDate = Proposed },
            cancellationToken);

        OutboxMessage row = Assert.Single(await OutboxRowsAsync(services, cancellationToken));

        using JsonDocument payload = JsonDocument.Parse(row.Payload);
        JsonElement tracked = payload.RootElement.GetProperty("trackedAction");

        Assert.Equal(JsonValueKind.Null, tracked.GetProperty("ownerId").ValueKind);
        Assert.Equal(JsonValueKind.Null, tracked.GetProperty("ownerName").ValueKind);
        Assert.Equal("Approved", payload.RootElement.GetProperty("reviewDecision").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task A_failure_after_the_outbox_insert_leaves_neither_the_action_nor_the_row()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        FailAfterOutboxInsert failure = new();

        await using ServiceProvider services = await MigratedHostAsync(
            "outbox_nfr3_forced_failure",
            cancellationToken,
            collection => collection.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(failure)));

        (ProposedAction proposal, _) = await SavedPendingProposalAsync(services, cancellationToken);
        User actor = await SavedUserAsync(services, "officer", "Olu Officer", cancellationToken);
        await SavedSubscriptionAsync(services, ["action.approved"], isActive: true, cancellationToken);

        failure.Armed = true;

        await Assert.ThrowsAnyAsync<Exception>(() => DecideThroughHandlerAsync(
            services, actor.Id, proposal.Id,
            new() { Decision = ReviewVerb.Approve, Description = "Order the replacement scanners.", DueDate = Proposed },
            cancellationToken));

        failure.Armed = false;

        // The interceptor fired after PostgreSQL had executed the insert — the row existed inside
        // the transaction, and the rollback is what removed it.
        Assert.True(failure.Tripped);

        Assert.Equal(0, await CountAsync(services, "tracked_actions", cancellationToken));
        Assert.Equal(0, await CountAsync(services, "outbox_messages", cancellationToken));

        await using AsyncServiceScope check = services.CreateAsyncScope();

        ProposedAction reloaded = await check.ServiceProvider.GetRequiredService<AppDbContext>()
            .Set<ProposedAction>()
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == proposal.Id, cancellationToken);

        Assert.Equal(ReviewState.Pending, reloaded.ReviewState);
        Assert.Null(reloaded.DecidedByUserId);
    }

    // ---------------------------------------------------------------------------------------
    // The pipeline, saved directly
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_save_clears_the_events_it_carried()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync("outbox_events_cleared", cancellationToken);

        (ProposedAction proposal, _) = await SavedPendingProposalAsync(services, cancellationToken);
        User actor = await SavedUserAsync(services, "officer", "Olu Officer", cancellationToken);
        await SavedSubscriptionAsync(services, ["action.approved"], isActive: true, cancellationToken);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        TrackedAction tracked = await DecideAndStageAsync(scope, proposal.Id, actor.Id, cancellationToken);

        Assert.Single(tracked.DomainEvents);

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        Assert.Empty(tracked.DomainEvents);
        Assert.Single(await OutboxRowsAsync(services, cancellationToken));
    }

    [Fact]
    public async Task A_suppressed_save_writes_no_row_and_still_clears_the_events()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync("outbox_suppressed", cancellationToken);

        (ProposedAction proposal, _) = await SavedPendingProposalAsync(services, cancellationToken);
        User actor = await SavedUserAsync(services, "officer", "Olu Officer", cancellationToken);
        await SavedSubscriptionAsync(services, ["action.approved"], isActive: true, cancellationToken);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        TrackedAction tracked = await DecideAndStageAsync(scope, proposal.Id, actor.Id, cancellationToken);

        await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .SaveChangesAsync(suppressOutbox: true, cancellationToken: cancellationToken);

        Assert.Empty(tracked.DomainEvents);
        Assert.Equal(1, await CountAsync(services, "tracked_actions", cancellationToken));
        Assert.Empty(await OutboxRowsAsync(services, cancellationToken));
    }

    [Fact]
    public async Task A_positional_accept_all_changes_flag_still_runs_the_pipeline()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync("outbox_positional_bool", cancellationToken);

        (ProposedAction proposal, _) = await SavedPendingProposalAsync(services, cancellationToken);
        User actor = await SavedUserAsync(services, "officer", "Olu Officer", cancellationToken);
        await SavedSubscriptionAsync(services, ["action.approved"], isActive: true, cancellationToken);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        await DecideAndStageAsync(scope, proposal.Id, actor.Id, cancellationToken);

        // EF's (bool acceptAllChangesOnSuccess, CancellationToken). A bool in first position must
        // never bind to the suppressing overload.
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync(true, cancellationToken);

        Assert.Single(await OutboxRowsAsync(services, cancellationToken));
    }

    [Fact]
    public async Task A_synchronous_save_is_refused()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync("outbox_sync_refused", cancellationToken);
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        AppDbContext context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Throws<NotSupportedException>(() => context.SaveChanges());
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// NFR-3's forced failure: throws once PostgreSQL has executed the command that inserts into
    /// <c>outbox_messages</c>, so the row exists inside the transaction when the save fails.
    /// </summary>
    private sealed class FailAfterOutboxInsert : DbCommandInterceptor
    {
        public bool Armed { get; set; }

        public bool Tripped { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (IsOutboxInsert(command))
            {
                // Closed first, so the rollback is not queued behind an open reader.
                await result.DisposeAsync();

                throw Trip();
            }

            return result;
        }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default) =>
            IsOutboxInsert(command) ? throw Trip() : new(result);

        private bool IsOutboxInsert(DbCommand command) =>
            Armed
            && command.CommandText.Contains("INSERT INTO outbox_messages", StringComparison.Ordinal);

        private InvalidOperationException Trip()
        {
            Tripped = true;

            return new InvalidOperationException("Forced failure after the outbox insert (NFR-3).");
        }
    }

    private static DecideProposalHandler HandlerIn(AsyncServiceScope scope, Guid actorUserId) =>
        new(
            scope.ServiceProvider.GetRequiredService<IExtractionRunRepository>(),
            scope.ServiceProvider.GetRequiredService<IActionRepository>(),
            scope.ServiceProvider.GetRequiredService<IActionRevisionRepository>(),
            scope.ServiceProvider.GetRequiredService<IReadDb>(),
            new FixedCurrentUser(actorUserId),
            new FixedClock(DecidedAt),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>());

    private static async Task<ProposalDecisionDto> DecideThroughHandlerAsync(
        IServiceProvider services,
        Guid actorUserId,
        Guid proposalId,
        DecideProposalCommand command,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        return await HandlerIn(scope, actorUserId).HandleAsync(proposalId, command, cancellationToken);
    }

    /// <summary>
    /// Loads the proposal as the handler does, approves it as it stands, and stages the result in
    /// <paramref name="scope"/> without committing, so a test chooses how the save happens.
    /// </summary>
    private static async Task<TrackedAction> DecideAndStageAsync(
        AsyncServiceScope scope,
        Guid proposalId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ExtractionRun run = (await scope.ServiceProvider
            .GetRequiredService<IExtractionRunRepository>()
            .FindByProposedActionIdAsync(proposalId, cancellationToken))!;

        ProposedAction proposal = run.Proposals.Single(candidate => candidate.Id == proposalId);

        DecisionResult result = proposal.Decide(
            DecisionKind.Approved,
            new DecisionEdits(proposal.Description, null, proposal.SuggestedDueDate, null, null),
            actorUserId,
            DecidedAt);

        scope.ServiceProvider.GetRequiredService<IActionRepository>().Add(result.TrackedAction!);
        scope.ServiceProvider.GetRequiredService<IActionRevisionRepository>().AddRange(result.Revisions);

        return result.TrackedAction!;
    }

    private static async Task<IReadOnlyList<OutboxMessage>> OutboxRowsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .OutboxMessages
            .AsNoTracking()
            .OrderBy(message => message.SubscriptionId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Counts a table's rows in raw SQL, so nothing EF has tracked can answer for the database.</summary>
    private static async Task<long> CountAsync(IServiceProvider services, string table, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = new(await ConnectionStringAsync(services));
        await connection.OpenAsync(cancellationToken);

        // The table name is one of this file's own literals, never input.
        await using NpgsqlCommand command = new($"SELECT count(*) FROM {table}", connection);

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<WebhookSubscription> SavedSubscriptionAsync(
        IServiceProvider services,
        string[] eventTypes,
        bool isActive,
        CancellationToken cancellationToken)
    {
        WebhookSubscription subscription = WebhookSubscription.Create(
            "https://receiver.example/hooks", $"secret-{Guid.CreateVersion7():N}", eventTypes, isActive);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        AppDbContext context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        context.WebhookSubscriptions.Add(subscription);
        await context.SaveChangesAsync(cancellationToken);

        return subscription;
    }

    private static async Task<(ProposedAction Proposal, Meeting Meeting)> SavedPendingProposalAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
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

        return (run.Proposals[0], meeting);
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

    /// <summary>
    /// A migrated host whose context clock reads <see cref="CommittedAt"/>, so an outbox row's
    /// instants are assertable.
    /// </summary>
    private async Task<ServiceProvider> MigratedHostAsync(
        string name,
        CancellationToken cancellationToken,
        Action<IServiceCollection>? configure = null)
    {
        string connectionString = await postgres.CreateDatabaseAsync(name, cancellationToken);

        ServiceProvider services = TestHost.Build(connectionString, TestHost.SeedingOff, collection =>
        {
            collection.AddSingleton<IClock>(new FixedClock(CommittedAt));
            configure?.Invoke(collection);
        });

        await TestHost.MigrateAsync(services, cancellationToken);

        return services;
    }

    private static async Task<string?> IndexDefinitionAsync(
        string connectionString,
        string table,
        string index,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand command = new(
            """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public' AND tablename = @table AND indexname = @name
            """,
            connection);

        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("name", index);

        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task<List<(string Column, string Principal, string OnDelete)>> ForeignKeysAsync(
        string connectionString,
        string table,
        CancellationToken cancellationToken)
    {
        List<(string, string, string)> foreignKeys = [];

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand command = new(
            """
            SELECT a.attname::text, c.confrelid::regclass::text, c.confdeltype::text
            FROM pg_constraint c
            JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY (c.conkey)
            WHERE c.contype = 'f' AND c.conrelid = @table::regclass
            """,
            connection);

        command.Parameters.AddWithValue("table", table);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            foreignKeys.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return foreignKeys;
    }

    private static async Task<Dictionary<string, (string Type, string Udt, string Nullable)>> ColumnsAsync(
        string connectionString,
        string table,
        CancellationToken cancellationToken)
    {
        Dictionary<string, (string, string, string)> columns = new(StringComparer.Ordinal);

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand command = new(
            """
            SELECT column_name, data_type, udt_name, is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = @table
            """,
            connection);

        command.Parameters.AddWithValue("table", table);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            columns[reader.GetString(0)] = (reader.GetString(1), reader.GetString(2), reader.GetString(3));
        }

        return columns;
    }
}
