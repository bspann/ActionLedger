using ActionLedger.Web.Core;
using ActionLedger.Web.Core.Api;
using ActionLedger.Web.Core.Auth;
using ActionLedger.Web.Core.Formatting;
using ActionLedger.Web.Core.Shell;
using ActionLedger.Web.Core.Users;
using ActionLedger.Web.Features.Meetings;
using ActionLedger.Web.Features.Meetings.Data;
using ActionLedger.Web.Shared;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace ActionLedger.Web.Tests;

/// <summary>
/// UX-DR13 and ADR-002 — Meeting Detail and the write-once notes paste area. The claim this file
/// exists to hold is structural: once notes are attached, the edit markup is not in the rendered
/// tree at all.
/// </summary>
/// <remarks>
/// AD-14 shows up here as an absence: the page cannot see <c>MeetingDetailDto</c>, so this file
/// reaches it only through the stub client, on the far side of <c>MeetingsService</c>.
/// </remarks>
public sealed class MeetingDetailPageTests : BunitContext
{
    private static readonly Guid MeetingId = Guid.Parse("01999999-0000-7000-8000-00000000beef");

    private static readonly DateTimeOffset SavedAt = new(2026, 9, 21, 14, 3, 0, TimeSpan.Zero);

    private readonly StubApiClient client = new();
    private readonly SessionState session = new();

    public MeetingDetailPageTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton<IActionLedgerApiClient>(client);
        Services.AddSingleton(session);
        Services.AddSingleton(new LoadingState());
        Services.AddSingleton<UserDirectory>();
        Services.AddSingleton<MeetingsService>();
    }

    [Fact]
    public void The_fields_render_through_the_shared_formats()
    {
        SignIn();
        client.Meeting = Meeting(attendees: ["Dana Whitfield", "Priya Ramaswamy"]);

        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        Assert.Equal("Office move follow-up", page.Find("h1").TextContent);
        Assert.Equal("2026-09-21", page.Find($"#{MeetingDetailPage.DateId}").TextContent);
        Assert.Equal(
            ["Dana Whitfield", "Priya Ramaswamy"],
            page.FindAll(".mud-chip").Select(chip => chip.TextContent.Trim()));
    }

    [Fact]
    public void A_meeting_nobody_attended_renders_a_dash_rather_than_an_empty_row()
    {
        SignIn();
        client.Meeting = Meeting(attendees: []);

        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        Assert.Equal(Voice.NoValue, page.Find($"#{MeetingDetailPage.AttendeesId}").TextContent);
    }

    [Fact]
    public void A_meeting_without_notes_shows_the_paste_area_with_its_count_and_caption()
    {
        SignIn();

        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        Assert.Equal(Voice.Notes, page.Find($"label[for='{MeetingDetailPage.NotesFieldId}']").TextContent);
        Assert.Equal("0 / 50,000", page.Find($"#{MeetingDetailPage.NotesCountId}").TextContent);
        Assert.Equal(Voice.NotesImmutable, page.Find($"#{MeetingDetailPage.NotesImmutableId}").TextContent);

        // NFR-6's floor: the count and the immutability sentence are both announced with the
        // field rather than sitting beside it as decoration a screen reader never reaches. The
        // sentence is the one that makes this write irreversible, so it is the last thing that
        // should be reachable by sight only.
        Assert.Equal(
            $"{MeetingDetailPage.NotesCountId} {MeetingDetailPage.NotesImmutableId}",
            page.Find($"#{MeetingDetailPage.NotesFieldId}").GetAttribute("aria-describedby"));

        // Nothing to save yet.
        Assert.True(page.Find($"#{MeetingDetailPage.SaveNotesId}").HasAttribute("disabled"));
    }

    [Fact]
    public void The_count_follows_the_typed_text()
    {
        SignIn();

        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        // 26 characters, counted against the contract's own limit and rendered invariantly, so
        // a browser running under de-DE still reads the limit as 50,000 rather than 50.000.
        page.Find($"#{MeetingDetailPage.NotesFieldId}").Input("Dana will book the movers.");

        Assert.Equal("26 / 50,000", page.Find($"#{MeetingDetailPage.NotesCountId}").TextContent);
        Assert.False(page.Find($"#{MeetingDetailPage.SaveNotesId}").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Cancelling_the_confirm_dialog_sends_nothing_and_keeps_the_text()
    {
        SignIn();

        IRenderedComponent<MudDialogProvider> provider = Render<MudDialogProvider>();
        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        page.Find($"#{MeetingDetailPage.NotesFieldId}").Input("Dana will book the movers.");

        // Not awaited: the handler stays parked on the dialog's result until it is closed.
        Task saving = page.Find($"#{MeetingDetailPage.SaveNotesId}").ClickAsync(new MouseEventArgs());

        // The dialog repeats the caption's sentence verbatim — one Voice constant, both places.
        Assert.Contains(
            Voice.NotesImmutable,
            provider.WaitForElement($"#{ConfirmDialog.ConfirmId}").ParentElement!.ParentElement!.TextContent,
            StringComparison.Ordinal);

        provider.Find($"#{ConfirmDialog.CancelId}").Click();

        await saving;

        Assert.Equal(0, client.SaveMeetingNotesCalls);
        Assert.Equal(
            "Dana will book the movers.",
            page.Find($"#{MeetingDetailPage.NotesFieldId}").GetAttribute("value"));
    }

    [Fact]
    public async Task Confirming_sends_the_text_byte_for_byte_and_the_area_becomes_read_only()
    {
        // ADR-002 — leading whitespace and CRLF survive. The server hashes what it stored, so
        // anything tidied on the way out would silently become the record.
        const string Typed = "  Dana will book the movers.\r\n\tFollow up Friday.\n";

        SignIn();
        client.SavedNotes = new MeetingNotesDto
        {
            Id = Guid.Empty,
            Text = Typed,
            Sha256 = new string('a', 64),
            SavedAt = SavedAt,
        };

        IRenderedComponent<MudDialogProvider> provider = Render<MudDialogProvider>();
        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        page.Find($"#{MeetingDetailPage.NotesFieldId}").Input(Typed);

        Task saving = page.Find($"#{MeetingDetailPage.SaveNotesId}").ClickAsync(new MouseEventArgs());
        provider.WaitForElement($"#{ConfirmDialog.ConfirmId}").Click();
        await saving;

        Assert.Equal(1, client.SaveMeetingNotesCalls);
        Assert.Equal(MeetingId, client.LastSaveMeetingNotesId);
        Assert.Equal(Typed, client.LastSaveMeetingNotesCommand?.Text);

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("textarea")));

        // The leading spaces, the tab, and the blank structure all survive into the read-only
        // block. The comparison normalises CRLF because AngleSharp does so while parsing, exactly
        // as the HTML parsing spec says a browser does; what was actually sent is asserted above.
        Assert.Equal(
            Typed.Replace("\r\n", "\n", StringComparison.Ordinal),
            page.Find($"#{MeetingDetailPage.NotesTextId}").TextContent);
        Assert.Equal(
            Voice.NotesSavedPrefix + Formats.Instant(SavedAt) + Voice.NotesSavedSuffix,
            page.Find($"#{MeetingDetailPage.NotesCaptionId}").TextContent);
    }

    [Fact]
    public async Task The_save_button_cannot_fire_twice_while_the_call_is_outstanding()
    {
        // The flag has to be claimed before the confirm dialog opens. Claimed after it closes,
        // both of HandleEventAsync's renders see it false, the button never draws disabled, and
        // two quick clicks stack two dialogs and send two PUTs at a write that may happen once.
        TaskCompletionSource gate = new();
        client.SaveMeetingNotesGate = gate.Task;

        SignIn();

        IRenderedComponent<MudDialogProvider> provider = Render<MudDialogProvider>();
        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        page.Find($"#{MeetingDetailPage.NotesFieldId}").Input("Dana will book the movers.");

        Task saving = page.Find($"#{MeetingDetailPage.SaveNotesId}").ClickAsync(new MouseEventArgs());
        provider.WaitForElement($"#{ConfirmDialog.ConfirmId}").Click();

        page.WaitForAssertion(() =>
            Assert.True(page.Find($"#{MeetingDetailPage.SaveNotesId}").HasAttribute("disabled")));

        page.Find($"#{MeetingDetailPage.SaveNotesId}").Click();

        Assert.Equal(1, client.SaveMeetingNotesCalls);

        gate.SetResult();
        await saving;
    }

    [Fact]
    public async Task The_save_button_is_disabled_while_the_confirm_dialog_is_open()
    {
        SignIn();

        IRenderedComponent<MudDialogProvider> provider = Render<MudDialogProvider>();
        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        page.Find($"#{MeetingDetailPage.NotesFieldId}").Input("Dana will book the movers.");

        Task saving = page.Find($"#{MeetingDetailPage.SaveNotesId}").ClickAsync(new MouseEventArgs());
        provider.WaitForElement($"#{ConfirmDialog.ConfirmId}");

        // EXPERIENCE.md bans modal stacks deeper than one, so the control that opens the dialog
        // is unavailable while it is up.
        Assert.True(page.Find($"#{MeetingDetailPage.SaveNotesId}").HasAttribute("disabled"));

        provider.Find($"#{ConfirmDialog.CancelId}").Click();
        await saving;

        // Dismissing hands the button back.
        Assert.False(page.Find($"#{MeetingDetailPage.SaveNotesId}").HasAttribute("disabled"));
    }

    [Fact]
    public void A_meeting_that_arrives_with_notes_renders_no_edit_affordance_at_all()
    {
        // The story's one irreversible rule, asserted on the rendered markup. The page branches
        // on "notes is null" around the whole region rather than toggling a ReadOnly flag, so
        // there is nothing here to re-enable.
        SignIn();
        client.Meeting = Meeting(notes: new MeetingNotesDto
        {
            Id = Guid.Empty,
            Text = "  Dana will book the movers.\r\n",
            Sha256 = new string('a', 64),
            SavedAt = SavedAt,
        });

        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        Assert.Empty(page.FindAll("textarea"));
        Assert.Empty(page.FindAll($"#{MeetingDetailPage.SaveNotesId}"));
        Assert.DoesNotContain(Voice.SaveNotes, page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(Voice.NotesImmutable, page.Markup, StringComparison.Ordinal);

        // CRLF normalised by the HTML parser, as in any browser; the leading spaces are the part
        // that would be lost without white-space: pre-wrap.
        Assert.Equal("  Dana will book the movers.\n", page.Find($"#{MeetingDetailPage.NotesTextId}").TextContent);
        Assert.Equal(
            Voice.NotesSavedPrefix + "2026-09-21 14:03 UTC" + Voice.NotesSavedSuffix,
            page.Find($"#{MeetingDetailPage.NotesCaptionId}").TextContent);
    }

    [Fact]
    public void The_read_only_notes_block_preserves_the_whitespace_it_was_given()
    {
        // UX-DR13 — the class app.css gives white-space: pre-wrap. Without it the browser
        // collapses every run of spaces and every newline in text stored byte-for-byte.
        SignIn();
        client.Meeting = Meeting(notes: new MeetingNotesDto
        {
            Id = Guid.Empty,
            Text = "a\nb",
            Sha256 = new string('a', 64),
            SavedAt = SavedAt,
        });

        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        Assert.Contains("al-notes", page.Find($"#{MeetingDetailPage.NotesTextId}").ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_conflicting_save_re_reads_the_meeting_and_renders_the_stored_notes()
    {
        // 409 means the notes are already attached. The global handler raises "Already changed.
        // Reloading."; performing the reload is this page's half of that sentence.
        SignIn();
        client.SaveMeetingNotesThrows = StubApiClient.BareWithBody(
            409,
            """{"status":409,"title":"Notes are already attached to this meeting."}""");

        IRenderedComponent<MudDialogProvider> provider = Render<MudDialogProvider>();
        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        page.Find($"#{MeetingDetailPage.NotesFieldId}").Input("mine");

        client.Meeting = Meeting(notes: new MeetingNotesDto
        {
            Id = Guid.Empty,
            Text = "the notes that won",
            Sha256 = new string('a', 64),
            SavedAt = SavedAt,
        });

        Task saving = page.Find($"#{MeetingDetailPage.SaveNotesId}").ClickAsync(new MouseEventArgs());
        provider.WaitForElement($"#{ConfirmDialog.ConfirmId}").Click();
        await saving;

        Assert.Equal(2, client.GetMeetingCalls);
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("textarea")));
        Assert.Equal("the notes that won", page.Find($"#{MeetingDetailPage.NotesTextId}").TextContent);
    }

    [Fact]
    public async Task A_failed_save_keeps_the_typed_text_and_leaves_the_area_editable()
    {
        SignIn();
        client.SaveMeetingNotesThrows = StubApiClient.Bare(500);

        IRenderedComponent<MudDialogProvider> provider = Render<MudDialogProvider>();
        IRenderedComponent<MudSnackbarProvider> snackbars = Render<MudSnackbarProvider>();
        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        page.Find($"#{MeetingDetailPage.NotesFieldId}").Input("Dana will book the movers.");

        Task saving = page.Find($"#{MeetingDetailPage.SaveNotesId}").ClickAsync(new MouseEventArgs());
        provider.WaitForElement($"#{ConfirmDialog.ConfirmId}").Click();
        await saving;

        // EXPERIENCE.md's "Save failure or network error" row: the problem title with a Retry
        // action, and nothing marked saved. Asserted on the rendered snackbar rather than on
        // ISnackbar.ShownSnackbars, which exposes the message but not the action.
        snackbars.WaitForAssertion(() => Assert.Contains(
            Voice.UnexpectedFailureTitle,
            snackbars.Markup,
            StringComparison.Ordinal));

        Assert.NotEmpty(page.FindAll("textarea"));
        Assert.Equal(
            "Dana will book the movers.",
            page.Find($"#{MeetingDetailPage.NotesFieldId}").GetAttribute("value"));

        // The action has to do something. Reading the word "Retry" out of the markup would stay
        // green with the handler deleted, so it is pressed, and the save it starts is the one
        // that proves it — confirm dialog included, because a retry is still an irreversible
        // write and still asks.
        client.SaveMeetingNotesThrows = null;

        Task retry = RetryAction(snackbars).ClickAsync(new MouseEventArgs());
        provider.WaitForElement($"#{ConfirmDialog.ConfirmId}").Click();
        await retry;

        Assert.Equal(2, client.SaveMeetingNotesCalls);

        // The snackbar's click arrives from ISnackbar, outside the component's event pipeline, so
        // a successful retry repaints only if the handler dispatches onto the renderer itself.
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("textarea")));
        Assert.Equal("the notes", page.Find($"#{MeetingDetailPage.NotesTextId}").TextContent);
    }

    /// <summary>The snackbar's action button, reached by its text rather than by MudBlazor's own
    /// class names, which would retarget silently.</summary>
    private static IElement RetryAction(IRenderedComponent<MudSnackbarProvider> snackbars) =>
        snackbars.FindAll("button").First(button =>
            string.Equals(button.TextContent.Trim(), Voice.Retry, StringComparison.Ordinal));

    [Fact]
    public void An_unknown_meeting_renders_the_not_found_notice()
    {
        SignIn();
        client.GetMeetingThrows = StubApiClient.Problem(404, "The resource was not found.");

        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        Assert.Equal(Voice.NotFound, page.Find("h1").TextContent);
        Assert.Equal("/meetings", page.Find("a").GetAttribute("href"));
        Assert.DoesNotContain(Voice.LoadFailurePrefix, page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_load_renders_the_notice_and_retry_re_reads_the_meeting()
    {
        SignIn();
        client.GetMeetingThrows = StubApiClient.Bare(500);

        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        Assert.Contains(
            Voice.LoadFailurePrefix + Voice.UnexpectedFailureTitle,
            page.Markup,
            StringComparison.Ordinal);

        client.GetMeetingThrows = null;
        page.Find($"#{LoadFailure.RetryId}").Click();

        page.WaitForAssertion(() => Assert.Equal(2, client.GetMeetingCalls));
        page.WaitForAssertion(() => Assert.Equal("Office move follow-up", page.Find("h1").TextContent));
    }

    [Fact]
    public void A_transport_failure_is_not_mistaken_for_a_missing_meeting()
    {
        // ApiFailure.StatusCode is 0 when no response arrived, which is how 404 is told apart
        // from "the API was unreachable". Rendering "Not found." over a dead network would send
        // the user back to the list looking for a meeting that is still there.
        SignIn();
        client.GetMeetingThrows = new HttpRequestException("no route to host");

        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        Assert.Contains(Voice.LoadFailurePrefix, page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(Voice.NotFound, page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unauthenticated_visitor_is_sent_to_login_with_the_attempted_route_captured()
    {
        Navigation.NavigateTo("/meetings/" + MeetingId);

        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        Assert.Equal("/login", Navigation.History.First().Uri);
        Assert.Equal("/meetings/" + MeetingId, session.TakeAttemptedRoute());

        // The check runs before the load, so no round trip is spent on a 401 already known.
        Assert.Equal(0, client.GetMeetingCalls);
        Assert.Empty(page.FindAll("textarea"));
    }

    [Fact]
    public async Task The_page_has_a_heading_while_the_meeting_is_still_loading()
    {
        // App.razor's <FocusOnNavigate Selector="h1" /> looks once, after the first render that
        // follows the navigation — which lands here, with the GET still in flight. An h1 that
        // only exists once `detail` arrives is an h1 it never finds, and focus is left on the
        // row link being torn down.
        TaskCompletionSource gate = new();
        client.GetMeetingGate = gate.Task;

        SignIn();

        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        Assert.Equal(Voice.Meeting, page.Find("h1").TextContent);

        gate.SetResult();

        page.WaitForAssertion(() => Assert.Equal("Office move follow-up", page.Find("h1").TextContent));
    }

    [Fact]
    public void A_failed_load_still_leaves_the_page_a_heading()
    {
        // LoadFailure brings no heading of its own, so before this the failure branch left the
        // route with no h1 at all — permanently, not just for the length of a request.
        SignIn();
        client.GetMeetingThrows = StubApiClient.Bare(500);

        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        Assert.Equal(Voice.Meeting, page.Find("h1").TextContent);
    }

    [Fact]
    public void Each_field_value_is_named_by_its_caption()
    {
        // The captions are plain text, not form labels, so without aria-labelledby a screen
        // reader reads four unrelated lines instead of "Date, 2026-09-21".
        SignIn();
        client.Meeting = Meeting(attendees: ["Dana Whitfield"]);

        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        Assert.Equal(
            MeetingDetailPage.DateLabelId,
            page.Find($"#{MeetingDetailPage.DateId}").GetAttribute("aria-labelledby"));
        Assert.Equal(
            MeetingDetailPage.AttendeesLabelId,
            page.Find($"#{MeetingDetailPage.AttendeesId}").GetAttribute("aria-labelledby"));
        Assert.Equal(Voice.Date, page.Find($"#{MeetingDetailPage.DateLabelId}").TextContent);
        Assert.Equal(Voice.Attendees, page.Find($"#{MeetingDetailPage.AttendeesLabelId}").TextContent);
    }

    [Fact]
    public void The_paste_area_caps_the_draft_at_the_contract_limit()
    {
        // The only thing standing between a 60,000-character paste and a guaranteed 400 on the
        // one write the contract allows once. Nothing else in the page looks at the upper bound:
        // both guards test draft.Length against 0.
        SignIn();

        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        Assert.Equal(
            MeetingsService.NotesMaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            page.Find($"#{MeetingDetailPage.NotesFieldId}").GetAttribute("maxlength"));
    }

    [Fact]
    public void A_draft_of_nothing_but_whitespace_cannot_be_saved()
    {
        // The server's MinimumLength of 1 counts spaces, so without this the irreversible write
        // attaches blank notes to the meeting for good. The text still goes byte-for-byte when
        // there is something in it — this gates the button, it does not trim the payload.
        SignIn();

        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        page.Find($"#{MeetingDetailPage.NotesFieldId}").Input("   \n  ");

        Assert.True(page.Find($"#{MeetingDetailPage.SaveNotesId}").HasAttribute("disabled"));
        Assert.Equal(0, client.SaveMeetingNotesCalls);
    }

    [Fact]
    public async Task A_successful_save_announces_itself()
    {
        // The read-only branch takes the Save button out of the tree in the same render, and
        // that button is what the confirm dialog returns focus to — so nothing on screen has
        // focus and, without this, nothing says the write happened.
        SignIn();

        IRenderedComponent<MudDialogProvider> provider = Render<MudDialogProvider>();
        IRenderedComponent<MudSnackbarProvider> snackbars = Render<MudSnackbarProvider>();
        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        page.Find($"#{MeetingDetailPage.NotesFieldId}").Input("Dana will book the movers.");

        Task saving = page.Find($"#{MeetingDetailPage.SaveNotesId}").ClickAsync(new MouseEventArgs());
        provider.WaitForElement($"#{ConfirmDialog.ConfirmId}").Click();
        await saving;

        snackbars.WaitForAssertion(() =>
            Assert.Contains(Voice.NotesSaved, snackbars.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_stale_retry_sends_nothing_once_notes_are_already_attached()
    {
        // The failure snackbar outlives the failure, so its Retry is still clickable after a
        // later attempt has succeeded. Without the third guard it opens a confirm dialog over a
        // read-only region and sends a second PUT at a write the contract allows exactly once.
        SignIn();
        client.SaveMeetingNotesThrows = StubApiClient.Bare(500);

        IRenderedComponent<MudDialogProvider> provider = Render<MudDialogProvider>();
        IRenderedComponent<MudSnackbarProvider> snackbars = Render<MudSnackbarProvider>();
        IRenderedComponent<MeetingDetailPage> page = RenderDetail();

        page.Find($"#{MeetingDetailPage.NotesFieldId}").Input("Dana will book the movers.");

        Task failing = page.Find($"#{MeetingDetailPage.SaveNotesId}").ClickAsync(new MouseEventArgs());
        provider.WaitForElement($"#{ConfirmDialog.ConfirmId}").Click();
        await failing;

        snackbars.WaitForAssertion(() => Assert.Contains(
            Voice.UnexpectedFailureTitle,
            snackbars.Markup,
            StringComparison.Ordinal));

        // The user presses the button again rather than the snackbar, and that one succeeds.
        client.SaveMeetingNotesThrows = null;

        Task saving = page.Find($"#{MeetingDetailPage.SaveNotesId}").ClickAsync(new MouseEventArgs());
        provider.WaitForElement($"#{ConfirmDialog.ConfirmId}").Click();
        await saving;

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("textarea")));
        Assert.Equal(2, client.SaveMeetingNotesCalls);

        // The first failure's snackbar is still up, and its Retry is still live.
        await RetryAction(snackbars).ClickAsync(new MouseEventArgs());

        Assert.Equal(2, client.SaveMeetingNotesCalls);
        Assert.Empty(provider.FindAll($"#{ConfirmDialog.ConfirmId}"));
    }

    private BunitNavigationManager Navigation =>
        (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

    private IRenderedComponent<MeetingDetailPage> RenderDetail() =>
        Render<MeetingDetailPage>(parameters => parameters.Add(component => component.Id, MeetingId));

    private void SignIn() =>
        session.SignIn(new SignedInUser("jwt", DateTimeOffset.UnixEpoch, Guid.Empty, "Dana Whitfield", RoleNames.ActionOfficer));

    private static MeetingDetailDto Meeting(string[]? attendees = null, MeetingNotesDto? notes = null) => new()
    {
        Id = MeetingId,
        Title = "Office move follow-up",
        MeetingDate = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero),
        Attendees = attendees ?? [],
        CreatedByUserId = Guid.Empty,
        CreatedAt = DateTimeOffset.UnixEpoch,
        Notes = notes,
    };
}
