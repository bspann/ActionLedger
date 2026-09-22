using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Ai;
using ActionLedger.Application.Extraction;
using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Common;
using ActionLedger.Domain.Extraction;
using ActionLedger.Domain.Meetings;
using ActionLedger.Domain.Users;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ActionLedger.Application.Tests.Extraction;

/// <summary>
/// AD-11, AD-20 and NFR-4 at the use-case level: the run is persisted whatever the outcome, the
/// whole write is one commit, the revisions the aggregate minted reach their own repository, and
/// the completion event names the run without naming what was in the notes.
/// </summary>
/// <remarks>
/// AD-18 — the ports are in-memory fakes here; <c>ExtractionPersistenceTests</c> proves the same
/// rows against a real database.
/// </remarks>
public sealed class RunExtractionHandlerTests
{
    private static readonly DateOnly Held = new(2026, 9, 22);

    private static readonly DateTimeOffset Now = new(2026, 9, 22, 14, 30, 0, TimeSpan.Zero);

    /// <summary>Deliberately hours away from <see cref="Now"/>, so the two cannot be confused.</summary>
    private static readonly DateTimeOffset ProviderStartedAt = new(2026, 9, 22, 9, 15, 42, TimeSpan.Zero);

    private static readonly Guid Actor = Guid.CreateVersion7();

    private const string Notes = "Dana will order the replacement scanners by Friday.\r\nPriya should book the range.";

    private static readonly ExtractionMetrics Metrics = new(
        "Fake",
        "fixture-catalog",
        "v1",
        "1",
        ProviderStartedAt,
        DurationMs: 87,
        InputTokens: 0,
        OutputTokens: 0);

    private static readonly ExtractedProposal[] Kept =
    [
        new("Order the replacement scanners.", "Dana Whitfield", new DateOnly(2026, 9, 25), 0.91, "Dana will order the replacement scanners by Friday."),
        new("Book the range.", "Priya Raman", null, 0.62, "Priya should book the range."),
    ];

    // ---------------------------------------------------------------------------------------
    // The succeeded run
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_succeeded_result_persists_the_run_its_proposals_and_one_revision_each()
    {
        Harness harness = Harness.WithNotes(ExtractionResult.Succeeded(Kept, [], Metrics, []));

        RunDto answer = await harness.RunAsync();

        ExtractionRun run = Assert.Single(harness.Runs.Added);

        Assert.Equal(run.Id, answer.Id);
        Assert.Equal(ExtractionOutcome.Succeeded, answer.Outcome);
        Assert.Equal(ExtractionOutcome.Succeeded, run.Outcome);
        Assert.Null(run.FailureReason);

        Assert.Equal(Kept.Length, run.Proposals.Count);
        Assert.Equal([0, 1], run.Proposals.Select(proposal => proposal.Ordinal));
        Assert.Equal(
            [.. Kept.Select(proposal => proposal.Description)],
            run.Proposals.Select(proposal => proposal.Description));

        Assert.Equal(Kept.Length, harness.Revisions.Added.Count);
        Assert.All(harness.Revisions.Added, revision => Assert.Equal(RevisionKind.AiProposal, revision.Kind));
        Assert.Equal(
            [.. run.Proposals.Select(proposal => proposal.Id)],
            harness.Revisions.Added.Select(revision => revision.TargetId));
    }

    [Fact]
    public async Task The_whole_run_is_one_commit()
    {
        Harness harness = Harness.WithNotes(ExtractionResult.Succeeded(Kept, [], Metrics, []));

        await harness.RunAsync();

        // AD-20 — the run, its proposals, and its revisions are one unit of work. A second commit
        // would let a crash between them leave proposals with no audit rows.
        Assert.Equal(1, harness.Commits.Count);
    }

    [Fact]
    public async Task Everything_is_staged_before_the_commit_and_the_event_is_written_after_it()
    {
        Harness harness = Harness.WithNotes(ExtractionResult.Succeeded(Kept, [], Metrics, []));

        await harness.RunAsync();

        // Three orderings in one sequence, because counts can see none of them. The clock is read
        // after the provider returns, so the AD-7 instant stamped on every revision is never
        // earlier than the run's own StartedAt. Staging after the commit would write nothing at
        // all, and logging before it would announce a completed run that a failed commit left no
        // row for — NFR-4 counts persisted runs, not attempted ones.
        Assert.Equal(
            ["provider called", "clock read", "revisions added", "run added", "commit", "ExtractionRunCompleted logged"],
            harness.Journal.Entries);
    }

    [Fact]
    public async Task A_commit_that_throws_writes_no_completion_event()
    {
        Harness harness = Harness.WithNotes(ExtractionResult.Succeeded(Kept, [], Metrics, []));

        harness.Commits.Throws = new ConcurrencyConflictException("Another writer got there first.");

        await Assert.ThrowsAsync<ConcurrencyConflictException>(harness.RunAsync);

        // The run never reached a row, so nothing may claim it completed.
        Assert.Empty(harness.Logger.Lines);
    }

    [Fact]
    public async Task The_run_records_the_notes_identity_and_hash_it_read()
    {
        Harness harness = Harness.WithNotes(ExtractionResult.Succeeded(Kept, [], Metrics, []));

        await harness.RunAsync();

        ExtractionRun run = Assert.Single(harness.Runs.Added);
        MeetingNotes notes = harness.Meeting.Notes!;

        Assert.Equal(harness.Meeting.Id, run.MeetingId);
        Assert.Equal(notes.Id, run.MeetingNotesId);
        Assert.Equal(notes.Sha256, run.NotesSha256);
    }

    [Fact]
    public async Task The_actor_is_the_current_user_and_nothing_else()
    {
        Harness harness = Harness.WithNotes(ExtractionResult.Succeeded(Kept, [], Metrics, []));

        await harness.RunAsync();

        // AD-12 — there is no command and no body on this route, so there is nowhere else it
        // could have come from; this is what proves it came from the token's subject.
        Assert.Equal(Actor, Assert.Single(harness.Runs.Added).StartedByUserId);
    }

    [Fact]
    public async Task The_extractor_receives_exactly_the_notes_text_and_the_meeting_date()
    {
        Harness harness = Harness.WithNotes(ExtractionResult.Succeeded(Kept, [], Metrics, []));

        await harness.RunAsync();

        ExtractionRequest request = Assert.Single(harness.Extractor.Requests);

        // FR-4 — the notes byte-for-byte and the date. Not the title, not the attendees, not the id.
        Assert.Equal(Notes, request.Notes);
        Assert.Equal(Held, request.MeetingDate);
    }

    [Fact]
    public async Task The_timings_come_from_the_metrics_and_not_from_the_clock()
    {
        Harness harness = Harness.WithNotes(ExtractionResult.Succeeded(Kept, [], Metrics, []));

        await harness.RunAsync();

        ExtractionRun run = Assert.Single(harness.Runs.Added);

        // AD-6 — the extractor is the only code that sees both ends of the provider call.
        Assert.Equal(ProviderStartedAt, run.StartedAt);
        Assert.NotEqual(Now, run.StartedAt);
        Assert.Equal(87, run.DurationMs);
        Assert.Equal(0, run.InputTokens);
        Assert.Equal(0, run.OutputTokens);
        Assert.Equal("Fake", run.Provider);
        Assert.Equal("fixture-catalog", run.Model);
        Assert.Equal("v1", run.PromptVersion);
        Assert.Equal("1", run.SchemaVersion);
    }

    [Fact]
    public async Task Every_revision_carries_the_clocks_one_instant()
    {
        Harness harness = Harness.WithNotes(ExtractionResult.Succeeded(Kept, [], Metrics, []));

        await harness.RunAsync();

        // AD-7's shared `now` is IClock's, read once — not the provider's StartedAt, which is
        // hours earlier here precisely so the two cannot be confused.
        Assert.All(harness.Revisions.Added, revision => Assert.Equal(Now, revision.OccurredAt));
        Assert.Equal(1, harness.Clock.Reads);
    }

    /// <summary>
    /// AD-7's instant records when the proposals were recorded, and they do not exist until the
    /// extractor returns. Read before the provider call it would sit earlier than the run's own
    /// StartedAt — by up to the whole NFR-1 ceiling once a real provider is wired — and the audit
    /// trail would say the proposals were recorded before the run began.
    /// </summary>
    [Fact]
    public async Task The_shared_revision_instant_is_read_after_the_provider_answers()
    {
        Harness harness = Harness.WithNotes(ExtractionResult.Succeeded(Kept, [], Metrics, []));

        await harness.RunAsync();

        Assert.Equal(
            ["provider called", "clock read"],
            harness.Journal.Entries.Where(entry => entry is "provider called" or "clock read"));
    }

    // ---------------------------------------------------------------------------------------
    // The failed run
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_failed_result_is_still_a_persisted_run_with_no_proposals_and_no_revisions()
    {
        const string Reason = "The provider did not answer within Ai:CallTimeoutSeconds.";

        Harness harness = Harness.WithNotes(ExtractionResult.Failed(Reason, Metrics));

        RunDto answer = await harness.RunAsync();

        ExtractionRun run = Assert.Single(harness.Runs.Added);

        Assert.Equal(ExtractionOutcome.Failed, answer.Outcome);
        Assert.Equal(ExtractionOutcome.Failed, run.Outcome);

        // Verbatim: the UI shows it to a human beside "Run again".
        Assert.Equal(Reason, run.FailureReason);

        Assert.Empty(run.Proposals);
        Assert.Empty(harness.Revisions.Added);

        // AD-11 — a failed extraction is not an HTTP error, so it is still one ordinary commit.
        Assert.Equal(1, harness.Commits.Count);
    }

    [Fact]
    public async Task A_failed_run_still_records_every_AD6_metric()
    {
        Harness harness = Harness.WithNotes(ExtractionResult.Failed("Both attempts failed validation.", Metrics));

        await harness.RunAsync();

        ExtractionRun run = Assert.Single(harness.Runs.Added);

        Assert.Equal("Fake", run.Provider);
        Assert.Equal("v1", run.PromptVersion);
        Assert.Equal(ProviderStartedAt, run.StartedAt);
        Assert.Equal(87, run.DurationMs);
    }

    // ---------------------------------------------------------------------------------------
    // Dropped proposals
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Dropped_proposals_do_not_become_rows_but_their_warnings_are_persisted()
    {
        ExtractedProposal unverifiable = new(
            "Resurface the lot.",
            "Marcus Bell",
            null,
            0.8,
            "Marcus will resurface the lot.");

        const string Warning = "Dropped a proposal: its source excerpt was not found in the notes. Excerpt: 'Marcus will resurface the lot.'";

        Harness harness = Harness.WithNotes(ExtractionResult.Succeeded(
            Kept,
            [new DroppedProposal(unverifiable, Warning)],
            Metrics,
            [Warning]));

        await harness.RunAsync();

        ExtractionRun run = Assert.Single(harness.Runs.Added);

        // Only Kept becomes rows. A dropped proposal survives as its warning and nothing else.
        Assert.Equal(Kept.Length, run.Proposals.Count);
        Assert.DoesNotContain(run.Proposals, proposal => proposal.Description == "Resurface the lot.");
        Assert.Equal(Kept.Length, harness.Revisions.Added.Count);

        Assert.Equal([Warning], run.Warnings);
    }

    [Fact]
    public async Task A_clean_run_publishes_warnings_as_an_empty_list_rather_than_null()
    {
        Harness harness = Harness.WithNotes(ExtractionResult.Succeeded(Kept, [], Metrics, []));

        await harness.RunAsync();

        IReadOnlyList<string> warnings = Assert.Single(harness.Runs.Added).Warnings;

        Assert.NotNull(warnings);
        Assert.Empty(warnings);
    }

    [Fact]
    public async Task A_succeeded_run_that_kept_nothing_persists_with_no_proposals_and_no_revisions()
    {
        Harness harness = Harness.WithNotes(ExtractionResult.Succeeded([], [], Metrics, []));

        RunDto answer = await harness.RunAsync();

        Assert.Equal(ExtractionOutcome.Succeeded, answer.Outcome);
        Assert.Empty(Assert.Single(harness.Runs.Added).Proposals);
        Assert.Empty(harness.Revisions.Added);
        Assert.Equal(1, harness.Commits.Count);
    }

    /// <summary>
    /// The handler calls <c>AddProposals</c> for an empty Kept too. Skipping it would leave the
    /// aggregate's once-only flag unset — the exact state the flag exists to tell apart from
    /// "nothing has been added yet" — so the run it commits would still accept a second set.
    /// </summary>
    [Fact]
    public async Task A_succeeded_run_that_kept_nothing_has_still_had_its_proposals_added()
    {
        Harness harness = Harness.WithNotes(ExtractionResult.Succeeded([], [], Metrics, []));

        await harness.RunAsync();

        ExtractionRun run = Assert.Single(harness.Runs.Added);

        Assert.Throws<DomainRuleException>(() => run.AddProposals([], Now));
    }

    // ---------------------------------------------------------------------------------------
    // The refusals
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unknown_meeting_is_a_not_found_and_never_reaches_the_extractor()
    {
        Harness harness = Harness.WithoutMeeting();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            harness.Handler.HandleAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken));

        Assert.Empty(harness.Extractor.Requests);
        Assert.Equal(0, harness.Commits.Count);
        Assert.Empty(harness.Runs.Added);
    }

    [Fact]
    public async Task A_meeting_with_no_notes_is_a_validation_failure_and_never_reaches_the_extractor()
    {
        Harness harness = Harness.WithoutNotes();

        ValidationFailedException refused = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            harness.RunAsync());

        Assert.Contains("no notes", refused.Message, StringComparison.OrdinalIgnoreCase);

        // FR-4 — there is nothing to extract from, so no provider call is spent finding that out.
        Assert.Empty(harness.Extractor.Requests);
        Assert.Equal(0, harness.Commits.Count);
        Assert.Empty(harness.Runs.Added);
        Assert.Empty(harness.Revisions.Added);
    }

    [Fact]
    public async Task A_second_run_on_the_same_meeting_is_an_ordinary_new_run()
    {
        Harness harness = Harness.WithNotes(ExtractionResult.Succeeded(Kept, [], Metrics, []));

        RunDto first = await harness.RunAsync();
        RunDto second = await harness.RunAsync();

        // AD-5 — any number of runs per Meeting, each with its own proposals.
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, harness.Runs.Added.Count);
        Assert.Equal(Kept.Length * 2, harness.Revisions.Added.Count);
        Assert.Equal(2, harness.Commits.Count);

        // And the two runs' proposals are distinct rows, not the same ones counted twice.
        Assert.Equal(
            Kept.Length * 2,
            harness.Runs.Added.SelectMany(run => run.Proposals).Select(proposal => proposal.Id).Distinct().Count());
    }

    // ---------------------------------------------------------------------------------------
    // The completion event (NFR-4)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task One_ExtractionRunCompleted_event_is_written_per_completed_run()
    {
        Harness harness = Harness.WithNotes(ExtractionResult.Succeeded(Kept, [], Metrics, []));

        await harness.RunAsync();

        string line = Assert.Single(harness.Logger.Lines);

        Assert.Contains("ExtractionRunCompleted", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_completion_event_carries_provider_model_prompt_duration_tokens_and_outcome()
    {
        Harness harness = Harness.WithNotes(ExtractionResult.Succeeded(Kept, [], Metrics, []));

        await harness.RunAsync();

        ExtractionRun run = Assert.Single(harness.Runs.Added);
        string line = Assert.Single(harness.Logger.Lines);

        // The two token counts are asserted as rendered *pairs*, not as a bare "0". Both are 0 for
        // the Fake, so a lone "0" is satisfied by either one alone — and by the hex of the two
        // UUIDs on the line besides — which left the template free to lose a count and stay green.
        foreach (string expected in new[]
        {
            "Fake",
            "fixture-catalog",
            "v1",
            "87",
            "inputTokens 0",
            "outputTokens 0",
            "Succeeded",
            run.Id.ToString(),
        })
        {
            Assert.Contains(expected, line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_failed_run_also_writes_exactly_one_event_naming_its_outcome()
    {
        Harness harness = Harness.WithNotes(ExtractionResult.Failed("Both attempts failed validation.", Metrics));

        await harness.RunAsync();

        Assert.Contains("Failed", Assert.Single(harness.Logger.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_notes_text_description_or_excerpt_appears_anywhere_in_the_event()
    {
        ExtractedProposal unverifiable = new("Resurface the lot.", "Marcus Bell", null, 0.8, "Marcus will resurface the lot.");
        const string Warning = "Dropped a proposal: its source excerpt was not found. Excerpt: 'Marcus will resurface the lot.'";

        Harness harness = Harness.WithNotes(ExtractionResult.Succeeded(
            Kept,
            [new DroppedProposal(unverifiable, Warning)],
            Metrics,
            [Warning]));

        await harness.RunAsync();

        string line = Assert.Single(harness.Logger.Lines);

        // NFR-4 — never log notes text or secrets. The excerpt is notes text quoted back, the
        // description is derived from it, and a warning carries an excerpt verbatim.
        string[] forbidden =
        [
            Notes,
            "Dana will order the replacement scanners by Friday.",
            "Order the replacement scanners.",
            "Priya should book the range.",
            "Marcus will resurface the lot.",
            Warning,
        ];

        foreach (string secret in forbidden)
        {
            Assert.DoesNotContain(secret, line, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The handler with every port faked. It holds the Meeting so a test can assert against the
    /// notes the handler actually read rather than against a copy the test built.
    /// </summary>
    private sealed class Harness
    {
        private Harness(Meeting? meeting, ExtractionResult result)
        {
            Meeting = meeting ?? Meeting.Create("Weekly sync", Held, null, Actor, Now);

            FakeMeetingRepository meetings = meeting is null ? new() : new(meeting);

            Extractor = new FakeExtractor(result, Journal);
            Clock = new CountingClock(Now, Journal);
            Commits = new CountingUnitOfWork(Journal);
            Runs = new RecordingRunRepository(Journal);
            Revisions = new RecordingRevisionRepository(Journal);
            Logger = new CapturingLogger(Journal);

            Handler = new RunExtractionHandler(
                meetings,
                Runs,
                Revisions,
                Extractor,
                new FakeCurrentUser(Actor),
                Clock,
                Commits,
                Logger);
        }

        public Meeting Meeting { get; }

        public RunExtractionHandler Handler { get; }

        public FakeExtractor Extractor { get; }

        public RecordingRunRepository Runs { get; }

        public RecordingRevisionRepository Revisions { get; }

        public Journal Journal { get; } = new();

        public CountingUnitOfWork Commits { get; }

        public CountingClock Clock { get; }

        public CapturingLogger Logger { get; }

        public static Harness WithNotes(ExtractionResult result)
        {
            Meeting meeting = Meeting.Create("Weekly sync", Held, null, Actor, Now);
            meeting.AttachNotes(Notes, Now);

            return new Harness(meeting, result);
        }

        public static Harness WithoutNotes() =>
            new(Meeting.Create("Weekly sync", Held, null, Actor, Now), ExtractionResult.Succeeded([], [], Metrics, []));

        public static Harness WithoutMeeting() =>
            new(meeting: null, ExtractionResult.Succeeded([], [], Metrics, []));

        public Task<RunDto> RunAsync() => Handler.HandleAsync(Meeting.Id, TestContext.Current.CancellationToken);
    }

    /// <summary>The write seam over a list. It adds and loads; it never saves (AD-10).</summary>
    private sealed class FakeMeetingRepository(params Meeting[] existing) : IMeetingRepository
    {
        private readonly List<Meeting> _meetings = [.. existing];

        public void Add(Meeting meeting) => _meetings.Add(meeting);

        public Task<Meeting?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_meetings.FirstOrDefault(meeting => meeting.Id == id));
    }

    /// <summary>AD-11 — the seam, scripted. It never throws for a failed run; it answers with one.</summary>
    private sealed class FakeExtractor(ExtractionResult result, Journal journal) : IActionExtractor
    {
        private readonly List<ExtractionRequest> _requests = [];

        /// <summary>Every request the handler sent, so a test can assert what the provider saw.</summary>
        public IReadOnlyList<ExtractionRequest> Requests => _requests;

        public Task<ExtractionResult> ExtractAsync(ExtractionRequest request, CancellationToken cancellationToken = default)
        {
            _requests.Add(request);
            journal.Record("provider called");

            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// What happened, in the order it happened. Counts alone cannot tell "staged, then committed"
    /// from "committed, then staged" — and the second writes nothing.
    /// </summary>
    private sealed class Journal
    {
        private readonly List<string> _entries = [];

        public IReadOnlyList<string> Entries => _entries;

        public void Record(string what) => _entries.Add(what);
    }

    private sealed class RecordingRunRepository(Journal journal) : IExtractionRunRepository
    {
        private readonly List<ExtractionRun> _added = [];

        /// <summary>What <see cref="Add"/> staged, so a test can assert what was built.</summary>
        public IReadOnlyList<ExtractionRun> Added => _added;

        public void Add(ExtractionRun run)
        {
            _added.Add(run);
            journal.Record("run added");
        }

        public Task<ExtractionRun?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_added.FirstOrDefault(run => run.Id == id));

        public Task<ExtractionRun?> FindByProposedActionIdAsync(Guid proposedActionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_added.FirstOrDefault(run => run.Proposals.Any(proposal => proposal.Id == proposedActionId)));
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
            [
                .. _added
                    .Where(revision => revision.TargetType == targetType && revision.TargetId == targetId)
                    .OrderBy(revision => revision.OccurredAt)
                    .ThenBy(revision => revision.Sequence),
            ]);
    }

    /// <summary>AD-12 — the actor a token would have carried.</summary>
    private sealed class FakeCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId { get; } = userId;

        public string DisplayName => "Dana Whitfield";

        public Role Role => Role.ActionOfficer;
    }

    /// <summary>
    /// AD-15 — the only time source, fixed so a timestamp is assertable, and counted so "read once
    /// per request" is a claim a test can check rather than a comment.
    /// </summary>
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

    /// <summary>
    /// NFR-4 — captures the rendered log line. Rendered rather than structured on purpose: what
    /// NFR-4 forbids is notes text reaching a log, and a message template that interpolated the
    /// notes would leak them into exactly this string.
    /// </summary>
    private sealed class CapturingLogger(Journal journal) : ILogger<RunExtractionHandler>
    {
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines => _lines;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _lines.Add(formatter(state, exception));
            journal.Record("ExtractionRunCompleted logged");
        }
    }
}
