using ActionLedger.Web.Core;
using ActionLedger.Web.Core.Api;
using ActionLedger.Web.Core.Auth;
using ActionLedger.Web.Core.Shell;
using ActionLedger.Web.Core.Users;
using ActionLedger.Web.Features.Review;
using ActionLedger.Web.Features.Review.Data;
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
using ApiReviewState = ActionLedger.Web.Core.Api.ReviewState;

namespace ActionLedger.Web.Tests;

/// <summary>
/// FR-10, FR-14, UX-DR3/4/8 — the Review Screen: two panes from 1200px and a collapsed notes panel
/// below, cards in AI order, one Source Excerpt highlight that follows hover, focus and a quote
/// click, the Pending counter, the zero-proposal state, and the not-found and failure branches.
/// </summary>
public sealed class ReviewPageTests : BunitContext
{
    private static readonly Guid MeetingId = Guid.Parse("01999999-0000-7000-8000-00000000beef");

    private static readonly Guid RunId = Guid.Parse("01999999-0000-7000-8000-0000000000aa");

    private static readonly Guid NewRunId = Guid.Parse("01999999-0000-7000-8000-0000000000bb");

    private const string NotesText = "A.\nDana will call Bob.\nPriya will book the range.";

    private readonly StubApiClient client = new();
    private readonly SessionState session = new();
    private readonly StubScrollManager scroll = new();
    private readonly StubViewport viewport = new();

    public ReviewPageTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton<IActionLedgerApiClient>(client);
        Services.AddSingleton(session);
        Services.AddSingleton(new LoadingState());
        Services.AddSingleton<UserDirectory>();
        Services.AddSingleton<ReviewService>();
        Services.AddSingleton<IScrollManager>(scroll);
        Services.AddSingleton<IBrowserViewportService>(viewport);

        client.Run = Run(proposals:
        [
            Proposal(0, "Call Bob.", "Dana will call Bob.", start: 3, length: 18),
            Proposal(1, "Book the range.", "Priya will book the range.", start: 23, length: 25),
        ]);
        client.Meeting = Meeting();
        client.StartedRun = new RunDto { Id = NewRunId, Outcome = ExtractionOutcome.Succeeded };
    }

    // ---------------------------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void At_1200px_and_wider_the_notes_sit_left_of_the_cards_in_ai_order()
    {
        viewport.Width = 1280;
        SignIn();

        IRenderedComponent<ReviewPage> page = RenderReview();

        Assert.Equal(Voice.ReviewProposals, page.Find("h1").TextContent);

        IElement panes = page.Find($"#{ReviewPage.PanesId}");
        IElement[] children = [.. panes.Children];

        Assert.Equal([ReviewPage.NotesPaneId, ReviewPage.CardsId], children.Select(child => child.Id));

        // The notes, verbatim, in the pre-wrap block.
        IElement notes = page.Find($"#{ReviewPage.NotesPaneId} .al-notes");
        Assert.Equal(NotesText, notes.TextContent);

        Assert.Equal(["Call Bob.", "Book the range."], Cards(page).Select(card => card.QuerySelector(".al-proposal-description")!.TextContent));
        Assert.Empty(page.FindAll($"#{ReviewPage.NotesPanelId}"));
    }

    [Fact]
    public void An_unknown_width_is_treated_as_wide()
    {
        viewport.Width = null;
        SignIn();

        IRenderedComponent<ReviewPage> page = RenderReview();

        Assert.NotNull(page.Find($"#{ReviewPage.PanesId}"));
    }

    [Fact]
    public void From_1024_to_1199px_the_notes_collapse_into_a_panel_above_the_cards()
    {
        viewport.Width = 1100;
        SignIn();

        IRenderedComponent<ReviewPage> page = RenderReview();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll($"#{ReviewPage.PanesId}")));

        IElement panel = page.Find($"#{ReviewPage.NotesPanelId}");

        Assert.Contains(Voice.Notes, panel.TextContent, StringComparison.Ordinal);

        // Collapsed until the user opens it.
        Assert.DoesNotContain("mud-panel-expanded", panel.ClassList);

        // Above the cards.
        Assert.Equal(
            [ReviewPage.NotesPanelId, ReviewPage.CardsId],
            page.FindAll($"#{ReviewPage.NotesPanelId}, #{ReviewPage.CardsId}").Select(element => element.Id));
        Assert.Equal(2, Cards(page).Length);
    }

    [Fact]
    public async Task A_resize_across_1200px_switches_the_layout_both_ways()
    {
        viewport.Width = 1280;
        SignIn();

        IRenderedComponent<ReviewPage> page = RenderReview();

        Assert.NotNull(page.Find($"#{ReviewPage.PanesId}"));

        // 1200px is not a MudBlazor breakpoint, so every resize has to be asked for.
        Assert.False(viewport.SubscribedOptions?.NotifyOnBreakpointOnly);

        await Resize(page, 1100);

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll($"#{ReviewPage.PanesId}"));
            Assert.NotNull(page.Find($"#{ReviewPage.NotesPanelId}"));
        });

        await Resize(page, 1280);

        page.WaitForAssertion(() =>
        {
            Assert.NotNull(page.Find($"#{ReviewPage.PanesId}"));
            Assert.Empty(page.FindAll($"#{ReviewPage.NotesPanelId}"));
        });
    }

    [Fact]
    public void In_the_narrow_layout_a_collapsed_panel_still_marks_the_span_but_nothing_scrolls()
    {
        viewport.Width = 1100;
        SignIn();

        IRenderedComponent<ReviewPage> page = RenderReview();
        page.WaitForAssertion(() => Assert.NotNull(page.Find($"#{ReviewPage.NotesPanelId}")));

        Cards(page)[0].TriggerEvent("onmouseenter", new MouseEventArgs());

        page.WaitForAssertion(() =>
        {
            Assert.Equal("Dana will call Bob", Assert.Single(page.FindAll("mark")).TextContent);
            Assert.Equal(NotesPane.HighlightId, Cards(page)[0].GetAttribute("aria-describedby"));
        });

        // Scrolling to a hidden mark would move the page instead.
        Assert.Empty(scroll.Selectors);
    }

    [Fact]
    public void In_the_narrow_layout_an_open_panel_scrolls_to_the_span()
    {
        viewport.Width = 1100;
        SignIn();

        IRenderedComponent<ReviewPage> page = RenderReview();
        page.WaitForAssertion(() => Assert.NotNull(page.Find($"#{ReviewPage.NotesPanelId}")));

        page.Find($"#{ReviewPage.NotesPanelId} .mud-expand-panel-header").Click();

        Cards(page)[1].TriggerEvent("onmouseenter", new MouseEventArgs());

        page.WaitForAssertion(() => Assert.Equal([$"#{NotesPane.HighlightId}"], scroll.Selectors));
    }

    [Fact]
    public void In_the_narrow_layout_opening_the_panel_scrolls_to_the_active_span()
    {
        viewport.Width = 1100;
        SignIn();

        IRenderedComponent<ReviewPage> page = RenderReview();
        page.WaitForAssertion(() => Assert.NotNull(page.Find($"#{ReviewPage.NotesPanelId}")));

        Cards(page)[0].TriggerEvent("onmouseenter", new MouseEventArgs());
        page.WaitForAssertion(() => Assert.Single(page.FindAll("mark")));
        Assert.Empty(scroll.Selectors);

        page.Find($"#{ReviewPage.NotesPanelId} .mud-expand-panel-header").Click();

        page.WaitForAssertion(() => Assert.Equal([$"#{NotesPane.HighlightId}"], scroll.Selectors));
    }

    [Fact]
    public void In_the_narrow_layout_opening_the_panel_with_no_active_card_scrolls_nothing()
    {
        viewport.Width = 1100;
        SignIn();

        IRenderedComponent<ReviewPage> page = RenderReview();
        page.WaitForAssertion(() => Assert.NotNull(page.Find($"#{ReviewPage.NotesPanelId}")));

        page.Find($"#{ReviewPage.NotesPanelId} .mud-expand-panel-header").Click();

        page.WaitForAssertion(() => Assert.Contains("mud-panel-expanded", page.Find($"#{ReviewPage.NotesPanelId}").ClassList));
        Assert.Empty(scroll.Selectors);
    }

    [Fact]
    public async Task Disposing_the_page_unsubscribes_its_viewport_observer()
    {
        SignIn();

        IRenderedComponent<ReviewPage> page = RenderReview();
        page.WaitForAssertion(() => Assert.NotNull(viewport.SubscribedObserverId));

        await page.Instance.DisposeAsync();

        Assert.Equal([viewport.SubscribedObserverId!.Value], viewport.UnsubscribedObserverIds);
    }

    [Fact]
    public void Fifty_proposals_render_in_the_order_received()
    {
        SignIn();
        client.Run = Run(proposals:
        [
            .. Enumerable.Range(0, 50).Select(ordinal =>
                Proposal(ordinal, $"Proposal {ordinal}.", "Dana will call Bob.", start: 3, length: 18)),
        ]);

        IRenderedComponent<ReviewPage> page = RenderReview();

        Assert.Equal(
            Enumerable.Range(0, 50).Select(ordinal => $"Proposal {ordinal}."),
            Cards(page).Select(card => card.QuerySelector(".al-proposal-description")!.TextContent));
        Assert.Equal("50 proposals pending", page.Find($"#{PendingCounter.CounterId}").TextContent);
    }

    [Fact]
    public void The_meta_line_names_the_meeting_the_start_the_provider_the_model_and_the_prompt_version()
    {
        SignIn();

        IRenderedComponent<ReviewPage> page = RenderReview();

        IElement meeting = page.Find($"#{ReviewPage.MeetingLinkId}");
        Assert.Equal("Office move planning", meeting.TextContent);
        Assert.Equal($"/meetings/{MeetingId}", meeting.GetAttribute("href"));

        Assert.Equal("2026-09-22 09:15 UTC", page.Find($"#{ReviewPage.StartedId}").TextContent);
        Assert.Equal("Fake", page.Find($"#{ReviewPage.ProviderId}").TextContent);
        Assert.Equal("fixture-catalog", page.Find($"#{ReviewPage.ModelId}").TextContent);
        Assert.Equal("v1", page.Find($"#{ReviewPage.PromptVersionId}").TextContent);

        IElement back = page.Find($"#{ReviewPage.BackToMeetingId}");
        Assert.Equal(Voice.BackToMeeting, back.TextContent);
        Assert.Equal($"/meetings/{MeetingId}", back.GetAttribute("href"));
    }

    // ---------------------------------------------------------------------------------------
    // The highlight
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Nothing_is_highlighted_before_a_card_is_activated()
    {
        SignIn();

        IRenderedComponent<ReviewPage> page = RenderReview();

        Assert.Empty(page.FindAll("mark"));
        Assert.All(Cards(page), card => Assert.Null(card.GetAttribute("aria-describedby")));
        Assert.Empty(scroll.Selectors);
    }

    [Fact]
    public void Hovering_a_card_highlights_its_span_describes_the_card_and_scrolls_to_it()
    {
        SignIn();

        IRenderedComponent<ReviewPage> page = RenderReview();

        Cards(page)[0].TriggerEvent("onmouseenter", new MouseEventArgs());

        AssertHighlighted(page, "Dana will call Bob", activeIndex: 0);
        Assert.Equal([$"#{NotesPane.HighlightId}"], scroll.Selectors);

        // Moving to another card moves the one highlight.
        Cards(page)[1].TriggerEvent("onmouseenter", new MouseEventArgs());

        AssertHighlighted(page, "Priya will book the range", activeIndex: 1);
        Assert.Equal(2, scroll.Selectors.Count);
    }

    [Fact]
    public void Focusing_inside_a_card_activates_it()
    {
        SignIn();

        IRenderedComponent<ReviewPage> page = RenderReview();

        Cards(page)[1].TriggerEvent("onfocusin", new FocusEventArgs());

        AssertHighlighted(page, "Priya will book the range", activeIndex: 1);
        Assert.Single(scroll.Selectors);
    }

    [Fact]
    public void Clicking_a_cards_quote_activates_it()
    {
        SignIn();

        IRenderedComponent<ReviewPage> page = RenderReview();

        Cards(page)[1].QuerySelector("blockquote")!.Click();

        AssertHighlighted(page, "Priya will book the range", activeIndex: 1);
    }

    [Fact]
    public void Activating_the_active_card_again_does_nothing()
    {
        SignIn();

        IRenderedComponent<ReviewPage> page = RenderReview();

        Cards(page)[0].TriggerEvent("onmouseenter", new MouseEventArgs());
        Cards(page)[0].TriggerEvent("onfocusin", new FocusEventArgs());
        Cards(page)[0].TriggerEvent("onmouseenter", new MouseEventArgs());

        AssertHighlighted(page, "Dana will call Bob", activeIndex: 0);
        Assert.Single(scroll.Selectors);
    }

    [Fact]
    public void A_card_whose_excerpt_was_not_located_gives_no_mark_no_description_and_no_scroll()
    {
        SignIn();
        client.Run = Run(proposals:
        [
            Proposal(0, "Call Bob.", "Dana will call Bob.", start: 3, length: 18),
            Proposal(1, "Seeded.", "Nobody said this.", start: null, length: null),
        ]);

        IRenderedComponent<ReviewPage> page = RenderReview();

        Cards(page)[0].TriggerEvent("onmouseenter", new MouseEventArgs());
        Cards(page)[1].TriggerEvent("onmouseenter", new MouseEventArgs());

        Assert.Empty(page.FindAll("mark"));
        Assert.All(Cards(page), card => Assert.Null(card.GetAttribute("aria-describedby")));
        Assert.Single(scroll.Selectors);

        // The card itself still renders in full.
        Assert.Equal("Nobody said this.", Cards(page)[1].QuerySelector("blockquote")!.TextContent);
    }

    [Fact]
    public void Every_card_is_reachable_by_the_keyboard()
    {
        SignIn();

        IRenderedComponent<ReviewPage> page = RenderReview();

        Assert.All(Cards(page), card =>
        {
            Assert.Equal("ARTICLE", card.TagName);
            Assert.Equal("0", card.GetAttribute("tabindex"));
        });
    }

    // ---------------------------------------------------------------------------------------
    // The counter and decided cards
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void The_counter_counts_only_pending_proposals()
    {
        SignIn();

        ProposedActionDto approved = Proposal(1, "Book the range.", "Priya will book the range.", start: 23, length: 25);
        approved.ReviewState = ApiReviewState.Approved;
        approved.DecidedByDisplayName = "Dana Whitfield";
        approved.DecidedAt = new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);
        approved.TrackedActionId = Guid.CreateVersion7();
        approved.DecidedDescription = "Book the range.";

        client.Run = Run(proposals:
        [
            Proposal(0, "Call Bob.", "Dana will call Bob.", start: 3, length: 18),
            approved,
            Proposal(2, "Order scanners.", "Dana will call Bob.", start: 3, length: 18),
        ]);

        IRenderedComponent<ReviewPage> page = RenderReview();

        IElement counter = page.Find($"#{PendingCounter.CounterId}");

        Assert.Equal("2 proposals pending", counter.TextContent);
        Assert.Equal("polite", counter.Closest("[aria-live]")?.GetAttribute("aria-live"));
        Assert.Empty(page.FindAll($"#{PendingCounter.ViewActionsId}"));

        // The decided card keeps its place, between the two Pending ones.
        Assert.Equal(
            [Voice.Pending, Voice.Approved, Voice.Pending],
            Cards(page).Select(card => card.QuerySelector(".al-review-state")!.TextContent.Trim()));
    }

    [Fact]
    public void When_everything_is_decided_the_counter_links_to_this_meetings_actions()
    {
        SignIn();

        ProposedActionDto rejected = Proposal(0, "Call Bob.", "Dana will call Bob.", start: 3, length: 18);
        rejected.ReviewState = ApiReviewState.Rejected;
        rejected.DecidedByDisplayName = "Seed";
        rejected.DecidedAt = new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);

        client.Run = Run(proposals: [rejected]);

        IRenderedComponent<ReviewPage> page = RenderReview();

        Assert.Equal(Voice.AllProposalsDecided, page.Find($"#{PendingCounter.CounterId}").TextContent);
        Assert.Equal($"/actions?meetingId={MeetingId}", page.Find($"#{PendingCounter.ViewActionsId}").GetAttribute("href"));
        Assert.Equal(Voice.NoReasonGiven, page.Find(".al-rejection").TextContent);
    }

    [Fact]
    public void The_card_buttons_are_left_unbound_until_story_3_5()
    {
        SignIn();

        IRenderedComponent<ReviewPage> page = RenderReview();

        // Pressing them changes nothing and calls nothing: the decision POST is 3.5's.
        page.Find(".al-approve").Click();
        page.Find(".al-reject").Click();
        page.Find(".al-edit").Click();

        Assert.Equal(1, client.GetExtractionRunCalls);
        Assert.Equal(0, client.StartExtractionRunCalls);
    }

    // ---------------------------------------------------------------------------------------
    // Zero proposals
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void A_run_with_no_proposals_offers_run_again_and_back_to_meeting_and_no_counter()
    {
        SignIn();
        client.Run = Run();

        IRenderedComponent<ReviewPage> page = RenderReview();

        Assert.Equal(Voice.NoProposals, page.Find($"#{ReviewPage.NoProposalsId}").TextContent);
        Assert.Equal(Voice.RunAgain, page.Find($"#{ReviewPage.RunAgainId}").TextContent.Trim());
        Assert.Equal(Voice.BackToMeeting, page.Find($"#{ReviewPage.BackToMeetingId}").TextContent);
        Assert.Empty(page.FindAll($"#{PendingCounter.CounterId}"));
        Assert.Empty(page.FindAll($"#{ReviewPage.PanesId}"));
    }

    [Theory]
    [InlineData(ExtractionOutcome.Succeeded, "/review")]
    [InlineData(ExtractionOutcome.Failed, "")]
    public async Task Run_again_opens_the_new_runs_review_or_its_detail(ExtractionOutcome outcome, string suffix)
    {
        SignIn();
        client.Run = Run();
        client.StartedRun = new RunDto { Id = NewRunId, Outcome = outcome };

        IRenderedComponent<ReviewPage> page = RenderReview();

        await page.Find($"#{ReviewPage.RunAgainId}").ClickAsync(new MouseEventArgs());

        Assert.Equal(MeetingId, client.LastStartExtractionRunId);
        Assert.EndsWith($"/meetings/{MeetingId}/runs/{NewRunId}{suffix}", Navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_again_is_disabled_while_its_call_is_in_flight()
    {
        TaskCompletionSource gate = new();
        client.StartExtractionRunGate = gate.Task;

        SignIn();
        client.Run = Run();

        IRenderedComponent<ReviewPage> page = RenderReview();

        Task running = page.Find($"#{ReviewPage.RunAgainId}").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Assert.True(page.Find($"#{ReviewPage.RunAgainId}").HasAttribute("disabled")));
        page.Find($"#{ReviewPage.RunAgainId}").Click();

        Assert.Equal(1, client.StartExtractionRunCalls);

        gate.SetResult();
        await running;
    }

    [Fact]
    public async Task A_failed_run_again_raises_a_snackbar_with_retry_and_stays_put()
    {
        SignIn();
        client.Run = Run();
        client.StartExtractionRunThrows = StubApiClient.Bare(500);

        IRenderedComponent<MudSnackbarProvider> snackbars = Render<MudSnackbarProvider>();
        IRenderedComponent<ReviewPage> page = RenderReview();
        string before = Navigation.Uri;

        await page.Find($"#{ReviewPage.RunAgainId}").ClickAsync(new MouseEventArgs());

        snackbars.WaitForAssertion(() =>
            Assert.Contains(Voice.UnexpectedFailureTitle, snackbars.Markup, StringComparison.Ordinal));
        Assert.Equal(before, Navigation.Uri);
        Assert.False(page.Find($"#{ReviewPage.RunAgainId}").HasAttribute("disabled"));
    }

    // ---------------------------------------------------------------------------------------
    // Not found, failure, and the Failed run
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void An_unknown_run_is_not_found_linking_to_the_meeting()
    {
        SignIn();
        client.GetExtractionRunThrows = StubApiClient.Problem(404, "The resource was not found.");

        IRenderedComponent<ReviewPage> page = RenderReview();

        Assert.Equal(Voice.NotFound, page.Find("h1").TextContent);
        Assert.Equal($"/meetings/{MeetingId}", page.Find("a").GetAttribute("href"));
    }

    [Fact]
    public void An_unknown_meeting_is_not_found()
    {
        SignIn();
        client.GetMeetingThrows = StubApiClient.Problem(404, "The resource was not found.");

        IRenderedComponent<ReviewPage> page = RenderReview();

        Assert.Equal(Voice.NotFound, page.Find("h1").TextContent);
        Assert.Empty(page.FindAll("article"));
    }

    [Fact]
    public void A_run_of_another_meeting_is_not_found_at_this_address()
    {
        SignIn();
        client.Run = Run(meetingId: Guid.CreateVersion7(), proposals: [Proposal(0, "Call Bob.", "Dana will call Bob.", 3, 18)]);

        IRenderedComponent<ReviewPage> page = RenderReview();

        Assert.Equal(Voice.NotFound, page.Find("h1").TextContent);
        Assert.Empty(page.FindAll("article"));
    }

    [Fact]
    public void A_failed_run_replaces_this_address_with_its_run_detail()
    {
        SignIn();
        client.Run = Run(outcome: ExtractionOutcome.Failed);

        RenderReview();

        Assert.EndsWith($"/meetings/{MeetingId}/runs/{RunId}", Navigation.Uri, StringComparison.Ordinal);
        Assert.True(Navigation.History.First().Options.ReplaceHistoryEntry);
    }

    [Fact]
    public void A_failed_load_renders_the_notice_under_the_heading_and_retry_re_reads()
    {
        SignIn();
        client.GetExtractionRunThrows = StubApiClient.Bare(500);

        IRenderedComponent<ReviewPage> page = RenderReview();

        Assert.Equal(Voice.ReviewProposals, page.Find("h1").TextContent);
        Assert.Contains(Voice.LoadFailurePrefix + Voice.UnexpectedFailureTitle, page.Markup, StringComparison.Ordinal);

        client.GetExtractionRunThrows = null;
        page.Find($"#{LoadFailure.RetryId}").Click();

        page.WaitForAssertion(() => Assert.Equal(2, Cards(page).Length));
    }

    [Fact]
    public void Arriving_at_another_run_on_the_same_component_reloads_it()
    {
        SignIn();

        IRenderedComponent<ReviewPage> page = RenderReview();

        client.Run = Run(id: NewRunId, proposals: [Proposal(0, "Another run's proposal.", "Dana will call Bob.", 3, 18)]);

        page.Render(parameters => parameters
            .Add(component => component.MeetingId, MeetingId)
            .Add(component => component.RunId, NewRunId));

        page.WaitForAssertion(() => Assert.Equal(
            "Another run's proposal.",
            Assert.Single(Cards(page)).QuerySelector(".al-proposal-description")!.TextContent));
    }

    [Fact]
    public void Arriving_at_another_meeting_with_the_same_run_id_reloads_and_rechecks_the_meeting()
    {
        SignIn();

        IRenderedComponent<ReviewPage> page = RenderReview();

        Assert.Equal(2, Cards(page).Length);

        page.Render(parameters => parameters
            .Add(component => component.MeetingId, Guid.CreateVersion7())
            .Add(component => component.RunId, RunId));

        // The run belongs to MeetingId, so under another meeting's address it is not found.
        page.WaitForAssertion(() => Assert.Equal(Voice.NotFound, page.Find("h1").TextContent));
        Assert.Equal(2, client.GetExtractionRunCalls);
    }

    [Fact]
    public void An_unauthenticated_visitor_is_sent_to_login_with_the_attempted_route_captured()
    {
        string route = $"/meetings/{MeetingId}/runs/{RunId}/review";
        Navigation.NavigateTo(route);

        RenderReview();

        Assert.Equal("/login", Navigation.History.First().Uri);
        Assert.Equal(route, session.TakeAttemptedRoute());
        Assert.Equal(0, client.GetExtractionRunCalls);
    }

    private static void AssertHighlighted(IRenderedComponent<ReviewPage> page, string text, int activeIndex)
    {
        page.WaitForAssertion(() =>
        {
            IElement mark = Assert.Single(page.FindAll("mark"));
            Assert.Equal(NotesPane.HighlightId, mark.Id);
            Assert.Equal(text, mark.TextContent);

            // The notes read the same with or without the mark.
            Assert.Equal(NotesText, page.Find(".al-notes").TextContent);

            IElement[] cards = Cards(page);

            for (int index = 0; index < cards.Length; index++)
            {
                Assert.Equal(
                    index == activeIndex ? NotesPane.HighlightId : null,
                    cards[index].GetAttribute("aria-describedby"));
            }
        });
    }

    private Task Resize(IRenderedComponent<ReviewPage> page, int width) =>
        page.InvokeAsync(() => viewport.Callback!(new BrowserViewportEventArgs(
            Guid.NewGuid(),
            new BrowserWindowSize { Width = width, Height = 900 },
            Breakpoint.Lg,
            isImmediate: false)));

    private BunitNavigationManager Navigation =>
        (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

    private IRenderedComponent<ReviewPage> RenderReview() =>
        Render<ReviewPage>(parameters => parameters
            .Add(component => component.MeetingId, MeetingId)
            .Add(component => component.RunId, RunId));

    private void SignIn() =>
        session.SignIn(new SignedInUser("jwt", DateTimeOffset.UnixEpoch, Guid.Empty, "Dana Whitfield", RoleNames.ActionOfficer));

    private static IElement[] Cards(IRenderedComponent<ReviewPage> page) =>
        [.. page.FindAll($"#{ReviewPage.CardsId} article")];

    private static MeetingDetailDto Meeting() => new()
    {
        Id = MeetingId,
        Title = "Office move planning",
        MeetingDate = new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero),
        Attendees = [],
        CreatedByUserId = Guid.Empty,
        CreatedAt = DateTimeOffset.UnixEpoch,
        Notes = new MeetingNotesDto
        {
            Id = Guid.Empty,
            Text = NotesText,
            Sha256 = new string('a', 64),
            SavedAt = DateTimeOffset.UnixEpoch,
        },
    };

    private static RunDetailDto Run(
        Guid? id = null,
        Guid? meetingId = null,
        ExtractionOutcome outcome = ExtractionOutcome.Succeeded,
        ProposedActionDto[]? proposals = null) => new()
    {
        Id = id ?? RunId,
        MeetingId = meetingId ?? MeetingId,
        MeetingNotesId = Guid.Empty,
        NotesSha256 = new string('a', 64),
        StartedByUserId = Guid.Empty,
        Provider = "Fake",
        Model = "fixture-catalog",
        PromptVersion = "v1",
        SchemaVersion = "1",
        StartedAt = new DateTimeOffset(2026, 9, 22, 9, 15, 42, TimeSpan.Zero),
        DurationMs = 87,
        InputTokens = 0,
        OutputTokens = 0,
        Outcome = outcome,
        FailureReason = outcome is ExtractionOutcome.Failed ? "The AI returned invalid output twice." : null,
        Warnings = [],
        Proposals = proposals ?? [],
    };

    private static ProposedActionDto Proposal(int ordinal, string description, string excerpt, int? start, int? length) => new()
    {
        Id = Guid.CreateVersion7(),
        Ordinal = ordinal,
        Description = description,
        SuggestedOwner = string.Empty,
        SuggestedDueDate = null,
        Confidence = 0.9,
        SourceExcerpt = excerpt,
        IsLowConfidence = false,
        SuggestedOwnerUserId = null,
        ReviewState = ApiReviewState.Pending,
        ExcerptStart = start,
        ExcerptLength = length,
    };

    /// <summary>Records every scroll the page asks for; scrolls nothing.</summary>
    private sealed class StubScrollManager : IScrollManager
    {
        public List<string> Selectors { get; } = [];

        public ValueTask ScrollIntoViewAsync(string? selector, ScrollBehavior behavior)
        {
            Selectors.Add(selector ?? string.Empty);

            return ValueTask.CompletedTask;
        }

        public ValueTask ScrollToAsync(string? id, int left, int top, ScrollBehavior scrollBehavior) => ValueTask.CompletedTask;

        public ValueTask ScrollToTopAsync(string? id, ScrollBehavior scrollBehavior = ScrollBehavior.Auto) => ValueTask.CompletedTask;

        public ValueTask ScrollToYearAsync(string elementId) => ValueTask.CompletedTask;

        public ValueTask ScrollToListItemAsync(string elementId) => ValueTask.CompletedTask;

        public ValueTask LockScrollAsync(string selector = "body", string cssClass = "scroll-locked") => ValueTask.CompletedTask;

        public ValueTask UnlockScrollAsync(string selector = "body", string cssClass = "scroll-locked") => ValueTask.CompletedTask;

        public ValueTask ScrollToBottomAsync(string elementId, ScrollBehavior scrollBehavior = ScrollBehavior.Auto) => ValueTask.CompletedTask;

        public ValueTask ScrollToVirtualizedItemAsync(string containerId, int index, double itemHeight, string itemSelector, ScrollBehavior behavior) =>
            ValueTask.CompletedTask;
    }

    /// <summary>A viewport of a fixed width; <c>null</c> is a width the browser did not report.</summary>
    private sealed class StubViewport : IBrowserViewportService
    {
        public int? Width { get; set; } = 1280;

        public ResizeOptions ResizeOptions { get; } = new();

        /// <summary>The page's resize callback, as it subscribed it.</summary>
        public Action<BrowserViewportEventArgs>? Callback { get; private set; }

        /// <summary>The options the page subscribed with.</summary>
        public ResizeOptions? SubscribedOptions { get; private set; }

        /// <summary>The observer id the page subscribed under.</summary>
        public Guid? SubscribedObserverId { get; private set; }

        /// <summary>Every observer id the page unsubscribed.</summary>
        public List<Guid> UnsubscribedObserverIds { get; } = [];

        public Task<BrowserWindowSize> GetCurrentBrowserWindowSizeAsync() =>
            Task.FromResult(Width is { } width ? new BrowserWindowSize { Width = width, Height = 900 } : null!);

        public Task SubscribeAsync(IBrowserViewportObserver observer, bool fireImmediately = true) => Task.CompletedTask;

        public Task SubscribeAsync(Guid observerId, Action<BrowserViewportEventArgs> lambda, ResizeOptions? options = null, bool fireImmediately = true)
        {
            Callback = lambda;
            SubscribedOptions = options;
            SubscribedObserverId = observerId;

            return Task.CompletedTask;
        }

        public Task SubscribeAsync(Guid observerId, Func<BrowserViewportEventArgs, Task> lambda, ResizeOptions? options = null, bool fireImmediately = true) =>
            Task.CompletedTask;

        public Task UnsubscribeAsync(IBrowserViewportObserver observer) => Task.CompletedTask;

        public Task UnsubscribeAsync(Guid observerId)
        {
            UnsubscribedObserverIds.Add(observerId);

            return Task.CompletedTask;
        }

        public Task<bool> IsMediaQueryMatchAsync(string mediaQuery) => Task.FromResult(false);

        public Task<bool> IsBreakpointWithinWindowSizeAsync(Breakpoint breakpoint) => Task.FromResult(false);

        public Task<bool> IsBreakpointWithinReferenceSizeAsync(Breakpoint breakpoint, Breakpoint reference) => Task.FromResult(false);

        public Task<Breakpoint> GetCurrentBreakpointAsync() => Task.FromResult(Breakpoint.Lg);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
