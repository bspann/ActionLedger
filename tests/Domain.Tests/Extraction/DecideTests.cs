using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Common;
using ActionLedger.Domain.Extraction;
using Xunit;

namespace ActionLedger.Domain.Tests.Extraction;

/// <summary>
/// AD-3, AD-4 and AD-7 — <see cref="ProposedAction.Decide"/> is the only way a Review State
/// changes and the only way a Tracked Action exists, so every transition, every refusal, and the
/// order of the revisions it writes are pinned here.
/// </summary>
public sealed class DecideTests
{
    private static readonly Guid Actor = Guid.CreateVersion7();

    private static readonly Guid Dana = Guid.CreateVersion7();

    private static readonly Guid Priya = Guid.CreateVersion7();

    private static readonly DateOnly Proposed = new(2026, 9, 25);

    // A non-UTC offset, so "stamped in UTC" is a claim a test can falsify.
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 16, 30, 0, TimeSpan.FromHours(2));

    private static readonly DateTimeOffset NowUtc = Now.ToUniversalTime();

    private const string Description = "Order the replacement scanners.";

    // ---------------------------------------------------------------------------------------
    // Approve
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Approving_creates_an_open_tracked_action_with_the_proposal_values()
    {
        ProposedAction proposal = Pending();

        DecisionResult result = proposal.Decide(DecisionKind.Approved, Unchanged(Dana), Actor, Now);

        Assert.Equal(DecisionKind.Approved, result.Kind);
        TrackedAction tracked = Assert.IsType<TrackedAction>(result.TrackedAction);

        Assert.Equal(proposal.Id, tracked.ProposedActionId);
        Assert.Equal(Description, tracked.Description);
        Assert.Equal(Dana, tracked.OwnerUserId);
        Assert.Equal(Proposed, tracked.DueDate);
        Assert.Equal(ActionStatus.Open, tracked.Status);
        Assert.Equal(NowUtc, tracked.CreatedAt);
        Assert.Equal(TimeSpan.Zero, tracked.CreatedAt.Offset);
        Assert.Equal(7, (tracked.Id.ToByteArray(bigEndian: true)[6] & 0xF0) >> 4);
    }

    [Fact]
    public void Approving_writes_one_review_decision_revision_against_the_proposal()
    {
        ProposedAction proposal = Pending();

        DecisionResult result = proposal.Decide(DecisionKind.Approved, Unchanged(Dana), Actor, Now);

        ActionRevision decision = Assert.Single(result.Revisions);

        AssertReviewDecision(decision, proposal, "Approved");
    }

    [Fact]
    public void Approving_an_unmatched_owner_leaves_the_tracked_action_unassigned()
    {
        ProposedAction proposal = Pending(suggestedOwner: string.Empty);

        DecisionResult result = proposal.Decide(DecisionKind.Approved, Unchanged(owner: null), Actor, Now);

        Assert.Null(result.TrackedAction!.OwnerUserId);
        Assert.Single(result.Revisions);
    }

    [Theory]
    [InlineData(DecisionKind.Approved)]
    [InlineData(DecisionKind.Edited)]
    public void An_accepted_decision_raises_TrackedActionCreated_on_the_new_root(DecisionKind kind)
    {
        ProposedAction proposal = Pending();
        DecisionEdits edits = kind == DecisionKind.Approved
            ? Unchanged(Dana)
            : Unchanged(Dana) with { Description = "Order four replacement scanners." };

        TrackedAction tracked = proposal.Decide(kind, edits, Actor, Now).TrackedAction!;

        TrackedActionCreated created = Assert.IsType<TrackedActionCreated>(Assert.Single(tracked.DomainEvents));

        Assert.Equal(tracked.Id, created.TrackedActionId);
        Assert.Equal(proposal.Id, created.ProposedActionId);
        Assert.Equal(kind, created.Kind);
        Assert.Equal(NowUtc, created.OccurredAt);
    }

    [Theory]
    [InlineData(DecisionKind.Approved, ReviewState.Approved)]
    [InlineData(DecisionKind.Edited, ReviewState.Edited)]
    [InlineData(DecisionKind.Rejected, ReviewState.Rejected)]
    public void Every_kind_moves_a_pending_proposal_and_writes_its_decision_copy(DecisionKind kind, ReviewState expected)
    {
        ProposedAction proposal = Pending();

        proposal.Decide(kind, EditsFor(kind), Actor, Now);

        Assert.Equal(expected, proposal.ReviewState);
        Assert.Equal(Actor, proposal.DecidedByUserId);
        Assert.Equal(NowUtc, proposal.DecidedAt);
        Assert.Equal(TimeSpan.Zero, proposal.DecidedAt!.Value.Offset);
    }

    // ---------------------------------------------------------------------------------------
    // Edit
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Editing_all_three_fields_writes_the_decision_then_three_field_edits_in_order()
    {
        ProposedAction proposal = Pending();
        DateOnly moved = new(2026, 10, 2);

        DecisionResult result = proposal.Decide(
            DecisionKind.Edited,
            new DecisionEdits("Book movers", Priya, moved, ProposedOwnerUserId: Dana, Reason: null),
            Actor,
            Now);

        TrackedAction tracked = result.TrackedAction!;

        Assert.Equal("Book movers", tracked.Description);
        Assert.Equal(Priya, tracked.OwnerUserId);
        Assert.Equal(moved, tracked.DueDate);
        Assert.Equal(ActionStatus.Open, tracked.Status);

        // The AI's row is never the human's row: the proposal keeps what the AI said.
        Assert.Equal(Description, proposal.Description);
        Assert.Equal(Proposed, proposal.SuggestedDueDate);

        Assert.Equal(4, result.Revisions.Count);
        AssertReviewDecision(result.Revisions[0], proposal, "Edited");

        Assert.Equal(
            [
                (3, "Description", Description, "Book movers"),
                (4, "OwnerUserId", Dana.ToString("D"), Priya.ToString("D")),
                (5, "DueDate", "2026-09-25", "2026-10-02"),
            ],
            result.Revisions.Skip(1).Select(revision => (revision.Sequence, revision.Field!, revision.OldValue, revision.NewValue)));

        Assert.All(result.Revisions.Skip(1), revision =>
        {
            Assert.Equal(RevisionKind.FieldEdit, revision.Kind);
            Assert.Equal(RevisionTargetType.TrackedAction, revision.TargetType);
            Assert.Equal(tracked.Id, revision.TargetId);
        });

        // One shared instant and one actor, so the Audit Trail's OccurredAt-then-Sequence read puts
        // every FieldEdit after the decision.
        Assert.All(result.Revisions, revision =>
        {
            Assert.Equal(NowUtc, revision.OccurredAt);
            Assert.Equal(TimeSpan.Zero, revision.OccurredAt.Offset);
            Assert.Equal(Actor, revision.ActorUserId);
        });

        Assert.Equal([2, 3, 4, 5], result.Revisions.Select(revision => revision.Sequence));
    }

    [Fact]
    public void Clearing_a_matched_owner_writes_one_owner_edit_to_null()
    {
        ProposedAction proposal = Pending();

        DecisionResult result = proposal.Decide(DecisionKind.Edited, Unchanged(Dana) with { OwnerUserId = null }, Actor, Now);

        Assert.Null(result.TrackedAction!.OwnerUserId);
        Assert.Equal(2, result.Revisions.Count);

        ActionRevision edit = result.Revisions[1];

        Assert.Equal(3, edit.Sequence);
        Assert.Equal("OwnerUserId", edit.Field);
        Assert.Equal(Dana.ToString("D"), edit.OldValue);
        Assert.Null(edit.NewValue);
    }

    [Fact]
    public void Assigning_an_owner_where_none_matched_writes_an_owner_edit_from_null()
    {
        ProposedAction proposal = Pending(suggestedOwner: string.Empty);

        DecisionResult result = proposal.Decide(DecisionKind.Edited, Unchanged(owner: null) with { OwnerUserId = Priya }, Actor, Now);

        ActionRevision edit = result.Revisions[1];

        Assert.Equal("OwnerUserId", edit.Field);
        Assert.Null(edit.OldValue);
        Assert.Equal(Priya.ToString("D"), edit.NewValue);
    }

    [Fact]
    public void Clearing_the_due_date_writes_one_due_date_edit_to_null()
    {
        ProposedAction proposal = Pending();

        DecisionResult result = proposal.Decide(DecisionKind.Edited, Unchanged(Dana) with { DueDate = null }, Actor, Now);

        Assert.Null(result.TrackedAction!.DueDate);

        ActionRevision edit = Assert.Single(result.Revisions, revision => revision.Kind == RevisionKind.FieldEdit);

        Assert.Equal(3, edit.Sequence);
        Assert.Equal("DueDate", edit.Field);
        Assert.Equal("2026-09-25", edit.OldValue);
        Assert.Null(edit.NewValue);
    }

    [Fact]
    public void Only_the_changed_fields_get_a_field_edit_and_sequences_stay_contiguous()
    {
        ProposedAction proposal = Pending();

        DecisionResult result = proposal.Decide(
            DecisionKind.Edited,
            Unchanged(Dana) with { Description = "Order four scanners.", DueDate = new DateOnly(2026, 9, 30) },
            Actor,
            Now);

        Assert.Equal(
            [(2, "ReviewState"), (3, "Description"), (4, "DueDate")],
            result.Revisions.Select(revision => (revision.Sequence, revision.Field!)));
    }

    // ---------------------------------------------------------------------------------------
    // Reject
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Rejecting_with_a_reason_trims_it_and_records_it_on_the_decision()
    {
        ProposedAction proposal = Pending();

        DecisionResult result = proposal.Decide(DecisionKind.Rejected, Rejection("  not an action "), Actor, Now);

        Assert.Equal(DecisionKind.Rejected, result.Kind);
        Assert.Null(result.TrackedAction);
        Assert.Equal(ReviewState.Rejected, proposal.ReviewState);
        Assert.Equal("not an action", proposal.RejectionReason);

        AssertReviewDecision(Assert.Single(result.Revisions), proposal, "Rejected: not an action");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejecting_without_a_reason_stores_null_and_names_only_the_state(string? reason)
    {
        ProposedAction proposal = Pending();

        DecisionResult result = proposal.Decide(DecisionKind.Rejected, Rejection(reason), Actor, Now);

        Assert.Null(result.TrackedAction);
        Assert.Null(proposal.RejectionReason);

        AssertReviewDecision(Assert.Single(result.Revisions), proposal, "Rejected");
    }

    [Fact]
    public void Rejecting_ignores_the_description_owner_and_due_date()
    {
        ProposedAction proposal = Pending();

        // A blank, over-long description would be refused on an approval; a rejection never reads it.
        DecisionResult result = proposal.Decide(
            DecisionKind.Rejected,
            new DecisionEdits(new string('x', 501), Priya, new DateOnly(2030, 1, 1), Dana, "Duplicate."),
            Actor,
            Now);

        Assert.Null(result.TrackedAction);
        Assert.Single(result.Revisions);
    }

    [Fact]
    public void A_reason_of_exactly_the_maximum_is_accepted()
    {
        ProposedAction proposal = Pending();
        string reason = new('r', ProposedAction.RejectionReasonMaxLength);

        proposal.Decide(DecisionKind.Rejected, Rejection($"  {reason}  "), Actor, Now);

        Assert.Equal(reason, proposal.RejectionReason);
    }

    // ---------------------------------------------------------------------------------------
    // Refusals
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Approving_with_a_changed_field_is_refused()
    {
        ProposedAction proposal = Pending();

        AssertRefusedAndStillPending(proposal, DecisionKind.Approved, Unchanged(Dana) with { DueDate = null });
    }

    [Fact]
    public void Editing_with_nothing_changed_is_refused()
    {
        ProposedAction proposal = Pending();

        // Keeping the pre-selected owner is not a change.
        AssertRefusedAndStillPending(proposal, DecisionKind.Edited, Unchanged(Dana));
    }

    [Fact]
    public void A_description_that_differs_only_by_case_is_an_edit_not_an_approval()
    {
        ProposedAction proposal = Pending();

        AssertRefusedAndStillPending(proposal, DecisionKind.Approved, Unchanged(Dana) with { Description = Description.ToUpperInvariant() });
    }

    [Theory]
    [InlineData(DecisionKind.Approved, null)]
    [InlineData(DecisionKind.Approved, "")]
    [InlineData(DecisionKind.Edited, "   ")]
    [InlineData(DecisionKind.Edited, null)]
    public void A_blank_description_is_refused_on_an_accepted_decision(DecisionKind kind, string? description)
    {
        ProposedAction proposal = Pending();

        AssertRefusedAndStillPending(proposal, kind, Unchanged(Dana) with { Description = description });
    }

    [Fact]
    public void A_description_over_the_maximum_is_refused()
    {
        ProposedAction proposal = Pending();

        AssertRefusedAndStillPending(
            proposal,
            DecisionKind.Edited,
            Unchanged(Dana) with { Description = new string('d', ProposedAction.DescriptionMaxLength + 1) });
    }

    [Fact]
    public void A_description_of_exactly_the_maximum_is_accepted()
    {
        ProposedAction proposal = Pending();
        string description = new('d', ProposedAction.DescriptionMaxLength);

        DecisionResult result = proposal.Decide(DecisionKind.Edited, Unchanged(Dana) with { Description = description }, Actor, Now);

        Assert.Equal(description, result.TrackedAction!.Description);
    }

    [Theory]
    [InlineData(DecisionKind.Approved)]
    [InlineData(DecisionKind.Edited)]
    public void A_reason_is_refused_on_an_accepted_decision(DecisionKind kind)
    {
        ProposedAction proposal = Pending();

        AssertRefusedAndStillPending(proposal, kind, EditsFor(kind) with { Reason = "Because." });
    }

    [Theory]
    [InlineData(DecisionKind.Approved)]
    [InlineData(DecisionKind.Edited)]
    public void A_blank_reason_is_tolerated_on_an_accepted_decision(DecisionKind kind)
    {
        ProposedAction proposal = Pending();

        DecisionResult result = proposal.Decide(kind, EditsFor(kind) with { Reason = "  " }, Actor, Now);

        Assert.NotNull(result.TrackedAction);
        Assert.Null(proposal.RejectionReason);
    }

    [Fact]
    public void A_reason_over_the_maximum_is_refused()
    {
        ProposedAction proposal = Pending();

        AssertRefusedAndStillPending(
            proposal,
            DecisionKind.Rejected,
            Rejection(new string('r', ProposedAction.RejectionReasonMaxLength + 1)));
    }

    [Theory]
    [InlineData(DecisionKind.Approved)]
    [InlineData(DecisionKind.Edited)]
    [InlineData(DecisionKind.Rejected)]
    public void A_missing_actor_is_refused(DecisionKind kind)
    {
        ProposedAction proposal = Pending();

        Assert.Throws<DomainRuleException>(() => proposal.Decide(kind, EditsFor(kind), Guid.Empty, Now));

        AssertUndecided(proposal);
    }

    /// <summary>
    /// The 3×3: every decided state refuses every kind. With the transitions above, this covers
    /// every Review State transition there is — Pending to each of three, and nothing out of them.
    /// </summary>
    [Theory]
    [InlineData(DecisionKind.Approved, DecisionKind.Approved)]
    [InlineData(DecisionKind.Approved, DecisionKind.Edited)]
    [InlineData(DecisionKind.Approved, DecisionKind.Rejected)]
    [InlineData(DecisionKind.Edited, DecisionKind.Approved)]
    [InlineData(DecisionKind.Edited, DecisionKind.Edited)]
    [InlineData(DecisionKind.Edited, DecisionKind.Rejected)]
    [InlineData(DecisionKind.Rejected, DecisionKind.Approved)]
    [InlineData(DecisionKind.Rejected, DecisionKind.Edited)]
    [InlineData(DecisionKind.Rejected, DecisionKind.Rejected)]
    public void A_decided_proposal_cannot_be_decided_again(DecisionKind first, DecisionKind second)
    {
        ProposedAction proposal = Pending();
        proposal.Decide(first, EditsFor(first), Actor, Now);

        ReviewState state = proposal.ReviewState;
        Guid? decidedBy = proposal.DecidedByUserId;
        DateTimeOffset? decidedAt = proposal.DecidedAt;
        string? reason = proposal.RejectionReason;

        Assert.Throws<DomainRuleException>(() =>
            proposal.Decide(second, EditsFor(second), Guid.CreateVersion7(), Now.AddMinutes(5)));

        Assert.Equal(state, proposal.ReviewState);
        Assert.Equal(decidedBy, proposal.DecidedByUserId);
        Assert.Equal(decidedAt, proposal.DecidedAt);
        Assert.Equal(reason, proposal.RejectionReason);
    }

    // ---------------------------------------------------------------------------------------
    // Shape claims
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Nothing_outside_the_domain_can_mint_or_change_a_tracked_action()
    {
        // Decide is the only creator, and Epic 4 brings the only mutators. An allowlist of none.
        const System.Reflection.BindingFlags Public =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;

        string[] openings =
        [
            .. typeof(TrackedAction).GetConstructors(Public).Select(constructor => constructor.ToString()!),
            .. typeof(TrackedAction)
                .GetProperties(Public)
                .Where(property => property.SetMethod is { IsPublic: true })
                .Select(property => property.Name),
            .. typeof(TrackedAction)
                .GetMethods(Public)
                .Where(method => !method.IsSpecialName && method.DeclaringType == typeof(TrackedAction))
                .Select(method => method.Name),
        ];

        Assert.True(
            openings.Length == 0,
            $"Only ProposedAction.Decide creates a Tracked Action (AD-3). Remove: {string.Join(", ", openings)}");
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    /// <summary>A Pending proposal obtained the only way one exists: from a succeeded run.</summary>
    private static ProposedAction Pending(string suggestedOwner = "Dana Whitfield")
    {
        ExtractionRun run = ExtractionRun.Start(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "9f2c1d5e8a4b7c0d3e6f9a2b5c8d1e4f7a0b3c6d9e2f5a8b1c4d7e0f3a6b9c2d",
            Actor,
            new ExtractionRunMetadata("Fake", "fixture-catalog", "v1", "1", Now.AddSeconds(-2), 12, 0, 0),
            ExtractionOutcome.Succeeded,
            failureReason: null,
            warnings: null);

        run.AddProposals(
            [new ProposedActionDraft(Description, suggestedOwner, Proposed, 0.91, "Dana will order the replacement scanners.")],
            Now.AddSeconds(-1));

        return Assert.Single(run.Proposals);
    }

    /// <summary>The proposal's own values, with <paramref name="owner"/> as both the match and the choice.</summary>
    private static DecisionEdits Unchanged(Guid? owner) =>
        new(Description, owner, Proposed, ProposedOwnerUserId: owner, Reason: null);

    private static DecisionEdits Rejection(string? reason) =>
        new(Description: null, OwnerUserId: null, DueDate: null, ProposedOwnerUserId: Dana, Reason: reason);

    private static DecisionEdits EditsFor(DecisionKind kind) => kind switch
    {
        DecisionKind.Approved => Unchanged(Dana),
        DecisionKind.Edited => Unchanged(Dana) with { OwnerUserId = Priya },
        _ => Rejection("Duplicate of an earlier action."),
    };

    private static void AssertReviewDecision(ActionRevision revision, ProposedAction proposal, string newValue)
    {
        Assert.Equal(RevisionKind.ReviewDecision, revision.Kind);
        Assert.Equal(RevisionTargetType.ProposedAction, revision.TargetType);
        Assert.Equal(proposal.Id, revision.TargetId);
        Assert.Equal(ActionRevision.FirstSequence + 1, revision.Sequence);
        Assert.Equal("ReviewState", revision.Field);
        Assert.Equal("Pending", revision.OldValue);
        Assert.Equal(newValue, revision.NewValue);
        Assert.Equal(Actor, revision.ActorUserId);
        Assert.Equal(NowUtc, revision.OccurredAt);
    }

    private static void AssertRefusedAndStillPending(ProposedAction proposal, DecisionKind kind, DecisionEdits edits)
    {
        Assert.Throws<DomainRuleException>(() => proposal.Decide(kind, edits, Actor, Now));

        AssertUndecided(proposal);
    }

    private static void AssertUndecided(ProposedAction proposal)
    {
        Assert.Equal(ReviewState.Pending, proposal.ReviewState);
        Assert.Null(proposal.DecidedByUserId);
        Assert.Null(proposal.DecidedAt);
        Assert.Null(proposal.RejectionReason);
    }
}
