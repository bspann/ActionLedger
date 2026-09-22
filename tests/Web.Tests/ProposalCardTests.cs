using ActionLedger.Web.Core;
using ActionLedger.Web.Core.Extraction;
using ActionLedger.Web.Features.Review;
using ActionLedger.Web.Features.Review.Data;
using ActionLedger.Web.Shared;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor.Services;
using Xunit;

namespace ActionLedger.Web.Tests;

/// <summary>
/// DESIGN.md, Proposal card — the Pending card's anatomy, the three decided variants, and the
/// activation events the Review Screen's highlight hangs off. The card is presentational, so a
/// direct render is the whole test.
/// </summary>
public sealed class ProposalCardTests : BunitContext
{
    private static readonly DateTimeOffset DecidedAt = new(2026, 9, 8, 14, 12, 40, TimeSpan.Zero);

    private static readonly Guid TrackedId = Guid.Parse("01999999-0000-7000-8000-0000000000cc");

    public ProposalCardTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // ---------------------------------------------------------------------------------------
    // Pending
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void A_pending_card_shows_the_header_values_quote_and_three_verbs()
    {
        IRenderedComponent<ProposalCard> card = RenderCard(Pending() with
        {
            SuggestedOwner = "Dana",
            SuggestedOwnerUserId = Guid.CreateVersion7(),
            SuggestedOwnerDisplayName = "Dana Whitfield",
            SuggestedDueDate = new DateOnly(2026, 9, 18),
            Confidence = 0.876,
        });

        IElement article = card.Find("article");

        Assert.Equal("0", article.GetAttribute("tabindex"));

        // Named by its description, so a screen reader announces more than "article".
        string? labelledBy = article.GetAttribute("aria-labelledby");
        Assert.Equal(ProposalCard.DescriptionId(card.Instance.Proposal.Id), labelledBy);
        Assert.Equal("Circulate the packing schedule.", card.Find($"#{labelledBy}").TextContent);

        IElement header = card.Find(".al-proposal-header");

        Assert.Equal(Voice.ProposedByAi, header.QuerySelector($".{ProvenanceChip.ModifierFor(Provenance.Ai)}")!.TextContent.Trim());
        Assert.Equal("0.88", header.QuerySelector(".al-confidence-score")!.TextContent);
        Assert.Equal(Voice.Pending, header.QuerySelector($".{ReviewStateChip.ModifierFor(ReviewState.Pending)}")!.TextContent.Trim());
        Assert.Null(header.QuerySelector($".{LowConfidenceBadge.BaseClass}"));

        Assert.Equal("Circulate the packing schedule.", card.Find(".al-proposal-description").TextContent);
        Assert.Equal("Dana Whitfield", card.Find(".al-owner").TextContent);
        Assert.Equal("AI suggested: Dana", card.Find(".al-owner-hint").TextContent);
        Assert.Equal("2026-09-18", card.Find(".al-due-date").TextContent);

        IElement quote = card.Find("blockquote");
        Assert.Contains("al-source-excerpt", quote.ClassList);
        Assert.Equal("Dana will circulate the packing schedule.", quote.TextContent);

        // Reject (text), Edit (outlined), Approve (filled), in that order.
        IElement[] buttons = [.. card.FindAll(".al-proposal-actions button")];
        Assert.Equal([Voice.Reject, Voice.Edit, Voice.Approve], buttons.Select(button => button.TextContent.Trim()));
        Assert.Contains("mud-button-text", buttons[0].ClassList);
        Assert.Contains("mud-button-outlined", buttons[1].ClassList);
        Assert.Contains("mud-button-filled", buttons[2].ClassList);

        // Read-only values: nothing to type into.
        Assert.Empty(card.FindAll("input, textarea, select"));

        // No decision yet, so no human chip.
        Assert.Empty(card.FindAll($".{ProvenanceChip.ModifierFor(Provenance.Human)}"));
    }

    [Fact]
    public void An_unmatched_owner_reads_unassigned_with_the_suggestion_as_the_hint()
    {
        IRenderedComponent<ProposalCard> card = RenderCard(Pending() with { SuggestedOwner = "Facilities" });

        Assert.Equal(Voice.Unassigned, card.Find(".al-owner").TextContent);
        Assert.Equal("AI suggested: Facilities", card.Find(".al-owner-hint").TextContent);
    }

    [Fact]
    public void An_empty_suggestion_reads_ai_suggested_none_and_a_null_date_reads_no_due_date_proposed()
    {
        IRenderedComponent<ProposalCard> card = RenderCard(Pending());

        Assert.Equal(Voice.Unassigned, card.Find(".al-owner").TextContent);
        Assert.Equal("AI suggested: none", card.Find(".al-owner-hint").TextContent);
        Assert.Equal(Voice.NoDueDateProposed, card.Find(".al-due-date").TextContent);
    }

    [Fact]
    public void A_low_confidence_card_carries_the_badge_and_the_left_border_class()
    {
        IRenderedComponent<ProposalCard> card = RenderCard(Pending() with { IsLowConfidence = true, Confidence = 0.48 });

        Assert.Equal(Voice.LowConfidence, card.Find($".al-proposal-header .{LowConfidenceBadge.BaseClass}").TextContent.Trim());
        Assert.Contains(ProposalCard.LowConfidenceClass, card.Find($".{ProposalCard.CardBaseClass}").ClassList);
    }

    [Fact]
    public void A_high_confidence_card_has_no_left_border_class()
    {
        IRenderedComponent<ProposalCard> card = RenderCard(Pending());

        Assert.DoesNotContain(ProposalCard.LowConfidenceClass, card.Find($".{ProposalCard.CardBaseClass}").ClassList);
    }

    [Fact]
    public void The_three_verbs_raise_their_own_callbacks()
    {
        List<string> raised = [];

        IRenderedComponent<ProposalCard> card = Render<ProposalCard>(parameters => parameters
            .Add(component => component.Proposal, Pending())
            .Add(component => component.OnReject, () => raised.Add(Voice.Reject))
            .Add(component => component.OnEdit, () => raised.Add(Voice.Edit))
            .Add(component => component.OnApprove, () => raised.Add(Voice.Approve)));

        card.Find(".al-reject").Click();
        card.Find(".al-edit").Click();
        card.Find(".al-approve").Click();

        Assert.Equal([Voice.Reject, Voice.Edit, Voice.Approve], raised);
    }

    // ---------------------------------------------------------------------------------------
    // Activation
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Hover_focus_inside_and_a_quote_click_each_activate_the_card()
    {
        int activations = 0;

        IRenderedComponent<ProposalCard> card = Render<ProposalCard>(parameters => parameters
            .Add(component => component.Proposal, Pending())
            .Add(component => component.OnActivate, () => activations++));

        card.Find("article").TriggerEvent("onmouseenter", new MouseEventArgs());
        Assert.Equal(1, activations);

        card.Find("article").TriggerEvent("onfocusin", new FocusEventArgs());
        Assert.Equal(2, activations);

        card.Find("blockquote").Click();
        Assert.True(activations >= 3);
    }

    [Fact]
    public void Only_an_active_card_with_a_range_is_described_by_the_highlight()
    {
        ReviewProposal located = Pending() with { Excerpt = new ExcerptRange(3, 18) };

        Assert.Equal(NotesPane.HighlightId, DescribedBy(located, isActive: true));
        Assert.Null(DescribedBy(located, isActive: false));

        // No range, no mark to point at.
        Assert.Null(DescribedBy(Pending(), isActive: true));
    }

    // ---------------------------------------------------------------------------------------
    // Decided
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void An_approved_card_shows_the_tracked_values_the_decider_the_time_and_the_action_link()
    {
        IRenderedComponent<ProposalCard> card = RenderCard(Decided(ReviewState.Approved) with
        {
            DecidedDescription = "Circulate the packing schedule.",
            DecidedOwnerDisplayName = "Dana Whitfield",
            DecidedDueDate = new DateOnly(2026, 9, 18),
        });

        Assert.Equal(Voice.Approved, card.Find($".{ReviewStateChip.ModifierFor(ReviewState.Approved)}").TextContent.Trim());
        Assert.Equal("Circulate the packing schedule.", card.Find(".al-proposal-description").TextContent);
        Assert.Equal("Dana Whitfield", card.Find(".al-owner").TextContent);
        Assert.Equal("2026-09-18", card.Find(".al-due-date").TextContent);

        AssertDecisionRow(card);

        IElement link = card.Find(".al-view-action");
        Assert.Equal(Voice.ViewAction, link.TextContent);
        Assert.Equal($"/actions/{TrackedId}", link.GetAttribute("href"));

        // The quote stays; the verbs go.
        Assert.NotNull(card.Find("blockquote"));
        Assert.Empty(card.FindAll(".al-proposal-actions"));
        Assert.Empty(card.FindAll(".al-proposal-proposed"));
    }

    [Fact]
    public void An_approved_card_with_no_owner_or_date_reads_unassigned_and_no_due_date()
    {
        IRenderedComponent<ProposalCard> card = RenderCard(Decided(ReviewState.Approved) with
        {
            DecidedDescription = "Circulate the packing schedule.",
        });

        Assert.Equal(Voice.Unassigned, card.Find(".al-owner").TextContent);
        Assert.Equal(Voice.NoDueDate, card.Find(".al-due-date").TextContent);
    }

    [Fact]
    public void An_edited_card_shows_the_proposed_column_beside_the_decided_one()
    {
        IRenderedComponent<ProposalCard> card = RenderCard(Decided(ReviewState.Edited) with
        {
            Description = "Arrange surplus furniture pickup.",
            SuggestedOwner = "P. Ram",
            SuggestedDueDate = new DateOnly(2026, 10, 10),
            DecidedDescription = "Arrange surplus furniture pickup with the recycler.",
            DecidedOwnerDisplayName = "Priya Ramaswamy",
            DecidedDueDate = new DateOnly(2026, 10, 3),
            IsLowConfidence = true,
        });

        Assert.Equal(Voice.Edited, card.Find($".{ReviewStateChip.ModifierFor(ReviewState.Edited)}").TextContent.Trim());

        IElement proposed = card.Find(".al-proposal-proposed");
        Assert.Equal(Voice.Proposed, proposed.QuerySelector(".al-proposal-column-heading")!.TextContent);
        Assert.Equal("Arrange surplus furniture pickup.", proposed.QuerySelector(".al-proposal-proposed-description")!.TextContent);
        Assert.Equal("P. Ram", proposed.QuerySelector(".al-proposed-owner")!.TextContent);
        Assert.Equal("2026-10-10", proposed.QuerySelector(".al-proposed-due-date")!.TextContent);

        IElement decided = card.Find(".al-proposal-decided");
        Assert.Equal(Voice.Decided, decided.QuerySelector(".al-proposal-column-heading")!.TextContent);
        Assert.Equal("Priya Ramaswamy", decided.QuerySelector(".al-owner")!.TextContent);
        Assert.Equal("2026-10-03", decided.QuerySelector(".al-due-date")!.TextContent);

        Assert.Equal("Arrange surplus furniture pickup with the recycler.", card.Find(".al-proposal-description").TextContent);
        Assert.Equal(
            "Arrange surplus furniture pickup with the recycler.",
            card.Find($"#{card.Find("article").GetAttribute("aria-labelledby")}").TextContent);

        // It keeps the low-confidence marking it was proposed with.
        Assert.NotNull(card.Find($".al-proposal-header .{LowConfidenceBadge.BaseClass}"));

        AssertDecisionRow(card);
        Assert.Equal($"/actions/{TrackedId}", card.Find(".al-view-action").GetAttribute("href"));
    }

    [Fact]
    public void A_rejected_card_shows_the_proposed_values_and_the_reason_with_no_link()
    {
        IRenderedComponent<ProposalCard> card = RenderCard(Decided(ReviewState.Rejected) with
        {
            TrackedActionId = null,
            SuggestedOwner = "Marcus",
            SuggestedOwnerDisplayName = "Marcus Bell",
            RejectionReason = "discussion item, not an action",
        });

        Assert.Equal(Voice.Rejected, card.Find($".{ReviewStateChip.ModifierFor(ReviewState.Rejected)}").TextContent.Trim());
        Assert.Equal("Circulate the packing schedule.", card.Find(".al-proposal-description").TextContent);
        Assert.Equal("Marcus Bell", card.Find(".al-owner").TextContent);
        Assert.Equal("AI suggested: Marcus", card.Find(".al-owner-hint").TextContent);
        Assert.Equal(Voice.NoDueDateProposed, card.Find(".al-due-date").TextContent);
        Assert.Equal("Reason: discussion item, not an action", card.Find(".al-rejection").TextContent);

        AssertDecisionRow(card);
        Assert.Empty(card.FindAll(".al-view-action"));
        Assert.Empty(card.FindAll(".al-proposal-actions"));
    }

    [Fact]
    public void A_rejection_without_a_reason_says_so()
    {
        IRenderedComponent<ProposalCard> card = RenderCard(Decided(ReviewState.Rejected) with { TrackedActionId = null });

        Assert.Equal(Voice.NoReasonGiven, card.Find(".al-rejection").TextContent);
    }

    [Fact]
    public void A_pending_card_still_has_the_live_region_the_decision_will_land_in()
    {
        IRenderedComponent<ProposalCard> card = RenderCard(Pending());

        IElement region = card.Find(".al-proposal-decision");

        Assert.Equal("polite", region.GetAttribute("aria-live"));
        Assert.Empty(region.Children);
    }

    private void AssertDecisionRow(IRenderedComponent<ProposalCard> card)
    {
        IElement row = card.Find(".al-proposal-decision");

        Assert.Equal("polite", row.GetAttribute("aria-live"));

        IElement chip = row.QuerySelector($".{ProvenanceChip.ModifierFor(Provenance.Human)}")!;
        Assert.Equal("Decided by Dana Whitfield", chip.TextContent.Trim());

        // Beside the chip, not inside it.
        IElement at = row.QuerySelector(".al-decided-at")!;
        Assert.Equal("2026-09-08 14:12 UTC", at.TextContent);
        Assert.False(chip.Contains(at));
    }

    private string? DescribedBy(ReviewProposal proposal, bool isActive) =>
        Render<ProposalCard>(parameters => parameters
            .Add(component => component.Proposal, proposal)
            .Add(component => component.IsActive, isActive))
            .Find("article")
            .GetAttribute("aria-describedby");

    private IRenderedComponent<ProposalCard> RenderCard(ReviewProposal proposal) =>
        Render<ProposalCard>(parameters => parameters.Add(component => component.Proposal, proposal));

    internal static ReviewProposal Pending() => new(
        Guid.CreateVersion7(),
        Ordinal: 0,
        Description: "Circulate the packing schedule.",
        SuggestedOwner: string.Empty,
        SuggestedOwnerUserId: null,
        SuggestedOwnerDisplayName: null,
        SuggestedDueDate: null,
        Confidence: 0.91,
        IsLowConfidence: false,
        SourceExcerpt: "Dana will circulate the packing schedule.",
        ReviewState: ReviewState.Pending,
        DecidedByUserId: null,
        DecidedByDisplayName: null,
        DecidedAt: null,
        RejectionReason: null,
        TrackedActionId: null,
        DecidedDescription: null,
        DecidedOwnerUserId: null,
        DecidedOwnerDisplayName: null,
        DecidedDueDate: null,
        Excerpt: null);

    private static ReviewProposal Decided(ReviewState state) => Pending() with
    {
        ReviewState = state,
        DecidedByUserId = Guid.CreateVersion7(),
        DecidedByDisplayName = "Dana Whitfield",
        DecidedAt = DecidedAt,
        TrackedActionId = TrackedId,
    };
}
