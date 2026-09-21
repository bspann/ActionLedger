---
title: 'Story 1.3 — Database, first migration, migration bundle, and seeded users'
type: 'feature'
created: '2026-09-21'
status: 'done'
route: 'dispatch'
baseline_commit: 'b8e7b9256f5f58009d1f376d1b35548ac64d1352'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-2-api-foundation.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The API has a contract but no persistence. There is no `DbContext`, no schema, no migration path, and nobody to sign in as — so Story 1.4 has no users to authenticate and every later story has nowhere to put data.

**Approach:** Stand up `AppDbContext` with the conventions every later entity inherits, ship the first migration through a bundle produced in the api image build, add a readiness probe that actually checks the database, and seed the four demo users idempotently through a hosted service guarded by a PostgreSQL advisory lock.

## Boundaries & Constraints

**Always:** Tables snake_case plural, entities singular PascalCase, enums stored as strings, all `Guid` keys `ValueGeneratedNever` by model-wide convention, instants `timestamptz`, dates `date` (AD-10). `IUnitOfWork.CommitAsync` is `SaveChangesAsync` and only a handler calls it (AD-20). Unique index on `user(username)`. The api **never** migrates at startup — schema ships only through the bundle (AD-17). The seeder writes through aggregate methods with `SeedCurrentUser` and a `FixedClock`, never raw inserts (AD-21). `PasswordHasher<User>` for passwords (AD-12). EF Core 10.0.12 and Npgsql 10.0.3 per the spine's Stack table.

**Never:** No Meetings, runs, proposals, or actions — the `User` aggregate only; Story 6.1 brings the fixture-driven demo data. No EF Core or Npgsql reference from Domain or Application — AD-1 is enforced and will fail the build. No `Update`, `Attach`, or graph `AddRange`. No migration executed by the API process. No real secret committed.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Schema shape | First migration applied | `users` table, snake_case columns, unique index on `username`, role stored as string | Migration fails loudly |
| Key generation | New `User` persisted | Id is the UUIDv7 the Domain constructor made; EF generates nothing | N/A |
| Inserts only | A new aggregate saved | EF emits INSERT, never UPDATE on the new graph | Test asserts the command shape |
| Readiness, healthy | `GET /health/ready`, database reachable | 200 | N/A |
| Readiness, unreachable | `GET /health/ready`, database down | 503, not 200 | No unhandled exception |
| Liveness unaffected | `GET /health`, database down | 200 — liveness never touches the database | N/A |
| Seed on | `Seed:Enabled=true`, empty database | Dana Whitfield and Priya Ramaswamy (ActionOfficer), Marcus Bell (Lead), and `Seed` (`isSystem`) exist with hashed passwords | N/A |
| Seed idempotent | Second start, same database | Nothing changes — no duplicate rows, no new revisions | Advisory lock serializes concurrent starts |
| Seed off | `Seed:Enabled=false` | No users created | N/A |
| Seed on, no password | `Seed:Enabled=true`, `Seed:DefaultPassword` unset | Host fails to start naming the key | Fail fast; never seeds a default credential |
| Duplicate username | Two users with the same username | Unique index rejects; surfaces as `ConcurrencyConflictException` | 409 at the API boundary |
| Bundle produced | api image build | `dotnet ef migrations bundle` yields a self-contained executable in the image | Build fails if migrations do not compile |

## Decisions

- **Demo password: no credential value in any committed file.** The seeder reads `Seed:DefaultPassword` from the environment or user secrets. There is **no default in code, no value in `appsettings.json`, and no value in `.env.example`** — the example file carries a placeholder only, per NFR5. When `Seed:Enabled=true` and the key is absent, startup fails through `ValidateOnStart` with a message naming the key (AD-16), rather than silently seeding a guessable password. Locally the value lives in the gitignored `.env`; in CI and Azure it comes from secrets. The `Seed` system user gets no usable password at all.
- **The seeder is users-only in this story.** `DemoDataSeeder` is built to be extended by Story 6.1 (fixture-driven Meetings, runs, proposals, outbox rows), not replaced. Registration order already anticipates `OutboxDispatcher` arriving after it.
- **Seeding is off in tests by default.** `Api.Tests` sets `Seed:Enabled=false`; tests that need seeded users opt in explicitly, so `WebApplicationFactory` boots without a database.
- **`Microsoft.EntityFrameworkCore.Design` is added** for `dotnet ef`. It is not in the spine's Stack table — record it in the Spec Change Log. MIT, so NFR9 holds.

</frozen-after-approval>

## Code Map

- `src/ActionLedger.Domain/` -- holds only `DomainAssemblyMarker` and `Common/DomainRuleException`. Add `Common/AggregateRoot`, `Users/User`, `Users/Role`. **Zero packages** — AD-1 Rule 1 fails the build otherwise
- `src/ActionLedger.Application/Abstractions/` -- has `NotFoundException`, `ConcurrencyConflictException`, `PagedResult`. Add `IUnitOfWork`, `IUserRepository`, `ICurrentUser`, `IClock` ports here
- `src/ActionLedger.Infrastructure/` -- empty but for its marker. Everything EF lives here: `Persistence/`, `Auth/`, `Seed/`, `Time/`
- `src/ActionLedger.Api/Configuration/DatabaseOptions.cs`, `SeedOptions.cs` -- already exist and validate at startup; wire them, do not redefine them
- `src/ActionLedger.Api/Health/HealthStatus.cs` -- `/health` lives here; add `/health/ready` beside it. `RouteDisciplineTests` currently asserts readiness is **absent** — that assertion must be updated, not deleted
- `src/ActionLedger.Api/OpenApi/OpenApiExport.cs` -- **read before adding any hosted service.** The export assembles the pipeline with `UseRouting`/`UseEndpoints` rather than starting a host, precisely so `ValidateOnStart` and hosted services never run during export. Adding the seeder must not break `dotnet run -- --export-openapi` on a clean clone with no database
- `web/actionledger-web/openapi.json` -- committed contract; `/health/ready` changes it, so re-export and commit or `OpenApiSnapshotTest` fails
- `tests/Infrastructure.Tests/` -- one placeholder test today; this is where Testcontainers lands (AD-18)

## Tasks & Acceptance

**Execution:**
- [x] `Directory.Packages.props` -- add `Microsoft.EntityFrameworkCore` 10.0.12, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, `Microsoft.EntityFrameworkCore.Design` 10.0.12, `Microsoft.AspNetCore.Identity` for `PasswordHasher`, `Testcontainers.PostgreSql` 4.15.0
- [x] `src/ActionLedger.Domain/Common/AggregateRoot.cs` -- base with domain-event `Raise`; every later aggregate builds on it
- [x] `src/ActionLedger.Domain/Users/User.cs`, `Role.cs` -- UUIDv7 in the constructor, private setters, `isSystem` flag
- [x] `src/ActionLedger.Application/Abstractions/` -- `IUnitOfWork`, `IUserRepository`, `ICurrentUser`, `IClock` -- ports only, no EF types
- [x] `src/ActionLedger.Infrastructure/Persistence/AppDbContext.cs` -- snake_case naming convention, string enums, `ValueGeneratedNever` convention, `xmin` where AD-20 requires it
- [x] `src/ActionLedger.Infrastructure/Persistence/Configurations/UserConfiguration.cs` -- unique index on `username`
- [x] `src/ActionLedger.Infrastructure/Persistence/UserRepository.cs`, `UnitOfWork.cs` -- add and load only; repositories never save
- [x] `src/ActionLedger.Infrastructure/Persistence/ConcurrencyTranslation.cs` -- `DbUpdateConcurrencyException` and unique-violation to `ConcurrencyConflictException`
- [x] `src/ActionLedger.Infrastructure/Migrations/` -- the first migration, generated not hand-written
- [x] `src/ActionLedger.Infrastructure/Auth/PasswordService.cs`, `Seed/SeedCurrentUser.cs`, `Time/SystemClock.cs`, `Seed/FixedClock.cs`
- [x] `src/ActionLedger.Infrastructure/Seed/SeedRepository.cs` -- `pg_advisory_xact_lock`, one of the two sanctioned raw-SQL sites
- [x] `src/ActionLedger.Infrastructure/Seed/DemoDataSeeder.cs` -- `IHostedService`, gated on `Seed:Enabled`, idempotent, aggregate methods only
- [x] `src/ActionLedger.Api/Health/HealthStatus.cs` -- add `/health/ready` checking the database; leave `/health` database-free
- [x] `src/ActionLedger.Api/Program.cs` -- register DbContext, repositories, seeder; keep the export path host-free
- [x] `src/ActionLedger.Api/Dockerfile` -- multi-stage: build, publish api, and produce the migration bundle
- [x] `tests/Infrastructure.Tests/` -- Testcontainers PostgreSQL 18: schema shape, inserts-only, seed idempotence, seed-off, duplicate username
- [x] `tests/Api.Tests/` -- update `RouteDisciplineTests` for `/health/ready`; readiness healthy and unhealthy; re-export `openapi.json`
- [x] `.env.example` -- `Database:ConnectionString`, `Seed:Enabled`, `Seed:DefaultPassword` as **placeholders only**; no real value, no working default anywhere in the repository

**Acceptance Criteria:**
- Given a clean clone with Docker running, when I run `dotnet test`, then every project passes including the Testcontainers suite.
- Given the api image build, when it completes, then a migration bundle executable exists in the image.
- Given `dotnet run --project src/ActionLedger.Api -- --export-openapi` on a machine with **no database**, when it runs, then it still succeeds — the seeder and `ValidateOnStart` must not run during export.
- Given the pull request, when CI runs, then `build and test`, `linked issue`, and CodeQL all pass.

## Implementation Notes

**The export trap held, and the thing that could have broken it was not the seeder.** Hosted
services never start under `OpenApiExport`, so `DemoDataSeeder` was never the risk. The risk was
the `DbContext` registration: resolving `IOptions<DatabaseOptions>` runs `ValidateDataAnnotations`
on every `.Value`, whether or not `ValidateOnStart` is in play. `AddActionLedgerPersistence` takes
a `Func<IServiceProvider, string?>` rather than a value precisely so that read happens when a
context is first created — which, during export, never happens. `--export-openapi` was run with
`Database__ConnectionString`, `Jwt__Key`, and `Jwt__Issuer` all unset and still wrote the contract.

**That same lazy read is why `dotnet ef` needs `Database__ConnectionString` set.** EF's design-time
host builds the Api's service provider and asks it for the context, so the options *are* read, and
the annotation fails on a blank value with a message naming the key. Setting the variable is the
whole workaround, and it is what the Dockerfile's `bundle` stage does with an obvious placeholder;
the real connection reaches the bundle as an environment variable (or `--connection`) at run time.
`.env.example` carries the command. The alternative — an `IDesignTimeDbContextFactory` in
Infrastructure — was rejected because it would put a `Microsoft.EntityFrameworkCore.Design` type in
the shipped assembly's metadata while the package itself is `PrivateAssets="all"`, so any consumer
reflecting over Infrastructure's types would hit a `TypeLoadException`.

**The migration bundle needs an environment variable, not an `appsettings.json`.** EF's own hint
after a bundle build says to copy `appsettings.json` alongside it. That is one way; the variable is
the other, and it is the one compose already satisfies, because `migrate` and `api` read the same
`.env`. Passing only `--connection` is *not* enough: the bundle constructs the context through the
startup project before it applies the override, so the options validation fires first. Both paths
were run against a live `postgres:18-alpine`; the variable path applies the migration and exits 0,
and a second run exits 0 having done nothing.

**`libgssapi-krb5-2` is installed in the final image on purpose.** Npgsql probes for GSSAPI on
every connection. Without the library the connection still succeeds, but the probe writes
`Cannot load library libgssapi_krb5.so.2` followed by a line beginning `Error:` to stderr — which,
in front of a `docker compose up`, reads exactly like the migration failing. One small package
removes it; the bundle now prints `Done.` and nothing else.

**Snake_case is a sweep over the finished model, not a package.** `ModelConventions.Apply` runs
after `ApplyConfigurationsFromAssembly` and renames only what a configuration left at its default,
so an explicit `HasColumnName` still wins. The table name comes from EF's default, which is the
`DbSet` property name — declared plural — so the convention is only the case change: `Users` to
`users`, and a later `MeetingNotes` to `meeting_notes`. That is why there is no pluralizer here and
no `EFCore.NamingConventions` dependency.

**`xmin` is configured nowhere in this story, which is correct.** AD-20 puts the concurrency token
on `ProposedAction`, `TrackedAction`, `Meeting`, and `OutboxMessage` — the four roots with a state
machine. `User` is not one of them. `AppDbContext.OnModelCreating` carries a comment saying so, so
the next aggregate's author finds the rule where they will be looking. The duplicate-username row
in the matrix is served by the unique index and `ConcurrencyTranslation`, not by a token.

**Translation lives in `AppDbContext.SaveChangesAsync`, not in `UnitOfWork`.** Every save path is
then covered, including the seeder's, and AD-1's real guarantee — no EF Core or Npgsql type reaches
Application or Api — does not depend on remembering to save through the right object.

**`PasswordHasher<TUser>` needs a `TUser` it never reads, and the User does not exist yet.** A
password has to be hashed before the `User` that will hold the hash can be constructed, and the
hasher rejects a null subject. `PasswordService` keeps one private, never-persisted `HashingSubject`
for that, and `Verify` takes the real User. `Hash(string)` is therefore the whole public surface.

**The `Seed` User is a `Lead`.** Story 6.1 seeds a Cancelled transition, which AD-12 makes Lead-only.
Giving the system identity the lesser role now would mean changing it then. It is flagged
`IsSystem`, its password is a hash of 32 random bytes that are discarded immediately, and
`PasswordService.Verify` against it returns `Failed` — asserted.

**Every test database is its own database on one container.** `PostgresFixture` starts a single
`postgres:18-alpine` for the assembly and hands each test a freshly created database, so migrations
are applied from empty every time — which also means the migration is proven to apply repeatedly,
not just once. The concurrency test builds two independent hosts against one database and starts
both seeders together; `pg_advisory_xact_lock` is what makes the second a no-op.

**Idempotence is asserted on a fingerprint, not a count.** `A_second_start_changes_nothing` compares
`(id, username, passwordHash, createdAt)` for every row before and after. A re-seed that rewrote a
row in place would keep the count and still be a change; the `FixedClock` is what makes `createdAt`
comparable at all.

**No credential value exists anywhere in this repository, and that is enforced rather than agreed.**
`Seed:DefaultPassword` has no default in code, nothing in `appsettings.json`, and a placeholder in
`.env.example`; `SeedOptionsValidator` refuses a host told to seed without one, and the seeder
refuses again a ring lower. The tests do not read a constant either — `TestHost.NewPassword()`
invents one per call and each assertion verifies against the value that particular run seeded with,
so no test here can pass by agreeing with something on disk. `Api.Tests` still boots with
`Seed:Enabled=false` and therefore needs no password at all, which is what
`Seeding_off_needs_no_password` pins down. One consequence worth knowing:
`dotnet run --project src/ActionLedger.Api` with no environment now fails at startup naming
`Seed:DefaultPassword`, because `appsettings.json` leaves `Seed:Enabled` true. `--export-openapi` is
unaffected — it never starts the host, so `ValidateOnStart` never runs, and it was re-checked with
nothing set.

**Toolchain.** SDK 10.0.401 at `~/.dotnet`; every command below ran after
`export PATH="$HOME/.dotnet:$PATH"` and `export DOTNET_ROOT="$HOME/.dotnet"`. Docker is OrbStack
29.4.0. `postgres:18-alpine` was pulled once; the first Testcontainers run on a clean machine pays
for that pull.

## Spec Change Log

- **`Microsoft.Extensions.Identity.Core` 10.0.12, not `Microsoft.AspNetCore.Identity`.** The task
  list names the latter. That package id stopped at 2.3.x and has no .NET 10 version; the type
  AD-12 requires, `PasswordHasher<TUser>`, ships in `Microsoft.Extensions.Identity.Core` and is
  still in namespace `Microsoft.AspNetCore.Identity`. MIT, so NFR9 holds.
- **Two packages beyond the task list.** `Microsoft.EntityFrameworkCore.Relational` 10.0.12 (pinned
  explicitly because central transitive pinning is on) and `Microsoft.Extensions.Hosting.Abstractions`
  10.0.12, which is what lets `DemoDataSeeder` be an `IHostedService` without Infrastructure taking
  a dependency on the whole host. Both MIT.
- **`.config/dotnet-tools.json` added.** `dotnet ef` is pinned to 10.0.12 as a local tool, so the
  image build, CI, and a clean clone all use one version. Not in the task list.
- **`Domain/Common/DomainEvent.cs` added.** `AggregateRoot.Raise` needs a parameter type. It is an
  abstract record with an `OccurredAt` supplied from `IClock`; the `User` aggregate raises none yet.
- **`Infrastructure/InfrastructureRegistration.cs` and `Seed/SeedSettings.cs` added.** The Code Map
  says to wire the Api's `DatabaseOptions` and `SeedOptions`, not redefine them. Infrastructure
  therefore reads no configuration at all: the composition root passes accessors over its own
  validated options, and `SeedSettings` is the two values arriving, not a second options class.
  `Seed:Enabled`'s default lives in exactly one place, `SeedOptions`; `Seed:DefaultPassword` has no
  default to live anywhere.
- **`Seed:DefaultPassword` added to `SeedOptions`,** per the story's Decisions, with **no default
  anywhere**. `SeedOptionsValidator` fails the host at startup when seeding is on and the key is
  absent, naming it. `.env.example` carries a placeholder only, and `Infrastructure.Tests`
  generates a password per run rather than sharing a constant. An earlier draft of this spec
  named a committed default; that contradicted NFR5 and was corrected before anything was
  committed.
- **`DemoDataSeeder` refuses a blank password itself.** `ValidateOnStart` already stops a host
  before any hosted service runs, but the invariant belongs to the seeder rather than to whoever
  composes it: `StartAsync` throws an `InvalidOperationException` naming the key. Nothing invents a
  credential at any layer, and `Infrastructure.Tests` asserts the refusal directly rather than
  reaching it only through the Api.
- **`Infrastructure/Persistence/DatabaseReadiness.cs` and `ModelConventions.cs` added.** Neither is
  in the task list. The first is what `/health/ready` asks, so the Api ring never holds a
  `DbContext`; the second is the model-wide sweep the `AppDbContext` line calls for, split out
  because it is the file every later aggregate inherits from.
- **The readiness probe is bounded at 5 seconds.** The context retries transient connection
  failures, which is right for a query and wrong for a probe — a readiness check that keeps
  retrying never answers. The cap covers the whole attempt, retries included.
- **`/health/ready` declares its 503 explicitly.** `JsonHttpResult<T>` carries no status code in its
  endpoint metadata, so without `.Produces<HealthStatus>(503)` the committed contract — and the
  client generated from it — would describe only the 200. `HealthStatus` gained `Ready` and
  `Unavailable` beside `Healthy` rather than a second schema.
- **`.dockerignore` added.** Without it the build context carries every `bin/` and `obj/` in the
  repository, and host-built `obj/` state can win over the container's own restore.
- **`libgssapi-krb5-2` installed in the final image.** See the Implementation Note; it removes a
  line reading `Error: ...` from an otherwise successful `migrate` run.
- **`RouteDisciplineTests.Readiness_is_not_mapped_until_there_is_a_database_to_be_ready_for` became
  `Readiness_is_mapped_and_sits_under_the_health_allowlist`.** Updated, not deleted, as the Code Map
  requires: it now asserts the route exists and is inside the `/health` allowlist. What the route
  *answers* is `ReadinessTests`.
- **`Api.Tests` took `Testcontainers.PostgreSql`.** The task list asks for "readiness healthy and
  unhealthy" in `Api.Tests`, and healthy is only honest against a database that is really reachable.
  `Api.Tests` still boots with `Seed:Enabled=false`, so every other test in it needs no database.
- **`web/actionledger-web/openapi.json` re-exported and committed.** The sprint change proposal
  landing in parallel moves this file to `src/ActionLedger.Web/openapi.json` — explicitly in Story
  1.5, when the Blazor project is created. It is untouched here, and the path in this story is the
  one that exists today.

## Review Triage Log

## Design Notes

**The export trap, restated because this is the story that can break it.** Story 1.2 chose manual pipeline assembly over a no-op `IServer` specifically so that adding hosted services here would be safe. Verify `--export-openapi` on a machine with no database before calling this done — it is the one acceptance criterion a passing test suite will not catch.

**Idempotence is proven, not assumed.** "Second start changes nothing" means asserting row counts and that no new revisions appeared, against a real PostgreSQL — not a mock and not an in-memory provider (AD-18).

**Toolchain.** SDK 10.0.401 at `~/.dotnet`; `export PATH="$HOME/.dotnet:$PATH"` and `export DOTNET_ROOT="$HOME/.dotnet"` first. Docker is OrbStack and running; `postgres:18-alpine` is not cached locally, so the first Testcontainers run pulls it.

## Verification

**Run on 2026-09-21 against the working tree. SDK 10.0.401 at `~/.dotnet`, Docker (OrbStack) 29.4.0,
`postgres:18-alpine`.**

| Command | Expected | Result |
|---------|----------|--------|
| `dotnet build ActionLedger.sln` | succeeds, zero warnings | Build succeeded, 0 Warning(s), 0 Error(s) |
| `dotnet test ActionLedger.sln` | all projects pass, Testcontainers included | Passed — **74 tests**, 0 failed (57 before this story: 12 added in `Infrastructure.Tests`, 5 in `Api.Tests`) |
| every `bin`/`obj` wiped, then restore + build + test | both succeed | Passed — 74, failed 0 |
| `dotnet build -c Release` + `dotnet test -c Release --no-build` (CI's exact shape) | green | Passed — 74, failed 0 |
| `dotnet run --project src/ActionLedger.Api -- --export-openapi`, with `Database__ConnectionString`, `Jwt__Key`, and `Jwt__Issuer` all unset and no database anywhere | succeeds | `Wrote .../web/actionledger-web/openapi.json`, exit 0 |
| re-export to a second path, `diff` against the committed file | byte-identical | identical |
| `docker build -f src/ActionLedger.Api/Dockerfile .` | image builds, bundle present | built; `/app/migrate/efbundle`, 36 MB, `--help` answers |
| `efbundle` against a live `postgres:18-alpine` | applies the migration | exit 0, clean stderr, `users` created with `pk_users` and `ix_users_username UNIQUE` |
| the same `efbundle` a second time | nothing to do | exit 0 |
| api container against that database, `Seed__Enabled=true` | four users, hashed | `dana`/Dana Whitfield/ActionOfficer, `priya`/Priya Ramaswamy/ActionOfficer, `marcus`/Marcus Bell/Lead, `seed`/Seed/Lead/`is_system=t`, all `created_at 2026-09-01 00:00:00+00` |
| restart the api container | second start changes nothing | `"CreatedCount":0`, still 4 rows |
| `curl /health` and `/health/ready`, database up | 200, 200 | `{"status":"healthy"}`, `{"status":"ready"}` |
| `docker stop` the database, then the same two | 200 and 503 | `{"status":"healthy"}` [200]; `{"status":"unavailable"}` [503] |
| `grep -rn` for any credential literal across `src`, `tests`, `.env.example`, `appsettings*.json`, and this spec | none | none; `.env.example` holds `replace-with-your-own-demo-password` and nothing else |
| `git log --all -S` for the value the corrected spec removed | never reached history | 0 commits, 0 occurrences in any diff; nothing staged and no commit made on this branch |

**Every new guard was verified red, then reverted.** A test that cannot fail is not a guard:

| Mutation introduced | Test that failed |
|---------------------|------------------|
| `unique: true` changed to `unique: false` in the migration | `PersistenceBehaviourTests.A_duplicate_username_surfaces_as_a_concurrency_conflict` — *Assert.Throws() Failure: No exception was thrown* |
| the seeder's "does this user already exist" check deleted | `DemoDataSeederTests.A_second_start_changes_nothing` and `DemoDataSeederTests.Concurrent_starts_serialize_on_the_advisory_lock_and_still_seed_once` |
| `ApplyColumnName` removed from `ModelConventions`, columns left PascalCase in the migration | all three `SchemaShapeTests` |
| `DatabaseReadiness` returning `true` from both failure paths | `ReadinessTests.Readiness_is_503_when_the_database_is_unreachable` |
| a built-in default restored on `SeedOptions.DefaultPassword` | `StartupValidationTests.Seeding_without_a_password_fails_the_host_naming_the_key` — *Assert.Throws() Failure: Exception type was not an exact match*. The host started and the seeder ran for 57 seconds against the test connection string, which is exactly the failure NFR5 forbids |
| the seeder's own blank-password refusal deleted | `DemoDataSeederTests.Seeding_on_without_a_password_refuses_rather_than_inventing_one` — *Assert.Throws() Failure: No exception was thrown* |

Two further checks found the mutation was not expressible rather than not caught: deleting the
`_ = await context.Database.CanConnectAsync(...)` call fails the build outright (`CS9113: Parameter
'context' is unread`), and keeping the call while returning `true` unconditionally still answers 503,
because an unreachable database makes `CanConnectAsync` throw rather than return `false` — the
`catch` is the load-bearing part, which is why the mutation above targets it.

**Not verified here — it runs at landing, with a human present:**

- `gh pr checks` — `ci.yml`, `require-linked-issue`, and CodeQL on the pull request. `ci.yml` runs
  `dotnet test ActionLedger.sln` on `ubuntu-latest`, which has a Docker daemon, so the Testcontainers
  suite is a required check with no workflow change; the Release-configuration run above is that
  job's exact shape. The image build is not in `ci.yml` yet — Story 1.7 adds it.
