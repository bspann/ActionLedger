using System.Text.Json;
using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Common;
using ActionLedger.Domain.Extraction;
using ActionLedger.Domain.Meetings;
using Xunit;

namespace ActionLedger.Domain.Tests.Extraction;

/// <summary>
/// AD-3, AD-6 and AD-7 — the aggregate is where "one revision per proposal, one shared instant,
/// ordinals in AI order" is made unforgettable. Nothing above the Domain restates these rules, so
/// if they are not true here they are not true anywhere.
/// </summary>
public sealed class ExtractionRunTests
{
    private static readonly Guid MeetingId = Guid.CreateVersion7();

    private static readonly Guid NotesId = Guid.CreateVersion7();

    private static readonly Guid Actor = Guid.CreateVersion7();

    private static readonly DateTimeOffset Now = new(2026, 9, 22, 14, 30, 0, TimeSpan.Zero);

    /// <summary>A real 64-character lower-case hex digest, so the hash guard sees a plausible one.</summary>
    private const string NotesSha256 = "9f2c1d5e8a4b7c0d3e6f9a2b5c8d1e4f7a0b3c6d9e2f5a8b1c4d7e0f3a6b9c2d";

    private static readonly ExtractionRunMetadata Metrics = new(
        "Fake",
        "fixture-catalog",
        "v1",
        "1",
        new DateTimeOffset(2026, 9, 22, 14, 29, 58, TimeSpan.Zero),
        DurationMs: 12,
        InputTokens: 0,
        OutputTokens: 0);

    private static readonly ProposedActionDraft[] Drafts =
    [
        new("Order the replacement scanners.", "Dana Whitfield", new DateOnly(2026, 9, 25), 0.91, "Dana will order the replacement scanners."),
        new("Book the range for Thursday.", "Priya Raman", null, 0.62, "Priya should book the range for Thursday."),
        new("Draft the quarterly summary.", string.Empty, new DateOnly(2026, 10, 1), 0.70, "Someone needs to draft the quarterly summary."),
    ];

    // ---------------------------------------------------------------------------------------
    // Start
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void A_succeeded_run_records_every_AD6_field_and_the_notes_it_read()
    {
        ExtractionRun run = Succeeded();

        Assert.Equal(MeetingId, run.MeetingId);
        Assert.Equal(NotesId, run.MeetingNotesId);
        Assert.Equal(NotesSha256, run.NotesSha256);
        Assert.Equal(Actor, run.StartedByUserId);

        Assert.Equal("Fake", run.Provider);
        Assert.Equal("fixture-catalog", run.Model);
        Assert.Equal("v1", run.PromptVersion);
        Assert.Equal("1", run.SchemaVersion);
        Assert.Equal(Metrics.StartedAt, run.StartedAt);
        Assert.Equal(12, run.DurationMs);
        Assert.Equal(0, run.InputTokens);
        Assert.Equal(0, run.OutputTokens);

        Assert.Equal(ExtractionOutcome.Succeeded, run.Outcome);
        Assert.Null(run.FailureReason);
    }

    [Fact]
    public void A_runs_id_is_a_uuidv7_the_aggregate_made()
    {
        ExtractionRun run = Succeeded();

        Assert.NotEqual(Guid.Empty, run.Id);

        // Version 7: the nibble the UUIDv7 layout puts in byte 6 (AD-10).
        Assert.Equal(7, (run.Id.ToByteArray(bigEndian: true)[6] & 0xF0) >> 4);
    }

    [Fact]
    public void Warnings_default_to_empty_rather_than_null()
    {
        ExtractionRun run = Succeeded(warnings: null);

        Assert.NotNull(run.Warnings);
        Assert.Empty(run.Warnings);
    }

    [Fact]
    public void Warnings_are_kept_verbatim_and_in_order()
    {
        string[] warnings =
        [
            "Dropped a proposal: its source excerpt was not found in the notes. Excerpt: 'Marcus will resurface the lot.'",
            "Dropped a proposal: its source excerpt was not found in the notes. Excerpt: 'Nobody said this.'",
        ];

        ExtractionRun run = Succeeded(warnings: warnings);

        Assert.Equal(warnings, run.Warnings);
    }

    [Fact]
    public void A_failed_run_carries_its_reason_verbatim_and_no_proposals()
    {
        const string Reason = "Validation failed: actions[0].confidence must be between 0 and 1 inclusive, was 1.4.";

        ExtractionRun run = ExtractionRun.Start(
            MeetingId, NotesId, NotesSha256, Actor, Metrics, ExtractionOutcome.Failed, Reason, warnings: null);

        Assert.Equal(ExtractionOutcome.Failed, run.Outcome);
        Assert.Equal(Reason, run.FailureReason);
        Assert.Empty(run.Proposals);

        // AD-6 keeps every metric populated on a failed run too: a run nobody can compare is not
        // reproducible, and Run Detail has no "we do not know how long it took" to render.
        Assert.Equal("Fake", run.Provider);
        Assert.Equal(12, run.DurationMs);
    }

    [Fact]
    public void A_failed_run_with_no_reason_is_refused()
    {
        DomainRuleException refused = Assert.Throws<DomainRuleException>(() => ExtractionRun.Start(
            MeetingId, NotesId, NotesSha256, Actor, Metrics, ExtractionOutcome.Failed, failureReason: null, warnings: null));

        Assert.Contains("why it failed", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_failed_run_whose_reason_is_blank_is_refused(string reason) =>
        Assert.Throws<DomainRuleException>(() => ExtractionRun.Start(
            MeetingId, NotesId, NotesSha256, Actor, Metrics, ExtractionOutcome.Failed, reason, warnings: null));

    [Fact]
    public void A_succeeded_run_that_carries_a_failure_reason_is_refused() =>
        Assert.Throws<DomainRuleException>(() => ExtractionRun.Start(
            MeetingId, NotesId, NotesSha256, Actor, Metrics, ExtractionOutcome.Succeeded, "Nothing went wrong.", warnings: null));

    [Theory]
    [InlineData("meeting")]
    [InlineData("notes")]
    [InlineData("actor")]
    public void A_run_refuses_an_empty_id(string which)
    {
        Guid meeting = which == "meeting" ? Guid.Empty : MeetingId;
        Guid notes = which == "notes" ? Guid.Empty : NotesId;
        Guid actor = which == "actor" ? Guid.Empty : Actor;

        Assert.Throws<DomainRuleException>(() => ExtractionRun.Start(
            meeting, notes, NotesSha256, actor, Metrics, ExtractionOutcome.Succeeded, null, null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("deadbeef")]
    public void A_run_refuses_a_hash_that_is_not_a_sha256(string hash) =>
        Assert.Throws<DomainRuleException>(() => ExtractionRun.Start(
            MeetingId, NotesId, hash, Actor, Metrics, ExtractionOutcome.Succeeded, null, null));

    [Fact]
    public void The_hash_a_run_stores_is_exactly_the_notes_own_digest()
    {
        // AD-5 — copied, never recomputed. A run that re-hashed could agree with itself while
        // disagreeing with the row it claims to have read.
        Meeting meeting = Meeting.Create("Weekly sync", new DateOnly(2026, 9, 22), null, Actor, Now);
        MeetingNotes notes = meeting.AttachNotes("Dana will order the replacement scanners.", Now);

        ExtractionRun run = ExtractionRun.Start(
            meeting.Id, notes.Id, notes.Sha256, Actor, Metrics, ExtractionOutcome.Succeeded, null, null);

        Assert.Equal(notes.Sha256, run.NotesSha256);
        Assert.Equal(MeetingNotes.Sha256Length, run.NotesSha256.Length);
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("model")]
    [InlineData("prompt")]
    [InlineData("schema")]
    public void A_run_refuses_blank_metadata(string which)
    {
        ExtractionRunMetadata blank = Metrics with
        {
            Provider = which == "provider" ? "  " : Metrics.Provider,
            Model = which == "model" ? "  " : Metrics.Model,
            PromptVersion = which == "prompt" ? "  " : Metrics.PromptVersion,
            SchemaVersion = which == "schema" ? "  " : Metrics.SchemaVersion,
        };

        Assert.Throws<DomainRuleException>(() => ExtractionRun.Start(
            MeetingId, NotesId, NotesSha256, Actor, blank, ExtractionOutcome.Succeeded, null, null));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void A_run_refuses_a_negative_duration_or_token_count(int duration, int input, int output)
    {
        ExtractionRunMetadata negative = Metrics with
        {
            DurationMs = duration,
            InputTokens = input,
            OutputTokens = output,
        };

        Assert.Throws<DomainRuleException>(() => ExtractionRun.Start(
            MeetingId, NotesId, NotesSha256, Actor, negative, ExtractionOutcome.Succeeded, null, null));
    }

    [Fact]
    public void A_blank_warning_is_refused_rather_than_stored_as_an_empty_row() =>
        Assert.Throws<DomainRuleException>(() => Succeeded(warnings: ["A real warning.", "   "]));

    [Fact]
    public void An_over_long_failure_reason_is_clipped_to_what_the_column_holds()
    {
        // The one place the aggregate truncates rather than refuses: the reason can carry an SDK
        // exception's message, and throwing would turn a failed run into the 500 AD-11 exists to
        // prevent. Until now nothing executed that branch.
        string tooLong = new('r', ExtractionRun.FailureReasonMaxLength + 500);

        ExtractionRun run = ExtractionRun.Start(
            MeetingId, NotesId, NotesSha256, Actor, Metrics, ExtractionOutcome.Failed, tooLong, warnings: null);

        Assert.Equal(ExtractionRun.FailureReasonMaxLength, run.FailureReason!.Length);
        Assert.Equal(tooLong[..ExtractionRun.FailureReasonMaxLength], run.FailureReason);
    }

    [Fact]
    public void Clipping_a_failure_reason_never_leaves_a_lone_surrogate()
    {
        // An emoji straddling the cut is enough. A plain slice would keep the high half and store
        // text PostgreSQL round-trips as a replacement character, so the reason a human is shown
        // verbatim would end in mojibake.
        string reason = new string('r', ExtractionRun.FailureReasonMaxLength - 1) + "\U0001F600 and more";

        // The character at the cut really is the first half of a pair, or this proves nothing.
        Assert.True(char.IsHighSurrogate(reason[ExtractionRun.FailureReasonMaxLength - 1]));

        ExtractionRun run = ExtractionRun.Start(
            MeetingId, NotesId, NotesSha256, Actor, Metrics, ExtractionOutcome.Failed, reason, warnings: null);

        string stored = run.FailureReason!;

        Assert.Equal(ExtractionRun.FailureReasonMaxLength - 1, stored.Length);

        // Well-formed: no unpaired surrogate anywhere, so it survives an encode and a round trip.
        Assert.DoesNotContain(stored, char.IsSurrogate);
        Assert.Equal(stored, System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(stored)));
    }

    // ---------------------------------------------------------------------------------------
    // AddProposals — the proposals
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Ordinals_are_zero_based_and_follow_the_order_the_drafts_arrived_in()
    {
        ExtractionRun run = Succeeded();

        run.AddProposals(Drafts, Now);

        Assert.Equal([0, 1, 2], run.Proposals.Select(proposal => proposal.Ordinal));
        Assert.Equal(
            [.. Drafts.Select(draft => draft.Description)],
            run.Proposals.Select(proposal => proposal.Description));
    }

    [Fact]
    public void Every_proposal_belongs_to_the_run_that_minted_it_and_starts_pending()
    {
        ExtractionRun run = Succeeded();

        run.AddProposals(Drafts, Now);

        Assert.All(run.Proposals, proposal =>
        {
            Assert.Equal(run.Id, proposal.ExtractionRunId);
            Assert.Equal(ReviewState.Pending, proposal.ReviewState);
            Assert.Equal(7, (proposal.Id.ToByteArray(bigEndian: true)[6] & 0xF0) >> 4);
        });
    }

    [Fact]
    public void A_proposal_keeps_its_free_text_owner_including_the_empty_one()
    {
        ExtractionRun run = Succeeded();

        run.AddProposals(Drafts, Now);

        // FR-15 — the proposal keeps whatever the notes said, and "nobody was named" is "".
        Assert.Equal(["Dana Whitfield", "Priya Raman", string.Empty], run.Proposals.Select(proposal => proposal.SuggestedOwner));
        Assert.Equal([new DateOnly(2026, 9, 25), null, new DateOnly(2026, 10, 1)], run.Proposals.Select(proposal => proposal.SuggestedDueDate));
    }

    [Fact]
    public void A_succeeded_run_that_kept_nothing_still_accepts_an_empty_call()
    {
        ExtractionRun run = Succeeded();

        Assert.Empty(run.AddProposals([], Now));
        Assert.Empty(run.Proposals);
    }

    // ---------------------------------------------------------------------------------------
    // AddProposals — the revisions
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void One_AiProposal_revision_is_minted_per_proposal_with_a_null_actor_and_sequence_one()
    {
        ExtractionRun run = Succeeded();

        IReadOnlyList<ActionRevision> revisions = run.AddProposals(Drafts, Now);

        Assert.Equal(Drafts.Length, revisions.Count);

        Assert.All(revisions, revision =>
        {
            Assert.Equal(RevisionKind.AiProposal, revision.Kind);
            Assert.Equal(RevisionTargetType.ProposedAction, revision.TargetType);

            // AD-7 — the AI is not a User. A null actor here is the record, not a gap.
            Assert.Null(revision.ActorUserId);

            // A brand-new target has no prior revisions, so its first one is sequence 1.
            Assert.Equal(ActionRevision.FirstSequence, revision.Sequence);
            Assert.Equal(1, revision.Sequence);

            Assert.Null(revision.Field);
            Assert.Null(revision.OldValue);
            Assert.NotNull(revision.NewValue);
        });
    }

    [Fact]
    public void Each_revision_targets_the_proposal_it_was_minted_beside()
    {
        ExtractionRun run = Succeeded();

        IReadOnlyList<ActionRevision> revisions = run.AddProposals(Drafts, Now);

        Assert.Equal(
            [.. run.Proposals.Select(proposal => proposal.Id)],
            revisions.Select(revision => revision.TargetId));
    }

    [Fact]
    public void Every_revision_from_one_call_shares_the_one_instant_the_method_was_handed()
    {
        ExtractionRun run = Succeeded();

        IReadOnlyList<ActionRevision> revisions = run.AddProposals(Drafts, Now);

        // AD-7 — "each method receives `now` once and stamps every revision it produces with that
        // same instant". A fresh DateTimeOffset.UtcNow per row would pass every other assertion
        // here and still let one call's rows straddle a second boundary.
        Assert.All(revisions, revision => Assert.Equal(Now, revision.OccurredAt));
        Assert.Single(revisions.Select(revision => revision.OccurredAt).Distinct());
    }

    [Fact]
    public void The_shared_instant_is_normalized_to_utc()
    {
        ExtractionRun run = Succeeded();

        DateTimeOffset local = new(2026, 9, 22, 10, 30, 0, TimeSpan.FromHours(-4));

        IReadOnlyList<ActionRevision> revisions = run.AddProposals(Drafts, local);

        Assert.All(revisions, revision => Assert.Equal(TimeSpan.Zero, revision.OccurredAt.Offset));
        Assert.All(revisions, revision => Assert.Equal(local.ToUniversalTime(), revision.OccurredAt));
    }

    [Fact]
    public void The_new_value_json_carries_exactly_the_five_FR21_fields()
    {
        ExtractionRun run = Succeeded();

        IReadOnlyList<ActionRevision> revisions = run.AddProposals(Drafts, Now);

        JsonElement first = JsonDocument.Parse(revisions[0].NewValue!).RootElement;

        // FR-21 (prd.md:321) names these five and nothing else. Id, Ordinal and ReviewState are
        // the run's bookkeeping, not the proposal the AI made.
        Assert.Equal(
            ["confidence", "description", "sourceExcerpt", "suggestedDueDate", "suggestedOwner"],
            first.EnumerateObject().Select(member => member.Name).Order(StringComparer.Ordinal));

        Assert.Equal("Order the replacement scanners.", first.GetProperty("description").GetString());
        Assert.Equal("Dana Whitfield", first.GetProperty("suggestedOwner").GetString());
        Assert.Equal("2026-09-25", first.GetProperty("suggestedDueDate").GetString());
        Assert.Equal(0.91, first.GetProperty("confidence").GetDouble());
        Assert.Equal("Dana will order the replacement scanners.", first.GetProperty("sourceExcerpt").GetString());
    }

    [Fact]
    public void The_new_value_json_keeps_non_ascii_and_html_sensitive_text_literal()
    {
        ExtractionRun run = Succeeded();
        ProposedActionDraft draft = new(
            "Confirm the café's “R&D” budget — isn't <final>.",
            "Zoë O'Neil",
            null,
            0.8,
            "Zoë will confirm the café's “R&D” budget.");

        ActionRevision revision = Assert.Single(run.AddProposals([draft], Now));

        // The audit record should read as the proposal the AI made, not as \uXXXX escapes.
        Assert.Contains("\"Confirm the café's “R&D” budget — isn't <final>.\"", revision.NewValue, StringComparison.Ordinal);
        Assert.Contains("\"Zoë O'Neil\"", revision.NewValue, StringComparison.Ordinal);
        Assert.Equal(
            draft.Description,
            JsonDocument.Parse(revision.NewValue!).RootElement.GetProperty("description").GetString());
    }

    [Fact]
    public void An_unstated_due_date_is_published_as_an_explicit_null_rather_than_omitted()
    {
        ExtractionRun run = Succeeded();

        IReadOnlyList<ActionRevision> revisions = run.AddProposals(Drafts, Now);

        JsonElement second = JsonDocument.Parse(revisions[1].NewValue!).RootElement;

        // "The notes stated no date" and "this record has no such field" are different facts.
        Assert.Equal(JsonValueKind.Null, second.GetProperty("suggestedDueDate").ValueKind);
    }

    // ---------------------------------------------------------------------------------------
    // AddProposals — the refusals
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void A_failed_run_refuses_proposals()
    {
        ExtractionRun run = ExtractionRun.Start(
            MeetingId, NotesId, NotesSha256, Actor, Metrics, ExtractionOutcome.Failed, "The provider did not answer.", null);

        DomainRuleException refused = Assert.Throws<DomainRuleException>(() => run.AddProposals(Drafts, Now));

        Assert.Contains("failed Extraction Run has no proposals", refused.Message, StringComparison.Ordinal);
        Assert.Empty(run.Proposals);
    }

    [Fact]
    public void Adding_proposals_twice_is_a_rule_violation_and_leaves_the_first_set_alone()
    {
        ExtractionRun run = Succeeded();

        run.AddProposals(Drafts, Now);

        Assert.Throws<DomainRuleException>(() => run.AddProposals(Drafts, Now.AddSeconds(1)));

        Assert.Equal(Drafts.Length, run.Proposals.Count);
        Assert.Equal([0, 1, 2], run.Proposals.Select(proposal => proposal.Ordinal));
    }

    [Fact]
    public void A_second_call_is_refused_even_when_the_first_one_added_nothing()
    {
        // "The list is empty" cannot tell "nothing has been added yet" from "a succeeded run kept
        // nothing", so the guard cannot be a count.
        ExtractionRun run = Succeeded();

        run.AddProposals([], Now);

        Assert.Throws<DomainRuleException>(() => run.AddProposals(Drafts, Now));
    }

    [Fact]
    public void A_rehydrated_run_that_kept_nothing_still_refuses_proposals()
    {
        // The once-only flag is not a mapped column, so a run loaded out of the database starts
        // with whatever its rehydration constructor left. A Succeeded run that kept nothing has an
        // empty list, so the count fallback would wave a second AddProposals through and mint
        // proposals against a run whose answer was "none" — with ordinals restarting at 0 and a
        // fresh set of AiProposal revisions nobody's extraction produced.
        ExtractionRun rehydrated = Rehydrated();

        Assert.Empty(rehydrated.Proposals);
        Assert.Equal(ExtractionOutcome.Succeeded, rehydrated.Outcome);

        Assert.Throws<DomainRuleException>(() => rehydrated.AddProposals(Drafts, Now));
        Assert.Empty(rehydrated.Proposals);
    }

    /// <summary>
    /// A run as EF Core materializes one: through the private parameterless constructor, with the
    /// columns written onto the properties afterwards. Reflection is the only way to reach it from
    /// a test, and reaching it is the point — this is the state no factory can produce.
    /// </summary>
    private static ExtractionRun Rehydrated() =>
        (ExtractionRun)Activator.CreateInstance(typeof(ExtractionRun), nonPublic: true)!;

    [Theory]
    [InlineData("")]
    [InlineData("   x")]
    public void A_proposal_that_breaks_the_schemas_bounds_is_refused(string kind)
    {
        ExtractionRun run = Succeeded();

        ProposedActionDraft draft = kind.Length == 0
            ? new(string.Empty, "Dana Whitfield", null, 0.9, "Dana will do it.")
            : new(new string('x', ProposedAction.DescriptionMaxLength + 1), "Dana Whitfield", null, 0.9, "Dana will do it.");

        Assert.Throws<DomainRuleException>(() => run.AddProposals([draft], Now));
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    [InlineData(" \t \r\n ")]
    public void A_description_of_nothing_but_whitespace_is_refused(string blank)
    {
        // The schema's minLength and ExtractionOutputValidator both bound length only, so three
        // spaces reach the aggregate. A reviewer would be shown a blank commitment to approve.
        ExtractionRun run = Succeeded();

        DomainRuleException refused = Assert.Throws<DomainRuleException>(() => run.AddProposals(
            [new(blank, "Dana Whitfield", null, 0.9, "Dana will do it.")],
            Now));

        Assert.Contains("description is required", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\r\n")]
    public void A_source_excerpt_of_nothing_but_whitespace_is_refused(string blank)
    {
        ExtractionRun run = Succeeded();

        Assert.Throws<DomainRuleException>(() => run.AddProposals(
            [new("Do the thing.", "Dana Whitfield", null, 0.9, blank)],
            Now));
    }

    [Fact]
    public void Text_that_merely_has_whitespace_around_it_is_stored_untrimmed()
    {
        // The excerpt has to stay verbatim for the verifier's answer to keep meaning anything, so
        // the presence check must not become a trim.
        ExtractionRun run = Succeeded();

        run.AddProposals([new("  Do the thing.  ", "Dana Whitfield", null, 0.9, "  Dana will do it.\r\n")], Now);

        ProposedAction proposal = Assert.Single(run.Proposals);

        Assert.Equal("  Do the thing.  ", proposal.Description);
        Assert.Equal("  Dana will do it.\r\n", proposal.SourceExcerpt);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void A_confidence_outside_zero_to_one_is_refused(double confidence)
    {
        ExtractionRun run = Succeeded();

        Assert.Throws<DomainRuleException>(() => run.AddProposals(
            [new("Do the thing.", string.Empty, null, confidence, "Somebody will do the thing.")],
            Now));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void The_confidence_bounds_are_inclusive(double confidence)
    {
        ExtractionRun run = Succeeded();

        run.AddProposals([new("Do the thing.", string.Empty, null, confidence, "Somebody will do the thing.")], Now);

        Assert.Equal(confidence, Assert.Single(run.Proposals).Confidence);
    }

    [Fact]
    public void An_over_long_suggested_owner_is_refused_rather_than_truncated()
    {
        ExtractionRun run = Succeeded();

        Assert.Throws<DomainRuleException>(() => run.AddProposals(
            [new("Do the thing.", new string('n', ProposedAction.SuggestedOwnerMaxLength + 1), null, 0.9, "Somebody will do the thing.")],
            Now));
    }

    // ---------------------------------------------------------------------------------------
    // Shape claims
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Nothing_on_a_proposal_can_be_changed_but_its_review_state()
    {
        // AD-4 is a shape claim, not only a behaviour claim: if a mutator existed, some caller
        // would eventually find it. An allowlist over what ProposedAction itself declares, rather
        // than a filter on member names — a method called Reword writes Description just as well
        // as one whose name says so, and a name filter would wave it through. ReviewState's own
        // setter stays private too: Decide is the only mutation path AD-3 permits, so it is the one
        // name the allowlist carries, and it is a method on the aggregate, not a setter here.
        string[] writers =
        [
            .. typeof(ProposedAction)
                .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(property => property.SetMethod is { IsPublic: true })
                .Select(property => $"{nameof(ProposedAction)}.{property.Name}"),
            .. typeof(ProposedAction)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(method => !method.IsSpecialName && method.DeclaringType == typeof(ProposedAction))
                .Where(method => method.Name != nameof(ProposedAction.Decide))
                .Select(method => $"{nameof(ProposedAction)}.{method.Name}"),
        ];

        Assert.True(
            writers.Length == 0,
            $"A Proposed Action is immutable except through the aggregate (AD-4). Remove: {string.Join(", ", writers)}");
    }

    [Fact]
    public void Nothing_can_mint_a_proposal_or_a_revision_from_outside_the_aggregate()
    {
        // AD-7 — revisions are created only inside an aggregate method, and AD-3 gives the run the
        // only path to a proposal. A public constructor on either would be a second writer.
        Assert.Empty(typeof(ProposedAction).GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance));
        Assert.Empty(typeof(ActionRevision).GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance));
    }

    [Fact]
    public void Nothing_on_a_revision_can_be_changed_at_all()
    {
        // ADR-004 — append-only. No setter, no mutator, and no port member that updates one.
        string[] writers =
        [
            .. typeof(ActionRevision)
                .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(property => property.SetMethod is { IsPublic: true })
                .Select(property => $"{nameof(ActionRevision)}.{property.Name}"),
            .. typeof(ActionRevision)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(method => !method.IsSpecialName && method.DeclaringType == typeof(ActionRevision))
                .Select(method => $"{nameof(ActionRevision)}.{method.Name}"),
        ];

        Assert.True(
            writers.Length == 0,
            $"An audit row is append-only (AD-7, ADR-004). Remove: {string.Join(", ", writers)}");
    }

    [Fact]
    public void The_four_review_states_are_the_ones_the_prd_fixes() =>
        Assert.Equal(
            ["Approved", "Edited", "Pending", "Rejected"],
            Enum.GetNames<ReviewState>().Order(StringComparer.Ordinal));

    private static ExtractionRun Succeeded(IReadOnlyList<string>? warnings = null) =>
        ExtractionRun.Start(
            MeetingId,
            NotesId,
            NotesSha256,
            Actor,
            Metrics,
            ExtractionOutcome.Succeeded,
            failureReason: null,
            warnings);
}
