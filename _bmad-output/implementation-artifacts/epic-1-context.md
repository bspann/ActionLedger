# Epic 1 Context: Sign in to a running, deployable ActionLedger

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Epic 1 stands up the whole delivery chassis before any feature exists: a Clean Architecture solution with enforced dependency rules, a versioned API with a committed OpenAPI contract, a real PostgreSQL schema with a migration bundle and seeded users, JWT sign-in, an Angular shell generated from that contract, and a one-command compose environment. When it is done, anyone can clone the repository, run `docker compose up` with no host .NET or Node, sign in with seeded credentials, and reach the application shell and Swagger UI. Everything afterwards lands on a green pipeline that already enforces the architecture, the contract, and branch protection — so later stories never have to retrofit quality gates during a three-day build window.

## Stories

- Story 1.1: Green pipeline on an empty Clean Architecture solution
- Story 1.2: API foundation with versioned routes, ProblemDetails, and the committed OpenAPI contract
- Story 1.3: Database, first migration, migration bundle, and seeded users
- Story 1.4: Sign in and receive a JWT; list users
- Story 1.5: Angular scaffold with Material theme, tokens, and the generated API client
- Story 1.6: Login screen, session, shell, and global state patterns
- Story 1.7: One-command compose environment with migrate, api, and web

## Requirements & Constraints

- **Auth and attribution.** Username/password login returns an 8-hour HS256 JWT carrying id, display name, and role. Passwords are hashed. Invalid credentials return 401 without hinting which part failed. Every write is attributed to the user resolved from the token's `sub` claim — never from a request body or query parameter. Seed-created data is attributed to a system user named `Seed`, which must be excluded from the user roster the web app consumes.
- **Authorization shape.** Reads require any authenticated user; writes require ActionOfficer or Lead; one Lead-only operation exists (the Cancelled transition, enforced later). Unauthenticated is 401, wrong role is 403. A test must walk every OpenAPI operation except login and health and prove each returns 401 without a token.
- **API surface.** Every route lives under `/api/v1`; only the health routes, `/openapi`, and `/swagger` are unversioned. Errors are RFC 9457 ProblemDetails with stable types (`validation`, `unauthorized`, `forbidden`, `not-found`, `conflict`). Lists page with `page` and `pageSize` (default 50, max 200) inside a `{ items, page, pageSize, total }` envelope.
- **Contract discipline.** The OpenAPI document is generated from controllers, exported to a committed file, and guarded by a snapshot test in CI; the Angular typed client is generated from that committed file so a contract change breaks the web build.
- **Deployability.** `docker compose up` from a clean clone must reach a usable login page in under five minutes on macOS and Windows with Docker Desktop and no host toolchain. Fake AI provider is the compose default. Every configuration key is documented in an example env file with placeholders; no secret is committed.
- **Pipeline.** CI on pull requests and main runs restore, build, all test projects including Testcontainers and architecture tests, the OpenAPI snapshot, Angular lint/test/build, and image builds. Branch protection on main requires green CI and a linked issue. CodeQL, Dependabot, secret scanning, and push protection are enabled. Conventional commits; a PR template checklist covering issue link, tests added, license review, and an axe pass for UI stories.
- **Licensing.** Only MIT, Apache-2.0, or BSD dependencies. Specifically excluded: FluentAssertions 8+, MediatR 13+, AutoMapper 15+, MassTransit 9, JsonSchema.Net. Assertions use xunit built-ins, mapping is hand-written, and there is no mediator.
- **Latency budget that affects this epic.** The nginx proxy must allow 200 seconds on `/api` so the later synchronous extraction call survives a slow local model.

## Technical Decisions

- **Rings and enforcement.** Four projects — Domain, Application, Infrastructure, Api — with dependencies pointing inward. An architecture test suite fails the build when Domain references any package, when Application references anything outside Domain plus its allowlist, when Domain or Application reference EF Core, ASP.NET Core, Npgsql, or any AI SDK, or when any project references Api. Seven test projects exist from day one: Domain, Application, Infrastructure, Api, Architecture, Eval, and Web.E2E.
- **Handlers and controllers.** One handler class per use case named `<Verb><Noun>Handler`; reads go through `<Feature>Queries` over a read-only database port. Controllers validate HTTP shape, call one handler or query, and map to a DTO or ProblemDetails — no logic. No mediator library.
- **Persistence conventions.** PostgreSQL 18 through EF Core with Npgsql. Tables are snake_case plural via a naming convention; entities are singular PascalCase. Enums are stored and serialized as PascalCase strings. Ids are UUIDv7 generated in Domain constructors with `ValueGeneratedNever`. `DateOnly` for dates (serialized `YYYY-MM-DD`), UTC `DateTimeOffset` for instants (ISO 8601 with `Z`). The unit-of-work port commits once per handler. The users table carries a unique index on username.
- **Migrations never run in-process.** Schema changes ship only through an EF migration bundle produced in the api image's multi-stage build. A one-shot `migrate` service applies it and the api starts only after it exits successfully. A liveness endpoint answers without touching the database; a readiness endpoint returns 200 only when the database is reachable.
- **Seeding.** The demo seeder is a hosted service in the api, gated by a config flag, guarded by a PostgreSQL advisory lock, and idempotent — a second start changes nothing. It writes through the same aggregate methods as live writes, using a seed identity and a fixed clock. Seeded users: two Action Officers, one Lead, and the system user.
- **Configuration.** Option groups for AI, JWT, database, webhooks, and seeding are validated at startup; a missing required key fails the host with a message naming the key. Secrets come only from environment or user secrets. Serilog writes structured JSON to stdout with a correlation id on every line — the W3C trace id when present, otherwise the request trace identifier. Never log notes text or secrets.
- **Frontend architecture.** Angular with zoneless and standalone defaults, strict TypeScript pinned to the Angular-supported minor (never `typescript@latest`), Angular Material 3 on the prebuilt `azure-blue` theme with no Material component restyled, and Roboto Mono for the confidence-score role. MVVM maps as: template is the View, component class with signals and command methods is the ViewModel, and the generated client plus its DTOs is the Model. Feature folders are `auth`, `meetings`, `review`, `actions`, `audit`, plus `core/`. One smart container per route with presentational children. HTTP happens only in per-feature data services — a lint rule forbids importing the HTTP client anywhere except those services and `core/`.
- **Design tokens.** Semantic color families — ai-provenance, human-provenance, low-confidence, success, neutral-container — ship as CSS custom properties with light and dark pairs and matching on-colors, taken from the design document's hex values. Content sits in a 1280px container with 24px gutters.
- **Containers.** Images for api (multi-stage, also producing the bundle), migrate, web (nginx serving the built Angular app), and later a webhook receiver. The web image takes its API upstream from an environment variable templated into the nginx config so the same image serves compose and cloud. The api declares a host-gateway extra host so a local model server on the host is reachable.

## UX & Interaction Patterns

- **Shell.** A global toolbar with the product name, Meetings and Actions links with active state, and a user menu showing display name, role, and Sign out. No sidenav.
- **Login.** Username, password, Sign in. On 401 the form shows the sign-in failure message. The token is held in memory in a session store — not persisted. The user roster loads once into its own store.
- **Global state patterns, implemented once here and relied on by every later screen.** A progress bar under the toolbar during any load; a load failure showing "Couldn't load. {problem title}" with Retry; a Not found page with a link to the parent list for any unmatched or 404 detail route; a 403 snackbar; a 409 snackbar that refreshes rather than offering Retry; a 401 that redirects to Login with a session-expired message and restores the attempted route; a write failure that shows the problem title with Retry and keeps form values; write buttons disabled while in flight.
- **Formatting and voice.** Shared pipes render dates as `YYYY-MM-DD` and instants as `YYYY-MM-DD HH:mm UTC`. No relative time anywhere. Voice and tone strings live in a constants file and are used verbatim.
- **Accessibility floor.** Every indicator is icon plus text, never color alone; every icon button has an accessible label; fields have visible labels; dialogs trap and return focus; Material default focus rings are kept.

## Cross-Story Dependencies

- Within the epic the order is strict: 1.1 (solution and pipeline) → 1.2 (API foundation and exported contract) → 1.3 (database, migration bundle, seeded users) → 1.4 (login and user list) → 1.5 (Angular scaffold generated from the contract) → 1.6 (login screen and shell) → 1.7 (compose). The readiness endpoint is specified in 1.2 but only becomes real in 1.3, when the database context and first migration exist.
- 1.5 cannot generate its client until 1.2 has committed the OpenAPI document, and 1.6 cannot sign in until 1.4 exists.
- Every later epic depends on this one: the ProblemDetails mapping, paging envelope, current-user port, revision and concurrency conventions, and the contract-plus-generated-client loop are all established here. The shell's Not found and error states are what later review and action screens rely on rather than building their own.
- The seeder and the database context are touched again in later epics (full demo data, webhook subscriptions, outbox rows). Build them to be extended, not replaced.
- Build order note: this epic is the Saturday of a three-day window ending in a code freeze and a tagged release. Pipeline work comes first and nothing in Epic 1 is on the cut list.
