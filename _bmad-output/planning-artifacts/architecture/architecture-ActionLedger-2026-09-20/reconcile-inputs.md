---
title: Input reconciliation — architecture vs seed, PRD, addendum, UX
created: 2026-09-20
inputs:
  - docs/bmad-seed-prompt.md (§5–§11)
  - _bmad-output/planning-artifacts/prds/prd-ActionLedger-2026-09-19/prd.md
  - _bmad-output/planning-artifacts/prds/prd-ActionLedger-2026-09-19/addendum.md
  - _bmad-output/planning-artifacts/ux-designs/ux-ActionLedger-2026-09-20/EXPERIENCE.md
targets:
  - ARCHITECTURE-SPINE.md
  - ARCHITECTURE.md
  - adrs/ADR-001..007
---

# Reconciliation findings

Severity key: **gap** = input requires it, architecture is silent; **contradiction** = architecture says something incompatible; **weakened** = present but looser than the input; **drift** = naming or placement differs from the input without a recorded reason.

Format per item: severity · input location · spine/arch location · fix.

## Checks that pass (no finding)

- Seven seed §8 ADR topics: ADR-001..007 each exist with "Alternatives considered". ADR-005 records the separate-worker scale path the addendum §4 asks for. ADR-007 lists what changes for SQL Server.
- Retry schedule and states (FR-32, addendum): ARCHITECTURE.md §5, ERD `OUTBOX_MESSAGE`, `Webhooks:RetryDelaysSeconds`, `OutboxState` all match 10s/30s/2m/10m/30m, Dead after sixth failure, Pending/Delivered/Dead only, 10 s timeout, best-effort ordering.
- Compose services (FR-36): AD-17 has db, migrate, api, web, receiver; migrations via the EF bundle, never in API startup.
- Seed §13 diagrams: layer, ERD, extraction+approval sequence, outbox sequence are all present in ARCHITECTURE.md.
- Provider-swap proof point (seed §6, FR-7): explicit in ARCHITECTURE.md §2.
- Transitions on entities (seed §6, FR-11..17): AD-3.
- Eval report path (FR-39 `/tests/Eval/reports/`): AD-19 `tests/Eval/reports/<provider>-<model>-<promptVersion>-<date>.json`.
- LocalOpenAI replacing Ollama: accepted PRD decision, not a finding.

## Findings

### A. Seed layout and port names (seed §6)

- **drift** · seed §6 names `IWebhookDispatcher` as an Application interface · spine AD-8 and Structural Seed have no dispatcher port; `OutboxDispatcher` is a concrete Infrastructure `BackgroundService` and Application only has `IWebhookSigner` · Add `IWebhookDispatcher` (e.g. `DispatchPendingAsync(CancellationToken)`) to `Application/Abstractions`, implemented by `Infrastructure/Webhooks/OutboxDispatcher`, with the hosted service calling it; or record the rename and why in the Naming convention row.
- **drift** · seed §6 names `IActionRepository` · spine only says "ports `I<Noun>Repository`" and never lists the repository ports · Enumerate the ports in AD-3/Structural Seed (`IMeetingRepository`, `IExtractionRunRepository`, `IActionRepository` or `ITrackedActionRepository`, `IUserRepository`, `IWebhookSubscriptionRepository`, `IOutboxRepository`) and say `IActionRepository` is the seed's name for the tracked-action port.
- **contradiction (internal)** · seed §6 / FR-21 revisions target both Proposed and Tracked Actions · AD-3 says `TrackedAction` owns the `ActionRevision` list, but AD-7 says revisions are also created in `ExtractionRun.AddProposals` and `ProposedAction.Decide` · State that `ActionRevision` is a standalone append-only entity written by three aggregates (or that both `ExtractionRun` and `TrackedAction` hold revision lists); pick one and make AD-3 and AD-7 agree.
- **weakened** · NFR-7 bans EF Core, ASP.NET Core, and AI SDKs from Domain/Application · AD-1 bans Application from referencing "anything but Domain", which also bans harmless abstractions (e.g. `Microsoft.Extensions.Logging.Abstractions`) the handlers will want · Either keep the stricter rule and say so deliberately, or phrase AD-1 as the NFR-7 denylist plus "no outward reference".

### B. NFR homes (PRD §4A)

- **gap** · NFR-1 (90 s per-call timeout, 180 s max run) and FR-4 synchronous extraction · AD-11 has no timeout; `Ai:` config keys have no timeout key; only the Deferred section mentions 90 s · Add `Ai:CallTimeoutSeconds=90` to AD-11 and the Config keys row.
- **gap** · NFR-1 + AD-17 (nginx proxies `/api`) · nginx default `proxy_read_timeout` is 60 s, so a 180 s synchronous run would be cut off by the web container · Add to AD-17: `proxy_read_timeout`/`proxy_send_timeout` ≥ 200 s on the `/api` location (or on the runs route), and the Angular HttpClient must not impose a shorter timeout.
- **gap** · NFR-2 (500 Tracked Actions, list and Review Screen under 2 s) · not in the Capability map; no index guidance · Add NFR-2 to the Capability map under AD-10 and name the indexes (`tracked_action(due_date, status)`, `tracked_action(owner_user_id)`, `proposed_action(extraction_run_id, ordinal)`, `outbox_message(state, next_attempt_at)`).
- **weakened** · NFR-4 requires a correlation id on both log events · Logging convention row names the two events but not the correlation id · Add "correlation id on every request and worker iteration (`TraceIdentifier` / activity id)" to the Logging row.
- **gap** · NFR-5 and seed §10: CodeQL, Dependabot, secret scanning enabled · nowhere in spine or ARCHITECTURE.md · Add `.github/dependabot.yml` and `.github/workflows/codeql.yml` to the Structural Seed and a bullet in AD-17 or a new "Repository security" convention row.
- **gap** · NFR-6, NFR-9, NFR-11: PR template with checklist (issue link, tests, license review, axe scan) · no PR template in the Structural Seed; no licensing convention row · Add `.github/pull_request_template.md` to the tree and a "Licensing: MIT / Apache-2.0 / BSD only, reviewed in the PR checklist" convention row.
- **gap** · NFR-10 (no host .NET or Node) + FR-27/AD-13 (web client generated from `openapi.json` at build) · the web Docker image build has no way to obtain `openapi.json` from the api build; CI step order is also unstated · Decide and record: commit `openapi.json` (generated by `dotnet build`, with a CI step that regenerates and fails on diff), or build web after api in one multi-stage Dockerfile. Also add to `ci.yml` step order.
- **gap** · NFR-11: no-op deploy path when Azure is unavailable (Open Question 2 resolution) · AD-17 and ARCHITECTURE.md §8 assume ACA exists · Add to AD-17: if `AZURE_*` secrets are absent, `cd.yml` still builds, pushes, runs the bundle against a disposable Postgres service, and logs "deploy skipped".
- **weakened** · NFR-11: branch protection, GitHub Project board, one issue per story · Branching convention row covers branches and PR-links-issue only · Add branch protection on `main` (green `ci.yml`, linked PR) and the Project board to the same row.

### C. UX "Needs from the API" and Component Pattern shapes (EXPERIENCE.md)

- **gap** · EXPERIENCE.md Needs + FR-7: read-only endpoint for active AI Provider and model · no such endpoint in spine or arch · Add `GET /api/v1/ai/provider` → `{ provider, model }` to AD-11 and the Capability map (FR-4..FR-9 row).
- **gap** · EXPERIENCE.md Needs + FR-14: `isLowConfidence` on Proposed Actions, computed server-side · `Ai:LowConfidenceThreshold` exists but no DTO field is named; AD-15 names only `isOverdue` · Add `ProposedActionDto.isLowConfidence` (computed in the read model against `Ai:LowConfidenceThreshold`, default 0.70) to AD-15 or AD-11.
- **gap** · EXPERIENCE.md Needs + FR-18/FR-19/FR-26: Meeting filter, "Unassigned" owner filter, overdue-only filter, due-date range, sort · list query parameters are not specified anywhere · Add a row or AD: `GET /api/v1/tracked-actions?ownerId=<uuid>|unassigned&status=<csv>&dueFrom&dueTo&meetingId&overdueOnly&sort=dueDate|status&page&pageSize`.
- **gap** · EXPERIENCE.md Proposal card (owner `mat-select` of Users) and Filter bar (Owner select of all Users) · no users read endpoint; FR-26's resource list also omits it · Add `GET /api/v1/users` (id, displayName, role; read-only, any authenticated User) to the FR-23..25 row and flag to the PM that FR-26 should list `users`.
- **gap** · EXPERIENCE.md Meeting table (Runs, Tracked Actions counts) and Run list (proposal count, Pending count) · DTO fields unnamed · Name `MeetingSummaryDto.runCount/trackedActionCount` and `RunSummaryDto.proposalCount/pendingCount`.
- **gap** · EXPERIENCE.md decided card ("Decided by {name}", timestamp, "Proposed" column beside decided values, "View action" link) and Run proposal rows · `ProposedActionDto` shape unnamed; ERD has `decided_by_user_id` but no display name join · Name `ProposedActionDto` fields: `suggestedOwnerUserId` (pre-resolved match, AD-9), `decidedByUserId`, `decidedByDisplayName`, `decidedAt`, `rejectionReason`, `trackedActionId`, and for Edited the tracked action's current `description/ownerDisplayName/dueDate`.
- **gap** · EXPERIENCE.md Action Detail header ("Created from AI proposal, run {Prompt Version}" linking to `/meetings/:id/runs/:runId`; "last-changed-by name") · `TrackedActionDto` shape unnamed · Add `proposedActionId`, `extractionRunId`, `meetingId`, `meetingTitle`, `promptVersion`, `ownerDisplayName`, `lastChangedByDisplayName`, `isOverdue`.
- **gap** · EXPERIENCE.md Audit entry (actor display name, Low Confidence badge on the AI entry) and FR-22 · `ActionRevisionDto` shape unnamed · Add `actorDisplayName` (null → "AI") and `isLowConfidence` on the AiProposal entry.
- **gap** · FR-13 optional rejection reason · ARCHITECTURE.md §4 decision body is `{kind, ownerUserId, dueDate, description}` with no `reason` · Add `reason` to the Decide command body.
- **weakened** · FR-12 "an edit that changes nothing is treated as Approve" and "keeping the pre-selected User is not a change" · AD-9 says the client sends `UserId`; nothing says the server classifies Approve vs Edited from the diff · State in AD-3/AD-9 that `DecideProposalHandler` computes the kind from the diff against the proposal and the pre-resolved owner, and does not trust a client-supplied kind.
- **gap** · FR-27 "example payloads for extraction and review" in the OpenAPI document; Swagger UI in Development and compose · AD-13 does not say how examples are attached or in which environments Swagger is served · Add: examples via `OpenApi` operation transformers (or XML docs), Swagger UI on in `Development` and when `ASPNETCORE_ENVIRONMENT=Compose`.
- **gap** · FR-28 "No unversioned routes except health and Swagger"; AD-17 `api` must be healthy before `web` · no `/health` endpoint named · Add `GET /health` (liveness) and use it as the compose healthcheck.

### D. Webhook header and signature spec (FR-31, addendum "Webhook payload and signature")

- **weakened** · addendum lists four headers and that `X-ActionLedger-Delivery` is the event id, stable across retries · spine AD-8 says only "signs, posts"; ARCHITECTURE.md §5 says "X-ActionLedger-* headers" · Put the four header names, the signed string `timestamp + "." + rawBody`, `sha256=<hex>`, and "Delivery = event id, stable across retries" into AD-8 so the binding document carries the contract, not only the narrative.
- **gap** · addendum: "Including the timestamp lets a receiver reject stale deliveries" (implies per-attempt timestamp) · neither document says whether `X-ActionLedger-Timestamp` is `occurredAt` or the send time of that attempt · Decide: timestamp is the send time of each attempt; body is fixed at enqueue; signature recomputed per attempt. Record in AD-8.
- **gap** · FR-31 "signature scheme is documented in the API docs"; addendum demo step 6 shows it in Swagger · spine does not say where · Add: an OpenAPI document description section (or `docs/webhooks.md` linked from Swagger) with the header table and a verification snippet.
- **weakened** · FR-33 receiver "verifies the signature and displays received events"; addendum says compare in constant time · Structural Seed calls `tools/webhook-receiver` a "tiny echo service" and does not mention verification · Say the receiver recomputes the HMAC with the seeded secret, compares in constant time, and shows verified true/false per event.
- **gap** · FR-30 payload needs `ownerName`, `meetingTitle`, `byUserName` · AD-8 builds `OutboxMessage` rows inside `AppDbContext.SaveChangesAsync` from the domain event, which carries only ids · State that the outbox event pipeline loads User and Meeting to build the FR-30 payload (or that `TrackedActionCreated` carries the denormalized fields).
- **gap** · FR-29 secret never returned; reads return a masked value · not in spine · Add to AD-8 or the DTO conventions: `WebhookSubscriptionDto.secret` is masked (`****` + last 4).

### E. JSON schema location and version (FR-5, addendum "Extraction schema")

- **gap** · addendum: "this schema version is recorded on every Extraction Run" · AD-6 requires `SchemaVersion` on the run and AD-11 places the file at `Application/Ai/extract-actions.schema.json`, but the file name carries no version and nothing says where the value comes from · Name the file `extract-actions.schema.v1.json` (mirror the prompt convention) or put `"$id": ".../extract-actions.schema.v1"` in the file and read it; state which in AD-6/AD-11.
- **gap** · Glossary "Fake Provider" returns the fixed expected proposals for Golden Set cases and seeded Meetings, else the modal-verb heuristic; compose default is `Fake` (FR-36) · AD-11 models the Fake as `FakeChatClient` but says nothing about how it recognises Golden Set or seed inputs when `tests/Eval/golden` is not in the api image · Decide and record: Fake keys canned JSON by SHA-256 of the notes text; fixtures for the three seed Meetings are embedded in Infrastructure; the Eval project supplies Golden Set fixtures to the same class; anything else hits the heuristic.

### F. Eval report and gate (FR-38, FR-39, addendum §1)

- **weakened** · FR-38: scorer writes a JSON report and a markdown summary with per-case detail · AD-19 names only the `.json` · Add the sibling `.md` summary to AD-19 and say the markdown is what gets pasted into the PRD addendum baseline.
- **weakened** · addendum §1: committed report records the server (LM Studio) as well as model, Prompt Version, date · report name has provider, not server · Add `server` (and `baseUrl` host) as fields inside the report.
- **weakened** · FR-39: AzureOpenAI scoring is optional and runs only when its secret is present · AD-19 mentions Fake and LocalOpenAI only · Add the optional AzureOpenAI step to AD-19.

### G. Seed data and compose (FR-34..36, seed §11)

- **gap** · FR-34/FR-35 seeding on first start, idempotent; FR-25 seed rows attributed to system User "Seed"; AD-17 forbids migrations in API startup · nothing says which process runs `DemoDataSeeder` (the migration bundle cannot run C#) · State in AD-17: the api runs `DemoDataSeeder` at startup after `migrate` exits, idempotent by natural keys, acting as the "Seed" User.
- **gap** · FR-7: an unreachable LocalOpenAI endpoint fails fast at startup · AD-16 `ValidateOnStart` validates configuration presence only · Add a startup probe (`GET {BaseUrl}/models`) for `LocalOpenAI` and a credential check for `AzureOpenAI` in AD-16.
- **gap** · AD-17 says the same `web` image deploys to ACA · nginx proxies `/api` to the compose service name `api`, which does not exist in ACA · Make the upstream an env var (`API_UPSTREAM`) templated into the nginx config at container start.
- **weakened** · FR-36 "under five minutes with no cached images", NFR-10 no host toolchain · Structural Seed shows no Dockerfiles · Add `src/ActionLedger.Api/Dockerfile`, `web/actionledger-web/Dockerfile`, `tools/webhook-receiver/Dockerfile`, and a `migrate` Dockerfile (or the bundle built in the api multi-stage) to the tree.

### H. Docs deliverables (seed §1, §7, §11; PRD §0, §6.1; addendum "Artifact location")

- **gap** · `docs/` must hold the brief, PRD + addendum, architecture, ADRs, stories, and `docs/frontend-architecture.md`; ARCHITECTURE.md §7 already points readers at `docs/frontend-architecture.md` · `docs/` is absent from the Structural Seed tree; no owner for the tag-time copy · Add `docs/{brief.md, prd.md, prd-addendum.md, architecture.md, adrs/, stories/, frontend-architecture.md}` to the tree and a "Documentation deliverables at tag time" bullet (owner: the `v1.0.0` tag story) in the Capability map.
- **weakened** · seed §10 / PRD §6.1: `v1.0.0` tag with release notes triggers the versioned deploy · ARCHITECTURE.md §8 covers `v*` tags but release notes have no owner · Add release notes to the same tag-time bullet.

### I. Locked stack and non-goals (seed §5, PRD §5)

- **drift** · seed §5 "Angular (current stable release)"; today is 2026-09-20 · Stack table pins Angular 20, Angular Material 20, Node 22 LTS, Microsoft.Extensions.AI 9.x, PostgreSQL 17, Playwright "1.5x", nginx 1.27 — these read as 2025 versions · Have the version-research pass confirm current stable for each and update the table; the seed's rule is "current stable", so a stale pin is a contradiction once verified.
- **weakened** · Conventions Errors row: `ProblemDetails.type` set is `{validation, not-found, conflict, forbidden, extraction-failed}` · `extraction-failed` is never sent (the same row says extraction failure is recorded on the run, not thrown) and 401 has no type · Replace `extraction-failed` with `unauthorized` or add it; keep the set matching what the API can actually return.
- No contradiction found with PRD §5 non-goals or with the locked stack beyond the version drift above. The spine's Deferred list matches the PRD roadmap.
