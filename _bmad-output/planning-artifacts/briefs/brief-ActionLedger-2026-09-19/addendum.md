---
title: "Addendum: ActionLedger"
status: ready-for-review
created: 2026-09-19
updated: 2026-09-19
---

# Addendum: ActionLedger

This addendum carries detail from the seed prompt (`docs/bmad-seed-prompt.md`) for downstream documents to consume. The PRD, UX flows, architecture document, ADRs, and story backlog should treat every section as a locked input unless that section says otherwise.

## Locked technology stack

Do not re-decide.

- **Backend.** C#, .NET 10, ASP.NET Core Web API with controllers. Controllers are the controller element of MVC, returning JSON in place of views.
- **Data.** EF Core, code-first migrations, PostgreSQL. Migrations ship as an EF migration bundle in the pipeline.
- **Front end.** Angular, current stable release, TypeScript strict mode, standalone components, signals, Angular Material.
- **AI.** `Microsoft.Extensions.AI` abstractions. Providers: Azure OpenAI, local Ollama, deterministic fake.
- **Tests.** xUnit, Testcontainers for PostgreSQL, NetArchTest for architecture rules. Angular unit tests for view model logic. One Playwright smoke test of the happy path.
- **Runtime.** Docker. `docker compose up` brings up API, web, and database in one command on macOS and Windows.
- **Licensing.** Permissive open source only (MIT, Apache 2.0, BSD). No libraries that have moved to commercial licenses. Plain handler classes for use cases in place of a mediator library.

## Clean Architecture layout and rules

```
src/
  ActionLedger.Domain          entities, value objects, domain rules. References nothing.
  ActionLedger.Application     use cases, DTOs, interfaces (IActionExtractor, IActionRepository,
                               IWebhookDispatcher, IClock, ICurrentUser). References Domain only.
  ActionLedger.Infrastructure  EF Core, AI providers, webhook sender, outbox worker.
                               Implements Application interfaces.
  ActionLedger.Api             controllers, auth, DI composition root, OpenAPI.
web/
  actionledger-web             Angular app
tests/
  Domain.Tests, Application.Tests, Infrastructure.Tests, Api.Tests, Architecture.Tests, Eval
```

- Dependencies point inward only. Domain and Application must not reference EF Core, ASP.NET Core, or any AI SDK.
- `Architecture.Tests` uses NetArchTest to fail the build if the dependency rule is broken.
- Approval state transitions (Proposed to Approved, Edited, Rejected) are domain logic on the entity, not controller logic.
- **Demo proof point.** Switching the AI provider is a configuration change plus an Infrastructure class. Zero edits in Domain or Application. The architecture document must make this explicit.

## Front end: MVVM mapping

- **View.** Component template. Rendering and event binding only.
- **ViewModel.** Component class exposing signals and commands. No HTTP calls inside components.
- **Model.** Typed API client services and interfaces generated from the OpenAPI document.
- Feature folders: `meetings`, `review`, `actions`, `audit`, `auth`. Smart container components per route, presentational child components with inputs and outputs.
- Deliverable: `/docs/frontend-architecture.md` with a diagram of this mapping.

## Data model: starting entities

- `Meeting`
- `MeetingNotes`: immutable raw text.
- `ExtractionRun`: provider, model, prompt version, timestamp, duration, token counts.
- `ProposedAction`: belongs to a run; description, suggested owner, suggested due date, confidence, source excerpt, review state.
- `TrackedAction`: created from an approved proposal, keeps a link back to the proposal.
- `ActionRevision`: who, when, field, old value, new value.
- `User`
- `WebhookSubscription`
- `OutboxMessage`

## ADRs required from the architect

Each ADR records the alternatives considered and why each was rejected.

1. Proposals and tracked actions as separate tables rather than one table with a status flag.
2. Immutable raw notes with repeatable extraction runs against them.
3. Prompt version and model stored on every run, for traceability and regression comparison.
4. Audit approach: explicit revision table versus temporal tables versus event sourcing.
5. Outbox pattern for webhooks rather than a direct HTTP call inside the request.
6. Owner as free-text AI suggestion, resolved to a `User` only on human approval.
7. PostgreSQL choice, and what would change to target SQL Server or Azure SQL.

## AI requirements

- Extraction returns structured output validated against a JSON schema. Invalid output is rejected and retried once. If the retry also fails, the run is surfaced as failed. Invalid output is never silently accepted.
- Prompts are versioned files in the repo (`/prompts/extract-actions.v1.md`). The version is recorded on each `ExtractionRun`.
- Notes are untrusted data. The prompt instructs the model to ignore instructions inside the notes, and one golden-set case tests this.
- **Evaluation gate.** `/tests/Eval` holds a golden set of 12 to 15 fictional meeting notes with expected actions. A scorer computes precision and recall on extracted actions and accuracy on owner and due date. CI fails when scores fall below the thresholds defined in the PRD.
- The eval job runs on pull requests touching `/prompts` or the extractor code, and on manual dispatch. It uses a repository secret for the cloud provider.
- The deterministic fake provider serves unit tests, the Playwright smoke test, and the demo fallback if the network fails.
- Provider, model, latency, and token counts are logged per run and shown on the run detail screen.

## ALM and CI/CD

- GitHub Issues and a GitHub Project board. Every story becomes an issue. Pull requests link to issues.
- Trunk-based flow, short-lived feature branches, conventional commits, PR template with checklist, branch protection on `main` requiring green checks.
- `ci.yml`: restore, build, backend tests including Testcontainers, architecture tests, Angular lint, test, and build; Docker image build.
- `eval.yml`: the AI evaluation gate with a path filter.
- `cd.yml`: on merge to `main`, push images to a registry, run the EF migration bundle, deploy to Azure Container Apps. A tagged release triggers a versioned deploy.
- CodeQL, Dependabot, and secret scanning enabled. No secrets in the repo. `.env.example` only.
- Tag `v1.0.0` at code freeze and write release notes.

## Demo readiness

- `docker compose up` from a clean clone gives a working, seeded app in under five minutes.
- Seed data includes three meetings: one fully reviewed, one awaiting review, one with a low-confidence proposal and an edited approval so the audit trail has content.
- A webhook receiver page or echo endpoint so the signed webhook can be shown arriving live.
- `DEMO.md` with the click path and the fallback plan using the fake provider.

## Sample data rule

All sample content is obviously fictional and unclassified. Use an invented generic organization doing mundane work: an office move, a training event, an equipment inventory. No real units, operations, locations, people, or anything resembling actual government or employer work.

## Build sequence for the story backlog

Pipeline work comes first, not last. Every story merges through a pull request with green checks.

- **Saturday 2026-09-19.** Repo, solution skeleton, CI green on an empty build, architecture tests, domain model, first migration, docker compose.
- **Sunday 2026-09-20.** Extraction use case with fake provider, real provider, schema validation, review and approval flow end to end, Angular review screen.
- **Monday 2026-09-21.** Action list, audit trail, webhook outbox, eval gate, CD to Azure, seed data, Playwright smoke test, `v1.0.0` tag.

## Open questions for downstream agents

1. Evaluation thresholds that are honest for a golden set of 12 to 15 cases.
2. Matching rule for scoring an extracted action against an expected one: exact, fuzzy, or model-graded.
3. Simplest credible auth that still gives the audit trail a real user identity.
4. Whether the outbox worker runs as a hosted service inside the API or as a separate container.

## Artifact location note

The seed requires every BMAD artifact to live under `/docs` because the artifacts are part of the demo. BMAD is configured to write planning artifacts to `_bmad-output/planning-artifacts`. Finalized artifacts should be copied or linked into `/docs` before code freeze.
