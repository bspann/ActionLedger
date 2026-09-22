using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Meetings;
using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Common;
using ActionLedger.Domain.Extraction;
using ActionLedger.Domain.Meetings;
using ActionLedger.Domain.Users;
using Xunit;

namespace ActionLedger.Application.Tests.Meetings;

/// <summary>
/// AD-2, AD-12, and AD-20 at the use-case level: handlers take the actor from
/// <see cref="ICurrentUser"/> and the time from <see cref="IClock"/>, commit exactly once, and
/// keep "not there" and "already has notes" as different answers. AD-18 — the ports are in-memory
/// fakes here; the Infrastructure suite proves the same rows against a real database.
/// </summary>
public sealed class MeetingsTests
{
    private static readonly DateOnly Held = new(2026, 9, 18);

    private static readonly DateTimeOffset Now = new(2026, 9, 18, 14, 30, 0, TimeSpan.Zero);

    private static readonly Guid Actor = Guid.CreateVersion7();

    // ---------------------------------------------------------------------------------------
    // CreateMeetingHandler
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Creating_a_meeting_attributes_it_to_the_token_not_to_the_request()
    {
        FakeMeetingRepository meetings = new();
        CountingUnitOfWork commits = new();

        CreateMeetingHandler handler = new(meetings, new FakeCurrentUser(Actor), new FakeClock(Now), commits);

        MeetingCreatedDto created = await handler.HandleAsync(
            new CreateMeetingCommand { Title = "Weekly sync", MeetingDate = Held, Attendees = ["Dana Whitfield"] },
            TestContext.Current.CancellationToken);

        Meeting stored = Assert.Single(meetings.Added);

        Assert.Equal(Actor, created.CreatedByUserId);
        Assert.Equal(Actor, stored.CreatedByUserId);
        Assert.Equal(stored.Id, created.Id);

        // The instant is the clock's, not DateTimeOffset.UtcNow (AD-15).
        Assert.Equal(Now, stored.CreatedAt);
        Assert.Equal(["Dana Whitfield"], stored.Attendees);
    }

    [Fact]
    public async Task Creating_a_meeting_commits_exactly_once()
    {
        CountingUnitOfWork commits = new();

        CreateMeetingHandler handler = new(
            new FakeMeetingRepository(),
            new FakeCurrentUser(Actor),
            new FakeClock(Now),
            commits);

        await handler.HandleAsync(
            new CreateMeetingCommand { Title = "Weekly sync", MeetingDate = Held },
            TestContext.Current.CancellationToken);

        Assert.Equal(1, commits.Count);
    }

    [Fact]
    public async Task A_command_with_no_date_is_refused_rather_than_dated_year_one()
    {
        // Model validation answers this with a 400 before the handler runs. The throw is what
        // keeps a direct call from quietly creating a meeting on 0001-01-01.
        CountingUnitOfWork commits = new();

        CreateMeetingHandler handler = new(
            new FakeMeetingRepository(),
            new FakeCurrentUser(Actor),
            new FakeClock(Now),
            commits);

        await Assert.ThrowsAsync<DomainRuleException>(() => handler.HandleAsync(
            new CreateMeetingCommand { Title = "Weekly sync", MeetingDate = null },
            TestContext.Current.CancellationToken));

        Assert.Equal(0, commits.Count);
    }

    // ---------------------------------------------------------------------------------------
    // SaveMeetingNotesHandler
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Saving_notes_stores_the_text_as_submitted_and_commits_once()
    {
        Meeting meeting = NewMeeting("Weekly sync");
        FakeMeetingRepository meetings = new(meeting);
        CountingUnitOfWork commits = new();

        SaveMeetingNotesHandler handler = new(meetings, new FakeClock(Now), commits);

        const string pasted = "  Dana will send the draft by Friday.\r\n";

        MeetingNotesDto notes = await handler.HandleAsync(
            meeting.Id,
            new SaveMeetingNotesCommand { Text = pasted },
            TestContext.Current.CancellationToken);

        Assert.Equal(pasted, notes.Text);
        Assert.Equal(Now, notes.SavedAt);
        Assert.Equal(meeting.Notes!.Sha256, notes.Sha256);
        Assert.Equal(1, commits.Count);
        Assert.True(meeting.HasNotes);
    }

    [Fact]
    public async Task Saving_notes_on_a_meeting_that_is_not_there_is_a_not_found()
    {
        CountingUnitOfWork commits = new();

        SaveMeetingNotesHandler handler = new(new FakeMeetingRepository(), new FakeClock(Now), commits);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.HandleAsync(
            Guid.CreateVersion7(),
            new SaveMeetingNotesCommand { Text = "Anything." },
            TestContext.Current.CancellationToken));

        // Nothing changed, so nothing is committed — 404 is not a write.
        Assert.Equal(0, commits.Count);
    }

    [Fact]
    public async Task Saving_notes_twice_is_a_conflict_and_leaves_the_first_text_alone()
    {
        Meeting meeting = NewMeeting("Weekly sync");
        FakeMeetingRepository meetings = new(meeting);
        CountingUnitOfWork commits = new();

        SaveMeetingNotesHandler handler = new(meetings, new FakeClock(Now), commits);

        await handler.HandleAsync(
            meeting.Id,
            new SaveMeetingNotesCommand { Text = "The first paste." },
            TestContext.Current.CancellationToken);

        // DomainRuleException is what the Api maps to 409 conflict (AD-13 Errors row).
        await Assert.ThrowsAsync<DomainRuleException>(() => handler.HandleAsync(
            meeting.Id,
            new SaveMeetingNotesCommand { Text = "A replacement." },
            TestContext.Current.CancellationToken));

        Assert.Equal("The first paste.", meeting.Notes!.Text);
        Assert.Equal(1, commits.Count);
    }

    // ---------------------------------------------------------------------------------------
    // MeetingsQueries.ListAsync
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task The_list_is_newest_meeting_first_then_newest_created_first()
    {
        Meeting older = NewMeeting("Older day", new DateOnly(2026, 9, 16), Now);
        Meeting earlierOnTheDay = NewMeeting("Earlier", new DateOnly(2026, 9, 18), Now.AddHours(-2));
        Meeting laterOnTheDay = NewMeeting("Later", new DateOnly(2026, 9, 18), Now);

        MeetingsQueries queries = Over(older, earlierOnTheDay, laterOnTheDay);

        PagedResult<MeetingSummaryDto> page = await queries.ListAsync(
            page: null,
            pageSize: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(["Later", "Earlier", "Older day"], page.Items.Select(meeting => meeting.Title));
    }

    [Fact]
    public async Task Two_meetings_sharing_a_day_and_an_instant_still_page_in_a_stable_order()
    {
        Meeting[] namesakes =
        [
            NewMeeting("One", Held, Now),
            NewMeeting("Two", Held, Now),
        ];

        MeetingsQueries queries = Over(namesakes);

        PagedResult<MeetingSummaryDto> first = await queries.ListAsync(1, 1, TestContext.Current.CancellationToken);
        PagedResult<MeetingSummaryDto> second = await queries.ListAsync(2, 1, TestContext.Current.CancellationToken);

        // The id tiebreak makes the order total, descending, so paging cannot repeat or skip a row.
        Guid[] expected = [.. namesakes.Select(meeting => meeting.Id).OrderDescending()];

        Guid[] paged = [first.Items.Single().Id, second.Items.Single().Id];

        Assert.Equal(expected, paged);
    }

    [Fact]
    public async Task A_meeting_with_no_runs_reports_a_run_count_of_zero()
    {
        MeetingsQueries queries = Over(NewMeeting("Weekly sync"));

        MeetingSummaryDto summary = Assert.Single(
            (await queries.ListAsync(null, null, TestContext.Current.CancellationToken)).Items);

        // A correlated count, not a join: a Meeting with no runs still appears, with 0.
        Assert.Equal(0, summary.RunCount);
        Assert.Equal(0, summary.TrackedActionCount);
    }

    [Fact]
    public async Task Both_counts_are_this_meetings_real_numbers()
    {
        Meeting busy = NewMeeting("Weekly sync");
        Meeting quiet = NewMeeting("Solo review", new DateOnly(2026, 9, 17), Now);

        ExtractionRun decided = RunFor(busy, ExtractionOutcome.Succeeded);
        ExtractionRun undecided = RunFor(quiet, ExtractionOutcome.Succeeded);

        decided.AddProposals([Draft("Order the scanners."), Draft("Book the range."), Draft("Brief the team.")], Now);
        undecided.AddProposals([Draft("Review the budget.")], Now);

        // Two approvals and a rejection on one Meeting: only the approvals are Tracked Actions.
        TrackedAction first = Approve(decided.Proposals[0]);
        TrackedAction second = Approve(decided.Proposals[1]);
        decided.Proposals[2].Decide(DecisionKind.Rejected, new DecisionEdits(null, null, null, null, null), Actor, Now);

        // Two runs on one Meeting and one on another, so a count that ignored the correlation —
        // or counted every run in the table — would answer 3 for both rows. The Tracked Actions
        // reach a Meeting only through proposal and run, so the proposals are rows too.
        MeetingsQueries queries = new(new FakeReadDb(
            [
                busy,
                quiet,
                decided,
                RunFor(busy, ExtractionOutcome.Failed),
                undecided,
                .. decided.Proposals,
                .. undecided.Proposals,
                first,
                second,
            ]));

        IReadOnlyList<MeetingSummaryDto> items =
            (await queries.ListAsync(null, null, TestContext.Current.CancellationToken)).Items;

        MeetingSummaryDto busySummary = items.Single(meeting => meeting.Id == busy.Id);
        MeetingSummaryDto quietSummary = items.Single(meeting => meeting.Id == quiet.Id);

        // A failed run is still a run: the Meeting List counts attempts, not successes.
        Assert.Equal(2, busySummary.RunCount);
        Assert.Equal(1, quietSummary.RunCount);

        // A correlated count through proposal and run: a count of every row in the table would
        // answer 2 for both, and the undecided proposal on the quiet Meeting is not an action.
        Assert.Equal(2, busySummary.TrackedActionCount);
        Assert.Equal(0, quietSummary.TrackedActionCount);
    }

    [Fact]
    public async Task A_window_outside_the_bounds_is_clamped_rather_than_refused()
    {
        MeetingsQueries queries = Over(NewMeeting("Weekly sync"));

        PagedResult<MeetingSummaryDto> page = await queries.ListAsync(
            page: 0,
            pageSize: 9999,
            TestContext.Current.CancellationToken);

        Assert.Equal(Paging.FirstPage, page.Page);
        Assert.Equal(Paging.MaxPageSize, page.PageSize);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task Total_counts_every_matching_row_not_the_length_of_the_page()
    {
        MeetingsQueries queries = Over(
            NewMeeting("One", new DateOnly(2026, 9, 18), Now),
            NewMeeting("Two", new DateOnly(2026, 9, 17), Now),
            NewMeeting("Three", new DateOnly(2026, 9, 16), Now));

        PagedResult<MeetingSummaryDto> page = await queries.ListAsync(2, 2, TestContext.Current.CancellationToken);

        Assert.Equal(["Three"], page.Items.Select(meeting => meeting.Title));
        Assert.Equal(3, page.Total);
    }

    [Fact]
    public async Task A_page_far_past_the_end_is_empty_and_still_reports_the_true_total()
    {
        MeetingsQueries queries = Over(NewMeeting("Weekly sync"));

        // (page - 1) * pageSize overflows int well before this, which would send PostgreSQL a
        // negative OFFSET on a read any authenticated caller can reach.
        PagedResult<MeetingSummaryDto> page = await queries.ListAsync(
            int.MaxValue,
            Paging.MaxPageSize,
            TestContext.Current.CancellationToken);

        Assert.Empty(page.Items);
        Assert.Equal(1, page.Total);
        Assert.Equal(int.MaxValue, page.Page);
        Assert.Equal(Paging.MaxPageSize, page.PageSize);
    }

    // ---------------------------------------------------------------------------------------
    // MeetingsQueries.GetAsync
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Reading_a_meeting_without_notes_reports_them_as_null_rather_than_as_missing()
    {
        Meeting meeting = NewMeeting("Weekly sync");

        MeetingDetailDto detail = await Over(meeting).GetAsync(meeting.Id, TestContext.Current.CancellationToken);

        Assert.Null(detail.Notes);
        Assert.Equal("Weekly sync", detail.Title);
        Assert.Equal(Actor, detail.CreatedByUserId);
        Assert.Equal(Held, detail.MeetingDate);
        Assert.Equal(["Dana Whitfield"], detail.Attendees);
    }

    [Fact]
    public async Task Reading_a_meeting_returns_the_notes_as_they_were_pasted()
    {
        Meeting meeting = NewMeeting("Weekly sync");
        const string pasted = "  Dana will send the draft.\r\n";

        MeetingNotes notes = meeting.AttachNotes(pasted, Now);

        MeetingDetailDto detail = await Over(meeting).GetAsync(meeting.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(detail.Notes);
        Assert.Equal(notes.Id, detail.Notes.Id);
        Assert.Equal(pasted, detail.Notes.Text);
        Assert.Equal(notes.Sha256, detail.Notes.Sha256);
        Assert.Equal(Now, detail.Notes.SavedAt);
    }

    [Fact]
    public async Task Reading_a_meeting_that_is_not_there_is_a_not_found() =>
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Over().GetAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken));

    // ---------------------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------------------

    private static MeetingsQueries Over(params Meeting[] meetings) => new(new FakeReadDb(meetings));

    private static Meeting NewMeeting(string title) => NewMeeting(title, Held, Now);

    private static Meeting NewMeeting(string title, DateOnly held, DateTimeOffset created) =>
        Meeting.Create(title, held, ["Dana Whitfield"], Actor, created);

    /// <summary>A run against a Meeting, with no notes needed: the list counts rows, not content.</summary>
    private static ExtractionRun RunFor(Meeting meeting, ExtractionOutcome outcome) =>
        ExtractionRun.Start(
            meeting.Id,
            Guid.CreateVersion7(),
            new string('a', 64),
            Actor,
            new ExtractionRunMetadata("Fake", "fixture-catalog", "v1", "1", Now, 12, 0, 0),
            outcome,
            outcome == ExtractionOutcome.Failed ? "Both attempts failed validation." : null,
            warnings: null);

    private static ProposedActionDraft Draft(string description) =>
        new(description, "Dana Whitfield", null, 0.9, description);

    /// <summary>Approves a proposal exactly as it stands, which is the only way to mint a Tracked Action.</summary>
    private static TrackedAction Approve(ProposedAction proposal) =>
        proposal.Decide(
            DecisionKind.Approved,
            new DecisionEdits(proposal.Description, null, proposal.SuggestedDueDate, null, null),
            Actor,
            Now).TrackedAction!;

    /// <summary>The write seam over a list. It adds and loads; it never saves (AD-10).</summary>
    private sealed class FakeMeetingRepository(params Meeting[] existing) : IMeetingRepository
    {
        private readonly List<Meeting> _meetings = [.. existing];

        private readonly List<Meeting> _added = [];

        /// <summary>What <see cref="Add"/> staged, so a test can assert what was built.</summary>
        public IReadOnlyList<Meeting> Added => _added;

        public void Add(Meeting meeting)
        {
            _meetings.Add(meeting);
            _added.Add(meeting);
        }

        public Task<Meeting?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_meetings.FirstOrDefault(meeting => meeting.Id == id));
    }

    /// <summary>AD-12 — the actor a token would have carried.</summary>
    private sealed class FakeCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId { get; } = userId;

        public string DisplayName => "Dana Whitfield";

        public Role Role => Role.ActionOfficer;
    }

    /// <summary>AD-15 — the only time source, fixed so a timestamp is assertable.</summary>
    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    /// <summary>AD-20 — one commit per use case, counted.</summary>
    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int Count { get; private set; }

        public Task<int> CommitAsync(CancellationToken cancellationToken = default)
        {
            Count++;

            return Task.FromResult(0);
        }
    }

    /// <summary>
    /// The read seam over a list. LINQ to Objects composes the same <c>OrderBy</c>/<c>Skip</c>/
    /// <c>Take</c>/<c>Select</c> the provider translates, so what is asserted here is the query
    /// the class builds; that it also translates to SQL is the Infrastructure suite's job.
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
