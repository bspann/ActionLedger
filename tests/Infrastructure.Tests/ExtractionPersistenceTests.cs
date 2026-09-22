using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Extraction;
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
/// AD-6, AD-7, AD-10 and AD-20 for the extraction aggregate against a real PostgreSQL 18: the
/// migration produces the columns and indexes the conventions promise, a whole run persists as
/// inserts only, the revisions read back in their documented order, and a second run leaves the
/// first one's rows alone.
/// </summary>
[Collection(PostgresFixture.CollectionName)]
public sealed class ExtractionPersistenceTests(PostgresFixture postgres)
{
    private static readonly DateOnly Held = new(2026, 9, 22);

    private static readonly DateTimeOffset Created = new(2026, 9, 22, 14, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset StartedAt = new(2026, 9, 22, 9, 15, 42, TimeSpan.Zero);

    private static readonly ExtractionRunMetadata Metrics = new(
        "Fake", "fixture-catalog", "v1", "1", StartedAt, DurationMs: 87, InputTokens: 0, OutputTokens: 0);

    private static readonly ProposedActionDraft[] Drafts =
    [
        new("Order the replacement scanners.", "Dana Whitfield", new DateOnly(2026, 9, 25), 0.91, "Dana will order the scanners."),
        new("Book the range.", string.Empty, null, 0.62, "Someone should book the range."),
    ];

    // ---------------------------------------------------------------------------------------
    // The schema
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task The_migration_creates_extraction_runs_with_snake_case_columns()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await MigratedDatabaseAsync(nameof(The_migration_creates_extraction_runs_with_snake_case_columns), cancellationToken);

        Dictionary<string, string> columns = await ColumnsAsync(connectionString, "extraction_runs", cancellationToken);

        // The exact set, so a column added without a decision behind it reddens here.
        Assert.Equal(
            [
                "duration_ms", "failure_reason", "id", "input_tokens", "meeting_id", "meeting_notes_id",
                "model", "notes_sha256", "outcome", "output_tokens", "prompt_version", "provider",
                "schema_version", "started_at", "started_by_user_id", "warnings",
            ],
            columns.Keys.Order(StringComparer.Ordinal));

        Assert.Equal("uuid", columns["id"]);
        Assert.Equal("uuid", columns["meeting_id"]);
        Assert.Equal("uuid", columns["meeting_notes_id"]);
        Assert.Equal("uuid", columns["started_by_user_id"]);
        Assert.Equal("character", columns["notes_sha256"]);

        // AD-6's metrics, and AD-10's type rows: an instant is timestamptz, and the outcome and
        // the token counts are what FR-6 renders.
        Assert.Equal("timestamp with time zone", columns["started_at"]);
        Assert.Equal("integer", columns["duration_ms"]);
        Assert.Equal("integer", columns["input_tokens"]);
        Assert.Equal("integer", columns["output_tokens"]);

        // Enums are stored as strings (AD-10, Enums row), not as an ordinal nobody can read.
        Assert.Equal("character varying", columns["outcome"]);

        // A list of warnings is a PostgreSQL array, not a delimited string or a JSON blob.
        Assert.Equal("ARRAY", columns["warnings"]);
    }

    [Fact]
    public async Task The_migration_creates_proposed_actions_with_snake_case_columns()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await MigratedDatabaseAsync(nameof(The_migration_creates_proposed_actions_with_snake_case_columns), cancellationToken);

        Dictionary<string, string> columns = await ColumnsAsync(connectionString, "proposed_actions", cancellationToken);

        Assert.Equal(
            [
                "confidence", "description", "extraction_run_id", "id", "ordinal", "review_state",
                "source_excerpt", "suggested_due_date", "suggested_owner",
            ],
            columns.Keys.Order(StringComparer.Ordinal));

        Assert.Equal("uuid", columns["id"]);
        Assert.Equal("uuid", columns["extraction_run_id"]);
        Assert.Equal("integer", columns["ordinal"]);
        Assert.Equal("character varying", columns["description"]);
        Assert.Equal("character varying", columns["suggested_owner"]);
        Assert.Equal("double precision", columns["confidence"]);
        Assert.Equal("character varying", columns["review_state"]);

        // AD-10 — a due date is a calendar day, not an instant.
        Assert.Equal("date", columns["suggested_due_date"]);
    }

    [Fact]
    public async Task The_migration_creates_action_revisions_with_the_ten_AD7_columns()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await MigratedDatabaseAsync(nameof(The_migration_creates_action_revisions_with_the_ten_AD7_columns), cancellationToken);

        Dictionary<string, string> columns = await ColumnsAsync(connectionString, "action_revisions", cancellationToken);

        // AD-7 names exactly these ten. The list is the assertion.
        Assert.Equal(
            [
                "actor_user_id", "field", "id", "kind", "new_value", "occurred_at", "old_value",
                "sequence", "target_id", "target_type",
            ],
            columns.Keys.Order(StringComparer.Ordinal));

        Assert.Equal("uuid", columns["id"]);
        Assert.Equal("uuid", columns["target_id"]);
        Assert.Equal("uuid", columns["actor_user_id"]);
        Assert.Equal("integer", columns["sequence"]);
        Assert.Equal("character varying", columns["target_type"]);
        Assert.Equal("character varying", columns["kind"]);
        Assert.Equal("text", columns["new_value"]);
        Assert.Equal("timestamp with time zone", columns["occurred_at"]);
    }

    [Fact]
    public async Task The_actor_column_is_nullable_because_the_AI_is_not_a_user()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await MigratedDatabaseAsync(nameof(The_actor_column_is_nullable_because_the_AI_is_not_a_user), cancellationToken);

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand command = new(
            """
            SELECT column_name, is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'action_revisions'
              AND column_name IN ('actor_user_id', 'target_id')
            """,
            connection);

        Dictionary<string, string> nullability = new(StringComparer.Ordinal);

        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                nullability[reader.GetString(0)] = reader.GetString(1);
            }
        }

        // AD-7 — a null actor on an AiProposal row is the record, not a gap. A NOT NULL column
        // would make the aggregate's own rule unwritable.
        Assert.Equal("YES", nullability["actor_user_id"]);
        Assert.Equal("NO", nullability["target_id"]);
    }

    [Fact]
    public async Task The_proposal_concurrency_token_is_the_system_column_not_one_the_migration_added()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await postgres.CreateDatabaseAsync(nameof(The_proposal_concurrency_token_is_the_system_column_not_one_the_migration_added), cancellationToken);

        await using ServiceProvider services = TestHost.Build(connectionString, TestHost.SeedingOff);
        await TestHost.MigrateAsync(services, cancellationToken);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        // The absence check below is satisfied just as happily by a ProposedAction that has no
        // token at all, so it cannot stand alone. This is the half that says the token exists.
        IProperty? token = scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Model
            .FindEntityType(typeof(ProposedAction))!
            .FindProperty(ProposedActionConfiguration.ConcurrencyTokenProperty);

        Assert.NotNull(token);
        Assert.True(token.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, token.ValueGenerated);
        Assert.Equal(ProposedActionConfiguration.ConcurrencyTokenProperty, token.GetColumnName());

        Dictionary<string, string> columns = await ColumnsAsync(connectionString, "proposed_actions", cancellationToken);

        // PostgreSQL already has a system column by that name, so a real one would have failed the
        // CREATE TABLE outright — this is what proves the token is bound to the system column.
        Assert.DoesNotContain(ProposedActionConfiguration.ConcurrencyTokenProperty, columns.Keys);
    }

    [Fact]
    public async Task A_run_carries_no_concurrency_token_because_nothing_updates_one()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await postgres.CreateDatabaseAsync(nameof(A_run_carries_no_concurrency_token_because_nothing_updates_one), cancellationToken);

        await using ServiceProvider services = TestHost.Build(connectionString, TestHost.SeedingOff);
        await TestHost.MigrateAsync(services, cancellationToken);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        // AD-20 names four token-carrying types and ExtractionRun is not one of them. Without this
        // the "no xmin column" half above would pass on a run that quietly grew one.
        Assert.DoesNotContain(
            scope.ServiceProvider.GetRequiredService<AppDbContext>()
                .Model
                .FindEntityType(typeof(ExtractionRun))!
                .GetProperties(),
            property => property.IsConcurrencyToken);
    }

    [Fact]
    public async Task The_AD20_unique_index_on_run_and_ordinal_exists_by_name()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await MigratedDatabaseAsync(nameof(The_AD20_unique_index_on_run_and_ordinal_exists_by_name), cancellationToken);

        string? definition = await IndexDefinitionAsync(
            connectionString, "proposed_actions", ProposedActionConfiguration.RunAndOrdinalIndexName, cancellationToken);

        Assert.NotNull(definition);

        // AD-20 — unique. It is what makes "AI order" a stored fact rather than an insertion
        // accident: two rows cannot claim one position in a run's answer.
        Assert.Contains("UNIQUE", definition, StringComparison.Ordinal);
        Assert.Contains("extraction_run_id", definition, StringComparison.Ordinal);
        Assert.Contains("ordinal", definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_revision_target_index_exists_by_name_and_is_not_unique()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await MigratedDatabaseAsync(nameof(The_revision_target_index_exists_by_name_and_is_not_unique), cancellationToken);

        string? definition = await IndexDefinitionAsync(
            connectionString, "action_revisions", ActionRevisionConfiguration.TargetIndexName, cancellationToken);

        Assert.NotNull(definition);

        // The spine's Indexes row lists this one as non-unique, and it has to be: one aggregate
        // call writes several revisions against one target, and Story 3.2 writes more.
        Assert.DoesNotContain("UNIQUE", definition, StringComparison.Ordinal);
        Assert.Contains("target_type", definition, StringComparison.Ordinal);
        Assert.Contains("target_id", definition, StringComparison.Ordinal);
        Assert.Contains("sequence", definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_meeting_id_index_behind_the_list_run_count_exists_by_name()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await MigratedDatabaseAsync(nameof(The_meeting_id_index_behind_the_list_run_count_exists_by_name), cancellationToken);

        string? definition = await IndexDefinitionAsync(
            connectionString, "extraction_runs", ExtractionRunConfiguration.MeetingIndexName, cancellationToken);

        // NFR-2 — MeetingsQueries counts this column once per row of every Meeting page. Without
        // the index that is a sequential scan per row on the first list every user loads.
        Assert.NotNull(definition);
        Assert.Contains("meeting_id", definition, StringComparison.Ordinal);

        // Not unique: AD-5 puts any number of runs on a Meeting.
        Assert.DoesNotContain("UNIQUE", definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_proposals_cannot_claim_the_same_ordinal_in_one_run()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(nameof(Two_proposals_cannot_claim_the_same_ordinal_in_one_run), cancellationToken);

        Meeting meeting = await SavedMeetingAsync(services, "Weekly sync", cancellationToken);

        ExtractionRun first = RunFor(meeting, ExtractionOutcome.Succeeded);
        first.AddProposals([Drafts[0]], Created);
        await SaveRunAsync(services, first, cancellationToken);

        // The unique index is the backstop for a writer that reached the table another way: the
        // aggregate cannot produce a duplicate ordinal, so this is the only way to test it.
        await using NpgsqlConnection connection = new(await ConnectionStringAsync(services));

        await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await connection.OpenAsync(cancellationToken);

            await using NpgsqlCommand insert = new(
                """
                INSERT INTO proposed_actions
                  (id, extraction_run_id, ordinal, description, suggested_owner, confidence, source_excerpt, review_state)
                VALUES (@id, @run, 0, 'A duplicate position.', '', 0.5, 'Nobody said this.', 'Pending')
                """,
                connection);

            insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
            insert.Parameters.AddWithValue("run", first.Id);

            await insert.ExecuteNonQueryAsync(cancellationToken);
        });
    }

    // ---------------------------------------------------------------------------------------
    // The write
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_run_its_proposals_and_its_revisions_persist_as_inserts_only()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RecordingCommandInterceptor recorder = new();

        await using ServiceProvider services = await MigratedHostAsync(
            nameof(A_run_its_proposals_and_its_revisions_persist_as_inserts_only),
            cancellationToken,
            collection => collection.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(recorder)));

        Meeting meeting = await SavedMeetingAsync(services, "Weekly sync", cancellationToken);

        ExtractionRun run = RunFor(meeting, ExtractionOutcome.Succeeded);
        IReadOnlyList<ActionRevision> revisions = run.AddProposals(Drafts, Created);

        // Saving the Meeting was setup. Only the run's own write is of interest.
        recorder.Clear();

        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IExtractionRunRepository>().Add(run);
            scope.ServiceProvider.GetRequiredService<IActionRevisionRepository>().AddRange(revisions);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        }

        string[] touching =
        [
            .. recorder.Commands.Where(command =>
                command.Contains("extraction_runs", StringComparison.OrdinalIgnoreCase)
                || command.Contains("proposed_actions", StringComparison.OrdinalIgnoreCase)
                || command.Contains("action_revisions", StringComparison.OrdinalIgnoreCase)),
        ];

        Assert.NotEmpty(touching);
        Assert.Contains(touching, command => command.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase));

        // AD-10 — a new run with proposals and revisions persists as inserts only. An UPDATE here
        // would mean EF had been handed a graph it thought it already knew.
        Assert.DoesNotContain(touching, command => command.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(touching, command => command.Contains("DELETE ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_run_round_trips_every_AD6_field()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(nameof(A_run_round_trips_every_AD6_field), cancellationToken);

        Meeting meeting = await SavedMeetingAsync(services, "Weekly sync", cancellationToken);

        ExtractionRun run = RunFor(meeting, ExtractionOutcome.Succeeded);
        await SaveRunAsync(services, run, cancellationToken);

        ExtractionRun stored = await LoadRunAsync(services, run.Id, cancellationToken);

        Assert.Equal(meeting.Id, stored.MeetingId);
        Assert.Equal(meeting.Notes!.Id, stored.MeetingNotesId);
        Assert.Equal(meeting.Notes.Sha256, stored.NotesSha256);
        Assert.Equal("Fake", stored.Provider);
        Assert.Equal("fixture-catalog", stored.Model);
        Assert.Equal("v1", stored.PromptVersion);
        Assert.Equal("1", stored.SchemaVersion);
        Assert.Equal(StartedAt, stored.StartedAt);
        Assert.Equal(87, stored.DurationMs);
        Assert.Equal(0, stored.InputTokens);
        Assert.Equal(0, stored.OutputTokens);

        // Enums come back as the enum, having been stored as a string.
        Assert.Equal(ExtractionOutcome.Succeeded, stored.Outcome);
        Assert.Null(stored.FailureReason);

        // Version 7: EF generated nothing, or these would be database-side values (AD-10).
        Assert.Equal(7, (stored.Id.ToByteArray(bigEndian: true)[6] & 0xF0) >> 4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Warnings_round_trip_as_an_array_including_the_empty_one(int count)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(
            // The count leads, because PostgresFixture truncates the database name to 40
            // characters and these three rows are identical well before that.
            $"warnings_{count}_round_trip",
            cancellationToken);

        Meeting meeting = await SavedMeetingAsync(services, "Weekly sync", cancellationToken);

        string[] warnings = [.. Enumerable.Range(0, count).Select(index => $"Dropped a proposal: excerpt {index} was not in the notes.")];

        ExtractionRun run = ExtractionRun.Start(
            meeting.Id, meeting.Notes!.Id, meeting.Notes.Sha256, Guid.CreateVersion7(),
            Metrics, ExtractionOutcome.Succeeded, null, warnings);

        await SaveRunAsync(services, run, cancellationToken);

        Assert.Equal(warnings, (await LoadRunAsync(services, run.Id, cancellationToken)).Warnings);
    }

    [Fact]
    public async Task A_failed_run_round_trips_its_reason_and_writes_no_proposals()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(nameof(A_failed_run_round_trips_its_reason_and_writes_no_proposals), cancellationToken);

        Meeting meeting = await SavedMeetingAsync(services, "Weekly sync", cancellationToken);

        const string Reason = "The provider did not answer within Ai:CallTimeoutSeconds.";

        ExtractionRun run = RunFor(meeting, ExtractionOutcome.Failed, Reason);
        await SaveRunAsync(services, run, cancellationToken);

        ExtractionRun stored = await LoadRunAsync(services, run.Id, cancellationToken);

        Assert.Equal(ExtractionOutcome.Failed, stored.Outcome);
        Assert.Equal(Reason, stored.FailureReason);
        Assert.Empty(stored.Proposals);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Set<ProposedAction>()
            .CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Proposals_round_trip_in_ordinal_order_with_their_enum_as_a_string()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(nameof(Proposals_round_trip_in_ordinal_order_with_their_enum_as_a_string), cancellationToken);

        Meeting meeting = await SavedMeetingAsync(services, "Weekly sync", cancellationToken);

        ExtractionRun run = RunFor(meeting, ExtractionOutcome.Succeeded);
        IReadOnlyList<ActionRevision> revisions = run.AddProposals(Drafts, Created);

        await SaveRunAsync(services, run, cancellationToken, revisions);

        ExtractionRun stored = await LoadRunAsync(services, run.Id, cancellationToken);

        Assert.Equal(Drafts.Length, stored.Proposals.Count);

        // Unsorted on purpose: the repository's ordered Include is what puts them in AI order.
        Assert.Equal(
            [.. Drafts.Select(draft => draft.Description)],
            stored.Proposals.Select(proposal => proposal.Description));

        ProposedAction first = stored.Proposals.Single(proposal => proposal.Ordinal == 0);

        Assert.Equal("Dana Whitfield", first.SuggestedOwner);
        Assert.Equal(new DateOnly(2026, 9, 25), first.SuggestedDueDate);
        Assert.Equal(0.91, first.Confidence);
        Assert.Equal(ReviewState.Pending, first.ReviewState);

        // An unstated owner is "" and an unstated date is null; neither becomes the other.
        ProposedAction second = stored.Proposals.Single(proposal => proposal.Ordinal == 1);

        Assert.Equal(string.Empty, second.SuggestedOwner);
        Assert.Null(second.SuggestedDueDate);
    }

    [Fact]
    public async Task The_review_state_is_stored_as_a_pascal_case_string()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(nameof(The_review_state_is_stored_as_a_pascal_case_string), cancellationToken);

        Meeting meeting = await SavedMeetingAsync(services, "Weekly sync", cancellationToken);

        ExtractionRun run = RunFor(meeting, ExtractionOutcome.Succeeded);
        run.AddProposals([Drafts[0]], Created);

        await SaveRunAsync(services, run, cancellationToken);

        await using NpgsqlConnection connection = new(await ConnectionStringAsync(services));
        await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand command = new(
            "SELECT review_state, outcome FROM proposed_actions JOIN extraction_runs ON extraction_runs.id = extraction_run_id",
            connection);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        Assert.True(await reader.ReadAsync(cancellationToken));

        // Read as raw SQL rather than through EF, so the converter cannot answer for itself.
        Assert.Equal("Pending", reader.GetString(0));
        Assert.Equal("Succeeded", reader.GetString(1));
    }

    // ---------------------------------------------------------------------------------------
    // The revisions
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task One_AiProposal_revision_per_proposal_persists_with_a_null_actor()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(nameof(One_AiProposal_revision_per_proposal_persists_with_a_null_actor), cancellationToken);

        Meeting meeting = await SavedMeetingAsync(services, "Weekly sync", cancellationToken);

        ExtractionRun run = RunFor(meeting, ExtractionOutcome.Succeeded);
        IReadOnlyList<ActionRevision> minted = run.AddProposals(Drafts, Created);

        await SaveRunAsync(services, run, cancellationToken, minted);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        IReadOnlyList<ActionRevision> stored =
        [
            .. await scope.ServiceProvider.GetRequiredService<AppDbContext>()
                .ActionRevisions
                .AsNoTracking()
                .ToListAsync(cancellationToken),
        ];

        Assert.Equal(Drafts.Length, stored.Count);

        Assert.All(stored, revision =>
        {
            Assert.Equal(RevisionKind.AiProposal, revision.Kind);
            Assert.Equal(RevisionTargetType.ProposedAction, revision.TargetType);
            Assert.Null(revision.ActorUserId);
            Assert.Null(revision.Field);
            Assert.Null(revision.OldValue);
            Assert.Equal(1, revision.Sequence);
            Assert.Equal(Created, revision.OccurredAt);
        });

        Assert.Equal(
            [.. run.Proposals.Select(proposal => proposal.Id).Order()],
            stored.Select(revision => revision.TargetId).Order());
    }

    [Fact]
    public async Task The_revision_new_value_round_trips_as_the_json_the_aggregate_wrote()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(nameof(The_revision_new_value_round_trips_as_the_json_the_aggregate_wrote), cancellationToken);

        Meeting meeting = await SavedMeetingAsync(services, "Weekly sync", cancellationToken);

        ExtractionRun run = RunFor(meeting, ExtractionOutcome.Succeeded);
        IReadOnlyList<ActionRevision> minted = run.AddProposals(Drafts, Created);

        await SaveRunAsync(services, run, cancellationToken, minted);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        IReadOnlyList<ActionRevision> stored = await scope.ServiceProvider
            .GetRequiredService<IActionRevisionRepository>()
            .ListForTargetAsync(RevisionTargetType.ProposedAction, run.Proposals[0].Id, cancellationToken);

        Assert.Equal(minted[0].NewValue, Assert.Single(stored).NewValue);
    }

    [Fact]
    public async Task Revisions_for_a_target_read_back_by_occurred_at_then_sequence()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(nameof(Revisions_for_a_target_read_back_by_occurred_at_then_sequence), cancellationToken);

        Meeting meeting = await SavedMeetingAsync(services, "Weekly sync", cancellationToken);

        ExtractionRun run = RunFor(meeting, ExtractionOutcome.Succeeded);
        IReadOnlyList<ActionRevision> minted = run.AddProposals(Drafts, Created);

        await SaveRunAsync(services, run, cancellationToken, minted);

        Guid target = run.Proposals[0].Id;
        Guid other = run.Proposals[1].Id;

        // Story 2.5 writes exactly one revision per target, so the aggregate cannot produce an
        // order worth reading. Story 3.2's decisions will, and it will be the first code to rely
        // on this read — so the later rows are inserted here by raw SQL, the way the duplicate
        // ordinal above is.
        //
        // Sequence 2 deliberately carries the *latest* instant. AD-7 orders by OccurredAt first,
        // so the right answer puts it last while its sequence puts it second — and that is the
        // only arrangement that can catch a dropped OrderBy. Rows come back through
        // ix_action_revisions_target_type_target_id_sequence, which already yields them in
        // sequence order, so data whose two orderings agree would let an unordered read pass by
        // accident. Nothing in the database enforces "sequence increases with time" (see the note
        // on that index), which is exactly why the read must not lean on it.
        await InsertRevisionsAsync(
            services,
            cancellationToken,
            (target, 2, Created.AddMinutes(10)),
            // Two rows sharing an instant: only Sequence separates them, which is what the ThenBy
            // is for.
            (target, 4, Created.AddMinutes(5)),
            (target, 3, Created.AddMinutes(5)),
            // And one against the *other* proposal at the earliest instant, so a read that lost
            // its Where would sort this to the front.
            (other, 9, Created.AddMinutes(-10)));

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        IReadOnlyList<ActionRevision> forTarget = await scope.ServiceProvider
            .GetRequiredService<IActionRevisionRepository>()
            .ListForTargetAsync(RevisionTargetType.ProposedAction, target, cancellationToken);

        // Scoped to the one target: the other proposal's rows are absent however they sort.
        Assert.All(forTarget, revision => Assert.Equal(target, revision.TargetId));
        Assert.DoesNotContain(forTarget, revision => revision.TargetId == other);

        // AD-7's full order — OccurredAt, then Sequence. Dropping the OrderBy returns these in
        // sequence order (1, 2, 3, 4), which this expectation is shaped to reject.
        //
        // The ThenBy is pinned here but is not independently falsifiable against this schema: the
        // index that answers the Where already delivers rows in sequence order, so the two that
        // share an instant arrive pre-sorted and a read without the ThenBy would agree with this
        // expectation by accident. It is asserted because the contract is the pair, not because
        // this test can break it.
        Assert.Equal(
            [
                (Created, 1),
                (Created.AddMinutes(5), 3),
                (Created.AddMinutes(5), 4),
                (Created.AddMinutes(10), 2),
            ],
            forTarget.Select(revision => (revision.OccurredAt, revision.Sequence)));
    }

    // ---------------------------------------------------------------------------------------
    // Repeatability
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_second_run_on_the_same_meeting_inserts_and_leaves_the_first_alone()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(nameof(A_second_run_on_the_same_meeting_inserts_and_leaves_the_first_alone), cancellationToken);

        Meeting meeting = await SavedMeetingAsync(services, "Weekly sync", cancellationToken);

        ExtractionRun first = RunFor(meeting, ExtractionOutcome.Succeeded);
        IReadOnlyList<ActionRevision> firstRevisions = first.AddProposals(Drafts, Created);
        await SaveRunAsync(services, first, cancellationToken, firstRevisions);

        Guid[] firstProposalIds = [.. first.Proposals.Select(proposal => proposal.Id).Order()];

        ExtractionRun second = RunFor(meeting, ExtractionOutcome.Succeeded);
        IReadOnlyList<ActionRevision> secondRevisions = second.AddProposals(Drafts, Created.AddMinutes(5));
        await SaveRunAsync(services, second, cancellationToken, secondRevisions);

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        AppDbContext context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // AD-5 — any number of runs per Meeting.
        Assert.Equal(2, await context.ExtractionRuns.CountAsync(cancellationToken));
        Assert.Equal(Drafts.Length * 2, await context.Set<ProposedAction>().CountAsync(cancellationToken));
        Assert.Equal(Drafts.Length * 2, await context.ActionRevisions.CountAsync(cancellationToken));

        // And the first run's own rows are untouched: same ids, same ordinals, same instant.
        ExtractionRun storedFirst = await LoadRunAsync(services, first.Id, cancellationToken);

        Assert.Equal(firstProposalIds, storedFirst.Proposals.Select(proposal => proposal.Id).Order());
        Assert.Equal([0, 1], storedFirst.Proposals.OrderBy(proposal => proposal.Ordinal).Select(proposal => proposal.Ordinal));

        IReadOnlyList<ActionRevision> firstTargetRevisions = await scope.ServiceProvider
            .GetRequiredService<IActionRevisionRepository>()
            .ListForTargetAsync(RevisionTargetType.ProposedAction, firstProposalIds[0], cancellationToken);

        Assert.Equal(Created, Assert.Single(firstTargetRevisions).OccurredAt);
    }

    // ---------------------------------------------------------------------------------------
    // The reads
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task The_run_detail_query_translates_to_sql_and_carries_the_derived_values()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(nameof(The_run_detail_query_translates_to_sql_and_carries_the_derived_values), cancellationToken);

        Meeting meeting = await SavedMeetingAsync(services, "Weekly sync", cancellationToken);
        User dana = await SavedUserAsync(services, "dana", "Dana Whitfield", cancellationToken);

        ExtractionRun run = RunFor(meeting, ExtractionOutcome.Succeeded);
        IReadOnlyList<ActionRevision> minted = run.AddProposals(Drafts, Created);
        await SaveRunAsync(services, run, cancellationToken, minted);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        RunDetailDto detail = await QueriesIn(scope).GetAsync(run.Id, cancellationToken);

        Assert.Equal(run.Id, detail.Id);
        Assert.Equal(meeting.Id, detail.MeetingId);
        Assert.Equal(0, detail.InputTokens);
        Assert.Empty(detail.Warnings);

        // The Application-ring fake is LINQ to Objects, so the ordering and the projection are
        // only proven to be a *query* there. This is what proves the provider can translate them.
        Assert.Equal([0, 1], detail.Proposals.Select(proposal => proposal.Ordinal));

        Assert.Equal(dana.Id, detail.Proposals[0].SuggestedOwnerUserId);
        Assert.False(detail.Proposals[0].IsLowConfidence);

        // 0.62 against the default 0.70 the test host hands the read model.
        Assert.Null(detail.Proposals[1].SuggestedOwnerUserId);
        Assert.True(detail.Proposals[1].IsLowConfidence);
    }

    [Fact]
    public async Task The_run_detail_query_answers_an_unknown_id_with_a_not_found()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(nameof(The_run_detail_query_answers_an_unknown_id_with_a_not_found), cancellationToken);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            QueriesIn(scope).GetAsync(Guid.CreateVersion7(), cancellationToken));
    }

    [Fact]
    public async Task The_meeting_list_run_count_translates_to_sql_and_counts_only_this_meetings_runs()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(nameof(The_meeting_list_run_count_translates_to_sql_and_counts_only_this_meetings_runs), cancellationToken);

        Meeting busy = await SavedMeetingAsync(services, "Weekly sync", cancellationToken);
        Meeting quiet = await SavedMeetingAsync(services, "Solo review", cancellationToken);

        await SaveRunAsync(services, RunFor(busy, ExtractionOutcome.Succeeded), cancellationToken);
        await SaveRunAsync(services, RunFor(busy, ExtractionOutcome.Failed, "Both attempts failed validation."), cancellationToken);
        await SaveRunAsync(services, RunFor(quiet, ExtractionOutcome.Succeeded), cancellationToken);

        Meeting withNoRuns = Meeting.Create("Quiet meeting", Held.AddDays(-1), null, Guid.CreateVersion7(), Created);
        await SaveMeetingAsync(services, withNoRuns, cancellationToken);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        MeetingsQueries meetings = new(scope.ServiceProvider.GetRequiredService<IReadDb>());

        IReadOnlyList<MeetingSummaryDto> items = (await meetings.ListAsync(null, null, cancellationToken)).Items;

        // A correlated subquery, so the Meeting with no runs is still in the page.
        Assert.Equal(2, items.Single(meeting => meeting.Id == busy.Id).RunCount);
        Assert.Equal(1, items.Single(meeting => meeting.Id == quiet.Id).RunCount);
        Assert.Equal(0, items.Single(meeting => meeting.Id == withNoRuns.Id).RunCount);

        // Story 3.1's literal is still a literal.
        Assert.All(items, meeting => Assert.Equal(0, meeting.TrackedActionCount));
    }

    [Fact]
    public async Task The_run_list_query_translates_to_sql_with_both_counts_newest_first()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(nameof(The_run_list_query_translates_to_sql_with_both_counts_newest_first), cancellationToken);

        Meeting meeting = await SavedMeetingAsync(services, "Weekly sync", cancellationToken);
        Meeting other = await SavedMeetingAsync(services, "Solo review", cancellationToken);

        ExtractionRun succeeded = ExtractionRun.Start(
            meeting.Id, meeting.Notes!.Id, meeting.Notes.Sha256, Guid.CreateVersion7(),
            Metrics with { StartedAt = StartedAt.AddMinutes(5) },
            ExtractionOutcome.Succeeded, null, warnings: null);
        IReadOnlyList<ActionRevision> minted = succeeded.AddProposals(Drafts, Created);

        await SaveRunAsync(services, succeeded, cancellationToken, minted);
        await SaveRunAsync(services, RunFor(meeting, ExtractionOutcome.Failed, "Both attempts failed validation."), cancellationToken);
        await SaveRunAsync(services, RunFor(other, ExtractionOutcome.Succeeded), cancellationToken);

        // Nothing can decide a proposal before Story 3.2, so one is decided by raw SQL. Without it
        // every fixture is all-Pending and PendingCount could not be told apart from ProposalCount.
        await using (NpgsqlConnection connection = new(await ConnectionStringAsync(services)))
        {
            await connection.OpenAsync(cancellationToken);

            await using NpgsqlCommand decide = new(
                "UPDATE proposed_actions SET review_state = 'Approved' WHERE id = @id",
                connection);

            decide.Parameters.AddWithValue("id", succeeded.Proposals[0].Id);

            Assert.Equal(1, await decide.ExecuteNonQueryAsync(cancellationToken));
        }

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        IReadOnlyList<RunSummaryDto> runs = await QueriesIn(scope).ListForMeetingAsync(meeting.Id, cancellationToken);

        // The later-started run first, although it was inserted first; the other meeting's run absent.
        Assert.Equal(2, runs.Count);
        Assert.Equal(succeeded.Id, runs[0].Id);

        // Correlated subqueries, so the failed run with no proposals is still listed, with 0. The
        // decided proposal still counts toward the total but not toward Pending.
        Assert.Equal((Drafts.Length, Drafts.Length - 1), (runs[0].ProposalCount, runs[0].PendingCount));
        Assert.Equal((0, 0), (runs[1].ProposalCount, runs[1].PendingCount));
        Assert.Equal(ExtractionOutcome.Failed, runs[1].Outcome);
        Assert.Equal("Both attempts failed validation.", runs[1].FailureReason);
    }

    [Fact]
    public async Task The_run_list_query_answers_an_unknown_meeting_with_a_not_found()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(nameof(The_run_list_query_answers_an_unknown_meeting_with_a_not_found), cancellationToken);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            QueriesIn(scope).ListForMeetingAsync(Guid.CreateVersion7(), cancellationToken));
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The query class over the real read seam, with the 0.70 default threshold the shipped
    /// configuration carries. What is under test is the translation, not the registration.
    /// </summary>
    private static RunsQueries QueriesIn(AsyncServiceScope scope)
    {
        IReadDb readDb = scope.ServiceProvider.GetRequiredService<IReadDb>();

        return new RunsQueries(readDb, new ProposedActionReadModel(readDb, new DefaultThreshold()));
    }

    private sealed class DefaultThreshold : IExtractionSettings
    {
        public double LowConfidenceThreshold => 0.70;
    }

    /// <summary>
    /// Appends audit rows by raw SQL. The aggregate mints exactly one revision per proposal in
    /// this story, so a later story's rows are the only way to exercise the read's ordering — and
    /// writing them through the aggregate is not possible before Story 3.2 exists.
    /// </summary>
    private static async Task InsertRevisionsAsync(
        IServiceProvider services,
        CancellationToken cancellationToken,
        params (Guid TargetId, int Sequence, DateTimeOffset OccurredAt)[] rows)
    {
        await using NpgsqlConnection connection = new(await ConnectionStringAsync(services));
        await connection.OpenAsync(cancellationToken);

        foreach ((Guid targetId, int sequence, DateTimeOffset occurredAt) in rows)
        {
            await using NpgsqlCommand insert = new(
                """
                INSERT INTO action_revisions
                  (id, target_type, target_id, sequence, kind, field, old_value, new_value, actor_user_id, occurred_at)
                VALUES (@id, 'ProposedAction', @target, @sequence, 'AiProposal', NULL, NULL, '{}', NULL, @occurred)
                """,
                connection);

            insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
            insert.Parameters.AddWithValue("target", targetId);
            insert.Parameters.AddWithValue("sequence", sequence);
            insert.Parameters.AddWithValue("occurred", occurredAt);

            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// The host's own connection string, read through a scope: <c>AppDbContext</c> is scoped, and
    /// resolving it from the root provider throws.
    /// </summary>
    private static async Task<string> ConnectionStringAsync(IServiceProvider services)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        return scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString()!;
    }

    private static ExtractionRun RunFor(Meeting meeting, ExtractionOutcome outcome, string? failureReason = null) =>
        ExtractionRun.Start(
            meeting.Id,
            meeting.Notes!.Id,
            meeting.Notes.Sha256,
            Guid.CreateVersion7(),
            Metrics,
            outcome,
            failureReason,
            warnings: null);

    private static async Task<Meeting> SavedMeetingAsync(
        IServiceProvider services,
        string title,
        CancellationToken cancellationToken)
    {
        Meeting meeting = Meeting.Create(title, Held, ["Dana Whitfield"], Guid.CreateVersion7(), Created);
        meeting.AttachNotes("Dana will order the scanners. Someone should book the range.", Created);

        await SaveMeetingAsync(services, meeting, cancellationToken);

        return meeting;
    }

    private static async Task SaveMeetingAsync(IServiceProvider services, Meeting meeting, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IMeetingRepository>().Add(meeting);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
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

    private static async Task SaveRunAsync(
        IServiceProvider services,
        ExtractionRun run,
        CancellationToken cancellationToken,
        IReadOnlyList<ActionRevision>? revisions = null)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IExtractionRunRepository>().Add(run);

        if (revisions is { Count: > 0 })
        {
            scope.ServiceProvider.GetRequiredService<IActionRevisionRepository>().AddRange(revisions);
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
    }

    private static async Task<ExtractionRun> LoadRunAsync(IServiceProvider services, Guid id, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        ExtractionRun? run = await scope.ServiceProvider
            .GetRequiredService<IExtractionRunRepository>()
            .FindByIdAsync(id, cancellationToken);

        Assert.NotNull(run);

        return run;
    }

    private async Task<string> MigratedDatabaseAsync(string name, CancellationToken cancellationToken)
    {
        string connectionString = await postgres.CreateDatabaseAsync(name, cancellationToken);

        await using ServiceProvider services = TestHost.Build(connectionString, TestHost.SeedingOff);
        await TestHost.MigrateAsync(services, cancellationToken);

        return connectionString;
    }

    private async Task<ServiceProvider> MigratedHostAsync(
        string name,
        CancellationToken cancellationToken,
        Action<IServiceCollection>? configure = null)
    {
        string connectionString = await postgres.CreateDatabaseAsync(name, cancellationToken);

        ServiceProvider services = TestHost.Build(connectionString, TestHost.SeedingOff, configure);

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

    private static async Task<Dictionary<string, string>> ColumnsAsync(
        string connectionString,
        string table,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> columns = new(StringComparer.Ordinal);

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand command = new(
            """
            SELECT column_name, data_type
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = @table
            """,
            connection);

        command.Parameters.AddWithValue("table", table);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            columns[reader.GetString(0)] = reader.GetString(1);
        }

        return columns;
    }
}
