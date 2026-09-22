using ActionLedger.Web.Core;
using ActionLedger.Web.Core.Api;
using ActionLedger.Web.Features.Meetings.Data;
using Xunit;

namespace ActionLedger.Web.Tests;

/// <summary>
/// AD-14 and AD-18 — the Meetings feature's only door to the API. Every generated type stops
/// here: the four calls map their DTOs onto web-owned records, and every shape the client can
/// throw leaves as an <c>ApiFailure</c>.
/// </summary>
/// <remarks>
/// The <c>DateTimeOffset</c> ⇄ <see cref="DateOnly"/> conversion is asserted in both directions
/// because it happens in exactly one place and nothing above the seam could catch it going wrong.
/// </remarks>
public sealed class MeetingsServiceTests
{
    private static readonly Guid MeetingId = Guid.Parse("01999999-0000-7000-8000-00000000beef");

    [Fact]
    public async Task List_passes_the_page_window_through_to_the_generated_client()
    {
        StubApiClient client = new();
        MeetingsService service = new(client);

        await service.ListAsync(2, 50, TestContext.Current.CancellationToken);

        Assert.Equal(1, client.ListMeetingsCalls);
        Assert.Equal(2, client.LastMeetingsPage);
        Assert.Equal(50, client.LastMeetingsPageSize);
    }

    [Fact]
    public async Task List_maps_each_summary_onto_a_web_owned_row_in_the_servers_order()
    {
        StubApiClient client = new();
        client.MeetingPage = new PagedResultOfMeetingSummaryDto
        {
            Items =
            [
                Summary("Office move follow-up", new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero)),
                Summary("Equipment inventory", new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero), runs: 4, trackedActions: 9),
            ],
            Page = 2,
            PageSize = 50,
            Total = 73,
        };

        MeetingsService service = new(client);

        MeetingOutcome<MeetingPage> outcome = await service.ListAsync(2, 50, TestContext.Current.CancellationToken);

        Assert.Null(outcome.Failure);
        MeetingPage page = outcome.Value!;

        Assert.Equal(2, page.Page);
        Assert.Equal(50, page.PageSize);
        Assert.Equal(73, page.Total);

        // UX-DR12 — the server's order is the order; nothing here re-sorts a page.
        Assert.Equal(["Office move follow-up", "Equipment inventory"], page.Items.Select(item => item.Title));

        // The contract writes meetingDate as a date; the generator maps it to DateTimeOffset.
        Assert.Equal(new DateOnly(2026, 9, 21), page.Items[0].Date);
        Assert.Equal(new DateOnly(2026, 9, 14), page.Items[1].Date);

        // Whatever the server said, never recomputed here (they are 0 and 0 until Stories 2.5
        // and 3.1). The values differ so the two cannot be swapped without this failing.
        Assert.Equal(3, page.Items[0].RunCount);
        Assert.Equal(7, page.Items[0].TrackedActionCount);
        Assert.Equal(4, page.Items[1].RunCount);
        Assert.Equal(9, page.Items[1].TrackedActionCount);
    }

    [Fact]
    public async Task Create_sends_the_calendar_date_as_midnight_at_zero_offset()
    {
        StubApiClient client = new();
        client.MeetingCreated = new MeetingCreatedDto { Id = MeetingId, CreatedByUserId = Guid.Empty };

        MeetingsService service = new(client);

        MeetingOutcome<Guid> outcome = await service.CreateAsync(
            "Office move follow-up",
            new DateOnly(2026, 10, 3),
            ["Dana Whitfield", "Priya Ramaswamy"],
            TestContext.Current.CancellationToken);

        Assert.Null(outcome.Failure);
        Assert.Equal(MeetingId, outcome.Value);

        CreateMeetingCommand command = client.LastCreateMeetingCommand!;
        Assert.Equal("Office move follow-up", command.Title);
        Assert.Equal(["Dana Whitfield", "Priya Ramaswamy"], command.Attendees);

        // Midnight at zero offset. A local-time default would move the date across a day boundary
        // west of UTC, and the converter writes only the yyyy-MM-dd part, so nothing downstream
        // could notice.
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero), command.MeetingDate);
    }

    [Fact]
    public async Task Create_sends_an_empty_attendee_list_rather_than_omitting_it()
    {
        StubApiClient client = new();
        MeetingsService service = new(client);

        await service.CreateAsync("Standup", new DateOnly(2026, 10, 3), [], TestContext.Current.CancellationToken);

        Assert.NotNull(client.LastCreateMeetingCommand?.Attendees);
        Assert.Empty(client.LastCreateMeetingCommand.Attendees);
    }

    [Fact]
    public async Task Get_maps_a_meeting_without_notes_onto_a_null_notes_record()
    {
        StubApiClient client = new();
        client.Meeting = new MeetingDetailDto
        {
            Id = MeetingId,
            Title = "Office move follow-up",
            MeetingDate = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero),
            Attendees = ["Dana Whitfield"],
            CreatedByUserId = Guid.Empty,
            CreatedAt = new DateTimeOffset(2026, 9, 21, 9, 0, 0, TimeSpan.Zero),
            Notes = null,
        };

        MeetingsService service = new(client);

        MeetingOutcome<MeetingDetail> outcome = await service.GetAsync(MeetingId, TestContext.Current.CancellationToken);

        MeetingDetail detail = outcome.Value!;
        Assert.Equal(MeetingId, client.LastGetMeetingId);
        Assert.Equal("Office move follow-up", detail.Title);
        Assert.Equal(new DateOnly(2026, 9, 21), detail.Date);
        Assert.Equal(["Dana Whitfield"], detail.Attendees);
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 9, 0, 0, TimeSpan.Zero), detail.CreatedAt);

        // ADR-002 — a null here is the whole immutability branch on the detail page.
        Assert.Null(detail.Notes);
    }

    [Fact]
    public async Task Get_maps_attached_notes_onto_the_web_owned_notes_record()
    {
        StubApiClient client = new();
        client.Meeting.Notes = new MeetingNotesDto
        {
            Id = Guid.Parse("01999999-0000-7000-8000-0000000000aa"),
            Text = "  Dana will book the movers.\r\n",
            Sha256 = new string('b', 64),
            SavedAt = new DateTimeOffset(2026, 9, 21, 14, 3, 0, TimeSpan.Zero),
        };

        MeetingsService service = new(client);

        MeetingOutcome<MeetingDetail> outcome = await service.GetAsync(MeetingId, TestContext.Current.CancellationToken);

        // CreatedAt is the Meeting's, not the notes' — mapped from a different DTO field, and
        // the stub's default differs from SavedAt so the two cannot be crossed unnoticed.
        Assert.Equal(DateTimeOffset.UnixEpoch, outcome.Value!.CreatedAt);

        MeetingNotes notes = outcome.Value!.Notes!;
        Assert.Equal("  Dana will book the movers.\r\n", notes.Text);
        Assert.Equal(new string('b', 64), notes.Sha256);
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 14, 3, 0, TimeSpan.Zero), notes.SavedAt);
    }

    [Fact]
    public async Task Save_notes_sends_the_text_byte_for_byte()
    {
        // ADR-002 — the server stores what it is sent and hashes what it stored. Anything tidied
        // at the seam would silently become the record.
        const string Typed = "  Leading spaces.\r\n\tA tab.\n\nTrailing newline.\n";

        StubApiClient client = new();
        MeetingsService service = new(client);

        await service.SaveNotesAsync(MeetingId, Typed, TestContext.Current.CancellationToken);

        Assert.Equal(1, client.SaveMeetingNotesCalls);
        Assert.Equal(MeetingId, client.LastSaveMeetingNotesId);
        Assert.Equal(Typed, client.LastSaveMeetingNotesCommand?.Text);
    }

    [Fact]
    public async Task Save_notes_maps_the_saved_notes_back()
    {
        StubApiClient client = new();
        client.SavedNotes = new MeetingNotesDto
        {
            Id = Guid.Parse("01999999-0000-7000-8000-0000000000bb"),
            Text = "the notes",
            Sha256 = new string('c', 64),
            SavedAt = new DateTimeOffset(2026, 9, 21, 14, 3, 0, TimeSpan.Zero),
        };

        MeetingsService service = new(client);

        MeetingOutcome<MeetingNotes> outcome = await service.SaveNotesAsync(
            MeetingId, "the notes", TestContext.Current.CancellationToken);

        Assert.Null(outcome.Failure);
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 14, 3, 0, TimeSpan.Zero), outcome.Value!.SavedAt);
    }

    [Fact]
    public async Task A_problem_document_becomes_a_failure_carrying_its_status_and_title()
    {
        StubApiClient client = new()
        {
            GetMeetingThrows = StubApiClient.Problem(404, "The resource was not found."),
        };

        MeetingsService service = new(client);

        MeetingOutcome<MeetingDetail> outcome = await service.GetAsync(MeetingId, TestContext.Current.CancellationToken);

        Assert.Null(outcome.Value);
        Assert.Equal(404, outcome.Failure?.StatusCode);
        Assert.Equal("The resource was not found.", outcome.Failure?.Title);
    }

    [Fact]
    public async Task A_validation_problem_becomes_a_failure_rather_than_an_exception()
    {
        // ValidationProblemDetails is a separate flat class, not a subclass, so it is its own arm.
        StubApiClient client = new() { CreateMeetingThrows = StubApiClient.Invalid("The request was not valid.") };
        MeetingsService service = new(client);

        MeetingOutcome<Guid> outcome = await service.CreateAsync(
            "x", new DateOnly(2026, 10, 3), [], TestContext.Current.CancellationToken);

        Assert.Equal(400, outcome.Failure?.StatusCode);
        Assert.Equal("The request was not valid.", outcome.Failure?.Title);
    }

    [Fact]
    public async Task An_undeclared_status_still_renders_the_servers_own_problem_title()
    {
        // A 409 on an operation that declared no 409 arrives undeserialised, with the server's
        // problem document sitting in Response as text. Rendering "An unexpected error occurred."
        // over a problem the server titled properly is exactly what ApiFailures.From avoids.
        StubApiClient client = new()
        {
            CreateMeetingThrows = StubApiClient.BareWithBody(
                409,
                """{"status":409,"title":"A meeting with that title and date already exists."}"""),
        };

        MeetingsService service = new(client);

        MeetingOutcome<Guid> outcome = await service.CreateAsync(
            "Office move follow-up", new DateOnly(2026, 10, 3), [], TestContext.Current.CancellationToken);

        Assert.Equal(409, outcome.Failure?.StatusCode);
        Assert.Equal("A meeting with that title and date already exists.", outcome.Failure?.Title);
    }

    [Fact]
    public async Task A_bodyless_failure_falls_back_to_the_unexpected_title()
    {
        StubApiClient client = new() { ListMeetingsThrows = StubApiClient.Bare(500) };
        MeetingsService service = new(client);

        MeetingOutcome<MeetingPage> outcome = await service.ListAsync(1, 50, TestContext.Current.CancellationToken);

        Assert.Equal(500, outcome.Failure?.StatusCode);
        Assert.Equal(Voice.UnexpectedFailureTitle, outcome.Failure?.Title);
    }

    [Fact]
    public async Task A_transport_failure_becomes_a_failure_with_no_status()
    {
        // HttpRequestException is not an ApiException, so it needs its own catch arm. Status 0 is
        // how the detail page tells "no response" from a 404.
        StubApiClient client = new() { ListMeetingsThrows = new HttpRequestException("no route to host") };
        MeetingsService service = new(client);

        MeetingOutcome<MeetingPage> outcome = await service.ListAsync(1, 50, TestContext.Current.CancellationToken);

        Assert.Null(outcome.Value);
        Assert.Equal(0, outcome.Failure?.StatusCode);
        Assert.Equal(Voice.UnexpectedFailureTitle, outcome.Failure?.Title);
    }

    [Fact]
    public async Task A_timeout_the_caller_did_not_ask_for_becomes_a_failure()
    {
        // A timed-out request throws TaskCanceledException, which is neither of the arms above.
        // Uncaught it escapes as an unhandled component exception instead of the notice with Retry.
        StubApiClient client = new() { GetMeetingThrows = new TaskCanceledException("the request timed out") };
        MeetingsService service = new(client);

        MeetingOutcome<MeetingDetail> outcome = await service.GetAsync(MeetingId, TestContext.Current.CancellationToken);

        Assert.Null(outcome.Value);
        Assert.Equal(Voice.UnexpectedFailureTitle, outcome.Failure?.Title);
    }

    [Fact]
    public async Task A_cancellation_the_caller_asked_for_still_propagates()
    {
        // Rendering "Couldn't load." at someone who navigated away would be wrong. The catch is
        // for failures, not for the caller's own decision to stop.
        StubApiClient client = new() { GetMeetingThrows = new TaskCanceledException() };
        MeetingsService service = new(client);

        using CancellationTokenSource source = new();
        await source.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() => service.GetAsync(MeetingId, source.Token));
    }

    [Fact]
    public async Task A_date_that_arrives_at_a_non_zero_offset_keeps_the_day_the_server_wrote()
    {
        // DateFormatConverter.Read is DateTimeOffset.Parse with no format provider, and the
        // contract writes meetingDate as a bare "2026-09-21" — so the value reaches the seam
        // carrying the browser's own offset, not UTC. Reading it in any other offset moves the
        // day: UtcDateTime.Date turns 2026-09-21T00:00+09:00 into 2026-09-20 for every user
        // east of UTC. Every other fixture in the suite is TimeSpan.Zero, where the two agree.
        StubApiClient client = new();
        client.Meeting = new MeetingDetailDto
        {
            Id = MeetingId,
            Title = "Office move follow-up",
            MeetingDate = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.FromHours(9)),
            Attendees = [],
            CreatedByUserId = Guid.Empty,
            CreatedAt = DateTimeOffset.UnixEpoch,
            Notes = null,
        };

        MeetingsService service = new(client);

        MeetingOutcome<MeetingDetail> outcome = await service.GetAsync(MeetingId, TestContext.Current.CancellationToken);

        Assert.Equal(new DateOnly(2026, 9, 21), outcome.Value!.Date);
    }

    [Fact]
    public void The_three_limits_are_the_ones_the_committed_contract_states()
    {
        // The constants are re-declarations of the contract's own maxLength values, and the
        // dialog tests build their fixtures from the constants themselves — so they move with
        // any drift instead of catching it. Read as data, not as wording: a regenerated client
        // that raises a bound leaves these three the only thing still saying the old number,
        // and the dialog goes on refusing input the API would now accept.
        using System.Text.Json.JsonDocument contract =
            System.Text.Json.JsonDocument.Parse(WebProject.ReadAllText("openapi.json"));

        System.Text.Json.JsonElement schemas = contract.RootElement
            .GetProperty("components")
            .GetProperty("schemas");

        System.Text.Json.JsonElement create = schemas.GetProperty("CreateMeetingCommand").GetProperty("properties");

        Assert.Equal(
            MeetingsService.TitleMaxLength,
            create.GetProperty("title").GetProperty("maxLength").GetInt32());
        Assert.Equal(
            MeetingsService.AttendeeMaxLength,
            create.GetProperty("attendees").GetProperty("items").GetProperty("maxLength").GetInt32());
        Assert.Equal(
            MeetingsService.NotesMaxLength,
            schemas.GetProperty("SaveMeetingNotesCommand")
                .GetProperty("properties")
                .GetProperty("text")
                .GetProperty("maxLength")
                .GetInt32());
    }

    /// <summary>
    /// The two counts are distinct and non-zero on purpose. Every real Meeting carries 0 and 0
    /// until Stories 2.5 and 3.1 land, and against 0 and 0 the two arguments <c>ToListItem</c>
    /// passes are interchangeable.
    /// </summary>
    private static MeetingSummaryDto Summary(
        string title,
        DateTimeOffset date,
        int runs = 3,
        int trackedActions = 7) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        MeetingDate = date,
        RunCount = runs,
        TrackedActionCount = trackedActions,
    };
}
