---
title: 'Story 1.7 — One-command compose environment with migrate, api, and web'
type: 'feature'
created: '2026-09-21'
status: 'done'
baseline_commit: '5d2e5036a13387ac713bf7314cebe32598a0ff1c'
baseline_revision: '5d2e5036a13387ac713bf7314cebe32598a0ff1c'
review_loop_iteration: 0
followup_review_recommended: true  # owed; the pass halted on `no subagents`, not run inline
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-6-login-screen-session-shell-and-global-state-patterns.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Everything Epic 1 built — the API, the migration bundle, the seeded users, the Blazor
login shell — can only be run today by a developer with the .NET 10 SDK, and the web app has never
been exercised in a browser at all because nothing serves it on the same origin as the API. There
is no `docker-compose.yml`, no `web` image, no `migrate` image, and CI builds no images.

**Approach:** Add the four-service compose skeleton AD-17 specifies (`db`, `migrate`, `api`, `web`)
with the two Dockerfiles that are missing, an nginx template that reverse-proxies the API onto the
web origin, the compose-only database keys in `.env.example`, a CI job that builds all three
images, and a text-pinned guard test over the whole topology.

## Boundaries & Constraints

**Always:**

- AD-17 verbatim: `db` is `postgres:18-alpine`; `migrate` runs the EF bundle once and `api` starts
  only on `depends_on: condition: service_completed_successfully`; the `api` service declares
  `extra_hosts: ["host.docker.internal:host-gateway"]`; `/health` is the compose healthcheck; the
  api never migrates at startup.
- AD-16 verbatim: `.env.example` lists every key from the spine's Config keys row with a
  placeholder; `Ai__Provider=Fake` is the compose default; the `web` image reads `API_UPSTREAM` at
  container start and templates it into nginx, so one image serves compose and cloud.
- AD-13: `ASPNETCORE_ENVIRONMENT=Compose` on the `api` service — that is the only value besides
  `Development` that serves Swagger UI, and `/swagger` is an acceptance criterion.
- nginx proxies `/api`, `/swagger`, and `/openapi` with `proxy_read_timeout` and
  `proxy_send_timeout` of 200 seconds on each (NFR-1).
- No secret is committed. `.env.example` carries placeholders only; `.env` stays git-ignored.
- The demo needs Docker Desktop only: no host .NET, no Node, no workload install, anywhere.

**Never:**

- No `receiver` service, no `tools/webhook-receiver`, no `Webhooks:Receiver:*` keys — Story 5.2
  owns those, and they are absent from the spine's Config keys row.
- No `DEMO.md`, no README, no five-minute macOS/Windows timing exercise — Epic 6 (Story 6.6) owns
  the demo documentation and Story 1.7's bar is "runs on the development Mac".
- No Playwright, no browser automation, no change to `tests/Web.E2E` — Story 6.4 owns that.
- No `cd.yml`, no GHCR push, no Azure anything. CI **builds** images; it does not publish them.
- Do not change behaviour in `src/ActionLedger.Api`, `src/ActionLedger.Application`,
  `src/ActionLedger.Domain`, `src/ActionLedger.Infrastructure`, or any `.razor`/`.cs` file under
  `src/ActionLedger.Web`. The only sanctioned source-tree edits are the api Dockerfile (image tag
  pin, healthcheck tooling) and net-new deployment files.
- Do not add a NuGet package. The spine's Stack table is the package allowlist; the topology guard
  is a text-pinning test, not a YAML-parsing one.
- Do not commit `Api:BaseAddress` anywhere. Single origin is the whole point: the browser calls
  `/api/v1/...` on its own origin and nginx forwards it.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Cold start | Clean clone, `.env` filled from `.env.example`, no cached images, `docker compose up` | `db` becomes healthy, `migrate` applies the bundle and exits 0, `api` starts and answers `/health`, `web` serves the login page at `http://localhost:8080` | No error expected |
| Ordering | `migrate` exits non-zero | `api` never starts; compose reports the failed dependency | `api` is gated by `service_completed_successfully`, not by `service_started` |
| Re-run | `docker compose up` a second time on an existing volume | `migrate` re-runs the bundle as a no-op and exits 0; the seeder writes nothing (advisory lock + existence checks) | Idempotent by construction |
| Missing `.env` | `.env` absent | Compose fails fast naming the missing env file | `env_file` is declared `required: true` |
| Missing required key | `.env` present, `Jwt__Key` blank | `api` exits at startup with a message naming `Jwt:Key`; `web` never reports healthy upstream | AD-16 startup validation, unchanged |
| Sign-in through nginx | Browser posts the login form on the web origin | `/api/v1/auth/login` is proxied to `API_UPSTREAM`, a token comes back, the shell renders | 401 renders the inline sign-in failure from Story 1.6 |
| Swagger through nginx | `GET /swagger` on the web origin | Swagger UI renders and its `/openapi/v1.json` fetch is proxied too | 404 only if `ASPNETCORE_ENVIRONMENT` is not `Compose` |
| SPA deep link | `GET /meetings` on the web origin, no such file | `index.html` with 200, Blazor routes it to the Not found notice | `try_files` fallback |
| Framework config probe | Blazor's startup `GET /appsettings.json` | 404, exactly as the dev server returns today | The SPA fallback must not answer it with HTML, which would fail JSON parsing and abort startup |
| Slow upstream | An API call takes 150 s | nginx holds the connection and returns the response | Below the 200 s read/send timeout (NFR-1) |
| Local model | `Ai__Provider=LocalOpenAI` pointing at `http://host.docker.internal:1234/v1` | The `api` container resolves the host through `extra_hosts` on Docker Desktop and on Linux engines | Out of this story's test scope; the alias is what is asserted |

</intent-contract>

## Code Map

**Everything below was verified on disk at `5d2e503`.** No `docker-compose.yml`, no web Dockerfile,
no migrate Dockerfile, and no nginx config exist anywhere in the repository today.

### The one Dockerfile that already exists

- `src/ActionLedger.Api/Dockerfile` — four stages: `build` (`mcr.microsoft.com/dotnet/sdk:10.0`,
  restore from `global.json` + `Directory.Build.props` + `Directory.Packages.props` +
  `.config/dotnet-tools.json` + the four `src/ActionLedger.*/*.csproj`, then `COPY src/ src/`),
  `publish`, `bundle`, `final` (`mcr.microsoft.com/dotnet/aspnet:10.0`).
  - `:34-49` the `bundle` stage: `ENV Database__ConnectionString=<design-time placeholder>` then
    `dotnet tool restore && dotnet dotnet-ef migrations bundle --project src/ActionLedger.Infrastructure
    --startup-project src/ActionLedger.Api --configuration Release --no-build --force
    --output /app/migrate/efbundle`. **Copy this stage verbatim into the migrate Dockerfile** — it
    is the only working incantation and it is cache-shared with the api build.
  - `:38` the comment that pins the invocation: *"the real one arrives as `--connection` when
    `migrate` runs the bundle."*
  - `:55-60` installs `libgssapi-krb5-2` so Npgsql's GSSAPI probe does not print a scary line in
    front of `docker compose up`. The migrate image needs the same package for the same reason.
  - `:62-63` `ENV ASPNETCORE_HTTP_PORTS=8080`, `EXPOSE 8080`. `:71` `USER $APP_UID` — non-root, so
    it cannot bind :80. `:73` `ENTRYPOINT ["dotnet", "ActionLedger.Api.dll"]`.
  - **Two things must change here.** (1) `sdk:10.0` floats; `global.json` pins `10.0.401` with
    `rollForward: disable`, so the moment that tag moves past 10.0.401 every image build fails.
    Pin the tag. (2) The final image has no `curl` or `wget` (aspnet:10.0 is Debian slim), so the
    compose healthcheck AD-17 requires has nothing to run. Add `curl` to the existing apt line.
- `.dockerignore` — already excludes `**/bin/`, `**/obj/`, `.env`, `.git/`, `.github/`, `_bmad/`,
  `_bmad-output/`. Nothing to change; both new builds inherit a clean context.

### The web project, from a packaging point of view

- `src/ActionLedger.Web/ActionLedger.Web.csproj` — `Microsoft.NET.Sdk.BlazorWebAssembly`, **zero
  `ProjectReference`** (AD-13), so the restore layer needs only the root props files plus this one
  csproj. `:21` `<GeneratedApiClient>Core/Api/ActionLedgerApiClient.g.cs</GeneratedApiClient>`,
  removed from the Compile glob at `:30`; `:42-58` the `GenerateApiClient` target runs
  `$(NSwagExe_Net100) openapi2csclient /input:openapi.json …` **`BeforeTargets="BeforeCompile"`**.
  Consequence for the Dockerfile: `dotnet publish --no-build` is only safe after a `dotnet build`
  in the same stage — mirror the api Dockerfile's build-then-publish pattern.
- `src/ActionLedger.Web/openapi.json` — tracked; `Core/Api/` is git-ignored, so a container build
  always regenerates the client. NSwag runs from the restored package's `tools/Net100`, so
  generation needs **no network and no `dotnet tool restore`** (`.config/dotnet-tools.json` holds
  only `dotnet-ef`).
- `Directory.Build.props:5-14` — `TreatWarningsAsErrors=true`. The generated client must compile
  warning-clean inside the container exactly as it does on the host; it does today.
- Publish layout, verified against `src/ActionLedger.Web/bin/Release/net10.0/publish/`: the root
  holds `ActionLedger.Web.runtimeconfig.json`, `ActionLedger.Web.staticwebassets.endpoints.json`,
  `dotnet.js`, `openapi.json`, `web.config`, and `wwwroot/`. **Copy `publish/wwwroot/` and nothing
  else** into the nginx webroot — that keeps the contract file and the IIS config out of the image.
- `publish/wwwroot/_framework/` census: 79 `.wasm`, 5 `.js`, 3 `.dat` (ICU), plus 92 `.br` and 92
  `.gz` siblings. **No `.dll`** — .NET 10 ships assemblies as fingerprinted `.wasm` — and **no
  `blazor.boot.json`** (boot config is inlined). nginx's stock alpine `mime.types` already maps
  `.wasm`; `.dat` needs an explicit `application/octet-stream`.
- `src/ActionLedger.Web/wwwroot/index.html:8` `<base href="/" />` — root-hosted, no sub-path
  rewriting. `:11-13` pulls Roboto from Google Fonts over the network. `css/tokens.css` and
  `css/app.css` are **not** fingerprinted, so a blanket `immutable` cache header is wrong.
- `src/ActionLedger.Web/Core/ApiClientRegistration.cs:24-29` — `BaseAddressKey = "Api:BaseAddress"`,
  and the XML doc says it outright: *"Absent — which is the compose and single-origin case, where
  nginx serves the app and proxies `/api` — the host's own origin is used."* `:36-38` falls back to
  `builder.HostEnvironment.BaseAddress`. **The browser never learns `API_UPSTREAM`**; it is purely
  server-side nginx templating.
- There is **no `wwwroot/appsettings.json`**. `WebAssemblyHostBuilder.CreateDefault`
  (`src/ActionLedger.Web/Program.cs:11`) still fetches it at startup and tolerates a 404 — but an
  unguarded `try_files … /index.html` answers it with HTML and 200, which fails JSON parsing and
  aborts the app before the login page renders. This is the single highest-risk detail in the story.
- `src/ActionLedger.Web/App.razor:8-18` — only `/` and `/login` are routable; `/meetings` and
  `/actions` hit the in-app Not found notice. nginx must 200-fallback **every** unknown path to
  `index.html` and let Blazor decide; do not whitelist routes.

### What the api needs from compose

- `src/ActionLedger.Api/Program.cs:84` — `GET /health`, always `200 {"status":"healthy"}`, touches
  no dependency. `:92-105` — `GET /health/ready`, `200 {"status":"ready"}` or `503`, backed by
  `src/ActionLedger.Infrastructure/Persistence/DatabaseReadiness.cs:16,26` with a 5-second probe
  timeout. AD-17 names `/health` as the compose healthcheck; use it, and give the healthcheck a
  timeout above 5 s anyway.
- `src/ActionLedger.Api/Program.cs:107-117` — `app.MapOpenApi()` unconditionally (so
  `/openapi/v1.json` exists everywhere), and `UseSwaggerUI` with `RoutePrefix = "swagger"` **only**
  when the environment is `Development` or `ApiEnvironments.Compose`.
  `src/ActionLedger.Api/Configuration/ApiEnvironments.cs:7` — `Compose = "Compose"`, doc comment
  *"The name compose sets."* Asserted by `tests/Api.Tests/SwaggerExposureTests.cs:17-51`.
- `src/ActionLedger.Api/Routing/ApiRoutePrefix.cs:17` — `UnversionedPaths = ["/health", "/openapi",
  "/swagger"]`. Everything else is under `/api/v1`.
- `src/ActionLedger.Api/Configuration/ApiOptionsRegistration.cs:13-32` — every section is
  `.ValidateDataAnnotations().ValidateOnStart()`. Hard-required with no shipped default:
  `Jwt:Key` (≥32 chars), `Jwt:Issuer`, `Database:ConnectionString`, and — via
  `SeedOptionsValidator` at `:90-102` — `Seed:DefaultPassword` whenever `Seed:Enabled` is true,
  which `src/ActionLedger.Api/appsettings.json:26-28` makes the default. **Those four keys are the
  minimum compose must inject**; `Ai:*` and `Webhooks:*` already have image defaults.
- `src/ActionLedger.Infrastructure/Seed/DemoDataSeeder.cs:31-34` — an `IHostedService` inside the
  api container, idempotent, guarded by `pg_advisory_xact_lock`
  (`src/ActionLedger.Infrastructure/Seed/SeedRepository.cs:23,37-39`). Users `dana`, `priya`
  (ActionOfficer) and `marcus` (Lead) at `:46-51`, all sharing `Seed:DefaultPassword`. It assumes
  the schema exists — it does not wait for `migrate`, which is exactly why the ordering gate matters.
- `src/ActionLedger.Infrastructure/InfrastructureRegistration.cs:75-80` — `EnableRetryOnFailure()`,
  added for "a transient connection failure on a cold compose start".
- Logging is `CompactJsonFormatter` to stdout unconditionally
  (`src/ActionLedger.Api/Program.cs:28-33`); nothing in compose needs to enable it.

### `.env.example` as it stands

- `.env.example` — already carries all 17 keys from the spine's Config keys row in `__` form,
  including `:44-50` `Database__ConnectionString=Host=db;Port=5432;Database=actionledger;Username=actionledger;Password=replace-me`,
  `:68` `Seed__Enabled=true`, `:75` `Seed__DefaultPassword=…`, and `:77-80` the `# --- Web image ---`
  block with `API_UPSTREAM=http://api:8080`. AC3 is therefore **almost** satisfied already; what is
  missing is the `db` service's own `POSTGRES_*` credentials, which have no home today and which
  must agree with the connection string above.
- `.gitignore:97-99` — `.env` and `.env.local` are ignored. Nothing to change.

### Test scaffolding to reuse, not reinvent

- `tests/Architecture.Tests/ProjectFile.cs:95-115` — `internal static readonly DirectoryInfo
  RepositoryRoot`, found by walking up to `ActionLedger.sln`. `WebStructureTests` already shares it;
  the new topology test shares it too. **Do not write a second root-finder.**
- `tests/Web.Tests/ThemeAndTokenTests.cs` — the `TheoryData<string,string>` + assert-literal shape
  for pinning values that live in a non-C# file. That is the precedent the topology guard copies.
- `tests/Architecture.Tests/Architecture.Tests.csproj` — `xunit.v3` and
  `NetArchTest.eNhancedEdition` only. The new test adds no package.
- `tests/Infrastructure.Tests/PostgresFixture.cs:12,17` — `new PostgreSqlBuilder("postgres:18-alpine")`
  with the comment *"The image matches the compose service"*. The compose file must keep that true.
- `tests/Web.E2E/SmokeTests.cs:6-9` — *"this project must not need a browser, a container, or a
  network today."* Leave it exactly as it is; Story 6.4 changes it.
- `.github/workflows/ci.yml:5-6` — *"Image builds join this workflow in Story 1.7."* One job,
  `build-and-test`, on `ubuntu-latest` with `actions/setup-dotnet@v6` pinned to `10.0.401`.

## Tasks & Acceptance

**Execution:**

- `src/ActionLedger.Api/Dockerfile` — pin the `build` stage to an SDK tag that actually contains
  10.0.401 (verify by building; `sdk:10.0` is a floating tag and `global.json` sets
  `rollForward: disable`), and add `curl` to the existing `apt-get install` line — without it the
  `api` healthcheck AD-17 mandates has no binary to run, and with a floating tag the whole compose
  build breaks silently on the next base-image refresh.
- `src/ActionLedger.Api/Dockerfile.migrate` — a new multi-stage build whose `build`/`bundle` stages
  are byte-identical to the api Dockerfile's (so Docker's layer cache serves them) and whose final
  stage is `aspnet` + `libgssapi-krb5-2` carrying only `/app/migrate/efbundle`, with
  `ENTRYPOINT ["/app/migrate/efbundle"]` — AD-17 requires `migrate` to have its own Dockerfile, and
  building it from the same context at the same commit keeps the schema and the code that expects
  it at one SHA.
- `src/ActionLedger.Web/Dockerfile` — a new multi-stage build: `sdk` stage restores from the root
  props plus this one csproj, `dotnet build` then `dotnet publish --no-build` (the NSwag target
  hangs off `BeforeCompile`), final stage `nginx:1.30-alpine` with `publish/wwwroot/` copied to
  `/usr/share/nginx/html`, `nginx.conf.template` copied to
  `/etc/nginx/templates/default.conf.template`, `ENV API_UPSTREAM=http://api:8080` as the in-image
  default and `ENV NGINX_ENVSUBST_FILTER='^API_UPSTREAM$'` so envsubst cannot eat nginx's own
  `$uri`/`$host` — this is the "no Node stage" web image the 2026-09-21 sprint change promised.
- `src/ActionLedger.Web/nginx.conf.template` — one `server` block on :80: a `types { }` addition for
  `.dat` (and defensively `.blat`/`.dll`); `location = /appsettings.json` and
  `location ~ ^/appsettings\..+\.json$` returning 404 so Blazor's startup probe is not answered with
  HTML; `location /` with `try_files $uri $uri/ /index.html` and `no-cache` on `index.html`;
  `gzip_static on` so the published `.gz` siblings are used; and `proxy_pass ${API_UPSTREAM}` blocks
  for `/api/`, `/swagger`, `/openapi`, and `/health`, each carrying
  `proxy_read_timeout 200s; proxy_send_timeout 200s;` plus the usual `Host`/`X-Forwarded-*` headers
  — `/health` is proxied because the generated client exposes `GetHealthAsync`/`GetReadinessAsync`
  and the SPA fallback would otherwise answer those with a 200 page of HTML.
- `docker-compose.yml` — repository root, the name the structural seed pins. Four services and one
  named volume: `db` (`postgres:18-alpine`, `POSTGRES_*` from `.env`, `pg_isready` healthcheck,
  `db-data` volume, no published port); `migrate` (built from `Dockerfile.migrate`,
  `depends_on: db: service_healthy`, `restart: "no"`, `command` passing `--connection` with the
  connection string); `api` (built from `src/ActionLedger.Api/Dockerfile`,
  `depends_on: migrate: service_completed_successfully`, `ASPNETCORE_ENVIRONMENT=Compose`,
  `extra_hosts: ["host.docker.internal:host-gateway"]`, a `curl`-based `/health` healthcheck, no
  published port); `web` (built from `src/ActionLedger.Web/Dockerfile`, `API_UPSTREAM`,
  `depends_on: api: service_healthy`, `8080:80`). Every service that reads configuration declares
  `env_file` with `required: true`. A header comment carries the two-step quickstart
  (`cp .env.example .env`, fill `Jwt__Key`/`Seed__DefaultPassword`/the password pair, then
  `docker compose up`) and the URL — compose auto-loads `.env` for *interpolation* only, so the
  `env_file` declaration is what actually puts the keys inside the containers.
- `.env.example` — append a `# --- Compose database service ---` block with `POSTGRES_USER`,
  `POSTGRES_DB`, and `POSTGRES_PASSWORD`, documenting that they must agree with the username,
  database, and password inside `Database__ConnectionString` above — the `db` service needs its own
  credentials and a silent divergence between the two halves is the most likely first-run failure.
- `.github/workflows/ci.yml` — add an `images` job that checks out, builds `api`, `migrate`, and
  `web` with `docker build` (no push, no registry), and runs `docker compose config --quiet` against
  `.env.example` to prove the compose file parses and interpolates — AC3 requires CI to build all
  images and NFR-11 makes the pipeline itself a deliverable.
- `tests/Architecture.Tests/ComposeTopologyTests.cs` — a new text-pinning guard over the files above,
  in the assembly that already treats repository files as the subject under test and already owns
  `ProjectFile.RepositoryRoot`. It asserts: every one of the 17 spine config keys appears in
  `.env.example` in `__` form; `Ai__Provider=Fake`; the `POSTGRES_*` trio agrees with
  `Database__ConnectionString`; no placeholder looks like a real secret; the compose file names
  exactly `db`, `migrate`, `api`, `web` (no `receiver`); `postgres:18-alpine`;
  `service_completed_successfully` gating `api` on `migrate`; `host.docker.internal:host-gateway`;
  `ASPNETCORE_ENVIRONMENT` of `Compose`; the `/health` healthcheck; `nginx:1.30-alpine` in the web
  Dockerfile; `proxy_read_timeout 200s` and `proxy_send_timeout 200s` in each of the four proxied
  locations; and that `ci.yml` builds all three images — none of this is observable from an
  assembly, so without it AD-17 has no enforcement at all.

**Acceptance Criteria:**

- Given a clean clone with `.env` copied from `.env.example` and its three secrets filled, when I
  run `docker compose build --no-cache` and `docker compose up` on the development Mac, then `db`
  becomes healthy, `migrate` applies the bundle and exits 0, `api` starts only afterwards and
  reports healthy, `web` serves the login page at `http://localhost:8080`, and no step needs a host
  .NET SDK, Node, or a workload install.
- Given the running stack, when I sign in through the browser as a seeded user, then the POST is
  proxied to the API on the same origin, the shell renders with the display name and role, `/meetings`
  shows the Not found notice, and the browser console reports no error — this is the browser pass
  Story 1.6 recorded as unverifiable without compose.
- Given the running stack, when I open `/swagger` and `/openapi/v1.json` on the web origin, then both
  render through the proxy.
- Given `docker compose down` followed by `docker compose up` on the existing volume, when the stack
  comes back, then `migrate` exits 0 with nothing to apply, the seeder writes nothing, and sign-in
  still works.
- Given each pinned topology fact, when it is mutated — the `service_completed_successfully`
  condition weakened, `extra_hosts` dropped, a proxy timeout lowered, a config key removed from
  `.env.example`, the postgres tag changed — then `ComposeTopologyTests` fails, verified red and then
  reverted.
- Given a clean clone and a stock SDK 10.0.401 with no workloads, when I run
  `dotnet build ActionLedger.sln` and the test projects, then everything passes with zero warnings.
- Given the pull request, when CI runs, then `build and test`, the new image-build job, `linked
  issue`, and CodeQL all pass.

## Spec Change Log

## Review Triage Log

### 2026-09-21 — Review pass
- verdicts: 31 findings — high 0, medium 12, low 9, false 6, maybe-false 0
- findings:
  - `[medium]` `[patch]` blind-hunter: `.playwright-mcp/` browser-snapshot debris would be committed; `.gitignore` covers `**/.playwright/` but not that directory — confirmed on disk (four files, two empty). Fix: the stray files were deleted and `.playwright-mcp/` added to the Playwright block in `.gitignore`.
  - `[medium]` `[patch]` blind-hunter: postgres only honours `POSTGRES_PASSWORD` when it initialises the volume, so hardening the password after a first run fails authentication forever and nothing says `docker compose down -v`. Fix: one line added to the compose header quickstart.
  - `[medium]` `[patch]` blind-hunter: `${Database__ConnectionString}` is unguarded, so an unset key interpolates to `""`, compose warns and exits 0, and `migrate` runs `efbundle --connection ""`. Fix: `:?` error form on it and on the three `POSTGRES_*` keys.
  - `[low]` `[reject]` blind-hunter: the connection string is passed as argv and so appears in `docker inspect`/`docker compose config`. Rejected — the same value is already in the container environment via `env_file`, and reading either needs the host access that reading `.env` needs, so no privilege boundary is crossed; the fix would change the bundle's invocation mechanism documented in Story 1.3's Dockerfile rather than being a direct correction.
  - `[medium]` `[patch]` blind-hunter: `env_file` on all four services puts `Jwt__Key`, `Seed__DefaultPassword` and `Ai__AzureOpenAI__ApiKey` into the postgres container and the host-published nginx container, which read none of them. Fix: `env_file` dropped from `db` and `web` for explicit `environment:` blocks; `required: true` kept on `migrate` and `api`; the guard test narrowed and a negative assertion added.
  - `[low]` `[patch]` blind-hunter: `web` has no healthcheck, so `docker compose up --wait` returns before nginx serves. Fix: busybox-`wget` healthcheck, which the new CI smoke step now gates on.
  - `[medium]` `[patch]` blind-hunter: nothing anywhere starts the stack — the topology is pinned only as file text. Fix: a `Smoke the stack` CI step (`up -d --wait`, migrate exit 0, SPA, `/appsettings.json` 404, `/health`, `/api/v1/users` 401) plus teardown with `if: always()`.
  - `[low]` `[patch]` blind-hunter: `docker/setup-buildx-action@v3` is set up and never used — the three builds pass no cache flags. Fix: the step was deleted.
  - `[low]` `[patch]` blind-hunter: `The_migrate_service_…` and `The_web_service_…` assert a bare `Assert.Contains("condition: service_healthy")`, so repointing a `depends_on` leaves them green — confirmed by reading both tests. Fix: an `AssertDependsOn` helper naming both ends, used by all three dependency tests.
  - `[low]` `[patch]` blind-hunter: `Services()` appends every non-header line, so a service body absorbs the next service's two-space-indented banner comment and the class doc's "a match can never come from the wrong service" is false. Fix: comment lines are skipped; demonstrated that `db` previously absorbed the migrate banner's prose.
  - `[low]` `[reject]` blind-hunter: `http://api:8080` appears in three files and the password is duplicated between `POSTGRES_PASSWORD` and `Database__ConnectionString`. Rejected — the compose `environment:` copy stopped being redundant once `env_file` was removed from `web`, the image `ENV` is the deliberate cloud default, and eliminating the password duplication would contradict `.env.example`'s documented `Database__ConnectionString`, which host-side `dotnet ef` also uses; the duplication is guarded by a test.
  - `[low]` `[patch]` blind-hunter: the nginx template repeats eight directives four times and omits `server_tokens off;` and a nosniff header. Partially patched — `server_tokens off;` added (the only host-exposed port advertised its exact version on every response). The de-duplication and the nosniff header were rejected: collapsing the blocks into an included snippet changes the template's structure and the test's parsing model for no behavioural gain, and a security-header policy is a deliberate decision this story does not make.
  - `[medium]` `[patch]` edge-case: `pg_isready` with no host answers over the unix socket, so `db` can report healthy while initdb's temporary server still has `listen_addresses=''` and `migrate` then fails with connection refused. Fix: `-h 127.0.0.1`.
  - `[low]` `[reject]` edge-case: `migrate` has no `restart: on-failure` for a transient database failure. Rejected — `depends_on: db: service_healthy` plus the TCP healthcheck fix closes the transient window, and a restart policy on a one-shot contradicts a deliberate documented decision and complicates the `service_completed_successfully` gate.
  - `[medium]` `[patch]` edge-case: `POSTGRES_PASSWORD` edited after a prior `up` — same defect as the blind-hunter row above; fixed by the same header line.
  - `[medium]` `[patch]` edge-case: unguarded `${Database__ConnectionString}` — same defect as above; fixed by the same `:?` change.
  - `[medium]` `[patch]` edge-case: an `API_UPSTREAM` carrying a trailing slash or path makes `proxy_pass` strip the location prefix, and nothing guards it. Fixed as part of the upstream rework: `proxy_pass $upstream$request_uri;` passes the original URI explicitly, and a new assertion pins that shape.
  - `[medium]` `[patch]` edge-case: nginx resolves `api` once at config load, so a recreated `api` on a new IP 502s every proxied request. **Reproduced**: squatting the old address and recreating `api` gave a permanent 502 while `docker compose ps` reported api healthy, db healthy, web up — and a plain `docker compose up -d` did not refresh `web`. Fix: server-level `resolver ${NGINX_RESOLVER} valid=10s ipv6=off;` (image ENV defaulting to Docker's `127.0.0.11`, envsubst filter widened), `set $upstream ${API_UPSTREAM};` and `proxy_pass $upstream$request_uri;`. Re-verified: the same repro now answers 200/401 with no outage window.
  - `[false]` `[reject]` edge-case: nginx's 1 MB `client_max_body_size` would 413 a long transcript POST. Refuted — the largest documented body is Meeting notes capped at 50,000 characters (≈200 KB worst case in UTF-8), well under 1 MB, so the 413 does not occur at the cited location.
  - `[low]` `[patch]` edge-case: bare `/api` and a missing `/_framework/` asset fall through to the SPA rule and return 200 HTML. The asset half was confirmed live (`/_framework/does-not-exist.wasm` → 200 text/html) and patched with `try_files $uri =404;` on `/_framework/` and `/_content/`. The bare-`/api` half was rejected: no consumer requests it — the generated client always calls `/api/v1/...` — and the fix adds a branch.
  - `[medium]` `[patch]` edge-case: `env_file` injects the whole `.env` into `db` and `web` — same defect as the blind-hunter row; fixed by the same change.
  - `[medium]` `[patch]` edge-case: CI validates parse and interpolation only, so the startup chain is never exercised — same defect as the blind-hunter row; fixed by the same smoke step.
  - `[false]` `[reject]` edge-case: the spec says the types block adds `.dat` "and defensively `.blat`/`.dll`" while the template declares only `dat` and `blat`. Rejected — its only fix is to edit this build's spec, which triage does not permit, and the template's own comment already records why `.dll` is omitted (the stock mime map maps it, and re-declaring earns a duplicate-extension warning).
  - `[medium]` `[patch]` edge-case: `.playwright-mcp/` scratch files are added and not ignored — same defect as the blind-hunter row; fixed by the same change.
  - `[medium]` `[patch]` verification-gap: `location /api/` proxy semantics are pinned only as file text and nothing ever starts nginx — a trailing slash on `proxy_pass` ships a green pipeline and a login page that cannot log in. Filed pre-verified with disposition `patch`. Fix: the CI smoke step plus the explicit `$request_uri` and its assertion.
  - `[medium]` `[patch]` verification-gap: the migrate image's entrypoint is never executed and its runtime base image is asserted nowhere — swapping `aspnet:10.0` for `runtime-deps` keeps every text assertion green while the demo breaks on the next clone. Filed pre-verified with disposition `patch`. Fix: the CI smoke step asserts `migrate`'s exit code 0, which executes the entrypoint, the base image, `USER $APP_UID`'s access to `/app/migrate/`, and `--connection` handling.
  - `[medium]` `[patch]` verification-gap: `The_db_service_runs_the_postgres_image_the_tests_run` restates `postgres:18-alpine` as a third literal while `tests/Infrastructure.Tests/PostgresFixture.cs:17` holds its own copy, so a one-sided bump leaves the suite proving the schema against a server the demo does not run. Filed pre-verified with disposition `patch`. Fix: the tag is now read out of `PostgresFixture.cs`, the way the SDK tag is read out of `global.json`.
  - `[medium]` `[patch]` verification-gap (other): every service gets the whole `.env` — same defect as above; fixed by the same change.
  - `[medium]` `[patch]` verification-gap (other): four `.playwright-mcp` snapshot files are added to the repository — same defect as above; fixed by the same change.
  - `[false]` `[reject]` intent-alignment: the story's acceptance criteria are machine-bound (a filled `.env`, `docker compose up`, a browser sign-in) and could be read as owed to an operator under `awaiting-operator`. Refuted — every one was performed on this machine with Docker Desktop and a browser, both inside agent tool reach; none names a domain, DNS record, API key or vendor console, so the intent's conditional does not fire and `done` is the correct terminal status.
  - `[false]` `[reject]` intent-alignment: the spec has no `## Auto Run Result`, `status` reads `in-review`, and nothing is committed. Refuted — all three describe the workflow mid-flight at the moment the diff was staged; the finalize step writes the Auto Run Result, sets `status: done`, and commits. `sprint-status.yaml` was correctly never touched.

## Design Notes

**Why `migrate` gets its own Dockerfile that duplicates two stages.** AD-17 says each of `api`,
`web`, `receiver`, and `migrate` has its own Dockerfile *and* that the bundle is produced in the api
multi-stage build, and Story 1.7's AC1 names a `migrate` Dockerfile explicitly. Docker cannot
`COPY --from` a stage in another file, and Compose's `additional_contexts: service:api` would make
the migrate image depend on the api image being built first — an ordering compose does not promise
at build time. Duplicating the `build` and `bundle` stages byte-for-byte costs nothing at build time
(identical instructions over an identical context hit the same layer cache) and keeps the two images
independently buildable from one commit, which is the property AD-17 actually wants.

**Why the guard test pins text instead of parsing YAML.** A structured assertion
(`services.api.depends_on.migrate.condition`) would be less brittle, but it needs a YAML package,
and the spine's Stack table is the package allowlist — adding one for a test is a bigger deviation
than a regex. `ThemeAndTokenTests` already pins 40 literal values out of a non-C# file, so the shape
is established. Scope each assertion to a service block rather than searching the whole document, so
a match cannot come from the wrong service.

**The `appsettings.json` 404 is load-bearing, not defensive.** `WebAssemblyHostBuilder.CreateDefault`
fetches `appsettings.json` and `appsettings.{Environment}.json` over HTTP at startup. It tolerates a
404 — that is what the dev server returns today, since the file does not exist. Under a naive SPA
fallback it instead receives `index.html` with a 200 and a `text/html` content type, and the JSON
parse throws before the login page ever renders. The nginx template must reproduce the dev server's
404, and the browser pass in the acceptance criteria is what proves it.

**Envsubst discipline.** `nginx:1.30-alpine`'s entrypoint runs
`/docker-entrypoint.d/20-envsubst-on-templates.sh` over `/etc/nginx/templates/*.template`. Without a
filter it substitutes *every* `$name` in the file, which would blank out `$uri`, `$host`,
`$remote_addr` and `$proxy_add_x_forwarded_for`. `NGINX_ENVSUBST_FILTER='^API_UPSTREAM$'` restricts
it to the one variable that is meant to be templated. Set it in the Dockerfile, not in compose, so
the property travels with the image to cloud.

## Verification

**Commands:**

- `/Users/brianspann/.dotnet/dotnet build ActionLedger.sln --nologo` — expected: zero warnings, zero
  errors.
- `for p in Domain.Tests Application.Tests Infrastructure.Tests Api.Tests Architecture.Tests Eval Web.E2E Web.Tests; do ./tests/$p/bin/Debug/net10.0/$p; done`
  — expected: every project green, including the new `ComposeTopologyTests`. (`dotnet test` on this
  machine reports "Zero tests ran"; the app hosts run correctly.)
- `docker compose config --quiet` with a `.env` present — expected: exit 0, no interpolation warning.
- `docker compose build --no-cache` — expected: all three images build; watch that the SDK tag
  resolves 10.0.401 under `rollForward: disable`.
- `docker compose up -d` then `docker compose ps` — expected: `db` healthy, `migrate` exited 0,
  `api` healthy, `web` running.
- `curl -fsS http://localhost:8080/api/v1/auth/login -X POST -H 'content-type: application/json' -d '{"username":"dana","password":"<the .env value>"}'`
  — expected: 200 with a token, proving the proxy path.
- `curl -fsS -o /dev/null -w '%{http_code}' http://localhost:8080/appsettings.json` — expected: 404,
  not 200.
- `curl -fsS -o /dev/null -w '%{http_code}' http://localhost:8080/meetings` — expected: 200 (the SPA
  fallback).
- `docker compose down && docker compose up -d` — expected: `migrate` exits 0 again, sign-in still
  works.
- With `.env` temporarily renamed, `docker compose config` — expected: a non-zero exit naming the
  missing env file, not a silent start.
- With `Jwt__Key` blanked in a scratch env file, `docker compose up api` — expected: the api
  container exits and its log names `Jwt:Key`. Restore the file afterwards.

**Matrix coverage map** (every row of the I/O & Edge-Case Matrix and the check that covers it):

| Matrix row | Covering check |
|---|---|
| Cold start | `docker compose build --no-cache` + `up -d` + `ps`, and the browser pass |
| Ordering | `ComposeTopologyTests` pins `service_completed_successfully` on `api`→`migrate`; `docker compose ps` shows `migrate` exited 0 before `api` started |
| Re-run | `docker compose down && docker compose up -d`, then sign in again |
| Missing `.env` | `docker compose config` with `.env` renamed |
| Missing required key | `docker compose up api` with `Jwt__Key` blanked |
| Sign-in through nginx | the `curl` login POST on `http://localhost:8080` and the browser pass |
| Swagger through nginx | `curl` of `/swagger` and `/openapi/v1.json` on the web origin |
| SPA deep link | `curl -w '%{http_code}' http://localhost:8080/meetings` → 200 |
| Framework config probe | `curl -w '%{http_code}' http://localhost:8080/appsettings.json` → 404 |
| Slow upstream | `ComposeTopologyTests` pins `proxy_read_timeout 200s` and `proxy_send_timeout 200s` in each proxied location (a 150 s live call is not reproducible without a stalling upstream) |
| Local model | `ComposeTopologyTests` pins `host.docker.internal:host-gateway` on the `api` service |

**Manual checks:**

- Open `http://localhost:8080` in a browser, sign in as a seeded user, and confirm the app bar shows
  the display name and role, `/meetings` renders the Not found notice, and the console is clean.
  Record the result in the Verification section of this spec — Story 1.6 deferred exactly this pass
  to Story 1.7.
- Open `http://localhost:8080/swagger` and confirm the UI lists the v1 operations.

**Manual check results** (recorded 2026-09-21 on the development Mac, Docker 29.4.0 / Compose
v5.1.2, arm64; this is the browser pass Story 1.6 deferred to this story):

- `docker compose build --no-cache` — all three images built, zero warnings inside every
  `dotnet build`/`dotnet publish` stage. The pinned `sdk:10.0.401` tag resolves under
  `rollForward: disable`.
- `docker compose up -d` then `ps` — `db` healthy, `migrate` exited 0, `api` healthy, `web` up on
  `8080:80`. First run applied the migration and seeded three users; nothing raced the schema.
- Browser at `http://localhost:8080` — the login page rendered, sign-in as `dana` POSTed to
  `/api/v1/auth/login` on the web origin and succeeded, the app bar showed `Dana Whitfield` and
  `Action Officer` with `Meetings` and `Actions`, `/meetings` rendered the `Not found.` notice with
  its link to the parent list, and the console reported **zero** messages at warning level or above
  across the whole session.
- `http://localhost:8080/swagger` — 301 to `/swagger/index.html`, and Swagger UI listed the v1
  operations (`/health`, `/health/ready`, `POST /api/v1/auth/login`, `GET /api/v1/users`) with
  `/openapi/v1.json` fetched through the proxy.
- `curl` matrix — `/appsettings.json` **404**, `/appsettings.Production.json` **404**, `/meetings`
  **200 text/html**, `/health` `{"status":"healthy"}`, `/health/ready` `{"status":"ready"}`,
  `/openapi/v1.json` 200 `application/json`, a login POST 200 with a token, a wrong password 401.
  A `_framework/*.wasm` served as `application/wasm`, an ICU `.dat` as `application/octet-stream`,
  `dotnet.js` with `Content-Encoding: gzip` from the published sibling, `index.html` with a single
  `Cache-Control: no-cache`.
- `docker compose down && docker compose up -d` — `migrate` logged "No migrations were applied. The
  database is already up to date." and exited 0; the seeder logged `CreatedCount: 0`; sign-in as
  `marcus` still returned 200. No GSSAPI line appeared in any container's log.
- `.env` renamed, `docker compose config` — exit 1 with
  `env file …/.env not found: stat …/.env: no such file or directory`.
- `Jwt__Key` blanked, `docker compose up api` — the container exited with
  `DataAnnotation validation failed for 'JwtOptions' members: 'Key' with the error: 'Jwt:Key is
  required.'`. The file was restored and the stack came back green.
- `ComposeTopologyTests` mutation pass — ten mutations were applied one at a time and each turned
  the suite red before being reverted: the `service_completed_successfully` condition weakened to
  `service_started`; `extra_hosts` dropped; one `proxy_read_timeout` lowered to `100s`;
  `Webhooks__LeaseSeconds` removed from `.env.example`; the postgres tag changed to `17-alpine`;
  the web image's SDK tag unpinned to `10.0`; the `location = /appsettings.json` 404 deleted;
  `curl` removed from the api image; a CI image-build path changed; and a fifth `receiver` service
  added. The reverted tree is green at 86 tests.
- Independently re-run after the implementation returned, from `docker compose down -v`: build
  succeeded with 0 warnings, all eight test projects green (384 tests; `Architecture.Tests` 86),
  `docker compose config --quiet` clean, the start-order chain reproduced exactly
  (`db` healthy → `migrate` Exited (0) → `api` healthy → `web` up), and every `curl`, fail-fast, and
  browser result above reproduced. Every row of the I/O & Edge-Case Matrix has a covering check that
  ran and passed.

**Review round 1 — re-verification** (2026-09-21, same machine). The review found a live outage
the text pins could not see, plus a set of hardening gaps; all were fixed and re-checked:

- **Stale upstream address.** `proxy_pass ${API_UPSTREAM};` renders to a literal, so nginx resolved
  `api` once at config load and held it. Reproduced: squat `api`'s address with a throwaway
  container, recreate `api` so it lands on a new one, and every proxied path 502s while
  `docker compose ps` reports the whole stack up — and `docker compose up -d` does not recreate
  `web`, so it never recovers. Fixed with a server-level `resolver ${NGINX_RESOLVER} valid=10s
  ipv6=off;` (image ENV, default `127.0.0.11`) plus `set $upstream ${API_UPSTREAM};` and
  `proxy_pass $upstream$request_uri;` in each of the four blocks. Re-verified: api moved
  `192.168.147.5 → 192.168.147.3`, `web` untouched, and after the 10 s TTL `/health` 200,
  `/api/v1/users` 401, `/swagger/index.html` 200, `/openapi/v1.json` 200. `/swagger` still 301s to
  `/swagger/index.html` and renders.
- **db healthcheck on the unix socket** — now `pg_isready -h 127.0.0.1`, so it cannot report healthy
  while initdb's temporary server still has `listen_addresses=''`.
- **Unguarded interpolation** — `${Database__ConnectionString:?…}` and the three `POSTGRES_*` keys in
  the `:?` error form, so a blank key fails by name instead of running `efbundle --connection ""`.
- **Secret blast radius** — `env_file` dropped from `db` and `web` (explicit `environment:` blocks
  instead); kept `required: true` on `migrate` and `api`, so an absent `.env` still fails naming it.
- **Missing assets** — `location /_framework/` and `location /_content/` with `try_files $uri =404;`.
  Re-verified: `/_framework/does-not-exist.wasm` **404** (was 200 text/html), a real
  `_content/MudBlazor/MudBlazor.min.css` still 200, `/meetings` still 200.
- Also: `server_tokens off;` (`Server: nginx`, no version), a busybox-`wget` healthcheck on `web` so
  `up --wait` gates on nginx serving, a header line saying a database password change after the
  first run needs `docker compose down -v`, and the unused `docker/setup-buildx-action` step removed.
- **CI now runs the stack.** New `Smoke the stack` step: `docker compose up -d --wait`, assert
  `migrate` exit code 0, then `/` (SPA), `/appsettings.json` (404), `/health`
  (`{"status":"healthy"}`) and an unauthenticated `/api/v1/users` (401), then `down -v` with
  `if: always()`. Confirmed locally against the **unmodified placeholder `.env.example`**: every
  assertion passed, so CI needs no secret.
- **Guard hardening.** `depends_on` assertions now name both ends; `Services()` skips comment lines
  (without it the `db` body absorbed the migrate banner's prose, so "a match can never come from the
  wrong service" was false); the postgres tag is read out of `PostgresFixture.cs` instead of being a
  second copy. New assertions cover the variable `proxy_pass`, the resolver, the asset 404s,
  `server_tokens`, the TCP healthcheck, the `:?` guards, `db`/`web` not taking the whole env file,
  the `web` healthcheck, and the CI smoke step.
- `ComposeTopologyTests` is 93 tests, green. Sixteen fresh mutations — one per new guard — each
  turned it red and were reverted.

## Auto Run Result

Status: done

### Implemented change

The four-service compose skeleton AD-17 specifies, plus the two Dockerfiles and the nginx
template that were missing, the compose-only database keys, a CI job that builds all three
images and then runs the stack, and a guard test over the whole topology. `docker compose up`
from a clean clone now brings up `db` → `migrate` → `api` → `web` in that order and serves a
working login page on a single origin at `http://localhost:8080`, with Docker Desktop as the only
prerequisite.

### Files changed

- `docker-compose.yml` (new) — `db` (`postgres:18-alpine`, TCP `pg_isready`, `db-data` volume),
  one-shot `migrate` gated on `db` healthy, `api` gated on `migrate` exiting 0 with
  `ASPNETCORE_ENVIRONMENT=Compose`, `extra_hosts` and a `/health` healthcheck, and `web` on
  `8080:80` as the only published port.
- `src/ActionLedger.Api/Dockerfile.migrate` (new) — `build`/`bundle` stages byte-identical to the
  api image's, final stage carrying only `/app/migrate/efbundle`.
- `src/ActionLedger.Web/Dockerfile` (new) — SDK build → publish → `nginx:1.30-alpine` serving
  `wwwroot/` only, with `API_UPSTREAM`/`NGINX_RESOLVER` templated at container start. No Node stage.
- `src/ActionLedger.Web/nginx.conf.template` (new) — re-resolving proxy for `/api/`, `/swagger`,
  `/openapi` and `/health` at 200 s read/send timeouts, the `/appsettings*.json` 404 pair, asset
  404s for `/_framework/` and `/_content/`, `gzip_static`, the `.dat` MIME type, and the SPA fallback.
- `tests/Architecture.Tests/ComposeTopologyTests.cs` (new) — 93 text-pinning guards over compose,
  the three Dockerfiles, the nginx template, `.env.example` and `ci.yml`. No new package.
- `src/ActionLedger.Api/Dockerfile` — SDK tag pinned to 10.0.401; `curl` added for the healthcheck.
- `.env.example` — the `POSTGRES_USER`/`POSTGRES_DB`/`POSTGRES_PASSWORD` block the `db` service needs.
- `.github/workflows/ci.yml` — an `images` job that builds all three images, parses the compose file
  against the placeholder env, then starts the stack and probes it.
- `.gitignore` — `.playwright-mcp/` added to the Playwright block.

### Review findings

31 findings across four layers — 12 medium, 9 low, 6 false, 0 high, 0 maybe-false.

- **Patched (12 entries: 8 medium, 4 low).** nginx resolving the `api` hostname once at config load
  (reproduced as a permanent 502 behind a green `docker compose ps`); `pg_isready` answering over
  the unix socket during initdb; unguarded `${...}` interpolation of the connection string and the
  `POSTGRES_*` trio; `env_file` handing the JWT key, the Azure key and the demo password to the
  postgres and nginx containers; the undocumented `down -v` needed after a database-password change;
  CI never starting the stack; the missing `web` healthcheck; missing assets answering 200 HTML;
  `server_tokens`; the unused buildx step; three weaknesses in the guard test itself (bare
  `depends_on` assertions, a service-block parser that absorbed banner comments, and a third copy of
  the postgres tag); and `.playwright-mcp/` scratch output that would have been committed.
- **Deferred: none.**
- **Rejected (7).** The connection string in argv — the same value is already in the container
  environment and reading either needs the same host access. A `restart: on-failure` on `migrate` —
  the healthcheck fix closes the window and a restart policy contradicts a deliberate decision.
  `client_max_body_size` — refuted: notes cap at 50,000 characters, far under 1 MB. Bare `/api`
  falling back to the SPA — no consumer requests it. The `.dll` MIME entry — its only fix is to edit
  this build's spec. The `API_UPSTREAM`/password duplication — deliberate, guarded, and removing it
  would contradict `.env.example`. The nginx block de-duplication and a nosniff header — structural
  churn for no behavioural gain, and a header policy is not this story's decision. Two
  intent-alignment observations were refuted: every acceptance criterion was performed on this
  machine, so the `awaiting-operator` branch does not fire; and the missing Auto Run Result,
  `in-review` status and uncommitted tree all described the workflow mid-flight.

### Follow-up review

`followup_review_recommended: true` — eight medium entries were patched in one round, and two
named risks remain unverified. First, the proxy mechanism was rewritten during triage: `proxy_pass`
now takes a variable and appends `$request_uri` explicitly, and the resolver defaults to Docker's
embedded DNS at `127.0.0.11`. Both were verified live on macOS/arm64, but AD-17's "one topology for
compose and Azure" means this image is also the cloud artefact, and nothing exercises the cloud
path — a Container Apps deployment would need `NGINX_RESOLVER` overridden, and no test or document
says so. Second, the new CI smoke step was run verbatim locally but has never executed on a GitHub
amd64 runner; every image in this story has only ever been built on arm64.
Patched counts by verdict: high 0, medium 8, low 4.

### Verification

- `dotnet build ActionLedger.sln` — 0 warnings, 0 errors.
- All eight test projects run directly — 391 tests, 0 failures (`Architecture.Tests` 93).
- `docker compose build`, `config --quiet`, and `up -d --wait` from an empty volume — `db` healthy,
  `migrate` exited 0, `api` healthy, `web` healthy; the ordering chain reproduced exactly.
- The CI smoke sequence run verbatim against the **unmodified placeholder `.env.example`**: SPA
  served, `/appsettings.json` 404, `/health` `{"status":"healthy"}`, `/api/v1/users` 401, `migrate`
  exit code 0. CI needs no secret.
- Browser pass on the patched stack — sign-in as `dana` through the proxy, app bar showing
  `Dana Whitfield` / `Action Officer`, `/meetings` rendering the Not found notice, Swagger UI at
  `/swagger`, and **zero** console messages at warning level or above. This is the pass Story 1.6
  deferred to this story.
- Stale-upstream repro re-run after the fix — `api` recreated onto a different IP with `web`
  untouched: `/health` 200 and `/api/v1/users` 401 immediately, where the pre-patch stack returned
  502 permanently.
- Fail-fast checks — `.env` absent: `docker compose config` exit 1 naming the file; `Jwt__Key`
  blanked: the api refuses to start with `'Jwt:Key is required.'`.
- Idempotence — `down` then `up` on the existing volume: "No migrations were applied",
  `CreatedCount: 0`, sign-in still 200.
- Mutation passes — 10 in the first round and 16 in the patch round, one per guard, each verified
  red and reverted.

### Residual risks

- **arm64 only.** Every image was built and run on Apple Silicon. The `images` job is the first
  amd64 build of all three.
- **The cloud half of AD-17 is unexercised.** `NGINX_RESOLVER=127.0.0.11` is Docker's embedded DNS;
  a Container Apps deployment must override it. Nothing tests or documents that yet — `cd.yml` is a
  later story.
- **The upstream re-resolves within a 10 s TTL, not instantly.** A recreated `api` is reachable
  again without operator action, but the window is bounded by `valid=10s` rather than zero.
- **`ComposeTopologyTests` pins text, not behaviour.** The CI smoke step is what executes the
  topology; the unit guards can only catch edits to the files they read.
- **`receiver` is absent by design.** Story 5.2 adds it, along with `Webhooks:Receiver:*`, which are
  deliberately not in `.env.example` yet.

### Follow-up review pass — attempted 2026-09-21, HALTED

The follow-up pass this spec asks for has **not** been run. Run `20260921-171838-dfcf` dispatched
it and halted with `CRITICAL escalation: no subagents`: all four layers reported "Spawned
successfully", then sat idle for 23 minutes with no result; a direct request to each inbox drew no
reply over a further 5 minutes; and a control probe — a subagent whose entire task was to reply
with one word using no tools — behaved identically. The probe rules out a large diff or one wedged
reviewer: the mechanism is not delivering results in this environment. The same escalation stopped
story 1.6's follow-up pass earlier the same day.

That pass planned, implemented, and changed nothing. Its only edits were to this spec's `status`
and this section, both of which are reverted here: the implementation genuinely reached `done` at
the end of the first pass, as recorded in commit `e7c66ed`, and the first pass's 31 triage rows
stand untouched.

**It was deliberately not run inline instead.** Step-04 specifies every layer as context-free, and
a session that has already read this spec, its intent contract, and the diff is primed to accept
the claims it is supposed to test. An inline pass would be *a* review, not the one this spec is
owed, and recording it as convergence would be false. `followup_review_recommended` stays `true`.

**The two unverified risks the first pass named are both environment risks, and CI closes them by
running rather than by reading:**

1. `proxy_pass $upstream$request_uri` with the `NGINX_RESOLVER` default has only ever been
   exercised on macOS/arm64 against Docker's embedded DNS.
2. The CI smoke step has only ever been run locally, never on a GitHub amd64 runner.

`e7c66ed` wires `ci.yml` to build all three images, parse the compose file against the placeholder
env, start the stack and probe it. Opening this story's pull request therefore exercises both risks
on amd64 directly — stronger evidence than any review layer could produce.
