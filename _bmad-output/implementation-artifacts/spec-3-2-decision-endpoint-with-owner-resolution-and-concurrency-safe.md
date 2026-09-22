---
title: 'Story 3.2 — Decision endpoint with owner resolution and concurrency safety'
type: 'feature'
created: '2026-09-22'
baseline_revision: '8321c05b94d95ade1ab476397bca6c1455eec1b9'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** `ProposedAction.Decide` exists (3.1), but nothing calls it. No API lets an Action Officer approve, edit-and-approve, or reject a proposal. So no Tracked Action can be created, and the "one decision per proposal" guarantee has never been exercised over HTTP (epics.md:538-556; FR-11..FR-16, NFR-8; AD-3, AD-9, AD-12, AD-13, AD-20).

**Approach:** Add `POST /api/v1/proposed-actions/{id}/decision`. A new `DecideProposalHandler` loads the proposal's run, derives Approved or Edited from a diff against the proposal and the `OwnerResolver.Match` result, and calls `Decide`. It adds the Tracked Action through a new `IActionRepository.Add` and the revisions through `IActionRevisionRepository.AddRange`, then commits once. Both concurrency losers become 409 `conflict`.

## Boundaries & Constraints

**Always:**

- **Body:** `DecideProposalCommand { decision, description, ownerUserId, dueDate, reason }`. `decision` is a new string enum `ReviewVerb { Approve, Reject }`, which is required. It is the client's *intent* only. The server alone decides between Approved and Edited, and the client can never send `Edited` (reconcile-inputs.md:64). See Design Notes.
- **Actor and time:** the actor is `ICurrentUser.UserId` and `now` is `IClock.UtcNow`, each read once. Neither ever comes from the body. The command has no actor-named member (`ActorIntegrityTests`).
- **Approve verb:**
  - `kind = Approved` when all three hold:
    - `description` equals `proposal.Description` (ordinal compare, untrimmed, like 3.1).
    - `dueDate == proposal.SuggestedDueDate`.
    - `ownerUserId == OwnerResolver.Match(proposal.SuggestedOwner, roster)`.
  - Otherwise `kind = Edited`.
  - Pass `DecisionEdits(description, ownerUserId, dueDate, ProposedOwnerUserId: match, Reason: null)`.
  - The Tracked Action's owner is exactly the sent `ownerUserId` (null = Unassigned). The match is never assigned.
- **Reject verb:** `kind = Rejected`, with `DecisionEdits(null, null, null, null, reason)`. Any description, owner, or due date that was sent is ignored, exactly as `Decide` ignores them.
- **Roster:** non-system Users only, read the same way for the read model and the handler. Extract one shared helper in `Application/Review` (for example `OwnerRoster.ReadAsync(IReadDb, ct)`). `ProposedActionReadModel` uses it too, so "system users excluded" stays a single rule.
- **Handler validation:** these checks run after the load, before `Decide`. Each failure throws `ValidationFailedException` (400 `validation`) and writes nothing.
  - Approve:
    - `description` blank, or over `ProposedAction.DescriptionMaxLength`.
    - A non-blank `reason`.
    - An `ownerUserId` that is not a non-system roster User.
  - Reject: a `reason` over `ProposedAction.RejectionReasonMaxLength`.
  - Model binding already answers a missing or invalid `decision` with its own 400.
- **Order of outcomes:** unknown proposal id → 404 `not-found`. Then validation → 400. Then a non-Pending proposal → `Decide`'s `DomainRuleException` → 409 `conflict`. A lost race → the proposal `xmin` or the `tracked_actions(proposed_action_id)` unique index → `ConcurrencyConflictException` → 409 `conflict`.
- **One commit:** `IActionRepository.Add(trackedAction)` runs only when it is non-null. `revisions.AddRange(result.Revisions)` always runs. Then `IUnitOfWork.CommitAsync` runs exactly once.
- **Response:** 200 with `ProposalDecisionDto { proposedActionId, reviewState, trackedActionId (null when Rejected), decidedByUserId, decidedAt }`.
- **Auth:** a bare `[Authorize]` on the controller. Both roles are writers, as `MeetingsController.cs:20-24` explains. Anonymous → 401.
- **Contract:** add XML docs, `EndpointName("DecideProposedAction")`, and `ProducesResponseType` for 200/400/401/404/409. Regenerate `src/ActionLedger.Web/openapi.json` with `dotnet run --project src/ActionLedger.Api -- --export-openapi`. The web build must still compile.
- **Deferred 3.1 item (medium):** `MeetingsQueries.ListAsync` must return the real per-meeting `trackedActionCount`. Use a correlated count over `TrackedAction` → `ProposedAction` → `ExtractionRun.MeetingId`, with the query roots hoisted the way the run count's already is. Fix the stale "Story 3.1 replaces" comments in `MeetingsQueries`/`MeetingDtos`, and the "Story 3.1's… nothing can write them yet" remark in `ProposedActionDtos.cs`.

**Never:**

- No `POST /tracked-actions`, and no other endpoint that creates a Tracked Action.
- No changes to `ProposedActionDto`'s fields, and no Web code changes. The decision-copy DTO fields and the Review Screen belong to 3.4 and 3.5. The regenerated `openapi.json` is the only Web file that changes.
- No outbox, webhook entities, or migration. Those belong to 3.3. No Domain change: `Decide` is used as-is.
- The client never supplies the kind (Approved or Edited), and never supplies the actor or the time.
- Domain rule messages never reach a caller as a 409 for a request that is really invalid input.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Approve as-is | Pending; Approve with proposal description/due date, owner = match | 200, `Approved`, trackedActionId set; TA owner = match id; 1 revision | — |
| Keep no-match unassigned | Suggested owner matches nobody; ownerUserId null, rest unchanged | 200 `Approved`; TA owner null | — |
| Edit | Approve verb, due date changed | 200 `Edited`; TA has new due date; ReviewDecision then FieldEdit DueDate | — |
| Clear matched owner | Match exists; ownerUserId null | 200 `Edited`; TA owner null (never the match) | — |
| Reject | Reject, reason `"  dup "` | 200 `Rejected`, trackedActionId null; RejectionReason `dup` | — |
| Unknown proposal | random id | 404 not-found | nothing written |
| Bad input | Approve blank/501-char description; Approve + reason; unknown or system owner id; Reject 501-char reason; missing/unknown `decision` | 400 validation | nothing written |
| Already decided | any verb on a decided proposal | 409 conflict | nothing written |
| Race | two decisions posted concurrently on one Pending proposal | one 200, one 409; exactly one TA (or none if the winner rejected) | loser writes nothing |

</intent-contract>

## Code Map

- `src/ActionLedger.Domain/Extraction/ProposedAction.cs:174` -- `Decide(kind, edits, actorUserId, now)`. It is read-only in this story. It throws `DomainRuleException` for non-Pending (→409 via `ProblemDetailsMapping.cs` `ApiExceptionHandler`) and for bad input, which the handler must pre-empt with a 400. Constants `DescriptionMaxLength` and `RejectionReasonMaxLength` are 500.
- `src/ActionLedger.Domain/Extraction/{DecisionKind,DecisionEdits,DecisionResult}.cs` -- the types the handler builds and consumes.
- `src/ActionLedger.Application/Extraction/RunExtractionHandler.cs` -- the handler template: primary-constructor ports, one `clock.UtcNow`, `ICurrentUser`, one `CommitAsync`, and the doc-comment density. The new handler lives at `Application/Review/DecideProposalHandler.cs` (spine :284).
- `src/ActionLedger.Application/Review/OwnerResolver.cs` -- `Match(string?, IReadOnlyList<UserSummaryDto>)`. Its doc already names 3.2's change test.
- `src/ActionLedger.Application/Review/ProposedActionReadModel.cs:84-89` -- the private `RosterAsync` (non-system users). Move it into the shared roster helper.
- `src/ActionLedger.Application/Abstractions/IExtractionRunRepository.cs` + `src/ActionLedger.Infrastructure/Persistence/ExtractionRunRepository.cs` -- add `FindByProposedActionIdAsync(Guid, ct)`. It loads the owning run with its ordered proposals (same `Include`), so the proposal is tracked and its `xmin` is checked at commit.
- `src/ActionLedger.Application/Abstractions/` -- new `IActionRepository` (`void Add(TrackedAction)`, doc per AD-10). New `Infrastructure/Persistence/ActionRepository.cs` over `context.TrackedActions`, registered in `InfrastructureRegistration.cs:49-54`.
- `src/ActionLedger.Application/ApplicationRegistration.cs:22-36` -- register `DecideProposalHandler`.
- `src/ActionLedger.Infrastructure/Persistence/AppDbContext.cs:59-69` + `ConcurrencyTranslation.cs` -- already turn `DbUpdateConcurrencyException` and unique violations into `ConcurrencyConflictException`. Nothing to change.
- `src/ActionLedger.Api/Controllers/RunsController.cs`, `MeetingsController.cs` -- the controller style: `[Route("<resource>")]` with no prefix, `[Tags]`, `EndpointName/Summary/Description`, and `ProducesResponseType` with `application/problem+json`. The new `ProposedActionsController` is `[Route("proposed-actions")]`, `[Tags("Review")]`, `[HttpPost("{id}/decision")]`. `RouteDisciplineTests` / `OpenApiContractTests` may enumerate routes. Keep them green.
- `src/ActionLedger.Application/Meetings/MeetingsQueries.cs:~60-68`, `MeetingDtos.cs`, `src/ActionLedger.Application/Review/ProposedActionDtos.cs:9-15,32` -- the deferred count and the stale comments.
- `tests/Application.Tests/Extraction/RunExtractionHandlerTests.cs` -- the harness and journal pattern (an ordered `Journal.Entries` list proves adds happen before the commit). `tests/Application.Tests/Meetings/MeetingsTests.cs:331-420` -- private fakes (`FakeCurrentUser`, `FakeClock`, `CountingUnitOfWork`, `FakeReadDb` over `OfType<T>()`).
- `tests/Api.Tests/AuthEndpointTests.cs:308-387` (`SeededApi`) -- a real Testcontainers PostgreSQL, migrated, behind `TestApi`. Mirror it for the new decision tests. Create the run and proposals directly through a scoped `AppDbContext`, using `ExtractionRun.Start(...).AddProposals(...)` as `tests/Infrastructure.Tests/TrackedActionPersistenceTests.cs` does. Mint tokens with `TestApi.TokenFor(role, subject: <real user id>)`.
- `tests/Api.Tests/RunsEndpointTests.cs:209-240` -- `The_meeting_list_reports_the_real_run_count_while_tracked_actions_stay_zero`. Rename or reword it, because the count is now real.
- `tests/Infrastructure.Tests/TrackedActionPersistenceTests.cs` -- 3.1's decision-copy agreement test. Extend it so the agreement is also asserted after a commit through the new handler path, for each kind. `tests/Infrastructure.Tests/ReadSeamTests.cs` or `MeetingPersistenceTests.cs` -- the SQL translation of the new count.
- `tests/Architecture.Tests/ActorIntegrityTests.cs` -- scans `*Handler.HandleAsync` parameter types. Do not name a command member `UserId` or `Actor`.

## Tasks & Acceptance

**Execution:**

- `src/ActionLedger.Application/Review/{ReviewVerb,DecideProposalCommand,ProposalDecisionDto,DecideProposalHandler,OwnerRoster}.cs` -- the verb enum, the body, the response, and the handler per Always. The roster helper is shared -- AD-3, AD-9, AD-12, AD-20.
- `src/ActionLedger.Application/Review/ProposedActionReadModel.cs` -- use the shared roster helper. Behaviour does not change.
- `src/ActionLedger.Application/Abstractions/{IActionRepository,IExtractionRunRepository}.cs`, `src/ActionLedger.Infrastructure/Persistence/{ActionRepository,ExtractionRunRepository}.cs`, `InfrastructureRegistration.cs`, `ApplicationRegistration.cs` -- the ports, their implementations, and DI.
- `src/ActionLedger.Api/Controllers/ProposedActionsController.cs` -- the endpoint -- AD-13.
- `src/ActionLedger.Application/Meetings/{MeetingsQueries,MeetingDtos}.cs`, `Review/ProposedActionDtos.cs` -- the real tracked-action count, and the corrected comments.
- `src/ActionLedger.Web/openapi.json` -- regenerated by the export command, never hand-edited.
- `tests/Application.Tests/Review/DecideProposalHandlerTests.cs` -- cover:
  - Every matrix row that the handler owns.
  - Both adds happen before the single commit (journal).
  - The Tracked Action owner is always the sent id, including when the match is non-null and the sent id is null, and when the match is null and the sent id is a user.
  - The actor and the instant come from `ICurrentUser` and `IClock`.
  - Validation failures stage and commit nothing.
- `tests/Api.Tests/DecisionEndpointTests.cs` -- against real PostgreSQL:
  - 200 for each verb, with the response shape.
  - 404, 400 (each bad-input row), 401 anonymous, and 409 on an already-decided proposal, all ProblemDetails with the right `type`.
  - The concurrent test: two `POST`s via `Task.WhenAll` on one Pending proposal (both Approve with different edits) → status codes are exactly {200, 409}, and `tracked_actions` holds exactly one row for that proposal. Repeat a few iterations or proposals so a serialized interleaving and a true race are both likely exercised.
- `tests/Api.Tests/OpenApiContractTests.cs` (or a new test) -- the published document has a `post` under `/api/v1/proposed-actions/{id}/decision`, and no `post` operation on any path starting `/api/v1/tracked-actions`.
- `tests/Infrastructure.Tests/TrackedActionPersistenceTests.cs` -- decision copy equals the ReviewDecision revision (state, actor, instant, reason from `Rejected: {reason}`) after a real handler commit, for Approved, Edited and Rejected. Also a SQL test for the meeting list's `trackedActionCount`: two meetings, and Tracked Actions under one only.
- `tests/Api.Tests/RunsEndpointTests.cs`, `tests/Application.Tests/Meetings/MeetingsTests.cs` -- update the assertions and comments that pinned the literal 0.

**Acceptance Criteria:**

- Given a Pending proposal, when `POST /api/v1/proposed-actions/{id}/decision` is sent with `decision: Approve` and unchanged values, then the response is 200 `Approved` with a `trackedActionId`. Given the same request with any changed field, then the response is 200 `Edited`. In both cases the persisted Tracked Action's owner equals the sent `ownerUserId`.
- Given `DecideProposalHandler` with fakes, when it runs, then `IActionRepository.Add` and `IActionRevisionRepository.AddRange` are both recorded before the one `CommitAsync`, and no path assigns the owner from `OwnerResolver.Match`.
- Given two concurrent decisions on one Pending proposal, when both are posted, then exactly one gets 200 and one gets 409 `conflict`, and exactly one Tracked Action exists for the proposal.
- Given the served OpenAPI document, when it is read, then it has the decision operation and no `POST` on `tracked-actions`. The committed `openapi.json` matches it, so `OpenApiSnapshotTest` passes.
- Given a committed decision of each kind, when the proposal row and its ReviewDecision revision are read from PostgreSQL, then the decision copy agrees with the revision.
- Given a meeting whose proposals have produced Tracked Actions, when `GET /api/v1/meetings` is read, then that meeting's `trackedActionCount` equals the real count, and other meetings' counts are unaffected.

## Spec Change Log

## Review Triage Log

### 2026-09-22 — Review pass
- verdicts: 22 findings — high 0, medium 0, low 13, false 9, maybe-false 0
- findings:
  - `[low]` `[reject]` (blind) A retry after a lost response gets a 409 that does not say whether the caller's own attempt won — FR-11 fixes 409 for a non-Pending proposal. The 3.5 UX answers any 409 by reloading the run ("Already changed. Reloading."), which shows the recorded decision. Adding idempotency keys or echoing the decision in the 409 adds surface for a rare case.
  - `[low]` `[patch]` (blind) The race test covers only Approve against Approve, so the xmin-only path (Reject involved) is never raced over HTTP — fixed: odd attempts now race Reject against Approve. Each asserts {200, 409}, with the Tracked Action count 0 when the Reject won and 1 otherwise. The xmin guard itself is already proven deterministically by 3.1's `A_second_decision_on_a_proposal_loaded_before_the_first_committed_is_a_conflict`.
  - `[false]` `[reject]` (blind) The race test does not prove the loser wrote nothing (revisions) — a partial write cannot happen. The loser's proposal, Tracked Action and revisions go through one `SaveChangesAsync`, which is one transaction, so a refused commit writes none of them.
  - `[false]` `[reject]` (blind) Nothing proves the commit-time conflict path ran against real PostgreSQL — 3.1's `TrackedActionPersistenceTests` proves it deterministically for both the proposal `xmin` and the unique index. The implementer also observed 7 of 8 HTTP races resolved by the unique index.
  - `[false]` `[reject]` (blind) Descriptions are stored untrimmed while rejection reasons are trimmed — carried from 3.1: ordinal, untrimmed description semantics are deliberate. Trimming would turn an unchanged Approve of an AI description with trailing whitespace into an Edit.
  - `[false]` `[reject]` (blind) A whitespace-only rejection reason is unpinned, and a whitespace reason on Approve is silently accepted — `Decide` trims a Reject reason and stores null when it is blank (3.1 DecideTests "Reject no reason: null or blank"). The handler refuses only a non-blank reason on Approve, which matches `Decide`'s own rule.
  - `[low]` `[reject]` (blind + edge) A numeric `decision` (0/1) binds as Approve/Reject although the contract lists only strings — real. It comes from the host-wide `JsonStringEnumConverter`, which allows integers for every enum and predates this story. No client sends numbers, and fixing it for one enum needs a custom converter type.
  - `[low]` `[reject]` (blind) Only ActionOfficer tokens are tested — the controller is a bare `[Authorize]` with no role filter, so no code path distinguishes the roles here. A Lead lockout would need a future policy change that its own story would test.
  - `[false]` `[reject]` (blind) The decider is never checked against Users, so a deleted or Seed actor could decide — Users are never deleted (no delete port or endpoint). The Seed identity cannot sign in (`AuthEndpointTests` system-identity 401). Only this Api mints tokens. So neither actor is reachable.
  - `[low]` `[reject]` (blind) The documented 400 for a malformed id is untested on this route — the same Guid model-binding 400 is pinned on other routes (`RunsEndpointTests.A_malformed_meeting_id_on_the_run_route_is_a_400_validation_with_an_errors_map`) through the shared `InvalidModelStateResponseFactory`.
  - `[false]` `[reject]` (blind) The 200 has no Location and omits decidedByDisplayName/rejectionReason — the architecture's sequence returns 200 with the decision result, and this spec's response shape is `ProposalDecisionDto`. The display fields belong to the read model in 3.4, which the Review Screen reloads.
  - `[low]` `[reject]` (blind) The 200 publishes a `text/plain` media type — the committed contract already had 10 such entries before this story. It is the host-wide MVC formatter default, and changing it touches every operation.
  - `[false]` `[reject]` (blind) The no-POST-tracked-actions test is too narrow (PUT, other prefixes) — the epic asks for exactly "no `POST /tracked-actions`". Epic 4 legitimately adds writes under `tracked-actions/{id}/status`.
  - `[low]` `[reject]` (blind) `trackedActionCount` is a nested correlated subquery with no plan or index test — demo scale (tens of rows per page), and the FK columns it walks are indexed by their FK conventions. A plan test adds nothing for the demo.
  - `[low]` `[reject]` (blind) Each Approve reads the whole roster and the whole run — the roster is a handful of users and a run holds tens of proposals. The run load is what puts the proposal's `xmin` under tracking.
  - `[low]` `[patch]` (blind) An unused `started` client in the contract test — fixed: it is now the `using HttpClient _` discard the other OpenAPI tests use. The two `out _` calls became `out JsonElement _` to avoid CS1657.
  - `[low]` `[reject]` (edge) A NUL character in the description or reason reaches PostgreSQL and 500s — real but pre-existing across every text input (titles, notes, attendees). No browser input produces U+0000, and a guard per field adds branches for input nobody sends.
  - `[low]` `[reject]` (edge) Numeric verbs — grouped with the blind numeric-verb finding above, and rejected on the same reason.
  - `[false]` `[reject]` (intent) The body adds a required `decision` beyond the epic's four fields — a reason-less Reject and an unchanged Approve would otherwise be identical bytes. The planning history's original body carried `kind`, and reconciliation removed only the client-chosen Approved/Edited (Design Notes).
  - `[low]` `[reject]` (intent) Invalid input on a decided proposal is a 400, not the epic's "any decision → 409" — both refuse and write nothing. The web client sends only valid bodies, so a reviewer never sees the difference. Reordering would duplicate `Decide`'s Pending guard in the handler.
  - `[low]` `[patch]` (intent) The concurrency test never races an Approve against a Reject — grouped with the blind race-coverage finding; fixed by the same change.
  - `[false]` `[reject]` (intent) `tests/Web.Tests/StubApiClient.cs` changed although the spec names `openapi.json` as the only Web file — that stub is a test double, not Web app code. The regenerated client interface forces it, and no file under `src/ActionLedger.Web` other than `openapi.json` changed.

## Design Notes

**Why a `decision` verb when the spine's body lists four fields.** An optional-reason Reject cannot be expressed as `{ ownerUserId, dueDate, description, reason }` alone. A reject with no reason and an unchanged approve would be the same bytes. The planning history shows the original body carried `kind`, and reconciliation added `reason` and ruled out a client-supplied *Approved or Edited* classification (reconcile-inputs.md:63-64). A two-value intent (`Approve`/`Reject`) keeps that rule: the Edit button is only a UI mode, and the server still derives Approved or Edited from the diff.

```json
POST /api/v1/proposed-actions/0199…/decision
{ "decision": "Approve", "description": "Book movers", "ownerUserId": null, "dueDate": "2026-10-02", "reason": null }
→ 200 { "proposedActionId": "0199…", "reviewState": "Edited", "trackedActionId": "0199…", "decidedByUserId": "…", "decidedAt": "…" }
```

**Why validation is a 400 before `Decide`.** `Decide` guards its inputs with `DomainRuleException`, and the Api maps that exception to 409. A blank description is a bad request, not a state conflict. So the handler checks every input rule that has a 400 meaning before calling `Decide`, and only "already decided" reaches the domain's 409.

## Verification

**Commands:**

- `dotnet build ActionLedger.sln` -- expected: 0 warnings, 0 errors (warnings are errors).
- `dotnet run --project src/ActionLedger.Api -- --export-openapi` -- run it with the Api's required settings in the environment (for example `Database__ConnectionString`, `Jwt__Key`, `Jwt__Issuer`). Expected: `src/ActionLedger.Web/openapi.json` is updated, and the Web project builds.
- `dotnet test ActionLedger.sln` (no `--nologo`) -- expected: all green, including Application, Api (Testcontainers), Infrastructure (Testcontainers), Architecture, and `OpenApiSnapshotTest`.
- `dotnet dotnet-ef migrations has-pending-model-changes --project src/ActionLedger.Infrastructure --startup-project src/ActionLedger.Api` -- expected: no pending changes. This story adds no migration.

## Auto Run Result

Status: done

**Summary.** `POST /api/v1/proposed-actions/{id}/decision` is live.
- The client sends a verb, `Approve` or `Reject`, plus description, owner, due date and reason.
- `DecideProposalHandler` loads the proposal's run. It checks input rules first, as 400s, then derives Approved or Edited by diffing against the proposal and `OwnerResolver.Match`, and calls `Decide`.
- It adds the Tracked Action (if any) through the new `IActionRepository` and the revisions through `AddRange`, then commits once.
- The owner is exactly the sent `ownerUserId`. The actor and the instant come only from `ICurrentUser` and `IClock`.
- A decided proposal gives 409 from `Decide`. A lost race gives 409 from the proposal `xmin` or the unique index.
- The meeting list's `trackedActionCount` is now real (deferred 3.1 item).

**Files changed**
- `src/ActionLedger.Api/Controllers/ProposedActionsController.cs`: the endpoint.
- `src/ActionLedger.Application/Review/{DecideProposalHandler,DecideProposalCommand,ReviewVerb,ProposalDecisionDto,OwnerRoster}.cs`: the handler, body, verb, response, and shared roster read.
- `src/ActionLedger.Application/Review/{ProposedActionReadModel,ProposedActionDtos}.cs`: the read model uses `OwnerRoster`, and a stale remark is fixed.
- `src/ActionLedger.Application/Abstractions/{IActionRepository,IExtractionRunRepository}.cs` and `src/ActionLedger.Infrastructure/Persistence/{ActionRepository,ExtractionRunRepository}.cs`, `InfrastructureRegistration.cs`, `ApplicationRegistration.cs`: the ports, their implementations, and DI.
- `src/ActionLedger.Application/Meetings/{MeetingsQueries,MeetingDtos}.cs`: the real Tracked Action count.
- `src/ActionLedger.Web/openapi.json`: regenerated by the export command.
- `tests/Application.Tests/Review/DecideProposalHandlerTests.cs`: the handler with fakes (adds-before-commit journal, owner never from the match, validation).
- `tests/Api.Tests/DecisionEndpointTests.cs`: real PostgreSQL. It covers every verb, 404/400/401/409, the 8-attempt race (Approve/Approve and Reject/Approve), and no `POST /tracked-actions`.
- `tests/Infrastructure.Tests/TrackedActionPersistenceTests.cs`: decision copy against revision after a real handler commit for each kind, and the SQL meeting count.
- `tests/Api.Tests/RunsEndpointTests.cs`, `tests/Application.Tests/{Meetings/MeetingsTests,Extraction/RunExtractionHandlerTests}.cs`, `tests/Web.Tests/StubApiClient.cs`: updated for the real count, the new port method, and the regenerated client.

**Review findings.** 22 findings: high 0, medium 0, low 13, false 9.
- **Patched (2 entries, both low):**
  - The race test now also races Reject against Approve. The blind and intent layers both found this, so it is one entry.
  - The unused contract-test client is now a discard.
- **Deferred:** none.
- **Rejected (19):** each has its reason in the Review Triage Log.
  - The rejected lows are: idempotent retry, numeric verbs (blind and edge), Lead-token coverage, malformed-id coverage, `text/plain` media type, count query plan, roster/run load cost, NUL characters, and 400-before-409 on a decided proposal. Each is pre-existing, unreachable in practice, or would need new complexity for a case nobody meets.
  - The falses are: loser partial write, commit path unproven, trimming, whitespace reason, decider not checked, missing response fields, a narrow contract test, the extra `decision` field, and the Web.Tests stub.

**Follow-up review recommendation:** `false`. This is a first pass, and it patched no high or medium entry. Patched counts: high 0, medium 0, low 2.

**Verification**
- `dotnet build ActionLedger.sln`: 0 warnings, 0 errors.
- `dotnet test ActionLedger.sln`: 1460 of 1460 passed, before and after the patches (Testcontainers PostgreSQL included).
- The new classes were run directly and all passed: Api `DecisionEndpointTests` plus the meeting-list test (21), `DecideProposalHandlerTests` (26), and `TrackedActionPersistenceTests` (17).
- `dotnet dotnet-ef migrations has-pending-model-changes`: "No changes have been made to the model since the last migration."
- `openapi.json` was regenerated, and `OpenApiSnapshotTest` passes.

**Residual risks**
- The wire body adds a required `decision` verb that the epic's four-field body did not list. Story 3.5's client must send it.
- A 409 from a lost race is logged by the request-logging middleware as an Error, "responded 500", although the client receives 409. This is pre-existing behaviour for all mapped exceptions, and worth checking before the demo if logs are shown.
- A unique-index 409 detail includes the index name (`ix_tracked_actions_proposed_action_id`). This comes from the existing `ConcurrencyTranslation`.
- The host-wide enum converter accepts integer enum values.
- No operator actions are owed.
