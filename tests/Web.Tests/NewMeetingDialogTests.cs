using ActionLedger.Web.Core;
using ActionLedger.Web.Core.Api;
using ActionLedger.Web.Core.Auth;
using ActionLedger.Web.Core.Shell;
using ActionLedger.Web.Core.Users;
using ActionLedger.Web.Features.Meetings;
using ActionLedger.Web.Features.Meetings.Data;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace ActionLedger.Web.Tests;

/// <summary>
/// UX-DR12's New meeting dialog: Title and Date are required, every bound value is refused
/// inline rather than by the server, attendees are chips, and a refusal keeps the dialog open
/// with everything the user typed still in it.
/// </summary>
/// <remarks>
/// AD-14 shows up here as an absence: the dialog cannot see <c>CreateMeetingCommand</c>, so this
/// file reaches it only through the stub client, on the far side of <c>MeetingsService</c>.
/// </remarks>
public sealed class NewMeetingDialogTests : BunitContext
{
    private static readonly Guid CreatedId = Guid.Parse("01999999-0000-7000-8000-00000000000c");

    private readonly StubApiClient client = new();
    private readonly SessionState session = new();

    public NewMeetingDialogTests()
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
    public async Task A_blank_title_shows_a_message_under_the_title_field_and_sends_nothing()
    {
        IRenderedComponent<MudDialogProvider> dialog = await OpenAsync();

        await SetDateAsync(dialog, new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Unspecified));
        dialog.Find($"#{NewMeetingDialog.CreateId}").Click();

        Assert.Contains(Voice.TitleRequired, FieldOf(dialog, NewMeetingDialog.TitleId).TextContent, StringComparison.Ordinal);
        Assert.Equal(0, client.CreateMeetingCalls);

        // The dialog stays open: the message is a correction, not a dismissal.
        Assert.NotNull(dialog.Find($"#{NewMeetingDialog.CreateId}"));
    }

    [Fact]
    public async Task A_missing_date_shows_a_message_under_the_date_field_and_sends_nothing()
    {
        IRenderedComponent<MudDialogProvider> dialog = await OpenAsync();

        dialog.Find($"#{NewMeetingDialog.TitleId}").Input("Office move follow-up");
        dialog.Find($"#{NewMeetingDialog.CreateId}").Click();

        Assert.Contains(Voice.DateRequired, dialog.FindComponent<MudDatePicker>().Markup, StringComparison.Ordinal);
        Assert.Equal(0, client.CreateMeetingCalls);
    }

    [Fact]
    public async Task An_over_long_title_is_refused_before_the_request_is_made()
    {
        // The contract caps Title at 200, so a 201-character title is a 400 waiting to happen.
        IRenderedComponent<MudDialogProvider> dialog = await OpenAsync();

        dialog.Find($"#{NewMeetingDialog.TitleId}").Input(new string('a', MeetingsService.TitleMaxLength + 1));
        await SetDateAsync(dialog, new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Unspecified));
        dialog.Find($"#{NewMeetingDialog.CreateId}").Click();

        Assert.Contains(Voice.TitleTooLong, FieldOf(dialog, NewMeetingDialog.TitleId).TextContent, StringComparison.Ordinal);
        Assert.Equal(0, client.CreateMeetingCalls);
    }

    [Fact]
    public async Task An_over_long_attendee_is_refused_inline_and_is_not_added_as_a_chip()
    {
        IRenderedComponent<MudDialogProvider> dialog = await OpenAsync();

        AddAttendee(dialog, new string('b', MeetingsService.AttendeeMaxLength + 1));

        Assert.Contains(
            Voice.AttendeeTooLong,
            FieldOf(dialog, NewMeetingDialog.AttendeeId).TextContent,
            StringComparison.Ordinal);
        Assert.Empty(dialog.FindAll(".mud-chip"));
    }

    [Fact]
    public async Task Attendees_round_trip_as_chips_and_a_closed_chip_is_removed()
    {
        IRenderedComponent<MudDialogProvider> dialog = await OpenAsync();

        AddAttendee(dialog, "Dana Whitfield");
        AddAttendee(dialog, "  Priya Ramaswamy  ");

        Assert.Equal(
            ["Dana Whitfield", "Priya Ramaswamy"],
            dialog.FindAll(".mud-chip").Select(chip => chip.TextContent.Trim()));

        dialog.FindAll(".mud-chip-close-button")[0].Click();

        Assert.Equal(["Priya Ramaswamy"], dialog.FindAll(".mud-chip").Select(chip => chip.TextContent.Trim()));
    }

    [Fact]
    public async Task Create_sends_exactly_one_command_carrying_the_trimmed_title_the_date_and_the_chips()
    {
        client.MeetingCreated = new MeetingCreatedDto { Id = CreatedId, CreatedByUserId = Guid.Empty };

        IRenderedComponent<MudDialogProvider> dialog = await OpenAsync();

        dialog.Find($"#{NewMeetingDialog.TitleId}").Input("  Office move follow-up  ");
        await SetDateAsync(dialog, new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Unspecified));
        AddAttendee(dialog, "Dana Whitfield");
        AddAttendee(dialog, "Priya Ramaswamy");

        dialog.Find($"#{NewMeetingDialog.CreateId}").Click();

        dialog.WaitForAssertion(() => Assert.Equal(1, client.CreateMeetingCalls));

        CreateMeetingCommand command = client.LastCreateMeetingCommand!;
        Assert.Equal("Office move follow-up", command.Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero), command.MeetingDate);
        Assert.Equal(["Dana Whitfield", "Priya Ramaswamy"], command.Attendees);
    }

    [Fact]
    public async Task A_value_typed_but_not_entered_is_still_sent_rather_than_dropped()
    {
        // Enter commits a chip, but nobody reads the instructions. Pressing Create with a name
        // still in the field would otherwise create the meeting without that attendee.
        client.MeetingCreated = new MeetingCreatedDto { Id = CreatedId, CreatedByUserId = Guid.Empty };

        IRenderedComponent<MudDialogProvider> dialog = await OpenAsync();

        dialog.Find($"#{NewMeetingDialog.TitleId}").Input("Office move follow-up");
        await SetDateAsync(dialog, new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Unspecified));
        dialog.Find($"#{NewMeetingDialog.AttendeeId}").Input("Dana Whitfield");

        dialog.Find($"#{NewMeetingDialog.CreateId}").Click();

        dialog.WaitForAssertion(() => Assert.Equal(1, client.CreateMeetingCalls));
        Assert.Equal(["Dana Whitfield"], client.LastCreateMeetingCommand!.Attendees);
    }

    [Fact]
    public async Task Create_refuses_an_over_long_attendee_still_sitting_in_the_field()
    {
        // Create commits the draft first, so an over-long value nobody pressed Enter on is
        // refused here rather than travelling as a 400 — or, worse, being dropped silently.
        IRenderedComponent<MudDialogProvider> dialog = await OpenAsync();

        dialog.Find($"#{NewMeetingDialog.TitleId}").Input("Office move follow-up");
        await SetDateAsync(dialog, new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Unspecified));
        dialog.Find($"#{NewMeetingDialog.AttendeeId}").Input(new string('b', MeetingsService.AttendeeMaxLength + 1));

        dialog.Find($"#{NewMeetingDialog.CreateId}").Click();

        Assert.Equal(0, client.CreateMeetingCalls);
        Assert.Contains(
            Voice.AttendeeTooLong,
            FieldOf(dialog, NewMeetingDialog.AttendeeId).TextContent,
            StringComparison.Ordinal);
        Assert.Empty(dialog.FindAll(".mud-chip"));
    }

    [Fact]
    public async Task Clearing_a_refused_attendee_lets_create_through_again()
    {
        // Otherwise the message stays attached to an empty field and Create goes silent: the
        // validation guard keeps returning false and nothing on screen says why.
        client.MeetingCreated = new MeetingCreatedDto { Id = CreatedId, CreatedByUserId = Guid.Empty };

        IRenderedComponent<MudDialogProvider> dialog = await OpenAsync();

        dialog.Find($"#{NewMeetingDialog.TitleId}").Input("Office move follow-up");
        await SetDateAsync(dialog, new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Unspecified));
        AddAttendee(dialog, new string('b', MeetingsService.AttendeeMaxLength + 1));

        dialog.Find($"#{NewMeetingDialog.AttendeeId}").Input(string.Empty);
        dialog.Find($"#{NewMeetingDialog.CreateId}").Click();

        dialog.WaitForAssertion(() => Assert.Equal(1, client.CreateMeetingCalls));
        Assert.Empty(client.LastCreateMeetingCommand!.Attendees);
    }

    [Fact]
    public async Task Cancel_is_unavailable_while_the_create_is_outstanding()
    {
        // Dismissing mid-request leaves the POST on its way: the meeting is created and nothing
        // navigates to it or shows it.
        TaskCompletionSource gate = new();
        client.CreateMeetingGate = gate.Task;

        IRenderedComponent<MudDialogProvider> dialog = await OpenAsync();

        dialog.Find($"#{NewMeetingDialog.TitleId}").Input("Office move follow-up");
        await SetDateAsync(dialog, new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Unspecified));

        dialog.Find($"#{NewMeetingDialog.CreateId}").Click();

        dialog.WaitForAssertion(() =>
            Assert.True(dialog.Find($"#{NewMeetingDialog.CancelId}").HasAttribute("disabled")));

        gate.SetResult();
    }

    [Fact]
    public async Task The_create_button_cannot_fire_twice_while_the_call_is_outstanding()
    {
        TaskCompletionSource gate = new();
        client.CreateMeetingGate = gate.Task;

        IRenderedComponent<MudDialogProvider> dialog = await OpenAsync();

        dialog.Find($"#{NewMeetingDialog.TitleId}").Input("Office move follow-up");
        await SetDateAsync(dialog, new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Unspecified));

        dialog.Find($"#{NewMeetingDialog.CreateId}").Click();

        dialog.WaitForAssertion(() =>
            Assert.True(dialog.Find($"#{NewMeetingDialog.CreateId}").HasAttribute("disabled")));

        dialog.Find($"#{NewMeetingDialog.CreateId}").Click();

        Assert.Equal(1, client.CreateMeetingCalls);

        gate.SetResult();
    }

    [Fact]
    public async Task A_refusal_keeps_the_dialog_open_with_the_values_retained()
    {
        // EXPERIENCE.md's "Save failure" row asks for the problem title with the form values
        // retained. Inside a modal the retry affordance is the Create button itself, over the
        // values still on screen, so the failure renders inline rather than behind the backdrop.
        client.CreateMeetingThrows = StubApiClient.BareWithBody(
            409,
            """{"status":409,"title":"A meeting with that title and date already exists."}""");

        IRenderedComponent<MudDialogProvider> dialog = await OpenAsync();

        dialog.Find($"#{NewMeetingDialog.TitleId}").Input("Office move follow-up");
        await SetDateAsync(dialog, new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Unspecified));
        AddAttendee(dialog, "Dana Whitfield");

        dialog.Find($"#{NewMeetingDialog.CreateId}").Click();

        dialog.WaitForAssertion(() => Assert.Equal(
            "A meeting with that title and date already exists.",
            dialog.Find($"#{NewMeetingDialog.FailureId}").TextContent));

        // The Create button is the retry affordance, so the reason is named from it.
        Assert.Equal(
            NewMeetingDialog.FailureId,
            dialog.Find($"#{NewMeetingDialog.CreateId}").GetAttribute("aria-describedby"));

        // Still open, with everything the user typed.
        Assert.Equal("Office move follow-up", dialog.Find($"#{NewMeetingDialog.TitleId}").GetAttribute("value"));
        Assert.Equal(["Dana Whitfield"], dialog.FindAll(".mud-chip").Select(chip => chip.TextContent.Trim()));
    }

    private async Task<IRenderedComponent<MudDialogProvider>> OpenAsync()
    {
        IRenderedComponent<MudDialogProvider> provider = Render<MudDialogProvider>();
        IDialogService dialogs = Services.GetRequiredService<IDialogService>();

        await provider.InvokeAsync(() => dialogs.ShowAsync<NewMeetingDialog>(Voice.NewMeeting));
        provider.WaitForElement($"#{NewMeetingDialog.CreateId}");

        return provider;
    }

    private static void AddAttendee(IRenderedComponent<MudDialogProvider> dialog, string attendee)
    {
        dialog.Find($"#{NewMeetingDialog.AttendeeId}").Input(attendee);
        dialog.Find($"#{NewMeetingDialog.AttendeeId}").KeyDown(new KeyboardEventArgs { Key = "Enter" });
    }

    /// <summary>
    /// The whole input control a field sits in — label, input, and the helper slot MudBlazor
    /// renders <c>ErrorText</c> into. "Under the offending field" is what the AC asks for, so the
    /// message is asserted inside that field's control rather than anywhere in the dialog.
    /// </summary>
    private static IElement FieldOf(IRenderedComponent<MudDialogProvider> dialog, string id) =>
        dialog.Find("#" + id).Closest(".mud-input-control")!;

    /// <summary>
    /// MudDatePicker's calendar is a popover driven by JS interop, which the loose runtime does
    /// not operate. Raising the bound callback is what the calendar itself does.
    /// </summary>
    private static Task SetDateAsync(IRenderedComponent<MudDialogProvider> dialog, DateTime date)
    {
        IRenderedComponent<MudDatePicker> picker = dialog.FindComponent<MudDatePicker>();

        return picker.InvokeAsync(() => picker.Instance.DateChanged.InvokeAsync(date));
    }
}
