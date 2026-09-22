---
title: 'Story 2.6 — Run extraction from Meeting Detail and inspect Run Detail'
type: 'feature'
created: '2026-09-22'
baseline_revision: '74644eaa32b3c5bd2e867a24bee44a02396c892c'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-2-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-ActionLedger-2026-09-20/EXPERIENCE.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Story 2.5 made runs and proposals exist on the API, but nothing in the web app can start
a run or show one. Meeting Detail ends at the notes block, there is no endpoint that lists a
meeting's runs, `IAiProviderInfo` is registered but served by no endpoint, and there is no Run Detail
page — so an Action Officer cannot see what produced a proposal before reviewing it (FR-4 UI, FR-6,
FR-7, FR-9; epics.md:475-497).

**Approach:** Add two read endpoints — `GET /api/v1/ai/provider` (`{ provider, model }`) and
`GET /api/v1/meetings/{id}/runs` (`RunSummaryDto[]`) — then extend Meeting Detail with the Run
extraction button, its in-flight caption, and a run list, and add a Run Detail page at
`/meetings/{meetingId}/runs/{runId}` with the FR-6 metadata definition list, expandable warnings,
a "Run again" button on Failed runs, and the proposal rows in AI order with a shared
`ReviewStateChip`.

## Boundaries & Constraints

**Always:**

- **Epic AC copy verbatim.** No-notes caption: "Add notes first" as visible text beneath the disabled
  button (never a tooltip). In-flight caption: "Extracting with {provider} · {model}. This can take
  up to a minute with a local model." with `{provider}`/`{model}` from `GET /api/v1/ai/provider`.
  Empty run list: "No extraction runs. Add notes, then run extraction." (EXPERIENCE.md:113).
- **Nothing else is blocked while a run is in flight.** Only the Run extraction button disables;
  notes, run-list rows, and navigation stay interactive. The page shows a `MudProgressLinear`
  (indeterminate) with the caption beneath the button.
- **Post-run navigation:** a 201 with `Outcome = Succeeded` navigates to Run Detail for the new run.
  A 201 with `Outcome = Failed` stays on Meeting Detail and reloads the run list, where the Failed row
  shows the server's `failureReason` verbatim.
- **Run Detail metadata is a two-column definition list** (`<dl>`, not a table; DESIGN.md:202) with
  every FR-6/AD-6 field: AI Provider, model, Prompt Version, schema version, started
  (`Formats.Instant`), duration (`{n} ms`), input tokens, output tokens, outcome, failure reason
  (only when Failed), warnings. Token counts render the integer, so `0` renders as "0", never blank.
- **Warnings** show their count in the list and expand (`MudExpansionPanels`) to one line per
  warning string, rendered verbatim — the server's string already embeds the dropped excerpt.
- **Proposal rows are in `ordinal` order** (the API already orders them) with description,
  confidence (`0.00`), a "Low confidence" icon-plus-text badge when `isLowConfidence`, a
  `ReviewStateChip`, and three visible cells — decider display name, decision timestamp, rejection
  reason — that render empty today because `ProposedActionDto` has no decision fields until Story 3.1.
- **Every indicator pairs an icon or text with color**; dates `yyyy-MM-dd`, instants
  `yyyy-MM-dd HH:mm UTC` via `Formats`; microcopy lives in `Voice` and is pinned in
  `VoiceAndFormatsTests`.
- **Both new endpoints are bare `[Authorize]`** (any authenticated role reads; anonymous is 401
  before any lookup) and under the `api/v1` convention prefix.
- HTTP only in `Core/` and `Features/*/Data/` services; pages consume web-owned records and enums,
  never generated DTOs (`WebStructureTests`).

**Never:**

- No decision, review, or Review Screen work (Epic 3). No "Review" button: every run-list row opens
  Run Detail; Story 3.4 retargets navigation.
- No new fields on `ProposedActionDto`, `RunDetailDto`, `RunDto`, or `MeetingDetailDto`; no Domain or
  migration change.
- No edit to `prompts/`, `fixtures/`, `.env.example`, `appsettings.json`, `docker-compose.yml`, or any
  `ActionLedger.Infrastructure` file.
- No client-side timeout on the run POST (spine :192), and no client-side recomputation of
  `isLowConfidence` or owner match.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Provider info | Authenticated GET `/api/v1/ai/provider`, Fake configured | 200 `{ provider: "Fake", model: "fixture-catalog" }` | Anonymous → 401 |
| Run list, runs exist | Meeting with 2 runs (one Succeeded with 3 proposals, one Failed) | 200 array newest `startedAt` first; each item has id, startedAt, promptVersion, provider, model, outcome, failureReason, proposalCount, pendingCount (3/3 and 0/0) | No error expected |
| Run list, no runs | Meeting exists, no runs | 200 `[]` | No error expected |
| Run list, unknown meeting | id matches nothing | 404 ProblemDetails `not-found` | Anonymous → 401 first |
| No notes | Meeting Detail, `Notes` null | Run extraction button disabled; caption "Add notes first" visible; empty run-list text shown | No error expected |
| Run succeeds | Notes present, POST 201 Succeeded | Button disabled + progress + provider caption while in flight; then navigate to `/meetings/{id}/runs/{runId}` | No error expected |
| Run fails | POST 201 Failed | Stay on page; run list reloads; Failed row shows the reason verbatim; button re-enabled | No error expected |
| POST transport/5xx/400 failure | POST throws / non-201 | Stay on page, button re-enabled, error snackbar with Retry (same pattern as notes save) | 401/403/409 snackbars come from `SessionMessageHandler` as today |
| Provider info unavailable | GET ai/provider fails | Page still loads; in-flight caption is `Voice.ExtractingWithoutProvider` | Not a load failure |
| Run Detail, Succeeded | Run with 0 input/output tokens, 1 warning, 2 proposals | Tokens show "0"; warnings count "1" expands to the warning text; 2 proposal rows in ordinal order with Pending chips | No error expected |
| Run Detail, Failed | Failed run | Failure reason verbatim + "Run again"; clicking it POSTs for the run's meeting and navigates to the new run's detail (either outcome) | Error snackbar with Retry on failure |
| Run Detail, unknown run or wrong meeting | GET 404, or `run.MeetingId != route meetingId` | `NotFoundNotice` | Load failure otherwise → `LoadFailure` with Retry |

</intent-contract>

## Code Map

**API (existing, extend):**

- `src/ActionLedger.Application/Abstractions/IAiProviderInfo.cs:14-24` — `string Provider`, `string Model`; doc already names this story. Registered singleton at `src/ActionLedger.Infrastructure/InfrastructureRegistration.cs:128` (read-only here).
- `src/ActionLedger.Application/Extraction/RunDtos.cs:18, :49-66` — `RunDto`, `RunDetailDto`; `RunSummaryDto` joins this file (spine AD-13 :132 names `RunSummaryDto { proposalCount, pendingCount }`).
- `src/ActionLedger.Application/Extraction/RunsQueries.cs:23` — `RunsQueries(IReadDb, ProposedActionReadModel)` with `GetAsync`; add the list query here. Copy the correlated-count pattern from `src/ActionLedger.Application/Meetings/MeetingsQueries.cs:27-64` (hoisted `IQueryable<ExtractionRun>`, total ordering, 404 via `NotFoundException("Meeting", id)`).
- `src/ActionLedger.Api/Controllers/MeetingsController.cs:129-157` — `StartRun` is the house style for the new `ListRuns` action (EndpointName/Summary/Description, `ProducesResponseType` per status with `application/problem+json` on error arms, `<response>` docs).
- `src/ActionLedger.Api/Controllers/RunsController.cs:47-61` — template for the new `AiController` (`[Route("ai")]`, `[Tags("Ai")]`, bare `[Authorize]`).
- `src/ActionLedger.Web/openapi.json` — regenerate with `dotnet run --project src/ActionLedger.Api -- --export-openapi`; the Web build regenerates `IActionLedgerApiClient` (NSwag target `ActionLedger.Web.csproj:42-54`), producing `ListExtractionRunsAsync` / `GetAiProviderAsync` from the `EndpointName`s.

**API tests that pin the surface:**

- `tests/Api.Tests/NotesImmutabilityTests.cs:64-82` — exact meeting-operation set ("five"); add `get /meetings/{id}/runs`, rename to "six".
- `tests/Api.Tests/MeetingsEndpointTests.cs:35-61, :133-137` — route-pattern set (likely unchanged; reword comment if stale) and the anonymous InlineData list (add `GET /{id}/runs`).
- `tests/Api.Tests/RunsEndpointTests.cs` — `ExtractionStore` fake + `MeetingWithNotesAsync`; home for run-list endpoint tests. `:378` anonymous theory.
- `tests/Api.Tests/AiRegistrationTests.cs:24-37` — Fake/`fixture-catalog` pin; new endpoint test sits beside it or in a new `AiProviderEndpointTests.cs`.
- `tests/Api.Tests/OpenApiSnapshotTest.cs:16`, `AuthDisciplineTests.cs:32-60`, `OpenApiContractTests.cs` — automatic; stay green after regeneration.
- `tests/Application.Tests/Extraction/RunsQueriesTests.cs` — existing fakes for `RunsQueries`; add list-query tests here.
- `tests/Architecture.Tests/ActorIntegrityTests.cs:92` — a query parameter may not be an actor id.

**Web (existing, extend):**

- `src/ActionLedger.Web/Features/Meetings/MeetingDetailPage.razor` — `@page "/meetings/{Id:guid}"`; notes block ends at `:103`; public const ids `:113-142`; `SaveNotesAsync` `:197-285` + `RetrySaveAsync` `:294-298` are the in-flight-guard and error-snackbar-with-Retry pattern to mirror. `LoadAsync` + `LoadFailure`/`NotFoundNotice` at `:26, :39`.
- `src/ActionLedger.Web/Features/Meetings/Data/MeetingsService.cs` — `MeetingsService(IActionLedgerApiClient)`, private `CallAsync<T>` `:124-148`, `MeetingOutcome<T>` `:237-254`, `ToDate` conversion. Gains run-list, start-run, and provider calls.
- `src/ActionLedger.Web/Features/Meetings/MeetingListPage.razor:45-106` — `MudTable` exemplar: `OnRowClick`, `<a href>` in first cell for keyboard/Enter, `NoRecordsContent`.
- `src/ActionLedger.Web/Features/Review/Data/` — only `.gitkeep`; `ReviewService.cs` goes here.
- `src/ActionLedger.Web/Program.cs:24-25` — scoped service registrations; add `ReviewService`.
- `src/ActionLedger.Web/Core/ApiClientRegistration.cs:50-59` — `HttpClient` built with the .NET default 100 s `Timeout`; the run POST can legitimately take up to 180 s (spine :192).
- `src/ActionLedger.Web/Core/Voice/Voice.cs` — every microcopy constant; `Voice.Runs` exists. `src/ActionLedger.Web/Core/Formatting/Formats.cs` — `Date`, `Instant`.
- `src/ActionLedger.Web/Shared/` — `LoadFailure`, `NotFoundNotice`, `ConfirmDialog`; `ReviewStateChip.razor` joins them. Chip tokens: `wwwroot/css/tokens.css` (`--al-primary-container`, `--al-success-container`, `--al-neutral-container` + `on-` pairs); DESIGN.md:114-126, :211 fix the mapping (Pending→primary-container, Approved→success-container, Edited→success-container + `edit` icon, Rejected→neutral-container). Chip classes go in `wwwroot/css/app.css`.
- `src/ActionLedger.Web/Layout/MainLayout.razor:35-40` — the global `LoadingState` bar also shows during the run POST; that is accepted (it blocks nothing).

**Web tests:**

- `tests/Web.Tests/StubApiClient.cs:91-97, :292` — run operations throw `NotExercisedYet()`; implement them with the file's per-operation pattern (`XCalls`, `LastX`, `XThrows`, `XGate`, response property) and add the two new interface methods.
- `tests/Web.Tests/MeetingDetailPageTests.cs` — `BunitContext` setup, `RenderDetail()`, `SignIn()`, id-based lookups, `XGate` for in-flight assertions, `Navigation` for URL assertions.
- `tests/Web.Tests/MeetingsServiceTests.cs` — plain xUnit service tests over `StubApiClient`.
- `tests/Web.Tests/ShellTests.cs:232` — route-template test pattern for the Run Detail route.
- `tests/Web.Tests/VoiceAndFormatsTests.cs:22-58, :86-98` — every `Voice` constant needs a pinned row; the no-emoji test allows ASCII + U+2019 only.
- `tests/Architecture.Tests/WebStructureTests.cs:50, :97` — HTTP namespaces rule; routable components must be `*Page` directly in a feature namespace.

## Tasks & Acceptance

**Execution:**

- `src/ActionLedger.Application/Extraction/RunDtos.cs` -- add `public sealed record RunSummaryDto(Guid Id, DateTimeOffset StartedAt, string PromptVersion, string Provider, string Model, ExtractionOutcome Outcome, string? FailureReason, int ProposalCount, int PendingCount)` with per-member docs -- the run-list row shape the spine names.
- `src/ActionLedger.Application/Ai/AiProviderDto.cs` -- `public sealed record AiProviderDto(string Provider, string Model)` -- FR-7 wire shape.
- `src/ActionLedger.Application/Extraction/RunsQueries.cs` -- add `ListForMeetingAsync(Guid meetingId, CancellationToken)`: 404 when the meeting does not exist, else runs of that meeting ordered `StartedAt` desc then `Id` desc, counts computed in the projection (`PendingCount` = proposals with `ReviewState.Pending`) -- one query class serves both run reads.
- `src/ActionLedger.Api/Controllers/MeetingsController.cs` -- add `[HttpGet("{id}/runs")]` `ListRuns` (`EndpointName("ListExtractionRuns")`) returning `IReadOnlyList<RunSummaryDto>`, 200/401/404 documented -- the run list's data.
- `src/ActionLedger.Api/Controllers/AiController.cs` -- new `[Route("ai")]` controller, `[HttpGet("provider")]` `GetProvider` (`EndpointName("GetAiProvider")`) mapping `IAiProviderInfo` to `AiProviderDto`, 200/401 documented -- FR-7 / AD-11 endpoint.
- `src/ActionLedger.Web/openapi.json` -- regenerate via `--export-openapi` -- contract snapshot and generated client input.
- `tests/Api.Tests/*` -- update `NotesImmutabilityTests` (six operations) and the `MeetingsEndpointTests` anonymous list; add endpoint tests for both new GETs covering the matrix API rows (Fake provider/model body, ordering newest first, both counts, empty list, unknown meeting 404, anonymous 401).
- `tests/Application.Tests/Extraction/RunsQueriesTests.cs` -- list-query tests: ordering, counts, empty list, unknown meeting.
- `src/ActionLedger.Web/Core/ApiClientRegistration.cs` -- set `Timeout = Timeout.InfiniteTimeSpan` on the `HttpClient` with a comment citing spine :192 (the reverse proxy's 200 s is the bound) -- the run POST must not be cut at 100 s.
- `src/ActionLedger.Web/Core/Extraction/RunOutcome.cs`, `src/ActionLedger.Web/Core/Extraction/ReviewState.cs` -- web-owned enums `{ Succeeded, Failed }` and `{ Pending, Approved, Edited, Rejected }` -- pages and the shared chip cannot see generated enums.
- `src/ActionLedger.Web/Core/Voice/Voice.cs` -- add: `RunExtraction` "Run extraction", `AddNotesFirst` "Add notes first", `ExtractingWithPrefix` "Extracting with ", `ProviderModelSeparator` " · ", `ExtractingSuffix` ". This can take up to a minute with a local model.", `ExtractingWithoutProvider` "Extracting. This can take up to a minute with a local model.", `NoRuns` "No extraction runs. Add notes, then run extraction.", `RunAgain` "Run again", `LowConfidence` "Low confidence", `NoProposals` "The AI found no actions in these notes.", `ExtractionFailedPrefix` "Extraction failed. ", plus the column/term labels the pages render (Started, Prompt Version, AI Provider, Model, Schema version, Duration, Input tokens, Output tokens, Outcome, Failure reason, Warnings, Proposals, Pending, Description, Confidence, Review state, Decided by, Decided, Rejection reason, Extraction run, Back to meeting, Dropped excerpts) -- UX-DR20 vocabulary.
- `tests/Web.Tests/VoiceAndFormatsTests.cs` -- pin every new constant; widen the allowed non-ASCII set to U+2019 and U+00B7 (the middle dot EXPERIENCE.md:89 writes), updating the comment and message -- the caption copy requires it.
- `src/ActionLedger.Web/Features/Meetings/Data/MeetingsService.cs` -- add `ListRunsAsync(Guid)` → `MeetingOutcome<IReadOnlyList<RunListItem>>`, `StartRunAsync(Guid)` → `MeetingOutcome<StartedRun>` (`StartedRun(Guid Id, RunOutcome Outcome)`), `GetProviderAsync()` → `MeetingOutcome<ProviderInfo>` (`ProviderInfo(string Provider, string Model)`), all through `CallAsync`; `RunListItem` mirrors `RunSummaryDto` with web enums -- HTTP confined to the data service.
- `src/ActionLedger.Web/Features/Meetings/MeetingDetailPage.razor` -- load runs and provider info with the detail (runs failure → existing `LoadFailure`; provider failure → keep `null`); after the notes block add an "Extraction runs" section: Run extraction button (disabled when no notes or in flight) with "Add notes first" caption when no notes, in-flight `MudProgressLinear` + caption, `MudTable` (Started link to Run Detail, Prompt Version, `{provider} · {model}`, outcome text with the Failed row's reason rendered as `ExtractionFailedPrefix + reason`, Proposals, Pending; row click and Enter open Run Detail; `NoRecordsContent` = `NoRuns`); run handler per the matrix; saving notes enables the button without a reload; public const ids for every new element -- UX-DR13/UX-DR14.
- `src/ActionLedger.Web/Shared/ReviewStateChip.razor` + `wwwroot/css/app.css` -- `[Parameter] ReviewState State`; `MudChip` with text = state name, class per DESIGN.md token mapping, `edit` icon for Edited -- UX-DR6.
- `src/ActionLedger.Web/Features/Review/Data/ReviewService.cs` -- `ReviewService(IActionLedgerApiClient)` with `GetRunAsync(Guid runId)` → `ReviewOutcome<RunDetail>` and `StartRunAsync(Guid meetingId)` → `ReviewOutcome<Guid>`; its own private `CallAsync` and `ReviewOutcome<T>` mirroring `MeetingOutcome<T>` (no cross-feature reference); `RunDetail` / `RunProposal` web records carry every displayed field, with `RunProposal.DecidedBy`, `DecidedAt`, `RejectionReason` always `null` until Story 3.1 adds them to the contract -- Run Detail data.
- `src/ActionLedger.Web/Program.cs` -- register `ReviewService` scoped.
- `src/ActionLedger.Web/Features/Review/RunDetailPage.razor` -- `@page "/meetings/{MeetingId:guid}/runs/{RunId:guid}"`, h1 "Extraction run", back link to the meeting, `<dl>` metadata per the Always rules, warnings expansion, Failed reason + Run again (in-flight disabled, navigate to new run on 201), proposals `MudTable` in received order (empty → `NoProposals`), `NotFoundNotice`/`LoadFailure` per the matrix, public const ids -- FR-9.
- `tests/Web.Tests/StubApiClient.cs` -- implement the run operations and the two new ones.
- `tests/Web.Tests/RunDetailPageTests.cs`, `ReviewServiceTests.cs`, `ReviewStateChipTests.cs`, plus new cases in `MeetingDetailPageTests.cs`, `MeetingsServiceTests.cs`, `ShellTests.cs` -- cover every Web matrix row (tokens "0", warnings expansion, Failed + Run again navigation, wrong-meeting not found, disabled button + caption, in-flight state via a gate, both post-run outcomes, provider-less caption, error snackbar, route template binding, chip text/class/icon per state).

**Acceptance Criteria:**

- Given Meeting Detail with no notes, when the page renders, then the Run extraction button is disabled and "Add notes first" is visible text beneath it.
- Given Meeting Detail with notes, when I click Run extraction, then the button disables, a progress bar shows with "Extracting with Fake · fixture-catalog. This can take up to a minute with a local model.", the notes block and run rows stay interactive, and a Succeeded run navigates to `/meetings/{id}/runs/{runId}`.
- Given a run that returns Failed, when the response arrives, then Meeting Detail stays, and the run list shows that run as Failed with "Extraction failed. {reason}" using the server's reason verbatim.
- Given the run list, when it renders, then its columns are started, Prompt Version, provider and model, outcome, proposal count, and Pending count, newest first, and a row opens Run Detail by click or Enter.
- Given Run Detail, when it renders, then the definition list shows every FR-6 field, tokens read "0" for Fake, warnings expand to each dropped excerpt, a Failed run shows its reason verbatim and "Run again", and proposal rows appear in AI order with a `ReviewStateChip` and visible (empty) decider, timestamp, and rejection-reason cells.
- Given the solution, when `dotnet test ActionLedger.sln` runs, then every assembly is green including new bUnit tests for `RunDetailPage` and `ReviewService` in `tests/Web.Tests`.

## Spec Change Log

## Review Triage Log

### 2026-09-22 — Review pass
- verdicts: 32 findings — high 0, medium 5, low 19, false 8, maybe-false 0
- findings:
  - `[medium]` `[patch]` Blind: snackbar Retry outlives Meeting Detail / Run Detail and POSTs an unseen run after disposal — patched: `RetryRunAsync`/`RetryRunAgainAsync` return early when `disposed`; post-Failed list reload skipped when disposed; one test per page.
  - `[low]` `[reject]` Blind: a failed list re-read after a Failed run swaps the whole page to `LoadFailure` — real but needs a transient GET failure right after a 201; the state is recoverable through Retry, and the fix adds a new branch plus snackbar.
  - `[low]` `[reject]` Blind: `RunDetailPage` reload key ignores `MeetingId` — no in-app link reuses the component with the same run id under another meeting; only a hand-edited URL reaches it; the fix adds a guard.
  - `[low]` `[reject]` Blind: overlapping `LoadAsync` calls can let an older response win — needs Run again then an immediate back navigation inside one GET's latency; the fix adds a request-token guard.
  - `[low]` `[reject]` Blind: infinite `HttpClient.Timeout` applies to every call, not only the run POST — the spec's Design Notes chose it deliberately; nginx's 200 s bounds compose; a hung API outside the proxy is not everyday use and a per-call client is more than a direct correction. Recorded as a residual risk.
  - `[medium]` `[patch]` Blind: `PendingCount`'s Pending filter is never tested with a non-Pending proposal — patched: the Postgres run-list test sets one proposal to Approved by raw SQL and asserts Pending is one below the total.
  - `[low]` `[patch]` Blind: two in-flight assertions (anchor `disabled`, notes element exists) cannot fail — patched: the test now clicks a run row while the POST is gated and asserts navigation to that run's detail.
  - `[false]` `[reject]` Blind: Run Detail can render a dangling "Extraction failed. " for a null reason — `ExtractionRun.Start` refuses a Failed run with a blank reason (`RequireReasonMatchesOutcome`), so a Failed run always carries one; the prefix is EXPERIENCE.md's own "Extraction failed. {reason}".
  - `[low]` `[patch]` Blind: in-flight caption is not announced to screen readers — patched: `role="status"` + `aria-live="polite"` on the caption, asserted in the in-flight test.
  - `[low]` `[reject]` Blind: `ToOutcome` mapping duplicated in `MeetingsService` and `ReviewService` — both are exhaustive switches that throw on an unknown member, so they cannot silently diverge; consolidating is a refactor, not a direct correction.
  - `[false]` `[reject]` Blind: `--al-low-confidence` and `--al-page-gutter` may be undefined — both are defined in `wwwroot/css/tokens.css` (:36, :55).
  - `[low]` `[patch]` Blind: duration and token counts render without group separators — patched: `Integer()` formats `N0` invariantly ("41,250 ms"; 0 stays "0"), with a test.
  - `[low]` `[patch]` Blind: run-list header "AI Provider" labels a "{provider} · {model}" cell — patched: new pinned `Voice.AiProviderAndModel` ("AI Provider and model", EXPERIENCE.md:90) for that header and DataLabel.
  - `[low]` `[reject]` Blind: the documented 400 on `ListRuns` for a malformed id is untested — framework model-binding behaviour shared with every `{id}` route; users never send a malformed id through the UI.
  - `[low]` `[reject]` Edge: infinite timeout on the shared client (same as the Blind timeout row) — same reason.
  - `[medium]` `[patch]` Edge: Meeting Detail start-run failure after disposal leaves a live Retry (same root as the Blind Retry row) — same patch.
  - `[medium]` `[patch]` Edge: Run Detail Run-again failure after disposal leaves a live Retry (same root) — same patch.
  - `[false]` `[reject]` Edge: Meeting Detail offers Retry on a 400/404 start-run — 400 needs a meeting without notes, whose button is disabled, and 404 needs a deleted meeting, which no endpoint can produce; notes are immutable.
  - `[false]` `[reject]` Edge: Run Detail offers Retry on a 400/404 Run again — the run's meeting exists and has notes (a run cannot exist otherwise), and nothing deletes either.
  - `[low]` `[reject]` Edge: reload key ignores `MeetingId` (same as the Blind row) — same reason.
  - `[low]` `[reject]` Edge: overlapping loads race (same as the Blind row) — same reason.
  - `[false]` `[reject]` Edge: null `FailureReason` renders a dangling prefix — same refutation as the Blind row.
  - `[low]` `[reject]` Edge: `Id` changes while a run is in flight and navigation targets the wrong meeting — needs browser history between two Meeting Detail URLs during a run; the page already does not reload on an `Id` change (pre-existing); the fix adds a captured-id guard.
  - `[low]` `[reject]` Edge: failed list re-read after a Failed run swaps to `LoadFailure` (same as the Blind row) — same reason.
  - `[medium]` `[patch]` Verification gap: Pending filter never exercised with a non-Pending proposal (same root as the Blind row) — same patch.
  - `[low]` `[patch]` Verification gap: no page test proves a 403/409 run POST raises no Retry snackbar — patched: a 403/409 theory on each page.
  - `[low]` `[patch]` Verification gap: Run Detail's `if (!disposed)` navigation guard is untested — patched: a gated Run again, disposed, then released, leaves the URI unchanged.
  - `[low]` `[reject]` Verification gap (other): failed list re-read swaps the page to `LoadFailure` (same as the Blind row) — same reason.
  - `[false]` `[reject]` Intent: Web tests exercise `StubApiClient`, never the live API — the epic AC names bUnit tests in `tests/Web.Tests` as the surface; the endpoint-to-client link is pinned by `OpenApiSnapshotTest` and the regenerated NSwag client.
  - `[false]` `[reject]` Intent: "verbatim" is rendered with a fixed prefix — the server's reason is unaltered after EXPERIENCE.md's "Extraction failed. {server-supplied reason}" (:52).
  - `[false]` `[reject]` Intent: spec at `in-progress` and uncommitted — the audit saw the tree mid-review; finalization sets `done` and commits.
  - `[low]` `[patch]` Intent: "nothing else is blocked" was asserted only by element existence (same root as the Blind in-flight-test row) — same patch.

### 2026-09-22 — Review pass
- verdicts: 25 findings — high 0, medium 1, low 13, false 10, maybe-false 1
- findings:
  - `[false]` `[reject]` Blind: Run Detail's "Run again" shows almost nothing while in flight — the button disables and the global `LoadingState` bar (an indeterminate progress bar for every call) shows for the whole POST; the provider caption is specified for Meeting Detail only.
  - `[low]` `[reject]` Blind: a Failed run on Meeting Detail is not announced to screen readers — real, but reaching it takes a screen-reader user and a Failed run (fixture-driven in the demo), and the fix adds a new live region and message branch.
  - `[maybe-false]` `[reject]` Blind: the `role="status"` caption is inserted with its text, so some screen reader and browser pairs may not announce it — settling it needs a real NVDA/VoiceOver pass; if true it is only low (the global progress bar and disabled button remain), so rejected.
  - `[low]` `[reject]` Blind: the "Extraction failed. " prefix repeats the "Failure reason" term and the Failed outcome row; the null fallback differs by page — the null half is refuted as before (`RequireReasonMatchesOutcome`); the prefix is EXPERIENCE.md:52's mandated "Extraction failed. {server-supplied reason}" pattern, so removing it would break the voice rule.
  - `[low]` `[reject]` Blind: `Integer()`/`Confidence()` are private page helpers, not `Formats` — the spec requires `Formats` only for dates and instants; `Formats.Count` is an "n / limit" counter, not a substitute; the harm is possible future drift in Epic 3, and the fix adds public surface plus pins.
  - `[low]` `[patch]` Blind: stale comments — `ReviewService` says "four catch arms" (three exist) and `MeetingsService`'s OCE arm says a timed-out request, which the infinite client timeout ruled out — patched: "the same catch arms" in both summaries; the OCE comment now says an unrequested cancellation (e.g. a torn-down connection).
  - `[low]` `[reject]` Blind: the accepted residual risks never reached `deferred` — the fix is to edit this build's spec.
  - `[false]` `[reject]` Blind: spec frontmatter out of step with the commit — carried: the reviewer saw the spec mid-review (this pass sets `in-review` on entry); finalization sets `done` and commits.
  - `[false]` `[reject]` Blind: the page ordering test cannot catch a re-sort by ordinal — the API already orders by ordinal, so a page that sorted by ordinal renders the same rows; there is no bad outcome.
  - `[low]` `[reject]` Blind: the disposed path after a Failed run is untested — the guarded harm is one stray list GET after the page has gone; negligible, and the fix is a new gated test.
  - `[false]` `[reject]` Blind: Run Detail omits `SourceExcerpt`, suggested owner and due date — the intent enumerates the proposal row cells (description, confidence, low-confidence badge, chip, three decision cells); "what produced a proposal" is the run metadata, and the rest is Epic 3's Review Screen.
  - `[low]` `[reject]` Blind: Meeting Detail makes its three GETs one after another — real, but the added latency is milliseconds against a local API, and parallelising adds concurrency handling to the load path.
  - `[false]` `[reject]` Blind: meeting existence checked with `CountAsync` — the filter is on the primary key, so the count reads at most one row; there is no cost difference from `Any`.
  - `[low]` `[reject]` Blind: the chip CSS test does not check that the `--al-*` tokens exist — they exist today (`tokens.css`); the harm needs a future rename, and the fix is new test parsing logic.
  - `[low]` `[reject]` Blind: the caption's "up to a minute" contradicts the 180 s ceiling — the intent mandates this copy verbatim (Epic AC), so changing it is excluded by the intent.
  - `[medium]` `[patch]` Edge: `RunAgainAsync` dereferences `run.MeetingId` after the POST's await; a back navigation to another run reuses the component and sets `run = null`, so a POST landing before that run's GET throws and ends the circuit — patched: `meetingId` captured before the await and used for both the POST and the navigation.
  - `[low]` `[reject]` Edge: `running` carries over to another run on the reused component and the user is pulled to the new run — needs two consecutive Failed runs plus Back inside one run's latency; the fix adds a load token checked before navigation.
  - `[low]` `[reject]` Edge: a Retry raised on run A and pressed after the component loaded run B starts a run — the route guard keeps both runs under the same meeting, so it POSTs the run the user asked to retry; reaching it needs the same Back-during-failure sequence, and the fix adds a guard.
  - `[low]` `[patch]` Verification gap: a 401 run POST is never tested on either page — patched: `[InlineData(401)]` added to both `A_refusal_the_session_handler_already_announced_offers_no_retry` theories; both pass.
  - `[false]` `[reject]` Intent: "verbatim" is rendered with a fixed prefix — carried: the server's reason is unaltered after EXPERIENCE.md's "Extraction failed. {server-supplied reason}" (:52).
  - `[false]` `[reject]` Intent: token counts use `N0` grouping rather than the raw integer — the integer is rendered, and the spec's stated rule (0 renders "0", never blank) holds; grouping was the prior pass's patch.
  - `[low]` `[reject]` Intent: the infinite timeout covers every call, not just the run POST — carried: the Design Notes chose it deliberately; nginx's 200 s bounds it.
  - `[false]` `[reject]` Intent: "nothing else is blocked" is asserted only for a run-row click — only the Run extraction button binds `running`; notes are read-only once saved and navigation is not gated anywhere, so nothing else is blocked.
  - `[false]` `[reject]` Intent: Web tests exercise `StubApiClient`, never the live API — carried: the epic AC names bUnit tests as the surface; `OpenApiSnapshotTest` and the regenerated client pin the link.
  - `[false]` `[reject]` Intent: ordinal order is checked on the web side only as "order received" — the intent says the API already orders them and forbids client-side recomputation; the API ordering is asserted in `RunsEndpointTests`.

## Design Notes

**Why a run-list endpoint and not runs on `MeetingDetailDto`.** The spine lists `meetings/<id>/runs` as
a route and names `RunSummaryDto`; a separate GET also lets the page reload only the list after a
Failed run without re-reading notes.

**Why the caption is three `Voice` constants.** The provider and model are runtime values; the
page composes `ExtractingWithPrefix + provider + ProviderModelSeparator + model + ExtractingSuffix`
so each literal stays pinned. The middle dot is copy EXPERIENCE.md writes, so the voice rule widens
by exactly that one code point.

**Why decider cells render empty.** The AC wants them "as visible text (empty until Epic 3)", and
`ProposedActionDto` deliberately lacks the fields (its remarks defer them to Story 3.1). The web
record carries nullable slots so Story 3.1 changes only the service mapping.

**Why an infinite client timeout.** The spine fixes "no client-side timeout on the run endpoint"
against a 180 s run ceiling; the generated client shares one `HttpClient`, so the timeout is lifted
on it and nginx's 200 s `proxy_read_timeout` remains the outer bound for every call.

## Verification

**Commands:**

- `dotnet build ActionLedger.sln` and `dotnet build ActionLedger.sln -c Release` -- expected: 0 warnings, 0 errors.
- `dotnet run --project src/ActionLedger.Api -- --export-openapi` -- expected: exit 0; `openapi.json` gains exactly `get /api/v1/ai/provider` and `get /api/v1/meetings/{id}/runs` plus `AiProviderDto` and `RunSummaryDto` schemas.
- `dotnet test ActionLedger.sln` (Docker running) -- expected: all assemblies green, 0 skipped, total above 1142.
- `git diff --stat HEAD -- .env.example src/ActionLedger.Api/appsettings.json docker-compose.yml prompts fixtures src/ActionLedger.Domain src/ActionLedger.Infrastructure` -- expected: empty.

**Manual checks:**

- Read the rendered `RunDetailPage` markup in a bUnit test output: metadata is a `<dl>`, not a `<table>`.

## Auto Run Result

Status: done

**Summary of implemented change.** This was a follow-up review pass on Story 2.6. It kept the story's
change and made three small fixes:

- Run Detail's "Run again" now captures the meeting id before its POST await, so a back navigation to
  another run mid-POST can no longer null-dereference `run` and end the circuit.
- A 401 case is added to each page's "refusal offers no Retry" theory.
- Two stale service comments are corrected.

The story's change itself: `GET /api/v1/ai/provider` and `GET /api/v1/meetings/{id}/runs`; Meeting
Detail's Run extraction button, in-flight caption and run list; the Run Detail page with its FR-6
definition list, warnings, "Run again", and proposal rows with `ReviewStateChip`.

**Files changed in this pass.**

- `src/ActionLedger.Web/Features/Review/RunDetailPage.razor` — `RunAgainAsync` captures `meetingId` before the await.
- `src/ActionLedger.Web/Features/Review/Data/ReviewService.cs` — corrected the "four catch arms" summary.
- `src/ActionLedger.Web/Features/Meetings/Data/MeetingsService.cs` — corrected the catch-arm summary and the OCE-arm comment (the client no longer times out).
- `tests/Web.Tests/MeetingDetailPageTests.cs`, `tests/Web.Tests/RunDetailPageTests.cs` — added `[InlineData(401)]`.

The story's full file list is in commit `cd698da`.

**Review findings breakdown.** 25 findings: high 0, medium 1, low 13, false 10, maybe-false 1.

- **Patched: 3 entries.**
  - Medium (1): the Run again null dereference after component reuse.
  - Low (2): the stale comments; the 401 test rows.
- **Deferred:** none.
- **Rejected, with reasons (see the triage log):**
  - No Run Detail caption (the global progress bar shows).
  - No screen-reader notice of a Failed run.
  - Live-region insertion timing: maybe-false, and low if true.
  - The failure-reason prefix (EXPERIENCE.md:52 mandates it).
  - Private number helpers.
  - Residual risks not deferred (the fix edits the spec).
  - The untested disposed-after-Failed path.
  - Sequential GETs.
  - The chip token check.
  - The caption's "up to a minute" (intent-mandated copy).
  - `running` carrying over on reuse; Retry after reuse.
  - The shared infinite timeout (carried).
  - False: frontmatter mid-review, ordinal re-sort (two findings), omitted proposal fields, `CountAsync`, the verbatim prefix, `N0` grouping, nothing-blocked coverage, and stub-only Web tests.

**Follow-up review recommendation: false.** This was a follow-up pass, and it patched no `high`
entries (0 high, 1 medium, 2 low), so the work has converged.

**Verification performed.**

- `dotnet build ActionLedger.sln` and `-c Release` (both `--no-incremental`): 0 warnings, 0 errors. The outputs are newer than the edits.
- `dotnet run --project src/ActionLedger.Api -- --export-openapi`: exit 0, and `openapi.json` is unchanged.
- `dotnet test ActionLedger.sln` with Docker up: **1308 passed, 0 failed**. That is 1306 plus the two new 401 cases.
- `git diff --stat HEAD` over `.env.example`, `appsettings.json`, `docker-compose.yml`, `prompts`, `fixtures`, Domain and Infrastructure `src`: empty.

**Residual risks.**

- The captured-meeting-id fix has no dedicated regression test. Reproducing it needs a gated POST, then component reuse via navigation to another run, then release. The fix is a local capture, and the build and existing Run again tests pass.
- These are still open from the first pass:
  - The shared `HttpClient` has no timeout outside nginx.
  - No real-browser keyboard, axe or screen-reader pass has been done. This includes the live-region announcement.
  - A transient list re-read failure after a Failed run shows `LoadFailure`.
  - On reused Run Detail components, `running` and the snackbar Retry are not scoped to one run.

