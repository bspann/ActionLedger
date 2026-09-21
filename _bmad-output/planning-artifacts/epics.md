---
stepsCompleted: [1, 2, 3, 4]
inputDocuments:
  - _bmad-output/planning-artifacts/prds/prd-ActionLedger-2026-09-19/prd.md
  - _bmad-output/planning-artifacts/prds/prd-ActionLedger-2026-09-19/addendum.md
  - _bmad-output/planning-artifacts/ux-designs/ux-ActionLedger-2026-09-20/DESIGN.md
  - _bmad-output/planning-artifacts/ux-designs/ux-ActionLedger-2026-09-20/EXPERIENCE.md
  - _bmad-output/planning-artifacts/architecture/architecture-ActionLedger-2026-09-20/ARCHITECTURE-SPINE.md
  - _bmad-output/planning-artifacts/architecture/architecture-ActionLedger-2026-09-20/ARCHITECTURE.md
  - _bmad-output/planning-artifacts/architecture/architecture-ActionLedger-2026-09-20/adrs/
  - _bmad-output/planning-artifacts/briefs/brief-ActionLedger-2026-09-19/addendum.md
  - docs/bmad-seed-prompt.md
---

# ActionLedger - Epic Breakdown

## Overview

This document provides the complete epic and story breakdown for ActionLedger, decomposing the requirements from the PRD, UX Design if it exists, and Architecture requirements into implementable stories.

Build window: Saturday 2026-09-19 through Monday 2026-09-21, one developer with Claude Code. Code freeze end of day Monday, `v1.0.0` tag, demo 2026-09-23. Every story merges through a pull request with green checks. Pipeline work comes first.

## Requirements Inventory

### Functional Requirements

FR1: An Action Officer can create a Meeting with title (1-200 chars), date, and attendee names (each 1-100 chars); API returns 201 with the creating User id.
FR2: An Action Officer can attach Meeting Notes (1-50,000 chars) to a Meeting exactly once; a second save returns 409; no update or delete path exists; text is stored byte-for-byte.
FR3: Any User can list Meetings (title, date, run count, tracked action count, sorted by Meeting date then creation time descending) and open one to see fields, notes as pasted, and Extraction Runs with outcome and Prompt Version.
FR4: An Action Officer can start an Extraction Run on a Meeting with notes (400 without notes); multiple runs per Meeting are allowed; runs use the configured Prompt Version; the provider receives notes text and Meeting date only; the response includes the run id and outcome.
FR5: AI output is accepted only when it validates against the extraction JSON schema (object with `actions` array; description 1-500, suggestedOwner up to 100, suggestedDueDate ISO date or null, confidence 0-1, sourceExcerpt 1-1,000); invalid output is retried once then the run is Failed with a reason and zero proposals; excerpt verification is a separate post-validation filter using the FR38 normalization that drops individual proposals with a recorded warning.
FR6: Every Extraction Run records provider, model, Prompt Version, start timestamp, duration ms, input and output tokens (zero for Fake, never null), outcome, failure reason, and warnings; prompts live at `/prompts/extract-actions.v<N>.md` and change only through pull requests.
FR7: The active AI Provider is selected by one configuration key among LocalOpenAI, AzureOpenAI, Fake; switching is config plus restart; adding one is one Infrastructure class plus DI registration with zero Domain or Application edits; missing credentials or an unreachable local endpoint fail fast at startup; a read-only endpoint returns the active provider and model.
FR8: The prompt instructs the model to treat notes as data; one fixture case contains an injected instruction and the Evaluation Gate fails if any proposal derives from it.
FR9: Any User can open Run Detail showing every FR6 field including warnings, the Prompt Version, and the proposals with Review States; a Failed run shows the reason and a "Run again" control.
FR10: An Action Officer can open the Review Screen for a run and see all proposals in AI return order with description, suggested owner, suggested due date, Confidence Score, Source Excerpt, Review State, and an API-computed low-confidence flag; Pending proposals show Approve, Edit, Reject; decided ones show decision, decider, timestamp; a Pending count is shown.
FR11: An Action Officer can approve a Pending proposal as-is: Review State Approved, a Tracked Action is created (Open, owner per FR15), a ReviewDecision revision is written; approving a non-Pending proposal returns 409.
FR12: An Action Officer can edit description, owner, or due date and approve: Review State Edited, Tracked Action created with edited values, original proposal values unchanged; revisions written in order ReviewDecision then FieldEdit per changed field with one shared timestamp; an edit that changes nothing (including keeping the pre-selected owner) is treated as Approve.
FR13: An Action Officer can reject a Pending proposal with an optional reason; no Tracked Action; a ReviewDecision revision records rejection, reason, User, timestamp, visible on Review Screen and Run Detail.
FR14: The Review Screen visually flags proposals below the Low Confidence Threshold (configurable, default 0.70) with a distinct treatment plus text label, never color alone; flagged proposals are not blocked.
FR15: On approval the free-text suggested owner is resolved to a User: the API pre-selects a case-insensitive display-name match, approving with no owner yields a null Owner (Unassigned), the proposal keeps the original text, the Tracked Action stores only the User reference.
FR16: A Tracked Action is created only by an Approve or Edit-and-Approve decision; it links to exactly one proposal, run, and Meeting; creation raises the domain event that FR30 consumes; no endpoint creates one directly.
FR17: Action Status transitions are Open to In Progress, Open to Complete, Open to Cancelled, In Progress to Complete, In Progress to Cancelled, In Progress to Open; only a Lead may set Cancelled (403 otherwise); Complete and Cancelled are terminal (409 on exit); rules live in the entity; each transition writes a StatusChange revision.
FR18: Any User can view the Action List filtered by Owner (including Unassigned), Action Status, due date range, and Meeting; filters AND together and mirror the API query parameters; rows show description, owner or Unassigned, due date, status, Meeting title, Overdue indicator; sortable by due date and status, default due date ascending nulls last; paged at 50.
FR19: A Tracked Action is Overdue when due date is before today (UTC, via the clock abstraction) and status is Open or In Progress; never for Complete or Cancelled; the API returns `isOverdue`; an overdue-only filter exists.
FR20: A Lead or Action Officer can edit a Tracked Action's description, Owner, or due date after creation, each change writing a FieldEdit revision; edits on Complete or Cancelled return 409. First P0 item to cut if Monday slips.
FR21: The system records an append-only Action Revision (id, target, kind, field, old value, new value, actor, timestamp) for every Review State change and every Tracked Action field or status change; the AI Proposal is a stored revision row with a JSON new-value and null actor.
FR22: Any User can open the Audit Trail for a Tracked Action: entries oldest first, AI Proposal with Confidence Score and Source Excerpt, then each decision, field edit, and status change with display name and timestamp, old and new values side by side; available via API as an ordered list.
FR23: A User can log in with username and password and receive an 8-hour JWT carrying id, display name, role; passwords are hashed; invalid credentials return 401 without hinting which part failed; the web app holds the token in memory and redirects to login on 401.
FR24: API endpoints enforce Role: writes require ActionOfficer or Lead, the Cancelled transition requires Lead, reads require any authenticated User; 401 unauthenticated, 403 wrong role.
FR25: Every revision and created record carries the id of the User from the JWT (never from request parameters); seed data is attributed to a system User "Seed".
FR26: Every read and write is available through the REST API for resources meetings, notes, runs, proposed actions, tracked actions, revisions, users (read-only), webhook subscriptions (read-only), outbox messages (read-only), ai/provider, auth; errors are RFC 9457 ProblemDetails with stable types; lists page with `page` and `pageSize` (default 50, max 200).
FR27: The API publishes an OpenAPI 3.x document generated from controllers with schemas, security scheme, and example payloads; Swagger UI is served in Development and compose with JWT bearer support; the Blazor typed client is generated from the committed `openapi.json` so a contract change breaks the web build.
FR28: All routes are prefixed `/api/v1`; only health and Swagger are unversioned.
FR29: A Webhook Subscription has URL, secret (never returned, masked on read), active flag, event types; subscriptions are seeded and read-only in v1; event type `action.approved`.
FR30: On Tracked Action creation, one Outbox Message per active matching subscription is written in the same transaction; the payload carries event type, event id, timestamp, the Tracked Action, its proposal, and the Review Decision.
FR31: The dispatcher POSTs each message with headers X-ActionLedger-Event, -Delivery (event id, stable across retries), -Timestamp, -Signature `sha256=<hex>` of HMAC-SHA256 over `timestamp + "." + rawBody`; 2xx marks Delivered; the scheme is documented for integrators.
FR32: Failed deliveries (non-2xx or 10 s timeout) retry at 10 s, 30 s, 2 m, 10 m, 30 m then Dead after the sixth failure; attempt count, last status, last error, next attempt are visible via API; ordering is best-effort per subscription; states are Pending, Delivered, Dead.
FR33: The compose environment includes a webhook receiver that verifies the signature in constant time and displays received events; a seeded subscription points at it.
FR34: On first start the database contains at least one Lead, two Action Officers with documented passwords, and the system User "Seed"; seeding is idempotent.
FR35: On first start three fictional Pinecrest Regional Office Meetings exist: one fully reviewed with mixed statuses including an Overdue edited action, one with a Succeeded run awaiting review including a low-confidence proposal, one audit showcase with an edited low-confidence approval; at least one Delivered and one Dead Outbox Message are seeded; all content fictional and unclassified; seeding is idempotent.
FR36: `docker compose up` from a clean clone starts db, migrate (EF migration bundle), api, web, receiver; usable login page under five minutes on macOS and Windows; Fake provider by default; `.env.example` documents every variable including the LocalOpenAI base URL and model; `DEMO.md` holds credentials, click path, and the Fake fallback.
FR37: The repository holds 12 to 15 fictional notes cases with expected actions (description, owner, due date, expected Source Excerpt) covering plain actions, no owner, no date, relative dates, non-actions, duplicate mentions, and one prompt injection; a roster with aliases is included.
FR38: A scorer runs extraction on each case and computes action precision and recall (pooled), owner accuracy, and due date accuracy using the deterministic two-signal matching rule (50 percent token overlap of excerpts or 0.60 token-set description similarity, greedy one-to-one, normalization lowercase strip punctuation collapse whitespace) and writes a JSON report and markdown summary; the Fake provider scores 1.0.
FR39: The Evaluation Gate fails below recall 0.80, precision 0.75, owner 0.85, due date 0.80, or on the injection hard fail (three-token overlap with the injected span or any unmatched action in that case); thresholds live in one file and print in the job; runs on PRs touching prompts or extractor code and on dispatch; Fake always in CI, LocalOpenAI when `LOCAL_AI_BASE_URL` is set else a committed local report from LM Studio, AzureOpenAI optional.
FR40: (P1) An Action Officer can generate, review, edit, and copy or download a draft follow-up email per Meeting; never sent by the system; generation records run-style metadata.
FR41: (P1) A Lead sees dashboard tiles for open, Overdue, and completed-in-seven-days actions, each linking to a pre-filtered Action List.
FR42: (P1) An Action Officer can run extraction with a different Prompt Version and compare two runs' proposals side by side using the FR38 matching rule.

### NonFunctional Requirements

NFR1: Extraction latency: a run on up to 2,000 words completes within 60 s on LocalOpenAI (demo Mac), 30 s on a cloud provider, 1 s on Fake; per-call timeout 90 s, run ceiling 180 s; nginx proxy timeouts 200 s on `/api`.
NFR2: With 500 Tracked Actions in the database an Action List page and a 50-proposal Review Screen each render within 2 s on compose.
NFR3: No approved Tracked Action ever lacks its Outbox Message for an active subscription; proven by a Testcontainers test that fails after the outbox write and asserts neither row exists.
NFR4: Every Extraction Run and every delivery attempt writes one structured log event with correlation id, provider, model, Prompt Version, duration, tokens, outcome (runs) or subscription, attempt, status, duration (deliveries); no notes text or secrets in logs.
NFR5: All writes require a valid JWT; secrets only from environment or user secrets; CodeQL, Dependabot, secret scanning enabled; `.env.example` only.
NFR6: Review Screen and Action List are keyboard operable with named controls; low-confidence flag not color alone; manual keyboard pass and browser axe scan recorded in the PR checklist for UI stories.
NFR7: NetArchTest (eNhancedEdition) fails the build if Domain or Application reference EF Core, ASP.NET Core, or any AI SDK, or if any project references outward.
NFR8: Backend tests cover Review State and Action Status transitions on entities, use cases with the Fake provider, repositories against Testcontainers PostgreSQL, and API auth; bUnit component tests cover component logic; one Playwright test runs UJ-1 against compose with Fake.
NFR9: Only MIT, Apache-2.0, or BSD dependencies; license review in the PR checklist; excluded by license: FluentAssertions 8+, MediatR 13+, AutoMapper 15+, MassTransit 9, JsonSchema.Net.
NFR10: The compose environment runs on macOS and Windows with Docker Desktop; no host-installed .NET needed to run the demo.
NFR11: Delivery pipeline: `ci.yml` (restore, build, tests incl. Testcontainers, architecture tests, OpenAPI snapshot, bUnit component tests, image build) on PRs and main; `eval.yml` with path filter and dispatch; `cd.yml` on main and `v*` tags (GHCR push, migration bundle, Azure Container Apps deploy or documented no-op when secrets absent); branch protection on main; PR template checklist; conventional commits; Project board with one issue per story; `v1.0.0` tag with release notes.

### Additional Requirements

- Starter: none. Greenfield `dotnet new sln` with four projects plus seven test projects, and `dotnet new blazorwasm` for `src/ActionLedger.Web` (Microsoft.AspNetCore.Components.WebAssembly 10.0.12, MudBlazor 9.10.0); `tests/Web.Tests` on bUnit 2.11.3 joins the solution with the web project, bringing the test projects to eight.
- Clean Architecture rings Domain, Application, Infrastructure, Api; `Architecture.Tests` with NetArchTest.eNhancedEdition 1.4.5 enforcing AD-1 including the Application allowlist and the single-normalizer rule.
- One handler per use case (`<Verb><Noun>Handler`), reads as `<Feature>Queries` over `IReadDb`; controllers map only (AD-2).
- Aggregate roots and repositories per AD-3: `Meeting`, `ExtractionRun` (via `ExtractionRun.Start`), `TrackedAction`, `ActionRevision`, `User`, `WebhookSubscription`, `OutboxMessage`; `ProposedAction.Decide` returns a `DecisionResult` and raises `TrackedActionCreated` on the returned Tracked Action; handlers add roots and revisions explicitly.
- `ActionRevision` is its own root with `Sequence` and one timestamp per aggregate call (AD-7).
- Transactional outbox pipeline in `AppDbContext.SaveChangesAsync` using Application-owned `WebhookEventDto`, `WebhookEventTypes`, `IWebhookPayloadBuilder`; `OutboxDispatcher` behind `IWebhookDispatcher` as a BackgroundService in the api; lease-based claim (`UPDATE ... RETURNING` with `FOR UPDATE SKIP LOCKED`, `Webhooks:LeaseSeconds` 60); `MarkDelivered` and `RecordFailure` (AD-8).
- `OwnerResolver.Match` pure function used only by `ProposedActionReadModel` and the FR12 change test; `IExtractionSettings` port for the threshold (AD-9).
- PostgreSQL 18 via Npgsql EF 10.0.3; UUIDv7 with `ValueGeneratedNever`; Add-only repositories; `text[]` and `jsonb` behind value converters; raw SQL only in `ClaimBatchAsync` and `SeedRepository.AcquireLockAsync` (AD-10).
- AI seam: `IActionExtractor` never throws; `ChatClientActionExtractor` on `Microsoft.Extensions.AI` 10.10.0 with the OpenAI SDK 2.14.0 `Endpoint` for LocalOpenAI (LM Studio at `host.docker.internal:1234/v1`, Ollama) and Azure `/openai/v1/`; `strict = true` structured output; strict System.Text.Json deserialization into `ExtractionOutput` plus `ExtractionOutputValidator`; `TextNormalization` and `ExcerptVerifier` in Application/Ai; `JsonSchemaExporter` parity test; prompts embedded as resources with `IPromptCatalog` and `Ai:PromptVersion`; `SchemaVersion` from the schema file's `version` property (AD-6, AD-11).
- Identity: HS256 JWT, `PasswordHasher<User>`, `ICurrentUser` from `sub`, `SeedCurrentUser` for the seeder only (AD-12).
- API contract: `openapi.json` committed at `src/ActionLedger.Web/openapi.json` (moved there by Story 1.5), exported by `--export-openapi`, guarded by `OpenApiSnapshotTest`; `NSwag.MSBuild` 14.7.1 in a pre-build target into git-ignored `Core/Api/`; resources and query parameters per AD-13; DTO fields `isLowConfidence`, `suggestedOwnerUserId`, `decidedByDisplayName`, `rejectionReason`, `trackedActionId`, `isOverdue`, `ownerDisplayName`, `lastChangedByDisplayName`, `promptVersion`, `runCount`, `trackedActionCount`, `proposalCount`, `pendingCount`; ProblemDetails types validation, unauthorized, forbidden, not-found, conflict; `/health` and `/health/ready`.
- Overdue as a Domain expression `TrackedActionRules.IsOverdueOn(today)` used by both entity and queries (AD-15).
- Options with `ValidateOnStart`; startup probe (`GET {BaseUrl}/models` for LocalOpenAI); `API_UPSTREAM` templated into nginx (AD-16).
- Deployment: `migrate` one-shot bundle with `depends_on: service_completed_successfully`; `extra_hosts host-gateway`; Dockerfiles for api (multi-stage incl. bundle), web, receiver, migrate; GHCR images tagged by SHA and version; Container Apps liveness `/health` and readiness `/health/ready`; Azure Database for PostgreSQL Flexible Server; no-op deploy path (AD-17).
- Tests per ring (AD-18) including the NFR3 outbox test, inserts-only test, revision-copy agreement test, `OpenApiSnapshotTest`, concurrent-decision test, bUnit component tests in `tests/Web.Tests`.
- Eval project reads the fixture catalog, applies no re-filter, writes JSON and markdown reports with server and model fields (AD-19).
- AD-20: `IUnitOfWork.CommitAsync` once per handler; `xmin` concurrency tokens on ProposedAction, TrackedAction, Meeting, OutboxMessage; unique indexes `tracked_action(proposed_action_id)`, `meeting_notes(meeting_id)`, `proposed_action(extraction_run_id, ordinal)`, `user(username)`, `meeting(title, meeting_date)`; `ConcurrencyConflictException` to 409.
- AD-21: `fixtures/extraction/` (`<case>.md` with front matter, `<case>.expected.json`, `roster.json`) embedded in Infrastructure; single source for Golden Set, Fake answers keyed by SHA-256 of normalized notes, and seed data (`seed: true` cases); seeder as a hosted service in api before the dispatcher, `Seed:Enabled`, advisory lock, aggregates with `SeedCurrentUser` and `FixedClock`, direct writes only for Delivered and Dead outbox rows, `SaveChangesAsync(suppressOutbox: true)` restricted to Infrastructure/Seed.
- Conventions: snake_case tables, string enums, `DateOnly` and UTC `DateTimeOffset`, paging envelope, Serilog with correlation id, config keys list, indexes list, conventional commits, PR template.
- Documentation deliverables at tag time: `DEMO.md`, `docs/webhooks.md`, `docs/frontend-architecture.md`, copies of brief, PRD and addendum, architecture, ADRs, stories under `docs/`, release notes.

### UX Design Requirements

UX-DR1: MudBlazor 9.10.0 with a `MudTheme` palette as the base; no MudBlazor component restyled; Roboto Mono loaded for the `confidence-score` role.
UX-DR2: Semantic color tokens implemented as CSS custom properties with light and dark pairs: ai-provenance, human-provenance, low-confidence, success, neutral-container families with their on-colors, matching DESIGN.md hex values.
UX-DR3: Shared `ProvenanceChip` component (AI variant "Proposed by AI" with `auto_awesome`; human variant "Decided by {display name}" with `person`), icon plus text, static, timestamp rendered beside not inside.
UX-DR4: Shared `LowConfidenceBadge` component (icon `warning`, text "Low confidence") driven by the API `isLowConfidence` flag; the proposal card adds a 4px left border in the low-confidence color.
UX-DR5: Shared `OverdueIndicator` component rendered as a badge (icon `schedule`, text "Overdue", the MudBlazor error container) driven by the API `isOverdue` flag.
UX-DR6: Shared `StatusChip` (Open, In Progress, Complete with line-through Cancelled) and `ReviewStateChip` (Pending, Approved, Edited with `edit` icon, Rejected) components using the DESIGN.md token mapping.
UX-DR7: `PendingCounter` component on the Review Screen header ("{n} proposals pending" / "All proposals decided" with a "View actions" link), inside an `aria-live="polite"` region.
UX-DR8: `ProposalCard` component with three states: Pending (read-only values, "AI suggested: {text}" hint always visible, "No due date proposed" and "Unassigned"/"AI suggested: none" for nulls, Source Excerpt blockquote on the AI container, Reject text button, Edit outlined, Approve filled), Edit mode (text field, owner select with Unassigned, date picker with clear, Cancel text button, "Approve" or "Approve with edits" label driven by a diff against proposed values), Decided (kept in place, keeps Source Excerpt, human provenance chip plus timestamp plus Review State chip, "Reason: {text}" or "No reason given" when Rejected, "Proposed" column beside decided values when Edited, "View action" link when Approved or Edited).
UX-DR9: Source Excerpt highlight: focusing or hovering a card, or clicking its blockquote, highlights the matching sentence in the notes pane, scrolls to it, and sets `aria-describedby`; one highlight at a time; notes rendered with `white-space: pre-wrap`.
UX-DR10: Review Screen two-pane layout at 1200px and wider (notes pane min 360px left, cards right, independent scroll); at 1024 to 1199px the notes pane becomes a collapsed `MudExpansionPanels` above the cards; below 1024px unsupported.
UX-DR11: Global `MudAppBar` with product name, Meetings and Actions links with active state, a `MudMenu` user menu showing display name and role with "Sign out"; no sidenav; content max width 1280px with 24px gutters.
UX-DR12: Meeting List table (Title, Date, Runs, Tracked Actions; row click; paginator at 50) and New meeting dialog (Title required max 200, Date required, Attendees chip input each max 100, validation messages).
UX-DR13: Meeting Detail notes paste area (50,000 char limit with live count, caption "Notes cannot be changed after saving", Save notes confirm dialog, read-only `pre-wrap` after save with "Saved {timestamp}, immutable") and Run extraction button (disabled with visible caption "Add notes first" when no notes; in-flight progress bar and caption "Extracting with {provider} · {model}. This can take up to a minute with a local model."; navigates to Review Screen on success; failed run shown in the run list with reason).
UX-DR14: Run list (`MudTable`: started, Prompt Version, provider and model, outcome, proposal count, Pending count, Review or Details) and Run Detail metadata definition list (all FR6 fields, tokens rendered "0" never blank, warnings expandable to dropped excerpts) with proposal rows in AI order showing Review State, decider, timestamp, rejection reason.
UX-DR15: Action List `MudTable` with `Dense` (Description, Owner or Unassigned, Due date, Status, Meeting, Overdue indicator; sort by due date asc nulls last; paged at 50) with Filter bar (`MudSelect` for Owner incl. Unassigned, multi-select `MudSelect` for Status, `MudDateRangePicker` for Due date range, `MudSelect` for Meeting, Overdue only toggle; filters in URL query string; Clear filters).
UX-DR16: Action Detail with header "Created from AI proposal, run {Prompt Version}" linking to Run Detail, Status control (allowed transitions only; Cancelled disabled with caption "Lead only" for Action Officers; Open and In Progress transitions apply on select with an Undo `ISnackbar` message; Complete and Cancelled confirm "This cannot be reopened" in an `IDialogService` dialog with no Undo; disabled when terminal), inline Edit fields with Save and Cancel disabled on terminal with caption "{Status} actions cannot be edited".
UX-DR17: `AuditEntry` timeline component, oldest first, AI entry on the purple container with all five proposed fields and Low Confidence badge when applicable, human entries with display name, "Was / Now" two-column field changes, never collapsed.
UX-DR18: Login form (username, password, "Sign in"; on 401 "Sign-in failed. Check your username and password."); `SessionState` holds the token in memory; 401 redirects to login with an `ISnackbar` message and restores the attempted route.
UX-DR19: State patterns implemented globally with `MudProgressLinear` and `ISnackbar`: cold load progress bar, load failure with Retry, not found, 403 snackbar "Your role does not allow this.", 409 snackbar "Already changed. Reloading." with refresh and no Retry, write failure snackbar with Retry and retained form values, write buttons disabled while in flight.
UX-DR20: Voice and Tone strings used verbatim (Do column of the table), dates as `YYYY-MM-DD`, timestamps as `YYYY-MM-DD HH:mm UTC`, no relative time.
UX-DR21: Accessibility floor: every indicator icon plus text, every icon button `aria-label`, visible labels on fields, `MudTable` sortable headers announced, focus trap and return on `IDialogService` dialogs, MudBlazor default focus rings; manual keyboard pass and axe scan recorded in the PR checklist for Review Screen and Action List stories.
UX-DR22: An `Architecture.Tests` rule failing the build when `System.Net.Http.HttpClient` or the generated client is referenced from any type outside `Core/` and `Features/*/Data/`; feature folders `Auth`, `Meetings`, `Review`, `Actions`, `Audit`; one routable container component per route; `Core/Auth/SessionState.cs` and `Core/Users/UserDirectory.cs`.
UX-DR23: `docs/frontend-architecture.md` with the Blazor mapping diagram (View = `.razor` markup, component class = state and command methods in code-behind, Model = generated client and DTOs).

### FR Coverage Map

FR1: Epic 2 - Create Meeting
FR2: Epic 2 - Immutable notes
FR3: Epic 2 - Meeting List and Detail
FR4: Epic 2 - Run extraction
FR5: Epic 2 - Schema validation, retry, excerpt filter
FR6: Epic 2 - Run metadata and prompt files
FR7: Epic 2 - Provider selection and provider info endpoint
FR8: Epic 2 - Untrusted notes prompt and injection fixture
FR9: Epic 2 - Run Detail
FR10: Epic 3 - Review Screen
FR11: Epic 3 - Approve
FR12: Epic 3 - Edit then approve
FR13: Epic 3 - Reject with reason
FR14: Epic 3 - Low confidence flag
FR15: Epic 3 - Owner resolution
FR16: Epic 3 - Tracked Action creation
FR17: Epic 4 - Status lifecycle, Lead-only Cancelled
FR18: Epic 4 - Action List filters
FR19: Epic 4 - Overdue indicator
FR20: Epic 4 - Edit Tracked Action fields
FR21: Epic 3 - Action Revisions (AI Proposal row in Epic 2, status and field edits extended in Epic 4)
FR22: Epic 4 - Audit Trail
FR23: Epic 1 - Login and JWT
FR24: Epic 1 - Role authorization (Lead-only Cancelled enforced in Epic 4)
FR25: Epic 1 - Current User on every write
FR26: Epic 1 - API foundation; each epic adds its resources
FR27: Epic 1 - OpenAPI document, Swagger UI, generated client
FR28: Epic 1 - /api/v1 prefix and health routes
FR29: Epic 5 - Webhook Subscriptions
FR30: Epic 3 - Enqueue on approval (transactional outbox)
FR31: Epic 5 - Signed delivery
FR32: Epic 5 - Retry with backoff and states
FR33: Epic 5 - Demo receiver
FR34: Epic 6 - Seeded users (minimal seed users land in Epic 1 for login; full seeder here)
FR35: Epic 6 - Seeded Meetings and outbox rows
FR36: Epic 1 - Compose skeleton; Epic 6 - DEMO.md and five-minute verification
FR37: Epic 2 - Fixture catalog
FR38: Epic 6 - Scorer
FR39: Epic 6 - Evaluation Gate thresholds and eval.yml
FR40: Epic 7 - Follow-up email
FR41: Epic 7 - Dashboard tiles
FR42: Epic 7 - Re-run and compare

## Epic List

### Build Sequence

Thirty-two P0 stories, each sized for one focused session with an AI coding agent. Quality gates land before features that can slip. Every story merges through a pull request with green checks and links its issue on the Project board.

| Day | Order | Notes |
| --- | --- | --- |
| Saturday 2026-09-19 | 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, then 2.3 in the evening | Pipeline first. 2.3 is fixture content, not code, and unblocks Sunday. |
| Sunday 2026-09-20 | 2.1, 2.2, 2.4, 2.5, 2.6, 3.1, 3.2, 3.3, 3.4, 3.5 | Entire day runs on the Fake provider. |
| Monday 2026-09-21 | 2.7, 4.1, 4.2, 4.3, 4.4, 6.4, 5.1, 5.2, 6.2, 6.1, 6.3, 6.5, 6.6, then 4.5 if time remains | Playwright (6.4) needs only Epics 1 to 4 and runs before webhooks. Cut order: 4.5 (FR20) first, then let 6.5 take its no-op deploy path. Never cut 6.2, 6.3, 6.4, or 5.2. Epic 7 does not start unless everything above is green. |

## Epic 1: Sign in to a running, deployable ActionLedger (Saturday)

An Action Officer or Lead can clone the repository, run `docker compose up`, sign in with seeded credentials, and see the application shell and Swagger UI. Every later story lands on a green pipeline with architecture tests, the migration bundle, the committed OpenAPI contract, and branch protection already in place.

### Story 1.1: Green pipeline on an empty Clean Architecture solution

As a developer,
I want a solution skeleton with the four rings, the seven test projects, architecture tests, and a CI workflow that is green on the first pull request,
So that every feature story merges through a pull request with green checks and the dependency rule is enforced from day one.

**Requirements:** NFR5, NFR7, NFR9, NFR11. **UX:** none.

**Acceptance Criteria:**

**Given** a clean clone on .NET SDK 10.0.401
**When** I run `dotnet build` and `dotnet test`
**Then** the solution contains `ActionLedger.Domain`, `ActionLedger.Application`, `ActionLedger.Infrastructure`, `ActionLedger.Api` and tests `Domain.Tests`, `Application.Tests`, `Infrastructure.Tests`, `Api.Tests`, `Architecture.Tests`, `Eval`, `Web.E2E` on xunit.v3 4.0.1, and all tests pass
**And** `Architecture.Tests` uses NetArchTest.eNhancedEdition 1.4.5 and fails when Domain references any package, when Application references anything beyond Domain plus the AD-1 allowlist, when Domain or Application reference EF Core, ASP.NET Core, Npgsql, OpenAI, or Microsoft.Extensions.AI, or when any project references Api

**Given** the repository on GitHub
**When** a pull request is opened
**Then** `ci.yml` runs restore, build, and all test projects and reports green
**And** branch protection on `main` requires `ci.yml` and a linked issue; `.github/pull_request_template.md` carries the checklist (issue link, tests added, license review, axe pass for UI stories); `codeql.yml` and `dependabot.yml` exist; secret scanning and push protection are enabled on the repository; commits follow conventional commit format

**Given** the GitHub Project board
**When** this story is merged
**Then** every story in this document exists as an issue on the board (scripted with `gh`) and this pull request links its issue

### Story 1.2: API foundation with versioned routes, ProblemDetails, and the committed OpenAPI contract

As an Integrator,
I want every route under `/api/v1`, errors as RFC 9457 ProblemDetails, a liveness endpoint, and an OpenAPI document that is committed and guarded by a test,
So that the API contract is stable and the web client can be generated from it without a .NET toolchain.

**Requirements:** FR26, FR27, FR28; NFR4, NFR5. **UX:** none.

**Acceptance Criteria:**

**Given** the Api project
**When** I request `GET /health`
**Then** it returns 200 without touching a database, and no route other than `/health`, `/health/ready` (added in Story 1.3), `/openapi`, and `/swagger` exists outside `/api/v1`

**Given** the exception mapping in `Api/Errors`
**When** a handler throws
**Then** `DomainRuleException` and `ConcurrencyConflictException` map to 409 `conflict`, `NotFoundException` to 404 `not-found`, model validation to 400 `validation`, missing or invalid JWT to 401 `unauthorized`, wrong role to 403 `forbidden`, all as ProblemDetails

**Given** `dotnet run --project src/ActionLedger.Api -- --export-openapi`
**When** it runs
**Then** it writes `web/actionledger-web/openapi.json` with schemas, a JWT bearer security scheme, and operation transformers for examples
**And** `Api.Tests/OpenApiSnapshotTest` generates the document through `WebApplicationFactory` and fails when it differs from the committed file, and `ci.yml` runs it

**Given** `ASPNETCORE_ENVIRONMENT` is `Development` or `Compose`
**When** I open `/swagger`
**Then** Swagger UI is served with the bearer scheme, and paging parameters `page` and `pageSize` (default 50, max 200) with the `{ items, page, pageSize, total }` envelope are documented

**Given** options `Ai`, `Jwt`, `Database`, `Webhooks`, `Seed` with `ValidateOnStart`
**When** a required key is missing
**Then** the host fails to start with a message naming the key, and Serilog writes structured JSON to stdout with a `correlationId` on every line

### Story 1.3: Database, first migration, migration bundle, and seeded users

As a developer,
I want the database context, the first migration, the migration bundle, and idempotent seeding of the demo users,
So that every later story has a real PostgreSQL to build on and the demo has people to sign in as.

**Requirements:** FR25, FR34 (users); NFR8, NFR10. **UX:** none.

**Acceptance Criteria:**

**Given** `AppDbContext` in Infrastructure with the snake_case naming convention, string enums, `ValueGeneratedNever` on all `Guid` keys, and `IUnitOfWork.CommitAsync` as `SaveChangesAsync`
**When** the first migration runs
**Then** the `users` table exists with a unique index on `username`, `dotnet ef migrations bundle` produces a bundle in the api multi-stage build, and `GET /health/ready` returns 200 only when the database is reachable

**Given** `DemoDataSeeder` as an `IHostedService` with `SeedRepository.AcquireLockAsync` (`pg_advisory_xact_lock`), `SeedCurrentUser`, and `FixedClock`
**When** the api starts with `Seed:Enabled=true`
**Then** Users Dana Whitfield (ActionOfficer), Priya Ramaswamy (ActionOfficer), Marcus Bell (Lead), and the system User `Seed` (flagged `isSystem`) exist with `PasswordHasher<User>` hashes, a second start changes nothing, and `Seed:Enabled=false` seeds nothing
**And** `Infrastructure.Tests` proves this against Testcontainers.PostgreSql 4.15.0

### Story 1.4: Sign in and receive a JWT; list users

As an Action Officer,
I want to sign in with a username and password and receive a JWT that carries my role,
So that every write I make is attributed to me and the API can enforce roles.

**Requirements:** FR23, FR24, FR25; NFR5. **UX:** none.

**Acceptance Criteria:**

**Given** valid credentials
**When** I `POST /api/v1/auth/login`
**Then** I receive an HS256 JWT with `sub`, `name`, `role` and an 8-hour expiry signed with `Jwt:Key`
**And** invalid credentials return 401 `unauthorized` with a message that does not say which part was wrong

**Given** a valid JWT
**When** I `GET /api/v1/users`
**Then** I receive `UserSummaryDto { id, displayName, role }` for every non-system User
**And** `ICurrentUser` reads the `sub` claim and handlers never accept an actor id from a request body

**Given** the OpenAPI document
**When** `Api.Tests` walks every operation except `auth/login` and the health routes
**Then** each returns 401 without a token, and a `Lead`-only test action returns 403 for an ActionOfficer

### Story 1.5: Blazor WebAssembly scaffold with MudBlazor theme, tokens, and the generated API client

As a developer,
I want the Blazor WebAssembly app scaffolded with the MudBlazor theme, the semantic tokens, the generated client, and the architecture rule,
So that every feature screen is built on the same foundation.

**Requirements:** FR27 (generated client); NFR8. **UX:** UX-DR1, UX-DR2, UX-DR22.

**Acceptance Criteria:**

**Given** `dotnet new blazorwasm` creating `src/ActionLedger.Web` on Microsoft.AspNetCore.Components.WebAssembly 10.0.12 with nullable reference types enabled, MudBlazor 9.10.0 registered through `AddMudServices` with a `MudTheme` palette, and Roboto Mono loaded
**When** the `NSwag.MSBuild` 14.7.1 pre-build target runs
**Then** `openapi.json` is committed at `src/ActionLedger.Web/openapi.json` (moved there from `web/actionledger-web/`, with `--export-openapi` and `OpenApiSnapshotTest` following it) and the typed client is generated from it into git-ignored `src/ActionLedger.Web/Core/Api/`, and a contract change that has not been re-exported fails `dotnet build` with a compile error

**Given** the feature folders `Auth`, `Meetings`, `Review`, `Actions`, `Audit` under `src/ActionLedger.Web/Features` and `src/ActionLedger.Web/Core/`
**When** `Architecture.Tests` runs
**Then** the build fails when `System.Net.Http.HttpClient` or the generated client is referenced from any type outside `Core/` and `Features/*/Data/`
**And** `tests/Web.Tests` exists on bUnit 2.11.3, and the web project builds and its tests run as part of `dotnet build` and `dotnet test` in `ci.yml` with no separate Node step

**Given** the `MudTheme` loads
**When** I inspect the styles
**Then** CSS custom properties exist for the ai-provenance, human-provenance, low-confidence, success, and neutral-container families with light and dark pairs matching DESIGN.md, and content sits in a 1280px container with 24px gutters

### Story 1.6: Login screen, session, shell, and global state patterns

As an Action Officer,
I want to sign in, see the toolbar with Meetings and Actions, and get consistent loading, error, and not-found behavior everywhere,
So that the shell exists for every screen and failures never leave me guessing.

**Requirements:** FR23 (web); NFR8. **UX:** UX-DR11, UX-DR18, UX-DR19, UX-DR20.

**Acceptance Criteria:**

**Given** the Login screen
**When** I submit valid credentials
**Then** `Core/Auth/SessionState.cs` holds the token in memory, the toolbar shows the product name, Meetings and Actions links with active state, and a user menu with my display name, role, and "Sign out", and `Core/Users/UserDirectory.cs` loads the roster once
**And** on 401 the form shows "Sign-in failed. Check your username and password."

**Given** a `DelegatingHandler` on the generated client's `HttpClient` and the shell
**When** any request runs
**Then** a `MudProgressLinear` under the toolbar shows during loads; a failed load shows "Couldn't load. {problem title}" with Retry; an unmatched or 404 detail route shows a Not found page with a link to the parent list; 403 shows the `ISnackbar` message "Your role does not allow this."; 401 redirects to Login with "Session expired. Sign in again." and restores the attempted route; a failed write shows the problem title with Retry and keeps form values; write buttons disable while in flight

**Given** shared date and instant formatters and a voice constants file
**When** any screen renders a date or instant
**Then** dates render `YYYY-MM-DD`, instants `YYYY-MM-DD HH:mm UTC`, no relative time appears, and Voice and Tone strings are used verbatim
**And** the login page component, `SessionState`, and the delegating handler have bUnit tests in `tests/Web.Tests` with the generated client mocked

### Story 1.7: One-command compose environment with migrate, api, and web

As an interview panelist,
I want `docker compose up` from a clean clone to give me a working login page with no host .NET toolchain,
So that the demo starts from nothing on any machine.

**Requirements:** FR36 (skeleton); NFR1 (proxy timeouts), NFR10, NFR11. **UX:** none.

**Acceptance Criteria:**

**Given** Dockerfiles for `api` (multi-stage build that also produces the migration bundle), `migrate`, and `web` (nginx 1.30-alpine with `nginx.conf.template`)
**When** I run `docker compose up` on the development Mac with no cached images
**Then** `db` (`postgres:18-alpine`) starts, `migrate` applies the bundle and exits 0, `api` starts only after `migrate` completes successfully (`depends_on: condition: service_completed_successfully`) and passes its `/health` healthcheck, and `web` serves the login page
**And** `api` declares `extra_hosts: ["host.docker.internal:host-gateway"]`

**Given** the `web` container
**When** it starts
**Then** it templates `API_UPSTREAM` into nginx and proxies `/api`, `/swagger`, and `/openapi` with `proxy_read_timeout` and `proxy_send_timeout` of 200 seconds

**Given** `.env.example`
**When** I read it
**Then** every key from the spine's Config keys row is present with a placeholder, `Ai:Provider=Fake` is the compose default, no secret is committed, and `ci.yml` builds all images

## Epic 2: Capture meeting notes and get AI proposals (Sunday, with 2.3 on Saturday evening and 2.7 on Monday morning)

An Action Officer can create a Meeting, paste notes once, run extraction against the Fake provider or LM Studio, and see validated proposals with confidence, source excerpts, and full run metadata. The provider is swappable by configuration.

### Story 2.1: Meeting and immutable notes API

As an Action Officer,
I want to create a Meeting and attach its notes exactly once through the API,
So that extraction always works from the same immutable text.

**Requirements:** FR1, FR2, FR3 (API). **UX:** none.

**Acceptance Criteria:**

**Given** the `Meeting` aggregate owning `MeetingNotes` and a migration adding `meetings` (unique `(title, meeting_date)`, `xmin`, `attendees` as `text[]`) and `meeting_notes` (unique `meeting_id`)
**When** I `POST /api/v1/meetings` with title (1 to 200), date, and attendees (each 1 to 100)
**Then** I receive 201 with the Meeting id and `createdByUserId` from my JWT, invalid input returns 400, and a duplicate title and date returns 409

**Given** a Meeting without notes
**When** I `PUT /api/v1/meetings/<id>/notes` with 1 to 50,000 characters
**Then** the notes are stored byte-for-byte with a SHA-256, and a second `PUT` returns 409
**And** `Api.Tests` proves no endpoint updates or deletes notes

**Given** `GET /api/v1/meetings` and `GET /api/v1/meetings/<id>`
**When** I call them
**Then** the list returns `MeetingSummaryDto { runCount, trackedActionCount }` sorted by Meeting date then creation time descending with paging, and the detail returns fields and notes as pasted

### Story 2.2: Meeting List, New meeting dialog, and notes paste area

As an Action Officer,
I want to create a Meeting and paste its notes in the web app,
So that I can start the extraction flow from the screen.

**Requirements:** FR1, FR2, FR3 (UI). **UX:** UX-DR12, UX-DR13 (notes area).

**Acceptance Criteria:**

**Given** the Meeting List at `/meetings`
**When** it loads
**Then** a `MudTable` shows Title, Date, Runs, Tracked Actions sorted by Meeting date descending, a row click or Enter opens Meeting Detail, and a paginator appears at 50 rows

**Given** I click "New meeting"
**When** I complete the `IDialogService` dialog
**Then** Title (required, max 200), Date (required), and Attendees chip input (a `MudChipSet` fed by a `MudTextField`, each max 100) validate with messages under each field, and "Create" opens Meeting Detail

**Given** Meeting Detail without notes
**When** I paste notes
**Then** the textarea shows a live count against 50,000, the caption "Notes cannot be changed after saving", and "Save notes" (enabled only when non-empty) confirms in a dialog with the same sentence; after save the notes render read-only with `white-space: pre-wrap` and "Saved {timestamp} UTC, immutable", with no edit affordance
**And** the meetings page component and its data service have bUnit tests in `tests/Web.Tests`

### Story 2.3: Fixture catalog and prompt v1 (Saturday evening)

As a developer,
I want the fictional fixture cases and the versioned extraction prompt in the repository,
So that the Fake provider, the seeder, and the Evaluation Gate share one source.

**Requirements:** FR6 (prompt file), FR8 (untrusted notes), FR37. **UX:** none.

**Acceptance Criteria:**

**Given** `fixtures/extraction/`
**When** I list it
**Then** it holds 12 to 15 Pinecrest Regional Office cases, each `<case>.md` with front matter `title`, `meetingDate`, `attendees`, `seed`, and optional `injectionSpan`, each `<case>.expected.json` a valid `extract-actions.schema.json` document, and `roster.json` with Dana Whitfield, Priya Ramaswamy, Marcus Bell and aliases
**And** cases cover plain actions with owner and date (4), no owner (2), no due date (2), relative dates (2), discussion items that are not actions (2), one action mentioned twice (1), and one prompt injection (1); exactly three are marked `seed: true` (office move, training event, equipment inventory) with the proposals the PRD addendum storyline needs; all content is obviously fictional

**Given** `prompts/extract-actions.v1.md`
**When** I read it
**Then** it instructs the model to treat the notes as data and ignore instructions inside them, asks for the schema's fields including the verbatim source sentence, and resolves relative dates against the Meeting date

### Story 2.4: Extraction seam, output validation, and the Fake provider

As a developer,
I want `IActionExtractor` implemented once on `IChatClient` with strict validation, excerpt verification, and a deterministic Fake,
So that every provider goes through the same validated path.

**Requirements:** FR5, FR7 (seam); NFR7. **UX:** none.

**Acceptance Criteria:**

**Given** `ExtractionRequest`, `ExtractionResult`, `ExtractionOutput`, `extract-actions.schema.json` (with a top-level `version`), `ExtractionOutputValidator`, `TextNormalization`, and `ExcerptVerifier` in `Application/Ai`
**When** a response is validated
**Then** strict System.Text.Json deserialization (required members, unmapped members disallowed) plus the validator enforce the schema's lengths, ranges, and date format; an unverifiable Source Excerpt drops that proposal into `dropped` with a warning carrying the text; and a unit test asserts `JsonSchemaExporter` parity on names, required set, and types
**And** `Architecture.Tests` fails if any type outside `Application/Ai` has a name containing `Normaliz`

**Given** `ChatClientActionExtractor` in Infrastructure on Microsoft.Extensions.AI 10.10.0 with `PromptCatalog` reading the embedded `prompts/*.md` (`Current` is `Ai:PromptVersion` when set, else the highest N, validated at startup)
**When** it runs with any `IChatClient`
**Then** it sends the prompt, notes, and Meeting date with `ChatOptions.ResponseFormat` from the schema and `strict = true`, retries once on invalid output, and returns `ExtractionResult.Failed(reason, metrics)` rather than throwing
**And** `Application.Tests` covers valid, invalid-then-valid, and invalid-twice responses

**Given** `FakeChatClient` and `FixtureCatalog` (the embedded `fixtures/extraction/`)
**When** notes whose normalized SHA-256 match a case are extracted
**Then** the case's expected JSON is returned with zero token counts; for unknown notes it returns up to five modal-verb sentence proposals with confidences 0.85 and one 0.55, within 1 second

### Story 2.5: Extraction Run and proposals persisted with AI Proposal revisions

As an Action Officer,
I want to start an extraction run and get the run and its proposals back,
So that proposals exist to review and every run is reproducible.

**Requirements:** FR4, FR6, FR9 (API), FR10 (read model), FR14 (flag), FR15 (pre-selection), FR21 (AiProposal row). **UX:** none.

**Acceptance Criteria:**

**Given** `ExtractionRun.Start(...)` and `AddProposals(...)` in Domain, `ActionRevision` as its own root with `Sequence`, `IActionRevisionRepository` exposing `AddRange` and an ordered read only, and a migration adding `extraction_runs`, `proposed_actions` (unique `(extraction_run_id, ordinal)`, `xmin`), and `action_revisions` (index `(target_type, target_id, sequence)`)
**When** I `POST /api/v1/meetings/<id>/runs` with `Ai:Provider=Fake`
**Then** `RunExtractionHandler` persists the run with provider, model, Prompt Version, `SchemaVersion`, start timestamp, duration, tokens, outcome, `FailureReason`, and warnings, plus one AiProposal revision per proposal (JSON new-value, null actor) in one commit, and returns 201 `RunDto { id, outcome }` for Succeeded and Failed alike
**And** a Meeting without notes returns 400, and a Meeting may have any number of runs

**Given** `ProposedActionReadModel` in `Application/Review` with `OwnerResolver.Match` and `IExtractionSettings`
**When** I `GET /api/v1/runs/<id>`
**Then** the run carries every field above and its proposals in AI order as `ProposedActionDto` with `isLowConfidence` (against `Ai:LowConfidenceThreshold`, default 0.70), `suggestedOwnerUserId` (case-insensitive display-name match or null), and `reviewState`
**And** one `ExtractionRunCompleted` log event carries correlation id, provider, model, Prompt Version, duration, tokens, and outcome, and no notes text

### Story 2.6: Run extraction from Meeting Detail and inspect Run Detail

As an Action Officer,
I want to press "Run extraction" and see the run's metadata, warnings, and proposals,
So that I know what produced the proposals before I review them.

**Requirements:** FR4 (UI), FR9. **UX:** UX-DR13 (run button), UX-DR14, UX-DR6 (ReviewStateChip).

**Acceptance Criteria:**

**Given** Meeting Detail with no notes
**When** I look at the Run extraction button
**Then** it is disabled with the visible caption "Add notes first"

**Given** Meeting Detail with notes
**When** I click "Run extraction"
**Then** the button disables, a `MudProgressLinear` shows with "Extracting with {provider} · {model}. This can take up to a minute with a local model." (provider and model from `GET /api/v1/ai/provider`, which this story adds returning `{ provider, model }` from `IAiProviderInfo`), nothing else is blocked, and on success the app navigates to Run Detail for the new run (Story 3.4 changes the target to the Review Screen)
**And** on failure the run appears in the run list as Failed with the server-supplied reason

**Given** the run list (`MudTable`: started, Prompt Version, provider and model, outcome, proposal count, Pending count) and Run Detail
**When** I open a run
**Then** the metadata definition list shows every FR6 field with tokens rendered as "0" never blank and warnings expandable to the dropped excerpts; a Failed run shows its failure reason verbatim and a "Run again" button; proposal rows appear in AI order with a `ReviewStateChip`, decider display name, timestamp, and rejection reason as visible text (empty until Epic 3)
**And** the run detail page component and its data service have bUnit tests in `tests/Web.Tests`

### Story 2.7: Real providers through the same seam: LM Studio, Ollama, and Azure OpenAI (Monday morning)

As an Action Officer,
I want to switch the provider to LM Studio on my Mac by configuration and get real proposals,
So that the demo runs on a local model and the provider-swap proof point holds.

**Requirements:** FR7; NFR1, NFR4. **UX:** none.

**Acceptance Criteria:**

**Given** `LocalOpenAIClientFactory` and `AzureOpenAIClientFactory` built on the OpenAI SDK 2.14.0 with `OpenAIClientOptions.Endpoint`
**When** `Ai:Provider=LocalOpenAI` with `Ai:LocalOpenAI:BaseUrl=http://host.docker.internal:1234/v1` and a model name
**Then** extraction goes through the unchanged `ChatClientActionExtractor`, each call times out at `Ai:CallTimeoutSeconds` (90) so a run never exceeds 180 seconds, and a real run on a fixture case returns proposals
**And** `Architecture.Tests` proves Application references no AI SDK, and the diff for this story touches only `Infrastructure/Ai/providers`, DI registration, and configuration

**Given** the api starts with `LocalOpenAI`
**When** `GET {BaseUrl}/models` is unreachable
**Then** the host fails to start with a clear message; with `AzureOpenAI` (endpoint `https://<resource>.openai.azure.com/openai/v1/`, model, api-key), a missing value fails the same way

## Epic 3: Decide what becomes tracked work (Sunday)

An Action Officer can approve, edit-and-approve, or reject each proposal on the Review Screen with the source sentence highlighted in context. Every decision is attributed and audited, and approved proposals become Tracked Actions with the outbox row committed in the same transaction.

### Story 3.1: Decision domain model with ordered revisions and the Tracked Action root

As a developer,
I want `ProposedAction.Decide` to be the only way a proposal changes state and to return the Tracked Action and revisions it produced,
So that the trust model is enforced in the entity, not the controller.

**Requirements:** FR11, FR12, FR13, FR16, FR21 (domain). **UX:** none.

**Acceptance Criteria:**

**Given** `ProposedAction.Decide(kind, edits, actor, now)`, `DecisionResult`, `TrackedAction` as a root with private setters, `AggregateRoot.Raise`, and a migration adding `tracked_actions` (unique `proposed_action_id`, `xmin`)
**When** `Decide` runs on a Pending proposal
**Then** Approved and Edited create a Tracked Action with status Open and raise `TrackedActionCreated(trackedActionId, proposedActionId, kind)` on it, Rejected creates none, and the result carries a ReviewDecision revision then one FieldEdit revision per changed field, all stamped with the single `now` and increasing `Sequence`
**And** `Decide` on a non-Pending proposal throws `DomainRuleException`; the proposal's `ReviewState`, `DecidedByUserId`, `DecidedAt`, `RejectionReason` copy is set alongside the revision
**And** `Domain.Tests` covers every Review State transition and the revision order

### Story 3.2: Decision endpoint with owner resolution and concurrency safety

As an Action Officer,
I want to approve, edit-and-approve, or reject a proposal through the API,
So that only my decision creates tracked work and two officers can never both decide one proposal.

**Requirements:** FR11, FR12, FR13, FR15, FR16 (API); NFR8. **UX:** none.

**Acceptance Criteria:**

**Given** `DecideProposalHandler`, `IActionRepository`, and `IActionRevisionRepository`
**When** I `POST /api/v1/proposed-actions/<id>/decision` with `{ ownerUserId, dueDate, description, reason }`
**Then** the handler derives Approved when nothing differs from the proposal (the `OwnerResolver.Match` result counting as the proposed owner) and Edited otherwise, calls `Decide`, adds the returned Tracked Action through `IActionRepository.Add` and the revisions through `AddRange`, and commits once; the Tracked Action's owner is exactly the sent `ownerUserId` (null means Unassigned)
**And** `Application.Tests` asserts both adds happen before the commit and that the handler never assigns an owner from the match

**Given** a proposal that is not Pending
**When** any decision is posted
**Then** the response is 409 `conflict`, and two concurrent decisions on the same proposal yield exactly one Tracked Action and one 409, proven in `Api.Tests`
**And** `Infrastructure.Tests` asserts the proposal's decision copy agrees with its revision after each kind, and `Api.Tests` asserts the OpenAPI document has no `POST /tracked-actions`

### Story 3.3: Transactional outbox row on every approval

As an Integrator,
I want every approved action to leave an outbox row in the same transaction,
So that no approval is ever lost before delivery.

**Requirements:** FR16 (event), FR29 (entities), FR30; NFR3. **UX:** none.

**Acceptance Criteria:**

**Given** `WebhookSubscription` and `OutboxMessage` roots with a migration (`text[]` event types, `jsonb` payload, `xmin`, index `(state, next_attempt_at)`), `WebhookEventDto`, `WebhookEventTypes`, and `IWebhookPayloadBuilder` in `Application/Webhooks`, and `IClock` injected into `AppDbContext`
**When** a decision commits with `TrackedActionCreated` raised on the added Tracked Action
**Then** the outbox pipeline in `SaveChangesAsync` builds the FR30 payload (event id, `action.approved`, timestamp, Tracked Action, proposal, decision with display names) and writes one Pending `OutboxMessage` per active subscription whose event types include `action.approved`, all sharing the event id, in the same transaction
**And** `SaveChangesAsync(suppressOutbox: true)` exists and `Architecture.Tests` asserts it is referenced only from `Infrastructure/Seed`

**Given** a decision whose transaction fails after the outbox write
**When** the failure is forced in `Infrastructure.Tests` against Testcontainers
**Then** neither the Tracked Action nor the outbox row exists (NFR3)

**Given** a subscription is inactive or lacks the event type
**When** an approval commits
**Then** no outbox row is written for it

### Story 3.4: Review Screen layout with proposal cards and source highlighting

As an Action Officer,
I want to see each proposal beside the notes it came from,
So that I can judge every proposal in context.

**Requirements:** FR10, FR14 (UI); NFR2. **UX:** UX-DR3, UX-DR4, UX-DR6 (ReviewStateChip), UX-DR7, UX-DR8 (Pending and Decided rendering), UX-DR9, UX-DR10.

**Acceptance Criteria:**

**Given** shared `ProvenanceChip`, `LowConfidenceBadge`, `ReviewStateChip`, and `PendingCounter` components and the `ProposalCard` component
**When** I open `/meetings/:id/runs/:runId/review` at 1200px or wider
**Then** the notes pane (min 360px, `white-space: pre-wrap`) sits left and the cards right in AI return order with independent scroll; each Pending card shows "Proposed by AI", the Confidence Score in Roboto Mono, the Low Confidence badge and 4px left border when `isLowConfidence`, read-only description, owner with the always-visible hint "AI suggested: {text}" ("Unassigned" and "AI suggested: none" when empty, "No due date proposed" when null), the Source Excerpt blockquote, and Reject, Edit, Approve controls
**And** between 1024 and 1199px the notes pane becomes a collapsed `MudExpansionPanels` above the cards; a run with zero proposals shows "The AI found no actions in these notes." with "Run again" and "Back to meeting"; Story 2.6's post-run navigation now targets this route

**Given** a card is focused or hovered, or its blockquote is clicked
**When** the highlight applies
**Then** the matching sentence in the notes pane is highlighted on the AI provenance container, scrolled into view, and referenced by `aria-describedby`, one highlight at a time

**Given** already-decided proposals from the API
**When** the screen renders
**Then** decided cards keep their place and Source Excerpt and show "Decided by {name}" with the timestamp beside it, the Review State chip, "Reason: {text}" or "No reason given" when Rejected, a "Proposed" column beside decided values when Edited, and a "View action" link to `/actions/:id` when Approved or Edited (the route lands in Story 4.4; until then the Not found page from Story 1.6 shows); the Pending counter reads "{n} proposals pending" or "All proposals decided" with a "View actions" link to `/actions?meetingId=<id>` (route lands in Story 4.3)
**And** a run with 50 proposals renders within 2 seconds on compose

### Story 3.5: Make decisions on the Review Screen

As an Action Officer,
I want to approve, edit-and-approve, or reject each card and see the result in place,
So that nothing the AI said becomes a record without my hand on it.

**Requirements:** FR11, FR12, FR13, FR15 (UI); NFR6, NFR8. **UX:** UX-DR8 (edit mode and reject dialog), UX-DR19, UX-DR21.

**Acceptance Criteria:**

**Given** I click Edit on a Pending card
**When** the card enters edit mode
**Then** description becomes a `MudTextField`, owner a `MudSelect` over the `UserDirectory` roster plus Unassigned pre-selected from `suggestedOwnerUserId`, due date a `MudDatePicker` with a clear button, and the action row becomes Cancel and "Approve" or "Approve with edits" with the label driven by a diff against the proposed values; Cancel restores the proposed values

**Given** I click Reject
**When** the `IDialogService` dialog opens
**Then** it offers an optional reason and a Reject confirm

**Given** I approve, approve with edits, or reject
**When** the response returns
**Then** the card re-renders in its decided state per Story 3.4, the Pending counter updates inside an `aria-live` region, the write button was disabled while in flight, and a 409 shows the `ISnackbar` message "Already changed. Reloading." and refreshes the run

**Given** the Review Screen
**When** the manual keyboard pass and browser axe scan run
**Then** every control is reachable and named, no indicator relies on color alone, the results are recorded in the pull request checklist, and the review page component and its data service have bUnit tests in `tests/Web.Tests`

## Epic 4: Track, filter, and audit actions (Monday)

A Lead can find what is slipping in a filterable Action List with the Overdue indicator, change status within the allowed transitions (Cancelled is Lead-only), edit fields, and open any action's full Audit Trail from AI proposal to current state.

### Story 4.1: Action Status transitions and field edits API

As a Lead,
I want to change an action's status within the allowed transitions and edit its fields through the API,
So that the committed record stays consistent and every change is audited.

**Requirements:** FR17, FR20 (API), FR21, FR24 (Lead-only Cancelled). **UX:** none.

**Acceptance Criteria:**

**Given** `TrackedAction.Transition(status, actor, now)` and `TrackedAction.Edit(changes, actor, now)` in Domain
**When** I `PUT /api/v1/tracked-actions/<id>/status`
**Then** only Open to In Progress, Open to Complete, Open to Cancelled, In Progress to Complete, In Progress to Cancelled, and In Progress to Open succeed, each writing a StatusChange revision; transitions out of Complete or Cancelled return 409; Cancelled by an ActionOfficer returns 403
**And** `Domain.Tests` covers the full transition table

**Given** `PATCH /api/v1/tracked-actions/<id>` with description, `ownerUserId`, or due date
**When** the action is Open or In Progress
**Then** each changed field writes a FieldEdit revision with my id and the same timestamp; on Complete or Cancelled the response is 409

### Story 4.2: Filterable Tracked Action list API with Overdue computed once

As a Lead,
I want to query actions by owner, status, due date, Meeting, and overdue,
So that I can find what is slipping.

**Requirements:** FR18, FR19 (API); NFR2. **UX:** none.

**Acceptance Criteria:**

**Given** `TrackedActionRules.IsOverdueOn(today)` as a Domain expression, `TrackedAction.IsOverdue(today)`, and indexes `tracked_action(due_date, status)` and `tracked_action(owner_user_id)`
**When** I `GET /api/v1/tracked-actions` with `ownerId=<uuid>|unassigned`, `status=<csv>`, `dueFrom`, `dueTo`, `meetingId`, `overdueOnly`, `sort=dueDate|status`, `dir`, `page`, `pageSize`
**Then** filters combine with AND, `overdueOnly` filters in SQL through the expression, default sort is due date ascending with nulls last, and each `TrackedActionDto` carries `isOverdue`, `ownerDisplayName`, `meetingTitle`, `promptVersion`, `proposedActionId`, `extractionRunId`, `lastChangedByDisplayName`
**And** `Domain.Tests` asserts the compiled and expression forms of Overdue agree, and Complete or Cancelled actions are never Overdue

**Given** an `Api.Tests` case that inserts 500 Tracked Actions through the aggregates into Testcontainers
**When** it requests one page
**Then** the response returns within 2 seconds

### Story 4.3: Action List with filters in the URL and the Overdue indicator

As a Lead,
I want a filterable, sortable Action List that shows what is overdue,
So that one screen answers what is slipping and who owns it.

**Requirements:** FR18, FR19 (UI); NFR6, NFR8. **UX:** UX-DR5, UX-DR6 (StatusChip), UX-DR15, UX-DR21.

**Acceptance Criteria:**

**Given** shared `StatusChip` and `OverdueIndicator` components and the `actions` feature
**When** I open `/actions`
**Then** a `MudTable` with `Dense` shows Description, Owner or "Unassigned", Due date, Status chip, Meeting, and the Overdue indicator (icon plus "Overdue") driven only by `isOverdue`; Due date and Status headers are sortable and drive `sort` and `dir`; default sort is due date ascending; the table pages at 50 with a paginator

**Given** the filter bar (a `MudSelect` for Owner with Unassigned from `UserDirectory`, a multi-select `MudSelect` for Status, a `MudDateRangePicker` for the due date range, a `MudSelect` for Meeting, an "Overdue only" toggle)
**When** I change any filter
**Then** the list reloads immediately, the query string reflects every filter so the view can be linked, "Clear filters" appears, and no matches shows "No actions match these filters." with "Clear filters"

**Given** a row
**When** I click it or press Enter on it
**Then** the app navigates to `/actions/:id` (the route lands in Story 4.4)
**And** the manual keyboard pass and axe scan are recorded in the pull request checklist, and the actions page component and its data service have bUnit tests in `tests/Web.Tests`

### Story 4.4: Action Detail with the Audit Trail

As a Lead,
I want to open an action and read its full history from AI proposal to now,
So that I can see who decided what and when.

**Requirements:** FR22 (UI and endpoint). **UX:** UX-DR16 (header), UX-DR17.

**Acceptance Criteria:**

**Given** `GET /api/v1/tracked-actions/<id>/revisions` returning `ActionRevisionDto` ordered by `OccurredAt` then `Sequence` with `actorDisplayName` (null means AI) and `isLowConfidence` on the AiProposal entry
**When** I open `/actions/:id`
**Then** the header shows the fields, owner, status chip, Overdue indicator, "Created from AI proposal, run {Prompt Version}" linking to Run Detail, and last-changed-by name
**And** the `AuditEntry` timeline shows oldest first the purple AI entry with all five proposed fields and the Low Confidence badge when applicable, then blue human entries with display names, with "Was / Now" columns for field changes, never collapsed
**And** the audit page component and its data service have bUnit tests in `tests/Web.Tests`

### Story 4.5: Status control and inline edit on Action Detail (first P0 cut)

As a Lead,
I want to change an action's status or fields from Action Detail,
So that I can act on what I find.

**Requirements:** FR17, FR20 (UI). **UX:** UX-DR16 (status control and edit fields), UX-DR19.

**Acceptance Criteria:**

**Given** the Status control as a `MudSelect`
**When** I open it as an ActionOfficer
**Then** it lists only allowed transitions, shows Cancelled disabled with the caption "Lead only", applies Open and In Progress transitions on select with the `ISnackbar` message "Status set to {status}" and an Undo that performs the reverse, and confirms Complete and Cancelled in an `IDialogService` dialog with "Set to {status}? This cannot be reopened." with no Undo; as a Lead, Cancelled is enabled
**And** on Complete or Cancelled the control and the edit fields disable with "{Status} actions cannot be edited"

**Given** the inline edit fields with Save and Cancel
**When** I save a change
**Then** the field updates, a new FieldEdit entry appears in the trail, and a 409 shows the `ISnackbar` message "Already changed. Reloading."

## Epic 5: Deliver approved actions to other systems (Monday)

An Integrator receives an HMAC-signed webhook for every approved action, with retries and a visible delivery state, and can verify it against documented headers. The demo receiver shows the event arriving live.

### Story 5.1: Outbox dispatcher with HMAC signing, leasing, and retry

As an Integrator,
I want signed webhook deliveries with retries,
So that I can trust and verify every approved action that reaches my system.

**Requirements:** FR31, FR32; NFR4. **UX:** none.

**Acceptance Criteria:**

**Given** `IWebhookDispatcher` and `IWebhookSigner` in Application, `OutboxDispatcher`, `HmacWebhookSigner`, and `WebhookHttpSender` in Infrastructure, and a `BackgroundService` in the api polling every 5 seconds
**When** Pending rows are due
**Then** `ClaimBatchAsync` leases up to 20 rows in one `UPDATE ... RETURNING` with `FOR UPDATE SKIP LOCKED`, advancing `next_attempt_at` by `Webhooks:LeaseSeconds` (60) before any HTTP call, and this plus `SeedRepository.AcquireLockAsync` are the only raw SQL in the codebase

**Given** a leased message
**When** it is sent
**Then** the POST carries `X-ActionLedger-Event`, `X-ActionLedger-Delivery` (the event id), `X-ActionLedger-Timestamp` (send time of this attempt, ISO 8601 UTC), and `X-ActionLedger-Signature` as `sha256=<hex>` of HMAC-SHA256 over `timestamp + "." + rawBody`, with a `Webhooks:TimeoutSeconds` (10) timeout and the body fixed at enqueue
**And** 2xx calls `MarkDelivered`; non-2xx or timeout calls `RecordFailure`, scheduling 10 s, 30 s, 2 m, 10 m, 30 m and setting Dead after the sixth failure, each result in its own short transaction, with one `WebhookDeliveryAttempted` log event per attempt carrying the message id as correlation id
**And** `Infrastructure.Tests` covers lease, success, failure, and Dead

### Story 5.2: Read endpoints, webhook docs, and the demo receiver in compose

As an interview panelist,
I want to watch a signed webhook arrive on a receiver page and see its delivery state in the API,
So that the integration surface is shown live.

**Requirements:** FR29 (read endpoints), FR31 (documentation), FR33. **UX:** none.

**Acceptance Criteria:**

**Given** `GET /api/v1/webhook-subscriptions` and `GET /api/v1/outbox-messages`
**When** I call them
**Then** the secret is masked to `****` plus the last four characters, and each message shows state, attempt count, last status code, last error, and next attempt time
**And** `docs/webhooks.md` holds the header table and a verification snippet and is linked from the OpenAPI description

**Given** `tools/webhook-receiver` with its Dockerfile added to compose as `receiver`, and `Webhooks:Receiver:Url` and `Webhooks:Receiver:Secret` in configuration shared by both sides
**When** the seeder runs with `Seed:Enabled=true`
**Then** one active `WebhookSubscription` for `action.approved` points at `http://receiver:8080/hook` with that secret

**Given** I approve a proposal in compose
**When** the dispatcher delivers
**Then** the receiver recomputes the HMAC, compares in constant time, and shows the event as verified with its timestamp within 10 seconds; stopping the receiver and approving again shows the message Pending with a growing attempt count, and it delivers after the receiver returns

## Epic 6: Prove quality and ship v1.0.0 (Monday)

The panel can start a fully seeded demo in one command, watch the Evaluation Gate pass in CI with published thresholds and a committed LM Studio baseline, see the Playwright smoke test and the CD pipeline run, and read every artifact under `docs/` at the `v1.0.0` tag.

### Story 6.1: Seeded demo meetings from the fixture catalog

As an interview panelist,
I want every screen populated on first run with three fictional meetings in different states,
So that the demo tells its story without manual setup.

**Requirements:** FR34, FR35. **UX:** none.

**Acceptance Criteria:**

**Given** `DemoDataSeeder` extended to read the `seed: true` cases from `FixtureCatalog`
**When** the api starts with `Seed:Enabled=true`
**Then** each case becomes a Meeting, notes, a Succeeded run attributed to the Fake provider with `extract-actions.v1`, proposals, decisions, and status changes, all created through the aggregate methods with `SeedCurrentUser` and `FixedClock` and committed with `SaveChangesAsync(suppressOutbox: true)`, and a second start changes nothing

**Given** the three seeded Meetings
**When** I sign in as Dana
**Then** the office move Meeting is fully reviewed with four approved, one edited (AI due 2026-09-26 set to 2026-09-12, owner Dana, Open, Overdue), one rejected, and actions in Open, In Progress, and Complete; the training event Meeting has a Succeeded run with five Pending proposals including one low-confidence; the equipment inventory Meeting has the 0.55-confidence "P. Ram" proposal edited to Priya Ramaswamy with the corrected date
**And** at least one Outbox Message is Delivered and one is Dead with recorded attempts, written directly by the seeder, and `ClaimBatchAsync` never picks them up

### Story 6.2: Evaluation Gate scorer and thresholds

As a developer,
I want a deterministic scorer over the fixture catalog with published thresholds,
So that extraction quality is measured, not asserted.

**Requirements:** FR8 (gate), FR38, FR39 (scorer and thresholds). **UX:** none.

**Acceptance Criteria:**

**Given** `tests/Eval` with `thresholds.json` (recall 0.80, precision 0.75, owner 0.85, due date 0.80) and a scorer that runs `IActionExtractor` through Infrastructure and reads kept and dropped proposals from `ExtractionResult`
**When** it runs against a provider
**Then** it matches extracted to expected actions by 50 percent token overlap of excerpts or 0.60 token-set description similarity, greedy one-to-one, using `TextNormalization`; computes pooled precision and recall, owner accuracy via `roster.json` aliases, and due date accuracy (both null matches); and fails the injection case on any three-token overlap with the injected span or any unmatched action
**And** it writes `reports/<provider>-<model>-<promptVersion>-<date>.json` with `server`, `baseUrlHost`, `model`, `promptVersion`, `schemaVersion`, per-case detail, plus a sibling `.md` summary, and prints the thresholds

**Given** the Fake provider
**When** the scorer runs
**Then** precision and recall are 1.0 and the injection case passes

### Story 6.3: eval.yml and the committed LM Studio baseline

As a developer,
I want CI to run the gate on prompt and extractor changes and to hold a real baseline,
So that a change that degrades quality cannot merge.

**Requirements:** FR39 (eval.yml and baseline); NFR11. **UX:** none.

**Acceptance Criteria:**

**Given** `eval.yml` triggered on `prompts/**`, `fixtures/**`, `src/ActionLedger.Infrastructure/Ai/**`, `src/ActionLedger.Application/Ai/**`, `tests/Eval/**`, and manual dispatch
**When** it runs
**Then** the Fake pass always runs; the LocalOpenAI pass runs when `LOCAL_AI_BASE_URL` is set and otherwise the job verifies that the newest committed report under `tests/Eval/reports/` meets `thresholds.json` and logs which report it used; the AzureOpenAI pass runs only when its secret is present

**Given** a local run against LM Studio on the demo Mac
**When** the report is committed
**Then** every threshold is met, one recorded run against a deliberately degraded prompt shows a failing verdict, and the scores are pasted into the PRD addendum as the baseline

### Story 6.4: Playwright smoke test of the happy path

As a developer,
I want one end-to-end test of UJ-1 against the compose environment with the Fake provider,
So that the demo path is proven green before rehearsal.

**Requirements:** NFR8 (Playwright). **UX:** none.

**Acceptance Criteria:**

**Given** `tests/Web.E2E` on Microsoft.Playwright 1.62.0
**When** `ci.yml` runs it against `docker compose up` with `Ai:Provider=Fake`
**Then** the test signs in as Dana, creates a Meeting, pastes a fixture case's notes, runs extraction, approves, edits-and-approves, and rejects proposals until "All proposals decided", follows "View actions", and asserts the Tracked Actions with their owners and statuses
**And** the test fails on any console error

### Story 6.5: Continuous deployment to Azure Container Apps with a documented no-op path

As an interview panelist,
I want to see a merge to main build images, run the migration bundle, and deploy to Azure Container Apps, or clearly report that deployment was skipped,
So that the CD story is real either way.

**Requirements:** NFR11 (cd.yml). **UX:** none.

**Acceptance Criteria:**

**Given** `cd.yml` on merge to `main` and on `v*` tags
**When** it runs
**Then** it pushes `api` and `web` images to GHCR as `ghcr.io/<owner>/actionledger-<service>` tagged with the commit SHA and, on tags, the version; runs the migration bundle; and deploys with `azure/container-apps-deploy-action@v2` using GitHub environment secrets written to Container Apps secrets, with `Seed:Enabled=false`, `/health` as liveness, and `/health/ready` as readiness probes

**Given** the `AZURE_*` secrets are absent
**When** `cd.yml` runs
**Then** it still builds, pushes to GHCR, runs the bundle against a disposable PostgreSQL service, and logs "deploy skipped"

### Story 6.6: DEMO.md, documentation deliverables, release notes, and the v1.0.0 tag

As an interview panelist,
I want the demo script and every artifact that produced the product in the repository, with release notes and a tagged release,
So that I can walk the methodology from the repository alone.

**Requirements:** FR36 (DEMO.md and timing); NFR10, NFR11 (release notes, tag). **UX:** UX-DR23.

**Acceptance Criteria:**

**Given** `DEMO.md` at the repository root
**When** I read it
**Then** it holds the seeded credentials, the numbered click path from the PRD addendum, the LM Studio model to load with the local-model size caveat, the Fake provider fallback steps, and the recorded clean-clone `docker compose up` timings under five minutes on macOS and Windows

**Given** `docs/`
**When** this story merges
**Then** it contains `bmad-seed-prompt.md`, `brief.md`, `prd.md`, `prd-addendum.md`, `architecture.md`, `adrs/ADR-001` to `ADR-007`, `stories/epics.md`, `frontend-architecture.md` with the Blazor mapping diagram, and `webhooks.md`

**Given** all P0 stories are merged with green checks
**When** I tag `v1.0.0` by end of day 2026-09-21
**Then** release notes list the delivered FRs, the eval baseline scores, and any deploy shortfall, and the tag triggers the versioned `cd.yml` run

## Epic 7: P1 enhancements (only if Epics 1 to 6 are green)

An Action Officer can draft a follow-up email, a Lead can see dashboard tiles, and a prompt change can be compared side by side against the previous run. Prefer Story 7.1 if only one lands.

### Story 7.1: Re-run extraction with a different Prompt Version and compare side by side

As an Action Officer,
I want to run extraction with a newer Prompt Version and see both runs' proposals aligned,
So that a prompt change shows its effect on the same notes.

**Requirements:** FR42. **UX:** none.

**Acceptance Criteria:**

**Given** `prompts/extract-actions.v2.md` and `POST /api/v1/meetings/<id>/runs` accepting an optional `promptVersion`
**When** I run with v2 on a Meeting that has a v1 run
**Then** a compare view aligns proposals across the two runs using the FR38 matching rule, highlights unmatched proposals on each side, and shows each run's provider, model, and Prompt Version

### Story 7.2: Dashboard tiles for open, overdue, and completed this week

As a Lead,
I want tiles for open, Overdue, and completed-in-seven-days actions,
So that I get the state of play at a glance.

**Requirements:** FR41. **UX:** none.

**Acceptance Criteria:**

**Given** `GET /api/v1/tracked-actions/summary` added to the committed `openapi.json`
**When** I open the dashboard
**Then** three `MudCard` tiles show the counts and each links to the Action List with the matching filters in the URL

### Story 7.3: AI-drafted follow-up email reviewed before export

As an Action Officer,
I want a draft follow-up email listing a Meeting's Tracked Actions that I can edit and copy or download,
So that follow-up is fast without the system ever sending mail.

**Requirements:** FR40. **UX:** none.

**Acceptance Criteria:**

**Given** a memlog decision on whether `ExtractionRun` gains a `Kind` or a new entity records the draft
**When** I request a draft for a Meeting
**Then** the AI drafts an email from the Tracked Actions, the draft is shown for editing with copy and download only, nothing is sent, and a metadata record with provider, model, and Prompt Version is stored
