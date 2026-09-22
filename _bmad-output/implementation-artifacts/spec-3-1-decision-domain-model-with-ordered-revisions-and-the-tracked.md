---
title: 'Story 3.1 — Decision domain model with ordered revisions and the Tracked Action root'
type: 'feature'
created: '2026-09-22'
baseline_revision: '28aece9102d25eacf3f4807028a0caa6cfc8a491'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
warnings: ['oversized']
deferred:
  - summary: >-
      Application and Web doc comments still say the decision-copy DTO fields are "Story 3.1's" and "Always null until Story 3.1", which is now stale because Decide writes them while the DTO does not publish them yet.
    evidence: |-
      src/ActionLedger.Application/Review/ProposedActionDtos.cs:14-15 and src/ActionLedger.Web/Features/Review/Data/ReviewService.cs:91,152-154. This spec forbids Application and Web changes in 3.1. The comments should be corrected by the story that publishes the fields (3.4 read model / 3.2 DTOs).
    location: >-
      src/ActionLedger.Application/Review/ProposedActionDtos.cs:14
    severity: low
  - summary: >-
      MeetingsQueries.ListAsync still publishes a literal 0 for MeetingSummaryDto.trackedActionCount, although tracked_actions now exists; no test seeds a Tracked Action under a meeting.
    evidence: |-
      src/ActionLedger.Application/Meetings/MeetingsQueries.cs:60-68 builds new MeetingSummaryDto(..., runs.Count(...), 0) and its comment says "Story 3.1 replaces that last literal"; MeetingDtos.cs:8-11,18 and tests/Application.Tests/Meetings/MeetingsTests.cs:210,236,239-240 assert/describe 0. The 3.1 intent forbids Application changes, and no production path creates a Tracked Action until 3.2's endpoint, so the count is wrong only once decisions ship. Fix: a correlated count of tracked_actions joined via proposed_actions to extraction_runs.meeting_id, a per-meeting test seeding Tracked Actions, and the stale comments corrected.
    location: >-
      src/ActionLedger.Application/Meetings/MeetingsQueries.cs:63
    severity: medium
---

<intent-contract>

## Intent

**Problem:** A proposal has no way to leave Pending, and there is no Tracked Action at all. Epic 3's
trust model ("only a human decision creates tracked work, and every decision is audited in order")
must be enforced in the entity before 3.2's endpoint and 3.3's outbox can build on it
(epics.md:522-536; AD-3, AD-4, AD-7).

**Approach:** Add `ProposedAction.Decide(kind, edits, actorUserId, now)` as the only Review State
mutation. It returns a `DecisionResult` carrying the new `TrackedAction` (Approved or Edited) and
the ordered revisions: one ReviewDecision revision, then one FieldEdit revision per changed field.
Add the `TrackedAction` root (private setters, `TrackedActionCreated` raised through
`AggregateRoot.Raise`), its EF configuration, and one generated migration for `tracked_actions`
plus the proposal's decision-copy columns.

## Boundaries & Constraints

**Always:**

- Signature: `public DecisionResult Decide(DecisionKind kind, DecisionEdits edits, Guid actorUserId, DateTimeOffset now)`.
  `DecisionEdits` is a public record `(string? Description, Guid? OwnerUserId, DateOnly? DueDate, Guid? ProposedOwnerUserId, string? Reason)`, mirroring the 3.2 body.
  `ProposedOwnerUserId` is the `OwnerResolver.Match` result, which 3.2 supplies. It is the baseline for the owner diff, because Domain cannot see users.
- Kind rules (each violation throws `DomainRuleException`):
  - The proposal must be Pending.
  - `actorUserId` must not be `Guid.Empty`.
  - **Approved** requires Description equal to the proposal's (ordinal), DueDate equal to `SuggestedDueDate`, and OwnerUserId equal to ProposedOwnerUserId.
  - **Edited** requires at least one of the three to differ.
  - Both require Description to be non-blank and at most 500 characters.
  - A non-blank Reason is refused on Approved and Edited.
  - **Rejected** ignores Description, OwnerUserId, and DueDate. Its Reason is trimmed, stored as null when blank, and refused over `ProposedAction.RejectionReasonMaxLength` = 500.
- The Tracked Action takes the sent `OwnerUserId` verbatim (null = Unassigned), the edited or
  proposal Description and DueDate, `Status = Open`, and `CreatedAt = now` (UTC). It raises
  `TrackedActionCreated(TrackedActionId, ProposedActionId, Kind)` with `OccurredAt = now`.
  Rejected creates no Tracked Action and raises nothing.
- On success, the proposal's `ReviewState`, `DecidedByUserId`, `DecidedAt` (now, UTC), and
  `RejectionReason` are set in the same call (private setters).
- Revisions: every one carries `ActorUserId = actorUserId` and the same UTC `now`.
  - **ReviewDecision** targets the proposal, with `Field = "ReviewState"`, `OldValue = "Pending"`, and `NewValue` = the new state name. A Rejected decision with a reason has `NewValue = "Rejected: {reason}"`.
  - Its `Sequence` is `ActionRevision.FirstSequence + 1`. A Pending proposal has exactly its AiProposal revision (sequence 1) by construction.
  - **FieldEdit** rows target the Tracked Action, in the fixed order Description, OwnerUserId, DueDate, and only for fields that changed.
  - Their sequences continue from the ReviewDecision's (3, 4, 5…). The Audit Trail reads proposal and Tracked Action together by `OccurredAt` then `Sequence`, so every FieldEdit must sort after the decision.
  - FieldEdit values are text: the proposal's description; owner ids in `Guid` "D" format, or null; due dates as `yyyy-MM-dd` (invariant culture), or null.
- `RevisionKind` gains `ReviewDecision` and `FieldEdit`. `RevisionTargetType` gains `TrackedAction`. New `ActionRevision` factories stay `internal`.
- `ActionStatus { Open, InProgress, Complete, Cancelled }` is declared whole (prd.md:76), following `ReviewState`'s precedent.
- Migration `AddTrackedActions` is generated, never hand-written. It creates `tracked_actions`:
  - `id`, `proposed_action_id` (required, FK → `proposed_actions` Restrict, unique), and `description` varchar(500).
  - `owner_user_id` (nullable, FK → `users` Restrict), `due_date` date, `status` varchar(32), and `created_at` timestamptz.
  - An `xmin` row-version, and non-unique indexes `(due_date, status)` and `(owner_user_id)`.
  - It also adds nullable `decided_by_user_id` uuid, `decided_at` timestamptz, and `rejection_reason` varchar(500) to `proposed_actions`, with no FK (it matches `started_by_user_id`).

**Never:**

- No handler, repository port, endpoint, DTO, `openapi.json`, outbox, or Web change. Those belong to 3.2 through 3.5.
- `TrackedAction` has no public constructor or factory. `Decide` is the only creator.
- No `Transition` or `Edit` on `TrackedAction`. Those belong to Epic 4.
- No new `action_revisions` column, and no change to its index (AD-7 fixes the columns).
- Existing migrations stay unedited.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Approve | Pending; edits equal proposal, owner = ProposedOwnerUserId | Approved; TA Open with proposal values; 1 revision (ReviewDecision seq 2, Pending→Approved); event raised | — |
| Approve unassigned | ProposedOwnerUserId null, OwnerUserId null | Approved; TA OwnerUserId null | — |
| Edit all three | Pending; all differ | Edited; TA edited values; revisions seq 2 (decision) then FieldEdit Description 3, OwnerUserId 4, DueDate 5; same OccurredAt | — |
| Edit owner only | Owner cleared (match existed) | Edited; one FieldEdit OwnerUserId old=match id, new=null | — |
| Reject with reason | `"  not an action "` | Rejected; no TA; reason `not an action`; NewValue `Rejected: not an action` | — |
| Reject no reason | Reason null or blank | Rejected; RejectionReason null; NewValue `Rejected` | — |
| Kind/edit mismatch | Approved with a diff; Edited with none | — | `DomainRuleException` |
| Bad input | blank/501-char description (Approve/Edit); reason on Approve/Edit; 501-char reason; empty actor | — | `DomainRuleException`, proposal still Pending |
| Already decided | Any kind on Approved/Edited/Rejected | — | `DomainRuleException`, no state change |

</intent-contract>

## Code Map

- `src/ActionLedger.Domain/Extraction/ProposedAction.cs` -- child entity of `ExtractionRun`, not a root. It has an `internal` ctor and a `RequireText` helper to reuse for the description bound. Add `Decide`, the three decision-copy properties, and `RejectionReasonMaxLength`. Refresh the "Story 3.2's Decide" remarks to 3.1.
- `src/ActionLedger.Domain/Extraction/ReviewState.cs` -- enum already complete. Refresh the remarks only.
- `src/ActionLedger.Domain/Extraction/` -- new `DecisionKind.cs` (Approved, Edited, Rejected), `DecisionEdits.cs`, and `DecisionResult.cs` (`Kind`, `TrackedAction?`, `IReadOnlyList<ActionRevision> Revisions`), per spine :277.
- `src/ActionLedger.Domain/Actions/ActionRevision.cs:111-121` -- the `AiProposal` factory is the template for `internal static ReviewDecision(...)` and `FieldEdit(...)`. Update the class remarks.
- `src/ActionLedger.Domain/Actions/RevisionKind.cs`, `RevisionTargetType.cs` -- add the members and refresh the remarks.
- `src/ActionLedger.Domain/Actions/` -- new `TrackedAction.cs` (`: AggregateRoot`, EF private ctor), `ActionStatus.cs`, and `TrackedActionCreated.cs` (`sealed record : DomainEvent`).
- `src/ActionLedger.Domain/Common/AggregateRoot.cs` -- `Raise` is protected, and `DomainEvent.OccurredAt` is `required`.
- `src/ActionLedger.Domain/Extraction/ExtractionRun.cs:688-720` -- the style reference (stamp `now.ToUniversalTime()` once, build the revision list). Read-only.
- `src/ActionLedger.Infrastructure/Persistence/AppDbContext.cs` -- add `DbSet<TrackedAction> TrackedActions`, and update the xmin comment at :69-77 (TrackedAction now has its token).
- `src/ActionLedger.Infrastructure/Persistence/Configurations/ProposedActionConfiguration.cs` -- the template for the new `TrackedActionConfiguration.cs`. It uses the `ConcurrencyTokenProperty` shadow `xmin` `.IsRowVersion()`, `Ignore(DomainEvents)` (see `ExtractionRunConfiguration`), and public index-name constants. Map the three new proposal columns here.
- `src/ActionLedger.Infrastructure/Persistence/ModelConventions.cs` -- snake_case, string enums, `date`, and `ValueGeneratedNever`, all applied automatically.
- `src/ActionLedger.Infrastructure/Migrations/` -- generate with `dotnet tool restore` then `dotnet dotnet-ef migrations add AddTrackedActions --project src/ActionLedger.Infrastructure --startup-project src/ActionLedger.Api`, with the Api's `ValidateOnStart` keys (for example `Database__ConnectionString`, `Jwt__Key`, `Jwt__Issuer`) set in the environment.
- `tests/Infrastructure.Tests/ExtractionPersistenceTests.cs:85-110` -- the proposed_actions column-set assertion. It must list the three new columns. Mirror its helpers (`MigratedDatabaseAsync`, `ColumnsAsync`, the index lookup at :229) for the tracked_actions checks.
- `tests/Domain.Tests/Extraction/ExtractionRunTests.cs` -- the test style. It builds a run and proposals via `ExtractionRun.Start(...).AddProposals(...)`, so `Decide` tests obtain a Pending proposal that way.

## Tasks & Acceptance

**Execution:**

- `src/ActionLedger.Domain/Actions/{ActionStatus,TrackedAction,TrackedActionCreated}.cs`, `RevisionKind.cs`, `RevisionTargetType.cs`, `ActionRevision.cs` -- the Tracked Action root (internal creation, event raised), the enum members, and the two internal revision factories -- AD-3, AD-4, AD-7.
- `src/ActionLedger.Domain/Extraction/{DecisionKind,DecisionEdits,DecisionResult}.cs`, `ProposedAction.cs`, `ReviewState.cs` -- `Decide` with every rule in Always -- FR-11, FR-12, FR-13, FR-16, FR-21.
- `src/ActionLedger.Infrastructure/Persistence/Configurations/TrackedActionConfiguration.cs`, `ProposedActionConfiguration.cs`, `AppDbContext.cs` -- the mapping per Always -- AD-20.
- `src/ActionLedger.Infrastructure/Migrations/*_AddTrackedActions*.cs` and `AppDbContextModelSnapshot.cs` -- generated -- AD-17.
- `tests/Domain.Tests/Extraction/DecideTests.cs` -- every I/O matrix row. The 3×3 already-decided theory (each decided state × each kind) proves every Review State transition. Also assert revision order, shared `OccurredAt`, sequences, target types, the event payload, and that a refused call leaves the proposal Pending with a null decision copy.
- `tests/Infrastructure.Tests/ExtractionPersistenceTests.cs` (or a new `TrackedActionPersistenceTests.cs`) -- the migrated schema has the tracked_actions columns, types, unique index, and FKs. The new proposal columns are present. A decided proposal plus its Tracked Action and revisions round-trip through `AppDbContext`, and the decision copy reads back.

**Acceptance Criteria:**

- Given a Pending proposal, when `Decide` runs with each kind, then Approved and Edited return a Tracked Action (status Open) that has raised `TrackedActionCreated` with its id, the proposal's id, and the kind. Rejected returns none. Each result's revisions are the ReviewDecision first, then the FieldEdits, all with one `OccurredAt` and strictly increasing `Sequence`.
- Given a proposal that is not Pending, when `Decide` runs with any kind, then it throws `DomainRuleException`, and its `ReviewState` and decision copy are unchanged.
- Given the migrated database, when the schema is read, then `tracked_actions` exists with a unique `proposed_action_id` index and no stored `xmin` column. Also, `dotnet dotnet-ef migrations has-pending-model-changes` reports none.
- Given the finished change, when `git diff --stat <baseline> -- src` is read, then only Domain, `Infrastructure/Persistence`, and `Infrastructure/Migrations` files appear.

## Spec Change Log

## Review Triage Log

### 2026-09-22 — Review pass
- verdicts: 28 findings — high 0, medium 3, low 11, false 14, maybe-false 0
- findings:
  - `[low]` `[patch]` (blind) Tracked Action sequences start at 3, contradicting `FirstSequence`'s "brand-new target" doc — fixed: `ActionRevision.FirstSequence` doc now says decision FieldEdits continue from the ReviewDecision's sequence.
  - `[false]` `[reject]` (blind) Non-unique `(target_type, target_id, sequence)` index lets two revisions share a sequence — every current writer is guarded: AiProposal targets a brand-new row, and a second decision is stopped by the proposal's `xmin` (now proven by a test). The index is AD-7's and this spec's Never list keeps it unchanged.
  - `[false]` `[reject]` (blind) Caller-supplied `ProposedOwnerUserId` baseline is trusted — by design: Domain cannot see users (AD-9), and the only caller is 3.2's handler, which computes it with `OwnerResolver.Match`. No caller exists yet that could pass a wrong one.
  - `[low]` `[reject]` (blind) `Guid.Empty` owner is not refused in Domain — an empty id is just one case of an unknown user id, which the FK refuses at commit and 3.2's handler must validate anyway. Adding a guard is an extra branch for a case no client produces.
  - `[false]` `[reject]` (blind) Due date is not range-checked — no PRD or architecture rule bounds a due date; any calendar day is valid.
  - `[false]` `[reject]` (blind) Whitespace-only description differences are not pinned — the behaviour is consistent: an ordinal compare against the AI's untrimmed text. Trimming would break Approve for an AI description with trailing whitespace. 3.2's handler derives the kind with the same compare.
  - `[false]` `[reject]` (blind) `(due_date, status)` column order is wrong for status-only filters — the spine's Indexes row prescribes `tracked_action(due_date, status)` verbatim.
  - `[medium]` `[patch]` (blind) The proposal-`xmin` concurrency claim is untested — grouped with the verification-gap finding below; fixed by the same new test.
  - `[false]` `[reject]` (blind) No test shows `TrackedActionCreated` drained after commit — no consumer exists yet. Draining and clearing belong to 3.3's outbox pipeline (AD-8), so nothing mishandles the event today.
  - `[false]` `[reject]` (blind) Internal constructor and factories reachable via `InternalsVisibleTo` — `ActionLedger.Domain` declares no `InternalsVisibleTo` (grep of `src/ActionLedger.Domain` and `Directory.Build.props`), so no other assembly can call them.
  - `[low]` `[reject]` (blind) The allowlist exempts `Decide` by name, so a future overload would pass — requires someone to add a second public `Decide` that rewrites AI fields. Pinning the signature adds reflection complexity for an unlikely case.
  - `[low]` `[reject]` (blind) `DecisionResult` is a public record whose revision list can be cast and mutated — only trusted handler code holds one, and revisions themselves cannot be minted outside Domain. A defensive copy guards a misuse nobody performs.
  - `[false]` `[reject]` (blind) A rejection raises no domain event — AD-8 defines exactly one event, `TrackedActionCreated`. No consumer wants a rejection event.
  - `[low]` `[reject]` (blind) Persistence tests use whole-second instants — the copy and the revision are both truncated to microseconds by PostgreSQL identically, so they still agree. Only a hypothetical sub-second test assertion would notice.
  - `[low]` `[reject]` (blind) `TrackedAction` has no `DescriptionMinLength` constant — cosmetic. Epic 4's `Edit` can add it when it needs it.
  - `[false]` `[reject]` (edge) Padded description counts as an edit — same as the blind whitespace finding: consistent ordinal semantics. Trimming would introduce the Approve bug described there.
  - `[false]` `[reject]` (edge) A padded 1-character description passes the length check — `RequireText` rejects whitespace-only text via `IsNullOrWhiteSpace`, and the length bound is the column's. Padding is stored verbatim, like the AI's own text.
  - `[low]` `[reject]` (edge) `Guid.Empty` owner ids reach the FK — same as the blind finding; 3.2 validates owner existence.
  - `[false]` `[reject]` (edge) Wrong caller baseline hides or fabricates an owner change — same as the blind baseline finding; AD-9 by design.
  - `[low]` `[reject]` (edge) Clock skew could put a decision before its AiProposal — both instants come from one server's `IClock` minutes apart. Guarding or clamping adds a branch for a practically unreachable case.
  - `[false]` `[reject]` (edge) Hard-coded sequence 2 plus a non-unique index allows duplicates — the proposal's `xmin` stops a second decision, now proven by `A_second_decision_on_a_proposal_loaded_before_the_first_committed_is_a_conflict`.
  - `[medium]` `[patch]` (verification-gap) No test runs the proposal's `xmin` guard against a second decision — fixed: added `A_second_decision_on_a_proposal_loaded_before_the_first_committed_is_a_conflict` (late Rejected and late Approved) in `TrackedActionPersistenceTests`. It asserts `ConcurrencyConflictException`, one ReviewDecision, and at most one Tracked Action.
  - `[medium]` `[patch]` (verification-gap) RunsQueries `PendingCount` is indistinguishable from `ProposalCount` in tests — fixed: the run-list test in `RunsQueriesTests` now rejects one of three proposals and asserts ProposalCount 3 and PendingCount 2.
  - `[low]` `[defer]` (verification-gap) Application/Web comments say the decision fields are "Story 3.1's" / "null until Story 3.1" — real but outside this story's allowed `src` surface. Deferred to the story that publishes the fields.
  - `[low]` `[patch]` (verification-gap) `ActionRevisionConfiguration` comment describes a count-based sequence read that does not exist — fixed: the comment now says sequence 2 is fixed by construction and the proposal's `xmin` is what stops a second decision.
  - `[low]` `[patch]` (verification-gap) Tracked Action `xmin` test comment overstates what the `information_schema` assertion can catch — fixed: reworded to credit the migration's CREATE TABLE, matching `ExtractionPersistenceTests`.
  - `[false]` `[reject]` (intent) "Approve unassigned" matrix row has no named test — covered by `Approving_an_unmatched_owner_leaves_the_tracked_action_unassigned` (ProposedOwnerUserId null, OwnerUserId null, asserts a null Tracked Action owner). The row does not concern the due date.
  - `[false]` `[reject]` (intent) The decision-copy agreement test lands here although the epics assign it to 3.2 — not a defect. Testing earlier leaves 3.2's own test free to exercise the handler path.

### 2026-09-22 — Review pass
- verdicts: 24 findings — high 0, medium 1, low 8, false 15, maybe-false 0
- findings:
  - `[false]` `[reject]` (edge) A padded description counts as an edit — carried: consistent ordinal semantics against the AI's untrimmed text. Trimming would break Approve for an AI description with trailing whitespace.
  - `[low]` `[reject]` (edge) A `Guid.Empty` owner reaches the FK — carried: the FK refuses it at commit, and 3.2's handler validates owner existence.
  - `[low]` `[reject]` (edge) A null or blank decision description throws "A Proposed Action's description is required", which names the AI row — true, but a reviewer's client always sends the description it shows. The fix needs a new message parameter on `RequireText` or a separate guard, which is more than a direct correction.
  - `[false]` `[reject]` (blind) The caller-supplied `ProposedOwnerUserId` baseline is trusted — carried: by design, because Domain cannot see users (AD-9), and 3.2's handler supplies the `OwnerResolver.Match` result.
  - `[low]` `[reject]` (blind) `Guid.Empty` is accepted as an owner — carried: same as the edge finding above.
  - `[false]` `[reject]` (blind) The description is never trimmed, but the reason is — carried: the intent names trimming only for Reason, and the ordinal compare is deliberate.
  - `[low]` `[reject]` (blind) Decide's error messages name the Proposed Action — grouped with the edge message finding and rejected on the same reason.
  - `[low]` `[patch]` (blind) The race test asserts `TrackedActions <= 1` where the winner is always a rejection, so a persisted losing approval would pass. It also never runs Approve-vs-Approve — fixed: the assertion is now `Assert.Equal(0, …)`, with a comment. The Approve-vs-Approve half is left out: the database guard for that pair is already proven by `A_second_tracked_action_for_one_proposal_is_refused_by_the_database`.
  - `[false]` `[reject]` (blind) The `(due_date, status)` index column order is wrong — carried: the spine's Indexes row prescribes `tracked_action(due_date, status)` verbatim.
  - `[low]` `[reject]` (blind) The database has no CHECK constraints tying the decision copy to `review_state`, or `status` to its enum — no architecture decision calls for CHECK constraints, and no current writer bypasses `Decide`. Adding them means a migration and a new schema convention for a writer nobody has.
  - `[false]` `[reject]` (blind) Nothing drains `TrackedActionCreated`, and the config comment says "drained inside the commit" — carried: no consumer exists yet, and draining is 3.3's outbox (AD-8). The comment is the one every aggregate configuration shares (`MeetingConfiguration.cs:79`, `ExtractionRunConfiguration.cs:103`, `UserConfiguration.cs:50`) and states the design rule, not a claim about 3.1.
  - `[false]` `[reject]` (blind) The Tracked Action repository port is missing — the intent's Never list forbids a repository port in 3.1, because it belongs to 3.2.
  - `[false]` `[reject]` (blind) The `ActionStatus` "declare whole" remark contradicts the `RevisionKind` "grow with stories" remark — the intent mandates declaring `ActionStatus` whole (prd.md:76), and each enum states its own reason. No caller or rule diverges because of the two remarks.
  - `[low]` `[patch]` (blind) A comment line in `ActionRevisionConfiguration.cs` runs to 141 characters, past the file's wrap width — fixed: the line is reflowed to the file's width.
  - `[low]` `[reject]` (blind) Tests use only whole-second instants — carried: PostgreSQL truncates the copy and the revision identically, so they still agree.
  - `[low]` `[reject]` (blind) There are no tests for an undefined `DecisionKind`, a null `edits`, or an Edited case in the decision-copy agreement theory — the first two are programmer errors that throw standard argument exceptions no client can send through a typed body. The Edited copy is written by the same `Record` call that `DecideTests` already covers. More tests add surface for cases nobody meets.
  - `[false]` `[reject]` (blind) The reflection test misses the internal constructor if `InternalsVisibleTo` is added later — carried: `ActionLedger.Domain` declares no `InternalsVisibleTo`, and the claim needs a future change to become true.
  - `[false]` `[reject]` (blind) The owner FieldEdit's old value is the matcher's guess, not the AI's free text — the intent makes `ProposedOwnerUserId` the baseline for the owner diff explicitly. The AI's free text stays on the proposal and its AiProposal revision.
  - `[medium]` `[defer]` (verification-gap) `MeetingsQueries.ListAsync` still returns a literal `0` for `TrackedActionCount`, and no test seeds a Tracked Action — the literal predates this story, and this change did not cause it. No production path creates a Tracked Action until 3.2's endpoint, so no user sees a wrong count yet. Deferred to the story that exposes decisions (3.2 or 3.4), with the stale "Story 3.1 replaces" comments.
  - `[low]` `[patch]` (verification-gap) The race test's `<= 1` assertion should be exactly 0 — grouped with the blind race-test finding; fixed by the same `Assert.Equal(0, …)`.
  - `[false]` `[reject]` (intent) Audit Trail ordering is proven only against a test-written query — no production Audit Trail reader exists in 3.1. The revision data carries the order the intent requires (sequence plus a shared instant), and the future reader belongs to Epic 4.
  - `[false]` `[reject]` (intent) The owner baseline is tested only with test-supplied values — carried: the intent defers the source of the baseline to 3.2.
  - `[false]` `[reject]` (intent) The persistence tests write through `AppDbContext.TrackedActions`, not a handler — the intent forbids a handler or port in 3.1, so this is the only write path that exists.
  - `[false]` `[reject]` (intent) Narrow readings (no description trimming, an internal constructor, argument exceptions for programmer errors) are not tested against the alternative readings — the auditor found the diff implements the literal reading, and no alternative reading is required.

## Design Notes

**Why FieldEdit sequences continue past the decision.** The Audit Trail is one read over the
proposal's revisions and the Tracked Action's revisions, ordered by `OccurredAt` then `Sequence`.
All of one decision's rows share `OccurredAt`. So a FieldEdit numbered from 1 on its new target would sort ahead of
the ReviewDecision (seq 2). Numbering the FieldEdits from 3 still satisfies "strictly increasing per
target" and puts FR-12's order into the data. Epic 4's `TrackedAction.Transition` and `Edit` must continue
from the Tracked Action's current maximum.

**Why the reason rides in `NewValue`.** FR-13 requires the ReviewDecision revision to record the
reason, and AD-7 fixes the revision columns. FR-12 pins `Field`/`OldValue`/`NewValue` as
`ReviewState`/`Pending`/`<State>`. The one encoding that satisfies all three is `Rejected: {reason}`.
The prefix is fixed, so 3.2's agreement test can recover the reason exactly.

```csharp
DecisionResult r = proposal.Decide(DecisionKind.Edited,
    new DecisionEdits("Book movers", danaId, new DateOnly(2026, 10, 2), ProposedOwnerUserId: null, Reason: null),
    actorId, now);
// r.Revisions: [ReviewDecision seq2 Pending→Edited, FieldEdit seq3 Description, FieldEdit seq4 OwnerUserId, FieldEdit seq5 DueDate]
```

## Verification

**Commands:**

- `dotnet build ActionLedger.sln` -- expected: 0 warnings, 0 errors (warnings are errors).
- `dotnet test ActionLedger.sln` (no `--nologo`) -- expected: all green, including Domain, Infrastructure (Testcontainers), Architecture, and the Api OpenAPI snapshot, which is unchanged.
- `dotnet dotnet-ef migrations has-pending-model-changes --project src/ActionLedger.Infrastructure --startup-project src/ActionLedger.Api` -- expected: no pending changes.


## Auto Run Result

Status: done (follow-up review pass)

**Summary.** `ProposedAction.Decide(kind, edits, actorUserId, now)` is the only way a proposal leaves Pending and the only way a `TrackedAction` is created.
- It returns a `DecisionResult` holding the new Tracked Action (Approved or Edited) and the ordered revisions.
- The revisions are a ReviewDecision (sequence 2, against the proposal), then one FieldEdit per changed field (3, 4, 5, against the Tracked Action). All share one UTC instant.
- `TrackedActionCreated` is raised on the new root.
- The proposal's decision copy is written in the same call.
- The generated migration `AddTrackedActions` adds `tracked_actions` and the three decision-copy columns.

This follow-up pass re-reviewed the whole diff since `28aece9102d25eacf3f4807028a0caa6cfc8a491` and applied two low patches.

**Files changed**
- `src/ActionLedger.Domain/Extraction/ProposedAction.cs`: `Decide`, its kind and edit rules, the decision copy, and `RejectionReasonMaxLength`.
- `src/ActionLedger.Domain/Extraction/{DecisionKind,DecisionEdits,DecisionResult}.cs`: the decision types. `ReviewState.cs`: refreshed remarks.
- `src/ActionLedger.Domain/Actions/{TrackedAction,ActionStatus,TrackedActionCreated}.cs`: the new root (internal constructor), the status enum, and the event.
- `src/ActionLedger.Domain/Actions/{ActionRevision,RevisionKind,RevisionTargetType}.cs`: the internal `ReviewDecision` and `FieldEdit` factories and the new enum members.
- `src/ActionLedger.Infrastructure/Persistence/Configurations/TrackedActionConfiguration.cs`: the table, FKs, indexes, and `xmin`.
- `src/ActionLedger.Infrastructure/Persistence/Configurations/ProposedActionConfiguration.cs`: the decision-copy columns.
- `src/ActionLedger.Infrastructure/Persistence/Configurations/ActionRevisionConfiguration.cs`: the sequence comment. This pass reflowed an over-long line.
- `src/ActionLedger.Infrastructure/Persistence/AppDbContext.cs`: the `TrackedActions` set.
- `src/ActionLedger.Infrastructure/Migrations/20260922180354_AddTrackedActions*.cs`, `AppDbContextModelSnapshot.cs`: generated by `dotnet-ef`.
- `tests/Domain.Tests/Extraction/{DecideTests,ExtractionRunTests}.cs`: every I/O matrix row, and the immutability allowlist.
- `tests/Infrastructure.Tests/TrackedActionPersistenceTests.cs`: the schema, FKs, unique index, round trip, decision-copy agreement, and the concurrent-decision conflict. This pass tightened that test's Tracked Action count to exactly 0.
- `tests/Infrastructure.Tests/ExtractionPersistenceTests.cs`, `tests/Application.Tests/Extraction/RunsQueriesTests.cs`: the new column set, and PendingCount against a decided proposal.

**Review findings (this pass).** 24 findings: high 0, medium 1, low 8, false 15.
- **Patched (2 entries, both low):**
  - The race test's `<= 1` Tracked Action assertion is now `Assert.Equal(0, …)`. The blind and verification-gap layers both reported it, so it is one entry.
  - The 141-character comment line in `ActionRevisionConfiguration.cs` is reflowed.
- **Deferred (1, medium):** `MeetingsQueries.ListAsync` still publishes a literal `0` for `trackedActionCount`. This predates the story, and the intent forbids Application changes. It is added to the spec's `deferred` list.
- **Rejected (21):** each is in the Review Triage Log with its reason.
  - 10 are carried from the first pass: the padded description, the `Guid.Empty` owner (×2), the trusted owner baseline, trimming, the index order, draining, whole-second instants, `InternalsVisibleTo`, and the owner baseline in tests.
  - The rest are newly rejected:
    - Decide's error message names the Proposed Action. Low, found by edge and blind; the fix is not a direct correction.
    - No CHECK constraints. Low; no architecture decision calls for them.
    - The missing repository port is forbidden by the intent.
    - The enum-remark contradiction has no named harm.
    - The missing refusal tests cover programmer errors only.
    - The owner FieldEdit baseline is set by the intent.
    - The four intent-alignment gaps describe the literal reading the intent requires.

**Follow-up review recommendation:** `false`. This is a follow-up pass, and it patched no `high` entry. Patched counts: high 0, medium 0, low 2.

**Verification**
- `dotnet build ActionLedger.sln`: 0 warnings, 0 errors.
- `dotnet test ActionLedger.sln`: 1410 of 1410 passed, 0 failed (Testcontainers PostgreSQL included).
- `dotnet dotnet-ef migrations has-pending-model-changes`: "No changes have been made to the model since the last migration." This needs a design-time `Database__ConnectionString` environment variable; any syntactically valid value works, and no database is contacted.

**Residual risks**
- `ProposedOwnerUserId` is trusted as supplied, so 3.2's handler must pass the `OwnerResolver.Match` result.
- An unknown or empty owner id surfaces only as an FK violation at commit, until 3.2 validates it.
- Epic 4's `Transition` and `Edit` must number revisions on from the Tracked Action's current maximum sequence.
- The `Rejected: {reason}` encoding is a convention that 3.2 and Epic 4's Audit Trail must parse by its fixed prefix.
- The Meeting List's `trackedActionCount` stays `0` until the deferred MeetingsQueries fix lands.
- No operator actions are owed.
