---
review: rubric-walker
target: ARCHITECTURE-SPINE.md
date: 2026-09-20
rubric: references/reviewer-gate.md "Good-spine checklist"
scope-note: version currency skipped (separate reviewer); stack choices treated as locked by the seed
---

# Rubric review: ActionLedger architecture spine

## Verdict

Not ready to finalize: the 19 ADs are sound where they exist and the capability map covers every FR, but one AD's Rule is unenforceable as written and six cross-unit divergence points (transaction boundary, revision ownership, seeding, normalization placement, request timeouts, operational envelope) are silent.

## Findings

### Critical

**C-1. AD-13's Rule is not enforceable as written, and the openapi.json hand-off between api and web is undecided.**
Location: AD-13 Rule ("The Api emits `openapi.json` at build; the web build generates its typed client from it and fails on a contract change").
Problem: a client regenerated on every build cannot "fail on a contract change"; it simply regenerates. Only a committed artifact can fail a build when stale. The spine also does not say where `openapi.json` lives or how the `web` image in compose gets it. FR-36 requires `docker compose up` from a clean clone with no host .NET or Node; if the web Dockerfile builds from `web/` and the file is produced by the api build, the web image cannot build until the api has been built and the file copied across. Two units (api, web) and the CI author will each invent a different answer.
Fix: rewrite the Rule to one of two enforceable shapes and name the file's path. Recommended: `openapi.json` is committed at `web/actionledger-web/openapi.json`; a `dotnet` build step regenerates it into that path; the generated client is regenerated in the web build (never committed); `ci.yml` fails if `git diff --exit-code` shows the committed `openapi.json` is stale after the api build. That makes "contract change breaks the web build" literally true (the web compiles against the new client and TypeScript errors surface) and lets compose build `web` from a clean clone. Add a Conventions row "API contract file" with the path and the regen command.

### High

**H-1. The unit-of-work and transaction boundary never became an AD.**
Location: memlog decision "unit of work = DbContext.SaveChanges via IUnitOfWork; outbox rows written in the same SaveChanges"; spine only lists `IUnitOfWork` as a port name in the paradigm table.
Problem: AD-7 (revisions) and AD-8 (outbox rows) both depend on exactly one commit per use case, and the Decide command writes two aggregates (`ExtractionRun`/`ProposedAction` and the new `TrackedAction`) plus revisions plus outbox rows. Nothing says a repository never calls `SaveChanges`, that a handler calls `IUnitOfWork.CommitAsync` exactly once, or that a handler may touch more than one aggregate in one transaction. The review unit, the extraction unit, and the actions unit could each choose differently, and NFR-3's guarantee silently depends on the answer.
Fix: add AD-20 "One commit per use case": Binds Application, Infrastructure. Prevents partial writes across aggregate, revision, and outbox. Rule: repositories only add, load, and query; only a handler calls `IUnitOfWork.CommitAsync`, exactly once, at the end; `IUnitOfWork` is `AppDbContext.SaveChangesAsync`; a handler may modify several aggregates in that one commit; the outbox conversion in AD-8 runs inside it.

**H-2. AD-3 and AD-7 disagree on who owns `ActionRevision`, and the repository set is undefined.**
Location: AD-3 Rule ("`TrackedAction` (owns `ActionRevision` list)") versus AD-7 Rule (revisions created inside `ExtractionRun.AddProposals` and `ProposedAction.Decide`, targeting a `ProposedAction`).
Problem: an AiProposal revision is written before any `TrackedAction` exists, so it cannot live in a `TrackedAction`'s list. FR-21 also describes the audit trail as one ordered query over revisions by target, which implies a standalone table, not an owned collection. Related: AD-3 puts `ProposedAction` inside the `ExtractionRun` aggregate, but the ARCHITECTURE.md sequence loads a `ProposedAction` directly; the spine never says repositories are one per aggregate root, so builders will add ad-hoc repositories for child entities.
Fix: pick one model and state it in AD-3 and AD-7. Recommended: `ActionRevision` is a standalone append-only entity; aggregate methods return or raise the revisions they produce (for example `Decide` returns a `DecisionResult` carrying the `TrackedAction` and its revisions, or the aggregate exposes a pending-revisions list the handler drains into `IActionRevisionRepository.AddRange`). Remove "owns `ActionRevision` list" from AD-3. Add to AD-3: repositories exist only for aggregate roots (`IMeetingRepository`, `IExtractionRunRepository`, `ITrackedActionRepository`, `IUserRepository`, `IWebhookSubscriptionRepository`, `IOutboxRepository`) plus `IActionRevisionRepository` (append and ordered read only); the Decide handler loads the `ExtractionRun` and calls `run.Decide(proposalId, ...)`.

**H-3. Seeding is undecided on every axis two units could diverge on.**
Location: Capability map row FR-34..FR-36 (governed by AD-16, AD-17), neither of which mentions seed; seed tree `Infrastructure/Seed/DemoDataSeeder`.
Problem: the spine does not say (a) where the seeder runs: inside the `migrate` one-shot or in the api at startup; (b) which actor it uses, given AD-12 says handlers take the actor from `ICurrentUser` read from a JWT that does not exist at seed time, while FR-25 requires the "Seed" user id on every seeded write; (c) whether seeded rows go through aggregates (AD-3 says they must, and AD-7 says revisions are written only inside aggregate methods) or are written directly. FR-35 requires an Outbox Message in Dead state with recorded attempts and revision histories with specific past timestamps, which no aggregate path can produce. (d) what makes it idempotent, and (e) whether it runs in the Azure deploy.
Fix: add AD-21 "Demo seed": Binds Infrastructure/Seed, compose, CD. Prevents seeded data that bypasses the audit and outbox invariants in one place and follows them in another; the dispatcher delivering seeded outbox rows. Rule: the seeder runs as a step of the `migrate` container after the bundle (never in the api process); it constructs aggregates through their public methods with a `SeedCurrentUser` (the "Seed" user) and a `FixedClock` so revisions and timestamps come from the same code paths as live writes; the only direct writes it may make are `OutboxMessage` rows in `Delivered` and `Dead` state, and it marks those rows so `ClaimBatchAsync` never picks them up (state alone suffices, since only `Pending` is claimed); idempotency is keyed on fixed UUIDs for every seeded row; CD runs the same step. State whether the Azure environment is seeded.

**H-4. Excerpt verification and the FR-38 normalization have no fixed home, and three units consume them.**
Location: AD-11 Rule ("returning `ExtractionResult` (validated proposals, metrics, warnings)"); AD-19; ARCHITECTURE.md §4 puts the filter inside `ChatClientActionExtractor`.
Problem: FR-5 defines excerpt verification as a separate filter after schema validation using the FR-38 normalization (lowercase, strip punctuation, collapse whitespace), and requires the eval scorer to apply the same filter. FR-42 (if built) reuses the same matching rule. If the normalizer lives in Infrastructure, `tests/Eval` and Application cannot share it without a second copy. "Validated proposals" does not say whether the filter has already run.
Fix: in AD-11, state that `Application/Ai/TextNormalizer` (lowercase, strip punctuation, collapse whitespace) and `Application/Ai/ExcerptVerifier` are the single implementations; `ChatClientActionExtractor` applies the verifier after schema validation and records the dropped text as a warning; `ExtractionResult.Proposals` is post-filter; the scorer in AD-19 references Application and uses the same two classes. Add the normalizer to the seed tree under `Application/Ai/`.

**H-5. Synchronous extraction can run up to 180 seconds, and nothing pins the proxy and ingress timeouts to match.**
Location: Deferred ("synchronous request with a 90-second per-call timeout"); AD-17 (`web` nginx proxies `/api`); NFR-1.
Problem: with retry-once, a run can take 180 seconds before it fails. nginx's default `proxy_read_timeout` is 60 seconds, so with LM Studio on a slow model the browser gets a 504 while the api keeps running and later stores a run the UI never saw. The web unit (nginx config), the extraction unit (HttpClient timeout), and the CD unit (Azure Container Apps ingress) will each pick a number independently. No config key exists for the per-call timeout.
Fix: add a Conventions row "Timeouts": provider call `Ai:CallTimeoutSeconds` default 90; run ceiling 180; nginx `proxy_read_timeout 200s` and `proxy_send_timeout 200s` on `location /api/`; Angular HTTP has no client-side timeout on the run endpoint; webhook POST 10 seconds (`Webhooks:TimeoutSeconds`); ACA ingress timeout left at its default (240s) and noted. Add `Ai:CallTimeoutSeconds` and `Webhooks:TimeoutSeconds` to the Config keys row.

**H-6. The operational envelope is only half decided.**
Location: AD-16, AD-17, Conventions "Logging", Deferred. ARCHITECTURE.md §8 decides some of this, but the spine is the binding document.
Problem: these dimensions are neither decided, deferred, nor open:
- Container registry (GHCR or ACR) and image tagging scheme (`sha` and `v*`).
- Azure database hosting (Azure Database for PostgreSQL Flexible Server, per ADR-007, or a container) and how the CD migration step reaches it.
- Health and readiness: FR-28 mentions a health route, compose `depends_on` needs it, and ACA probes need it; no path or content is fixed (`/health` liveness, `/health/ready` checking the database).
- Correlation id (NFR-4): source inside a request (`HttpContext.TraceIdentifier` or W3C `traceparent`) and outside one (the dispatcher, where the outbox message id or event id must serve).
- Secret source per environment: `.env` in compose is stated; in Azure, ACA secrets fed from GitHub environment secrets or Key Vault is not.
- Backup, restore, and migration rollback: silent.
- NFR-11's no-op deploy path when the Azure subscription is not provisioned: silent.
Fix: extend AD-17's Rule with registry, tagging, health paths, and the Azure database target; add a Conventions row "Correlation" (request: `traceparent`/`TraceIdentifier` echoed as `correlationId` in every log line; dispatcher: `OutboxMessage.Id`); extend AD-16 with the Azure secret source; add Deferred lines for backup/restore, migration rollback, and a staging environment, each with its revisit condition; add the NFR-11 no-op deploy path to AD-17 or to an Open Questions section.

### Medium

**M-1. `extraction-failed` ProblemDetails type contradicts the Errors convention.**
Location: AD-13 Rule (`type` in {..., `extraction-failed`}) versus Conventions "Errors" (`ExtractionFailedException` recorded on the run, not thrown to the client).
Problem: FR-4 and UJ-1 say a failed run is returned as a run with outcome Failed and a reason, which is a 201 with a `RunDto`, not an error. As written, the api unit may return a 4xx or 5xx ProblemDetails and the web unit may expect a `RunDto`.
Fix: either delete `extraction-failed` from the type list, or define it narrowly (the run could not start: provider not configured or unreachable at call time, returned 503) and state that a run that starts always returns 201 with `Outcome` in {Succeeded, Failed}.

**M-2. The domain event to outbox payload path leaves the payload's inputs undecided.**
Location: AD-8 Rule; FR-30 payload (addendum) needs `ownerName`, `meetingTitle`, `reviewDecision`.
Problem: `TrackedActionCreated` is raised in Domain, which does not know the owner's display name or the meeting title. Either the event carries a snapshot (then Decide must be handed those values) or the outbox pipeline in `SaveChangesAsync` loads them before commit. Also unstated: one event produces N outbox messages sharing one `EventId`, which is what `X-ActionLedger-Delivery` and receiver deduplication rely on.
Fix: state in AD-8 that the event carries ids only, the outbox pipeline builds the FR-30 payload from tracked entities and one extra query inside the same transaction, and every `OutboxMessage` from one event shares that event's `EventId` while having its own `Id`.

**M-3. Prompt files and the schema version have no fixed packaging or source.**
Location: AD-6 Rule (`/prompts/extract-actions.v<N>.md` read by `PromptCatalog`); memlog decision "embedded as content files copied to output" did not land.
Problem: the api container, `Infrastructure.Tests`, and `tests/Eval` each need the prompt files at runtime. Without a packaging rule, the Dockerfile author and the eval author will locate them differently. `SchemaVersion` (AD-6) has no defined source: the schema file in the addendum carries no version.
Fix: in AD-6, state that `ActionLedger.Infrastructure.csproj` links `../../prompts/*.md` as embedded resources so every consumer reads them from the assembly, and that `SchemaVersion` is the `$id` (or a top-level `version` const) in `extract-actions.schema.json`, read by the extractor and written to the eval report.

**M-4. Server-side derived flags are pinned for Overdue but not for low confidence.**
Location: AD-15 (Overdue computed once, exposed as `isOverdue`); FR-10 (low-confidence flag computed by the API against `Ai:LowConfidenceThreshold`).
Problem: AD-14 forbids HTTP in components but not derived computation, so the review web feature can compute the flag from `confidence` and the threshold it does not have. Two units could disagree on the boundary condition.
Fix: generalize AD-15's title to "Time, identity, and derived flags are server-side" and add `isLowConfidence` computed once in the proposals read model against `Ai:LowConfidenceThreshold`; the DTO carries the flag and the web never compares against a threshold.

**M-5. AD-18 omits Angular unit tests, and the capability map omits most NFRs.**
Location: AD-18 Rule; Capability map (rows for NFR-7, NFR-8, NFR-11 only).
Problem: NFR-8 requires Angular unit tests covering view-model logic; AD-18 lists seven projects and no web unit placement. NFR-1 to NFR-6, NFR-9, and NFR-10 do not appear in the map, so a reader cannot tell whether NFR-6 (accessibility, PR checklist) or NFR-9 (license review) are decided, deferred, or dropped.
Fix: add web unit tests to AD-18 (`web/actionledger-web/src/**/*.spec.ts`, run by `ng test` in `ci.yml`, covering container components and data services with the generated client mocked). Add capability map rows: NFR-1 (Timeouts convention), NFR-2 (Paging convention), NFR-3 (AD-8, AD-18), NFR-4 (Logging and Correlation conventions), NFR-5 (AD-12, AD-16, repository settings under NFR-11 row), NFR-6 (web `review` and `actions` features, PR template checklist), NFR-9 (PR template checklist), NFR-10 (AD-17).

**M-6. AD-17 mixes seed with its invariant, and the NFR-11 pipeline row points at an AD that says nothing about the pipeline.**
Location: AD-17; Capability map row "NFR-11 pipeline → .github/workflows → AD-17".
Problem: the container list in AD-17 duplicates the seed diagram; the actual invariant is "schema changes ship only through the migration bundle; the api never migrates at startup". Meanwhile `ci.yml` contents, branch protection, and the PR template are decided in ARCHITECTURE.md §8 and the PRD but nowhere in the spine, so the map row is a dangling pointer.
Fix: tighten AD-17's Rule to the migration invariant plus the two facts a builder cannot read off code (api starts only after `migrate` exits 0; same images deploy to ACA). Leave the service list to the seed diagram. Add a Conventions row "Pipeline": `ci.yml` on PR and `main` (restore, build, tests with Testcontainers, `Architecture.Tests`, `ng lint/test/build`, image build); `eval.yml` on `/prompts/**` and `src/ActionLedger.Infrastructure/Ai/**` and manual dispatch; `cd.yml` on `main` and `v*`; branch protection requires `ci.yml` and a linked issue. Point the NFR-11 row at that convention.

### Low

**L-1. ERD cardinalities contradict the rules in both the spine and ARCHITECTURE.md.**
Location: Structural Seed erDiagram; ARCHITECTURE.md §3.
`USER ||--o{ TRACKED_ACTION` says every Tracked Action has exactly one User, but AD-9 makes the owner nullable; `USER ||--o{ ACTION_REVISION` conflicts with AD-7's null actor for AiProposal; `MEETING ||--|| MEETING_NOTES` conflicts with FR-1 (a Meeting without notes is valid). Fix: `USER |o--o{ TRACKED_ACTION`, `USER |o--o{ ACTION_REVISION`, `MEETING ||--o| MEETING_NOTES` in both files.

**L-2. LM Studio host naming is inconsistent.**
Location: Structural Seed compose diagram (`LM Studio on host:1234`) versus ARCHITECTURE.md §1 and the PRD (`host.docker.internal:1234`). Fix: use `host.docker.internal:1234/v1` in the spine diagram; it is the value builders will put in `.env.example`.

**L-3. AD-10 omits the jsonb and array mappings that ADR-007's switch cost depends on.**
Location: AD-10 Rule (uuid, timestamptz, date only); ADR-007 step 3. Fix: add "small lists (`attendees`, `event_types`) map to `text[]`; `warnings` and `payload` map to `jsonb`; both behind EF value converters so ADR-007 step 3 is one file".

**L-4. Deferred claims FR-40 to FR-42 fit "without a new AD", which is unverified.**
Location: Deferred, last bullet. FR-40's "Extraction Run-style metadata record" forces a choice between adding a `Kind` to `ExtractionRun` and a new entity, which is exactly the kind of fork a spine exists to settle. Fix: reword to "FR-41 and FR-42 fit without a new AD; FR-40 needs a memlog decision on whether `ExtractionRun` gains a `Kind` before it is built".

**L-5. AD-14 is enforceable only by review; name the lint rule.**
Location: AD-14 Rule. Fix: add "enforced by an ESLint `no-restricted-imports` rule that forbids `@angular/common/http` outside `**/data/*.service.ts` and `core/`".

**L-6. FR-7's fail-fast for an unreachable LocalOpenAI endpoint is not covered by AD-16.**
Location: AD-16 (options validation covers missing credentials only). Fix: add one sentence: "a startup hosted service pings the configured provider (`GET {BaseUrl}/models` for LocalOpenAI) and fails the host when unreachable; `Fake` skips the check".

**L-7. Small gaps against the PRD text.**
- AD-19 output: FR-38 requires a markdown summary per run in addition to the JSON report; AD-19 names only the JSON. Add `reports/<same-stem>.md`.
- Capability map row FR-29..FR-33 lists no Application or Api location, unlike every other row. Add `Application/Webhooks` (subscription and outbox read queries, secret masked in the DTO) and `WebhookSubscriptionsController`, `OutboxMessagesController`.
- Swagger UI must be reachable through the `web` container in compose (FR-27); nginx proxies only `/api`. State that nginx also proxies `/swagger` and `/openapi`, or that Swagger is reached on the api port directly and DEMO.md says which.

**L-8. Wording.**
Seed tree `Common/ # DomainEvent, DomainRuleException, IClock-free helpers` is unclear; say `Common/ # DomainEvent base, DomainRuleException, AggregateRoot`.

## Rubric walk summary

| Checklist item | Result |
| --- | --- |
| Fixes the real divergence points for the feature level, misses none | Six missed: H-1 to H-6 |
| Every AD Rule enforceable and prevents its divergence | AD-13 not enforceable as written (C-1); AD-3/AD-7 internally inconsistent (H-2); AD-14 enforceable only by review (L-5); the rest hold |
| Nothing under Deferred lets two units diverge | The 90-second timeout line hides an un-pinned proxy timeout (H-5); the FR-40 claim is unverified (L-4); the rest are safe |
| Covers the spec's capabilities | FR-1 to FR-42 all mapped; NFR-1 to NFR-6, NFR-9, NFR-10 unmapped (M-5); FR-34 to FR-36 mapped to ADs that do not govern them (H-3) |
| Every owned dimension decided, deferred, or open | Domain, boundaries, state mutation, data ownership, AI seam, API contract, frontend, tests: decided. Transactions: silent (H-1). Operational envelope: half silent (H-6). No Open Questions section; NFR-11's no-op deploy is the one item that belongs there |
| Seed vs AD placement | AD-17 carries seed (M-6). No AD is pure rationale; the ADRs hold rationale correctly. Nothing in the seed needs promotion beyond the transaction and seeding rules above, which are absent rather than misplaced |
| Ratifies brownfield / inherits parent | Not applicable (greenfield, no parent spine) |
| Named tech current | Skipped per instruction |

## Mechanical notes

- **AD numbering:** AD-1 through AD-19, sequential, no gaps or duplicates. Every AD has Binds, Prevents, Rule. Seven are tagged `[ADOPTED]`; AD-3, AD-10, AD-11 to AD-19 are not, which matches the memlog (the seed locked layering and stack, not those rules). New ADs from this review would be AD-20 (transactions) and AD-21 (seed).
- **Spine to ADR cross-references:** ADR-001 to ADR-007 each carry a `spine:` field, and the mapping is correct in both directions (AD-4 to ADR-001, AD-5 to ADR-002, AD-6 to ADR-003, AD-7 to ADR-004, AD-8 to ADR-005, AD-9 to ADR-006, AD-10 to ADR-007). ADR-003 also cites AD-19, which exists. The `companions` frontmatter lists all seven ADR files and they all exist.
- **ARCHITECTURE.md to spine:** every AD cited in ARCHITECTURE.md (AD-1, 2, 3 to 10, 11, 12, 13, 14, 17) exists. ARCHITECTURE.md §4 places the excerpt filter in `ChatClientActionExtractor` and §5 states a 5-second poll and batch of 20, neither of which the spine states; after H-4 the filter placement will be in the spine, and the poll interval and batch size should either be named in AD-8 or left to code (they are seed, so leaving them is fine). ARCHITECTURE.md §8's environment matrix decides registry-free image push, the Azure database, and the no-op deploy only by implication; H-6 moves those decisions into the spine.
- **PRD to spine:** the frontmatter `binds` FR-1..FR-42, NFR-1..NFR-11, UJ-1..UJ-4; UJs are all realized by mapped FRs. PRD Open Question 1 (thresholds after baseline) is correctly left to the PRD addendum.
- **Mermaid:** all three spine blocks parse (flowchart TD, flowchart LR with a subgraph and a cylinder node, erDiagram). The dotted-with-label edge `-. text .->` is valid syntax. The erDiagram issues in L-1 are semantic, not syntactic.
- **Placeholders and template comments:** none found. `status: draft` is correct before finalize.
- **Memlog drift:** two memlog decisions did not land in the spine: the unit-of-work rule (H-1) and prompt-file packaging (M-3). The memlog also records "repositories per aggregate", which the spine reduced to a port name (H-2).
