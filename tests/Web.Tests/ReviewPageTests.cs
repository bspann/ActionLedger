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
using MudBlazor.Extensions;
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

    // ---------------------------------------------------------------------------------------
    // Decisions
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void The_roster_loads_with_the_screen_and_reaches_the_owner_picker()
    {
        SignIn();
        client.Roster = Roster();

        Render<MudPopoverProvider>();
        IRenderedComponent<ReviewPage> page = RenderReview();

        Assert.Equal(1, client.ListUsersCalls);

        CardAt(page, 0).Find(".al-edit").Click();

        Assert.Equal(
            [null, Dana, Priya],
            page.FindComponents<MudSelectItem<Guid?>>().Select(item => item.Instance.GetState(x => x.Value)));
    }

    [Fact]
    public async Task A_plain_approve_sends_the_proposed_values_and_the_refreshed_card_is_approved()
    {
        SignIn();
        client.Roster = Roster();

        ProposedActionDto first = client.Run.Proposals.First();
        first.SuggestedOwner = "dana";
        first.SuggestedOwnerUserId = Dana;
        first.SuggestedOwnerDisplayName = "Dana Whitfield";
        first.SuggestedDueDate = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

        client.OnDecide = (id, _) => MarkDecided(id, ApiReviewState.Approved);

        IRenderedComponent<ReviewPage> page = RenderReview();

        Assert.Equal("2 proposals pending", Counter(page));

        await CardAt(page, 0).Find(".al-approve").ClickAsync(new MouseEventArgs());

        (Guid id, DecideProposalCommand command) = Assert.Single(client.Decisions);
        Assert.Equal(first.Id, id);
        Assert.Equal(ReviewVerb.Approve, command.Decision);
        Assert.Equal("Call Bob.", command.Description);
        Assert.Equal(Dana, command.OwnerUserId);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero), command.DueDate);
        Assert.Null(command.Reason);

        // One refresh, and the decided card in the same place.
        page.WaitForAssertion(() => Assert.Equal(2, client.GetExtractionRunCalls));
        Assert.Equal(
            [Voice.Approved, Voice.Pending],
            Cards(page).Select(card => card.QuerySelector(".al-review-state")!.TextContent.Trim()));
        Assert.Equal("Decided by Dana Whitfield", CardAt(page, 0).Find(".al-proposal-decision .al-provenance--human").TextContent.Trim());

        // The counter recounts inside its live region.
        IElement counter = page.Find($"#{PendingCounter.CounterId}");
        Assert.Equal(Voice.OneProposalPending, counter.TextContent);
        Assert.Equal("polite", counter.Closest("[aria-live]")?.GetAttribute("aria-live"));
    }

    [Fact]
    public async Task Approve_with_edits_sends_the_new_owner_and_a_cleared_date_and_the_card_is_edited()
    {
        SignIn();
        client.Roster = Roster();

        ProposedActionDto first = client.Run.Proposals.First();
        first.SuggestedOwnerUserId = Dana;
        first.SuggestedDueDate = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

        client.OnDecide = (id, _) => MarkDecided(id, ApiReviewState.Edited, dto =>
        {
            dto.DecidedOwnerUserId = Priya;
            dto.DecidedOwnerDisplayName = "Priya Ramaswamy";
        });

        Render<MudPopoverProvider>();
        IRenderedComponent<ReviewPage> page = RenderReview();

        CardAt(page, 0).Find(".al-edit").Click();

        await SetOwnerAsync(page, Priya);
        await SetDueAsync(page, null);

        IElement primary = CardAt(page, 0).Find(".al-approve-edits");
        Assert.Equal(Voice.ApproveWithEdits, primary.TextContent.Trim());

        await primary.ClickAsync(new MouseEventArgs());

        DecideProposalCommand command = Assert.Single(client.Decisions).Command;
        Assert.Equal(ReviewVerb.Approve, command.Decision);
        Assert.Equal("Call Bob.", command.Description);
        Assert.Equal(Priya, command.OwnerUserId);
        Assert.Null(command.DueDate);

        page.WaitForAssertion(() => Assert.Equal(Voice.Edited, CardAt(page, 0).Find(".al-review-state").TextContent.Trim()));
        Assert.NotNull(CardAt(page, 0).Find(".al-proposal-proposed"));
        Assert.Empty(CardAt(page, 0).FindAll("input, textarea"));
    }

    [Fact]
    public async Task Reject_opens_the_dialog_and_sends_the_trimmed_reason()
    {
        SignIn();
        client.OnDecide = (id, command) => MarkDecided(id, ApiReviewState.Rejected, dto => dto.RejectionReason = command.Reason);

        IRenderedComponent<MudDialogProvider> dialogs = Render<MudDialogProvider>();
        IRenderedComponent<ReviewPage> page = RenderReview();

        // Not awaited: the handler is parked on the dialog's result until it closes.
        Task rejecting = CardAt(page, 1).Find(".al-reject").ClickAsync(new MouseEventArgs());

        dialogs.WaitForElement($"#{RejectDialog.ConfirmId}");
        Assert.Contains(Voice.RejectDialogTitle, dialogs.Markup, StringComparison.Ordinal);
        Assert.Contains(Voice.RejectReasonLabel, dialogs.Markup, StringComparison.Ordinal);
        Assert.Equal(Voice.Reject, dialogs.Find($"#{RejectDialog.ConfirmId}").TextContent.Trim());
        Assert.Equal(Voice.Cancel, dialogs.Find($"#{RejectDialog.CancelId}").TextContent.Trim());
        Assert.Equal("TEXTAREA", dialogs.Find($"#{RejectDialog.ReasonId}").TagName);

        // Every card's writes are held while the dialog is open.
        Assert.All(page.FindAll(".al-proposal-actions button"), button => Assert.True(button.HasAttribute("disabled")));

        dialogs.Find($"#{RejectDialog.ReasonId}").Input("  dup  ");
        dialogs.Find($"#{RejectDialog.ConfirmId}").Click();
        await rejecting;

        (Guid id, DecideProposalCommand command) = Assert.Single(client.Decisions);
        Assert.Equal(client.Run.Proposals.ElementAt(1).Id, id);
        Assert.Equal(ReviewVerb.Reject, command.Decision);
        Assert.Equal("dup", command.Reason);

        page.WaitForAssertion(() => Assert.Equal("Reason: dup", CardAt(page, 1).Find(".al-rejection").TextContent));
        Assert.Equal(Voice.OneProposalPending, Counter(page));
    }

    [Fact]
    public async Task A_dismissed_reject_dialog_sends_nothing_and_hands_the_buttons_back()
    {
        SignIn();

        IRenderedComponent<MudDialogProvider> dialogs = Render<MudDialogProvider>();
        IRenderedComponent<ReviewPage> page = RenderReview();

        Task rejecting = CardAt(page, 0).Find(".al-reject").ClickAsync(new MouseEventArgs());

        dialogs.WaitForElement($"#{RejectDialog.CancelId}").Click();
        await rejecting;

        Assert.Empty(client.Decisions);
        Assert.Equal(1, client.GetExtractionRunCalls);
        page.WaitForAssertion(() =>
            Assert.All(page.FindAll(".al-proposal-actions button"), button => Assert.False(button.HasAttribute("disabled"))));
    }

    [Fact]
    public async Task Every_cards_writes_are_disabled_while_a_decision_is_in_flight()
    {
        TaskCompletionSource gate = new();
        client.DecideGate = gate.Task;
        client.OnDecide = (id, _) => MarkDecided(id, ApiReviewState.Approved);

        SignIn();

        IRenderedComponent<ReviewPage> page = RenderReview();

        Task approving = CardAt(page, 0).Find(".al-approve").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() =>
            Assert.All(page.FindAll(".al-proposal-actions button"), button => Assert.True(button.HasAttribute("disabled"))));
        Assert.Equal(6, page.FindAll(".al-proposal-actions button").Count);

        // A second decision, on either card, goes nowhere.
        CardAt(page, 1).Find(".al-approve").Click();
        CardAt(page, 0).Find(".al-approve").Click();

        Assert.Single(client.Decisions);

        gate.SetResult();
        await approving;

        page.WaitForAssertion(() => Assert.Equal(Voice.Approved, CardAt(page, 0).Find(".al-review-state").TextContent.Trim()));
        Assert.All(CardAt(page, 1).FindAll(".al-proposal-actions button"), button => Assert.False(button.HasAttribute("disabled")));
    }

    [Fact]
    public async Task A_conflict_refreshes_the_run_and_offers_no_retry()
    {
        SignIn();
        client.DecideThrows = StubApiClient.Problem(409, "Conflict.");

        IRenderedComponent<MudSnackbarProvider> snackbars = Render<MudSnackbarProvider>();
        IRenderedComponent<ReviewPage> page = RenderReview();

        // Another reviewer decided it first; the refresh brings their decision.
        MarkDecided(client.Run.Proposals.First().Id, ApiReviewState.Rejected);

        await CardAt(page, 0).Find(".al-approve").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Assert.Equal(Voice.Rejected, CardAt(page, 0).Find(".al-review-state").TextContent.Trim()));
        Assert.Equal(2, client.GetExtractionRunCalls);

        // SessionMessageHandler raises "Already changed. Reloading."; the page adds nothing.
        Assert.DoesNotContain("Conflict.", snackbars.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(snackbars.FindAll("button"), IsRetry);
    }

    [Fact]
    public async Task A_forbidden_decision_keeps_that_cards_writes_disabled_and_offers_no_retry()
    {
        SignIn();
        client.DecideThrows = StubApiClient.Problem(403, "Forbidden.");

        IRenderedComponent<MudSnackbarProvider> snackbars = Render<MudSnackbarProvider>();
        IRenderedComponent<ReviewPage> page = RenderReview();

        await CardAt(page, 0).Find(".al-approve").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() =>
            Assert.All(CardAt(page, 0).FindAll(".al-proposal-actions button"), button => Assert.True(button.HasAttribute("disabled"))));

        // Only that card: the other one is still decidable.
        Assert.All(CardAt(page, 1).FindAll(".al-proposal-actions button"), button => Assert.False(button.HasAttribute("disabled")));

        // Pressing it again sends nothing, and nothing was refreshed.
        CardAt(page, 0).Find(".al-approve").Click();
        Assert.Single(client.Decisions);
        Assert.Equal(1, client.GetExtractionRunCalls);

        Assert.DoesNotContain("Forbidden.", snackbars.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(snackbars.FindAll("button"), IsRetry);
    }

    [Fact]
    public async Task An_unauthorized_decision_adds_no_snackbar_and_no_refresh()
    {
        SignIn();
        client.DecideThrows = StubApiClient.Problem(401, "Unauthorized.");

        IRenderedComponent<MudSnackbarProvider> snackbars = Render<MudSnackbarProvider>();
        IRenderedComponent<ReviewPage> page = RenderReview();

        await CardAt(page, 0).Find(".al-approve").ClickAsync(new MouseEventArgs());

        // SessionMessageHandler signs out and redirects; the page adds nothing of its own.
        Assert.Single(client.Decisions);
        Assert.Equal(1, client.GetExtractionRunCalls);
        Assert.DoesNotContain("Unauthorized.", snackbars.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(snackbars.FindAll("button"), IsRetry);
    }

    [Fact]
    public async Task An_unauthorized_refresh_after_a_decision_offers_no_retry()
    {
        SignIn();
        client.OnDecide = (id, _) =>
        {
            MarkDecided(id, ApiReviewState.Approved);
            client.GetExtractionRunThrows = StubApiClient.Problem(401, "Unauthorized.");
        };

        IRenderedComponent<MudSnackbarProvider> snackbars = Render<MudSnackbarProvider>();
        IRenderedComponent<ReviewPage> page = RenderReview();

        await CardAt(page, 0).Find(".al-approve").ClickAsync(new MouseEventArgs());

        Assert.Equal(2, client.GetExtractionRunCalls);
        Assert.DoesNotContain("Unauthorized.", snackbars.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(snackbars.FindAll("button"), IsRetry);
    }

    [Fact]
    public async Task A_stale_retry_on_a_card_decided_since_sends_nothing()
    {
        SignIn();
        client.DecideThrows = StubApiClient.Bare(500);
        client.OnDecide = (id, command) => MarkDecided(id, ApiReviewState.Rejected, dto => dto.RejectionReason = command.Reason);

        IRenderedComponent<MudDialogProvider> dialogs = Render<MudDialogProvider>();
        IRenderedComponent<MudSnackbarProvider> snackbars = Render<MudSnackbarProvider>();
        IRenderedComponent<ReviewPage> page = RenderReview();

        await CardAt(page, 0).Find(".al-approve").ClickAsync(new MouseEventArgs());

        snackbars.WaitForAssertion(() => Assert.Contains(snackbars.FindAll("button"), IsRetry));
        IElement staleRetry = snackbars.FindAll("button").First(IsRetry);

        // The same card is then rejected, and the refresh brings it back decided.
        client.DecideThrows = null;
        Task rejecting = CardAt(page, 0).Find(".al-reject").ClickAsync(new MouseEventArgs());
        dialogs.WaitForElement($"#{RejectDialog.ConfirmId}").Click();
        await rejecting;

        page.WaitForAssertion(() => Assert.Equal(Voice.Rejected, CardAt(page, 0).Find(".al-review-state").TextContent.Trim()));
        Assert.Equal(2, client.Decisions.Count);

        await staleRetry.ClickAsync(new MouseEventArgs());

        Assert.Equal(2, client.Decisions.Count);
    }

    [Fact]
    public async Task A_failed_approve_keeps_the_edits_and_retry_resends_them()
    {
        SignIn();
        client.Roster = Roster();
        client.DecideThrows = StubApiClient.Bare(500);
        client.OnDecide = (id, _) => MarkDecided(id, ApiReviewState.Edited);

        Render<MudPopoverProvider>();
        IRenderedComponent<MudSnackbarProvider> snackbars = Render<MudSnackbarProvider>();
        IRenderedComponent<ReviewPage> page = RenderReview();

        CardAt(page, 0).Find(".al-edit").Click();
        CardAt(page, 0).Find("input, textarea").Input("Call Bob today.");
        await SetOwnerAsync(page, Priya);

        await CardAt(page, 0).Find(".al-approve-edits").ClickAsync(new MouseEventArgs());

        snackbars.WaitForAssertion(() =>
            Assert.Contains(Voice.UnexpectedFailureTitle, snackbars.Markup, StringComparison.Ordinal));

        // Still in edit mode, with what was typed, and the buttons handed back.
        Assert.Equal("Call Bob today.", CardAt(page, 0).Find("input, textarea").GetAttribute("value"));
        Assert.Equal(Priya, page.FindComponent<MudSelect<Guid?>>().Instance.GetState(x => x.Value));
        Assert.False(CardAt(page, 0).Find(".al-approve-edits").HasAttribute("disabled"));
        Assert.Equal(1, client.GetExtractionRunCalls);

        client.DecideThrows = null;
        await snackbars.FindAll("button").First(IsRetry).ClickAsync(new MouseEventArgs());

        Assert.Equal(2, client.Decisions.Count);
        Assert.Equal(client.Decisions[0].Id, client.Decisions[1].Id);
        Assert.Equal("Call Bob today.", client.Decisions[1].Command.Description);
        Assert.Equal(Priya, client.Decisions[1].Command.OwnerUserId);

        // The Retry comes from ISnackbar, outside the event pipeline, and still repaints.
        page.WaitForAssertion(() => Assert.Equal(Voice.Edited, CardAt(page, 0).Find(".al-review-state").TextContent.Trim()));
    }

    [Fact]
    public async Task A_failed_reject_retries_the_same_reason_without_reopening_the_dialog()
    {
        SignIn();
        client.DecideThrows = new HttpRequestException("no route to host");
        client.OnDecide = (id, command) => MarkDecided(id, ApiReviewState.Rejected, dto => dto.RejectionReason = command.Reason);

        IRenderedComponent<MudDialogProvider> dialogs = Render<MudDialogProvider>();
        IRenderedComponent<MudSnackbarProvider> snackbars = Render<MudSnackbarProvider>();
        IRenderedComponent<ReviewPage> page = RenderReview();

        Task rejecting = CardAt(page, 0).Find(".al-reject").ClickAsync(new MouseEventArgs());
        dialogs.WaitForElement($"#{RejectDialog.ReasonId}").Input("not an action");
        dialogs.Find($"#{RejectDialog.ConfirmId}").Click();
        await rejecting;

        snackbars.WaitForAssertion(() =>
            Assert.Contains(Voice.UnexpectedFailureTitle, snackbars.Markup, StringComparison.Ordinal));

        client.DecideThrows = null;
        await snackbars.FindAll("button").First(IsRetry).ClickAsync(new MouseEventArgs());

        Assert.Empty(dialogs.FindAll($"#{RejectDialog.ConfirmId}"));
        Assert.Equal(["not an action", "not an action"], client.Decisions.Select(decision => decision.Command.Reason));

        page.WaitForAssertion(() => Assert.Equal("Reason: not an action", CardAt(page, 0).Find(".al-rejection").TextContent));
    }

    [Fact]
    public async Task A_failed_refresh_keeps_the_screen_and_its_retry_reads_again()
    {
        SignIn();
        client.OnDecide = (id, _) =>
        {
            MarkDecided(id, ApiReviewState.Approved);
            client.GetExtractionRunThrows = StubApiClient.Bare(500);
        };

        IRenderedComponent<MudSnackbarProvider> snackbars = Render<MudSnackbarProvider>();
        IRenderedComponent<ReviewPage> page = RenderReview();

        await CardAt(page, 0).Find(".al-approve").ClickAsync(new MouseEventArgs());

        snackbars.WaitForAssertion(() =>
            Assert.Contains(Voice.UnexpectedFailureTitle, snackbars.Markup, StringComparison.Ordinal));

        // The screen as it was: no load-failure notice, both cards still there.
        Assert.DoesNotContain(Voice.LoadFailurePrefix, page.Markup, StringComparison.Ordinal);
        Assert.Equal(2, Cards(page).Length);
        Assert.Equal(Voice.Pending, CardAt(page, 0).Find(".al-review-state").TextContent.Trim());

        client.GetExtractionRunThrows = null;
        await snackbars.FindAll("button").First(IsRetry).ClickAsync(new MouseEventArgs());

        Assert.Single(client.Decisions);
        page.WaitForAssertion(() => Assert.Equal(Voice.Approved, CardAt(page, 0).Find(".al-review-state").TextContent.Trim()));
    }

    [Fact]
    public async Task The_highlight_survives_a_decision_on_another_card()
    {
        SignIn();
        client.OnDecide = (id, _) => MarkDecided(id, ApiReviewState.Approved);

        IRenderedComponent<ReviewPage> page = RenderReview();

        Cards(page)[0].TriggerEvent("onmouseenter", new MouseEventArgs());
        AssertHighlighted(page, "Dana will call Bob", activeIndex: 0);

        await CardAt(page, 1).Find(".al-approve").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Assert.Equal(Voice.Approved, CardAt(page, 1).Find(".al-review-state").TextContent.Trim()));
        AssertHighlighted(page, "Dana will call Bob", activeIndex: 0);
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

    private static readonly Guid Dana = Guid.Parse("01999999-0000-7000-8000-0000000000d1");

    private static readonly Guid Priya = Guid.Parse("01999999-0000-7000-8000-0000000000d2");

    private static PagedResultOfUserSummaryDto Roster() => new()
    {
        Items =
        [
            new UserSummaryDto { Id = Dana, DisplayName = "Dana Whitfield", Role = Role.ActionOfficer },
            new UserSummaryDto { Id = Priya, DisplayName = "Priya Ramaswamy", Role = Role.Lead },
        ],
        Page = 1,
        PageSize = 200,
        Total = 2,
    };

    /// <summary>What the server holds after a decision, so the page's refresh reads it back.</summary>
    private void MarkDecided(Guid id, ApiReviewState state, Action<ProposedActionDto>? more = null)
    {
        ProposedActionDto proposal = client.Run.Proposals.Single(candidate => candidate.Id == id);

        proposal.ReviewState = state;
        proposal.DecidedByUserId = Guid.Empty;
        proposal.DecidedByDisplayName = "Dana Whitfield";
        proposal.DecidedAt = new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);

        if (state is not ApiReviewState.Rejected)
        {
            proposal.TrackedActionId = Guid.CreateVersion7();
            proposal.DecidedDescription = proposal.Description;
        }

        more?.Invoke(proposal);
    }

    private static string Counter(IRenderedComponent<ReviewPage> page) => page.Find($"#{PendingCounter.CounterId}").TextContent;

    /// <summary>One card, as a fragment scoped to its own article.</summary>
    private static IRenderedComponent<ProposalCard> CardAt(IRenderedComponent<ReviewPage> page, int index) =>
        page.FindComponents<ProposalCard>()[index];

    private static bool IsRetry(IElement button) =>
        string.Equals(button.TextContent.Trim(), Voice.Retry, StringComparison.Ordinal);

    private static Task SetOwnerAsync(IRenderedComponent<ReviewPage> page, Guid? owner)
    {
        IRenderedComponent<MudSelect<Guid?>> select = page.FindComponent<MudSelect<Guid?>>();

        return select.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(owner));
    }

    private static Task SetDueAsync(IRenderedComponent<ReviewPage> page, DateTime? date)
    {
        IRenderedComponent<MudDatePicker> picker = page.FindComponent<MudDatePicker>();

        return picker.InvokeAsync(() => picker.Instance.DateChanged.InvokeAsync(date));
    }

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
