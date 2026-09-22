---
title: 'Story 3.3 — Transactional outbox row on every approval'
type: 'feature'
created: '2026-09-22'
baseline_revision: 'f1b601bfdf82fe3c8218c96b6b8942507fde376b'
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

**Problem:** An approval (3.2) creates a Tracked Action and raises `TrackedActionCreated`, but nothing consumes the event. No webhook subscription or outbox table exists, so an approval leaves no record for the Epic 5 dispatcher to deliver. NFR-3 (no approved action without its outbox row) is unproven (epics.md:558-579; FR-29 entities, FR-30, NFR-3; AD-8, AD-10, AD-20, AD-21; ADR-005).

**Approach:** Add the `WebhookSubscription` and `OutboxMessage` roots and a migration. In Application/Webhooks, add the wire shape `WebhookEventDto`, the `WebhookEventTypes` map, and `IWebhookPayloadBuilder` with its implementation. Inside `AppDbContext.SaveChangesAsync`, an outbox pipeline turns each raised event into one Pending `OutboxMessage` per active matching subscription. Those rows go into the same `SaveChanges` call, and therefore the same transaction, as the Tracked Action. `SaveChangesAsync(suppressOutbox: true)` is reserved for the seeder.

## Boundaries & Constraints

**Always:**

- **Domain (`src/ActionLedger.Domain/Webhooks/`):**
  - `WebhookSubscription : AggregateRoot` holds `Url`, `Secret`, `IsActive` and `EventTypes`. `EventTypes` uses the same CLR collection type as `Meeting.Attendees`, stored as `text[]`. The factory `Create(url, secret, eventTypes, isActive)` throws `DomainRuleException` when:
    - the url is not an absolute http/https URI;
    - the secret is blank;
    - the event type list is empty or contains a blank entry.
    Nothing mutates a subscription in this story.
  - `OutboxMessage : AggregateRoot` holds `SubscriptionId`, `EventType`, `EventId`, `Payload` (a JSON string), `State` (`OutboxState { Pending, Delivered, Dead }`), `AttemptCount`, `NextAttemptAt`, `LastStatusCode` (int?), `LastError` (string?) and `CreatedAt`. These match the ARCHITECTURE.md ERD. The factory `Enqueue(subscriptionId, eventType, eventId, payload, now)` gives Pending, 0 attempts, `NextAttemptAt = now` and `CreatedAt = now`. `MarkDelivered`/`RecordFailure` are 5.1's job and are not added here.
- **Migration `AddWebhookOutbox`:**
  - Creates the `webhook_subscriptions` and `outbox_messages` tables.
  - `event_types` is `text[]` and `payload` is `jsonb`. `state` is a string.
  - `outbox_messages` gets an `xmin` token (the `Property<uint>("xmin").IsRowVersion()` shape used by `TrackedActionConfiguration`) and a non-unique index `ix_outbox_messages_state_next_attempt_at`.
  - `subscription_id` is a Restrict foreign key to `webhook_subscriptions`. There is no FK to `tracked_actions`, because the ERD has no such column.
  - Both configurations ignore `DomainEvents`.
- **Application/Webhooks:**
  - `WebhookEventTypes`: the constant `ActionApproved = "action.approved"` and `For(DomainEvent) → string?`. `TrackedActionCreated` maps to `action.approved`; any other event maps to null (no webhook).
  - `WebhookEventDto { eventId, eventType, occurredAt, trackedAction, proposedAction, reviewDecision }`. The nested records follow the PRD addendum's field names (see Design Notes).
  - `WebhookJson.SerializerOptions`: web defaults plus `JsonStringEnumConverter`. This is the same configuration as `Program.cs:96-98`.
  - `IWebhookPayloadBuilder` and a `WebhookPayloadBuilder` implementation, registered in `ApplicationRegistration`.
- **Builder:** `BuildAsync(WebhookEventSource source, IReadDb reads, CancellationToken)`, where `WebhookEventSource(DomainEvent Event, TrackedAction TrackedAction, ProposedAction ProposedAction)`.
  - Values that exist only in memory come from the two tracked entities: every Tracked Action field, and the decider and decision instant (`ProposedAction.DecidedByUserId`/`DecidedAt`).
  - Everything that is already committed comes from at most **one** `IReadDb` query: the meeting id and title (via proposal → run → meeting), plus the owner's and decider's display names.
  - `kind` is the event's `DecisionKind`. `occurredAt` is the event's `OccurredAt`. `eventId` is a new `Guid.CreateVersion7()`, made once per event.
  - `reads` is passed in rather than injected, because the builder is resolved by `AppDbContext` and `ReadDb` depends on `AppDbContext`. Injecting it would create a DI cycle.
- **Pipeline (`AppDbContext`):**
  - The constructor becomes `(options, IClock clock, IWebhookPayloadBuilder payloadBuilder)`.
  - Every async save path goes through one core method: override `SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken)`. The existing concurrency translation moves there.
  - Unless the outbox is suppressed, the core method, before saving:
    1. Snapshots `ChangeTracker.Entries<AggregateRoot>()` in state Added or Modified that have events.
    2. For each event, calls `WebhookEventTypes.For`, and skips it when the result is null.
    3. Finds the decided `ProposedAction` among tracked `ProposedAction` entries by id. If it is not tracked, throws `InvalidOperationException`.
    4. Builds the DTO and serializes it once with `WebhookJson.SerializerOptions`.
    5. Loads active subscriptions whose `EventTypes` contain the type. Loads them once per type per save, through the tracked `Set<WebhookSubscription>()` with `AsNoTracking`.
    6. Adds one `OutboxMessage.Enqueue(sub.Id, type, dto.EventId, json, clock.UtcNow)` per subscription.
  - Then one `base.SaveChangesAsync` runs, so the outbox rows share EF's single transaction with the Tracked Action.
  - After it succeeds, `ClearDomainEvents()` runs on the snapshotted roots, and also after a suppressed save. After a failure, events are not cleared.
- **Suppress:** `public Task<int> SaveChangesAsync(bool suppressOutbox, bool acceptAllChangesOnSuccess = true, CancellationToken cancellationToken = default)`. The first parameter is named so that it cannot collide with EF's `(bool acceptAllChangesOnSuccess, CancellationToken)`. `DemoDataSeeder.SeedAsync` changes its commit to `context.SaveChangesAsync(suppressOutbox: true, cancellationToken: cancellationToken)`, which drops the `IUnitOfWork` resolve there.
- **Sync `SaveChanges`:** override `SaveChanges(bool)` to throw `NotSupportedException`, so no path skips the pipeline. Nothing in the solution calls the sync method.
- **Architecture test:** `SaveChangesAsync(suppressOutbox:` is referenced in `src/**/*.cs` only under `src/ActionLedger.Infrastructure/Seed/`, excluding its own declaration in `AppDbContext.cs`. At least one such reference exists. Implement it as a source scan from `ProjectFile.RepositoryRoot`, ignoring `bin/` and `obj/`.
- **DI:** `TestHost.Build` (Infrastructure.Tests) adds `services.AddActionLedgerApplication()` so that the context resolves.

**Never:**

- No dispatcher, signer, HTTP, `ClaimBatchAsync`, `MarkDelivered`/`RecordFailure`, or read endpoints. Those belong to Epic 5.
- No seeded subscription. That belongs to 5.2.
- No new API routes, and no `openapi.json` or Web change.
- No change to `TrackedActionCreated`, `Decide`, or `DecideProposalHandler`. The handler still commits exactly once through `IUnitOfWork`.
- No explicit or second transaction, and no second `SaveChanges` inside the pipeline. The outbox rows ride the single save.
- The outbox payload never carries a subscription secret.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Approve, two matching subs | 2 active subs with `action.approved`, and 1 decision Approved | 2 Pending rows, same `event_id`, distinct `id`, `event_type` `action.approved`, `attempt_count` 0, `next_attempt_at` = `created_at` = clock now; identical payloads | — |
| Edit and approve | 1 matching sub | 1 row; payload `reviewDecision.kind` `Edited`; `trackedAction` carries the edited values; `proposedAction` carries the original AI values | — |
| Reject | matching sub | no outbox row (no Tracked Action, so no event) | — |
| Inactive or non-matching | one inactive sub with the type, one active sub with only `other.event` | no row for either | — |
| No subscriptions | approval | commit succeeds, 0 outbox rows | — |
| Unassigned owner | `ownerUserId` null | payload `ownerId`/`ownerName` null | — |
| Forced failure after outbox write | interceptor throws after the `INSERT INTO outbox_messages` command executes | neither the Tracked Action nor the outbox row exists; proposal still Pending | the exception propagates |
| Suppressed | `SaveChangesAsync(suppressOutbox: true)` with a raised event and a matching sub | no outbox row; events cleared | — |

</intent-contract>

## Code Map

- `src/ActionLedger.Infrastructure/Persistence/AppDbContext.cs:48-60`: the current `SaveChangesAsync(CancellationToken)` override with `ConcurrencyTranslation`. Move the translation into the `(bool, CancellationToken)` core override and add the pipeline there. `:62-85`: `OnModelCreating`, which has a comment that anticipates OutboxMessage's `xmin`. Update that comment. Add `DbSet`s `WebhookSubscriptions` and `OutboxMessages`.
- `src/ActionLedger.Domain/Common/AggregateRoot.cs`: `DomainEvents` and `ClearDomainEvents()` (nothing calls the latter yet). `DomainEvent.OccurredAt`.
- `src/ActionLedger.Domain/Actions/TrackedAction.cs:40-58`, `TrackedActionCreated.cs`: the event is raised in the constructor, with `OccurredAt = createdAt`, and carries ids and kind only. Read-only.
- `src/ActionLedger.Domain/Extraction/ProposedAction.cs:93-142`: the fields the payload needs (`Id`, `ExtractionRunId`, `Description`, `SuggestedOwner`, `SuggestedDueDate`, `Confidence`, `SourceExcerpt`, `DecidedByUserId`, `DecidedAt`). A child entity of `ExtractionRun`. It is tracked (Modified) in the decision commit, because `ExtractionRunRepository.FindByProposedActionIdAsync` loads it.
- `src/ActionLedger.Domain/Meetings/Meeting.cs`: the `Attendees` type, the model for `EventTypes`.
- `src/ActionLedger.Infrastructure/Persistence/Configurations/TrackedActionConfiguration.cs`: the template for the new configurations (the xmin shape, `Ignore(DomainEvents)`, the Restrict shadow FK, and named index constants). `ModelConventions.cs` already applies snake_case, string enums, `ValueGeneratedNever` and timestamptz. Set `HasColumnType("jsonb")` explicitly on `Payload`.
- `src/ActionLedger.Infrastructure/Migrations/`: the latest is `20260922180354_AddTrackedActions`. Add the new migration with `dotnet dotnet-ef migrations add AddWebhookOutbox --project src/ActionLedger.Infrastructure --startup-project src/ActionLedger.Api`. It needs `Database__ConnectionString`, `Jwt__Key` and `Jwt__Issuer` in the environment.
- `src/ActionLedger.Infrastructure/Persistence/ReadDb.cs`: `internal`. The pipeline passes `new ReadDb(this)` to the builder.
- `src/ActionLedger.Infrastructure/Seed/DemoDataSeeder.cs:103-145`: `SeedAsync`, which commits through `unitOfWork.CommitAsync` at `:139`. Switch it to the suppress overload.
- `src/ActionLedger.Application/ApplicationRegistration.cs`: register `WebhookPayloadBuilder` as `IWebhookPayloadBuilder` (scoped).
- `src/ActionLedger.Api/Program.cs:94-98`: the API's JSON options, which `WebhookJson` mirrors.
- `src/ActionLedger.Application/Review/OwnerRoster.cs`, `Users` (`User.DisplayName`): the display name source for the builder's query.
- `tests/Infrastructure.Tests/TestHost.cs`: add `AddActionLedgerApplication()`. `TrackedActionPersistenceTests.cs:429-560`: the `HandlerIn(scope, actor)` and `FixedCurrentUser` helpers, and the migrated-database helpers (`MigratedDatabaseAsync`, `ColumnsAsync`, `IndexDefinitionAsync`) to mirror. `RecordingCommandInterceptor.cs` plus `ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(...))` (see `ExtractionPersistenceTests.cs:326-331`): the interceptor pattern for the forced failure.
- `tests/Application.Tests/Meetings/MeetingsTests.cs:331-420`: the `FakeReadDb`/`FakeClock` patterns for the builder unit test.
- `tests/Architecture.Tests/ProjectFile.cs:100`: `RepositoryRoot`, used by the source scan.

## Tasks & Acceptance

**Execution:**

- `src/ActionLedger.Domain/Webhooks/{WebhookSubscription,OutboxMessage,OutboxState}.cs`: the roots and factories, per Always.
- `src/ActionLedger.Application/Webhooks/{WebhookEventTypes,WebhookEventDto,WebhookJson,WebhookEventSource,IWebhookPayloadBuilder,WebhookPayloadBuilder}.cs`: the wire shape, the type map, the serializer options, and the builder. Register it in `ApplicationRegistration.cs`.
- `src/ActionLedger.Infrastructure/Persistence/Configurations/{WebhookSubscriptionConfiguration,OutboxMessageConfiguration}.cs`: the mappings, per Always.
- `src/ActionLedger.Infrastructure/Persistence/AppDbContext.cs`: the constructor, the `DbSet`s, the core override with the pipeline and translation, the suppress overload, and the throwing sync `SaveChanges`.
- `src/ActionLedger.Infrastructure/Migrations/*_AddWebhookOutbox.cs`: generated, plus the model snapshot.
- `src/ActionLedger.Infrastructure/Seed/DemoDataSeeder.cs`: commit through `SaveChangesAsync(suppressOutbox: true, ...)`.
- `tests/Infrastructure.Tests/TestHost.cs`: register the Application ring.
- `tests/Domain.Tests/Webhooks/WebhookEntityTests.cs`: the factory rules and the initial `Enqueue` state.
- `tests/Application.Tests/Webhooks/{WebhookEventTypesTests,WebhookPayloadBuilderTests}.cs`: the map (including null for a non-webhook event), and every payload field from a fake read seam. That covers display names, a null owner, Edited values against the original values, and `eventId` being a v7 GUID.
- `tests/Infrastructure.Tests/OutboxPersistenceTests.cs`: against Testcontainers, through `DecideProposalHandler`, cover:
  - Every matrix row, including the NFR-3 forced failure: an interceptor on `ReaderExecuted`/`NonQueryExecuted` for a command whose text contains `outbox_messages` throws, then new scopes assert 0 `tracked_actions` and 0 `outbox_messages` rows and a Pending proposal.
  - The schema: the columns and types (`text[]`, `jsonb`), the `(state, next_attempt_at)` index, the Restrict FK, and the `xmin` token.
  - The stored `payload` JSON properties.
- `tests/Architecture.Tests/OutboxSuppressionTests.cs`: the source-scan rule.
- `tests/Api.Tests/` (an existing JSON/contract test file or a new `WebhookJsonTests.cs`): a `WebhookEventDto` serialized with `WebhookJson.SerializerOptions` equals the same DTO serialized with the host's `IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>`. This pins "the API's JSON options".

**Acceptance Criteria:**

- Given two active `action.approved` subscriptions, when an Approve commits through `DecideProposalHandler` against PostgreSQL, then exactly two Pending `outbox_messages` rows exist, sharing one `event_id`, each with a payload holding `eventId`, `eventType: "action.approved"`, `occurredAt`, `trackedAction` (with `ownerName` and `meetingTitle`), `proposedAction`, and `reviewDecision` (with `byUserName`).
- Given the forced failure after the outbox insert, when the decision commits, then neither the Tracked Action nor any outbox row exists in PostgreSQL.
- Given the source tree, when `Architecture.Tests` runs, then `suppressOutbox:` call sites exist only under `src/ActionLedger.Infrastructure/Seed/`.
- Given the existing suites, when `dotnet test` runs, then everything still passes, including `DecisionEndpointTests`, `DemoDataSeederTests` and `OpenApiSnapshotTest`, with `openapi.json` unchanged.

## Spec Change Log

## Review Triage Log

### 2026-09-22 — Review pass
- verdicts: 33 findings — high 0, medium 5, low 23, false 5, maybe-false 0
- findings:
  - `[low]` `[patch]` (blind) A builder/query throw partway through `EnqueueOutboxAsync` leaves earlier messages Added on the context — the path is unreachable today (one event per decision save), but the fix is direct. Fixed: messages are collected and `AddRange`d only after the loop completes.
  - `[false]` `[reject]` (blind) EventId is not stable across save retries — the retrying execution strategy retries inside `base.SaveChangesAsync`, after the pipeline, so a replay reuses the same staged rows and EventId. No caller re-saves a request-scoped context after a failure.
  - `[low]` `[reject]` (blind) No unique index on `(subscription_id, event_id)` — the duplicate paths it would catch are false or unreachable (rows above), and it adds schema that no rule asks for.
  - `[false]` `[reject]` (blind) Raw EF exceptions escape because the pipeline reads run outside the `try` — `ConcurrencyTranslation` converts only concurrency and unique violations, which a read cannot raise. Every other EF exception already rethrows unchanged (`throw;`), before and after this story.
  - `[medium]` `[patch]` (blind) The suppression guard can be bypassed, and the overload is public and collides with EF's `SaveChangesAsync(bool, …)` — grouped with the edge overload-binding findings. Fixed: the overload is now `internal SaveChangesAsync(CancellationToken = default, bool suppressOutbox = false)`, and the scan is a regex.
  - `[low]` `[reject]` (blind) Every host must call `AddActionLedgerApplication()` — `Program.cs` already does. The only hosts that compose persistence alone are the two test hosts, and both are patched. A `TryAdd` of an Application type from Infrastructure would blur ring ownership.
  - `[low]` `[reject]` (blind) The payload is built before the subscriber lookup — real: one indexed read per approval that has no subscriber. But the intent-contract's pipeline order (build, then load subscriptions) fixes this sequence, so the fix would edit the spec. The "uncommitted run" failure it could expose has no path today (the seeder suppresses the outbox).
  - `[low]` `[reject]` (blind) Event-type matching is case-sensitive, and unknown types are accepted — nothing creates a subscription until 5.2, which seeds `WebhookEventTypes.ActionApproved` verbatim.
  - `[low]` `[reject]` (blind) `OutboxMessage.Enqueue` does not reject `Guid.Empty`, over-long types, or non-JSON payloads — its only caller is the pipeline. It passes a v7 event id, a subscription id read from the database, the 15-character constant type, and serializer output.
  - `[false]` `[reject]` (blind) `ownerName` can be null while `ownerId` is set — `tracked_actions.owner_user_id` is a Restrict FK, and the handler validates the owner against the committed roster. The decider is the JWT subject of an existing User, so a set id always resolves.
  - `[low]` `[patch]` (blind) `clock.UtcNow` is read once per message, so rows in one save can differ — fixed: the time is read once per save.
  - `[low]` `[reject]` (blind) Subscription URL validation allows plain http, userinfo and private addresses (SSRF) — nothing POSTs until 5.1, and the only subscriptions are seeded from operator configuration (5.2). It is a decision for the dispatcher story.
  - `[low]` `[reject]` (blind) Events on Unchanged roots are skipped, and non-webhook events are cleared — a Tracked Action is always Added when it raises `TrackedActionCreated`. Clearing after commit is the AD-8 rule.
  - `[low]` `[reject]` (blind) Missing tests (re-save after failure, multi-root saves, seeder end-to-end, case mismatch) — they cover paths with no caller today. The partial-enqueue fix makes the re-save path trivially correct.
  - `[low]` `[reject]` (blind) The seeder bypasses `IUnitOfWork`, and there is no outbox retention plan — `UnitOfWork` is a pure pass-through (`UnitOfWork.cs`). Retention and a partial claim index belong to Epic 5 and the roadmap.
  - `[medium]` `[patch]` (edge) `SaveChangesAsync(true)` binds to the suppress overload, because derived-type candidates win — confirmed with a scratch program (printed `SUPPRESS=True`). Fixed as in the blind row above. The new test `A_positional_accept_all_changes_flag_still_runs_the_pipeline` proves `SaveChangesAsync(true, ct)` writes the row.
  - `[medium]` `[patch]` (edge) The architecture scan misses positional and whitespace variants — same group. Fixed: a line regex `\bsuppressOutbox\s*:`. Residual: an argument name and its colon split across lines, or a positional `(ct, true)` inside Infrastructure, would still escape. The overload is internal, so no other ring can reach it.
  - `[low]` `[patch]` (edge) AppDbContext.cs is excluded from the scan wholesale — same group. Fixed: only the parameter-declaration line is skipped.
  - `[low]` `[reject]` (edge) A non-seed `suppressOutbox: false` would be flagged (false positive) — flagging any non-seed use of the flag is the intended strictness.
  - `[low]` `[patch]` (edge) A partial enqueue leaves orphan rows — grouped with the first blind row, and fixed the same way.
  - `[low]` `[reject]` (edge) Roots in Unchanged or Deleted state are skipped — same reason as the blind row.
  - `[false]` `[reject]` (edge) A subscription deleted between the read and the insert fails on the FK — no port, endpoint or seeder deletes a subscription (FR-29 non-goal).
  - `[low]` `[reject]` (edge) Case-variant event types never match — same reason as the blind row.
  - `[low]` `[reject]` (edge) Duplicate event types are stored — no delivery impact, and no creator exists yet.
  - `[low]` `[reject]` (edge) `Guid.Empty` ids in `Enqueue` — same reason as the blind row.
  - `[false]` `[reject]` (edge) An owner or decider name can be null while the id is set — same refutation as the blind row.
  - `[low]` `[patch]` (edge) jsonb reorders keys and normalizes whitespace — the storage type is mandated (AD-8, AD-10), and Epic 5 signs the body it reads back, so signatures stay valid. Fixed: the doc claims (`OutboxMessage.Payload`, the `WebhookEventDto` summary) now say jsonb keeps the meaning, not the bytes.
  - `[low]` `[patch]` (edge) The claim "stored exactly as it will be sent" — same group, same fix.
  - `[medium]` `[patch]` (edge) The claim "no call can land here by accident" was false — same group as the overload binding. The remark is rewritten to match the new signature.
  - `[medium]` `[patch]` (edge) The AC-scan claim did not hold for reformatted calls — same group as the scan fix.
  - `[low]` `[patch]` (edge) The `UnitOfWork` remark was stale after the seeder's direct save — fixed: it names the seeder's suppressed save as the one exception.
  - `[low]` `[reject]` (verification-gap, other) The payload is built before the subscriber lookup — same reason as the blind row (fixed order in the intent-contract).
  - `[low]` `[reject]` (intent) No HTTP-level test asserts outbox rows — the Infrastructure tests drive the same `DecideProposalHandler` the endpoint resolves. `DecisionEndpointTests` runs the pipeline end to end, with no subscription, and stays green. No subscription can exist in the running app until 5.2.

## Design Notes

**Why these payload field names, not `TrackedActionDto`/`ProposedActionDto`.** AD-8 names the nested types after API DTOs. `TrackedActionDto` does not exist until Epic 4, and `ProposedActionDto` is the Review Screen's read shape. The PRD addendum (addendum.md:91-98) is the concrete integrator contract, so the nested records copy it:

```json
{ "eventId": "…", "eventType": "action.approved", "occurredAt": "…",
  "trackedAction": { "id", "description", "ownerId", "ownerName", "dueDate", "status", "meetingId", "meetingTitle" },
  "proposedAction": { "id", "description", "suggestedOwner", "suggestedDueDate", "confidence", "sourceExcerpt" },
  "reviewDecision": { "kind", "byUserId", "byUserName", "at" } }
```

Webhook-owned records (`WebhookTrackedAction`, `WebhookProposedAction`, `WebhookReviewDecision`) keep the wire contract from changing when the UI DTOs change.

**Why the builder gets tracked entities.** The payload is built before the one `SaveChanges`, so the new Tracked Action and the decided proposal's decision copy exist only in the change tracker. A database query cannot see them. A two-phase save (save, query, save) inside a transaction would break the retrying execution strategy's replay. So the in-memory values come from the entities, and only already-committed facts (names, meeting title) come from the query.

## Verification

**Commands:**

- `dotnet build ActionLedger.sln`: expected 0 warnings and 0 errors.
- `dotnet test ActionLedger.sln` (no `--nologo`): expected all green (Domain, Application, Infrastructure and Api on Testcontainers, Architecture, Web).
- `dotnet dotnet-ef migrations has-pending-model-changes --project src/ActionLedger.Infrastructure --startup-project src/ActionLedger.Api`: expected no pending changes.
- `git diff --quiet -- src/ActionLedger.Web/openapi.json`: expected no change.

## Auto Run Result

Status: done

**Summary.** Every approval now writes its outbox rows in the same transaction as the Tracked Action.
- **New roots:** `WebhookSubscription` and `OutboxMessage` (Domain/Webhooks), with the `AddWebhookOutbox` migration. It adds `text[]` event types, a `jsonb` payload, `xmin`, the `(state, next_attempt_at)` index, and a Restrict FK to the subscription.
- **Application/Webhooks:** the wire shape `WebhookEventDto`, which follows the PRD addendum's fields; `WebhookEventTypes` (`TrackedActionCreated` → `action.approved`); `WebhookJson`, pinned to the host's JSON options; and `WebhookPayloadBuilder`. The builder takes in-memory values from the tracked entities, and gets names and the meeting title from one read.
- **Pipeline:** `AppDbContext`'s core save runs it. For each webhook event it writes one Pending `OutboxMessage` per active matching subscription, all sharing one `EventId`, in the single `SaveChanges`. Events clear after success.
- **Suppression:** the internal overload `SaveChangesAsync(cancellationToken, suppressOutbox)` is used only by the seeder. An Architecture test enforces that. Synchronous `SaveChanges` is refused.
- **Deviation from the intent-contract's literal signature:** the contract specified `SaveChangesAsync(bool suppressOutbox, bool acceptAllChangesOnSuccess = true, CancellationToken = default)`. Review proved `SaveChangesAsync(true)` binds to that overload and silently suppresses the outbox, which breaks the contract's own "cannot collide with EF's overload" rule. The shipped overload is `internal Task<int> SaveChangesAsync(CancellationToken cancellationToken = default, bool suppressOutbox = false)`. The epic's `SaveChangesAsync(suppressOutbox: true)` call shape is unchanged.

**Files changed**

Domain and Application:
- `src/ActionLedger.Domain/Webhooks/{WebhookSubscription,OutboxMessage,OutboxState}.cs`: the roots and their factories.
- `src/ActionLedger.Application/Webhooks/*`: the wire shape, type map, JSON options, event source, and builder port and implementation.
- `src/ActionLedger.Application/ApplicationRegistration.cs`: registers the builder.

Infrastructure:
- `src/ActionLedger.Infrastructure/Persistence/AppDbContext.cs`: the new DbSets, the outbox pipeline, the suppress overload, and the refused sync save.
- `src/ActionLedger.Infrastructure/Persistence/Configurations/{WebhookSubscription,OutboxMessage}Configuration.cs`: the mappings.
- `src/ActionLedger.Infrastructure/Migrations/20260922185533_AddWebhookOutbox*.cs` and `AppDbContextModelSnapshot.cs`: the migration.
- `src/ActionLedger.Infrastructure/Seed/DemoDataSeeder.cs`: commits with `suppressOutbox: true`.
- `src/ActionLedger.Infrastructure/Persistence/UnitOfWork.cs`: the remark names the seeder exception.

Tests:
- `tests/Domain.Tests/Webhooks/WebhookEntityTests.cs`: the factory rules.
- `tests/Application.Tests/Webhooks/{WebhookEventTypesTests,WebhookPayloadBuilderTests}.cs`: the type map and the payload builder.
- `tests/Infrastructure.Tests/OutboxPersistenceTests.cs`: every matrix row on Testcontainers, including the NFR-3 forced failure, plus the schema and the positional-flag test.
- `tests/Architecture.Tests/OutboxSuppressionTests.cs`: only the seeder may suppress.
- `tests/Api.Tests/WebhookJsonTests.cs`: `WebhookJson` equals the host's JSON options.
- `tests/Infrastructure.Tests/TestHost.cs` and `tests/Api.Tests/AuthEndpointTests.cs`: register the Application ring, which the context now needs.

**Review findings.** There were 33 findings: 0 high, 5 medium, 23 low, 5 false.
- **Patched:** 1 medium entry (the overload binding and the scan bypass, 5 findings in that group), and low entries for the partial enqueue, the per-message clock read, the stale `UnitOfWork` remark, and the jsonb doc claims.
- **Deferred:** none.
- **Rejected:** every other finding, each with its reason in the Review Triage Log.

**Follow-up review recommendation:** false. Patched at entry verdict: 0 high, 1 medium, 4 low.

**Verification**
- `dotnet build ActionLedger.sln --no-incremental`: 0 warnings, 0 errors.
- `dotnet test ActionLedger.sln`: 1501 total, 1501 succeeded, 0 failed, 0 skipped.
- `dotnet dotnet-ef migrations has-pending-model-changes`: no changes.
- `git diff --quiet -- src/ActionLedger.Web/openapi.json`: unchanged.
- Matrix audit: every I/O row has a passing test in `OutboxPersistenceTests`: two matching subscriptions, edit, reject, inactive or non-matching, no subscriptions, unassigned owner, forced failure, and suppressed.

**Residual risks**
- Every approval runs one payload read even when no subscription matches, because the contract fixes the build-then-lookup order.
- The builder throws if a run or meeting is created and decided in the same unsuppressed save. No path does that today.
- The suppression scan works line by line, so an argument name split from its colon across lines, or a positional `(ct, true)` call inside Infrastructure, would escape it. The overload is internal to Infrastructure.
- No subscription exists in the running app until 5.2, so approvals in compose write no outbox rows yet.
