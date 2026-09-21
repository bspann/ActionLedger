# BMAD Seed Prompt: ActionLedger (working name)

> Hand this whole file to the BMAD orchestrator. John (PM) produces the Project Brief and PRD, Sally (UX) the screen flows, Winston (Architect) the Architecture Document and ADRs, and Amelia (Dev) works the sequenced story backlog in Claude Code.

## 1. Context and hard constraints

I am building a small but production-shaped application to demo in a technical interview on **Wednesday, September 23, 2026**. The panel will ask me to walk through my development methodology, how I decided on the data model, how I handle ALM and CI/CD, and any creative use of AI. The role is a hybrid of software development, systems integration through APIs, business process automation, and AI/automation prototyping for a government customer.

- **Code freeze: end of day Monday, September 21.** Tuesday is rehearsal only.
- Scope must fit roughly three build days for one developer working with Claude Code. When in doubt, cut features, never quality gates.
- A small app that is tested, deployed, and explainable beats a large one that is half wired.
- Every BMAD artifact (brief, PRD, architecture, ADRs, stories) lives in the repo under `/docs`, because the artifacts themselves are part of the demo.
- This is a clean-room personal project. Do not model it on, or borrow from, any prior employer or client work.

## 2. Vision

Staff organizations run on meetings, and action items get lost between someone's notes and the tracking system. ActionLedger turns rough meeting notes into tracked actions with a human in the loop: the AI **proposes**, a person **approves, edits, or rejects**, and every step is kept in an audit trail. Approved actions are exposed through a documented API and pushed to other systems by webhook.

Design principle: **AI output is never authoritative.** Nothing the model produces becomes a tracked action until a named human approves it, and the record always shows what the AI proposed versus what the human decided.

## 3. Users

- **Action officer:** pastes meeting notes, reviews AI proposals, approves or edits them.
- **Lead:** views all tracked actions, filters by owner, status, and due date, sees what is overdue.
- **Integrator (another system):** consumes the REST API and receives webhooks.

## 4. Functional scope

### P0: must work in the demo
1. Create a meeting (title, date, attendees) and paste raw notes. Raw notes are stored immutably.
2. Run extraction. The AI returns proposed actions, each with description, suggested owner, suggested due date, a confidence score, and the source sentence from the notes that justifies it.
3. Review screen: approve, edit then approve, or reject each proposal. Low confidence proposals are visually flagged.
4. Approved proposals become tracked actions with status (Open, In Progress, Complete, Cancelled).
5. Tracked action list with filtering by owner, status, and due date, plus an overdue indicator.
6. Audit trail view for any action: the AI proposal, each human change, who made it, and when.
7. Lightweight identity: seeded demo users with two roles (ActionOfficer, Lead), simple login, JWT. No registration, no password reset.
8. REST API with OpenAPI document and Swagger UI.
9. Outbound webhook on action approval, signed with HMAC, delivered through an outbox table with retry.
10. Seed data so every screen is populated on first run.

### P1: only if P0 is finished and green
- AI drafted follow-up email per meeting, reviewed before copy or export.
- Dashboard tiles (open, overdue, completed this week).
- Re-run extraction with a newer prompt version and compare results side by side.

### Out of scope (describe as roadmap only)
Real integrations with Teams, Planner, or Outlook. Audio transcription. Multi-tenant support. SSO or CAC authentication. Notifications. Mobile layout polish.

## 5. Technology stack (locked, do not re-decide)

- **Backend:** C#, .NET 10, ASP.NET Core Web API with controllers.
- **Data:** EF Core, code-first migrations, PostgreSQL. Migrations ship as an EF migration bundle in the pipeline.
- **Front end:** Angular (current stable release), TypeScript strict mode, standalone components, signals, Angular Material for UI.
- **AI:** `Microsoft.Extensions.AI` abstractions. Providers: Azure OpenAI, local Ollama, and a deterministic fake.
- **Tests:** xUnit, Testcontainers for PostgreSQL, NetArchTest for architecture rules. Angular unit tests for view model logic. One Playwright smoke test of the happy path.
- **Runtime:** Docker and `docker compose up` brings up API, web, and database in one command on macOS and Windows.
- **Licensing:** permissive open source only (MIT, Apache 2.0, BSD). Do not introduce libraries that have moved to commercial licenses. Use plain handler classes for use cases in place of a mediator library.

## 6. Architecture: Clean Architecture, enforced

Solution layout:

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

Rules:
- Dependencies point inward only. Domain and Application must not reference EF Core, ASP.NET Core, or any AI SDK.
- `Architecture.Tests` uses NetArchTest to fail the build if the dependency rule is broken.
- Approval state transitions (Proposed to Approved, Edited, Rejected) are domain logic on the entity, not controller logic.
- **Proof point for the demo:** switching the AI provider between Azure OpenAI, Ollama, and the fake is a configuration change plus an Infrastructure class. Zero edits in Domain or Application. Winston should make this explicit in the architecture document.
- Backend controllers are the controller element of MVC, returning JSON in place of views.

## 7. Front end: Angular with an explicit MVVM mapping

- **View:** component template. Rendering and event binding only.
- **ViewModel:** component class exposing signals and commands for the template. No HTTP calls inside components.
- **Model:** typed API client services and interfaces generated from the OpenAPI document.
- Feature folders: `meetings`, `review`, `actions`, `audit`, `auth`. Smart container components per route, presentational child components with inputs and outputs.
- Include a short `/docs/frontend-architecture.md` with a diagram of this mapping, since I will be asked to point it out.

## 8. Data model: starting point and decisions to document

Starting entities: `Meeting`, `MeetingNotes` (immutable raw text), `ExtractionRun` (provider, model, prompt version, timestamp, duration, token counts), `ProposedAction` (belongs to a run; description, suggested owner, suggested due date, confidence, source excerpt, review state), `TrackedAction` (created from an approved proposal, keeps the link back), `ActionRevision` or audit entry (who, when, field, old value, new value), `User`, `WebhookSubscription`, `OutboxMessage`.

Winston: write an ADR for each of these decisions, with the alternative considered and why it was rejected:
1. Why proposals and tracked actions are separate tables and not one table with a status flag.
2. Why raw notes are immutable and extraction runs are repeatable against them.
3. Why prompt version and model are stored on every run (traceability and regression comparison).
4. Audit approach: explicit revision table versus temporal tables versus event sourcing.
5. Why the outbox pattern for webhooks and not a direct HTTP call inside the request.
6. Owner as free text suggestion from the AI, resolved to a `User` only on human approval.
7. PostgreSQL choice, and what would change to target SQL Server or Azure SQL.

## 9. AI requirements

- Extraction returns **structured output** validated against a JSON schema. Invalid output is rejected and retried once, then surfaced as a failed run. Never silently accepted.
- Prompts live as versioned files in the repo (`/prompts/extract-actions.v1.md`). The version is recorded on each `ExtractionRun`.
- Notes are treated as untrusted data. The prompt must instruct the model to ignore any instructions that appear inside the notes, and one golden set case must test this.
- **Evaluation gate:** `/tests/Eval` holds a golden set of 12 to 15 fictional meeting notes with expected actions. A scorer computes precision and recall on extracted actions and accuracy on owner and due date. CI fails below the thresholds defined in the PRD.
- The eval job runs on pull requests that touch `/prompts` or the extractor code, and on manual dispatch. It uses a repository secret for the cloud provider.
- The deterministic fake provider is used for unit tests, the Playwright smoke test, and as the demo fallback if the network fails.
- Log provider, model, latency, and token counts per run, and show them on the run detail screen.

## 10. ALM and CI/CD

- GitHub Issues and a GitHub Project board. Every story from the backlog becomes an issue. Pull requests link to issues.
- Trunk-based flow with short-lived feature branches, conventional commits, a PR template with a checklist, and branch protection on `main` requiring green checks.
- `ci.yml`: restore, build, backend tests including Testcontainers, architecture tests, Angular lint, test, and build, Docker image build.
- `eval.yml`: the AI evaluation gate described above, with a path filter.
- `cd.yml`: on merge to `main`, push images to a registry, run the EF migration bundle, and deploy to Azure Container Apps. A tagged release triggers a versioned deploy.
- CodeQL, Dependabot, and secret scanning enabled. No secrets in the repo, ever. `.env.example` only.
- Tag `v1.0.0` at code freeze and write release notes.

## 11. Demo readiness requirements

- `docker compose up` from a clean clone gives a working, seeded app in under five minutes.
- Seed data includes three meetings: one fully reviewed, one awaiting review, one with a low confidence proposal and an edited approval so the audit trail has something to show.
- A webhook receiver page or simple echo endpoint so the signed webhook can be shown arriving live.
- A `DEMO.md` script with the click path and the fallback plan using the fake provider.

## 12. Sample data rule

All sample content must be **obviously fictional and unclassified**. Use an invented generic organization doing mundane work: planning an office move, a training event, an equipment inventory. No real units, operations, locations, people, or anything resembling actual government or employer work.

## 13. Requested BMAD outputs

1. Project Brief (John): one page.
2. PRD (John): P0 and P1 scope as above, acceptance criteria per feature, evaluation thresholds.
3. UX flows (Sally): notes entry, review screen, action list, audit trail. Wireframe level only.
4. Architecture Document plus ADRs (Winston): layer diagram, data model diagram, sequence diagram for extraction and approval, sequence diagram for outbox webhook delivery.
5. Story backlog (John and Winston), sequenced for Amelia:
   - **Saturday:** repo, solution skeleton, CI green on an empty build, architecture tests, domain model, first migration, docker compose.
   - **Sunday:** extraction use case with fake provider, real provider, schema validation, review and approval flow end to end, Angular review screen.
   - **Monday:** action list, audit trail, webhook outbox, eval gate, CD to Azure, seed data, Playwright smoke test, `v1.0.0` tag.
   - Pipeline work comes **first**, not last. Every story merges through a pull request with green checks so the commit history tells the ALM story.

## 14. Open questions for the agents to resolve

- Evaluation thresholds that are honest for a golden set this small.
- Matching rule for scoring an extracted action against an expected one (exact, fuzzy, or model-graded).
- Simplest credible auth that still gives the audit trail a real user identity.
- Whether the outbox worker runs as a hosted service inside the API or as a separate container.
