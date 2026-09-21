---
title: Adversarial review of ARCHITECTURE-SPINE.md
lens: adversary
artifact: ../ARCHITECTURE-SPINE.md
date: 2026-09-20
reviewer: Reviewer Gate (adversary lens)
---

# Adversarial review: ActionLedger architecture spine

**Verdict:** The rings and the aggregate boundaries hold, but the spine leaves the audit-revision, outbox-payload, decision-concurrency, fixture, and seeding seams with two or three equally legal owners each, so feature builders obeying every AD to the letter can still produce a system that fails to compile its EF model, double-creates Tracked Actions, or seeds webhooks on every cold start.

Method: for each pair of features one level down that share a table, a DTO, a state machine, or a fixture, I built two units that each satisfy every AD literally and checked whether they compose. Each section below is one hole. Severity is by downstream impact on the build and the demo.

Counts: critical 2, high 6, medium 7, low 2.

---

## F-1. ActionRevision has one owner on paper and three writers in code — CRITICAL

**Units.** Tracked Actions (Domain/Actions, `TrackedAction` aggregate) and Extraction plus Review (Domain/Extraction, `ExtractionRun.AddProposals`, `ProposedAction.Decide`), with Audit (Application/Audit) as the reader.

**What Tracked Actions builds.** AD-3 says `TrackedAction` owns the `ActionRevision` list. The natural EF mapping is `HasMany(t => t.Revisions).WithOne().HasForeignKey("TrackedActionId").IsRequired()` and a `_revisions` backing field. Revisions are persisted by cascade when the `TrackedAction` root is added or saved.

**What Extraction and Review build.** AD-7 says revisions are created inside `ExtractionRun.AddProposals` (kind AiProposal, target ProposedAction) and `ProposedAction.Decide` (kind ReviewDecision, target ProposedAction). Those rows have no `TrackedAction`; on Reject there never will be one. So Extraction adds a `_revisions` list on `ProposedAction` and maps it as a second navigation, or Review calls an `IActionRevisionRepository.Add` port because the row has nowhere to live on any aggregate.

**How they clash.**
1. The EF model gets two navigations to one table with a required shadow FK from Tracked Actions. AiProposal rows fail insert with a null `TrackedActionId`, or the model builder throws on the second navigation. AD-7's `TargetType`/`TargetId` polymorphic key cannot be an EF navigation on both sides.
2. The FieldEdit revisions written by `Decide` target the *new* `TrackedAction`, while the ReviewDecision revision targets the `ProposedAction`. One method writes to two aggregates' collections in one call; whichever repository the handler saves through, the other half is untracked unless both roots are added.
3. Ordering. FR-12 requires ReviewDecision then FieldEdit rows "with the same timestamp"; FR-22 shows "oldest first". Audit orders by `occurred_at`, gets ties, and falls back to `id`. `Guid.CreateVersion7()` is millisecond-ordered only, with random low bits, so rows created in one `Decide` call sort nondeterministically. UJ-4's demo trail can show the edit before the decision.
4. Timestamp source. AD-3's signature `Decide(decision, actor, clock)` invites the aggregate to call `clock.UtcNow` once per revision, producing different instants and violating FR-12's "same timestamp".

**Proposed AD text (replace the ownership clause in AD-3 and tighten AD-7).**

> **AD-7 (tightened).** `ActionRevision` is its own persistence root with no navigation from any aggregate. It is created only by the four listed aggregate methods, which return the new revisions to the caller (`IReadOnlyList<ActionRevision>`); the handler adds them through `IActionRevisionRepository.AddRange` before `SaveChangesAsync`. Columns: `Id`, `TargetType`, `TargetId`, `Sequence` (int, strictly increasing per `(TargetType, TargetId)`), `Kind`, `Field`, `OldValue`, `NewValue`, `ActorUserId`, `OccurredAt`. Aggregate methods take `DateTimeOffset now` (captured once by the handler from `IClock`), not `IClock`, so every revision from one call carries one instant. The Audit Trail query orders by `OccurredAt, Sequence`. `Domain.Tests` asserts the FR-12 order and shared timestamp; `Infrastructure.Tests` asserts an Edited decision persists exactly one ReviewDecision plus N FieldEdit rows in one transaction.

> **AD-3 (amended).** Remove "`TrackedAction` (owns `ActionRevision` list)". `TrackedAction` owns no child collection.

---

## F-2. Two officers decide one proposal; nothing stops both — CRITICAL

**Units.** Review (`DecideProposalHandler`, `ProposedAction.Decide`) and Infrastructure/Persistence (entity configurations, migrations).

**What Review builds.** Loads the `ExtractionRun` with the `ProposedAction`, checks `ReviewState == Pending` inside `Decide`, throws `DomainRuleException` otherwise, creates the `TrackedAction`, saves. AD-3 satisfied.

**What Persistence builds.** Tables with the columns in the ER diagram. AD-10 satisfied. No concurrency token is listed in any AD, and no unique index is listed on `tracked_action.proposed_action_id` (the ER diagram says `||--o|` but nothing binds it).

**How they clash.** Two requests read the same Pending row, both pass the in-memory check, both `UPDATE proposed_action SET review_state = ...`, both `INSERT tracked_action`, both raise `TrackedActionCreated`. Result: two Tracked Actions and two webhooks for one proposal, or an Approve and a Reject that both "succeed" leaving `review_state = Rejected` next to a live Tracked Action. The PRD thesis ("one named human decides") is violated with no error. The same race exists on `TrackedAction.Transition` (Open→Complete and Open→Cancelled concurrently) and on `Meeting.AttachNotes` (two saves both pass the null check, two `meeting_notes` rows; FR-2's 409 never fires). Even if one builder adds a unique index, the Api/Errors convention maps only `DomainRuleException`, `NotFoundException`, and validation, so the unique violation and `DbUpdateConcurrencyException` surface as 500.

**Proposed AD text (new AD-20, plus ADR-007 item 5 and an Errors row).**

> **AD-20 — Optimistic concurrency on every state machine.** `ProposedAction`, `TrackedAction`, `Meeting`, and `OutboxMessage` carry a concurrency token: PostgreSQL `xmin` via `UseXminAsConcurrencyToken()` (SQL Server: `rowversion`, ADR-007 item 5). Unique indexes: `tracked_action(proposed_action_id)`, `meeting_notes(meeting_id)`, `proposed_action(extraction_run_id, ordinal)`. `DbUpdateConcurrencyException` and unique-constraint violations are translated in `Infrastructure/Persistence` to `ConcurrencyConflictException` (Application) and mapped to 409 `conflict`. Handlers do not retry; the client reloads. `Infrastructure.Tests` runs two concurrent `Decide` calls and asserts one 409 and exactly one Tracked Action.

---

## F-3. Outbox payload: three legal serializers, no documented shape — HIGH

**Units.** Webhooks (Infrastructure/Persistence outbox event pipeline, Infrastructure/Webhooks dispatcher) and API/OpenAPI (Api, FR-31 "signature scheme documented so an Integrator can verify it"), with Domain (`OutboxMessage`, `TrackedActionCreated`) in between.

**What Webhooks builds.** AD-8 says `AppDbContext.SaveChangesAsync` converts each event to `OutboxMessage` rows. The event carries `TrackedActionId`. The pipeline pulls the `TrackedAction` and `ProposedAction` out of the change tracker, builds a private Infrastructure record with the FR-30 fields, and `JsonSerializer.Serialize`s it into `payload jsonb`. Shape is whatever Infrastructure chose that day.

**What API/OpenAPI builds.** AD-13 says DTOs in Application "are the only shapes that cross the boundary" and FR-31 says the integrator contract is in the API docs. So Api documents the webhook body as `{ event, id, timestamp, trackedAction: TrackedActionDto, proposedAction: ProposedActionDto, decision: ReviewDecisionDto }` in an OpenAPI `webhooks` section. Nobody in Infrastructure references those DTOs, and `TrackedActionDto` needs `isOverdue` and `ownerDisplayName`, which the change tracker cannot supply without a User join inside `SaveChangesAsync`.

**Also unowned.** The mapping from the domain event name `TrackedActionCreated` to the subscription filter value `action.approved` (FR-29). Domain does not know the string; Infrastructure invents it; Api documents a third spelling. And the payload `timestamp` needs `IClock` inside the `DbContext`, which no AD injects.

**How they clash.** The receiver in `tools/webhook-receiver` is written against the documented shape; the dispatcher posts the Infrastructure shape; signature verification passes and deserialization fails. The demo's UJ-3 receiver page shows a verified but unreadable event.

**Proposed AD text (tighten AD-8).**

> **AD-8 (tightened).** Domain events carry ids and the decision kind only (`TrackedActionCreated(TrackedActionId, ProposedActionId, DecisionKind)`). Application/Webhooks owns `WebhookEventDto` (`event`, `id`, `occurredAt`, `trackedAction: TrackedActionDto`, `proposedAction: ProposedActionDto`, `decision`) and `IWebhookPayloadBuilder.BuildAsync(DomainEvent)`, plus the static map `WebhookEventTypes.For(DomainEvent) => "action.approved"`. The Infrastructure event pipeline calls the Application builder and stores its JSON verbatim; it never defines a payload type. `WebhookEventDto` is published in `openapi.json` under `webhooks` and is the shape `tools/webhook-receiver` deserializes. `AppDbContext` receives `IClock` for `created_at` and the payload instant.

---

## F-4. `ProposedAction.Decide` raises an event on an entity that is not an aggregate root — HIGH

**Units.** Review (Domain/Extraction) and Infrastructure/Persistence (outbox event pipeline in `SaveChangesAsync`).

**What Persistence builds.** A `AggregateRoot` base class with `DomainEvents` and `ClearDomainEvents()`, and a collector that scans `ChangeTracker.Entries<AggregateRoot>()`. Roots per AD-3: `Meeting`, `ExtractionRun`, `TrackedAction`, `User`, `WebhookSubscription`, `OutboxMessage`.

**What Review builds.** `ProposedAction.Decide` "raises `TrackedActionCreated`" (AD-8, literally). `ProposedAction` is a child of `ExtractionRun`, so the builder puts the event list on `ProposedAction`, or on the returned `TrackedAction`, or walks a back-reference to the `ExtractionRun`. All three obey the text.

**How they clash.** If the event sits on `ProposedAction`, the collector never sees it: no outbox row, no webhook, and the NFR-3 test passes vacuously (it asserts neither row exists after rollback, and neither row exists after commit either). If the event sits on the returned `TrackedAction` but the handler assumes `ExtractionRun` cascades the new Tracked Action and never calls `ITrackedActionRepository.Add`, the Tracked Action is not tracked, the event is not collected, and the proposal is marked Approved with no Tracked Action.

**Proposed AD text (tighten AD-3 and AD-8).**

> Domain events are raised only through `AggregateRoot.Raise(DomainEvent)`. `ProposedAction.Decide` raises `TrackedActionCreated` on the new `TrackedAction` root it returns. `DecideProposalHandler` must add that root through `ITrackedActionRepository.Add` before `IUnitOfWork.SaveChangesAsync`; `Application.Tests` asserts the add. The Infrastructure collector reads events from `ChangeTracker.Entries<AggregateRoot>()` with state Added or Modified, converts, and clears them after commit. `Infrastructure.Tests` asserts an approval commits exactly one outbox row per active `action.approved` subscription.

---

## F-5. The Fake provider's fixed answers have three owners — HIGH

**Units.** Extraction (Infrastructure/Ai/providers/`FakeChatClient`), Seed (Infrastructure/Seed/`DemoDataSeeder`), and Evaluation Gate (tests/Eval/golden).

**What Extraction builds.** AD-11 makes Fake an `IChatClient` under `ChatClientActionExtractor`. To satisfy the PRD Glossary ("for Golden Set cases and seeded Meetings it returns the fixed expected Proposed Actions"), `FakeChatClient` needs a lookup from notes text to a canned JSON completion. Infrastructure cannot reference `tests/Eval`, so the builder embeds a copy of the cases in Infrastructure, or reads a folder from `Ai:Fake:FixturesPath` (a key not in the AD-16 config list).

**What Seed builds.** Three Pinecrest meetings with notes and proposals written through `ExtractionRun.AddProposals`. The seeder authors its own notes text and its own proposals in C#.

**What Eval builds.** `golden/<case>.md` plus `<case>.expected.json`, 12 to 15 cases, a `roster.json`. AD-19 says Fake must score 1.0 against these.

**How they clash.** Three copies of "what the Fake says for this text". A seeded meeting re-run by Dana in the demo goes through `FakeChatClient`, which does not know the seeder's text, so the modal-verb heuristic fires and the re-run disagrees with the seeded run of the same immutable notes (AD-5's repeatability promise, visibly broken). Eval scores 1.0 only if someone hand-synchronizes the embedded copy with `golden/` after every case edit; the injection case (FR-8) passes or fails depending on which copy is stale. `Application.Tests` (AD-18: "Fake provider") either references Infrastructure to reach `FakeChatClient`, or writes a second `IActionExtractor` stub, giving a fourth definition of Fake.

**Proposed AD text (new AD-21).**

> **AD-21 — One fixture catalog.** `fixtures/extraction/` at the repository root holds every canned case: `<case>.md` (front matter: `title`, `meetingDate`, `attendees`, `seed: true|false`, `injectionSpan?`) and `<case>.expected.json` (the exact `extract-actions.schema.json` document), plus `roster.json`. It is the Golden Set (tests/Eval reads it; there is no `tests/Eval/golden`), the seeder's source of meetings and proposals (`DemoDataSeeder` seeds every case marked `seed: true` and derives its Tracked Actions and decisions from a `<case>.seed.json` beside it), and the Fake's answer key (`FakeChatClient` keys on SHA-256 of the FR-38-normalized notes; unknown text falls back to the modal-verb heuristic). The catalog is copied into the `api` image and the Eval project as content; `Ai:Fake:FixturesPath` is added to the config-key list with a validated default. `Application.Tests` uses `ChatClientActionExtractor` over `FakeChatClient` (referencing Infrastructure/Ai is allowed by AD-1); no other `IActionExtractor` implementation exists in tests.

---

## F-6. FR-12's "kept pre-selection is not a change" cannot be decided without resolving on write — HIGH

**Units.** Review (Application/Review `DecideProposalHandler`, `ProposedActionQueries`) and Extraction (Application/Extraction `RunQueries` for Run Detail), with the Angular `review` and `meetings` features consuming.

**What Review builds.** AD-9: pre-resolution by case-insensitive display name "in the run's read model", never on write. So `ProposedActionQueries` joins `User` and sets `suggestedOwnerUserId`; `isLowConfidence` is computed there too, which requires the threshold. Application may reference Domain only (AD-1), so `Microsoft.Extensions.Options` is out; the builder invents an `IReviewSettings` port or moves the flag into the controller (AD-2 "maps the result", arguably).

**What Extraction builds.** FR-9 Run Detail lists the proposals with Review States. `RunQueries` returns the same `ProposedActionDto` type but does not join users and does not know the threshold, so `suggestedOwnerUserId` is null and `isLowConfidence` is false. The Angular `meetings` feature renders the run; the `review` feature renders the same run; the same proposal is flagged on one screen and not the other, pre-selected on one and empty on the other.

**How they clash on write.** FR-12 says choosing the pre-selected user is Approve, choosing another is Edit. The server must know what was pre-selected. Option A: trust a client flag (the client decides Review State, which contradicts the trust model). Option B: `DecideProposalHandler` re-runs the display-name match to compare with the sent `ownerUserId`, which is "resolving on write", forbidden by AD-9's letter. Option C: the client sends back `suggestedOwnerUserId` from the DTO and the server compares, which is Option A with extra steps. Every builder picks a different option; the resulting Review States and Field Edit rows disagree.

**Proposed AD text (tighten AD-9, add a config port to AD-16).**

> **AD-9 (tightened).** `OwnerResolver.Match(string suggestedOwner, IEnumerable<UserSummary>)` is a pure function in Application/Review. It is called in exactly two places: `ProposedActionReadModel` (the single query class that produces `ProposedActionDto` for both Run Detail and the Review Screen; `RunQueries` composes it) and `DecideProposalHandler`, where it is used only to evaluate FR-12's change test (`sent ownerUserId != Match(...)?.Id`) and never to assign an owner. `TrackedAction.OwnerUserId` is always the value the client sent. `ProposedActionDto` carries `suggestedOwnerUserId` and `isLowConfidence`, computed nowhere else. **AD-16 addendum:** Application defines `IExtractionSettings { decimal LowConfidenceThreshold; string PromptVersion; }`; Infrastructure implements it from validated options. Controllers never read `IOptions`.

---

## F-7. The seeder has nowhere legal to run and, where it runs, it fires webhooks — HIGH

**Units.** Seed/Compose (Infrastructure/Seed, `migrate` service, AD-17) and Webhooks (`OutboxDispatcher` hosted in `api`, AD-8), with Identity (AD-12 `ICurrentUser`) in the way.

**What Compose builds.** AD-17: `migrate` is "an EF migration bundle, runs once". An `efbundle` executable applies migrations and nothing else; it cannot host `DemoDataSeeder`, `PasswordHasher<User>`, or the aggregates. "Migrations never run inside the API at startup" and FR-36 "applies migrations, seeds data" leave seeding implicitly in `api`.

**What Seed builds.** A hosted service in `api` that, after startup, seeds users and three meetings through the aggregates (AD-3 forbids writing rows directly). The fully reviewed meeting requires `ProposedAction.Decide` five times, raising five `TrackedActionCreated` events, which `SaveChangesAsync` turns into Pending outbox rows for the seeded subscription (FR-33). If the seeder reuses `DecideProposalHandler` for convenience, `ICurrentUser` reads a `sub` claim from no HTTP context and throws.

**What Webhooks builds.** `OutboxDispatcher` starts with the host and polls every 5 seconds.

**How they clash.** Every cold start posts five signed webhooks to the receiver before anyone logs in, the receiver page is full before UJ-3 begins, and FR-35's "one Delivered, one Dead" is unobtainable: the seeder cannot set terminal states without a second writer of outbox state (AD-3 says the dispatcher's aggregate methods are the only path; the seeder is not the dispatcher). On Azure Container Apps with two replicas both seed concurrently; idempotency "by username" races into duplicate meetings (no unique index on meeting title). With `Seed` also running on every CD deploy there is no way to turn it off.

**Proposed AD text (tighten AD-17, new clause in AD-8).**

> **AD-17 (tightened).** Seeding is an `IHostedService` in `api`, registered before `OutboxDispatcher` so it completes before the dispatcher's first poll (hosted services start sequentially). It runs only when `Seed:Enabled=true` (default true in compose, false in `cd.yml`). It takes an advisory lock (`pg_advisory_xact_lock`, isolated in `ISeedRepository`, second permitted raw statement in AD-10) and is idempotent on `user.username` and `meeting(title, meeting_date)` unique indexes. It calls aggregate methods with actor = the Seed user id, never Application handlers, so `ICurrentUser` is not involved. **AD-8 addendum:** `OutboxMessage` exposes `MarkDelivered(statusCode, now)`, `RecordFailure(statusCode, error, now, delays)`, and `SeedAs(state, attempts, lastStatus, lastError, now)`; the last is called only by `DemoDataSeeder` in the same transaction as the seeded decision, so the seeded outbox rows commit in their FR-35 terminal states and the dispatcher never sees them Pending.

---

## F-8. A failed run is a 201 to one builder and a ProblemDetails to another — HIGH

**Units.** Extraction (`RunExtractionHandler`) and API/OpenAPI (Api/Errors mapping, the `extraction-failed` ProblemDetails type in AD-13), with the Angular `meetings` feature consuming.

**What Extraction builds.** FR-4 and the ARCHITECTURE.md sequence: the handler stores the run with `Outcome = Failed` and `FailureReason`, saves, returns `RunDto`, controller replies 201. UJ-1's edge case ("shows as failed with the reason, and Dana re-runs it") depends on the row existing.

**What API builds.** AD-13 lists `extraction-failed` among the five ProblemDetails types and the Errors convention says `ExtractionFailedException` is "recorded on the run, not thrown to the client", which is self-contradictory: a ProblemDetails type exists for something never thrown. The Api builder maps `ExtractionFailedException` to 502 `extraction-failed`. If the extractor throws (AD-11: "retries once, then fails the run") before the handler reaches `SaveChangesAsync`, the Failed run is never persisted, Run Detail has nothing to show, and the generated Angular client throws on 502 so the `meetings` container shows a toast instead of a failed run card.

**Proposed AD text (tighten AD-11 and the Errors row).**

> `IActionExtractor.ExtractAsync` never throws for provider or validation failure; it returns `ExtractionResult.Failed(reason, metrics)`. `RunExtractionHandler` always persists the `ExtractionRun` (Succeeded or Failed) and returns `RunDto`; the controller returns 201 in both cases. `ExtractionFailedException` and the `extraction-failed` ProblemDetails type are removed. Exceptions from the extractor are limited to `OperationCanceledException` and misconfiguration, which AD-16 catches at startup.

---

## F-9. Overdue must be computed once, and the Action List must filter it in SQL — MEDIUM

**Units.** Tracked Actions (Domain `TrackedAction.IsOverdue(IClock)`, AD-15) and Tracked Actions queries (Application/Actions `TrackedActionQueries`, FR-18/FR-19 "overdue only" filter, NFR-2 paging).

**What Domain builds.** `IsOverdue(IClock)` as the one implementation.

**What Queries builds.** A paged EF query (`page`, `pageSize`, `total`) with `overdueOnly` must be a SQL predicate: `DueDate < today && (Status == Open || Status == InProgress)`. That is a second implementation of the rule, forbidden by AD-15's "computed once". The alternative, loading everything and filtering with `IsOverdue`, breaks paging and `total`.

**How they clash.** Either two rules drift (one builder forgets Cancelled), or the list is wrong past page 1.

**Proposed AD text (tighten AD-15).**

> The Overdue rule is one expression in Domain: `TrackedActionRules.IsOverdueOn(DateOnly today) : Expression<Func<TrackedAction, bool>>`. `TrackedAction.IsOverdue(DateOnly today)` compiles and applies the same expression; queries pass it to LINQ. Handlers derive `today` from `IClock.UtcNow` once per request. `Domain.Tests` asserts the compiled and expression forms agree on a transition table.

---

## F-10. `openapi.json` cannot be produced inside the web image, so who commits it? — MEDIUM

**Units.** API/OpenAPI (Api emits `openapi.json` at build, AD-13) and Angular web (generates the client at build, fails on contract change, AD-14; NFR-10 no host .NET or Node; `web` image is nginx plus an Angular build).

**What API builds.** `Microsoft.Extensions.ApiDescription.Server` writes `openapi.json` into the Api build output. Not committed.

**What Web builds.** The `web` Dockerfile runs `npm ci && npm run build` in a Node stage. There is no .NET SDK in that stage, so there is no fresh `openapi.json`. The builder either commits the file under `web/actionledger-web/` or adds a .NET stage to the web image. "Fails on a contract change" means a TypeScript compile error in one build, a `git diff` CI step in the other. Generator choice also diverges: `openapi-typescript` emits types only (nothing to "wrap"), `ng-openapi-gen` emits Angular services.

**How they clash.** `docker compose up` from a clean clone (FR-36) fails in the `web` stage when the file is absent, or CI passes while the committed file is stale because nobody owns regeneration.

**Proposed AD text (tighten AD-13).**

> `openapi.json` is committed at `web/actionledger-web/openapi.json`. `Api.Tests` contains `OpenApiSnapshotTest`, which generates the document through `WebApplicationFactory` and asserts equality with the committed file; a controller change without regeneration fails CI. The client is generated by `ng-openapi-gen` (MIT) into `src/app/core/api/` (git-ignored) in the npm `prebuild` script; a contract change fails the Angular build by type error. The `web` image has no .NET stage.

---

## F-11. The excerpt-drop filter and its normalizer have two homes — MEDIUM

**Units.** Extraction (`ChatClientActionExtractor`, Infrastructure/Ai, drops proposals whose excerpt is not a normalized substring) and Evaluation Gate (tests/Eval scorer, FR-38 normalization and FR-5 "the scorer applies the same filter"), later FR-42.

**What Extraction builds.** A private `Normalize` in Infrastructure/Ai: lowercase, `char.IsPunctuation` strip, whitespace collapse.

**What Eval builds.** Its own `Normalize` in tests/Eval: lowercase, regex `[^\w\s]` strip, whitespace collapse. Apostrophes, hyphens, and curly quotes now differ ("don't" becomes `dont` in one and `don t` in the other). AD-19 says Eval runs the extractor "through the same Infrastructure code", so the drop already happened before the scorer sees results, and the scorer's own filter is a second, different pass that can drop a kept proposal or fail to match an expected excerpt the extractor accepted.

**Also unowned.** `SchemaVersion` (AD-6) has no stated source; one builder hardcodes `"1"`, another reads `$id` from the schema file.

**Proposed AD text (tighten AD-11 and AD-19).**

> `TextNormalization.Normalize(string)` and `ExcerptVerifier.IsSubstring(excerpt, notes)` live in `ActionLedger.Application/Ai/` as pure static code. `ChatClientActionExtractor`, the Eval scorer, and FR-42 call them; no other normalizer exists (`Architecture.Tests` fails on any other type whose name contains `Normaliz`). `ExtractionResult` carries both the kept proposals and the dropped ones with their warnings, so the scorer reports drops without re-filtering. `SchemaVersion` is the `version` property of `extract-actions.schema.json`, read by the extractor and copied to the run.

---

## F-12. Domain-generated ids meet EF's generated-key conventions — MEDIUM

**Units.** Domain (UUIDv7 in constructors, conventions table) and Infrastructure/Persistence (entity configurations, repositories).

**What Domain builds.** `Id = Guid.CreateVersion7()` in every constructor; a private parameterless constructor for EF.

**What Persistence builds.** Default conventions: a `Guid` key is `ValueGeneratedOnAdd` with EF's client-side v4 generator. Repository `Add` works, but a repository that uses `Update(root)` or `Attach` for "save this graph" makes EF classify every new child with a set key as Modified, and the first `SaveChangesAsync` fails with `DbUpdateConcurrencyException` (expected 1 row affected, got 0). Revisions returned from `Decide` (F-1) and outbox rows created inside `SaveChangesAsync` are the first victims because they are always new and always have set keys.

**How they clash.** Both builders obey the text; whether the system persists depends on which repository verb the second builder picked.

**Proposed AD text (tighten AD-10).**

> All `Guid` keys are configured `ValueGeneratedNever()` by a model-wide convention in `AppDbContext`. Repositories expose `Add` for new roots and load-then-mutate for existing ones; `Update`, `Attach`, and `AddRange` on a graph are not used. `Infrastructure.Tests` asserts a new `ExtractionRun` with proposals and revisions persists as INSERT statements only.

---

## F-13. `ExtractionRun` is both owned by `Meeting` and its own aggregate — MEDIUM

**Units.** Meetings (Domain/Meetings, `Meeting` "owns MeetingNotes, ExtractionRun list") and Extraction (Domain/Extraction, `ExtractionRun` "owns ProposedAction list", `RunExtractionHandler`).

**What Meetings builds.** `Meeting.StartRun(notes, provider, ...)` appends to `_runs`; `IMeetingRepository.Get` includes runs and proposals so the aggregate is complete. Every Meeting load pulls every run and every proposal.

**What Extraction builds.** `new ExtractionRun(meetingId, notesId, ...)` added through `IExtractionRunRepository`. `DecideProposalHandler` loads the run by proposal id through the same repository. Two creation paths; the same row reachable through two roots in one `DbContext` when a handler touches both repositories.

**Proposed AD text (amend AD-3).**

> `Meeting` owns `MeetingNotes` only and exposes `HasNotes`. `ExtractionRun` is a root holding `MeetingId` and `MeetingNotesId`, created by the factory `ExtractionRun.Start(meeting, notes, provider, model, promptVersion, schemaVersion, actor, now)` in Domain/Extraction and saved through `IExtractionRunRepository`. Meeting List counts (FR-3) are computed in `MeetingQueries`, not by loading runs.

---

## F-14. Nobody owns the user roster the Review picker and the Action List filter need — MEDIUM

**Units.** Identity (FR-23..25, Infrastructure/Auth, Api/Auth) and Review plus Tracked Actions (owner picker, Owner filter), with the Angular `review`, `actions`, and `audit` features.

**What Identity builds.** `POST /api/v1/auth/login`. FR-26's resource list has no `users` resource, and the Capability Map puts Identity in Infrastructure/Auth and Api/Auth only, so Identity builds no roster endpoint.

**What Review and Actions build.** Each needs `[{id, displayName}]`. Review embeds `users[]` in the run DTO; Actions adds `GET /api/v1/users` under `TrackedActionsController`; Audit joins display names server-side. On the web, AD-14 confines HTTP to feature data services and cross-feature state to `session.store.ts`, so `review` and `actions` each fetch and cache the roster separately.

**Proposed AD text (amend the Capability Map and AD-14).**

> `UsersController` at `GET /api/v1/users` returns `UserSummaryDto { id, displayName, role }` to any authenticated user and is owned by Identity. AD-14: cross-feature stores are `core/auth/session.store.ts` and `core/users/users.store.ts` (loaded once after login); no feature fetches users itself.

---

## F-15. Prompt files: embedded resource, mounted folder, or relative path — MEDIUM

**Units.** Extraction (Infrastructure/Prompts `PromptCatalog`, AD-6), Compose/CD (`api` image, AD-17), and Evaluation Gate (tests/Eval runs from its own bin, AD-19).

**What Extraction builds.** `PromptCatalog` reads `/prompts` from disk at startup, version = highest N.

**What Compose builds.** The `api` Dockerfile copies `src/` and builds; `prompts/` at the repository root is not under `src/`, so it is not in the image unless someone adds a `COPY` and a `Prompts:Path` key, which is not in the AD-16 config list and therefore not validated at startup. First extraction in compose fails with file-not-found, contradicting AD-16's own "Prevents".

**What Eval builds.** Runs from `tests/Eval/bin/...`, resolves `../../../../prompts`, and names the report by the version it found. FR-4 says a run uses "the Prompt Version currently configured", but no `Ai:PromptVersion` key exists; Eval and Api can pick different "current" versions after a v2 file lands.

**Proposed AD text (tighten AD-6).**

> Prompt files are embedded resources of `ActionLedger.Infrastructure` (`<EmbeddedResource Include="../../prompts/*.md" LogicalName="prompts/%(Filename)%(Extension)" />`). `IPromptCatalog.Get(version)` and `IPromptCatalog.Current` read them; `Current` is `Ai:PromptVersion` when set, else the highest N. `Ai:PromptVersion` is added to the config-key list and validated to exist at startup. No path configuration and no `COPY` step.

---

## F-16. The outbox claim: a transaction held across HTTP calls, or a lease — LOW

**Units.** Webhooks (`OutboxRepository.ClaimBatchAsync` with `FOR UPDATE SKIP LOCKED`, AD-10) and Webhooks dispatcher (posts with a 10-second timeout, FR-32 states Pending, Delivered, Dead only).

**What one builder does.** Opens a transaction, `SELECT ... FOR UPDATE SKIP LOCKED LIMIT 20`, posts each message inside the transaction (up to 200 seconds of held row locks and an open connection), commits. A crash mid-batch releases the locks and re-delivers the already-posted messages (acceptable at-least-once, but every message in the batch, not just the failed one).

**What another does.** Claims by `UPDATE ... SET next_attempt_at = now() + interval '60 seconds' WHERE id IN (SELECT ... FOR UPDATE SKIP LOCKED) RETURNING *`, commits immediately, and posts outside the transaction. Same single raw statement, different failure behavior, and the Outbox API (FR-32) shows a future `next_attempt_at` on a message that is being delivered right now.

**Proposed AD text (clarify AD-8).**

> `ClaimBatchAsync` leases: one statement that selects with `FOR UPDATE SKIP LOCKED` and advances `next_attempt_at` by `Webhooks:LeaseSeconds` (default 60) in the same `UPDATE ... RETURNING`, committed before any HTTP call. Delivery results are written per message through `OutboxMessage.MarkDelivered` or `RecordFailure`, each in its own short transaction.

---

## F-17. The decision lives twice: proposal columns and the ReviewDecision revision — LOW

**Units.** Review (`ProposedAction.decided_by_user_id`, `decided_at`, `rejection_reason` columns in the ER diagram) and Audit (the ReviewDecision `ActionRevision` row, FR-13 "visible on the Review Screen and Run Detail").

**What Review builds.** Review Screen and Run Detail DTOs read the decision from the proposal columns.

**What Audit builds.** The Audit Trail reads it from the revision row, with `NewValue` carrying the reason.

**How they clash.** Both are written in one `Decide` call today, so no drift at runtime; but the seeder (F-7) and any future backfill must remember both, and the two screens format the reason from different columns. Low impact, but it is a second copy of a fact AD-7 said the revision table owns.

**Proposed AD text (clarify AD-4).**

> `ProposedAction` keeps `ReviewState`, `DecidedByUserId`, `DecidedAt`, and `RejectionReason` as a read-optimized copy; the ReviewDecision revision is authoritative. `Decide` writes both in one call, and `Infrastructure.Tests` asserts they agree after each decision kind.

---

## Summary of proposed spine changes

| Finding | Severity | Spine change |
| --- | --- | --- |
| F-1 Revision ownership, ordering, timestamp | Critical | AD-3 amended, AD-7 tightened (`Sequence`, `now` parameter, own root) |
| F-2 Decide/Transition/notes races | Critical | New AD-20 (concurrency tokens, unique indexes, 409 mapping, ADR-007 item 5) |
| F-3 Outbox payload owner | High | AD-8 tightened (Application `WebhookEventDto`, event-type map, `IClock` in `DbContext`) |
| F-4 Event carrier | High | AD-3/AD-8 tightened (raise on returned root, handler must Add) |
| F-5 Fake fixture catalog | High | New AD-21 (`fixtures/extraction/` shared by Fake, seeder, Eval) |
| F-6 Pre-resolution vs FR-12 change test | High | AD-9 tightened, `IExtractionSettings` port added to AD-16 |
| F-7 Seeder placement and outbox side effects | High | AD-17 tightened, `OutboxMessage.SeedAs`, `Seed:Enabled`, advisory lock |
| F-8 Failed run is not an error | High | AD-11 and Errors row tightened; `extraction-failed` removed |
| F-9 Overdue as expression | Medium | AD-15 tightened |
| F-10 Committed `openapi.json` | Medium | AD-13 tightened |
| F-11 Shared normalizer, `SchemaVersion` source | Medium | AD-11/AD-19 tightened |
| F-12 `ValueGeneratedNever`, `Add` only | Medium | AD-10 tightened |
| F-13 `ExtractionRun` is a root, not owned | Medium | AD-3 amended |
| F-14 Users resource and store | Medium | Capability Map and AD-14 amended |
| F-15 Embedded prompts, `Ai:PromptVersion` | Medium | AD-6 tightened |
| F-16 Outbox lease | Low | AD-8 clarified |
| F-17 Decision copy on proposal | Low | AD-4 clarified |
