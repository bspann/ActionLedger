# Epic 1 Context: Sign in to a running, deployable ActionLedger

<!-- Generated from planning artifacts. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Epic 1 builds the foundation every later story lands on: a Clean Architecture solution whose dependency rule is enforced by a test, a green CI pipeline with branch protection, a versioned REST API with a committed OpenAPI contract, a real PostgreSQL schema delivered through a migration bundle, JWT sign-in with seeded demo users, a Blazor WebAssembly shell on MudBlazor with the semantic design tokens and a generated typed client, and a one-command compose environment. Success means a panelist can clone the repository, run `docker compose up` on macOS or Windows with no host .NET toolchain, reach a working login page in under five minutes, sign in with documented credentials, and browse Swagger UI. This epic is scheduled first (Saturday of a three-day build) precisely because quality gates that land late never land at all.

## Stories

- Story 1.1: Green pipeline on an empty Clean Architecture solution
- Story 1.2: API foundation with versioned routes, ProblemDetails, and the committed OpenAPI contract
- Story 1.3: Database, first migration, migration bundle, and seeded users
- Story 1.4: Sign in and receive a JWT; list users
- Story 1.5: Blazor WebAssembly scaffold with MudBlazor theme, tokens, and the generated API client
- Story 1.6: Login screen, session, shell, and global state patterns
- Story 1.7: One-command compose environment with migrate, api, and web

## Requirements & Constraints

- **Auth and attribution.** Username/password sign-in returns an 8-hour HS256 JWT carrying user id, display name, and role. Passwords are hashed. Invalid credentials return 401 with a message that never reveals which half was wrong. Every write and every audit record takes its actor from the token's `sub` claim — never from a request body or parameter. Seeded data is attributed to a system user named "Seed".
- **Role enforcement.** Reads require any authenticated user; writes require ActionOfficer or Lead. Unauthenticated is 401, wrong role is 403. A test must walk every OpenAPI operation (except login and health) and prove it rejects an anonymous caller.
- **API surface.** All routes live under `/api/v1`; only liveness, readiness, `/openapi`, and `/swagger` are unversioned. Errors are RFC 9457 ProblemDetails with a stable `type` vocabulary. Lists page with `page`/`pageSize` (default 50, max 200) inside an `{ items, page, pageSize, total }` envelope.
- **Contract discipline.** The OpenAPI document is generated from controllers, committed to the repository, and guarded by a snapshot test that fails CI when the live document drifts. The web client is generated from that committed file, so an unexported contract change breaks the web build.
- **Startup safety.** Configuration is environment-bound and validated at startup; a missing required key fails the host with a message naming the key. No secret is committed — only a documented example environment file.
- **Portability.** Running the demo must require Docker Desktop only. No host .NET and no Node toolchain anywhere in the build.
- **Pipeline.** Every story merges through a pull request with green checks and a linked issue. CI restores, builds, runs all test projects (including Testcontainers and component tests), runs architecture and contract tests, and builds images. Code scanning, dependency updates, secret scanning with push protection, a PR checklist template, and conventional commits are all in place from the first commit. Dependencies are MIT, Apache-2.0, or BSD only.
- **Observability.** Structured JSON logging to stdout with a correlation id on every line. Notes text and secrets are never logged.

## Technical Decisions

- **Rings and enforcement.** Domain, Application, Infrastructure, Api. Architecture tests fail the build if Domain takes any package dependency, if Application reaches past Domain plus a narrow allowlist, if Domain or Application touch EF Core, ASP.NET Core, Npgsql, or any AI SDK, or if anything references Api. Infrastructure may reference Application and Domain only.
- **Use-case shape.** One handler class per write use case; reads are per-feature query classes over a read abstraction. Controllers validate HTTP shape, call exactly one handler or query, and map the result — they never touch a repository or the DbContext. Handlers take the actor from the current-user port and time from a clock port, never from the command.
- **Persistence conventions.** snake_case tables via a model-wide naming convention, enums stored as strings, UUIDv7 ids generated in Domain constructors with key generation turned off in EF, UTC instants and date-only due dates, one commit per use case through a unit-of-work port. Migrations ship only through the EF migration bundle — the API never migrates at startup.
- **Identity plumbing.** A current-user port reads the token claim; the seeder supplies the only alternate implementation, for the system user. Seeding is an idempotent hosted service guarded by a Postgres advisory lock and driven by a fixed clock, gated by a config flag.
- **Health split.** Liveness answers without touching the database; readiness checks database reachability. Liveness is the compose healthcheck and container liveness probe; readiness is the readiness probe.
- **Frontend structure.** Feature folders (Auth, Meetings, Review, Actions, Audit) with one routable page component per route; child components are presentational with parameter inputs and event callbacks. Component classes hold state and command methods only. HTTP lives exclusively in per-feature data services wrapping the generated client, plus shared session and user-directory scoped services. An architecture test — not a lint rule — fails the build when the HTTP client or the generated client is referenced outside those two locations.
- **Client generation.** A pre-build MSBuild target generates the typed C# client into a git-ignored folder from the committed contract file, which lives beside the web project. Generation is kept deliberately (rather than sharing Application DTOs directly) so the committed document remains a real boundary.
- **Compose topology.** `db`, a one-shot `migrate` that must exit zero before `api` starts, `api` (which also hosts the seeder and later the outbox dispatcher), `web` (nginx serving static WebAssembly assets), and `receiver`. The web image templates its API upstream at container start, so one image serves compose and cloud. The proxy allows 200-second read and send timeouts on the API path so a slow local model never gets cut off. The api container declares a host-gateway alias so a model server on the developer's machine resolves from inside Docker.
- **Stack pins that matter.** .NET 10 SDK, ASP.NET Core controllers, EF Core with Npgsql, PostgreSQL 18, xunit.v3, Testcontainers, NetArchTest (enhanced fork), Blazor WebAssembly, MudBlazor, NSwag MSBuild, bUnit, nginx alpine. Explicitly excluded by license: FluentAssertions 8+, MediatR 13+, AutoMapper 15+, MassTransit 9, JsonSchema.Net. Assertions use xunit built-ins, mapping is hand-written, and there is no mediator.

## UX & Interaction Patterns

- **Theme discipline.** MudBlazor on a Material-derived theme. No MudBlazor component is restyled — buttons, fields, tables, cards, dialogs, snackbars, and navigation ship as rendered. The design delta is only tokens and chips.
- **Semantic tokens.** Four brand color families beyond the base palette — AI provenance (purple), human provenance (blue), low confidence (amber), success (green) — plus a quiet neutral container family, each with an on-color and a dark-mode pair, implemented as CSS custom properties. Every pair meets AA contrast in both modes. Error red is reserved for overdue and destructive confirmation; amber is reserved for low confidence.
- **Typography.** The default Roboto ramp, plus one added role: a monospace treatment for confidence scores so two-decimal numbers align in a column. Roboto Mono must be loaded alongside Roboto.
- **Shell.** A single top app bar with the product name, exactly two destinations (Meetings, Actions) with active state, and a user menu showing display name, role, and Sign out. No side navigation. Content sits in a 1280px container with 24px gutters.
- **Session.** The token is held in memory by a scoped session service; the user roster loads once after login. A 401 anywhere redirects to login with a snackbar and restores the attempted route after sign-in.
- **Global state patterns** (established here, reused by every later screen): a linear progress bar under the toolbar during loads with no skeleton rows; load failure shows the problem title plus Retry while filters and toolbar stay usable; unmatched or missing detail routes show a not-found page linking to the parent list; 403 raises a snackbar saying the role does not allow the action; 409 raises a snackbar and refreshes the record with no Retry offered; write failures show the problem title with Retry and retain form values; buttons that start a write disable until the response returns.
- **Voice and formatting.** Declarative microcopy, no exclamation marks or emoji, the model is always called "AI". Dates render `YYYY-MM-DD`, instants render `YYYY-MM-DD HH:mm UTC`; relative time is never used. Specified strings are used verbatim from the shared voice constants.
- **Accessibility floor.** Every indicator pairs an icon with text — color is never the only signal. Every icon button is labeled, every field has a visible label, dialogs trap and return focus, and default focus rings are kept. UI stories record a manual keyboard pass and an axe scan in the PR checklist.
- **Viewport.** Desktop only, supported at 1024px and wider, checked at 1280px.

## Cross-Story Dependencies

- 1.1 gates everything: the solution, test projects, architecture tests, and CI must be green before any feature work merges.
- 1.2 must commit the contract before 1.5 can generate a client from it; the readiness endpoint that 1.2 references is added by 1.3.
- 1.3 provides the database and the demo users that 1.4 authenticates against; the migration bundle it produces is what 1.7's migrate service runs.
- 1.4 must exist before 1.6 can actually sign in; 1.5 must land before 1.6 has a shell to build in.
- 1.5 relocates the committed contract file next to the web project, so the export command and the snapshot test from 1.2 must be updated to follow it.
- 1.7 depends on Dockerfiles and the migration bundle from 1.3 and the built web app from 1.5/1.6.
- Downstream: the shell, session handling, global state patterns, formatters, and voice constants from 1.6 are consumed by every UI story in Epics 2 through 4. The contract-plus-generated-client mechanism from 1.2 and 1.5 is how every later resource reaches the web app. Only the minimal login users are seeded here; the full demo seed (meetings, runs, outbox rows) lands in Epic 6.
- Note: this epic's plan reflects the 2026-09-21 sprint change from Angular to Blazor WebAssembly. Stories 1.5 and 1.6 were rewritten; all behavioral acceptance criteria were preserved and only component vocabulary changed. No Angular, Node, npm, or TypeScript tooling should appear anywhere in the build.
