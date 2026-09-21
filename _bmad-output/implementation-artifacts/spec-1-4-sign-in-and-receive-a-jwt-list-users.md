---
title: 'Story 1.4 — Sign in and receive a JWT; list users'
type: 'feature'
created: '2026-09-21'
status: 'done'
route: 'dispatch'
baseline_commit: '63432a988749a690d632f5d5e3ce73f682c4b832'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-3-database-migrations-and-seeded-users.md'
warnings: ['oversized']
deferred:
  - summary: >-
      Sign-in has no rate limiting, lockout, or audit log line, so a brute force against
      POST /api/v1/auth/login is both unlimited and unobservable.
    evidence: |-
      AuthController and SignInHandler contain no throttling and emit no log entry on a refused
      credential, and no middleware supplies either. The epic's Observability constraint asks for
      a correlation id on every line, and a failed authentication is the canonical event needing
      one. Not caused by a defect in this story's code: no acceptance criterion in epics.md or
      PRD FR-23 requires throttling, and adding it is new product surface rather than a smallest
      fix. Worth a hardening story before anything beyond the demo.
    location: >-
      src/ActionLedger.Api/Controllers/AuthController.cs
    severity: medium
---

<intent-contract>

## Intent

**Problem:** Story 1.2 wired the bearer scheme that *accepts* tokens and Story 1.3 seeded the users, but nothing *issues* a token, so nobody can sign in. There are still zero controllers in `src/`, no `ICurrentUser` implementation that reads a claim, and no read-side seam — so every later story has no actor, no roster, and no query pattern to copy.

**Approach:** Add `POST /api/v1/auth/login` (anonymous) issuing an 8-hour HS256 JWT against the same key, issuer, and short claim names the existing handler validates, and `GET /api/v1/users` (authenticated) returning the paged roster of non-system users. Establish the three seams every later story reuses: the first `<Verb><Noun>Handler`, the first `<Feature>Queries` over a new `IReadDb` port, and the claims-backed `ICurrentUser`.

## Boundaries & Constraints

**Always:** Controllers declare routes **without** the `api/v1` prefix — `ApiRoutePrefixConvention` prepends it (AD-13). Controllers validate HTTP shape and call exactly one handler or query; they never touch a repository or `DbContext` (AD-2). `[Authorize]` is an explicit attribute on every controller — `UsersController` needs no role, login is `[AllowAnonymous]` (AD-12). The issued token is HS256 over `Jwt:Key`, issuer `Jwt:Issuer`, lifetime `JwtOptions.TokenLifetime`, claims `sub`/`name`/`role` using the `JwtBearerSetup` constants. DTOs live in Application (AD-13). Lists return `PagedResult<T>` honouring `Paging.Normalize` (AD-13 Paging). Time comes from `IClock` (AD-15). New packages need a `PackageVersion` in `Directory.Packages.props` and must be MIT/Apache-2.0/BSD (NFR9). Test method names are `Sentence_case_with_underscores`, assertions are xunit built-ins, every test class carries a `/// <summary>` naming the AD it defends, and every awaited call takes `TestContext.Current.CancellationToken`.

**Never:** No `Jwt:Audience` key and no `aud` claim — `ValidateAudience` is deliberately false. No `FallbackPolicy` on `AddAuthorization` — it would 401 the health probes, `/openapi`, and Swagger UI, and it would leave operations *undocumented* as secured because `RequireBearerWhereAuthorizedAsync` reads attribute metadata only. No `Microsoft.AspNetCore.*`, `Microsoft.IdentityModel.*`, `Microsoft.EntityFrameworkCore`, or `Npgsql` package **or namespace** in Domain or Application — `PasswordVerificationResult` and `ToListAsync` must not cross into Application (AD-1 Rules 2b/3). No project under `src/` may reference Api. No type named `*Normaliz*` outside `ActionLedger.Application.Ai`. No credential literal in any file, test, or spec (NFR5). No password-rehash-on-login and no new `User` mutator — see Design Notes. No API-side migration (AD-17); tests migrate, the app does not. No moving `app.UseAuthentication()`/`UseAuthorization()` above `UseApiProblemDetails()` — that silently empties 401/403 bodies.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Login, valid | `POST /api/v1/auth/login` `{username,password}` for a seeded user | 200 with token, `expiresAt`, and the caller's `UserSummaryDto`; token validates against the running host | No error expected |
| Login, wrong password | Correct username, wrong password | 401 `unauthorized`; detail identical to the unknown-user case | Never says which half was wrong |
| Login, unknown username | Username not in the table | 401 `unauthorized`; byte-identical body to the wrong-password case | Same |
| Login, system user | `username=seed` | 401 `unauthorized` — the system identity is not a sign-in | Same body as any other failure |
| Login, blank field | Empty or missing `username`/`password` | 400 `validation` from `[ApiController]` model state | ProblemDetails, not 401 |
| Login, username casing | `Dana` / ` dana ` | 200 — matched as stored (lower-cased, trimmed) | N/A |
| Token shape | A token from a successful login | HS256, `sub` = user id, `name` = display name, `role` = role, expiry 8h, no `aud` | N/A |
| Roster, authenticated | `GET /api/v1/users` with any valid token | 200 `{items,page,pageSize,total}` of `UserSummaryDto`, **excluding** `IsSystem` users, deterministically ordered | N/A |
| Roster, anonymous | No `Authorization` header | 401 `unauthorized` ProblemDetails | N/A |
| Roster, paging | `?page=0&pageSize=9999` | Clamped by `Paging.Normalize` to page 1, size 200; `total` counts all matching rows, not the page | N/A |
| Current user | Authenticated request | `ICurrentUser` reports the `sub`/`name`/`role` claims | Throws when no authenticated principal — never invents an actor |
| Auth walk | Every operation in the generated document except `auth/login` and `/health*` | Each answers 401 without a token | Test fails if it walks zero operations |

</intent-contract>

## Code Map

**Already built by 1.2/1.3 — read before writing anything; most of the acceptance half exists.**

- `src/ActionLedger.Api/Auth/JwtBearerSetup.cs:16,19,22` -- `SubjectClaim`/`NameClaim`/`RoleClaim` constants; `:25` `AddApiAuthentication` is **already called** at `Program.cs:34`. `:55` `MapInboundClaims = false`; `:59-73` validation parameters (issuer on, **audience off**, HS256 only, 30s skew, name/role claim types). Issue against exactly these.
- `src/ActionLedger.Api/Configuration/JwtOptions.cs:9` -- `Key` (`[MinLength(32)]`), `Issuer`; `:14` `public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(8)` — *"Story 1.4 stamps it; nothing configures it."* Use the constant; add no config key.
- `src/ActionLedger.Api/Program.cs:33-57` DI order, `:61-66` middleware (`UseAuthentication`/`UseAuthorization` already in the right slot), `:52` `ApiRoutePrefixConvention`, `:105` `MapControllers()`, `:107-110` the `--export-openapi` return **after** `MapControllers`, `:117` `public partial class Program`. Register the new Api-ring services and `AddHttpContextAccessor()` here.
- `src/ActionLedger.Api/Routing/ApiRoutePrefix.cs:14` -- `Prefix = "api/v1"`; `:17` `UnversionedPaths = ["/health","/openapi","/swagger"]` — the allowlist the auth-walk test excludes.
- `src/ActionLedger.Api/Errors/ProblemDetailsMapping.cs:116-133` -- `ProblemTypes` (`validation`/`unauthorized`/`forbidden`/`not-found`/`conflict`); `:136-144` `TitleFor` — **title is fixed per status; failure-specific text goes in `detail`**. `:152-184` `ApiExceptionHandler` maps only `NotFoundException`/`DomainRuleException`/`ConcurrencyConflictException`; anything else is a bare 500. **Trap:** a custom `InvalidCredentials` exception would become a 500 — return `Unauthorized()`/`Problem(statusCode: 401, …)` instead.
- `src/ActionLedger.Api/OpenApi/OpenApiSetup.cs:20,53-61` -- the `bearer` security scheme, whose description already names this endpoint; `:78-148` `AddPagingVocabulary` publishes the `PagedResult` schema and `page`/`pageSize` parameters, unreferenced, for *"the first list endpoint … Story 1.4's user roster"*; `:155-177` `RequireBearerWhereAuthorizedAsync` stamps `security` from `[Authorize]`/`[AllowAnonymous]` **attribute metadata only**.
- `src/ActionLedger.Api/OpenApi/OpenApiExport.cs:29` `ContractRelativePath = "web/actionledger-web/openapi.json"`; `:103-116` `GenerateAsync`; `:123` `CommittedContractPath()`; `:131-135` `AttachEndpoints` (`UseRouting` + empty `UseEndpoints`, no listener). Adding `[Authorize]` does not break export — it serves no request.
- `src/ActionLedger.Domain/Users/User.cs:56-74` -- `Username` (lower-cased, trimmed at construction), `DisplayName`, `PasswordHash`, `Role`, `IsSystem`, `CreatedAt`; `Id` from `Common/AggregateRoot.cs:26`. Factories `:83` `Register`, `:107` `RegisterSystem`. **No mutators exist.**
- `src/ActionLedger.Application/Abstractions/` -- `ICurrentUser.cs:14` (`Guid UserId`, `string DisplayName`, `Role Role` — non-nullable, sync); `IUserRepository.cs:22` `FindByUsernameAsync` (normalizes `Trim().ToLowerInvariant()`); `IClock.cs:11`; `PagedResult.cs:19` the record, `:29` `Paging` (`FirstPage`, `DefaultPageSize` 50, `MaxPageSize` 200), `:41` `Normalize(int?, int?)`.
- `src/ActionLedger.Infrastructure/Auth/PasswordService.cs:29` `Hash(string)`, `:37` `Verify(User, string)` → `PasswordVerificationResult` (**Identity type — must not escape this ring**). Registered singleton at `InfrastructureRegistration.cs:46`.
- `src/ActionLedger.Infrastructure/InfrastructureRegistration.cs:34` `AddActionLedgerPersistence(services, Func<IServiceProvider,string?>)`, `:41-47` the scoped/singleton registrations, `:58` `AddActionLedgerSeeding` — the precedent for passing settings in from the composition root rather than reading config in this ring.
- `src/ActionLedger.Infrastructure/Persistence/AppDbContext.cs:20,23` -- public context, `DbSet<User> Users`, `:31` `SaveChangesAsync` translating EF exceptions. `Persistence/UserRepository.cs:13` is `internal` — unreachable from Api and Application.
- `src/ActionLedger.Infrastructure/Seed/SeedCurrentUser.cs:16` -- the only `ICurrentUser` today; snapshot-at-construction shape the claims version must match.
- `src/ActionLedger.Infrastructure/Seed/DemoDataSeeder.cs:37,48-50` -- `SystemUsername = "seed"`, and public `DemoUsers`: `dana`/`priya` (ActionOfficer), `marcus` (Lead). Tests read the roster from here, never from literals.

**Tests and tooling.**

- `tests/Api.Tests/TestApi.cs:21` `WebApplicationFactory<Program>`; `:29-41` `ValidConfiguration()` (`Seed:Enabled=false`, `Jwt:Key`, `Jwt:Issuer`); `:44` `ConfigurationOverrides` (null removes a key); `:53` `IncludeTestEndpoints`; `:59-79` **`TokenFor(role, subject, displayName)`** — the token shape login must reproduce; `:82-88` `CreateClientAs(role)`; `:112` config sources cleared.
- `tests/Api.Tests/RouteDisciplineTests.cs:75-79` `IsOutsideTheRules` (allowlist prefix match), `:81-87` `RoutePatterns` over `EndpointDataSource`. **Trap at `:25`:** it creates a throwaway client purely to boot the host, or `EndpointDataSource` is empty.
- `tests/Api.Tests/ReadinessTests.cs:19-36` -- the per-class `PostgreSqlContainer` + `ConfigurationOverrides["Database:ConnectionString"]` pattern to copy. There is **no** shared Postgres fixture in Api.Tests.
- `tests/Api.Tests/ProblemDetailsTests.cs:99` `An_authenticated_user_in_the_wrong_role_is_a_403_forbidden` -- **already satisfies the AC's 403 half** against `AuthProbeController`. Keep it; do not duplicate it.
- `tests/Api.Tests/TestEndpoints.cs:62-75` `AuthProbeController` -- `[ApiController] [Route("protected")] [Authorize]` + `[Authorize(Roles = "Lead")]`: the exact controller shape to imitate.
- `tests/Api.Tests/OpenApiSnapshotTest.cs:15` byte-compares the generated document against the committed file; `:44` asserts no test route leaks. `OpenApiContractTests.cs:25,54` pin the `PagedResult`/`page`/`pageSize` components; `:69` `Bearer_scheme_is_published_for_the_operations_story_1_4_adds`.
- `tests/Architecture.Tests/DependencyRuleTests.cs:90,98` Application references Domain only and may declare only the three allowlisted packages; `:125,141` the banned package **prefixes and namespaces** for Domain/Application; `:156,170` nothing references Api; `:185` the `*Normaliz*` rule. Every rule is checked twice — assembly types **and** a raw `.csproj` scan, so an unused `PackageReference` still fails.
- `Directory.Packages.props` -- `ManagePackageVersionsCentrally` **and** `CentralPackageTransitivePinningEnabled` are both true. `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.12 is already pinned and referenced; `System.IdentityModel.Tokens.Jwt` and `Microsoft.IdentityModel.Tokens` already compile transitively in the Api ring (see `TestApi.cs:1,12`). **Adding an explicit reference to either without a matching `PackageVersion` fails restore (NU1010).**
- `Directory.Build.props` -- `TreatWarningsAsErrors`, `Nullable=enable`, `EnforceCodeStyleInBuild` repo-wide, tests included.

## Tasks & Acceptance

**Execution:**
- `Directory.Packages.props` -- add `PackageVersion` for `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.12 (MIT) -- Application needs it for its own registration extension; it is on AD-1's allowlist.
- `src/ActionLedger.Application/ActionLedger.Application.csproj` -- add that one `PackageReference` -- keeps Rule 2b satisfied (allowlisted) while letting Application own its DI wiring.
- `src/ActionLedger.Application/Abstractions/IReadDb.cs` -- the read seam AD-2 names: expose `IQueryable<T>` plus **its own async materialization and count members**, so Application never touches EF's `ToListAsync`/`CountAsync` (Rule 3b). This is the file every later `<Feature>Queries` builds on.
- `src/ActionLedger.Application/Abstractions/IPasswordVerifier.cs` -- port returning an Application-owned result enum -- `PasswordVerificationResult` is an `Microsoft.AspNetCore.Identity` type and may not cross the boundary.
- `src/ActionLedger.Application/Abstractions/IAccessTokenIssuer.cs` -- port returning the token string and its expiry for a given `User` -- keeps `Microsoft.IdentityModel.*` out of Application.
- `src/ActionLedger.Application/Users/UserSummaryDto.cs` -- `{ id, displayName, role }` per AD-13 -- the only user shape that crosses the boundary.
- `src/ActionLedger.Application/Auth/SignInHandler.cs` + its command/result -- the first `<Verb><Noun>Handler`: look up by username, verify, refuse system users, issue. One failure result for every failed reason so the controller cannot leak which half was wrong.
- `src/ActionLedger.Application/Users/UsersQueries.cs` -- the first `<Feature>Queries`: filter `!IsSystem`, order deterministically, page via `Paging.Normalize`, return `PagedResult<UserSummaryDto>`.
- `src/ActionLedger.Application/ApplicationRegistration.cs` -- `AddActionLedgerApplication()` registering the handler and the query -- the composition point later stories extend.
- `src/ActionLedger.Infrastructure/Auth/PasswordVerifier.cs` -- implements `IPasswordVerifier` over `PasswordService`, translating the Identity enum here and nowhere else.
- `src/ActionLedger.Infrastructure/Persistence/ReadDb.cs` -- implements `IReadDb` over `AppDbContext` with `AsNoTracking` and EF's async operators -- reads must not track.
- `src/ActionLedger.Infrastructure/InfrastructureRegistration.cs` -- register `IPasswordVerifier` and `IReadDb` beside the existing ports -- do not register anything needing `HttpContext` here (Rule 4).
- `src/ActionLedger.Api/Auth/JwtAccessTokenIssuer.cs` -- implements `IAccessTokenIssuer` from `IOptions<JwtOptions>` + `IClock`, using the `JwtBearerSetup` claim constants and `JwtOptions.TokenLifetime`. Lives in Api because `JwtOptions` does and Infrastructure may not reference Api. **Emit short claim names that survive the outbound claim map** — mirror `TestApi.cs:59-79` or use a handler that does no mapping.
- `src/ActionLedger.Api/Auth/ClaimsPrincipalCurrentUser.cs` -- `ICurrentUser` over `IHttpContextAccessor`, reading `sub`/`name`/`role`; throws rather than inventing an actor when unauthenticated (AD-12, FR-25).
- `src/ActionLedger.Api/Controllers/AuthController.cs` -- `[ApiController] [Route("auth")] [AllowAnonymous]`, `POST login` calling `SignInHandler` once and mapping failure to a 401 ProblemDetails with a neutral `detail`. **Never** `[Route("api/v1/auth")]`.
- `src/ActionLedger.Api/Controllers/UsersController.cs` -- `[ApiController] [Route("users")] [Authorize]`, `GET` calling `UsersQueries` once, taking `page`/`pageSize` from the query string.
- `src/ActionLedger.Api/Program.cs` -- `AddHttpContextAccessor()`, `AddActionLedgerApplication()`, and the two Api-ring implementations; leave the export path and middleware order untouched.
- `web/actionledger-web/openapi.json` -- re-export with `dotnet run --project src/ActionLedger.Api -- --export-openapi` and commit -- `OpenApiSnapshotTest` fails CI otherwise. Confirm the two new operations reference the `PagedResult`/`page`/`pageSize` components and that login carries no `security` block.
- `tests/Application.Tests/Auth/SignInHandlerTests.cs` -- the I/O matrix's login rows against in-memory fakes (AD-18): success claims, wrong password, unknown user, system user, and that the three failures are indistinguishable.
- `tests/Application.Tests/Users/UsersQueriesTests.cs` -- the roster rows against a fake `IReadDb`: system user excluded, ordering stable, clamping applied, `total` counts all matching rows rather than the page.
- `tests/Api.Tests/AuthEndpointTests.cs` -- end-to-end against a real `postgres:18-alpine` (copy `ReadinessTests`' per-class container): migrate first with a standalone context, boot `TestApi` with `Seed:Enabled=true` and a per-run generated password, sign in as a seeded user, then **use the returned token on `GET /api/v1/users`** — the only proof the issued token is one the host accepts. Cover the 401 and 400 rows too.
- `tests/Api.Tests/TestApi.cs` -- add a `NewPassword()` helper generating a fresh value per call -- NFR5 forbids a credential literal, including in tests.
- `tests/Api.Tests/AuthDisciplineTests.cs` -- walk every operation of the generated document, skip `auth/login` and `ApiRoutes.UnversionedPaths`, issue the real request without a token, assert 401. Assert the walk covered at least one operation so it can never pass vacuously.

**Acceptance Criteria:**
- Given a clean clone with Docker running, when I run `dotnet test ActionLedger.sln`, then every project passes, including the new Api.Tests container suite, with zero warnings.
- Given `dotnet run --project src/ActionLedger.Api -- --export-openapi` on a machine with **no database** and `Database__ConnectionString`, `Jwt__Key`, and `Jwt__Issuer` unset, when it runs, then it still succeeds — the story-1.3 export trap must survive two new controllers.
- Given the committed contract, when `OpenApiSnapshotTest` runs, then it matches the generated document byte for byte, and no test-assembly route appears in it.
- Given every new guard, when its protected behaviour is mutated (system user allowed to sign in, `IsSystem` filter removed, distinguishable failure details, `[Authorize]` removed from `UsersController`), then a named test fails — verified red, then reverted.
- Given the pull request, when CI runs, then `build and test`, `linked issue`, and CodeQL all pass.

## Spec Change Log

- **`tests/Api.Tests/CurrentUserTests.cs` added.** Not in the task list, but the I/O matrix's
  "Current user" row has no other home: nothing in production code resolves `ICurrentUser` yet,
  so without it the claims implementation would ship unexercised. It issues a token through the
  running host's `IAccessTokenIssuer`, validates it with the host's own
  `TokenValidationParameters` (read from `IOptionsMonitor<JwtBearerOptions>`), and reads the
  resulting principal — so it proves the round trip rather than a principal a test assembled to
  suit itself. It also covers the throw paths: no principal, no `HttpContext`, and a missing
  `sub`.
- **`OpenApiSetup.ReusePagingVocabularyAsync` added.** The task list asks to confirm the new
  operations reference the `page`/`pageSize` components; nothing made that happen by itself,
  because the generator emits a fresh inline parameter per operation. This operation transformer
  swaps any query parameter named `page` or `pageSize` for an `OpenApiParameterReference`, so the
  bounds and defaults a list documents are the ones `Paging` actually applies. Story 1.2's
  "no operation references them yet" remark was updated rather than left stale.
- **The envelope schema is deliberately *not* referenced.** `GET /users` generates
  `PagedResultOfUserSummaryDto`, which carries a typed `items`; pointing it at the untyped
  `PagedResult` component instead would hand Story 1.5's generated client an `object[]`. The
  component stays published as the reference definition, and `OpenApiContractTests` still pins it
  to `PagedResult<T>`. Both new operations reference the parameter components; neither references
  the schema one, on purpose.
- **`SignInCommand` is the request body, and `SignInResult` is the response body.** The task list
  names "`SignInHandler.cs` + its command/result" and AD-13 puts DTOs in Application, so rather
  than adding a second pair of wire types the command carries the `[Required]` annotations that
  make `[ApiController]`'s 400 fire and the controller passes it straight to the one handler.
  `System.ComponentModel.DataAnnotations` is in the shared framework, so this takes no package and
  AD-1 Rule 2b still holds.
- **`AuthController` aliases `SignInResult`.** `ControllerBase` already has one, from
  `ControllerBase.SignIn()`. A `using` alias at the top of the file names the Application ring's.
- **The password is verified *before* the system-identity refusal.** Either order satisfies the
  matrix, because both answer the same 401. Verifying first means any existing User costs the same
  work whichever way the request ends, and it makes the `IsSystem` check a real second guard
  rather than one permanently shadowed by the `Seed` User's unusable hash — which is what lets
  `The_system_identity_is_never_signed_in_even_with_the_right_password` mutate red.
- **`AuthController.RefusedDetail` is `internal`.** `Api.Tests` has `InternalsVisibleTo`, so the
  test asserts the exact string the controller sends rather than a copy of it that could drift.
- **`IPasswordVerifier` is a singleton, `IReadDb` is scoped.** The verifier wraps the singleton
  `PasswordService` and holds nothing per-request; the read seam wraps the scoped `AppDbContext`
  and must match its lifetime. `IAccessTokenIssuer` is a singleton (options and clock are both
  singletons) and `ICurrentUser` is scoped, because it snapshots one request's claims.
- **`AuthEndpointTests` uses a class fixture, not `ReadinessTests`' per-test container.** xunit
  builds a new instance of a test class per test method, so `ReadinessTests`' shape starts one
  container per `[Fact]` — acceptable for three, wasteful for ten. `SeededApi` is an
  `IClassFixture` that starts one `postgres:18-alpine`, migrates it through a standalone context,
  and boots one seeded host for the class. The container image, the migration path, and the
  seeding are otherwise exactly the pattern the Code Map points at.
- **`[EndpointName]`, `[EndpointSummary]`, and `[EndpointDescription]` on both operations.** Not in
  the task list. `GenerateDocumentationFile` is false repo-wide, so XML doc comments never reach
  the document, and both existing routes carry `WithName`/`WithSummary` — an operation with no
  `operationId` would leave Story 1.5's generated client naming methods after the URL path.
- **`Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.12** added to
  `Directory.Packages.props` and referenced by Application, exactly as the task list specifies.
  MIT, and on AD-1's three-package allowlist, so `Rule2_application_takes_no_package_outside_the_allowlist`
  stays green.
- **No rehash-on-sign-in, as designed.** `PasswordCheck` has two values, and `PasswordVerifier`
  folds `SuccessRehashNeeded` into `Succeeded` in the one place the Identity enum is read. The
  Design Notes say why; `PasswordService.cs:12`'s comment about Story 1.4 upgrading a hash is now
  the road not taken.

## Review Triage Log

**Round 1 — ten findings, all fixed.** Every fix below was mutation-verified: the guard was broken,
the named test went red, and the break was reverted.

| Finding | Fix | Guard that now holds it |
|---------|-----|-------------------------|
| The contract published `Role` as a bare `integer`; the wire carries `"ActionOfficer"`. The generator does not read MVC's `AddJsonOptions`, so Story 1.5's client would have typed `role` as a number. | `OpenApiSetup.DescribeEnumsAsStringsAsync` — a schema transformer publishing every enum as `type: string` with names taken from the type itself. Contract re-exported. | `OpenApiContractTests.Enums_are_published_as_the_strings_they_are_serialized_as` pins the published schema to `Enum.GetNames<Role>()`; `AuthEndpointTests` now also asserts the **raw** body carries the name, since `JsonStringEnumConverter` reads numbers too and deserializing would not notice. |
| `(page - 1) * pageSize` overflowed `int`, so `?page=2000000000&pageSize=200` sent PostgreSQL a negative OFFSET and 500'd — reachable by any authenticated caller, on the seam every later list copies. | Offset computed as `long` and compared against `total` before narrowing; past the end returns an empty page with the true total, and skips the query. | `UsersQueriesTests.A_page_far_past_the_end_is_empty_rather_than_an_overflowed_offset` |
| `SignInCommand.Password` had no length cap, so an anonymous caller could feed PBKDF2 a multi-megabyte body for a published demo username. | `[StringLength(PasswordMaxLength)]`, 256 — refused by model validation, before any hashing. | `AuthEndpointTests.A_malformed_credential_is_a_400_validation_not_a_401(OverlongPassword)` |
| Nothing asserted the roster's `page`/`pageSize` were `$ref`s; deleting `ReusePagingVocabularyAsync` and re-exporting left the suite green, because the snapshot only byte-compares a file this story regenerates. The Verification table claimed otherwise. | — | `AuthDisciplineTests.The_roster_reuses_the_published_paging_parameters_rather_than_redeclaring_them` |
| `ReadDb`'s `AsNoTracking` was unasserted: the LINQ-to-Objects fake in `UsersQueriesTests` has no change tracker, so removing it broke nothing. | — | `Infrastructure.Tests/ReadSeamTests` reads through `IReadDb` against a real database and asserts `context.ChangeTracker.Entries()` is empty. |
| `ICurrentUser` was resolved by no production path and no test — all four tests constructed it by hand, so the registration and its **lifetime** were both unexercised, though the class snapshots claims at construction. | `AuthProbeController.CurrentActor` — an `[Authorize]` action injecting `ICurrentUser`, in the test assembly only, so it stays out of the published contract. | `CurrentUserTests.The_container_resolves_a_fresh_actor_per_request_not_one_shared_between_them` drives two subjects; it fails on `AddSingleton` **and** on deleting the registration. |
| `Enum.TryParse` accepts numerics, so a `role` claim of `"99"` produced `(Role)99` and the `Malformed` throw never fired. Both `Malformed` paths were uncovered. | `&& Enum.IsDefined(role)`. | `CurrentUserTests.A_claim_this_api_never_issues_is_refused_rather_than_coerced` — non-Guid `sub`, `role` of `"99"`, and an undefined name. |
| The casing theory rewrote its own inline literals into the stored username, erasing the casing under test; the 400 theory hardcoded `"dana"`. | Both now derive every variant from `SeededApi.SomeoneWhoSignsIn.Username` through a `Typing` / `Malformation` enum. | The theories themselves — renaming a demo user can no longer turn them into a false 401. |
| `SeededApi.DisposeAsync` dereferenced a null `Api` when the container or migration threw, masking the real failure. | Nullable backing field, disposal guarded, and the property throws a message naming the cause. | — (a diagnostic path, not a behaviour) |
| `UsersController` could answer a 400 the contract did not document, unlike `AuthController`. | `[ProducesResponseType<ValidationProblemDetails>(400, "application/problem+json")]`. Contract re-exported. | The re-exported contract, guarded by `OpenApiSnapshotTest`. |

### 2026-09-21 — Review pass

- verdicts: 30 findings — high 1, medium 8, low 17, false 4, maybe-false 0
- findings:
  - `[high]` `[patch]` Contract publishes `Role` as bare `integer` while the wire sends `"ActionOfficer"` — confirmed: `components.schemas.Role` was `{"type":"integer"}` and `Program.cs` adds `JsonStringEnumConverter` via `AddControllers().AddJsonOptions`, which the document generator does not read. Fixed by a schema transformer; contract re-exported; pinned by a document assertion plus a raw-wire assertion.
  - `[medium]` `[patch]` Offset multiplication overflows `int` — confirmed: `Paging.Normalize` clamps `pageSize` but puts no ceiling on `page`, so `?page=2000000000&pageSize=200` yields a negative OFFSET and a 500. Fixed with `long` arithmetic and an early empty page.
  - `[medium]` `[patch]` `SignInCommand.Password` unbounded on an anonymous route — confirmed: `Username` is capped, `Password` was `[Required]` only, and the demo usernames are published in `.env.example`, so a known username reaches PBKDF2 with an arbitrary body. Capped at 256.
  - `[medium]` `[patch]` Paging `$ref`s pinned only by a regenerable snapshot — pre-verified by the verification-gap layer; deleting the transformer and re-exporting left the suite green. Assertion added to `AuthDisciplineTests`.
  - `[medium]` `[patch]` `ReadDb`'s `AsNoTracking` unasserted — pre-verified; the Application-ring fake is LINQ-to-Objects and has no change tracker. `Infrastructure.Tests/ReadSeamTests` added against a real database.
  - `[medium]` `[patch]` `ICurrentUser` registration and scoped lifetime unexercised through the container — pre-verified; all four tests constructed the class by hand, so `AddSingleton` or deleting the registration failed nothing. Probe action plus a two-subject test added.
  - `[medium]` `[defer]` No rate limiting, lockout, or log line for a refused sign-in — real: an unauthenticated brute force against `/api/v1/auth/login` is unlimited and invisible. Deferred, not rejected: no acceptance criterion in the epic or PRD FR-23 asks for it, and throttling is new product surface rather than a smallest fix.
  - `[medium]` `[reject]` Published `ProblemDetails` schema omits the `correlationId` the API stamps — real but additive; a client reads the field from the body regardless, and the fix needs a further schema transformer rather than a direct correction.
  - `[medium]` `[reject]` Nothing keeps `PagedResult` and `PagedResultOfUserSummaryDto` in sync — reclassified `false` on verification: `OpenApiContractTests.Paged_result_component_matches_the_application_type` pins the component to `PagedResult<T>`, which is also the source the concrete envelope is generated from, so the two cannot silently diverge.
  - `[low]` `[patch]` `Enum.TryParse` coerces numeric and undefined role claims — confirmed reachable only with the signing key, but the file's own remarks promise the `Malformed` throw. `Enum.IsDefined` added; both malformed paths covered.
  - `[low]` `[patch]` Casing theory rewrote its own inline literals into the stored username — confirmed: `Replace(..., OrdinalIgnoreCase)` erased the casing under test. Variants now derived from the seeded username.
  - `[low]` `[patch]` `SeededApi.DisposeAsync` dereferenced a null `Api` when startup threw, masking the real failure. Disposal guarded.
  - `[low]` `[patch]` Roster's 400 undocumented while `AuthController` documents its own. `ProducesResponseType` added; contract re-exported.
  - `[low]` `[reject]` Unknown usernames distinguishable by response latency — real in principle, no harm here: the three demo usernames are published in `.env.example`, so there is nothing to enumerate, and a decoy-hash branch is more than a direct correction.
  - `[low]` `[reject]` Roster ordering comparer differs across production collation, LINQ-to-Objects, and the Ordinal test expectation — latent only; all three agree for ASCII names, and pinning a database collation is more than a direct correction.
  - `[low]` `[reject]` `ReusePagingVocabularyAsync` rewrites any query parameter named `page`/`pageSize` without checking its shape — speculative: one list endpoint exists and the transformer is correct for it.
  - `[low]` `[reject]` `PagingParameterComponents` is a publicly mutable `string[]` — cosmetic; no named harm.
  - `[low]` `[reject]` Regenerated `epic-1-context.md` drops the roster-exclusion wording and other constraints — the file is a regenerable cache; the constraint lives in `epics.md` and this spec, and is implemented and tested.
  - `[low]` `[reject]` No test for a token signed with another key, expired, or from another issuer — `ProblemDetailsTests.An_invalid_token_is_a_401_unauthorized` already covers the class.
  - `[low]` `[reject]` No `jti`/`iat` on the issued token — no acceptance criterion requires either; audit ties to `sub`, not to a sign-in event.
  - `[low]` `[reject]` No assertion that `UserSummaryDto` never widens — the record has three positional members and the contract snapshot fails on any change to them.
  - `[low]` `[reject]` 200 responses publish a `text/plain` content type the API never produces — generator default, no consumer harm.
  - `[low]` `[reject]` `AuthController.Login` returns `IActionResult` where the roster returns `ActionResult<T>` — cosmetic; both are pinned by `ProducesResponseType` and the contract snapshot.
  - `[low]` `[reject]` `StringLength` message hardcodes "64 characters" beside `User.UsernameMaxLength` — cosmetic duplication, no behavioural drift.
  - `[low]` `[reject]` `NewPassword()` duplicated in `TestApi` and `SignInHandlerTests` — the rings may not reference each other; duplication is the cheaper of the two options.
  - `[low]` `[reject]` `Directory.Packages.props` group labelled by story number rather than concern — cosmetic.
  - `[false]` `[reject]` Two `ICurrentUser` registrations now conflict and the seeder resolves the throwing one — refuted: `DemoDataSeeder.cs:151` constructs `SeedCurrentUser` directly with `new(seedUser)`; the seeder never resolves the interface from the container, so registration order decides nothing.
  - `[false]` `[reject]` Spec's Verification table claims the `$ref`s are "asserted by `AuthDisciplineTests`" when they were not — the claim was true of the intent and false of the code; rejected as a finding whose fix edits this build's spec, and the underlying test gap was patched instead.
  - `[false]` `[reject]` Spec's per-project test-count split does not reconcile, and its credential-grep claim is contradicted by `"actionledger-tests-not-a-secret"` — fix edits this build's spec; the container password is a Testcontainers fixture value, not a product credential.
  - `[false]` `[reject]` Spec's `## Review Triage Log` is an empty heading — it is the template's placeholder, filled by this pass.

Intent-alignment audit (descriptive, no defects): the auditor enumerated a narrow reading of the orchestrator's `awaiting-operator` clause (vendor-class actions only), a wide reading (any criterion not satisfiable in-run, i.e. the CI-checks criterion), and a precedent reading. Resolved to the narrow reading: story 1.4's acceptance criteria are three HTTP/test criteria plus CI checks; opening a pull request is not a human-only action outside the repository, and stories 1.1-1.3 closed `done` carrying the identical "runs at landing, with a human present" wording. Its other divergences — paged envelope versus flat list, the 403 proven against a test-assembly probe, `ICurrentUser` registered but not yet consumed by a handler, the auth walk reading the generated rather than the committed document — are each named and defended in this spec's Design Notes or Spec Change Log, and the third was patched this pass.

## Design Notes

**Why the roster is paged even though the AC says "every non-system User".** The AC constrains the *filter*, not the envelope; the blanket Paging convention (AD-13, NFR-2) constrains the envelope, and `OpenApiSetup.cs:70-77` states outright that this endpoint is the paging vocabulary's first consumer. With three non-system users the default page of 50 returns every one of them, so both readings agree observably — but only the paged shape satisfies the convention and lets the generated client see the envelope from its first commit.

**Ordering is a decision this story has to make.** Nothing upstream specifies it, and paging without a total order is non-deterministic. Order by display name, tie-broken by id: the roster is read by humans and by the web's owner picker.

**Rehash-on-successful-sign-in is deliberately not done.** `PasswordService.cs:12` anticipates it, but no acceptance criterion asks for it, `User` has no mutator to write a new hash, and every stored hash was produced by the one hasher configuration in the repository — so `SuccessRehashNeeded` cannot currently occur. Adding a Domain mutator and a commit to a read path to serve an unreachable branch is scope this story should not take. Treat `SuccessRehashNeeded` as a successful verification.

**The 403 half of the third AC is already met.** `ProblemDetailsTests.cs:99` drives `AuthProbeController`'s `[Authorize(Roles = "Lead")]` action with an ActionOfficer token and asserts 403. The AC says "a `Lead`-only **test** action", which is exactly that. No real Lead-only route exists until the Cancelled transition in Epic 3.

**Why the auth walk reads the document but asserts over HTTP.** A doc-only "every operation declares `security`" test is vacuous in the one case that matters: a controller that forgot `[Authorize]` is both unsecured *and* documented as unsecured, so the two agree and the test passes. Enumerating from the document and then asserting a real 401 catches it.

## Verification

**Run on 2026-09-21 against the working tree. SDK 10.0.401 at `~/.dotnet`, Docker (OrbStack),
`postgres:18-alpine`.**

| Command | Expected | Result |
|---------|----------|--------|
| `dotnet build ActionLedger.sln` | succeeds, 0 warnings | Build succeeded, 0 Warning(s), 0 Error(s) |
| `dotnet test ActionLedger.sln` | every project passes | Passed — **117 tests**, 0 failed, 0 skipped, after the review fixes (106 at round 1; 74 before the story) |
| every `bin`/`obj` wiped, then restore + build + test | both succeed | Passed — 106, failed 0, 0 warnings (round 1; not re-run after the fixes, which added tests only) |
| `dotnet build -c Release` + `dotnet test -c Release --no-build` (CI's exact shape) | green | Passed — **117**, failed 0, 0 warnings, after the review fixes |
| `--export-openapi` with `Database__ConnectionString`, `Jwt__Key`, `Jwt__Issuer` all unset and no database reachable | exits 0 and writes the contract | `Wrote .../web/actionledger-web/openapi.json`, exit 0 — the story-1.3 export trap survives two controllers |
| export twice to two paths, `diff` | byte-identical | identical; and identical to the committed file |
| `git diff --stat web/actionledger-web/openapi.json` | shows the two new operations | +319 lines: `POST /api/v1/auth/login`, `GET /api/v1/users` |
| generated document, login operation | no `security` block | absent — `[AllowAnonymous]` |
| generated document, roster operation | `security: [{bearer: []}]`, parameters `$ref` `#/components/parameters/page` and `/pageSize` | both, asserted by `AuthDisciplineTests` |
| generated document, `Role` schema | `type: string`, values `ActionOfficer` and `Lead` | both, asserted by `OpenApiContractTests` against `Enum.GetNames<Role>()` |
| `grep -rn` for a credential literal across `src`, `tests`, `.env.example`, `appsettings*.json`, and this spec | none | none |

**Live, against a real `postgres:18-alpine` and real Kestrel** (not `TestServer`), with the schema
applied by `dotnet ef database update` and a per-run generated `Seed__DefaultPassword`:

| Check | Result |
|-------|--------|
| `POST /api/v1/auth/login` as `"Dana "` (wrong case, trailing space) | 200 — matched as stored |
| response body | `{token, expiresAt, user:{id, displayName:"Dana Whitfield", role:"ActionOfficer"}}` |
| decoded token header | `{"alg":"HS256","typ":"JWT"}` |
| decoded token claims | exactly `exp`, `iss`, `name`, `nbf`, `role`, `sub` — **no `aud`** |
| `sub` | the User's id, matching `user.id` in the body |
| `exp - nbf` | 8.0 hours |
| wrong password vs unknown username, `correlationId` removed | byte-identical: `{"detail":"The username or password is incorrect.","instance":"/api/v1/auth/login","status":401,"title":"Authentication is required.","type":"unauthorized"}` |
| `GET /api/v1/users?page=0&pageSize=9999` with that token | 200 — `page:1`, `pageSize:200`, `total:3`, Dana/Marcus/Priya in display-name order, **no `Seed`** |
| `GET /api/v1/users` with no token | 401 |

**Every new guard was verified red, then reverted.** The four the acceptance criterion names, plus
three more:

| Mutation introduced | Test that failed |
|---------------------|------------------|
| the `IsSystem` refusal deleted from `SignInHandler` | `SignInHandlerTests.The_system_identity_is_never_signed_in_even_with_the_right_password` and `.Every_refusal_is_the_same_answer_so_the_controller_has_nothing_to_leak` |
| `.Where(user => !user.IsSystem)` deleted from `UsersQueries` | `UsersQueriesTests.The_system_identity_is_not_in_the_roster`, `.Total_counts_every_matching_row_not_the_length_of_the_page`, and end to end `AuthEndpointTests.The_roster_is_every_seeded_human_ordered_by_display_name_and_no_system_identity` |
| the 401 detail made username-specific | `AuthEndpointTests.A_wrong_password_an_unknown_username_and_the_system_identity_are_one_401` |
| `[Authorize]` removed from `UsersController` | `AuthDisciplineTests.Every_documented_operation_but_login_and_health_refuses_an_anonymous_caller`, `.The_roster_is_published_as_requiring_the_bearer_scheme`, and `AuthEndpointTests.The_roster_refuses_an_anonymous_caller` |
| the `OrderBy`/`ThenBy` deleted from `UsersQueries` | `UsersQueriesTests.The_roster_is_ordered_by_display_name`, `.Two_people_sharing_a_display_name_still_page_in_a_stable_order`, `.Total_counts_every_matching_row_not_the_length_of_the_page` |
| `Paging.Normalize` replaced with the raw defaults | `UsersQueriesTests.A_window_outside_the_bounds_is_clamped_rather_than_refused` |
| `[AllowAnonymous]` on `AuthController` changed to `[Authorize]` | `AuthDisciplineTests.Sign_in_is_published_without_a_security_requirement` |

Round 2 added nine more, one per review fix; they are listed in the Review Triage Log above. Each
was broken, seen red, and reverted — including the two that need a re-export to be honest (deleting
either OpenAPI transformer and regenerating the contract, which the snapshot test alone cannot
catch).

The auth walk was also checked for vacuity directly: it collects what it visited and asserts the
list is non-empty, so a document that stopped publishing authenticated operations would fail
rather than pass silently.

**Not verified here — it runs at landing, with a human present:**

- `gh pr checks` — `ci.yml`, `require-linked-issue`, and CodeQL on the pull request. The
  Release-configuration run above is `ci.yml`'s exact shape, and `ubuntu-latest` has a Docker
  daemon, so the new `Api.Tests` container suite is covered by the existing required check with no
  workflow change.

**Manual checks:**

- Decoding a token from `POST /api/v1/auth/login` — done live, in the second table above.
- Both 401 login responses — done live; identical including `detail`.

