---
title: 'Story 2.1 — Meeting and immutable notes API'
type: 'feature'
created: '2026-09-21'
status: 'done'
baseline_revision: '8f0702b334b44bc274cb8b2fb670cfc0bb35234b'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-2-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-4-sign-in-and-receive-a-jwt-list-users.md'
warnings: ['oversized']
deferred:
  - summary: >-
      Every `<response code="...">` and `<param>` doc comment on a controller action exports as a
      bare HTTP reason phrase, so the prose that reads as contract documentation is dead.
    evidence: |-
      The exported document gives every response `"description": "Created"`, `"Bad Request"`,
      `"Not Found"`, `"Conflict"` and emits the `{id}` path parameter with no description; only
      `[EndpointSummary]` and `[EndpointDescription]` survive the export. Pre-existing rather than
      caused by this story: `POST /api/v1/auth/login`, `GET /api/v1/users`, `/health`, and
      `/health/ready` all read the same way, from Stories 1.2 and 1.4. This story roughly triples
      the volume of that dead prose, which is what made it visible. Settling it means either
      wiring XML response and parameter documentation into the OpenAPI export or dropping the
      comments; both are repo-wide decisions, not this story's.
    location: >-
      src/ActionLedger.Api/Controllers/MeetingsController.cs
    severity: low
  - summary: >-
      List paging counts and windows in two separate statements, so a concurrent insert can make a
      row repeat on two pages or be skipped, despite the total-order claim.
    evidence: |-
      `MeetingsQueries.ListAsync` calls `readDb.CountAsync` and then materializes the windowed
      query; nothing holds a snapshot across the two. The order is total, so paging is stable
      against a static table, but not against a concurrent writer. Pre-existing: `UsersQueries`
      `ListAsync` has the identical shape from Story 1.4, and this story's Code Map directed that
      it be copied. Settling it means a repeatable-read transaction around both statements or
      keyset paging, applied to every list endpoint at once rather than to this one.
    location: >-
      src/ActionLedger.Application/Meetings/MeetingsQueries.cs
    severity: low
  - summary: >-
      A note containing U+0000 satisfies the published contract and both length guards, but
      PostgreSQL's character types cannot store a NUL byte, so the save may surface as a 500.
    evidence: |-
      `System.Text.Json` deserializes `"\u0000"` into a string containing NUL. `[StringLength(50_000,
      MinimumLength = 1)]` counts it as one character and `MeetingNotes.RequireText` guards length
      only, so nothing between the request body and `INSERT` refuses it — while `text` is
      `character varying(50000)`, and PostgreSQL text types reject NUL with SQLSTATE 22021. The
      intent says any 1–50,000-character text is accepted and stored byte-for-byte, which is not
      satisfiable for that one character, so the choice between refusing it with a 400 and
      transforming it is a product decision rather than a coding one. Not reproduced here: it needs
      a real database, and the existing round-trip tests cover ASCII whitespace and line endings
      only. What would settle it: a single `MeetingPersistenceTests` case attaching `"a\u0000b"`
      against the containerized PostgreSQL — if it throws, decide between a 400 and normalization;
      if it stores, the intent already holds and only the test is missing.
    location: >-
      src/ActionLedger.Domain/Meetings/MeetingNotes.cs
    severity: medium (unverified)
---

<intent-contract>

## Intent

**Problem:** Epic 1 left the solution with exactly one aggregate (`User`) and two read/write routes
(`auth/login`, `users`). Nothing in the system can hold a meeting or the text that extraction will
read, so every Epic 2 story downstream — the web meeting screens, the Fake provider, the extraction
run — has nothing to attach to.

**Approach:** Add the `Meeting` aggregate owning a write-once `MeetingNotes` child, the migration
that creates `meetings` and `meeting_notes`, and the four API operations that create a Meeting,
attach its notes exactly once, list Meetings, and read one back — all on the Epic 1 rails (AD-2
handlers and query classes, AD-12 actor from the JWT, AD-13 `/api/v1` + ProblemDetails + committed
contract, AD-20 one commit with `xmin` and unique indexes).

## Boundaries & Constraints

**Always:**

- AD-5 / ADR-002 verbatim: `MeetingNotes` has **no** update method and **no** update or delete
  endpoint. A second save is 409. The saved text is byte-for-byte what was submitted — no trim, no
  newline normalization, no re-encoding — and carries a SHA-256 of that exact text.
- AD-3: `Meeting` is an aggregate root that owns `MeetingNotes` **only**, and exposes `HasNotes`.
  Runs, proposals, and tracked actions are *not* children and arrive in later stories.
- AD-12: `createdByUserId` comes from `ICurrentUser` (the token's `sub`), never from the request.
  `ActorIntegrityTests` bans `CreatedByUserId`/`UserId` on any handler command — the command must
  not carry it.
- AD-13: routes are `meetings`, `meetings/{id}/notes` (the convention prepends `/api/v1`); errors
  are ProblemDetails with `type` in the fixed five; the list envelope is `PagedResult<T>` with the
  shared `page`/`pageSize` vocabulary; `src/ActionLedger.Web/openapi.json` is re-exported and
  committed in the same commit.
- AD-20: one `IUnitOfWork.CommitAsync` per use case, called by the handler and nobody else;
  `meetings` carries `xmin` via `UseXminAsConcurrencyToken()`; unique `meeting(title, meeting_date)`
  and `meeting_notes(meeting_id)`.
- AD-10: UUIDv7 ids from the Domain constructor, snake_case by the `ModelConventions` sweep,
  `DateOnly` → `date`, `DateTimeOffset` → `timestamptz`, `attendees` → `text[]`.
- AD-17: the migration is produced by `dotnet ef migrations add` and applied only by the bundle —
  nothing calls `Migrate()` outside `TestHost.MigrateAsync`.
- Reads are open to any authenticated User; writes need ActionOfficer or Lead. Both enum members
  are writers, so a plain `[Authorize]` is the whole rule — do **not** add a vacuous role filter.

**Never:**

- No UI. Do not touch `src/ActionLedger.Web` except the regenerated `openapi.json` (the NSwag
  client under `Core/Api/` is git-ignored and regenerates itself).
- No `ExtractionRun`, `ProposedAction`, `TrackedAction`, `ActionRevision`, outbox, or domain events
  for meetings. `runCount`/`trackedActionCount` have no source table yet and are served as `0`.
- No notes mutation path of any kind: no `PATCH`, no `DELETE`, no setter, no "replace notes" flag.
- No seeder changes (seeded meetings are Story 6.1).
- No raw SQL — AD-10 allows it in exactly two existing places, neither of them here.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Create a Meeting | `POST /api/v1/meetings` `{title, meetingDate, attendees[]}` with a valid token | 201 + `Location`, body carries `id` and `createdByUserId` equal to the token's `sub` | No error expected |
| Create with no attendees | `attendees: []` or omitted | 201; `attendees` round-trips as `[]` | No error expected |
| Create with bad input | blank/201-char title, missing date, a 101-char attendee | 400 `ValidationProblemDetails`, `type: validation` | Model validation, before the handler runs |
| Create a duplicate | same `title` + `meetingDate` as an existing Meeting | 409, `type: conflict` | `meeting(title, meeting_date)` unique violation → `ConcurrencyConflictException` |
| Attach notes | `PUT /api/v1/meetings/{id}/notes` `{text}` 1–50,000 chars, Meeting has none | 200 with notes id, SHA-256 (lower-case hex), `savedAt`; stored text is byte-identical | No error expected |
| Attach notes twice | second `PUT` on the same Meeting | 409, `type: conflict`; the first text is still what a read returns | `DomainRuleException` from `Meeting.AttachNotes`; the unique index is the concurrent backstop |
| Attach notes out of range | empty text, or 50,001 chars | 400, `type: validation` | Model validation |
| Attach notes to a missing Meeting | unknown `{id}` | 404, `type: not-found` | `NotFoundException` |
| List Meetings | `GET /api/v1/meetings?page&pageSize` | 200 `PagedResult<MeetingSummaryDto>`, ordered `meetingDate` desc, then `createdAt` desc, then `id` desc; `runCount`/`trackedActionCount` are `0` | Out-of-range page is an empty page with the true `total`, not an error |
| Read one Meeting | `GET /api/v1/meetings/{id}` | 200 with title, date, attendees, `createdByUserId`, `createdAt`, and `notes` (null when unattached) as pasted | 404 when unknown |
| Anonymous caller | any of the four operations without a token | 401, `type: unauthorized` | Handled by the auth scheme before the endpoint |

</intent-contract>

## Code Map

Read these before writing anything; every one of them is a pattern this story copies rather than
invents.

**Copy the shape from (Story 1.4's rails):**

- `src/ActionLedger.Domain/Users/User.cs` — the aggregate shape to mirror: private ctor, static
  factory, `private set` everywhere, `RequireText`-style guards throwing `DomainRuleException`,
  length constants as `public const int` so configuration and tests name one number.
- `src/ActionLedger.Domain/Common/AggregateRoot.cs:24` — `Id` is a UUIDv7 from the base ctor.
  `MeetingNotes` is a *child entity*, not a root, so it does **not** inherit this; give it its own
  `Guid.CreateVersion7()` id.
- `src/ActionLedger.Application/Auth/SignInHandler.cs` — the `<Verb><Noun>Handler` shape: one class,
  one `HandleAsync(command, ct)`, ports injected. The `SignInCommand` record at the bottom of that
  file is the DataAnnotations-on-the-command pattern the two new commands copy.
- `src/ActionLedger.Application/Users/UsersQueries.cs` — the `<Feature>Queries` shape: compose over
  `IReadDb.Query<T>()`, count before the window, the `long offset` overflow guard, total order with
  an id tiebreak, project straight into the DTO. `MeetingsQueries.ListAsync` is this file with a
  different order-by.
- `src/ActionLedger.Api/Controllers/UsersController.cs` — controller shape: `[ApiController]`,
  `[Route("meetings")]` with no prefix (the convention adds `/api/v1`), `[Authorize]`, `[Tags]`,
  `[EndpointName]`/`[EndpointSummary]`/`[EndpointDescription]` and a `[ProducesResponseType]` per
  documented status. Every attribute here shows up in the committed contract.
- `src/ActionLedger.Api/Controllers/AuthController.cs:52` — how a hand-built non-2xx response pins
  `application/problem+json`. Not needed if you let the exceptions do the work (preferred).
- `src/ActionLedger.Infrastructure/Persistence/Configurations/UserConfiguration.cs` — the
  `IEntityTypeConfiguration` shape, and the `public const string ...IndexName` so a test can name
  the index. `builder.Ignore(x => x.DomainEvents)` is required on every root.
- `src/ActionLedger.Infrastructure/Persistence/UserRepository.cs` — repository shape: `Add`, find
  methods, **never** save.
- `src/ActionLedger.Application/Abstractions/IUserRepository.cs` — the port doc-comment tone and the
  "no Update/Attach" rule to restate for `IMeetingRepository`.

**Must be edited (registration and wiring):**

- `src/ActionLedger.Infrastructure/Persistence/AppDbContext.cs:21` — add `DbSet<Meeting> Meetings`.
  The comment at `:44` explicitly lists `Meeting` as an `xmin` root and says the configuration that
  arrives with its epic sets it — that is this story; update that comment as you satisfy it.
- `src/ActionLedger.Application/ApplicationRegistration.cs:26` — register the two handlers and
  `MeetingsQueries`.
- `src/ActionLedger.Infrastructure/InfrastructureRegistration.cs:57` — register `IMeetingRepository`.

**Read-only evidence (tests that will judge the change):**

- `tests/Api.Tests/AuthDisciplineTests.cs:37` — walks every documented operation and asserts 401
  for an anonymous caller; `Concrete()` at the bottom already substitutes `Guid.Empty` for `{id}`,
  so the new parameterized routes are covered automatically.
- `tests/Api.Tests/RouteDisciplineTests.cs:22` — every mapped route must sit under `/api/v1`.
- `tests/Api.Tests/OpenApiSnapshotTest.cs:17` — byte-compares the generated document against
  `src/ActionLedger.Web/openapi.json`. This goes red until you re-export.
- `tests/Architecture.Tests/ActorIntegrityTests.cs:33` — the banned-member list; `CreatedByUserId`
  is on it, so it may appear on a **DTO** but never on a command.
- `tests/Architecture.Tests/DependencyRuleTests.cs` — Domain may reference no package at all.
  `System.Security.Cryptography.SHA256` is BCL, so hashing inside Domain is legal.
- `tests/Infrastructure.Tests/SchemaShapeTests.cs` — the `information_schema` / `pg_indexes` probe
  helpers to copy for the two new tables.
- `tests/Infrastructure.Tests/PostgresFixture.cs` + `TestHost.cs` — real PostgreSQL 18 per
  assembly, one database per test, `TestHost.MigrateAsync` applies the migrations.
- `tests/Infrastructure.Tests/RecordingCommandInterceptor.cs` — records real SQL, for the AD-10
  "inserts only" assertion.
- `tests/Api.Tests/TestApi.cs:38` — `ValidConfiguration()` supplies a connection string pointing at
  `localhost` with no database behind it. `Api.Tests` therefore cannot exercise a query that
  actually hits PostgreSQL; **persistence behaviour belongs in `Infrastructure.Tests`**, and
  `Api.Tests` covers routing, contract, status codes, validation, and authorization.

**Tooling:**

- `.config/dotnet-tools.json` — `dotnet-ef` 10.0.12 is restored as a local tool.
- The SDK pinned by `global.json` (10.0.401) lives at `~/.dotnet` and is not on `PATH`; prefix
  commands with `export PATH="$HOME/.dotnet:$PATH"`.

## Tasks & Acceptance

**Execution:**

- `src/ActionLedger.Domain/Meetings/Meeting.cs` -- add the `Meeting` aggregate root: `Title`
  (1–200, trimmed), `MeetingDate` (`DateOnly`), `Attendees` (`IReadOnlyList<string>`, 0..n, each
  1–100, trimmed), `CreatedByUserId`, `CreatedAt`, plus `Notes` (nullable child) and
  `bool HasNotes => Notes is not null`. Static `Meeting.Create(title, meetingDate, attendees,
  createdByUserId, createdAt)`; instance `AttachNotes(string text, DateTimeOffset savedAt)` that
  throws `DomainRuleException` when `HasNotes`. No update or clear method for notes, ever.
  -- AD-3/AD-5: the aggregate is the only place the write-once rule can be made unforgettable.
- `src/ActionLedger.Domain/Meetings/MeetingNotes.cs` -- add the owned child: `Id` (UUIDv7),
  `MeetingId`, `Text` (stored verbatim — guard length only, never trim), `Sha256` (lower-case hex
  of the UTF-8 bytes of `Text`, computed here), `SavedAt`. `internal`/`private` constructor so only
  `Meeting.AttachNotes` can make one; no public mutator of any kind.
  -- ADR-002: byte-for-byte storage plus the hash that makes tampering detectable.
- `src/ActionLedger.Application/Abstractions/IMeetingRepository.cs` -- add the port: `Add(Meeting)`,
  `Task<Meeting?> FindByIdAsync(Guid, CancellationToken)` (loading `Notes` with it, so
  `AttachNotes` can see `HasNotes`). No `Update`, no `Attach`, no save.
  -- AD-10 repository discipline, mirrored from `IUserRepository`.
- `src/ActionLedger.Application/Meetings/CreateMeetingHandler.cs` -- add the handler and its
  `CreateMeetingCommand` record (DataAnnotations: `Title` required/`StringLength(200)`,
  `MeetingDate` required, `Attendees` a `string[]` with a per-element 1–100 rule). Handler takes
  `IMeetingRepository`, `ICurrentUser`, `IClock`, `IUnitOfWork`; builds the aggregate with the
  actor and `clock.UtcNow`, adds it, commits once, returns `MeetingCreatedDto(Id, CreatedByUserId)`.
  -- AD-2 + AD-12: the command must not expose a place to put an actor id.
- `src/ActionLedger.Application/Meetings/SaveMeetingNotesHandler.cs` -- add the handler and its
  `SaveMeetingNotesCommand` (`Text` required, `StringLength(50_000, MinimumLength = 1)`). Loads the
  Meeting or throws `NotFoundException`, calls `AttachNotes`, commits once, returns
  `MeetingNotesDto`. -- FR-2: exactly-once attach, 404 vs 409 kept distinct.
- `src/ActionLedger.Application/Meetings/MeetingDtos.cs` -- add `MeetingSummaryDto(Id, Title,
  MeetingDate, RunCount, TrackedActionCount)`, `MeetingDetailDto(Id, Title, MeetingDate,
  Attendees, CreatedByUserId, CreatedAt, MeetingNotesDto? Notes)`, `MeetingNotesDto(Id, Text,
  Sha256, SavedAt)`, `MeetingCreatedDto(Id, CreatedByUserId)`. `RunCount`/`TrackedActionCount` are
  `int` and are `0` for now — document in XML doc that the source tables arrive in Stories 2.5/3.1
  and only the projection changes.
  -- AD-13: DTOs are the only shapes that cross the boundary, and the envelope is fixed now so the
  generated web client does not change shape when the counts become real.
- `src/ActionLedger.Application/Meetings/MeetingsQueries.cs` -- add `ListAsync(page, pageSize, ct)`
  (order `MeetingDate` desc, `CreatedAt` desc, `Id` desc; `Paging.Normalize`; count before window;
  the same `long offset` guard as `UsersQueries`) and `GetAsync(id, ct)` returning
  `MeetingDetailDto` or throwing `NotFoundException`.
  -- AD-2: reads are query classes over `IReadDb`, never handlers.
- `src/ActionLedger.Infrastructure/Persistence/Configurations/MeetingConfiguration.cs` -- configure
  `meetings`: key, `Title` required max 200, `MeetingDate`, `Attendees` (leave the provider's
  `text[]` mapping; only set required), `CreatedByUserId`, `CreatedAt`, `UseXminAsConcurrencyToken()`,
  unique index `(Title, MeetingDate)` named by a `public const string`, `Ignore(DomainEvents)`, and
  the owned/one-to-one navigation to `MeetingNotes` with `meeting_notes`: `Text` required (max
  50,000), `Sha256` required fixed length 64, `SavedAt`, unique index on `MeetingId` named by a
  `public const string`, cascade from the Meeting.
  -- AD-20 index list, verbatim.
- `src/ActionLedger.Infrastructure/Persistence/AppDbContext.cs` -- add `DbSet<Meeting> Meetings`
  and amend the AD-20 comment that currently defers `Meeting`'s `xmin` to "its epic".
  -- one context, one place a write becomes SQL.
- `src/ActionLedger.Infrastructure/Persistence/MeetingRepository.cs` -- implement
  `IMeetingRepository` over `AppDbContext`, including the `Notes` navigation in `FindByIdAsync`.
  -- mirrors `UserRepository`; no save.
- `src/ActionLedger.Infrastructure/Migrations/<timestamp>_AddMeetings.cs` -- generate with
  `dotnet ef migrations add AddMeetings`; commit the migration, its designer file, and the updated
  `AppDbContextModelSnapshot.cs` unedited. -- AD-17: schema ships only through the bundle.
- `src/ActionLedger.Infrastructure/InfrastructureRegistration.cs` and
  `src/ActionLedger.Application/ApplicationRegistration.cs` -- register `IMeetingRepository`,
  `CreateMeetingHandler`, `SaveMeetingNotesHandler`, `MeetingsQueries`.
  -- each ring registers its own types; nothing is discovered by reflection.
- `src/ActionLedger.Api/Controllers/MeetingsController.cs` -- add the four operations with full
  OpenAPI annotation: `POST ""` → 201 `CreatedAtAction` at the detail route; `PUT "{id}/notes"` →
  200; `GET ""` → 200 `PagedResult<MeetingSummaryDto>`; `GET "{id}"` → 200 `MeetingDetailDto`.
  Declare `[ProducesResponseType]` for 400/401/404/409 exactly where each can occur. No `PATCH`,
  no `DELETE`. -- AD-13: the annotations *are* the contract.
- `src/ActionLedger.Web/openapi.json` -- regenerate with
  `dotnet run --project src/ActionLedger.Api -- --export-openapi` and commit the result in the same
  commit. -- `OpenApiSnapshotTest` fails until this is done.
- `tests/Domain.Tests/Meetings/MeetingTests.cs` -- cover the Domain guards: title/attendee length
  and blank rejection, trimming of title and attendee names, notes text left byte-identical
  (leading/trailing whitespace and CRLF survive), the SHA-256 value against a known vector,
  `HasNotes` transition, and `AttachNotes` twice throwing `DomainRuleException`.
  -- these are the invariants nothing above the Domain may restate.
- `tests/Application.Tests/Meetings/MeetingsTests.cs` -- cover both handlers and both queries over
  fakes: actor comes from `ICurrentUser` and timestamps from `IClock`; exactly one `CommitAsync`
  per use case; `NotFoundException` on an unknown id; list ordering, the paging clamp, and the
  past-the-end empty page reporting the true total.
  -- AD-2/AD-12/AD-20 at the use-case level.
- `tests/Api.Tests/MeetingsEndpointTests.cs` -- cover every row of the I/O matrix that is decided
  above persistence: validation 400s (blank title, 201-char title, 101-char attendee, empty notes,
  50,001-char notes), the route shapes, and the `ProblemDetails` `type` on each failure.
  -- the matrix's edge cases, asserted where they are actually decided.
- `tests/Api.Tests/NotesImmutabilityTests.cs` -- walk the generated OpenAPI document and assert:
  no `DELETE` or `PATCH` operation exists anywhere under `/api/v1/meetings`, and the only operation
  on a path ending `/notes` is the single `PUT`. Assert the walk found the `PUT` so it cannot pass
  vacuously. -- this is the AC's "`Api.Tests` proves no endpoint updates or deletes notes".
- `tests/Infrastructure.Tests/MeetingPersistenceTests.cs` -- against real PostgreSQL: the migration
  creates `meetings` and `meeting_notes` with the expected snake_case columns, `attendees` as
  `ARRAY`, `meeting_date` as `date`, `created_at`/`saved_at` as `timestamp with time zone`; both
  unique indexes exist by name; a duplicate `(title, meeting_date)` and a second notes row each
  surface as `ConcurrencyConflictException`; a Meeting with notes persists as inserts only
  (`RecordingCommandInterceptor`); and notes text round-trips byte-for-byte including CRLF and
  trailing whitespace. -- the schema row of the AC, proven where the database is real.

**Acceptance Criteria:**

- Given a signed-in Action Officer, when they `POST /api/v1/meetings` with a valid title, date, and
  attendee list, then the response is 201 carrying the new Meeting id and a `createdByUserId` equal
  to the `sub` claim of the token that was sent — not to any value in the request body.
- Given a Meeting that already has notes, when a second `PUT /api/v1/meetings/{id}/notes` arrives,
  then the response is 409 with `type: conflict` and a subsequent `GET /api/v1/meetings/{id}`
  returns the originally saved text unchanged.
- Given the committed contract, when `Api.Tests` walks it, then no `DELETE` or `PATCH` operation
  exists under `/api/v1/meetings` and the only `/notes` operation is the write-once `PUT`.
- Given several Meetings, when `GET /api/v1/meetings` is called, then the page is ordered by
  meeting date descending and then creation time descending, carries `runCount` and
  `trackedActionCount` on every item, and reports a `total` counted across all pages.
- Given the solution after this change, when `dotnet test` runs, then `OpenApiSnapshotTest`,
  `RouteDisciplineTests`, `AuthDisciplineTests`, `ActorIntegrityTests`, and `DependencyRuleTests`
  are all green without any of them being edited.

## Spec Change Log

- **`UseXminAsConcurrencyToken()` does not exist on Npgsql 10.** The method was removed after
  Npgsql 8; `NpgsqlEntityTypeBuilderExtensions` on 10.0.3 has no member by that name. The AD-20
  token is set the way the current provider spells it — a `uint` shadow property named `xmin`,
  `IsRowVersion()` — which `NpgsqlConcurrencyTokenConvention` binds to PostgreSQL's system column.
  `MeetingConfiguration.ConcurrencyTokenProperty` names it so a test can, and
  `MeetingPersistenceTests.The_concurrency_token_is_the_system_column_not_one_the_migration_added`
  asserts `meetings` has no user column called `xmin` — a real one would have failed the
  `CREATE TABLE` outright, so this is the assertion that says the token is bound rather than
  shadowing.
- **`MeetingNotes` is configured as an owned type, not a second root.** The task list allowed
  "owned/one-to-one"; owned is the one that makes AD-3 structural. Owned navigations load with the
  root, so `Meeting.HasNotes` is answered from the database on every path — `FindByIdAsync` needs
  no `Include`, and a later caller cannot reintroduce the "second save succeeded because nobody
  loaded the navigation" bug. Cascade comes with it.
  `MeetingPersistenceTests.A_meeting_loads_with_its_notes_so_the_write_once_rule_can_see_them`
  pins it.
- **`CreateMeetingCommand.MeetingDate` is `DateOnly?`.** `[Required]` on a non-nullable `DateOnly`
  always passes, so an omitted date would have become `0001-01-01` rather than the matrix's 400.
  The handler turns a null into a `DomainRuleException`, which HTTP can never reach because
  `[ApiController]` answers 400 first; it is what keeps a direct call honest.
- **`AttendeeNamesAttribute` added.** Not in the task list, but the matrix's "a 101-char attendee →
  400" has no other home: `[StringLength]` on a `string[]` measures the array, not its elements, so
  an overlong attendee would have reached the Domain and come back as a 409. The attribute lives
  beside the command it validates and takes no package —
  `System.ComponentModel.DataAnnotations` is in the shared framework, as `SignInCommand` already
  established.
- **`SaveMeetingNotesHandler.HandleAsync` takes the meeting id as its own parameter.** The id is a
  route value, not part of the body, so putting it on the command would have published a
  `meetingId` in the request schema that the route already carries and the server would ignore.
  `ActorIntegrityTests` inspects only parameter types from the Application assembly, so a bare
  `Guid` parameter is outside its rule and inside AD-12's intent.
- **`Meeting.Create` refuses `Guid.Empty` as the creator.** Not asked for. AD-12 says every write
  records who made it; a Meeting whose `created_by_user_id` is all zeroes records nobody, and the
  aggregate is the only place that can make that impossible.
- **`MeetingTests.Nothing_on_the_aggregate_can_change_or_clear_notes` added.** ADR-002 is a shape
  claim as much as a behaviour one. The behavioural tests prove a second `AttachNotes` throws; this
  one walks the public surface of `Meeting` and `MeetingNotes` by reflection and fails if any
  public setter, or any method naming Notes other than `AttachNotes`, ever appears. Without it the
  rule is a convention that the next story could quietly break with a `ReplaceNotes`.
- **`NotesImmutabilityTests` carries a third assertion.** Beyond "no DELETE or PATCH" and "the only
  `/notes` operation is the PUT", it pins the published set to exactly the four operations this
  story adds — so a fifth Meeting operation has to be a deliberate edit to this test rather than a
  silent addition to the contract.
- **`MeetingPersistenceTests` also drives `MeetingsQueries` against the real database.** The
  Application-ring fake is LINQ to Objects, which happily "translates" anything. The detail
  projection has two shapes only a provider can refuse — the conditional over an owned navigation
  and the `text[]` column read into an `IReadOnlyList<string>` — so the list ordering, the detail
  projection with and without notes, and the `NotFoundException` are all re-asserted where the SQL
  is real. `MeetingsQueries` is constructed over the registered `IReadDb` rather than resolved,
  because the Infrastructure test host deliberately does not call `AddActionLedgerApplication`.
- **`tests/Web.Tests/StubApiClient.cs` updated.** Not in the task list, and the only file outside
  the Api/Application/Domain/Infrastructure rings this story touches. The stub implements the
  generated `IActionLedgerApiClient`, which grew four members the moment the contract was
  re-exported, so the solution would not compile without it. The four throw with a message naming
  Story 2.2, exactly as the two health operations already throw — no UI was added.
- **`Attendees` maps itself.** No value converter and no backing-field configuration were needed:
  EF Core reads `IReadOnlyList<string>` as a primitive collection and Npgsql maps it to `text[]`,
  which is what the generated migration and `The_migration_creates_meetings_with_snake_case_columns`
  both show.
- **`tests/Api.Tests/MeetingsSuccessResponseTests.cs` added during the matrix test audit.** The
  matrix's first row asks for "201 + `Location`", and nothing asserted either: the Application
  tests stop at the handler's return value, and `Api.Tests` could not reach a successful write
  because `TestApi`'s connection string points at no database. `CreatedAtAction` resolves the
  detail route by action name at request time, so a renamed action would have turned every create
  into a 500 with a green suite behind it. `TestApi` grew a `ReplaceServices` hook that swaps the
  three persistence ports for in-memory fakes; everything above them — routing, model binding,
  `ClaimsPrincipalCurrentUser`, the result executor — stays what the host wires up, which is also
  what makes the `createdByUserId` assertion read the token rather than a stub. Three tests: the
  201 with a `Location` that is then followed and serves the same Meeting, the attribution under a
  body that tries to name a different author, and the 200 from a save whose text round-trips
  byte-for-byte through the detail read.

## Review Triage Log

### 2026-09-21 — Review pass

- verdicts: 26 findings — high 0, medium 6, low 15, false 5, maybe-false 0
- findings:
  - `[medium]` `[patch]` `AppDbContext`'s amended AD-20 comment prescribes `UseXminAsConcurrencyToken()` for the next three epics — Confirmed: zero occurrences of that name in `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3`. The comment was the one place the removal was written down, and it wrote down the wrong thing. Patched: the comment now names `builder.Property<uint>("xmin").IsRowVersion()` and records the old spelling as gone.
  - `[medium]` `[patch]` The per-element attendee rule never reaches the published contract — Confirmed against the exported document: `attendees` was `{"type":"array","items":{"type":"string"}}` while `title` and notes `text` both carried their bounds. Patched: a `DescribeCollectionElementBoundsAsync` schema transformer keyed on `AttendeeNamesAttribute`; `attendees.items` now publishes `minLength: 1, maxLength: 100`.
  - `[low]` `[reject]` Nothing bounds the number of attendees — Real but rejected as low: FR-1 says "zero or more names" and sets only a per-name bound, the route is authenticated, and the blast radius is capped by Kestrel's 30 MB body limit. The fix adds a new public constant, a validation rule, and a Domain guard against a case never demonstrated reachable in everyday use.
  - `[low]` `[patch]` `StringLength` error messages say "cannot exceed N" for a minimum-length violation and restate the constant in prose — Confirmed: an empty title was told it was too long. Patched: both messages are now `"... must be between {2} and {1} characters."`, which renders from the constants.
  - `[low]` `[patch]` Whitespace-only notes are refused by `[Required]` but accepted by the Domain, and the boundary is untested — Grouped with the verification-gap finding below; same root cause and same fix. The layering is deliberate (the Domain guards length, the API refuses blank), so the "contradiction" half is not a defect; the untested half is.
  - `[medium]` `[patch]` The second-`PUT` 409 is asserted at the handler and against the database but never at the HTTP surface the acceptance criterion names — Confirmed: no Api test reached a successful write before this pass. Patched: `MeetingsSuccessResponseTests.A_second_save_is_a_409_conflict_and_the_detail_read_still_carries_the_first_text`.
  - `[low]` `[reject]` The 201's `Location` header is absent from the contract — Confirmed absent (`responses.201` has only `description` and `content`). Rejected as low: Story 2.2 navigates from the `id` in the body, the header is asserted end-to-end by test, and declaring it needs a transformer rather than a direct correction.
  - `[low]` `[defer]` `<response code="...">` doc comments export as bare reason phrases — Confirmed, and pre-existing: `/auth/login`, `/users`, and both health routes are identical. Not caused by this story.
  - `[false]` `[reject]` `Meeting.Attendees` is a mutable `List<string>` behind `IReadOnlyList<string>` — Refuted empirically: the collection expression compiles to `<>z__ReadOnlyList<string>`, and `((List<string>)meeting.Attendees).Add(...)` throws `InvalidCastException`. Probe run on SDK 10.0.401.
  - `[low]` `[reject]` No index supports the list query's sort — Real, but the spine's Indexes row enumerates the non-unique indexes for this system and `meetings` is deliberately not among them; at demo data volumes the scan is not something a user meets. The fix edits a committed migration.
  - `[low]` `[reject]` `sha256` is `character(64)` rather than `text` — `bpchar` padding semantics never engage for a value that is always exactly 64 characters. The fix means changing a committed migration for no observable behaviour change.
  - `[low]` `[reject]` Both 409s are indistinguishable to a client — True, and by design: the Errors convention maps `DomainRuleException` and `ConcurrencyConflictException` to one status and one slug. A retrying client can settle it with the detail read. The proposed fix adds a new ProblemDetails extension, which is public surface.
  - `[low]` `[patch]` Whitespace-only notes are a 400 the Domain would have accepted (edge-case layer) — Same entry as the verification-gap finding; see the patch recorded there.
  - `[low]` `[reject]` A 200-character title surrounded by whitespace is a 400 though `Create` would trim it to 200 — Real and rejected as low: it needs a title at exactly the limit plus stray whitespace, and the fix is a custom trimming validator.
  - `[low]` `[reject]` Unbounded attendee array (edge-case layer) — Same entry as the blind-hunter finding above; rejected for the same reason.
  - `[false]` `[reject]` `AttendeeNamesAttribute` returns `Success` when the value is not `IEnumerable<string>` — The property is declared `string[]`, so the non-match branch is unreachable; the bad outcome does not occur at the cited location.
  - `[low]` `[defer]` `CountAsync` and the windowed query are not atomic, so a concurrent insert can repeat or skip a row across pages — Real, and pre-existing: `UsersQueries.ListAsync` has the identical shape from Story 1.4, which this story was directed to copy.
  - `[false]` `[reject]` Titles differing only in case both insert, defeating the unique index — Not a defect: no acceptance criterion or FR asks for case-insensitive meeting titles. `User.Username` is lower-cased deliberately because it is an identifier; a meeting title is display text.
  - `[medium]` `[patch]` Attendee bounds missing from the contract (edge-case layer) — Same entry as the blind-hunter finding; see the patch recorded there.
  - `[false]` `[reject]` A `sub` claim of `Guid.Empty` yields 409 instead of 401 — Unreachable: `JwtAccessTokenIssuer` only ever writes `user.Id`, which is a UUIDv7, and minting a token with any other subject needs the signing key.
  - `[low]` `[patch]` `MeetingNotes`' remarks claim "tampering with a row is detectable" while the digest sits unsigned in the same row — Confirmed over-claim. Patched: the remarks now say what the hash does provide — a stable short identity for an exact byte sequence, plus detection of accidental corruption.
  - `[low]` `[reject]` The spec's task list says `UseXminAsConcurrencyToken()` but the code does otherwise — Rejected: the only fix is to edit this build's spec, and the Spec Change Log already records the deviation.
  - `[false]` `[reject]` `FindByIdAsync` has no `.Include(m => m.Notes)` despite the spec wording — Not a defect: `MeetingNotes` is an owned type, so EF Core loads it with the root. That is the stronger guarantee, and the repository's own remarks explain it.
  - `[medium]` `[patch]` The AD-20 `xmin` token is pinned only by an absence assertion (pre-verified, verification-gap layer) — Filed disposition `patch`, accepted. Patched: the test now resolves the model and asserts the property exists, `IsConcurrencyToken`, is `ValueGenerated.OnAddOrUpdate`, and maps to column `xmin`, keeping the absence check. The implementer noted the original demonstration was imprecise — deleting the configuration also trips EF 10's `PendingModelChangesWarning` — but the missing positive assertion was real either way.
  - `[medium]` `[patch]` Whitespace-only notes are refused only by `[Required]` and nothing asserts it (pre-verified, verification-gap layer) — Filed disposition `patch`, accepted; groups the two edge-case reports above. Patched: the notes theory is now `Notes_that_are_absent_empty_or_only_whitespace_are_a_400_validation` with `"   "` and `"\t\r\n"` added. Mutation-checked: removing `[Required]` fails exactly the two new cases.
  - `[low]` `[reject]` The intent's bookkeeping expectations live in spec frontmatter and `sprint-status.yaml`, which no test observes, and the spec's Verification claimed `dotnet test` was unusable — Rejected as a finding: the only fix is to edit this build's spec. Acted on anyway during finalization, because the claim was false — `dotnet test` runs all 479 tests here, and the Verification section now records the correction.

### 2026-09-21 — Review pass (follow-up)

- verdicts: 48 findings — high 0, medium 9, low 30, false 7, maybe-false 2
- findings:
  - `[low]` `[reject]` `meetings.created_by_user_id` has no foreign key to `users`, so a Meeting can name a User that never existed — Real absence, confirmed in the migration: the only `table.ForeignKey` is `meeting_notes` → `meetings`. Rejected as low: the column is only ever written from `ICurrentUser.UserId`, `JwtAccessTokenIssuer` only ever mints `user.Id`, and there is no user-delete path, so no route to an orphan id was demonstrated. AD-20's constraint list is verbatim in the intent and names the two unique indexes only, and the fix edits a committed migration.
  - `[medium]` `[patch]` `CreateMeetingCommand.meetingDate` publishes as `["null","string"]` while sitting in `required`, so a contract-conforming `null` is always a 400 — Confirmed in the exported document. Patched: a `DescribeRequiredMembersAsNonNullableAsync` schema transformer drops the null bit from any `[Required]` member; `meetingDate` now publishes `"type": "string"`. Re-exported; the change is that one property and nothing else. Pinned by `OpenApiContractTests.A_required_member_does_not_publish_itself_as_nullable`, mutation-checked by removing the transformer.
  - `[low]` `[reject]` The published `attendees.items` bounds and the attribute disagree about trimming in both directions — Real: `IsValid` measures `attendee.Trim().Length`, so a 100-character name with trailing spaces passes the server and fails a client validating `maxLength: 100`; and `"   "` satisfies `minLength: 1` while the server refuses it as blank. Rejected as low: both divergences are in the safe direction (one party is stricter, nothing is stored wrong), the inputs need deliberate padding at the exact limit, and the fix is either a behaviour change or a new `pattern` on the contract.
  - `[low]` `[reject]` `AttendeeNamesAttribute.MinimumLength` is never read by `IsValid` — True as stated, but the published value `1` is enforced, and more strictly: `IsNullOrWhiteSpace` refuses both `""` and whitespace. Rejected as low — only a hypothetical different value would go unenforced, and the property is `=> 1`.
  - `[low]` `[patch]` `DescribeCollectionElementBoundsAsync` claims it closes the gap "for every collection whose elements are bounded that way" while keying on the one concrete attribute — Grouped with the verification-gap rationale finding below; same root cause (prose around this attribute overstating what it does) and same fix.
  - `[medium]` `[patch]` Nothing calls `GET /api/v1/meetings` over HTTP, so AC4's surface is unverified — Grouped with the verification-gap finding below; same root cause and same fix.
  - `[medium]` `[patch]` The 404 is declared on both routes and the matrix names it twice, and no test reaches it over HTTP — Confirmed: coverage stops at `NotFoundException` in `SaveMeetingNotesHandler` and `MeetingsQueries.GetAsync`, and the two 401 theories never enter the action. Patched: `MeetingsSuccessResponseTests.Reading_a_meeting_that_does_not_exist_is_a_404_not_found` and `.Saving_notes_on_a_meeting_that_does_not_exist_is_a_404_not_found`, both asserting the status, the `problem+json` media type, and the `not-found` slug.
  - `[low]` `[reject]` `meeting_notes` records no `saved_by_user_id` — Real absence, and the backfill argument is sound: a write-once row cannot gain attribution later. Rejected as low against this story's intent, which fixes the notes shape through its I/O matrix — the `PUT` response is "notes id, SHA-256, `savedAt`" and the detail read carries the text "as pasted", neither naming an actor — and the Meeting that owns the notes already records its creator. Recorded here rather than deferred because the intent settles the shape; a later story that wants notes attribution is adding a field, not fixing this one.
  - `[low]` `[patch]` `NotesImmutabilityTests` filters the contract to the `/api/v1/meetings` prefix while its remarks claim any later controller is covered — Confirmed: a `DELETE /api/v1/notes/{id}` would pass all three assertions untouched. Patched: the walk now also takes any path naming notes wherever it is routed, and the remarks say exactly what is covered.
  - `[medium]` `[patch]` `MeetingTests.Nothing_on_the_aggregate_can_change_or_clear_notes` inspects only `Meeting` methods whose name contains "Notes" — Grouped with the verification-gap finding below; same root cause and same fix.
  - `[maybe-false]` `[defer]` Nothing tests non-ASCII notes, and one constant named 50,000 stands for UTF-16 units, code points, and UTF-8 bytes — The length half is refuted: `text.Length` counts UTF-16 units, which is always at least the code-point count, so passing the guard implies fitting `character varying(50000)`. What survives is U+0000, which the contract accepts and PostgreSQL's text types cannot store. Deferred at medium (unverified) with what would settle it — the claim needs a real database this pass did not put it in front of.
  - `[low]` `[patch]` The attendee limit is tested only on the reject side, so narrowing the guard to `>=` would keep the suite green — Confirmed: title and notes each assert the limit is accepted, attendees only that the limit plus one is refused. Patched: `An_attendee_name_of_exactly_the_limit_is_accepted`.
  - `[medium]` `[patch]` The premise that makes a bare `[Authorize]` the whole write rule — that both `Role` members are writers — lives only in a comment — Grouped with the intent-alignment finding below; same root cause and same fix.
  - `[low]` `[patch]` `Notes_round_trip_byte_for_byte` names its database with `string.GetHashCode`, which is salted per process — Confirmed: every run created a differently-named database, and a negative hash put a hyphen in the identifier. Patched with a truncated SHA-256 of the input; the first attempt used `pasted.Length` and collided, because two theory rows are both 24 characters — caught by the suite and replaced.
  - `[low]` `[reject]` The Verification table reports 472 tests while the correction below it reports 479, and claims `git diff --stat` shows no `sprint-status.yaml` change — Both real discrepancies in the document. Rejected: the only fix is to edit this build's spec. The current counts are recorded under `## Auto Run Result` instead.
  - `[low]` `[reject]` Story bookkeeping is in three states at once (board `done`, spec `in-review`, `epic-2: backlog`) — Not a defect to fix here: `sprint-status.yaml` is the orchestrator's, `status: in-review` is what this pass sets while it runs, and the epic row is the orchestrator's too. Same ground as the previous pass's final row.
  - `[low]` `[defer]` `carried` — `MeetingsController`'s `<param name="pageSize">` hard-codes "200" while the published component interpolates `Paging.MaxPageSize`, and the prose is replaced by a `$ref` so it never reaches the contract — Same root cause as the logged DW-5 row: doc-comment prose that reads as contract documentation and is dead on export. `UsersController` carries the identical text from Story 1.4, so this story copied a pre-existing pattern. Keeping the logged verdict and route; not patched or re-deferred.
  - `[low]` `[reject]` Nothing bounds `MeetingDate`, so `0001-01-01` or `9999-12-31` is accepted — Grouped with the edge-case finding below; rejected for the same reason.
  - `[low]` `[reject]` `A_meeting_and_its_notes_persist_as_inserts_only` attaches notes before the Meeting was ever saved, so it never covers the path `PUT {id}/notes` takes — Real coverage asymmetry. Rejected as low: the second commit inserts a `meeting_notes` row and touches no `meetings` column, and `xmin` is database-generated and never written by EF, so there is no `UPDATE` for the assertion to catch; the interceptor test would be asserting the absence of something the provider cannot emit.
  - `[low]` `[reject]` `carried` — Nothing bounds the number of attendees — Logged last pass as low and rejected; `AttendeeNamesAttribute` still validates element length only and the column is still an unbounded `text[]`. Keeping that verdict and route.
  - `[low]` `[reject]` Duplicate attendee names are not collapsed — Real, and intended: no FR or acceptance criterion asks for a distinct roster, and storing the names as submitted is what the rest of the story is about. The fix would silently discard user input.
  - `[medium]` `[patch]` `meetingDate` is published nullable while `[Required]` refuses null — Same entry as the blind-hunter finding above; see the patch recorded there.
  - `[low]` `[reject]` The published attendee `maxLength` measures the raw string while the attribute measures after trimming — Same entry as the blind-hunter trimming finding above; rejected for the same reason.
  - `[low]` `[reject]` The published `minLength: 1` does not express `[Required]`'s rejection of whitespace — Same entry as the blind-hunter trimming finding above; rejected for the same reason.
  - `[false]` `[reject]` `carried` — `AttendeeNamesAttribute.IsValid` returns `Success` for a value that is not `IEnumerable<string>` — Logged last pass as false; the property is still declared `string[]`, so the branch is still unreachable. Keeping that verdict.
  - `[low]` `[reject]` `DescribeCollectionElementBoundsAsync` has no branch for a null `items` schema — The attribute is only applied to `string[]`, and no route to a non-array property carrying it was demonstrated. Rejected as low: the fix adds a branch or a test for a case never shown reachable.
  - `[false]` `[reject]` `carried` — Titles differing only in case both insert, defeating the unique index — Logged last pass as false: no criterion asks for case-insensitive titles, and a meeting title is display text, unlike `User.Username`. Keeping that verdict.
  - `[low]` `[reject]` `MeetingDate` has no range guard — Real: `Meeting.Create` guards title, attendees, and the actor, never the date. Rejected as low: backdated and future meetings are both legitimate, no bound appears anywhere in the intent, and an explicitly typed `0001-01-01` is user error rather than a system defect. The fix adds a new public constant and a guard against a case never demonstrated in everyday use — the same ground the attendee-count bound was rejected on.
  - `[low]` `[reject]` The concurrent notes 409 carries the raw index name instead of the write-once explanation — Confirmed: `ConcurrencyTranslation` emits "That value is already taken (ix_meeting_notes_meeting_id)." Rejected as low and pre-existing: that file is not in this diff, it has produced the same sentence for `ix_users_username` since Story 1.4, and the previous pass already settled the adjacent complaint that both 409s are indistinguishable. The fix adds a constraint-name-to-message map.
  - `[false]` `[reject]` The claim that the hash makes tampering detectable — Refuted at the cited location: `MeetingNotes`' remarks say the opposite in full ("It is not tamper-evidence. The digest is unsigned and lives in the same `meeting_notes` row…"), which the previous pass patched. The stale wording is in this spec's own task list, and editing that is out of bounds.
  - `[low]` `[reject]` `carried` — The claim that the token is configured with `UseXminAsConcurrencyToken()` — Logged last pass and rejected because the only fix is to edit this build's spec; the Spec Change Log records the deviation and `AppDbContext`'s comment was corrected. Keeping that verdict and route.
  - `[medium]` `[patch]` The `GET /api/v1/meetings` action body is never executed by any test, so its `page`/`pageSize` wiring is unverified (pre-verified, verification-gap layer) — Filed disposition `patch`, accepted; groups the blind-hunter finding above. Confirmed the demonstration: swapping the two forwarded arguments left every suite green. Patched: `MeetingsSuccessResponseTests.The_list_pages_with_the_window_the_query_string_asked_for` asserts the envelope, the ordering, the zero counts, and a one-row second page. Mutation-checked — the swapped-argument version now fails exactly that test.
  - `[medium]` `[patch]` The ADR-002 write-once shape guard matches on member names, so a notes mutator not named "Notes" passes it (pre-verified, verification-gap layer) — Filed disposition `patch`, accepted; groups the blind-hunter finding above. Patched: the `Meeting` branch is now an allowlist over what `Meeting` itself declares, matching how the `MeetingNotes` branches always worked. The first version allowlisted `object`'s members and correctly caught inherited `ClearDomainEvents`; scoping to `DeclaringType` is what fixed that. Mutation-checked with the reviewer's own `public void Overwrite(string, DateTimeOffset)`, which the test now names.
  - `[low]` `[patch]` The rationale for `AttendeeNamesAttribute` is stated inaccurately in three places — Confirmed: `[StringLength].IsValid` casts to `string`, so on a `string[]` it throws `InvalidCastException` rather than measuring the array. The decision to use a custom attribute is right and the stated reason was wrong. Patched in all three places, and the transformer's remarks now say it covers the one attribute it keys on rather than "every collection".
  - `[low]` `[reject]` `carried` — Nothing bounds the number of attendees (verification-gap other-findings) — Same entry as the edge-case and blind-hunter reports; keeping the logged verdict and route.
  - `[false]` `[reject]` AD-20's `xmin` is pinned as configuration and never as behaviour; no test updates a `meetings` row against a stale token — Accurate as description, not a defect: this story adds no path that updates a `meetings` row, so there is no concurrent update for a behavioural test to perform. The previous pass already strengthened the metadata assertion.
  - `[low]` `[reject]` `carried` — Whitespace-only notes are answered differently by the Domain and the HTTP surface — Logged last pass, which judged the layering deliberate and patched the untested half. Both still read that way. Keeping that verdict and route.
  - `[low]` `[reject]` The duplicate-create 409 is never observed at the HTTP surface its matrix row describes — Real. Rejected as low: the in-memory store does not enforce `(title, meeting_date)`, so the test would have to throw `ConcurrencyConflictException` itself and would then be asserting its own fake; the exception-to-409 mapping is already covered generically by `ProblemDetailsTests`, and `CreateMeetingHandler` has no catch for it to swallow.
  - `[medium]` `[patch]` The proposition that makes a bare `[Authorize]` correct — `Role` has exactly two members and both write — appears in a `<remarks>` paragraph and in no test — Confirmed: every authorization test covers only the anonymous 401. Patched: `MeetingsEndpointTests.A_bare_Authorize_is_the_whole_write_rule_only_while_every_role_is_a_writer` pins the enum's membership. Mutation-checked by adding a `Viewer` member — the assertion fails, so a third role now has to be acknowledged rather than silently gaining every Meeting write.
  - `[maybe-false]` `[defer]` "Byte-for-byte" is exercised over ASCII whitespace only, and U+0000 passes both guards while `character varying` cannot hold it — Same entry as the blind-hunter finding above; deferred at medium (unverified) with what would settle it.
  - `[low]` `[reject]` `carried` — Trim-relative bounds are answered differently at the HTTP and Domain surfaces within one request — Logged last pass as low and rejected. Keeping that verdict and route.
  - `[low]` `[patch]` The transformer's generality lives in its doc comment rather than in its code — Same entry as the verification-gap rationale finding above; see the patch recorded there.
  - `[low]` `[patch]` The paging prose promises that paging "cannot return a row twice or skip one" while DW-6 records that the count and the window are two statements — Confirmed over-claim at the published-documentation surface. Patched: both the controller remarks and `MeetingsQueries`' doc comment now say the order is total and stable over an unchanging set, and name the concurrent-insert case explicitly. The mechanism itself stays deferred as DW-6, untouched.
  - `[false]` `[reject]` `tests/Web.Tests/StubApiClient.cs` is a web-tier file changed by a story that says not to touch the web tier — Not a defect: the file is a test stub of the regenerated client interface, the change is compiler-forced by the re-export, no UI was added, and the Spec Change Log records it.
  - `[low]` `[patch]` Two matrix cells are exercised only below the surface they are written at — the omitted-`attendees` create and the out-of-range page — Confirmed. The out-of-range page is folded into the new list test's paging assertions; the omitted case is patched directly as `A_create_that_omits_attendees_entirely_round_trips_as_an_empty_array`, which sends no `attendees` property at all rather than a null.
  - `[false]` `[reject]` The notes `PUT` response carries more than the matrix's three fields — `MeetingNotesDto` includes `Text` because the same DTO is nested in the detail read, which is the shape the matrix's read row asks for. The row names a minimum, and publishing one shape for both is what keeps the generated client stable.
  - `[false]` `[reject]` `MeetingPersistenceTests` issues raw SQL against `information_schema` and `pg_indexes` while the intent says "No raw SQL" — The bullet is about production code, and the Code Map explicitly directs copying `SchemaShapeTests`' probe helpers, which are these queries. No production raw SQL was added.
  - `[low]` `[reject]` Bookkeeping surfaces contradict each other and the diff — Same entry as the blind-hunter bookkeeping findings above; rejected on the same ground.


## Design Notes

**Why the counts are zero rather than absent.** `MeetingSummaryDto { runCount, trackedActionCount }`
is fixed by AD-13, and `src/ActionLedger.Web/openapi.json` is the boundary the web client is
generated from. Publishing the fields now and projecting `0` means Stories 2.5 and 3.1 change one
`Select` and re-export a contract whose *shape* did not move; omitting them now would reshape the
generated client twice. `UsersQueries`' own doc-comment established this precedent for
`PagedResult` in Story 1.2.

**Why `AttachNotes` throws rather than returning a result.** The Errors convention maps
`DomainRuleException` to 409 `conflict` already (`ProblemDetailsMapping.cs`, `ApiExceptionHandler`),
and the unique index on `meeting_notes(meeting_id)` translates to the *same* 409 through
`ConcurrencyTranslation`. One status, two independent enforcers — the domain rule for the ordinary
case and the index for the concurrent one — with no third code path to keep in step.

**The hash, concretely.** Lower-case hex of `SHA256(Encoding.UTF8.GetBytes(Text))`, 64 characters,
computed in `MeetingNotes` at construction and never recomputed:

```csharp
private static string HashOf(string text) =>
    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
```

Computing it in Domain keeps "the hash is of the stored text" structurally true rather than
conventionally true, and `SHA256` is BCL, so `DependencyRuleTests` Rule 1 stays green.

**Trim the title, never the notes.** `Meeting.Create` trims title and attendee names — the
`(title, meeting_date)` unique index would otherwise treat `"Standup"` and `"Standup "` as distinct
meetings, which is the duplicate the AC asks to reject. `MeetingNotes` trims nothing: FR-2 says the
saved text is byte-for-byte what was submitted, including whitespace, and the SHA-256 is only
meaningful if that holds.

## Verification

**Run on 2026-09-21 against the working tree. SDK 10.0.401 at `~/.dotnet`, Docker (OrbStack),
`postgres:18-alpine`.**

| Command | Expected | Result |
|---------|----------|--------|
| `dotnet tool restore` | `dotnet-ef` 10.0.12 available | restored |
| `dotnet ef migrations add AddMeetings --project src/ActionLedger.Infrastructure --startup-project src/ActionLedger.Api` | a migration plus an updated snapshot | `20260922021601_AddMeetings.cs`, its designer, and `AppDbContextModelSnapshot.cs`; all three committed unedited. Needs `Database__ConnectionString` and the other `ValidateOnStart` keys in the environment — the design-time factory builds the host. |
| `dotnet build ActionLedger.sln` | 0 errors, 0 warnings | Build succeeded, 0 Warning(s), 0 Error(s) |
| `dotnet build ActionLedger.sln -c Release` | same | Build succeeded, 0 Warning(s), 0 Error(s) |
| `dotnet run --project src/ActionLedger.Api -- --export-openapi` | the four new operations and their schemas | `+550` lines: `POST /api/v1/meetings`, `GET /api/v1/meetings`, `GET /api/v1/meetings/{id}`, `PUT /api/v1/meetings/{id}/notes`, plus `MeetingCreatedDto`, `MeetingDetailDto`, `MeetingNotesDto`, `MeetingSummaryDto`, `PagedResultOfMeetingSummaryDto`, `CreateMeetingCommand`, `SaveMeetingNotesCommand` |
| export twice to two paths, with `Database__ConnectionString`, `Jwt__Key`, and `Jwt__Issuer` unset | byte-identical, and identical to the committed file | identical both ways — the story-1.3 export trap survives a third controller |
| every test assembly, Debug and Release | green | **472 tests**, 0 failed, 0 skipped, in both configurations (Domain 28, Application 32, Architecture 95, Infrastructure 32, Api 89, Web 194, Web.E2E 1, Eval 1). 398 before the story. |
| `git diff --stat` | nothing under `src/ActionLedger.Web/` but `openapi.json`, no `sprint-status.yaml` change | both hold; the one file outside the four rings is `tests/Web.Tests/StubApiClient.cs`, for the reason in the Spec Change Log |

**Correction — `dotnet test` does work on this machine.** The implementation session reported that
it returns `Zero tests ran, Exit code: 5` for every project and ran the built test applications
directly instead. The review session could not reproduce that: `dotnet test ActionLedger.sln`, run
from the repository root with `~/.dotnet` on `PATH`, discovers and runs all eight assemblies and
reports `total: 479, failed: 0, succeeded: 479, skipped: 0`. AC5 names `dotnet test` as its
verification surface, and that surface was driven. The earlier claim is left here struck rather
than deleted, because it is also what the "Not verified here" note below was reasoning from.

**Every new guard was verified red, then reverted.**

| Mutation introduced | Tests that failed |
|---------------------|-------------------|
| the `HasNotes` refusal deleted from `Meeting.AttachNotes` | `MeetingTests.Attaching_notes_a_second_time_is_a_rule_violation_not_an_update`, `MeetingsTests.Saving_notes_twice_is_a_conflict_and_leaves_the_first_text_alone` |
| `MeetingNotes` trims its text | six `MeetingTests` (`Notes_are_stored_byte_for_byte…` ×4, `The_hash_is_of_the_text_that_was_stored_including_its_whitespace`, `Whitespace_only_notes_are_kept…`) and three `MeetingPersistenceTests.Notes_round_trip_byte_for_byte` |
| a `DELETE {id}/notes` action added to the controller | all three `NotesImmutabilityTests`, plus `OpenApiSnapshotTest` |
| `[Authorize]` removed from `MeetingsController` | `AuthDisciplineTests.Every_documented_operation_but_login_and_health_refuses_an_anonymous_caller`, all four `MeetingsEndpointTests.Every_meeting_operation_refuses_an_anonymous_caller`, and `OpenApiSnapshotTest` |
| `CreatedByUserId` added to `CreateMeetingCommand` | `ActorIntegrityTests.A_handler_command_never_carries_the_actor_that_sent_it` |
| both unique indexes deleted from the migration | both `MeetingPersistenceTests.The_AD20_unique_indexes_exist_by_name` rows, `.A_duplicate_title_on_the_same_day_surfaces_as_a_concurrency_conflict`, `.Two_writers_attaching_notes_to_the_same_meeting_end_in_one_conflict` |
| `ListAsync`'s order-by replaced with `OrderBy(Title)` | `MeetingsTests.The_list_is_newest_meeting_first_then_newest_created_first`, `.Total_counts_every_matching_row_not_the_length_of_the_page` |
| the `long` offset guard replaced with `int` arithmetic | `MeetingsTests.A_page_far_past_the_end_is_empty_and_still_reports_the_true_total` |

**Not verified here — it runs at landing, with a human present:**

- `gh pr checks` — `ci.yml`, `require-linked-issue`, and CodeQL on the pull request. The
  Release-configuration build above is `ci.yml`'s exact shape, and the suites were driven through
  `dotnet test` locally (see the correction above), so what remains unrun here is only the hosted
  runner itself: CodeQL's analysis and the linked-issue gate, neither of which a local run can
  stand in for. This story adds no workflow change.

**Manual checks:**

- `git diff --stat` — done, in the table above.

## Auto Run Result

Status: done

Follow-up review pass over the committed Story 2.1 change (baseline `8f0702b`). The code the story
shipped was not re-derived: no finding routed to `intent_gap` or `bad_spec`, so this pass patched,
deferred, and rejected against the existing implementation.

### Implemented change

Story 2.1 remains as landed: the `Meeting` aggregate with its write-once `MeetingNotes` child, the
migration creating `meetings` and `meeting_notes`, and the four API operations. This pass closed
verification gaps around it and corrected published prose and one contract shape.

### Files changed in this pass

**Contract**
- `src/ActionLedger.Api/OpenApi/OpenApiSetup.cs` — a `DescribeRequiredMembersAsNonNullableAsync` schema transformer, so a `[Required]` member stops publishing itself as nullable; the collection-bounds transformer's remarks now describe what it actually keys on.
- `src/ActionLedger.Web/openapi.json` — re-exported: `meetingDate` publishes `"type": "string"` instead of `["null","string"]`. That one property is the whole diff.

**Prose that was wrong**
- `src/ActionLedger.Application/Meetings/CreateMeetingHandler.cs` — `AttendeeNamesAttribute`'s stated reason corrected: `[StringLength]` throws on an array rather than measuring it.
- `src/ActionLedger.Api/Controllers/MeetingsController.cs` and `src/ActionLedger.Application/Meetings/MeetingsQueries.cs` — the paging guarantee now says the order is total and stable over an unchanging set, and names the concurrent-insert case that DW-6 tracks.

**Tests**
- `tests/Api.Tests/MeetingsSuccessResponseTests.cs` — the list over HTTP (envelope, ordering, zero counts, a one-row second page), the omitted-`attendees` create, and both 404s.
- `tests/Api.Tests/MeetingsEndpointTests.cs` — the `Role` membership premise that makes a bare `[Authorize]` the whole write rule.
- `tests/Api.Tests/NotesImmutabilityTests.cs` — the contract walk now covers any path naming notes, not only the `/meetings` prefix.
- `tests/Api.Tests/OpenApiContractTests.cs` — a required member does not publish itself as nullable; the corrected `[StringLength]` rationale.
- `tests/Domain.Tests/Meetings/MeetingTests.cs` — the write-once shape guard is an allowlist over `Meeting`'s declared members rather than a name filter; the attendee limit's accept side.
- `tests/Infrastructure.Tests/MeetingPersistenceTests.cs` — a deterministic, collision-free test-database name.

### Review findings breakdown

48 findings across four layers: 0 high, 9 medium, 30 low, 7 false, 2 maybe-false. Grouped into
10 patched entries (5 medium, 5 low), 1 deferred entry, and the rest rejected. Every finding has a
row with its evidence under `## Review Triage Log`; the rejected ones carry their reason there.

**Patched (10 entries — medium 5, low 5, high 0):** the list endpoint's `page`/`pageSize` wiring was
never executed by a test; the write-once shape guard matched on member names; the 404 was declared
on two routes and asserted at neither; the `[Authorize]`-is-enough premise lived only in a comment;
`meetingDate` published as nullable while `[Required]` refused null; the notes-immutability walk was
scoped to a path prefix; the attendee limit was tested only on its reject side; a test database was
named with a per-process-salted hash; the attendee attribute's rationale was factually wrong in
three places; the paging prose promised more than the mechanism delivers.

**Deferred (1):** a note containing U+0000 satisfies the published contract and both length guards,
while PostgreSQL's character types cannot store a NUL byte. Recorded at medium (unverified) with
what would settle it — it needs a real database this pass did not put it in front of, and the
remedy (400 versus normalization) is a product decision.

**Notable rejections:** the missing foreign key on `created_by_user_id` (no route to an orphan id
demonstrated; the fix edits a committed migration); no `saved_by_user_id` on `meeting_notes` (the
intent's own matrix fixes the notes shape without an actor); no bound on attendee count, on the
attendee array, or on `MeetingDate` (no demonstrated everyday reachability, and each fix adds public
surface); the trimming divergence between the published attendee bounds and the attribute (both
directions are the safe one); and three findings whose only fix was to edit this build's spec or the
orchestrator's board.

### Follow-up review recommendation

`false`. This was itself a follow-up pass, and it patched no `high` — the two strongest entries were
medium verification gaps, both now closed and mutation-checked. Patched counts by verdict: high 0,
medium 5, low 5.

### Verification performed

SDK 10.0.401 at `~/.dotnet`, Docker (OrbStack), `postgres:18-alpine`.

| Command | Expected | Result |
|---------|----------|--------|
| `dotnet build ActionLedger.sln` | 0 errors, 0 warnings | Build succeeded, 0 Warning(s), 0 Error(s) |
| `dotnet build ActionLedger.sln -c Release` | same | Build succeeded, 0 Warning(s), 0 Error(s) |
| `dotnet test ActionLedger.sln` | green | **486 tests**, 0 failed, 0 skipped (479 before this pass) |
| `dotnet test ActionLedger.sln -c Release` | same | 486 tests, 0 failed, 0 skipped |
| `dotnet run --project src/ActionLedger.Api -- --export-openapi` twice, to two paths, with `Database__ConnectionString`, `Jwt__Key`, and `Jwt__Issuer` unset | byte-identical, and identical to the committed file | identical both ways, and `cmp` against the committed file matches |
| `git diff --stat` | nothing outside the four rings but `openapi.json` | holds |

**Every patch was mutation-checked.**

| Mutation introduced | Tests that failed |
|---------------------|-------------------|
| `meetings.ListAsync(pageSize, page, …)` — the reviewer's own demonstration | `MeetingsSuccessResponseTests.The_list_pages_with_the_window_the_query_string_asked_for` (nothing failed before this pass) |
| `public void Overwrite(string, DateTimeOffset)` added to `Meeting` | `MeetingTests.Nothing_on_the_aggregate_can_change_or_clear_notes`, naming `Meeting.Overwrite` (it passed before this pass) |
| a third `Role` member (`Viewer`) | `MeetingsEndpointTests.A_bare_Authorize_is_the_whole_write_rule_only_while_every_role_is_a_writer`, plus `OpenApiSnapshotTest` |
| `DescribeRequiredMembersAsNonNullableAsync` unregistered | `OpenApiContractTests.A_required_member_does_not_publish_itself_as_nullable`, plus `OpenApiSnapshotTest` |

Two patches were corrected mid-pass by the suite rather than by reasoning: the shape-guard allowlist
first caught `AggregateRoot.ClearDomainEvents` and was scoped to `DeclaringType`, and the
test-database name first used `pasted.Length`, which collides because two theory rows are both 24
characters. Both are recorded in their triage rows.

### Residual risks

- The deferred U+0000 case is unverified in either direction. If PostgreSQL refuses it, the current
  code answers a contract-conforming paste with a 500.
- `runCount` and `trackedActionCount` are still literal zeroes; Stories 2.5 and 3.1 change the
  projection, and the envelope shape is already published.
- CodeQL and the linked-issue gate still run only on the pull request; this pass adds no workflow
  change and the Release build above is `ci.yml`'s shape.
