using ActionLedger.Web.Core;
using ActionLedger.Web.Core.Api;
using ActionLedger.Web.Core.Auth;
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
/// FR-3 and UX-DR12 — the Meeting List. Four columns in the server's order, a paginator that
/// speaks the API's 1-based page numbers, both a mouse and a keyboard route into detail, and the
/// app's first route-level auth check.
/// </summary>
/// <remarks>
/// AD-14 shows up here as an absence: this file names no generated type except through the stub
/// client, because the page itself cannot see one.
/// </remarks>
public sealed class MeetingListPageTests : BunitContext
{
    private static readonly Guid FirstId = Guid.Parse("01999999-0000-7000-8000-000000000001");
    private static readonly Guid SecondId = Guid.Parse("01999999-0000-7000-8000-000000000002");

    private readonly StubApiClient client = new();
    private readonly SessionState session = new();

    public MeetingListPageTests()
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
    public void The_table_renders_the_four_columns_in_the_servers_order()
    {
        SignIn();
        client.MeetingPage = Page(
            Summary(FirstId, "Office move follow-up", new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero)),
            Summary(SecondId, "Equipment inventory", new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero), runs: 4, trackedActions: 9));

        IRenderedComponent<MeetingListPage> page = Render<MeetingListPage>();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("tbody tr").Count));

        Assert.Equal(
            [Voice.Title, Voice.Date, Voice.Runs, Voice.TrackedActions],
            page.FindAll("thead th").Select(cell => cell.TextContent.Trim()));

        // UX-DR12 — the order is the server's. Nothing here re-sorts the page it was handed.
        // The two counts differ, so Runs and Tracked Actions cannot be swapped without this
        // failing — they are 0 and 0 on every real Meeting until Stories 2.5 and 3.1.
        IReadOnlyList<IElement> rows = page.FindAll("tbody tr");
        Assert.Equal(
            ["Office move follow-up", "2026-09-21", "3", "7"],
            rows[0].QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()));
        Assert.Equal(
            ["Equipment inventory", "2026-09-14", "4", "9"],
            rows[1].QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()));
    }

    [Fact]
    public void The_first_load_asks_for_page_one_at_the_documented_page_size()
    {
        SignIn();

        IRenderedComponent<MeetingListPage> page = Render<MeetingListPage>();

        page.WaitForAssertion(() => Assert.Equal(1, client.ListMeetingsCalls));
        Assert.Equal(1, client.LastMeetingsPage);
        Assert.Equal(MeetingListPage.PageSize, client.LastMeetingsPageSize);
    }

    [Fact]
    public void The_pager_requests_page_two_as_the_api_numbers_it()
    {
        // MudBlazor's TableState.Page is 0-based and the API's page is 1-based. This is the one
        // place that conversion happens, so it is the one place it can be off by one.
        SignIn();
        client.MeetingPage = Page(73, Summary(FirstId, "Office move follow-up", Today));

        IRenderedComponent<MeetingListPage> page = Render<MeetingListPage>();
        page.WaitForAssertion(() => Assert.Equal(1, client.ListMeetingsCalls));

        page.Find("button[aria-label='Next page']").Click();

        page.WaitForAssertion(() => Assert.Equal(2, client.ListMeetingsCalls));
        Assert.Equal(2, client.LastMeetingsPage);
        Assert.Equal(MeetingListPage.PageSize, client.LastMeetingsPageSize);
    }

    [Fact]
    public void A_zero_total_renders_the_empty_state_and_no_rows()
    {
        SignIn();
        client.MeetingPage = Page();

        IRenderedComponent<MeetingListPage> page = Render<MeetingListPage>();

        page.WaitForAssertion(() => Assert.Contains(Voice.NoMeetings, page.Markup, StringComparison.Ordinal));
        Assert.Empty(page.FindAll("tbody tr td.mud-table-cell"));

        // EXPERIENCE.md's "No Meetings" row asks for a primary New meeting button beside it.
        Assert.Equal(Voice.NewMeeting, page.Find($"#{MeetingListPage.NewMeetingEmptyId}").TextContent.Trim());

        // It carries the same accessible name as the header's button, so the notice is what tells
        // a screen-reader user which is which.
        Assert.Equal(
            MeetingListPage.NoMeetingsId,
            page.Find($"#{MeetingListPage.NewMeetingEmptyId}").GetAttribute("aria-describedby"));
    }

    [Fact]
    public async Task The_empty_states_button_opens_the_same_dialog_the_header_does()
    {
        // Reading the button proves it renders; only clicking it proves it is wired to anything.
        SignIn();
        client.MeetingPage = Page();

        IRenderedComponent<MudDialogProvider> provider = Render<MudDialogProvider>();
        IRenderedComponent<MeetingListPage> page = Render<MeetingListPage>();
        page.WaitForAssertion(() => Assert.Contains(Voice.NoMeetings, page.Markup, StringComparison.Ordinal));

        Task opened = page.Find($"#{MeetingListPage.NewMeetingEmptyId}").ClickAsync(new MouseEventArgs());

        provider.WaitForElement($"#{NewMeetingDialog.CancelId}").Click();

        await opened;

        Assert.Equal(0, client.CreateMeetingCalls);
    }

    [Fact]
    public void A_failed_load_renders_the_notice_and_retry_re_requests_the_same_page()
    {
        SignIn();
        client.ListMeetingsThrows = StubApiClient.Bare(500);

        IRenderedComponent<MeetingListPage> page = Render<MeetingListPage>();

        page.WaitForAssertion(() => Assert.Contains(
            Voice.LoadFailurePrefix + Voice.UnexpectedFailureTitle,
            page.Markup,
            StringComparison.Ordinal));

        // A failed load leaves the body empty too, so the empty state must not claim there are
        // no meetings over a 500.
        Assert.DoesNotContain(Voice.NoMeetings, page.Markup, StringComparison.Ordinal);

        client.ListMeetingsThrows = null;
        client.MeetingPage = Page(Summary(FirstId, "Office move follow-up", Today));

        page.Find($"#{LoadFailure.RetryId}").Click();

        page.WaitForAssertion(() => Assert.Equal(2, client.ListMeetingsCalls));
        page.WaitForAssertion(() => Assert.Single(page.FindAll("tbody tr")));
        Assert.DoesNotContain(Voice.LoadFailurePrefix, page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reload_over_an_outstanding_load_lands_on_the_superseding_page()
    {
        // MudTable cancels the outstanding request before starting the next one, so the first
        // call abandons its wait and the second is the one that owns the result. What is pinned
        // here is the end state a user sees: the newer page's rows, no failure notice, and
        // nothing left on the renderer.
        //
        // It does not isolate LoadAsync's OperationCanceledException arm — nothing can; see the
        // comment there for why the renderer swallows that escape on its own.
        TaskCompletionSource gate = new();
        client.ListMeetingsGate = gate.Task;
        client.MeetingPage = Page(Summary(FirstId, "Office move follow-up", Today));

        SignIn();

        IRenderedComponent<MeetingListPage> page = Render<MeetingListPage>();
        page.WaitForAssertion(() => Assert.Equal(1, client.ListMeetingsCalls));

        IRenderedComponent<MudTable<MeetingListItem>> table = page.FindComponent<MudTable<MeetingListItem>>();
        Task reloaded = table.InvokeAsync(table.Instance.ReloadServerData);

        // The second call cancels the first, which abandons its wait on the gate.
        page.WaitForAssertion(() => Assert.Equal(2, client.ListMeetingsCalls));

        gate.SetResult();
        await reloaded;

        // MudTable starts the first load itself, from OnAfterRenderAsync, so nothing this test
        // awaits carries its outcome. bUnit parks an unhandled component exception here, which
        // is the only place a failure on that path could still show up.
        Assert.False(Renderer.UnhandledException.IsCompleted);

        // The cancelled call says nothing: the superseding one owns both the rows and the notice.
        page.WaitForAssertion(() => Assert.Single(page.FindAll("tbody tr")));
        Assert.DoesNotContain(Voice.LoadFailurePrefix, page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_title_cell_is_a_link_a_keyboard_user_can_open()
    {
        // MudTable renders its own <tr> and splats nothing onto it, so the keyboard half of
        // "row click opens detail" is a real anchor: focusable, Enter-activated, and announced.
        SignIn();
        client.MeetingPage = Page(Summary(FirstId, "Office move follow-up", Today));

        IRenderedComponent<MeetingListPage> page = Render<MeetingListPage>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("tbody tr")));

        IElement link = page.Find("tbody tr td a");
        Assert.Equal("/meetings/" + FirstId, link.GetAttribute("href"));
        Assert.Equal("Office move follow-up", link.TextContent.Trim());
    }

    [Fact]
    public void Clicking_the_title_link_navigates_exactly_once()
    {
        // The anchor sits inside the row, so without stopPropagation the row handler navigates
        // too and Back lands on the page the user is already looking at.
        SignIn();
        client.MeetingPage = Page(Summary(FirstId, "Office move follow-up", Today));

        IRenderedComponent<MeetingListPage> page = Render<MeetingListPage>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("tbody tr")));

        page.Find("tbody tr td a").Click();

        Assert.Equal("/meetings/" + FirstId, Assert.Single(Navigation.History).Uri);
    }

    [Fact]
    public void A_row_click_opens_the_meeting()
    {
        SignIn();
        client.MeetingPage = Page(Summary(FirstId, "Office move follow-up", Today));

        IRenderedComponent<MeetingListPage> page = Render<MeetingListPage>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("tbody tr")));

        page.Find("tbody tr").Click();

        Assert.Equal("/meetings/" + FirstId, Navigation.History.First().Uri);
    }

    [Fact]
    public async Task Creating_a_meeting_in_the_dialog_lands_on_its_detail_page()
    {
        SignIn();
        client.MeetingCreated = new MeetingCreatedDto { Id = SecondId, CreatedByUserId = Guid.Empty };

        IRenderedComponent<MudDialogProvider> provider = Render<MudDialogProvider>();
        IRenderedComponent<MeetingListPage> page = Render<MeetingListPage>();
        page.WaitForAssertion(() => Assert.Equal(1, client.ListMeetingsCalls));

        // Not awaited: the handler stays parked on the dialog's result until it is closed.
        Task opened = page.Find($"#{MeetingListPage.NewMeetingId}").ClickAsync(new MouseEventArgs());

        provider.WaitForElement($"#{NewMeetingDialog.TitleId}").Input("Office move follow-up");

        // EXPERIENCE.md bans modal stacks deeper than one, so the control that opened the dialog
        // is unavailable while it is up.
        Assert.True(page.Find($"#{MeetingListPage.NewMeetingId}").HasAttribute("disabled"));

        await SetDateAsync(provider, new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Unspecified));
        provider.Find($"#{NewMeetingDialog.CreateId}").Click();

        await opened;

        Assert.Equal(1, client.CreateMeetingCalls);
        Assert.Equal("/meetings/" + SecondId, Navigation.History.First().Uri);
    }

    [Fact]
    public async Task Cancelling_the_dialog_creates_nothing_and_goes_nowhere()
    {
        SignIn();

        IRenderedComponent<MudDialogProvider> provider = Render<MudDialogProvider>();
        IRenderedComponent<MeetingListPage> page = Render<MeetingListPage>();
        page.WaitForAssertion(() => Assert.Equal(1, client.ListMeetingsCalls));

        Task opened = page.Find($"#{MeetingListPage.NewMeetingId}").ClickAsync(new MouseEventArgs());

        provider.WaitForElement($"#{NewMeetingDialog.CancelId}").Click();

        await opened;

        Assert.Equal(0, client.CreateMeetingCalls);
        Assert.Empty(Navigation.History);
    }

    [Fact]
    public void An_unauthenticated_visitor_is_sent_to_login_with_the_attempted_route_captured()
    {
        Navigation.NavigateTo("/meetings");

        IRenderedComponent<MeetingListPage> page = Render<MeetingListPage>();

        Assert.Equal("/login", Navigation.History.First().Uri);
        Assert.Equal("/meetings", session.TakeAttemptedRoute());

        // A navigation is not an unmount. Nothing renders, and nothing is requested either.
        Assert.Empty(page.FindAll("table"));
        Assert.Equal(0, client.ListMeetingsCalls);
    }

    private static DateTimeOffset Today => new(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);

    private BunitNavigationManager Navigation =>
        (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

    private void SignIn() =>
        session.SignIn(new SignedInUser("jwt", DateTimeOffset.UnixEpoch, Guid.Empty, "Dana Whitfield", RoleNames.ActionOfficer));

    /// <summary>
    /// MudDatePicker's calendar is a popover driven by JS interop, which the loose runtime does
    /// not operate. Raising the bound callback is the same thing the calendar does, and it is the
    /// binding — not the popover — that this file is here to check.
    /// </summary>
    private static Task SetDateAsync(IRenderedComponent<MudDialogProvider> provider, DateTime date)
    {
        IRenderedComponent<MudDatePicker> picker = provider.FindComponent<MudDatePicker>();

        return picker.InvokeAsync(() => picker.Instance.DateChanged.InvokeAsync(date));
    }

    private static PagedResultOfMeetingSummaryDto Page(params MeetingSummaryDto[] items) =>
        Page(items.Length, items);

    private static PagedResultOfMeetingSummaryDto Page(int total, params MeetingSummaryDto[] items) => new()
    {
        Items = items,
        Page = 1,
        PageSize = MeetingListPage.PageSize,
        Total = total,
    };

    /// <summary>
    /// The two counts are distinct and non-zero on purpose. Every real Meeting carries 0 and 0
    /// until Stories 2.5 and 3.1 land, and against 0 and 0 the two columns — and the two
    /// arguments <c>MeetingsService.ToListItem</c> passes — are interchangeable.
    /// </summary>
    private static MeetingSummaryDto Summary(
        Guid id,
        string title,
        DateTimeOffset date,
        int runs = 3,
        int trackedActions = 7) => new()
    {
        Id = id,
        Title = title,
        MeetingDate = date,
        RunCount = runs,
        TrackedActionCount = trackedActions,
    };
}
