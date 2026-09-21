---
title: 'Story 1.2 — API foundation, ProblemDetails, and the committed OpenAPI contract'
type: 'feature'
created: '2026-09-21'
status: 'done'
route: 'dispatch'
baseline_commit: '162584c9c1ae99c196ece11cfc6f1c11e0a6cb0c'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-1-green-pipeline-on-an-empty-clean-architecture-solution.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The Api ring is an empty host. Nothing defines where routes live, how errors surface, or what shape the contract has — and Story 1.5 generates the Angular client from a committed `openapi.json` that does not yet exist. Every later story would otherwise invent its own conventions.

**Approach:** Establish the contract once: `/api/v1` prefixing, RFC 9457 ProblemDetails for every failure mode, a liveness endpoint, validated options, structured logging with a correlation id, and an OpenAPI document exported to a committed file and guarded by a snapshot test.

## Boundaries & Constraints

**Always:** Every route under `/api/v1` except `/health`, `/openapi`, `/swagger`. ProblemDetails `type` values are exactly the five AD-13 names. Controllers map HTTP shape only — no repository, no `DbContext`, no logic (AD-2). DTOs live in Application and are the only shapes crossing the boundary. Options `Ai`, `Jwt`, `Database`, `Webhooks`, `Seed` all validate with `ValidateOnStart` (AD-16). Serilog writes structured JSON to stdout with `correlationId` on every line — the W3C trace id when present, else `HttpContext.TraceIdentifier`. Package versions come from the spine's Stack table.

**Never:** No business endpoints — no meetings, runs, proposals, or actions; those are Epics 2 to 5. No `DbContext`, no EF Core, no migration — Story 1.3. No token issuing — Story 1.4 — though the bearer scheme and the authn/authz middleware are wired here. No `/health/ready`; it is specified in AD-13 but only becomes real in 1.3 when a database exists. Never log secrets. Never let Domain or Application see ASP.NET Core.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Liveness | `GET /health` | 200, no database touched | N/A |
| Route discipline | Every mapped endpoint | Only `/health`, `/openapi`, `/swagger` sit outside `/api/v1` | Test fails naming the offender |
| Domain rule broken | Handler throws `DomainRuleException` | 409, `type: conflict` | ProblemDetails body |
| Concurrent write | Handler throws `ConcurrencyConflictException` | 409, `type: conflict` | ProblemDetails body |
| Absent resource | Handler throws `NotFoundException` | 404, `type: not-found` | ProblemDetails body |
| Bad payload | Model validation fails | 400, `type: validation`, per-field errors | ProblemDetails body |
| No credential | Protected route, no or invalid JWT | 401, `type: unauthorized` | ProblemDetails body |
| Wrong role | Protected route, authenticated, role not permitted | 403, `type: forbidden` | ProblemDetails body |
| Contract export | `dotnet run --project src/ActionLedger.Api -- --export-openapi` | Writes `web/actionledger-web/openapi.json`, exits 0, host never listens | Non-zero exit on write failure |
| Contract drift | Generated document differs from the committed file | `OpenApiSnapshotTest` fails with a diff | CI red |
| Missing config | A required options key absent at startup | Host fails to start, message names the key | Fail fast, no partial start |
| Swagger exposure | `ASPNETCORE_ENVIRONMENT` is `Development` or `Compose` | `/swagger` served with the bearer scheme | Any other environment: not mapped |

## Decisions

- **ProblemDetails `type`** is the bare slug AD-13 names (`validation`, `unauthorized`, `forbidden`, `not-found`, `conflict`) — a valid relative URI-reference under RFC 9457 and exactly what the architecture records. No invented domain.
- **Exception placement:** `DomainRuleException` in `Domain/Common` per the Structural Seed; `NotFoundException` and `ConcurrencyConflictException` in `Application/Abstractions`. The Api mapper then references only Domain and Application.
- **401 and 403 are proven without production routes.** `Api.Tests` registers test-only endpoints through `WebApplicationFactory`, so the mapping is asserted in this story rather than deferred to 1.4, and no placeholder route ships.
- **The paging envelope is documented without a fake endpoint.** `PagedResult<T>` and the `page`/`pageSize` parameters are defined in Application and registered as reusable OpenAPI components; the first endpoint to use them arrives in 1.4.
- **All five options groups validate now,** even though only `Jwt` has a consumer this story. `Api.Tests` supplies a minimal valid configuration so `WebApplicationFactory` boots.

</frozen-after-approval>

## Code Map

- `src/ActionLedger.Api/Program.cs` -- currently a three-line empty host; becomes the composition root. Add the `--export-openapi` branch before the host runs
- `src/ActionLedger.Api/ActionLedger.Api.csproj` -- `Microsoft.NET.Sdk.Web`, already references Application and Infrastructure
- `Directory.Packages.props` -- central versions; add `Microsoft.AspNetCore.OpenApi` 10.0.12, `Swashbuckle.AspNetCore.SwaggerUI` 10.2.3, `Serilog.AspNetCore` 10.0.0, and `Microsoft.AspNetCore.Mvc.Testing` 10.0.12 for `WebApplicationFactory`. The last is not in the spine's Stack table — record it in the Spec Change Log; it is MIT, so NFR9 holds
- `tests/Api.Tests/` -- holds one placeholder test today; gains `OpenApiSnapshotTest` and the ProblemDetails mapping tests
- `tests/Architecture.Tests/DependencyRuleTests.cs` -- do not edit. The AD-1 rules must keep passing as Api grows; `Rule3` is what stops an ASP.NET Core type drifting inward
- `_bmad-output/planning-artifacts/architecture/architecture-ActionLedger-2026-09-20/ARCHITECTURE-SPINE.md` -- AD-2 (line 62), AD-13 (line 128), AD-16 (line 146), Consistency Conventions (line 182+) for the Errors, Paging, Logging, and Config keys rows, Stack (line 201+)
- Story 1.1 spec Implementation Notes -- MTP test mode, the assembly markers, why `Api` is already `Sdk.Web`

## Tasks & Acceptance

**Execution:**
- [x] `Directory.Packages.props` -- add the four package versions above -- central management, licenses auditable in one file
- [x] `src/ActionLedger.Domain/Common/DomainRuleException.cs` -- the Domain rule violation -- maps to 409
- [x] `src/ActionLedger.Application/Abstractions/NotFoundException.cs`, `ConcurrencyConflictException.cs` -- the two Application-level failures the Api maps
- [x] `src/ActionLedger.Application/Abstractions/PagedResult.cs` -- `{ items, page, pageSize, total }` -- the envelope every list returns
- [x] `src/ActionLedger.Api/Errors/ProblemDetailsMapping.cs` -- exception-to-ProblemDetails middleware or `IExceptionHandler` covering all six matrix rows -- one place owns error shape
- [x] `src/ActionLedger.Api/Configuration/*Options.cs` -- `Ai`, `Jwt`, `Database`, `Webhooks`, `Seed` with data annotations and `ValidateOnStart` -- AD-16 fail-fast
- [x] `src/ActionLedger.Api/Auth/JwtBearerSetup.cs` -- bearer scheme, authn/authz middleware, no token issuing -- 1.4 issues, this story accepts
- [x] `src/ActionLedger.Api/Program.cs` -- composition root: Serilog with correlation id, controllers, `/api/v1` convention, `/health`, OpenAPI, Swagger UI in Development and Compose, the `--export-openapi` branch
- [x] `web/actionledger-web/openapi.json` -- the committed contract, generated not hand-written
- [x] `tests/Api.Tests/OpenApiSnapshotTest.cs` -- generate through `WebApplicationFactory`, fail on any difference from the committed file
- [x] `tests/Api.Tests/ProblemDetailsTests.cs` -- one test per matrix error row, using test-only endpoints
- [x] `tests/Api.Tests/RouteDisciplineTests.cs` -- enumerate the endpoint data source, assert the `/api/v1` rule
- [x] `tests/Api.Tests/StartupValidationTests.cs` -- removing a required key fails the host with that key named
- [x] `.env.example` -- every key from the spine's Config keys row, placeholders only -- no secret committed

**Acceptance Criteria:**
- Given a clean clone, when I run `dotnet build` and `dotnet test`, then both succeed and every new test passes alongside the existing 19.
- Given the running API, when I request an unmapped path, then I get a ProblemDetails 404 rather than an HTML error page.
- Given a change to any DTO or route, when CI runs, then `OpenApiSnapshotTest` fails until `openapi.json` is regenerated and committed.
- Given the pull request, when CI runs, then `build and test`, `linked issue`, and CodeQL all pass.

## Implementation Notes

**The export branch has to attach the endpoints itself, and that is the whole trick of this story.**
`MapGet` and `MapControllers` only record a route on the `WebApplication`; routes are published to
the `EndpointDataSource` the document generator reads when the pipeline is assembled, which
normally happens as the host starts listening. Building the app and asking
`IOpenApiDocumentProvider` for the document therefore yields `"paths": {}` — a contract that is
valid JSON, passes its own snapshot test, and describes nothing. `OpenApiExport.AttachEndpoints`
calls `UseRouting()` and `UseEndpoints(_ => { })` explicitly, which copies the mapped routes into
routing without starting a server, without binding a port, and without running a hosted service.
The alternative — starting the host behind a no-op `IServer` — would also have run `ValidateOnStart`,
so `--export-openapi` would need a complete configuration and could not run on a clean clone.
It would also run the seeder from Story 1.3 onward. `AttachEndpoints` avoids both.

**`Api.Tests` is `Microsoft.NET.Sdk.Web`, not `Microsoft.NET.Sdk`.** `WebApplicationFactory` needs
the ASP.NET Core framework reference and the `MvcTestingAppManifest` the Web SDK emits; under the
plain SDK the factory cannot resolve the content root of the application under test.

**The 401 and 403 mappings are proven against controllers that live in the test assembly.**
`TestEndpoints.cs` declares `ErrorProbeController` and `AuthProbeController`; `TestApi` adds the
test assembly as an MVC application part only when `IncludeTestEndpoints` is set. `OpenApiSnapshotTest`
runs a host without it, and asserts directly that no `/errors/` or `/protected/` route reached the
contract — so the committed file stays free of routes that exist only for tests, and no placeholder
endpoint ships to production. The probe controllers also carry `RouteDisciplineTests`: they declare
`[Route("errors")]` with no prefix of their own, so they are what proves the `/api/v1` convention
still applies rather than the rule being vacuously true against a host with one route.

**Tokens are minted in the test, not issued by the Api.** `TestApi.TokenFor` signs an HS256 token
with the same key and issuer the host validates. That is what lets the 403 row be asserted now —
403 needs an authenticated caller in the wrong role — without Story 1.4's login endpoint existing.

**`MapInboundClaims = false`.** The .NET default rewrites `sub` to a SOAP-era claim URI. AD-12 says
the actor is the `sub` claim, so the remapping is turned off and `NameClaimType`/`RoleClaimType`
are set to the wire names; `[Authorize(Roles = "Lead")]` then reads the `role` claim as written.

**Model validation does not pass through `CustomizeProblemDetails`.** `[ApiController]`'s automatic
400 is built by `ApiBehaviorOptions.InvalidModelStateResponseFactory`, which bypasses the
ProblemDetails service entirely. Without overriding it, a bad payload would have been the one error
in the matrix answering with a different `type` and no correlation id. The factory is replaced so
every error — thrown, status-code, and model-binding — funnels through `ProblemDetailsMapping.Decorate`.

**`ProblemDetails.Title` is set, not defaulted.** The framework fills in the HTTP reason phrase
("Not Found"). The web app renders the title verbatim in its load-failure and write-failure states
("Couldn't load. {problem title}"), so each of the five statuses gets one sentence that reads as
one — and `ProblemDetailsTests` asserts the exact string, because it is contract, not decoration.

**Toolchain.** SDK 10.0.401 at `~/.dotnet`. Every command below ran with
`export PATH="$HOME/.dotnet:$PATH"` and `export DOTNET_ROOT="$HOME/.dotnet"` first.

## Spec Change Log

- **Two packages beyond the Code Map's four.** `Microsoft.AspNetCore.Authentication.JwtBearer`
  10.0.12 (Apache-2.0) is needed for the bearer scheme the story wires — it is not in the shared
  framework. `Microsoft.AspNetCore.Mvc.Testing` 10.0.12 (Apache-2.0) is the one the Code Map
  already flagged as absent from the spine's Stack table. Both satisfy NFR9; neither reaches past
  the Api ring, and `Architecture.Tests` still reports zero AD-1 violations.
- **`src/ActionLedger.Api/appsettings.json` added.** Not in the task list. It carries the
  non-secret defaults (`Ai:Provider=Fake`, prompt version, timeouts, retry schedule, `Seed:Enabled`)
  so a clean clone runs with only the three real secrets — `Jwt:Key`, `Jwt:Issuer`, and
  `Database:ConnectionString` — supplied from the environment, which is what AD-16 requires.
  No secret is in the file.
- **Files beyond the named ones.** `Routing/ApiRoutePrefix.cs` holds the `/api/v1` convention and
  the unversioned allowlist that `RouteDisciplineTests` asserts against; `Observability/RequestCorrelation.cs`
  holds the correlation-id resolver and the Serilog enricher; `OpenApi/OpenApiSetup.cs` and
  `OpenApi/OpenApiExport.cs` split the document's shape from the export command;
  `Health/HealthStatus.cs` gives liveness a named schema. The five `*Options.cs` files are joined
  by `Configuration/ApiOptionsRegistration.cs` and `Configuration/ApiEnvironments.cs`. The task
  list's "one place owns error shape" is honoured: `Errors/ProblemDetailsMapping.cs` is a single
  file holding the mapping, the five slugs, and the titles.
- **`correlationId` is an extension member on every ProblemDetails body,** and the framework's
  default `traceId` extension is removed so there is exactly one correlation field, under the name
  the Logging convention uses. RFC 9457 §3.2 allows extension members.
- **`Paging` helper added beside `PagedResult<T>`.** The spec names the envelope; the defaults it
  is paged with (1-based, 50, max 200) come from the same Consistency Conventions row and had to
  live somewhere both the OpenAPI components and Story 1.4's first list endpoint can read.
- **`Ai:LocalOpenAI` and `Ai:AzureOpenAI` validate conditionally, not unconditionally.** Which
  sub-section is required depends on `Ai:Provider`, which data annotations cannot express, so
  `AiOptionsValidator` covers it. AD-16's provider *reachability* probe (`GET {BaseUrl}/models`)
  is a hosted service over an AI ring that does not exist until Epic 2 and is not attempted here.
- **Tests beyond the four named.** `OpenApiContractTests` guards the parts of the contract no
  endpoint pins down yet — it is what stops `PagedResult<T>` and its unreferenced OpenAPI component
  drifting apart between here and Story 1.4. `SwaggerExposureTests` covers the Swagger exposure
  matrix row; `CorrelationIdTests` covers the `traceparent` preference. `RouteDisciplineTests` also
  asserts `/health/ready` is *absent*, since AD-13 specifies it but Story 1.3 is where it can
  answer honestly.
- **`web/actionledger-web/` is created by the export.** The Angular application itself is Story 1.5;
  this story puts only the generated contract there.

## Review Triage Log

## Design Notes

**Why the snapshot test matters more than it looks.** `openapi.json` is the only thing standing between the Angular build and silent contract drift — 1.5 generates its client from this file, so a route renamed here and not re-exported breaks the web build at type-check time, which is the intended failure. The test is what makes that guarantee real rather than a convention.

**Correlation id, not request id.** Prefer the W3C `traceparent` trace id so a log line correlates across the api, the dispatcher, and whatever an integrator runs; fall back to `HttpContext.TraceIdentifier` only when no trace context arrived.

**Toolchain.** SDK 10.0.401 lives at `~/.dotnet`. Run `export PATH="$HOME/.dotnet:$PATH"` and `export DOTNET_ROOT="$HOME/.dotnet"` before any `dotnet` command, or `global.json`'s `rollForward: disable` pin will not resolve.

## Verification

**Run on 2026-09-21 against the working tree, SDK 10.0.401 at `~/.dotnet`.**

| Command | Expected | Result |
|---------|----------|--------|
| `dotnet build ActionLedger.sln` | succeeds, zero warnings | Build succeeded, 0 Warning(s), 0 Error(s) |
| `dotnet test ActionLedger.sln` | all test projects pass | Passed — 7 projects, **57 tests**, 0 failed (19 before this story, 38 added) |
| every `bin`/`obj` wiped, then restore + build + test | both succeed | Passed — 57, failed 0 |
| `dotnet build -c Release` + `dotnet test -c Release --no-build` (CI's exact shape) | green | Passed — 57, failed 0 |
| `dotnet run --project src/ActionLedger.Api -- --export-openapi` | writes the contract, exits 0, never listens | `Wrote .../web/actionledger-web/openapi.json`, exit 0, no port bound |
| the same command from `/tmp` | writes into the repository, not the cwd | wrote the repository path |
| export twice to two paths, `diff` | byte-identical | identical, and identical to the committed file |
| `curl localhost:5199/health` | 200 | `{"status":"healthy"}` [200] |
| `curl localhost:5199/api/v1/nope` | ProblemDetails 404, not HTML | `{"type":"not-found","title":"The resource was not found.","status":404,"instance":"/api/v1/nope","correlationId":"8322f8..."}`, `application/problem+json` |
| `/swagger/index.html` under `ASPNETCORE_ENVIRONMENT=Compose` | 200 | 200 |
| `/swagger/index.html` under Production | not mapped | 404 |
| `/openapi/v1.json` under Production | served | 200 |
| stdout log lines | structured JSON, `correlationId` on every line | 0 lines without `correlationId`; request lines carry the W3C trace id, startup lines carry `host` |

**Both guards were verified red, then reverted.** A test that cannot fail is not a guard:

| Mutation introduced | Test that failed, and what it said |
|---------------------|-----------------------------------|
| `operationId` in the committed `openapi.json` changed to `GetLiveness` | `OpenApiSnapshotTest.Committed_contract_matches_the_generated_document` — *"web/actionledger-web/openapi.json is out of date. Run: dotnet run --project src/ActionLedger.Api -- --export-openapi. First difference at line 15: committed: "operationId": "GetLiveness", generated: "operationId": "GetHealth""* |
| a probe action given `[HttpGet("~/escaped-route")]`, escaping the convention | `RouteDisciplineTests.Every_mapped_endpoint_is_versioned_or_on_the_short_allowlist(includeProbeControllers: True)` — *"These routes sit outside /api/v1 and are not on the allowlist (/health, /openapi, /swagger): /escaped-route"*, naming the offender as the matrix requires |

The startup-validation rows need no mutation: `StartupValidationTests` removes each required key in
turn and asserts the host fails with that key in the message, which is the failing case directly.

**Not verified here — it runs at landing, with a human present:**

- `gh pr checks` — `ci.yml`, `require-linked-issue`, and CodeQL on the pull request. `ci.yml`
  already runs `dotnet test ActionLedger.sln`, so the snapshot test is a required check with no
  workflow change; the Release-configuration run above is that job's exact shape.
