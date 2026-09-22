---
title: 'Story 2.5 — Extraction Run and proposals persisted with AI Proposal revisions'
type: 'feature'
created: '2026-09-22'
baseline_revision: '3f0f9a7829aa19ca1e0e06e73fe1a209fbad134b'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-2-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-2-4-extraction-seam-output-validation-and-the-fake-provider.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-ActionLedger-2026-09-20/ARCHITECTURE-SPINE.md'
warnings: ['oversized']
deferred:
  - summary: >-
      `ExtractionResult.Failed` does not refuse a blank reason, and Story 2.5 is the first code to
      turn one into a 409 on the path AD-11 requires to answer 201.
    evidence: |-
      `src/ActionLedger.Application/Ai/ExtractionResult.cs:130` builds `Failed(reason, metrics)`
      with no guard on `reason`. `ExtractionRun.Start` refuses a Failed run whose reason is blank
      (`src/ActionLedger.Domain/Extraction/ExtractionRun.cs`, `RequireReasonMatchesOutcome`), so a
      blank reason becomes a `DomainRuleException` and the controller answers 409 instead of the
      201-with-Outcome-Failed that AD-11 fixes. Unreachable today: every reason
      `ChatClientActionExtractor` builds is a non-empty interpolation, and the Fake is the only
      registered provider. `ExtractionResult` is Story 2.4's file, so the missing guard predates
      this story; 2.5 is only the first consumer. What would settle it: when Story 2.7 wires a
      provider whose exception message can be empty, decide whether `Failed` rejects a blank
      reason or the aggregate substitutes a placeholder rather than throwing.
    location: >-
      src/ActionLedger.Application/Ai/ExtractionResult.cs:130
    severity: medium (unverified)
  - summary: >-
      `AiOptions`' two provider model names carry no length bound mirroring
      `ExtractionRunMetadata.ModelMaxLength`, so an over-long operator-supplied model turns every
      run into a 409 with no row.
    evidence: |-
      `AiOptions.LocalOpenAI.Model` and `AiOptions.AzureOpenAI.Model` are free strings with no
      `[StringLength]`, while `ExtractionRunMetadata.Validated()` throws `DomainRuleException` for a
      blank or over-200-character model — and `ApiExceptionHandler` maps that to 409, after the
      provider call, with no `ExtractionRun` persisted. That is the outcome AD-11 exists to
      prevent, and it would fail on every run rather than once. Unreachable today: the Fake is the
      only registered provider and supplies `fixture-catalog`, its own constant, so nothing
      operator-supplied reaches the guard. `AiOptions` is Story 2.4's file, so the missing bound
      predates this story; 2.5 is only the first code that turns it into a status code. What would
      settle it: when Story 2.7 wires LM Studio, Ollama and Azure OpenAI, decide whether the
      options bind with `[StringLength]` tied to the Domain constants and fail at startup (with the
      constant-agreement test `ProposedActionShapeTests` already models for the validator bounds),
      or whether the handler turns a metadata refusal into a persisted Failed run.
    location: >-
      src/ActionLedger.Api/Configuration/AiOptions.cs
    severity: medium
---

<intent-contract>

## Intent

**Problem:** Story 2.4 shipped an extraction seam that returns an `ExtractionResult` and throws it
away — there is no aggregate, no table, no endpoint, and no read model. Nothing can start a run,
nothing records that a run happened, and no proposal exists for Epic 3 to review. `IExtractionSettings`,
`OwnerResolver`, `ProposedActionReadModel`, `ExtractionRun`, `ProposedAction` and `ActionRevision`
exist only in the architecture documents.

**Approach:** Add the three Domain roots and the migration behind them, one `RunExtractionHandler`
that calls the seam and persists the whole run — Succeeded or Failed — in a single commit together
with one AiProposal revision per proposal, and one shared read model that serves Run Detail today
and the Review Screen in Epic 3. Two endpoints: `POST meetings/{id}/runs` (201 for both outcomes)
and `GET runs/{id}`.

## Boundaries & Constraints

**Always:**

- **A failed extraction is not an HTTP error** (spine Conventions/Errors `:190`, AD-11 `:120`). The
  handler persists the run either way and the controller answers `201 RunDto { id, outcome }`. The
  only 4xx on the POST are 400 (meeting has no notes), 401, and 404 (no such meeting).
- **One commit per use case** (AD-20 `:174`). `IUnitOfWork.CommitAsync` is called exactly once, at
  the end of `RunExtractionHandler`, and covers the run, its proposals, and its revisions.
- **Every AD-6 field is persisted and published, non-null where AD-6 says so** (`:90`, FR-6
  `prd.md:160-168`): `Provider`, `Model`, `PromptVersion`, `SchemaVersion`, `StartedAt`,
  `DurationMs`, `InputTokens`, `OutputTokens`, `Outcome`, `FailureReason` (null only when
  Succeeded), `Warnings` (empty, never null). Tokens are `int` and are `0` for Fake, never null.
- **`StartedAt` and `DurationMs` come from `ExtractionMetrics`, not from `IClock`.** The extractor
  is the only code that sees both ends of the provider call. `IClock.UtcNow` is read **once** per
  request and is used only as the AD-7 shared `now` stamped on every revision the aggregate mints.
- **Revisions are minted inside `ExtractionRun.AddProposals`** (AD-7 `:96`): kind `AiProposal`, one
  per proposal, `TargetType = ProposedAction`, `TargetId` the proposal's id, `Sequence` 1 (a brand
  new target has no prior revisions), `Field` null, `OldValue` null, `NewValue` the proposal as
  JSON carrying exactly description, suggestedOwner, suggestedDueDate, confidence and sourceExcerpt
  (FR-21 `prd.md:321`), `ActorUserId` **null** — the AI is not a User — and `OccurredAt` the one
  `now` the method was handed.
- **Proposals are stored and read in AI order.** `ProposedAction.Ordinal` is the zero-based index of
  the proposal in `ExtractionResult.Kept`, unique per run (AD-20 `:174`), and every read orders by
  it. `IReadDb` is untracked, so order is never implied by insertion.
- **`isLowConfidence` and `suggestedOwnerUserId` are computed once, server-side, in
  `ProposedActionReadModel`** (AD-9 `:108`, AD-15 `:144`). The threshold arrives through
  `IExtractionSettings`; Application never sees `IOptions`. Low confidence is
  `Confidence < LowConfidenceThreshold` — strictly below, so a proposal exactly at the threshold is
  not flagged.
- **`OwnerResolver.Match` is the only owner matcher**: case-insensitive equality on
  `User.DisplayName`, `null` when the suggestion is blank or matches no User or matches more than
  one. The proposal keeps its free-text suggestion regardless (FR-15 `prd.md:253`).
- **One `ExtractionRunCompleted` log event per completed run** (NFR-4 `prd.md:544`, spine `:193`)
  carrying provider, model, prompt version, duration, both token counts and outcome. The
  correlation id is ambient through Serilog's `LogContext` — never a parameter. **Never log notes
  text, excerpts, descriptions or secrets.**
- **The actor comes from `ICurrentUser`**, never from a request body; the POST has no body at all.
- Ids are UUIDv7 from `Guid.CreateVersion7()` in Domain constructors; instants are UTC
  `DateTimeOffset`; due dates are `DateOnly`; enums cross the wire as PascalCase strings.

**Never:**

- No decision endpoint, no review-state transition, no `TrackedAction`, no outbox row, no owner
  assignment — Epic 3 owns all of it. `ProposedAction` gets its `ReviewState` set to `Pending` at
  construction and nothing in this story changes it.
- No UI. `src/ActionLedger.Web/` changes only by the regenerated `openapi.json` and the client it
  produces. No `.razor` file is touched.
- No edit to `prompts/`, `fixtures/`, `.env.example`, `appsettings.json` or `docker-compose.yml` —
  every key this story needs is already bound and already placeholdered.
- No second `IActionExtractor`, no re-normalizer, no second excerpt verifier, no re-filtering of
  dropped proposals: dropped proposals are **not** persisted as rows; they survive only as the
  warnings the extractor already produced.
- No retry, no queueing, no background work: extraction is synchronous inside the request
  (FR-4 `prd.md:147`).
- No EF Core, ASP.NET Core, Npgsql or AI package reference in Domain or Application.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Succeeded run | Meeting with notes, Fake provider, catalog hit | 201 `RunDto { id, outcome: "Succeeded" }`; one run row, N proposal rows ordinals 0..N-1, N revision rows | No error expected |
| Failed run | Extractor returns `Failed(reason, metrics)` | 201 `RunDto { id, outcome: "Failed" }`; run row with `FailureReason` verbatim, zero proposals, zero revisions | No error expected |
| Run with dropped proposals | Result carries `Kept` and `Dropped` | 201; only `Kept` become rows; `Warnings` persisted one per drop | No error expected |
| Meeting has no notes | Meeting exists, `Notes` is null | 400 ProblemDetails `type: "validation"` | `ValidationFailedException` → 400 |
| Unknown meeting | id matches nothing | 404 ProblemDetails `type: "not-found"` | `NotFoundException` |
| Second run on same meeting | Meeting already has a run | 201; a new run with its own proposals; the earlier run and its proposals are untouched | No error expected |
| Anonymous POST or GET | No bearer token | 401 ProblemDetails `type: "unauthorized"` | Authorization runs before any state check |
| `GET runs/{id}` unknown id | id matches nothing | 404 ProblemDetails `type: "not-found"` | `NotFoundException` |
| Owner matches a User | `suggestedOwner: "Dana Whitfield"`, User display name `"dana whitfield"` | `suggestedOwnerUserId` is that User's id | No error expected |
| Owner matches nobody / is blank | `suggestedOwner: "Facilities"` or `""` | `suggestedOwnerUserId` is null; `suggestedOwner` still returned verbatim | No error expected |
| Confidence at the threshold | `confidence: 0.70`, threshold `0.70` | `isLowConfidence: false` | No error expected |

</intent-contract>

## Code Map

Read these before writing anything.

**The decisions this story implements (cite them in doc comments):**

- `_bmad-output/planning-artifacts/architecture/architecture-ActionLedger-2026-09-20/ARCHITECTURE-SPINE.md:68-72`
  (AD-3) — `ExtractionRun` owns the `ProposedAction` list and is created by `ExtractionRun.Start(...)`;
  `ActionRevision` is its own root; `IExtractionRunRepository` and `IActionRevisionRepository`
  (append and ordered read only) load, add and query but **never save**; setters are private.
- `…:74-78` (AD-4) — `ProposedAction` is immutable after creation except Review State and the
  decision fields Epic 3 adds.
- `…:80-84` (AD-5) — the run stores `MeetingNotesId` and a SHA-256 of the notes text it read; any
  number of runs per Meeting.
- `…:86-90` (AD-6) — the eleven required run fields, verbatim; `SchemaVersion` is the schema file's
  own top-level `version`, already exposed as `ExtractionSchema.Version`.
- `…:92-96` (AD-7) — the revision root, its ten columns, "created only inside
  `ExtractionRun.AddProposals`", and **"Each method receives `now` once and stamps every revision it
  produces with that same instant."**
- `…:104-108` (AD-9) — `OwnerResolver.Match` is a pure function in `Application/Review`, called from
  `ProposedActionReadModel`, "the single query class that produces `ProposedActionDto` for both Run
  Detail and the Review Screen".
- `…:116-120` (AD-11) — "`RunExtractionHandler` always persists the `ExtractionRun` (Succeeded or
  Failed) and returns `RunDto`; the controller returns 201 in both cases."
- `…:140-144` (AD-15) — one `IClock.UtcNow` per request; low confidence computed once in the read
  model; "the web never computes either".
- `…:146-150` (AD-16) — "Application reads settings only through ports (`IExtractionSettings`,
  `IPromptCatalog`), implemented in Infrastructure over the options."
- `…:170-174` (AD-20) — one commit, and the unique index `proposed_action(extraction_run_id, ordinal)`.
- `…:190` — "A failed extraction is not an error: it is a 201 `RunDto` with `Outcome = Failed`."
- `…:193` — one `ExtractionRunCompleted` event per NFR-4; never log notes text or secrets.
- `…:199` — the non-unique index `action_revision(target_type, target_id, sequence)`.
- `_bmad-output/planning-artifacts/epics.md:455-473` — Story 2.5's two acceptance criteria verbatim.
- `_bmad-output/planning-artifacts/prds/prd-ActionLedger-2026-09-19/prd.md:138-147` (FR-4),
  `:160-168` (FR-6), `:189-195` (FR-9), `:201-208` (FR-10), `:237-244` (FR-14), `:246-253` (FR-15),
  `:314-321` (FR-21), `:544` (NFR-4). `:69` fixes the four Review States: **Pending, Approved,
  Edited, Rejected**.
- `…/architecture/…/ARCHITECTURE.md:185-216` — the extraction sequence diagram; `:199-214` is this
  handler's exact shape, including `ExtractionRun.Start(...).AddProposals(...)` *after* the
  extractor returns.

**The seam this story consumes (read-only — Story 2.4 owns every byte):**

- `src/ActionLedger.Application/Abstractions/IActionExtractor.cs:22-45` —
  `Task<ExtractionResult> ExtractAsync(ExtractionRequest, CancellationToken = default)`. It **never
  throws** for a provider or validation failure; only caller cancellation escapes. Its doc already
  names this story.
- `src/ActionLedger.Application/Ai/ExtractionRequest.cs:14` —
  `sealed record ExtractionRequest(string Notes, DateOnly MeetingDate)`. Nothing else about the
  Meeting is sent (FR-4 `prd.md:146`).
- `src/ActionLedger.Application/Ai/ExtractionResult.cs:22-30` — `ExtractionMetrics(string Provider,
  string Model, string PromptVersion, string SchemaVersion, DateTimeOffset StartedAt, int DurationMs,
  int InputTokens, int OutputTokens)`. `:42-47` — `ExtractedProposal(string Description, string
  SuggestedOwner, DateOnly? SuggestedDueDate, double Confidence, string SourceExcerpt)`;
  `SuggestedOwner` is `""`, never null. `:59` — `DroppedProposal(ExtractedProposal, string Warning)`.
  `:96-111` — `IsSucceeded`, `Kept`, `Dropped`, `Warnings`, `FailureReason`, `Metrics`.
- `src/ActionLedger.Application/Ai/ExtractionOutputValidator.cs:29-50` — the length and range
  constants (`DescriptionMaxLength = 500`, `SuggestedOwnerMaxLength = 100`,
  `SourceExcerptMaxLength = 1000`). **Reuse these for the column lengths** rather than retyping
  numbers, so a validator change cannot silently outgrow a column.
- `src/ActionLedger.Api/Configuration/AiOptions.cs:36-37` —
  `[Range(0.0, 1.0)] public double LowConfidenceThreshold { get; init; } = 0.70;`. Bound and
  validated already; **no production code reads it yet**.
- `src/ActionLedger.Infrastructure/Ai/AiSettings.cs:33` —
  `sealed record AiSettings(string Provider, string? PromptVersion, int CallTimeoutSeconds)`; this
  story adds the threshold to it.

**Where the new code goes, and the shapes it must copy:**

- `src/ActionLedger.Domain/Common/AggregateRoot.cs:20-35` — `Id = Guid.CreateVersion7()` in the
  protected ctor, `Guid Id { get; private init; }`, the `private readonly List<T> _x = []` +
  `IReadOnlyList<T>` pair. Every new root derives from this.
- `src/ActionLedger.Domain/Meetings/Meeting.cs:44-49, :91-102, :114-125, :127-149` — the whole house
  style in one file: the `/// <summary>Rehydration constructor for EF Core.</summary>` private ctor,
  the expression-bodied static factory, the `private static RequireX` ternary guards throwing
  `DomainRuleException` with sentence-case messages, the `public const int XMaxLength` block, and
  `AttachNotes` — the precedent for a parent method that mints a child and returns it.
- `src/ActionLedger.Domain/Meetings/MeetingNotes.cs:34-48, :63-78` — the child-entity shape: sealed,
  **not** an `AggregateRoot`, an `internal` constructor so only the aggregate can mint one, its own
  UUIDv7, an explicit parent FK property, all setters private. `:80-81` is the repo's one SHA-256
  idiom. `MeetingNotes.Sha256` is the hash of the **raw** notes text and is exactly what AD-5 wants
  the run to copy — do not re-hash.
- `src/ActionLedger.Domain/Users/Role.cs:7` — the enum doc convention ("Stored as a string and
  serialized as a PascalCase string").
- `src/ActionLedger.Application/Meetings/SaveMeetingNotesHandler.cs:24-49` — the handler skeleton:
  primary-constructor injection, `HandleAsync(... , CancellationToken cancellationToken = default)`,
  `?? throw new NotFoundException("Meeting", id)`, exactly one `await unitOfWork.CommitAsync(...)`
  at the end, a DTO returned.
- `src/ActionLedger.Application/Meetings/MeetingsQueries.cs:10, :27-64, :77-94` — the query-class
  shape: `sealed class XQueries(IReadDb readDb)`, projection inside `Select`, total ordering,
  `found.Count == 0 ? throw new NotFoundException(...) : found[0]`. `:53-60` is the literal `0`
  run count this story replaces, and the comment saying so.
- `src/ActionLedger.Application/Meetings/MeetingDtos.cs:1-23` — DTO conventions: `public sealed
  record`, positional, one `<param>` doc per member, `IReadOnlyList<T>` for collections.
  `MeetingSummaryDto.RunCount` is documented "Always `0` until Story 2.5".
- `src/ActionLedger.Application/ApplicationRegistration.cs:20-31` — every handler and query is
  registered here and nowhere else.
- `src/ActionLedger.Application/ActionLedger.Application.csproj:12-15` — the one `PackageReference`
  item group. `Microsoft.Extensions.Logging.Abstractions` is on AD-1's allowlist
  (`tests/Architecture.Tests/DependencyRuleTests.cs:44-49`) but is **not yet referenced**; this
  story adds it, and `Directory.Packages.props` needs the matching `PackageVersion`.
- `src/ActionLedger.Infrastructure/Persistence/Configurations/MeetingConfiguration.cs:21-34, :39-68,
  :77-87` — the configuration template: `internal sealed class XConfiguration :
  IEntityTypeConfiguration<X>` (discovered by assembly scan, no registration line), `public const
  string XIndexName` so tests can name the index, `builder.Ignore(x => x.DomainEvents)`, and
  **`public const string ConcurrencyTokenProperty = "xmin";` + `builder.Property<uint>("xmin")
  .IsRowVersion();`** — the whole `xmin` idiom, with the comment explaining that no physical column
  is created. `:51-52` shows a `IReadOnlyList<string>` property mapped with nothing but
  `.IsRequired()` (Npgsql gives it `text[]`) — the precedent for `Warnings`.
- `src/ActionLedger.Infrastructure/Persistence/ModelConventions.cs:38-64, :108-182` — what is
  automatic: snake_case tables (from the **`DbSet` property name**), columns, keys, FK and index
  names; enums to strings; Guid keys `ValueGeneratedNever`; `DateTimeOffset` → `timestamptz`;
  `DateOnly` → `date`. Do not restate any of it by hand.
- `src/ActionLedger.Infrastructure/Persistence/AppDbContext.cs:12-15, :26-30, :38-48` — the `DbSet`
  block (one per root; owned children get none), and the `SaveChangesAsync` override whose
  exception filter runs `ConcurrencyTranslation.Translate`.
- `src/ActionLedger.Infrastructure/Persistence/MeetingRepository.cs:12-23` — repository shape:
  `internal sealed class XRepository(AppDbContext context) : IXRepository`, `Add` plus a find; no
  `Update`, no `Attach`, no save.
- `src/ActionLedger.Infrastructure/InfrastructureRegistration.cs:49-54` — the four `AddScoped` lines
  the new repositories join. `:102-130` (`AddActionLedgerAi`) is where `IExtractionSettings`'
  implementation is registered.
- `src/ActionLedger.Api/Program.cs:49-54` — the `AiSettings` hand-off that gains the threshold.
  `:79-81` registers `JsonStringEnumConverter`, so new enums publish as PascalCase strings.
- `src/ActionLedger.Api/Controllers/MeetingsController.cs:25-32, :44-63, :78-95` — the controller
  contract: `[ApiController]`, `[Route("meetings")]` **without** `api/v1`, bare `[Authorize]`,
  `[Tags]`, and per action `[EndpointName]`/`[EndpointSummary]`/`[EndpointDescription]`, one
  `[ProducesResponseType<T>]` per status with `"application/problem+json"` on the error arms,
  `<response code="…">` XML docs, and `CreatedAtAction(nameof(Get), new { id }, dto)`.
- `src/ActionLedger.Api/Errors/ProblemDetailsMapping.cs:116-121, :146-165` — the five `ProblemTypes`
  slugs and `ApiExceptionHandler`'s switch over "the three exceptions that cross into the Api ring".
  A fourth arm is added here for the 400.

**Read-only evidence — rules and tests the new code will trip:**

- `tests/Api.Tests/NotesImmutabilityTests.cs:64-78` —
  `The_contract_publishes_exactly_the_four_meeting_operations_this_story_adds` asserts the **exact**
  set of meeting operations. Adding `post /api/v1/meetings/{id}/runs` reddens it; update the list and
  the test name. `:28-43` also forbids any `delete`/`patch` under the meetings prefix — the new POST
  is fine.
- `tests/Api.Tests/MeetingsEndpointTests.cs:35-58` — a second exact route-set assertion that must
  gain the new POST. `:151-162` pins the "every role is a writer" premise behind bare `[Authorize]`.
- `tests/Api.Tests/RouteDisciplineTests.cs:75-87` — every mapped endpoint must start `/api/v1/`; a
  hand-written prefix double-prefixes and fails.
- `tests/Api.Tests/AuthDisciplineTests.cs:29-61, :152-154` — walks every operation in the generated
  contract anonymously and requires 401. Both new endpoints are walked automatically, so
  authorization must precede any state check.
- `tests/Api.Tests/OpenApiSnapshotTest.cs:16-42` — byte-compares `src/ActionLedger.Web/openapi.json`
  against a fresh generation. Regenerate with
  `dotnet run --project src/ActionLedger.Api -- --export-openapi` and commit the result.
- `tests/Api.Tests/TestApi.cs:36-48, :67, :73-102` — `WebApplicationFactory<Program>` with **no
  database**: `ValidConfiguration()` supplies `Ai:*`/`Jwt:*`, `ReplaceServices` swaps persistence
  for in-memory fakes, `TokenFor`/`CreateClientAs` mint a role token.
  `tests/Api.Tests/MeetingsSuccessResponseTests.cs:316-355` is the fake-store precedent, and `:32-37`
  is the test that actually **follows** the `Location` header.
- `tests/Architecture.Tests/ActorIntegrityTests.cs:18-21, :30-41, :67-110` — the banned actor member
  names on any `*Handler.HandleAsync` parameter type and any `*Queries` parameter. A *target* user
  (`OwnerUserId`, `SuggestedOwnerUserId`) is explicitly legal; a bare `UserId` is not.
- `tests/Architecture.Tests/DependencyRuleTests.cs:44-49, :52-59, :86-105, :119-149, :181-196` —
  Application's three-package allowlist, the five banned namespace roots, and Rule 5's `Normaliz`
  name rule across all four assemblies.
- `tests/Architecture.Tests/AiSeamTests.cs:44-64` — exactly one `IActionExtractor` implementation,
  and no Application type may implement it.
- `tests/Infrastructure.Tests/MeetingPersistenceTests.cs:30-50, :52-85, :106-133, :135-168, :170-195`
  — the persistence-test template: exact `information_schema` column set and types, the proof that
  `xmin` is a system column (absent from `information_schema`, `IsConcurrencyToken` true,
  `ValueGenerated.OnAddOrUpdate`), a `[Theory]` asserting `UNIQUE` in `pg_indexes.indexdef`, an
  inserts-only assertion via `RecordingCommandInterceptor`, and UUIDv7 version-nibble checks.
- `tests/Infrastructure.Tests/PostgresFixture.cs:15-58` — Testcontainers `postgres:18-alpine`, opted
  into with `[Collection(PostgresFixture.CollectionName)]`. **There is no skip-without-Docker path.**
- `tests/Application.Tests/Meetings/MeetingsTests.cs:298-376` — hand-written `private sealed class
  Fake…` under a `// --- Fakes ---` banner; **no mocking library exists in this repo**. Every
  `await` passes `TestContext.Current.CancellationToken`. `The_counts_are_published_as_zero_until_runs_and_tracked_actions_exist`
  and `tests/Api.Tests/MeetingsSuccessResponseTests.cs:217` both assert `runCount == 0` and must be
  updated.
- `tests/Domain.Tests/Meetings/MeetingTests.cs:14-20, :156-190` — the Domain test conventions
  (sentence-case names, `private static readonly` arrange data, `[Theory]` tables) and the
  reflection-based "shape claim" test worth copying for `ProposedAction`'s immutability.
- `Directory.Build.props:5-13` — `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`,
  `Nullable=enable`. Any warning fails the build.
- `Directory.Packages.props:3-7` — the licence gate; add `PackageVersion` entries in a labelled
  `ItemGroup` matching the file's house style.
- `.config/dotnet-tools.json` — `dotnet-ef` is a local tool pinned to 10.0.12; run `dotnet tool
  restore` and invoke it as `dotnet dotnet-ef`. There is no design-time factory, so the migration
  command needs `Database__ConnectionString` and the other `ValidateOnStart` keys in the environment.
- `tests/Architecture.Tests/ComposeTopologyTests.cs:45-64, :105-108` — the closed config-key list
  already contains `Ai__LowConfidenceThreshold`; `.env.example` and `appsettings.json` need **no**
  edit.

**Tooling (verified on this machine at `3f0f9a7`):**

- `dotnet` 10.0.401 at `~/.dotnet/dotnet`, matching `global.json`.
- Baseline at `3f0f9a7`: working tree clean, branch `main`. `dotnet test ActionLedger.sln` recorded
  **969 passed, 0 failed, 0 skipped** across eight assemblies at the end of Story 2.4. That is the
  number to beat.

## Tasks & Acceptance

**Execution:**

- `Directory.Packages.props` — add a `PackageVersion` for `Microsoft.Extensions.Logging.Abstractions`
  in a labelled `ItemGroup` naming this story — the handler's `ExtractionRunCompleted` event needs
  `ILogger<T>` in Application, which AD-1's allowlist already permits but the repo has never used.
  Add nothing else.
- `src/ActionLedger.Domain/Extraction/ExtractionOutcome.cs` — `public enum ExtractionOutcome
  { Succeeded, Failed }` with the Role.cs doc convention — AD-6 names `Outcome`; it is stored and
  published as a PascalCase string.
- `src/ActionLedger.Domain/Extraction/ReviewState.cs` — `public enum ReviewState { Pending, Approved,
  Edited, Rejected }` — the four states `prd.md:69` fixes. Only `Pending` is reachable in this story;
  the doc comment must say Story 3.2 writes the rest, so the published enum never has to move.
- `src/ActionLedger.Domain/Extraction/ExtractionRunMetadata.cs` — `public sealed record
  ExtractionRunMetadata(string Provider, string Model, string PromptVersion, string SchemaVersion,
  DateTimeOffset StartedAt, int DurationMs, int InputTokens, int OutputTokens)` with guards for blank
  strings and negative numbers — Domain cannot see `ExtractionMetrics` (AD-1), so it needs its own
  value type; the Application layer maps one to the other in one place.
- `src/ActionLedger.Domain/Extraction/ProposedActionDraft.cs` — `public sealed record
  ProposedActionDraft(string Description, string SuggestedOwner, DateOnly? SuggestedDueDate,
  double Confidence, string SourceExcerpt)` — the aggregate's input shape, mirroring
  `ExtractedProposal` without crossing the ring boundary.
- `src/ActionLedger.Domain/Extraction/ProposedAction.cs` — the child entity: sealed, **not** an
  `AggregateRoot`, `internal` constructor, own UUIDv7, `ExtractionRunId`, `Ordinal`, the five
  proposal members, `ReviewState = Pending`, all setters private, plus the EF rehydration ctor and
  `public const int` lengths taken from `ExtractionOutputValidator`'s constants — AD-4 makes it
  immutable except Review State, and `MeetingNotes.cs:34-48` is the shape to copy.
- `src/ActionLedger.Domain/Actions/ActionRevision.cs` — the append-only root with AD-7's ten columns
  (`Id`, `TargetType`, `TargetId`, `Sequence`, `Kind`, `Field`, `OldValue`, `NewValue`,
  `ActorUserId`, `OccurredAt`), an `internal` constructor so only an aggregate can mint one, and no
  mutator of any kind. Add `RevisionTargetType { ProposedAction }` and `RevisionKind { AiProposal }`
  in the same folder, each documented as gaining members with the stories that write them — AD-7
  gives revisions their own root with no navigation from any aggregate.
- `src/ActionLedger.Domain/Extraction/ExtractionRun.cs` — the aggregate root.
  `public static ExtractionRun Start(Guid meetingId, Guid meetingNotesId, string notesSha256,
  Guid startedByUserId, ExtractionRunMetadata metadata, ExtractionOutcome outcome,
  string? failureReason, IReadOnlyList<string>? warnings)` guards each argument and refuses a
  `Failed` run with no reason and a `Succeeded` run that carries one.
  `public IReadOnlyList<ActionRevision> AddProposals(IReadOnlyList<ProposedActionDraft> drafts,
  DateTimeOffset now)` assigns zero-based ordinals in list order, mints one `ProposedAction` and one
  AiProposal `ActionRevision` per draft (sequence 1, actor null, `OccurredAt = now` for all of them),
  returns the revisions, and refuses a second call or any call on a `Failed` run — AD-3 names both
  methods, AD-7 requires the one shared instant, and returning the revisions is how the handler
  hands them to a repository the aggregate must not know about.
- `src/ActionLedger.Domain/Extraction/ExtractionRun.cs` (same file) — a `private static string
  ToJson(ProposedActionDraft)` using a cached `JsonSerializerOptions` (camelCase, no indentation,
  `DateOnly` as `yyyy-MM-dd`) emitting exactly `description`, `suggestedOwner`, `suggestedDueDate`,
  `confidence`, `sourceExcerpt` — FR-21 `prd.md:321` puts those five fields and nothing else in the
  new-value JSON, and `System.Text.Json` is in the shared framework so Domain's zero-reference rule
  (`DependencyRuleTests.cs:66`) still holds.
- `src/ActionLedger.Application/Abstractions/IExtractionRunRepository.cs` — `void Add(ExtractionRun
  run)` and `Task<ExtractionRun?> FindByIdAsync(Guid id, CancellationToken = default)` — every port
  lives in `Abstractions/`; AD-3 says repositories never save.
- `src/ActionLedger.Application/Abstractions/IActionRevisionRepository.cs` — `void
  AddRange(IReadOnlyList<ActionRevision> revisions)` and one ordered read
  (`Task<IReadOnlyList<ActionRevision>> ListForTargetAsync(RevisionTargetType targetType,
  Guid targetId, CancellationToken = default)` ordered by `OccurredAt` then `Sequence`) — AD-3 says
  "append and ordered read only"; no update, no delete, ever.
- `src/ActionLedger.Application/Abstractions/IExtractionSettings.cs` — `double
  LowConfidenceThreshold { get; }` — AD-16 says Application reads settings only through ports.
  Use `double`, matching `AiOptions.LowConfidenceThreshold` and `ExtractedProposal.Confidence`; the
  adversary review's `decimal` would force a lossy cast on every comparison. The doc comment must
  record that choice and why. The port carries only the threshold: the prompt version already
  reaches Application on `ExtractionMetrics`.
- `src/ActionLedger.Application/Abstractions/ValidationFailedException.cs` — a sealed exception for
  a request that is well-formed but cannot proceed against current state, mirroring
  `NotFoundException`'s shape — "run a meeting with no notes" is a 400 (FR-4 `prd.md:143`), and
  `ApiExceptionHandler` has no 400 arm today.
- `src/ActionLedger.Application/Extraction/RunExtractionHandler.cs` — the one write use case:
  load the Meeting, 404 if absent, `ValidationFailedException` if `Notes` is null, read
  `IClock.UtcNow` **once**, call `IActionExtractor.ExtractAsync(new ExtractionRequest(notes.Text,
  meeting.MeetingDate), ct)`, map `ExtractionMetrics` → `ExtractionRunMetadata`,
  `ExtractionRun.Start(...)` with the outcome the result carries, `AddProposals(...)` only when it
  succeeded and kept anything, `runs.Add(...)`, `revisions.AddRange(...)`, log
  `ExtractionRunCompleted`, then exactly one `unitOfWork.CommitAsync(ct)`, and return
  `RunDto`. The actor is `ICurrentUser.UserId`; the method takes `(Guid meetingId,
  CancellationToken)` and no command type — AD-11 and AD-20, and `ActorIntegrityTests` bans an actor
  on a command anyway.
- `src/ActionLedger.Application/Extraction/RunDtos.cs` — `RunDto(Guid Id, ExtractionOutcome Outcome)`
  and `RunDetailDto` carrying every AD-6 field plus `MeetingId`, `MeetingNotesId`, `NotesSha256`,
  `StartedByUserId`, `Warnings` and `IReadOnlyList<ProposedActionDto> Proposals` — FR-6 `prd.md:165`
  requires all fields present on the API representation, and FR-9 renders them.
- `src/ActionLedger.Application/Review/OwnerResolver.cs` — `public static Guid? Match(string?
  suggestedOwner, IReadOnlyList<UserSummaryDto> users)`: trim, return null when blank, match
  `DisplayName` with `StringComparer.OrdinalIgnoreCase`, return null on no match **and** on an
  ambiguous multi-match — AD-9 makes this the only owner matcher; the ambiguous case has no
  defensible pick and FR-15 already allows an empty picker.
- `src/ActionLedger.Application/Review/ProposedActionDtos.cs` — `ProposedActionDto(Guid Id, int
  Ordinal, string Description, string SuggestedOwner, DateOnly? SuggestedDueDate, double Confidence,
  string SourceExcerpt, bool IsLowConfidence, Guid? SuggestedOwnerUserId, ReviewState ReviewState)`
  — AD-13's field list minus the decision fields, which `epics-validation.md:146` assigns to Story
  3.1.
- `src/ActionLedger.Application/Review/ProposedActionReadModel.cs` — the single query class
  producing `ProposedActionDto`, taking `IReadDb` and `IExtractionSettings`: read the proposals of a
  run ordered by `Ordinal`, read the Users once, and set `IsLowConfidence` (`Confidence <
  threshold`) and `SuggestedOwnerUserId` (`OwnerResolver.Match`) in memory after materializing —
  AD-9 and AD-15 make this the one place either value is computed, and the resolver is a pure C#
  function EF cannot translate.
- `src/ActionLedger.Application/Extraction/RunsQueries.cs` — `GetAsync(Guid id, CancellationToken)`
  projecting the run through `IReadDb`, throwing `NotFoundException("ExtractionRun", id)` when
  absent, and composing the proposals from `ProposedActionReadModel` — the house read shape
  (`MeetingsQueries.cs:77-94`).
- `src/ActionLedger.Application/Meetings/MeetingsQueries.cs` — replace the first literal `0` at
  `:58` with a correlated count of this Meeting's runs, leaving the tracked-action literal for Story
  3.1, and update the comment — `MeetingDtos.cs:17` promises `RunCount` stops being `0` in this story.
- `src/ActionLedger.Application/ApplicationRegistration.cs` — register `RunExtractionHandler`,
  `RunsQueries` and `ProposedActionReadModel` in their own labelled block — every handler and query
  is registered here and nowhere else.
- `src/ActionLedger.Application/ActionLedger.Application.csproj` — add
  `<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />` to the existing package
  item group with a comment naming NFR-4 — it is on AD-1's allowlist; adding anything else fails
  `DependencyRuleTests` Rule 2.
- `src/ActionLedger.Infrastructure/Persistence/Configurations/ExtractionRunConfiguration.cs`,
  `ProposedActionConfiguration.cs`, `ActionRevisionConfiguration.cs` — one per root/child, copying
  `MeetingConfiguration`'s shape: explicit `HasKey`, `IsRequired`, `HasMaxLength` from the Domain
  constants, `builder.Ignore(x => x.DomainEvents)` on the roots, `public const string` index names,
  the unique index on `(ExtractionRunId, Ordinal)` and the non-unique one on `(TargetType, TargetId,
  Sequence)`, the `OwnsMany`/`HasMany` mapping of the run's proposals with a cascade FK, and
  `builder.Property<uint>("xmin").IsRowVersion()` **on `ProposedAction` only** — AD-20 lists the
  four roots that carry the token and `ExtractionRun` is not one of them.
- `src/ActionLedger.Infrastructure/Persistence/AppDbContext.cs` — add `DbSet<ExtractionRun>
  ExtractionRuns` and `DbSet<ActionRevision> ActionRevisions`; `ProposedAction` gets none if it is
  mapped as an owned collection — the `DbSet` property name is what `ModelConventions` snake_cases
  into the table name, so these two names produce `extraction_runs` and `action_revisions` exactly.
- `src/ActionLedger.Infrastructure/Persistence/ExtractionRunRepository.cs` and
  `ActionRevisionRepository.cs` — `internal sealed class X(AppDbContext context) : IX`, `Add`/
  `AddRange` plus the reads, no save — `MeetingRepository.cs:12-23` is the template; the run's find
  must `Include` the proposals if they are a navigation EF does not load automatically.
- `src/ActionLedger.Infrastructure/Ai/AiSettings.cs` — extend the record with
  `double LowConfidenceThreshold` — Infrastructure still reads no configuration; the composition
  root hands it the already-validated value.
- `src/ActionLedger.Infrastructure/Ai/ExtractionSettings.cs` — `internal sealed class
  ExtractionSettings(AiSettings settings) : IExtractionSettings` exposing the threshold, registered
  in `AddActionLedgerAi` — AD-16's "implemented in Infrastructure over the options".
- `src/ActionLedger.Infrastructure/InfrastructureRegistration.cs` — add the two repositories beside
  the existing `AddScoped` lines and `IExtractionSettings` inside `AddActionLedgerAi`.
- `src/ActionLedger.Infrastructure/Migrations/<timestamp>_AddExtractionRuns.cs` (+ `.Designer.cs`
  and the updated `AppDbContextModelSnapshot.cs`) — generated, never hand-written, with
  `dotnet tool restore` then `dotnet dotnet-ef migrations add AddExtractionRuns --project
  src/ActionLedger.Infrastructure --startup-project src/ActionLedger.Api` and the `ValidateOnStart`
  keys in the environment — AD-17 ships schema only through the bundle, and all three files are
  committed unedited.
- `src/ActionLedger.Api/Program.cs` — pass `ai.LowConfidenceThreshold` into the `AiSettings`
  construction at `:53` — the composition root is the only place that reads `IOptions`.
- `src/ActionLedger.Api/Errors/ProblemDetailsMapping.cs` — add
  `ValidationFailedException => (StatusCodes.Status400BadRequest, …)` to `ApiExceptionHandler`'s
  switch and update the class doc from "three exceptions" to four — the slug `validation` and the
  title already exist at `:118` and `:136`.
- `src/ActionLedger.Api/Controllers/MeetingsController.cs` — add `[HttpPost("{id}/runs")]`
  `StartRun` taking `RunExtractionHandler`, returning `CreatedAtAction("Get", "Runs", new { id =
  run.Id }, run)` with `[EndpointName("StartExtractionRun")]` and `ProducesResponseType` for 201,
  400, 401 and 404 — the route lives under `meetings` (AD-13 `:132`), and the class doc's "four
  Meeting operations" line must be updated.
- `src/ActionLedger.Api/Controllers/RunsController.cs` — a new `[ApiController] [Route("runs")]
  [Authorize] [Tags("Runs")]` controller with one `[HttpGet("{id}")]` `Get` action named
  `GetExtractionRun`, returning `RunDetailDto` — AD-13 publishes `runs/<id>`; the class-level route
  carries no `api/v1`.
- `src/ActionLedger.Web/openapi.json` — regenerate with
  `dotnet run --project src/ActionLedger.Api -- --export-openapi` and commit — `OpenApiSnapshotTest`
  byte-compares it and the web client is generated from it.
- `tests/Domain.Tests/Extraction/ExtractionRunTests.cs` — the aggregate's rules: ordinals are
  zero-based and follow draft order; one revision per proposal with kind AiProposal, sequence 1,
  null actor and the **same** `OccurredAt` for every revision in one call; the new-value JSON
  carries exactly the five FR-21 fields; a `Failed` run refuses proposals; `AddProposals` twice is a
  rule violation; `Start` refuses a Failed run with no reason, a Succeeded run with one, a blank
  provider/model/prompt/schema, a negative duration or token count, and an empty actor; ids are
  UUIDv7; warnings default to empty, never null. Plus a reflection shape test in the
  `MeetingTests.cs:156-190` style proving `ProposedAction` exposes no public setter or mutator
  beyond its Review State.
- `tests/Application.Tests/Extraction/RunExtractionHandlerTests.cs` — with hand-written fakes under
  a `// --- Fakes ---` banner: a Succeeded result persists the run, N proposals and N revisions and
  commits **once**; a Failed result persists the run with the reason verbatim, zero proposals and
  zero revisions and still commits once and returns `Outcome.Failed`; dropped proposals do not
  become rows but their warnings are persisted; an unknown meeting throws `NotFoundException`; a
  meeting with no notes throws `ValidationFailedException` and never calls the extractor; the
  extractor receives exactly the notes text and the meeting date; the actor is the `ICurrentUser`
  value; `StartedAt`/`DurationMs`/tokens come from the metrics and not from the clock; and the
  `ExtractionRunCompleted` log line is emitted once, carries provider, model, prompt version,
  duration, both token counts and outcome, and contains **no** notes text, description or excerpt.
- `tests/Application.Tests/Review/OwnerResolverTests.cs` and `ProposedActionReadModelTests.cs` —
  the I/O matrix's resolver and flag rows: exact match, case-insensitive match, whitespace-padded
  suggestion, blank suggestion, no match, ambiguous duplicate display names; and
  `Confidence` below / exactly at / above the threshold, with a non-default threshold proving the
  value is read from `IExtractionSettings` rather than hard-coded, and proposals returned in
  `Ordinal` order.
- `tests/Infrastructure.Tests/ExtractionPersistenceTests.cs` — Docker-backed, in the
  `MeetingPersistenceTests` shape: the exact `information_schema` column set and types for
  `extraction_runs`, `proposed_actions` and `action_revisions`; `xmin` present as a concurrency
  token on `proposed_actions` and absent from `information_schema`; `UNIQUE` in the indexdef for
  `(extraction_run_id, ordinal)` and a plain index for `(target_type, target_id, sequence)`;
  `warnings` round-trips an empty list and a multi-element list; enums round-trip as strings; a
  second run on the same meeting inserts and leaves the first run's rows untouched;
  `IActionRevisionRepository.ListForTargetAsync` returns revisions ordered by `OccurredAt` then
  `Sequence`; and a `RecordingCommandInterceptor` assertion that the whole use case is inserts only.
- `tests/Api.Tests/RunsEndpointTests.cs` — 201 with a **followed** `Location` for both Succeeded and
  Failed; 400 `validation` for a meeting with no notes; 404 `not-found` for an unknown meeting and
  an unknown run; 401 anonymous on both routes; and `GET runs/{id}` returning every AD-6 field with
  `inputTokens`/`outputTokens` rendered as `0` rather than omitted.
- `tests/Api.Tests/NotesImmutabilityTests.cs`, `tests/Api.Tests/MeetingsEndpointTests.cs`,
  `tests/Api.Tests/MeetingsSuccessResponseTests.cs`, `tests/Application.Tests/Meetings/MeetingsTests.cs`
  — update the exact route-set assertions to include the new POST, and the `runCount == 0`
  assertions to expect the real count — these are the four tests this story's surface deliberately
  reddens.

**Acceptance Criteria:**

- Given a Meeting with notes and `Ai:Provider=Fake`, when I `POST /api/v1/meetings/{id}/runs`, then
  the response is 201 `RunDto { id, outcome }`, the `Location` header resolves to
  `GET /api/v1/runs/{id}`, and one run row exists carrying provider, model, prompt version, schema
  version, start timestamp, duration, both token counts, outcome, failure reason and warnings.
- Given the extractor returns `ExtractionResult.Failed`, when I POST a run, then the response is
  still 201 with `outcome: "Failed"`, the run row carries the failure reason verbatim, and no
  proposal row and no revision row is written.
- Given a Succeeded result with N kept proposals, when the run is persisted, then exactly N
  `proposed_actions` rows exist with ordinals 0..N-1 in the order the AI returned them, exactly N
  `action_revisions` rows exist with kind `AiProposal`, sequence 1, null actor and one shared
  `OccurredAt`, and the whole write happened in a single `SaveChanges`.
- Given a Meeting that has no notes, when I POST a run, then the response is 400 with
  ProblemDetails `type: "validation"` and no run row is written.
- Given a Meeting that already has runs, when I POST another, then it succeeds and no earlier run,
  proposal or revision row is modified.
- Given a persisted run, when I `GET /api/v1/runs/{id}`, then every FR-6 field is present (tokens
  rendered as `0`, warnings as an array) and its proposals come back in `Ordinal` order as
  `ProposedActionDto` with `isLowConfidence` computed against `Ai:LowConfidenceThreshold`,
  `suggestedOwnerUserId` set by case-insensitive display-name match or null, and
  `reviewState: "Pending"`.
- Given any completed run, when it is persisted, then exactly one `ExtractionRunCompleted` log event
  is written carrying the correlation id, provider, model, prompt version, duration, both token
  counts and outcome, and no notes text, description or source excerpt appears anywhere in it.
- Given an anonymous request, when it reaches either new route, then it is 401 before any meeting or
  run is loaded.
- Given the Meeting List, when a Meeting has runs, then `runCount` reports the real number while
  `trackedActionCount` stays `0` until Story 3.1.

## Spec Change Log

## Review Triage Log

### 2026-09-22 — Review pass (follow-up 2)

- verdicts: 32 findings — high 0, medium 4, low 21, false 7, maybe-false 0
- findings:
  - `[low]` `[reject]` intent-alignment: no test drives POST → handler → real Fake → EF unit of work → Postgres → GET as one path — carried: the single-commit claim split across a fake unit of work and a hand-built aggregate still reads as the first pass's row describes; closing it needs a new Postgres-backed handler harness.
  - `[low]` `[reject]` intent-alignment: the ambient correlation id is not asserted at the Serilog/`LogContext` surface — carried: the enricher is global and pinned by `CorrelationIdTests`; asserting it at the handler needs a Serilog sink harness.
  - `[low]` `[reject]` intent-alignment: `FailureReason` is trimmed and clipped, not stored verbatim as the matrix says — carried: the code is right and documented; the fix edits this build's spec.
  - `[medium]` `[defer]` intent-alignment: a blank failure reason becomes a 409 with no row — carried: already `DW-20`, unreachable with the only registered provider; not re-deferred.
  - `[medium]` `[defer]` intent-alignment: an over-long model name becomes a 409 with no row — carried: the prior pass's deferral, now `DW-21` in the ledger; not re-deferred.
  - `[false]` `[reject]` intent-alignment: refusing blank text in `ExtractionOutputValidator` breaches the "no second excerpt verifier / re-normalizer / re-filtering" rule — refuted: the change adds a presence check inside the existing layer-two validator; it adds no verifier, rewrites no value, and filters nothing (a blank answer fails the whole attempt, as every other validation failure does).
  - `[false]` `[reject]` intent-alignment: `OwnerResolver` trims and the roster excludes system users, beyond "case-insensitive equality" — carried for the roster half; the trim is what the intent's own "null when the suggestion is blank" requires, and equality is still whole-string case-insensitive.
  - `[false]` `[reject]` intent-alignment: the Meeting List `RunCount` is unrequested scope — carried: `MeetingSummaryDto` shipped the field documented "Always `0` until Story 2.5".
  - `[false]` `[reject]` intent-alignment: the clock is read after the provider call — refuted as a divergence: the intent requires one read per request used only as the revisions' `now`, which holds; the placement was the prior pass's deliberate patch.
  - `[low]` `[reject]` edge-case: the request token on `CommitAsync` discards a spent extraction on disconnect — carried.
  - `[false]` `[reject]` edge-case: an undefined `ExtractionOutcome` passes both reason checks — refuted: the only caller maps `result.IsSucceeded ? Succeeded : Failed` (`RunExtractionHandler.cs:95`), so no undefined value can be produced.
  - `[low]` `[reject]` edge-case: `RequireSha256` accepts a 64-character non-hex string — carried.
  - `[medium]` `[patch]` edge-case: `AddProposals` can leave a half-built aggregate when a draft is refused mid-loop — carried: the prior pass's validator fix removed every producer; not patched again.
  - `[false]` `[reject]` edge-case: a blank warning throws after the provider call — carried: the extractor's warning is a non-empty interpolation quoting the dropped excerpt.
  - `[false]` `[reject]` edge-case: the no-notes 400 is a plain `ProblemDetails` while the POST declares `ValidationProblemDetails`, so the client "expects an errors map" — refuted: `openapi.json` marks no member of `ValidationProblemDetails` required, so a body without `errors` is valid against the declared schema; the declaration follows the malformed-id 400, which does carry one.
  - `[low]` `[reject]` edge-case (claim): the spec says the clock is read before the extractor — the fix edits this build's spec; the code and its test are the authority.
  - `[low]` `[reject]` edge-case (claim): the spec says `AddProposals` is called only when something was kept — the fix edits this build's spec.
  - `[low]` `[reject]` edge-case (claim): the spec says log then commit — carried: stale spec text, code right.
  - `[low]` `[reject]` edge-case (claim): the reason is not verbatim — carried: fix edits this build's spec.
  - `[low]` `[reject]` verification-gap (other): the `DW-21` ledger `reason:` is cut off mid-sentence — real, but `deferred-work.md` entries are orchestrator-owned and this run is barred from rewriting them; the full text is in this spec's `deferred` frontmatter. Recorded under residual risks.
  - `[low]` `[reject]` verification-gap (other): `DW-20` has no `severity:` — carried: orchestrator owns the ledger.
  - `[low]` `[reject]` blind-hunter: `DW-21` truncated in the ledger — same claim as the verification-gap row; same reason.
  - `[low]` `[reject]` blind-hunter: `DW-20`'s severity is recorded three ways — carried: orchestrator owns the ledger.
  - `[low]` `[reject]` blind-hunter: Design Notes, the handler task and the matrix contradict the code and the Spec Change Log is empty — the fix edits this build's spec.
  - `[low]` `[patch]` blind-hunter: two "verbatim" claims survived the prior sweep (`ExtractionOutcome.Failed`, `ExtractionRun.Clip` remarks) — confirmed. Both reworded to match `FailureReason`'s own doc ("trimmed, and clipped to 2,000 characters"). "Both attempts failed" left as-is: `ChatClientActionExtractor` returns `Failed` only after the two-attempt loop.
  - `[low]` `[patch]` blind-hunter: `ProposedAction.RequireText`'s remarks still say the validator "bounds length and nothing else" — confirmed stale since the prior pass. Remarks rewritten: the validator refuses blank text first and this guard is the backstop.
  - `[low]` `[patch]` blind-hunter: the POST declares no 409 and its remark has a stray line break — the 409 half is carried (the reachable paths are `DW-20`/`DW-21`, unreachable today); the stray break in `MeetingsController.StartRun`'s remarks was reflowed.
  - `[low]` `[patch]` blind-hunter: the ordered-`Include` fix has no witness — confirmed: `Proposals_round_trip_in_ordinal_order_with_their_enum_as_a_string` sorted before asserting. The sort is removed so the assertion reads `Proposals` as the repository returns them; mutating the Include to `OrderByDescending` now turns it red.
  - `[low]` `[patch]` blind-hunter: `ExtractionRun.RevisionJson` uses the default encoder, so non-ASCII and `' & < >` land in `action_revisions.new_value` (a `text` column, not `jsonb`, so Postgres does not normalize it) as `\uXXXX` escapes — confirmed; pasted notes routinely carry curly quotes and dashes, and rows written now cannot be re-encoded later. `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping` set, with a comment (the value is stored, never embedded in HTML); new Domain test `The_new_value_json_keeps_non_ascii_and_html_sensitive_text_literal`, red with the encoder line removed.
  - `[medium]` `[patch]` blind-hunter: nothing drives the prior pass's headline fix end to end — a blank description or excerpt passing through `ChatClientActionExtractor` must come back `Failed`, not `Succeeded` — confirmed, and the prior pass's claim that this composition "cannot be built today" is wrong: `ChatClientActionExtractorTests` already has a `ScriptedChatClient` harness. Added the theory `Two_blank_text_responses_fail_the_run_rather_than_succeeding` (description and excerpt cases); removing either validator check turns it red. The Failed → persisted run → 201 half is already pinned by the handler and endpoint tests.
  - `[low]` `[reject]` blind-hunter: the migration adds no CHECK constraints mirroring the aggregate's invariants — no producer: the aggregate is the only writer, and the fix adds guards for a state nothing can create.
  - `[low]` `[reject]` blind-hunter: no endpoint lists a meeting's runs — out of scope by the intent itself ("Two endpoints: `POST meetings/{id}/runs` … and `GET runs/{id}`"); Story 2.6 owns the Meeting Detail read.

### 2026-09-22 — Review pass (follow-up)

- verdicts: 30 findings — high 2, medium 6, low 15, false 7, maybe-false 0
- findings:
  - `[high]` `[patch]` blind-hunter: a whitespace-only description passes `ExtractionOutputValidator` (length only) and is then refused by `ProposedAction.RequireText`, so a validated provider response leaves the POST as a 409 with no run row — the one outcome AD-11 exists to prevent. Confirmed: the validator checks `Length` only, the throw happens in `AddProposals` before `runs.Add` and before the commit, and `ApiExceptionHandler` maps `DomainRuleException` to 409. The prior pass created this path when it tightened `RequireText` from `IsNullOrEmpty` to `IsNullOrWhiteSpace` on the stated grounds that blank text is reachable. Fixed at the gate instead: the validator now refuses a blank-after-trim `description` and `sourceExcerpt`, so such a response becomes an ordinary `ExtractionResult.Failed` — a persisted Failed run and a 201 — and the aggregate guard returns to being the unreachable backstop it was described as.
  - `[medium]` `[patch]` blind-hunter: `AddProposals` appends to `_proposals` inside the loop and sets `_proposalsAdded` only after it, so a draft refused mid-loop leaves a half-built aggregate. Same root cause as the row above and shares its fix — with the validator refusing blank text no draft that reached `AddProposals` can be refused, so the partial-build state has no producer. The run object is discarded on the throw regardless.
  - `[false]` `[reject]` blind-hunter: `ProposedAction` assigns `ExtractionRunId` unguarded, so a `Guid.Empty` run id would orphan a row — refuted: `AddProposals` passes the aggregate's own `Id`, and `AggregateRoot`'s constructor (`src/ActionLedger.Domain/Common/AggregateRoot.cs:23`) sets it to `Guid.CreateVersion7()`. No path supplies an empty id.
  - `[low]` `[patch]` blind-hunter: "verbatim" is false in the published contract now that the reason is trimmed and clipped. Confirmed at four contract statements (`ExtractionRun.FailureReason`, `ExtractionRun` class remarks, `RunDtos.RunDetailDto`'s `FailureReason` param, `RunExtractionHandler` remarks, `MeetingsController.StartRun` remarks). Reworded to "trimmed, and clipped to 2,000 characters"; `FailureReasonMaxLength`'s own summary corrected too.
  - `[medium]` `[defer]` blind-hunter: `AiOptions.LocalOpenAI:Model` / `AzureOpenAI:Model` carry no `[StringLength]` mirroring `ExtractionRunMetadata.ModelMaxLength`, so an over-long operator-supplied model name becomes a per-request 409. Not caused by this story — `AiOptions` is Story 2.4's and the Fake supplies a constant model, so nothing reaches it today. Deferred with what would settle it.
  - `[low]` `[reject]` blind-hunter: `IExtractionRunRepository.FindByIdAsync` and `IActionRevisionRepository.ListForTargetAsync` have no production caller — true, but these are the AD-7 / AD-10 port shapes the spec's Code Map specifies, each with a doc comment stating why it exists, and both are exercised against real PostgreSQL by `ExtractionPersistenceTests`. No harm is named beyond tidiness and deleting them would remove tested infrastructure Story 3.x needs.
  - `[low]` `[patch]` blind-hunter: the POST declares `ValidationProblemDetails` for its 400 but no test sends a malformed id, so the declared schema has no witness — confirmed: the only tested 400 is `ValidationFailedException`, which `ApiExceptionHandler` renders as a bare `ProblemDetails` with no `errors` map. Added `A_malformed_meeting_id_on_the_run_route_is_a_400_validation_with_an_errors_map`, following the existing `MeetingsEndpointTests` pattern, asserting the `errors` member and that no run is written.
  - `[low]` `[patch]` blind-hunter: the `CreatedAtAction` comment asserts that being nameof-derived is what turns a rename into a 500 — confirmed backwards as written. Reworded to say the string resolution is the hazard and `nameof` is the mitigation, and to name what `nameof` cannot follow (a changed route template, a second `Get` on that controller).
  - `[false]` `[reject]` blind-hunter: a Failed run is indistinguishable at log level and the event drops `SchemaVersion` and `StartedAt` — refuted on both halves: the intent enumerates exactly the fields the event carries (provider, model, prompt version, duration, both token counts, outcome) and the template carries exactly those; and `{Outcome}` is a named hole, so Serilog captures it as a structured property an alert filters on rather than something to string-match. AD-11 makes Failed an ordinary outcome, so Information is the right level.
  - `[low]` `[reject]` blind-hunter: the `DW-20` ledger entry has no `severity:` field — the orchestrator owns `deferred-work.md` entries and this run is explicitly barred from rewriting them.
  - `[false]` `[reject]` blind-hunter: `sprint-status.yaml` says `done` while the spec says `in-review` — refuted: the board row is the orchestrator's own bookkeeping, and the spec's `in-review` is this pass's transient state, set at the top of this step and set back to `done` at finalization.
  - `[low]` `[reject]` blind-hunter: the `## Spec Change Log` is empty and `warnings: ['oversized']` is unexplained — rejected because the fix is to edit this build's spec.
  - `[high]` `[patch]` edge-case: a whitespace-only description yields 409 with no run row where AD-11 requires a persisted Failed run — same defect as the first blind-hunter row; shares its entry and fix.
  - `[low]` `[reject]` edge-case: blank or over-long provider metadata becomes a 409 — carried: the code still reads as the prior row describes. Provider, prompt version and schema version come from startup-validated configuration and `ExtractionSchema.Version`, and the model is the factory's own constant; the operator-supplied case is the deferred row above.
  - `[medium]` `[patch]` edge-case: the POST declares no 409 although `Start` and `AddProposals` can throw one — same entry as the first row. The fix removes the reachable path rather than declaring it, which is what the prior pass's refutation assumed.
  - `[low]` `[reject]` edge-case: the request token is passed to `CommitAsync`, so a disconnect discards a spent extraction — carried: no row is written and nothing is corrupted, and switching to `CancellationToken.None` changes cancellation semantics for the cost of one wasted Fake call.
  - `[low]` `[patch]` edge-case: `ExtractionRunRepository.FindByIdAsync` uses a bare `Include`, so `ExtractionRun.Proposals` — documented as AI order — rehydrates in whatever order the planner chose. Confirmed; the one Postgres test that could catch it sorts first. Changed to an ordered include on `Ordinal`, matching what the read side already asks for.
  - `[low]` `[reject]` edge-case: the roster read is unpaged, so a roster past the picker's 200-row page could pre-select a user the picker cannot show — matching against the whole roster is more correct than matching against a page; the defect is a UI concern Epic 3 owns, and paging the roster here adds machinery for a roster the product keeps small.
  - `[low]` `[reject]` edge-case: `RequireSha256` checks length only, so a 64-character non-hex string is accepted — carried: the hash is copied from `MeetingNotes.Sha256`, never caller-supplied, and the fix is a new guard for a state nothing can produce.
  - `[low]` `[patch]` edge-case: a Succeeded run that kept nothing skips `AddProposals`, so `_proposalsAdded` stays false on the aggregate that is committed — confirmed, and it is exactly the state that field's own doc comment says it exists to distinguish. The handler now calls `AddProposals` whenever the result succeeded; an empty list stages nothing. New test asserts the committed run refuses a second call.
  - `[low]` `[reject]` edge-case: the spec says log then commit, the code commits then logs — the code is right (the prior pass patched it deliberately) and the stale text is at `spec…:396`, so the fix is to edit this build's spec.
  - `[medium]` `[patch]` verification-gap: the completion event's two token counts are pinned only by a bare `"0"`, which either count alone satisfies — filed pre-verified, and a mutation confirmed it: dropping `outputTokens {OutputTokens}` from the template left the suite green before the fix. The assertion now demands the rendered pairs `inputTokens 0` and `outputTokens 0`; the same mutation is now red.
  - `[medium]` `[defer]` intent-alignment: a blank failure reason from the extractor becomes a 409 rather than the 201-with-Failed AD-11 fixes — carried: already `DW-20` in the ledger, unreachable with the only registered provider, and rooted in Story 2.4's `ExtractionResult.Failed`.
  - `[low]` `[reject]` intent-alignment: nothing at the persistence or HTTP surface asserts what a caller receives for an over-long reason — the clip is a Domain rule with two direct Domain tests, and `RunsQueries` copies the field through untouched; re-asserting the same transformation through three layers would not make it more falsifiable.
  - `[medium]` `[patch]` intent-alignment: `IClock.UtcNow` is read before the provider call, so every AiProposal revision's `OccurredAt` precedes the run's own `StartedAt` — by up to the whole NFR-1 ceiling once a real provider is wired. Confirmed, and it is the direction the handler's own remark says it exists to avoid. The read moved to immediately after the extractor returns; the ordering journal now records the provider call and the clock read, and a new test names the rule. Still one read per request.
  - `[low]` `[patch]` intent-alignment: `ExtractionRun.Proposals` is ordered at the read surface but not at the aggregate-load surface — same entry as the edge-case row; shares its fix.
  - `[false]` `[reject]` intent-alignment: excluding system users from the roster narrows `OwnerResolver.Match` beyond the intent's literal "matches no User" — refuted: the intent constrains the matcher's algorithm, which `Match` implements literally; the roster is its input, and `UsersQueries.ListAsync` already excludes system users, so matching one would hand the client an id its own picker has no row for.
  - `[false]` `[reject]` intent-alignment: lighting up the Meeting List's `runCount` is outside the stated two-endpoint deliverable — refuted: `MeetingSummaryDto` shipped the field documented "Always `0` until Story 2.5", so this is the designated work, not scope creep.
  - `[false]` `[reject]` intent-alignment: the threshold plumbing reaches Story 2.4's `AiSettings` record — refuted: the intent requires the threshold to arrive through `IExtractionSettings` with Application never seeing `IOptions`, and bans editing `appsettings.json`; projecting the existing options record is the only route left open.
  - `[false]` `[reject]` intent-alignment: `Microsoft.Extensions.Logging.Abstractions` is a new Application package reference — refuted: it is on the allowlist at `tests/Architecture.Tests/DependencyRuleTests.cs:46`, the ban at `:54-56` names EF Core, ASP.NET Core and Npgsql, and the Architecture suite passes.

### 2026-09-22 — Review pass

- verdicts: 36 findings — high 0, medium 9, low 18, false 9, maybe-false 0
- findings:
  - `[medium]` `[patch]` blind-hunter: `ExtractionRunCompleted` is logged before the commit — confirmed at the handler; the log call sat above `CommitAsync`. Moved below it; the ordering test now journals the log line and a new test proves a throwing commit writes no event.
  - `[low]` `[reject]` blind-hunter: `RequireSha256` checks length only, so a 64-character non-hex string is accepted — real but unreachable: the hash is copied from `MeetingNotes.Sha256`, never caller-supplied, and the fix is a new guard for a state nothing can produce.
  - `[low]` `[reject]` blind-hunter: truncation contradicts the "verbatim" AC and belongs in the Spec Change Log — the change-log half is rejected outright because its fix edits this build's spec; the substantive half (an untested clip that can split a surrogate) was patched under the verification-gap and edge-case rows below.
  - `[low]` `[patch]` blind-hunter: `_proposalsAdded` is unmapped, so the once-only guard vanishes after EF rehydration — confirmed. The rehydration constructor now sets it true, with a Domain test building a run through the private constructor.
  - `[low]` `[patch]` blind-hunter: `CreatedAtAction("Get", "Runs", …)` pins the Location to two string literals — confirmed against the repo's `nameof` convention. Replaced with `nameof(RunsController.Get)` and a `nameof`-derived controller name.
  - `[medium]` `[patch]` blind-hunter: the POST's 400 declares `ProblemDetails` while a malformed id yields `ValidationProblemDetails` — confirmed; every other action on the controller declares the validation shape. Changed and `openapi.json` regenerated.
  - `[medium]` `[patch]` blind-hunter: no index on `extraction_runs.meeting_id` behind the new per-row correlated count — confirmed; the migration created only the two named indexes. Added `ix_extraction_runs_meeting_id` in the configuration and regenerated the migration.
  - `[low]` `[reject]` blind-hunter: the read model projects placeholder values then clones every DTO — real but contained in one 40-line method, and the fix (a private row record) is more than a direct correction for a defect only a future early return would expose.
  - `[low]` `[reject]` blind-hunter: owner matching is O(proposals × roster) over an unbounded roster read — the blank-suggestion half is refuted (`OwnerResolver.Match` returns before the loop when the trimmed name is empty), and the rest needs a dictionary for a roster the product keeps small.
  - `[low]` `[patch]` blind-hunter: `Sequence` is documented strictly increasing per target but nothing enforces it — true, and unviolatable by this story's only writer. Patched as a comment naming what must stop a duplicate when Story 3.2 adds a second writer; the index was deliberately left non-unique.
  - `[low]` `[patch]` blind-hunter: `ProposedAction.RequireText` uses `IsNullOrEmpty`, so `"   "` passes a check whose message says "is required" — confirmed reachable: `ExtractionOutputValidator` bounds length only. Changed to `IsNullOrWhiteSpace` with the value still stored untrimmed.
  - `[false]` `[reject]` blind-hunter: `ValidationFailedException` takes prose and has an unused `(string, Exception)` overload — refuted: `NotFoundException` carries the same three-constructor shape including the inner-exception overload, so this matches the house convention rather than departing from it.
  - `[false]` `[reject]` blind-hunter: the exception switch's arm order is unpinned — refuted: all four exception types are `sealed`, so the hypothesised derivation cannot compile.
  - `[low]` `[reject]` blind-hunter: the Spec Change Log and Triage Log are empty — rejected because its fix is to edit this build's spec; the triage log is written by this pass regardless.
  - `[medium]` `[patch]` edge-case: completion event logged before the commit — same defect as the first blind-hunter row; shares its entry and fix.
  - `[low]` `[reject]` edge-case: the request token is passed to `CommitAsync`, so a disconnect discards a spent extraction — no row is written and nothing is corrupted; switching to `CancellationToken.None` changes cancellation semantics for a cost that is one wasted Fake call.
  - `[low]` `[patch]` edge-case: `_proposalsAdded` lost on rehydration — same entry as the blind-hunter row.
  - `[false]` `[reject]` edge-case: blank or over-long provider metadata becomes a 409 — refuted: provider, prompt version and schema version come from startup-validated configuration and `ExtractionSchema.Version`, and the model is the factory's own constant; no path produces blank or over-long values.
  - `[low]` `[defer]` edge-case: a blank failure reason from the extractor becomes a 409 — real but unreachable with the only registered provider, and its root is Story 2.4's unguarded `ExtractionResult.Failed`. Deferred with what would settle it.
  - `[false]` `[reject]` edge-case: a blank warning throws and discards a succeeded run — refuted: the extractor's warning is a non-empty interpolation quoting the dropped excerpt, so a blank warning has no producer.
  - `[low]` `[patch]` edge-case: `reason[..2000]` can split a surrogate pair — confirmed. Truncation extracted to a `Clip` helper that backs off one character on a high surrogate, with a well-formedness test.
  - `[false]` `[reject]` edge-case: whitespace-only notes spend a provider call instead of returning 400 — refuted: whitespace notes are notes by the repo's own deliberate rule (Story 2.1 pins it), FR-4's 400 is for a Meeting with no notes, and the run persists correctly as Succeeded with no proposals.
  - `[false]` `[reject]` edge-case: a `Guid.Empty` actor reaches `Start` after the provider call — refuted: `ClaimsPrincipalCurrentUser` throws when the `sub` claim is missing or unparseable, and tokens are signed by this Api.
  - `[medium]` `[patch]` edge-case: no index behind the meeting-list run count — same entry as the blind-hunter row.
  - `[medium]` `[patch]` edge-case: the POST 400 publishes the wrong schema — same entry as the blind-hunter row.
  - `[false]` `[reject]` edge-case: a 409 can leave the POST but is undeclared — refuted: every `DomainRuleException` path it names was itself refuted above, and ordinals are assigned inside one new run so the unique index cannot be raced.
  - `[low]` `[reject]` edge-case: the "verbatim" claim is contradicted by the clip — same claim as the blind-hunter row; the reachable part was patched, the spec edit rejected.
  - `[medium]` `[patch]` verification-gap: every `Ai:LowConfidenceThreshold` in the repo is 0.70, so hard-coding it ships green — filed pre-verified. Added an endpoint test driving a non-default threshold through `ConfigurationOverrides`.
  - `[low]` `[patch]` verification-gap: the truncation branch is executed by no test — filed pre-verified. Added a Domain test with a reason longer than the maximum.
  - `[low]` `[patch]` verification-gap: the revision ordering read is pinned by a test that sees one row — filed pre-verified. The test now inserts extra rows by raw SQL and asserts the whole order.
  - `[medium]` `[patch]` verification-gap (other): completion event logged before the commit — same entry as the first row.
  - `[medium]` `[defer]` verification-gap (other): `ExtractionResult.Failed` does not guard a blank reason — same entry as the edge-case row; deferred at the higher of the two grades.
  - `[false]` `[reject]` intent-alignment: nothing records that the human-only-action condition was evaluated — refuted by construction: the Auto Run Result and final status are written at finalization, after the review the diff was captured for.
  - `[low]` `[reject]` intent-alignment: no test asserts the correlation id is on the completion event — the enricher is global and already pinned by `CorrelationIdTests`; asserting it at the handler needs a Serilog sink harness, which is more than a direct correction.
  - `[low]` `[reject]` intent-alignment: the single-commit claim is split across a fake unit of work and a hand-built aggregate — both halves are covered and their composition is exercised over the HTTP surface; closing the last gap needs a new Postgres-backed handler harness.
  - `[false]` `[reject]` intent-alignment: the API ordering test would survive deleting the read model's `OrderBy` — refuted: `Proposals_come_back_in_ordinal_order_whatever_order_the_rows_arrive_in` feeds the rows reversed, and `Proposals_round_trip_in_ordinal_order_with_their_enum_as_a_string` covers it in SQL.

## Design Notes

**Why `Start` carries the outcome rather than a later `Complete` call.** AD-3 sketches
`ExtractionRun.Start(meeting, notes, provider, model, promptVersion, schemaVersion, actor, now)`, but
AD-11 requires one persisted row per attempt and AD-20 allows one commit, and `ARCHITECTURE.md:211`
puts `Start(...).AddProposals(...)` **after** the extractor returns. A two-phase Start/Complete would
either write the row twice or leave a half-populated aggregate in memory for no benefit. So `Start`
receives the finished metrics and the outcome, and the story's two named methods stay exactly the two
the acceptance criterion names.

**Where each instant comes from.** `ExtractionRunMetadata.StartedAt` and `DurationMs` are the
extractor's own measurement, carried through `ExtractionMetrics` — the handler never observes the
provider call's start. `IClock.UtcNow` is read once at the top of the handler and is used only as the
AD-7 shared `now`, so every revision from one `AddProposals` call carries the same timestamp even if
the call spans a second boundary. Conflating the two would make a run's audit trail claim the
proposals were recorded before the run began.

**Why `ProposedAction` carries `xmin` and `ExtractionRun` does not.** AD-20 names exactly four
token-carrying types — `ProposedAction`, `TrackedAction`, `Meeting`, `OutboxMessage` — because those
are the rows a second actor can race. A run is written once and never updated; giving it a token
would cost a column-shaped lie in the snapshot and protect nothing.

**Why the read model materializes before computing.** `OwnerResolver.Match` is pure C# with
`StringComparer.OrdinalIgnoreCase` semantics EF cannot translate, and `IExtractionSettings` is an
Application port with no SQL meaning. So the query projects the persisted columns, `ToListAsync`
materializes, and the two derived values are set in memory — once, in the one class AD-9 names, so
Run Detail and the Epic 3 Review Screen can never disagree about whether a proposal is flagged.

**Why `double` and not `decimal` for the threshold.** `AiOptions.LowConfidenceThreshold` is
`double` and `ExtractedProposal.Confidence` is `double`; the adversary review's `decimal` would put a
cast on the one comparison that matters. `Confidence < LowConfidenceThreshold` is a strict inequality,
so a proposal exactly at 0.70 is not flagged — the threshold is the first value that is *not* low.

**Why the aggregate serializes the revision JSON.** AD-7 says the new value is "the proposal as JSON"
written inside `AddProposals`. `System.Text.Json` ships in the `net10.0` shared framework, so Domain
serializing needs no `PackageReference` and Rule 1's zero-reference assertion still holds. Serializing
a small five-field shape — not the entity — keeps `Id`, `Ordinal` and `ReviewState` out of a record
FR-21 defines by its five fields.

**Golden example — the revision loop inside `AddProposals`:**

```csharp
// AD-7 — one instant for every revision this call produces, and a brand-new target's first
// revision is sequence 1.
List<ActionRevision> revisions = [];

for (int ordinal = 0; ordinal < drafts.Count; ordinal++)
{
    ProposedAction proposal = new(Id, ordinal, drafts[ordinal]);
    _proposals.Add(proposal);
    revisions.Add(ActionRevision.AiProposal(proposal.Id, ToJson(drafts[ordinal]), now));
}

return revisions;
```

## Verification

**Commands:**

- `dotnet tool restore` — expected: `dotnet-ef` 10.0.12 available as `dotnet dotnet-ef`.
- `dotnet build ActionLedger.sln` — expected: 0 errors, 0 warnings (`TreatWarningsAsErrors` is on).
- `dotnet build ActionLedger.sln -c Release` — expected: the same; this is `ci.yml`'s shape.
- `dotnet run --project src/ActionLedger.Api -- --export-openapi` — expected: exits 0 with no
  database and no `Jwt:*`/`Database:*` environment, and rewrites `src/ActionLedger.Web/openapi.json`.
- `dotnet test ActionLedger.sln` — expected: eight assemblies green, 0 skipped, total above the 969
  recorded at `3f0f9a7`. Docker must be running: the new `ExtractionPersistenceTests` use
  Testcontainers and there is no skip path.
- `git status --porcelain` — expected: changes confined to `Directory.Packages.props`,
  `src/ActionLedger.Domain/`, `src/ActionLedger.Application/`, `src/ActionLedger.Infrastructure/`,
  `src/ActionLedger.Api/`, `src/ActionLedger.Web/openapi.json`, `tests/`, and this spec.
- `git diff --stat 3f0f9a7 -- .env.example src/ActionLedger.Api/appsettings.json docker-compose.yml prompts fixtures` —
  expected: empty.
- `git diff --stat 3f0f9a7 -- src/ActionLedger.Web` — expected: `openapi.json` and the generated
  client only; no `.razor` file.

**Mutation checks — introduce each, confirm the named test goes red, then revert:**

- Return 400 instead of 201 for a Failed result → the Failed-run endpoint test.
- Commit twice in the handler (once after `Add`, once at the end) → the single-commit assertion.
- Stamp each revision with a fresh `DateTimeOffset.UtcNow` → the shared-`OccurredAt` domain test.
- Start ordinals at 1 → the ordinal-order domain test and the read-order API test.
- Give every revision sequence 0 → the sequence-1 domain test.
- Set `ActorUserId` to the current user on an AiProposal revision → the null-actor domain test.
- Persist `Dropped` proposals as rows → the dropped-proposals-are-not-rows handler test.
- Change the flag to `Confidence <= LowConfidenceThreshold` → the at-the-threshold read-model test.
- Hard-code `0.70` in the read model → the non-default-threshold test.
- Make `OwnerResolver.Match` ordinal-sensitive → the case-insensitive resolver test.
- Return the first match when two Users share a display name → the ambiguous-match test.
- Add the notes text to the `ExtractionRunCompleted` message template → the no-notes-in-logs test.
- Drop `.IsUnique()` from the `(extraction_run_id, ordinal)` index → the UNIQUE indexdef theory.
- Remove `builder.Property<uint>("xmin").IsRowVersion()` from `ProposedActionConfiguration` → the
  concurrency-token persistence test.
- Leave the fourth arm out of `ApiExceptionHandler` → the 400 endpoint test (it becomes a 500).

**Manual checks:**

- After generating the migration, read the generated `Up` before committing: confirm it creates the
  three tables with snake_case names, creates **no** physical `xmin` column, and emits the unique
  and non-unique indexes under the names the configurations declare. A hand-edit to a migration is
  never the fix — change the configuration and regenerate.


## Auto Run Result

Status: done

**Summary of implemented change.** Second follow-up review pass over the Story 2.5 tree at
`ef2f3ab` — no re-implementation. Four review layers reported 32 findings against the diff since
`3f0f9a7`. Six entries were patched, all small: one closes the verification gap the previous pass
named as its unverified risk, and the rest are an audit-record encoding fix, a test that now
actually witnesses the ordered `Include`, and three stale doc comments. Nothing new was deferred.
The previous pass's stated residual risk — that the blank-description fix "cannot be built today"
— was wrong: `ChatClientActionExtractorTests` already had a scripted provider harness, and the
composition is now pinned there.

**Files changed (this pass).**

- `src/ActionLedger.Domain/Extraction/ExtractionRun.cs` — `RevisionJson` uses
  `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, so AiProposal revisions store non-ASCII and
  `' & < >` literally instead of as `\uXXXX`; the `Clip` remark no longer says "verbatim".
- `src/ActionLedger.Domain/Extraction/ExtractionOutcome.cs` — `Failed`'s doc says the reason is
  trimmed and clipped, not shown verbatim.
- `src/ActionLedger.Domain/Extraction/ProposedAction.cs` — `RequireText` remarks updated: the
  validator refuses blank text first, and this guard is the backstop.
- `src/ActionLedger.Api/Controllers/MeetingsController.cs` — stray line break in the `StartRun`
  remarks reflowed (XML remark only; `openapi.json` unchanged).
- `tests/Infrastructure.Tests/ChatClientActionExtractorTests.cs` — new theory
  `Two_blank_text_responses_fail_the_run_rather_than_succeeding` (description, excerpt).
- `tests/Domain.Tests/Extraction/ExtractionRunTests.cs` — new test
  `The_new_value_json_keeps_non_ascii_and_html_sensitive_text_literal`.
- `tests/Infrastructure.Tests/ExtractionPersistenceTests.cs` — the round-trip test no longer sorts
  `Proposals` before asserting, so it witnesses the repository's ordered `Include`.

**Review findings breakdown.** 32 findings — high 0, medium 4, low 21, false 7, maybe-false 0.

- Patched (6 entries: medium 1, low 5): the end-to-end blank-text gap at the extractor (medium);
  the revision JSON encoder; the ordered-`Include` witness; the two surviving "verbatim" claims;
  the stale `RequireText` remarks; the `StartRun` remark reflow.
- Carried without re-patching: `AddProposals` half-built aggregate (medium, patched by the previous
  pass's validator fix — no producer remains).
- Deferred: none new. Carried, not re-deferred: `DW-20` (blank failure reason) and `DW-21`
  (over-long model name), both medium, both latent until Story 2.7.
- Rejected, with reasons: no single Postgres-backed POST→GET test, correlation id at the Serilog
  surface, request token on `CommitAsync`, non-hex `RequireSha256` (all carried rejections — code
  unchanged since the logged row); four spec-text contradictions — clock placement, `AddProposals`
  on empty `Kept`, log-then-commit, "verbatim" (the fix edits this build's spec); `DW-21` truncated
  in the ledger and `DW-20` lacking severity (ledger entries are orchestrator-owned); no DB CHECK
  constraints (no producer — the aggregate is the only writer); no list-runs endpoint (the intent
  fixes two endpoints; Story 2.6 owns it). Refuted: the validator change as a "second verifier",
  the owner-matcher trim and roster, `RunCount` scope, clock placement as a divergence, an
  undefined outcome enum (the handler maps from a bool), a blank warning (carried), and the no-notes
  400 schema mismatch (`openapi.json` makes no `ValidationProblemDetails` member required).

**Follow-up review recommendation: false.** This is a follow-up pass, and it patched no `high`.
Patched counts by verdict: high 0, medium 1, low 5. The work has converged.

**Verification performed.**

- `dotnet build ActionLedger.sln` and `dotnet build ActionLedger.sln -c Release` — both 0 warnings,
  0 errors.
- `dotnet test ActionLedger.sln` — **1142 passed, 0 failed, 0 skipped** (Docker up for
  Testcontainers). Previous pass recorded 1139; the 3 new tests are this pass's.
- `dotnet run --project src/ActionLedger.Api -- --export-openapi` — exit 0; `openapi.json`
  byte-identical.
- `git diff --stat 3f0f9a7 -- .env.example src/ActionLedger.Api/appsettings.json docker-compose.yml prompts fixtures`
  — empty. `git diff --stat 3f0f9a7 -- src/ActionLedger.Web` — `openapi.json` only.
- Mutation checks, each confirmed red and then reverted: removing the encoder line reddened the new
  Domain test; disabling both blank checks in `ExtractionOutputValidator` reddened both cases of the
  new extractor theory; changing the repository's Include to `OrderByDescending` reddened
  `Proposals_round_trip_in_ordinal_order_with_their_enum_as_a_string`.

**Residual risks.**

- The ordered-`Include` witness catches a wrong order, but a *bare* `Include` could still pass on
  a three-row table where Postgres happens to return insertion order.
- `DW-20` and `DW-21` stay latent until Story 2.7 wires a real provider. Both come from the handler
  answering every `DomainRuleException` with a 409, so 2.7 should settle them together.
- The `DW-21` ledger entry's `reason:` is cut off mid-sentence ("…or" then `status: open`). This
  run may not edit ledger entries. The full text is in this spec's `deferred` frontmatter; the
  orchestrator should restore the ledger copy.
- The spec's Design Notes and Tasks still describe the pre-patch clock placement, log-then-commit
  order, and a "verbatim" reason. The code and its tests are the authority.
