# Epics and Stories Validation Report

Artifact: `_bmad-output/planning-artifacts/epics.md`
Checked against: `ARCHITECTURE-SPINE.md` (AD-1 to AD-21), `EXPERIENCE.md`, and step-04-final-validation sections 1 to 5.
Date: 2026-09-20

Verdict summary

| Check | Result | Notes |
| --- | --- | --- |
| 1. FR coverage | PASS with 5 partials | Every FR has an implementing story; five have an AC gap |
| 2. NFR coverage | PASS with 3 partials | NFR2 rests on 500 seeded actions no story produces |
| 3. UX-DR coverage | FAIL on UX-DR19, partial on UX-DR12, UX-DR20 | Global state patterns are tagged but never implemented |
| 4. Forward dependencies | FAIL (3 unacknowledged) | 2.4 needs 3.1 and 3.3; 3.3 needs a Not found state nobody builds; 4.1 needs 500 actions |
| 5. Architecture compliance | PASS | No AD contradicted; four wording alignments |
| 6. Sizing | FAIL on 8 stories | 2.2 and 3.3 are two to three sessions each |
| 7. Day fit | FAIL on Sunday and Monday | Move 2.3 to Monday; cut FR20 first on Monday |

---

## 1. FR coverage

Implementing story per FR (AC-level, not tag-level):

| FR | Story or stories | Gap |
| --- | --- | --- |
| FR1 | 2.1 AC1 | none |
| FR2 | 2.1 AC2 | none |
| FR3 | 2.1 AC3, AC4; run list in 2.4 AC3 | none |
| FR4 | 2.2 AC3, 2.3 AC1, 2.4 AC2 | Partial: no AC says the run endpoint returns 201 with run id and outcome, or that a second run on the same Meeting is allowed |
| FR5 | 2.2 AC2, 2.2 AC3, 2.3 AC1 | none |
| FR6 | 2.2 AC3, 2.2 AC4 | Partial: `FailureReason` missing from the persisted-field list in 2.2 AC3 |
| FR7 | 2.3 AC1 to AC3 | none |
| FR8 | 2.2 AC1, AC4; 6.2 AC1 | none |
| FR9 | 2.4 AC3 | Partial: FR9 and EXPERIENCE.md put the failure reason and "Run again" on Run Detail; 2.4 puts them only in the run list |
| FR10 | 3.1 AC4, 3.3 AC1, AC4 | Partial: "AI return order" not stated for the Review Screen (it is for Run Detail) |
| FR11 | 3.1 AC1, AC3 | none |
| FR12 | 3.1 AC1 | none |
| FR13 | 3.1 AC2, 3.3 AC4, 2.4 AC3 | none |
| FR14 | 3.1 AC4, 3.3 AC1 | none |
| FR15 | 3.1 AC1, AC4 | none |
| FR16 | 3.1 AC1, 3.2 AC1, 4.1 AC3 (links) | Partial: no AC asserts that no endpoint creates a Tracked Action directly |
| FR17 | 4.1 AC1, 4.3 AC2 | none |
| FR18 | 4.1 AC3, 4.2 AC1, AC2 | Partial: sortable headers on Due date and Status not in 4.2 (only default sort) |
| FR19 | 4.1 AC1, AC3, 4.2 AC1 | none |
| FR20 | 4.1 AC2, 4.3 AC4 | none |
| FR21 | 2.2 AC3, 3.1 AC1, 4.1 AC1, AC2 | Partial: append-only is never asserted (no test that the revision port has no update or delete) |
| FR22 | 4.3 AC1 | none |
| FR23 | 1.3 AC2, 1.4 AC3, AC4 | none |
| FR24 | 1.3 AC3, AC4, 4.1 AC1 | Partial: no AC proves every non-auth route returns 401 without a token |
| FR25 | 1.3 AC3, 6.1 AC1 | none |
| FR26 | 1.2, plus each epic's endpoints | none |
| FR27 | 1.2 AC3, AC4, 1.4 AC1 | none |
| FR28 | 1.2 AC1 | none |
| FR29 | 3.2 AC1, 5.1 AC3, 5.2 AC2 | none |
| FR30 | 3.2 AC1 | none |
| FR31 | 5.1 AC2, AC4 | none |
| FR32 | 5.1 AC2, AC3 | none |
| FR33 | 5.2 | none |
| FR34 | 1.3 AC1, 6.1 AC3 | none |
| FR35 | 6.1 AC1, AC2 | none |
| FR36 | 1.5, 5.2, 6.1 AC3 | none |
| FR37 | 2.2 AC1 | none |
| FR38 | 6.2 AC1, AC2 | none |
| FR39 | 6.2 AC1, AC3, AC4 | Partial: FR39 says the fallback when `LOCAL_AI_BASE_URL` is unset is the committed LM Studio report; 6.2 AC3 only logs "skipped" and never checks the committed report against thresholds |
| FR40 | 7.3 | none (P1) |
| FR41 | 7.2 | none (P1) |
| FR42 | 7.1 | none (P1) |

Findings and fixes:

- **F1.1 (Story 2.2, FR4):** add "returns 201 with `RunDto { id, outcome }` for both Succeeded and Failed, and a Meeting may have any number of runs" to AC3.
- **F1.2 (Story 2.2, FR6):** add `FailureReason` to the persisted-field list in AC3.
- **F1.3 (Story 2.4, FR9):** add to AC3 "a Failed run's Run Detail shows the failure reason verbatim and a Run again button; a run with Pending proposals shows a Review button".
- **F1.4 (Story 3.3, FR10):** add "cards render in AI return order" to AC1.
- **F1.5 (Story 3.1, FR16):** add an `Api.Tests` assertion that the OpenAPI document has no `POST /tracked-actions`.
- **F1.6 (Story 4.2, FR18):** add "Due date and Status headers are sortable and drive `sort` and `dir`" to AC1.
- **F1.7 (Story 2.2, FR21):** add "`IActionRevisionRepository` exposes `AddRange` and an ordered read only" to AC3.
- **F1.8 (Story 1.3, FR24 and NFR5):** add an `Api.Tests` case that walks every operation in the OpenAPI document except `auth/login` and asserts 401 without a token.
- **F1.9 (Story 6.2, FR39):** replace "logs that it was skipped" with "verifies the newest committed report under `tests/Eval/reports/` meets `thresholds.json` and logs which report it used".

## 2. NFR coverage

| NFR | Story or stories | Gap |
| --- | --- | --- |
| NFR1 | 2.3 AC1 (90 s call), 1.5 AC2 (200 s nginx) | Partial: 180 s run ceiling and the 1 s Fake / 60 s local expectations appear in no AC |
| NFR2 | 4.1 AC3 | Partial: "500 seeded actions" is produced by no story (6.1 seeds roughly fifteen); the 50-proposal Review Screen timing appears in no AC |
| NFR3 | 3.2 AC2 | none |
| NFR4 | 1.2 AC5, 2.3 AC4, 5.1 AC2 | none |
| NFR5 | 1.1 AC2, 1.3, 1.5 AC3 | Partial: secret scanning is a repository setting not named in 1.1; see F1.8 for the global 401 proof |
| NFR6 | 3.3 AC5, 4.2 AC3 | none |
| NFR7 | 1.1 AC1 | none |
| NFR8 | 1.3, 2.2, 3.1, 3.2, 4.1, 6.3 | Partial: no AC requires Angular `*.spec.ts` for container components and data services (AD-18); 1.4 only runs `ng test` |
| NFR9 | 1.1 AC2 | none (checklist only, acceptable) |
| NFR10 | 1.5, 6.1 AC3 | none |
| NFR11 | 1.1, 1.2, 1.4, 1.5, 6.2, 6.4, 6.5 | none |

Findings and fixes:

- **F2.1 (Story 4.1, NFR2):** the 500-action timing has no data source. Either add a `Infrastructure.Tests` or `Api.Tests` case that inserts 500 Tracked Actions through the aggregates into Testcontainers and times the list query, or reword to "against a Testcontainers database seeded with 500 actions by the test".
- **F2.2 (Story 3.3, NFR2):** add "a run with 50 proposals renders within 2 seconds on compose".
- **F2.3 (Story 2.3, NFR1):** add "the handler enforces a 180 s run ceiling (two calls) and the Fake run completes within 1 s in `Application.Tests`".
- **F2.4 (Story 1.1, NFR5):** add "secret scanning and push protection enabled on the repository" to AC2.
- **F2.5 (Stories 1.4, 3.3, 4.2, NFR8):** add "container component and data service have `*.spec.ts` with the generated client mocked" to each UI story.

## 3. UX-DR coverage

| UX-DR | Implementing story ACs | Gap |
| --- | --- | --- |
| UX-DR1 | 1.4 AC1 | none |
| UX-DR2 | 1.4 AC5 | none |
| UX-DR3 | 3.3 AC1, AC4 | none |
| UX-DR4 | 3.3 AC1 | none |
| UX-DR5 | 4.2 AC1 | none |
| UX-DR6 | 4.2 AC1 (StatusChip), 2.4 AC3 and 3.3 AC4 (ReviewStateChip) | none |
| UX-DR7 | 3.3 AC4 | none |
| UX-DR8 | 3.3 AC1, AC3, AC4 | Partial: EXPERIENCE.md says Reject opens a small dialog with the optional reason; 3.3 never mentions the dialog. The "Zero Proposed Actions" state is also absent |
| UX-DR9 | 3.3 AC2 | none |
| UX-DR10 | 3.3 AC1 | none |
| UX-DR11 | 1.4 AC3 | none (1280px max width and 24px gutters unstated, minor) |
| UX-DR12 | 2.1 AC4 | Partial: the Meeting List table itself (columns Title, Date, Runs, Tracked Actions; row click; paginator at 50) is never described; only the dialog is |
| UX-DR13 | 2.1 AC4, 2.4 AC1, AC2 | none |
| UX-DR14 | 2.4 AC3 | none |
| UX-DR15 | 4.2 AC1, AC2 | see F1.6 |
| UX-DR16 | 4.3 AC2 to AC4 | none |
| UX-DR17 | 4.3 AC1 | none |
| UX-DR18 | 1.4 AC3, AC4 | none |
| UX-DR19 | 3.3 AC4 (409 snackbar, in-flight disable), 4.3 AC4 (409) | **Not implemented:** cold-load progress bar, load failure with Retry, Not found, 403 snackbar "Your role does not allow this.", write-failure snackbar with Retry and retained values. Tagged on 3.3 and 4.3 only |
| UX-DR20 | none | **Not implemented:** no AC fixes date format `YYYY-MM-DD`, timestamp `YYYY-MM-DD HH:mm UTC`, or "no relative time"; only tagged on 1.4 |
| UX-DR21 | 3.3 AC5, 4.2 AC3 | none |
| UX-DR22 | 1.4 AC2 | none |
| UX-DR23 | 6.5 AC1 | none |

Findings and fixes:

- **F3.1 (Story 1.4, UX-DR19):** add an AC: "a global HTTP interceptor and shell implement the state patterns: `mat-progress-bar` under the toolbar on any load, 'Couldn't load. {title}' with Retry, a Not found page with a link to the parent list on any unmatched or 404 detail route, 403 snackbar 'Your role does not allow this.', and write-failure snackbar with the problem title and Retry keeping form values". This also produces the Not found state that 3.3 AC4 relies on.
- **F3.2 (Story 1.4, UX-DR20):** add an AC: "shared date and instant pipes render `YYYY-MM-DD` and `YYYY-MM-DD HH:mm UTC`; no relative time anywhere; Voice and Tone strings are constants used verbatim".
- **F3.3 (Story 2.1, UX-DR12):** add the Meeting List table columns, row click to Meeting Detail, and paginator at 50 to AC4.
- **F3.4 (Story 3.3, UX-DR8):** add "Reject opens a dialog with an optional reason and a Reject confirm" and the zero-proposals state "The AI found no actions in these notes." with Run again and Back to meeting.

## 4. Forward dependencies

Unacknowledged cases only (acknowledged ones in 3.3 for the 4.2 and 4.3 routes are fine):

- **F4.1 (Story 2.4 → Story 3.3):** AC2 "on success the app navigates to the Review Screen route for the new run". The Review Screen lands in 3.3, next epic. Fix: navigate to Run Detail until 3.3, and state that 3.3 switches the target.
- **F4.2 (Story 2.4 → Story 3.1):** AC3 needs `GET /api/v1/runs/<id>` with proposal rows carrying Review State chip, decider display name, timestamp, rejection reason, and 2.4's Run Detail needs `isLowConfidence`. `ProposedActionReadModel` and `ProposedActionDto` are created in 3.1 AC4. Fix: move `ProposedActionReadModel` (with `isLowConfidence`, `suggestedOwnerUserId`) and the `GET runs/<id>` endpoint into 2.2 or 2.4; 3.1 then only adds the decision fields.
- **F4.3 (Story 3.3 → nothing):** AC4 says the "View action" link shows "its Not found state" until 4.3, but no story implements a Not found state. Fixed by F3.1 in 1.4.
- **F4.4 (Story 4.1 → nothing):** "with 500 seeded actions" is produced by no story. Fixed by F2.1.
- **F4.5 (Story 4.2 → Story 4.3):** AC3 "Action Detail opens" on row click; 4.3 is the next story. Fix: acknowledge, or say "navigates to `/actions/:id` (route lands in 4.3)".
- **F4.6 (Story 6.1 → Story 6.4):** AC1 "`cd.yml` runs with `Seed:Enabled=false`"; `cd.yml` is written in 6.4. Fix: move the clause to 6.4 AC1.
- **F4.7 (Story 1.2 → Story 1.3):** `/health/ready` "returns 200 only when the database is reachable" but the `AppDbContext`, connection, and first migration land in 1.3. Fix: move `/health/ready` to 1.3, or have 1.2 create the empty `AppDbContext` and `Database` options and say so.
- **F4.8 (Story 5.1 → Story 6.1), minor:** the raw-SQL allowlist names `SeedRepository.AcquireLockAsync`, which lands in 6.1. Acceptable as an allowlist string; note it.
- **F4.9 (Story 2.2 wording):** 2.2 runs the Fake through `IActionExtractor` but `ChatClientActionExtractor` is introduced in 2.3's Given. Since `FakeChatClient` is an `IChatClient`, 2.2 must build `ChatClientActionExtractor` (without timeouts and retry). State that explicitly in 2.2 so 2.3 is an extension, not a rewrite.

Epic independence: Epic 2 works without Epic 3 once F4.1 and F4.2 land. Epic 5 correctly depends on 3.2 (earlier). File churn: the seeder is touched in 1.3, 5.2, and 6.1, and `AppDbContext` plus migrations in 1.3, 2.1, 2.2, 3.1, 3.2, 6.1. Both are tables-when-needed churn and justified; no consolidation recommended.

## 5. Architecture compliance

No AC contradicts an AD. Alignments to make:

- **F5.1 (Story 3.1, AD-3):** AC1 says "the handler ... creates a Tracked Action". Per AD-3 `ProposedAction.Decide` creates it and returns it in `DecisionResult`; the handler only adds it. Reword to "Decide returns the Tracked Action and the handler adds it through `IActionRepository.Add`".
- **F5.2 (Story 5.1, spine ports):** name the Application port `IWebhookSigner` that `HmacWebhookSigner` implements; the AC lists only `IWebhookDispatcher`.
- **F5.3 (Story 5.2, AD-21):** "a `WebhookSubscription` from the fixture catalog" is not something AD-21 puts in the catalog (cases and `roster.json` only). Seed the subscription from the seeder's own code or a `Webhooks:Receiver*` config pair, and say where the receiver's secret comes from (it must match on both sides).
- **F5.4 (Story 2.1 and 2.2, AD-20 and Indexes convention):** `meeting(title, meeting_date)` unique index is deferred to 6.1 and `action_revision(target_type, target_id, sequence)` is never named. Add the first to 2.1 (it drives the 409 on duplicate create) and the second to 2.2.
- **F5.5 (Story 1.3, UX):** `GET /users` returns "every User", which includes the system User `Seed`, so 3.3's owner select would offer "Seed". Exclude system users from `UserSummaryDto` or add `isSystem` and filter in the users store.
- **F5.6 (Story 1.2, wording):** four exception causes map "respectively" onto five ProblemDetails types. State the mapping explicitly: `DomainRuleException` and `ConcurrencyConflictException` to 409, `NotFoundException` to 404, validation to 400, auth to 401 and 403.
- **F5.7 (Story 7.2, AD-13):** `GET /tracked-actions/summary` is a new route not in AD-13's list. Not a contradiction (spine says FR41 fits without a new AD) but it must be added to the committed `openapi.json` and snapshot.

## 6. Sizing

Estimates assume one developer with an AI coding agent, one focused session of two to four hours.

| Story | Estimate | Verdict | Split |
| --- | --- | --- | --- |
| 1.1 | 3 to 4 h | ok | Script the 22 board issues with `gh` |
| 1.2 | 3 to 4 h | ok | |
| 1.3 | 4 to 5 h | over | 1.3a `AppDbContext`, `User`, first migration, bundle, user seeding, Testcontainers test; 1.3b login, JWT, `ICurrentUser`, users endpoint, 403 test |
| 1.4 | 5 to 6 h | over | 1.4a `ng new`, Material, tokens, generated client, ESLint rule, CI steps; 1.4b login, session store, interceptor and state patterns (F3.1, F3.2), toolbar, users store |
| 1.5 | 3 to 4 h | ok | |
| 2.1 | 5 to 6 h | over | 2.1a Meeting aggregate, notes, migration, three endpoints, tests; 2.1b Meeting List, New meeting dialog, notes paste UI |
| 2.2 | 8 h plus | over | 2.2a fixture catalog (12 to 15 cases, expected JSON, roster) and prompt v1; 2.2b Application/Ai (output, schema, validator, normalizer, verifier, parity test) plus `ChatClientActionExtractor` with `FakeChatClient`; 2.2c `ExtractionRun`, `ProposedAction`, `ActionRevision`, migration, `RunExtractionHandler`, endpoint, read model (F4.2) |
| 2.3 | 3 to 4 h | ok | Depends on F4.9 |
| 2.4 | 4 to 5 h | borderline | ok if `GET runs/<id>` moves into 2.2c |
| 3.1 | 5 to 6 h | over | 3.1a Domain `Decide`, `DecisionResult`, `TrackedAction`, revisions, `Domain.Tests`, migration; 3.1b handler, `OwnerResolver`, endpoint, concurrency and copy-agreement tests |
| 3.2 | 3 to 4 h | ok | |
| 3.3 | 8 h plus | over | 3.3a shared components, `ProposalCard` Pending and Decided rendering, two-pane layout, responsive; 3.3b edit mode, reject dialog, decision commands, counter, highlight, 409, a11y pass |
| 4.1 | 4 to 5 h | borderline | 4.1a transitions, edit, status and PATCH endpoints; 4.1b list query, filters, indexes, perf test |
| 4.2 | 3 to 4 h | ok | |
| 4.3 | 5 to 6 h | over | 4.3a revisions endpoint, header, `AuditEntry` timeline; 4.3b status control with undo and confirm, inline edit |
| 5.1 | 4 to 5 h | borderline | move the two read endpoints and `docs/webhooks.md` to 5.2 if needed |
| 5.2 | 2 to 3 h | ok | |
| 6.1 | 4 to 5 h | borderline | move DEMO.md and the macOS and Windows timing to 6.5 |
| 6.2 | 5 to 6 h | over | 6.2a scorer, thresholds, reports, Fake pass; 6.2b `eval.yml`, LM Studio baseline, degraded-prompt run, addendum paste |
| 6.3 | 3 to 4 h | ok | compose-in-CI orchestration is the risk |
| 6.4 | 3 to 4 h | ok | Azure provisioning is outside the estimate |
| 6.5 | 2 h | ok | |
| 7.1 to 7.3 | 3 to 5 h each | P1, conditional | |

Eight stories over: 1.3, 1.4, 2.1, 2.2, 3.1, 3.3, 4.3, 6.2. Splitting them yields roughly 31 P0 stories.

## 7. Day fit

| Day | Stories | Estimated hours | Verdict |
| --- | --- | --- | --- |
| Saturday | 1.1 to 1.5 | 18 to 23 | Infeasible at face value; feasible only if 1.3 and 1.4 are kept to the minimum and Windows timing defers to 6.1 |
| Sunday | 2.1 to 2.4, 3.1 to 3.3 | 36 to 42 | Infeasible |
| Monday | 4.1 to 4.3, 5.1, 5.2, 6.1 to 6.5 | 35 to 42 | Infeasible |

Recommendations, consistent with "cut features, never quality gates" and FR20 as the first P0 cut:

- **Saturday:** cut nothing; pipeline first. Push the macOS and Windows five-minute timing to 6.1 (already there) and treat 1.5 as compose-runs-on-the-dev-Mac.
- **Sunday, move first:** Story 2.3 (real providers) to Monday morning. The Fake provider keeps 2.4, 3.1 to 3.3, and 6.3 fully testable, and 2.3 is needed only before the 6.2 LM Studio baseline. Second: write the fixture catalog (2.2a) on Saturday evening; it is content, not code, and unblocks everything Sunday.
- **Monday, cut first:** FR20 (4.1 AC2 `PATCH` edit and 4.3 AC4 inline edit), as the PRD directs. Second: let 6.4 take its documented no-op path (build, push, bundle, "deploy skipped") rather than provisioning Azure. Third: Epic 7 is already conditional; do not start it. Do not cut 6.2, 6.3, or 5.2 (gates and the demo's integration proof).
- **Reorder Monday** so quality gates land before features that can slip: 4.1a, 4.2, 4.3a, 6.3 (Playwright, needs only Epics 1 to 4), 5.1, 5.2, 6.2a, 6.1, 6.2b, 6.4, 6.5, then 4.3b and FR20 if time remains.
