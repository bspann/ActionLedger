# Sprint Change Proposal — Angular to Blazor WebAssembly

**Project:** ActionLedger
**Date:** 2026-09-21
**Author:** Brianspann (with Claude Code)
**Status:** Awaiting approval
**Scope classification:** Moderate — backlog reorganization, no epic restructuring

---

## 1. Issue Summary

**Trigger.** Not a defect or a failed approach. A capability and risk decision raised by the developer during Story 1.3, while stories 1.1 and 1.2 sat merged and no UI code existed.

**Issue type:** Strategic pivot — technology choice misaligned with the developer's fluency and with the project's actual acceptance criterion.

**Problem statement.** The planned web UI is Angular 22 with Angular Material 3. The developer is fluent in Blazor, not Angular. ActionLedger's purpose is an interview demonstration on 2026-09-23, where the developer must explain and defend the implementation live. An Angular application that cannot be discussed fluently is worth materially less than a Blazor application that can — the ability to defend the code *is* the product requirement here, not a preference about it.

**Supporting evidence.**

| Fact | Value |
|---|---|
| UI code written to date | **None** — no `web/` project, no components |
| Stories carrying UI acceptance criteria | 10 (9 P0 + Story 6.6 documentation) |
| Stories merged and unaffected | 1.1, 1.2 (backend and pipeline only) |
| Story in flight and unaffected | 1.3 — database, migrations, seeded users |
| ADRs containing Angular references | **0 of 7** |
| Code freeze | 2026-09-21 (today) |

**Secondary benefit.** The Angular path carries npm, a pinned TypeScript minor, `ng-openapi-gen`, a Node version constraint, a separate lint configuration, and a Node build stage in the `web` image. On a schedule already running a day behind, removing an entire second toolchain reduces the number of things that can fail before Monday. NFR10 ("no host .NET or Node needed") becomes easier to satisfy, not harder.

**Cost of delay.** Story 1.5 is the next UI story. Every UI story completed after this point converts this from a documentation edit into a code rewrite. This is the cheapest moment the change will ever have.

---

## 2. Impact Analysis

### 2.1 Epic impact

No epic is added, removed, resequenced, or redefined. Story count stays at 35. Epic goals are framework-independent and survive verbatim.

| Epic | Stories touched | Nature of change |
|---|---|---|
| 1 — Sign in to a running, deployable ActionLedger | 1.5, 1.6 | Rewrite: Angular scaffold → Blazor WASM scaffold; session/shell/state patterns re-expressed |
| 2 — Capture notes and get AI proposals | 2.2, 2.6 | Rewrite UI acceptance criteria; API criteria unchanged |
| 3 — Decide what becomes tracked work | 3.4, 3.5 | Rewrite UI acceptance criteria; Review Screen layout and behaviour preserved |
| 4 — Track, filter, and audit actions | 4.3, 4.4, 4.5 | Rewrite UI acceptance criteria; filter-in-URL behaviour preserved |
| 5 — Deliver approved actions | none | No UI |
| 6 — Prove quality and ship | 6.4, 6.6 | Playwright selectors re-expressed; documentation deliverables renamed |
| 7 — P1 enhancements | 7.2 | Dashboard tiles re-expressed (conditional story) |

**Dependency chain unchanged.** 1.5 still cannot start until 1.2 commits the contract (done). 1.6 still cannot sign in until 1.4 exists. The contract-then-client sequencing that AD-13 establishes is preserved exactly — only the generator changes.

### 2.2 Artifact conflicts

| Artifact | Coupled references | Required change |
|---|---|---|
| `DESIGN.md` | 34 | Heaviest. Theme inheritance and component vocabulary are Angular Material-specific |
| `epics.md` | 24 | 10 stories' UI acceptance criteria and the Additional Requirements block |
| `EXPERIENCE.md` | 19 | Component Patterns and State Patterns sections name Material components |
| `ARCHITECTURE-SPINE.md` | 11 | AD-13, AD-14, Stack table, Structural Seed, Consistency Conventions |
| `ARCHITECTURE.md` | 7 | Narrative frontend sections |
| `prd.md` | 6 | FR27, NFR8, NFR9, NFR10 wording |
| **7 ADRs** | **0** | **None — all survive untouched** |

### 2.3 Technical impact

- **AD-13 (contract)** — the committed `openapi.json` stays the boundary. `ng-openapi-gen` → **NSwag.MSBuild**, generating a typed C# client at build time. The load-bearing property is preserved: a contract change that is not re-exported breaks the client build. Keeping generation rather than referencing Application DTOs directly is deliberate — it keeps the contract a real boundary and is the more defensible answer in an interview.
- **AD-14 (frontend discipline)** — MVVM with Angular signals → Blazor components with code-behind; "no `HttpClient` in components" survives as typed clients behind per-feature services. The ESLint `no-restricted-imports` rule that enforced it has no Blazor equivalent; replace with an `Architecture.Tests` rule, which is stronger — it fails the build rather than a lint pass.
- **Stack table** — remove Angular 22.1.7, Angular Material 22.1.7, `ng-openapi-gen`, TypeScript 6.0.3, Node 24.21.0. Add:

  | Package | Version | License |
  |---|---|---|
  | Microsoft.AspNetCore.Components.WebAssembly | 10.0.12 | MIT |
  | MudBlazor | 9.10.0 | MIT |
  | NSwag.MSBuild | 14.7.1 | MIT |
  | bUnit | 2.11.3 | MIT |

  All verified against nuget.org on 2026-09-21. NFR9 holds.
- **NFR8 (tests)** — "Angular unit tests cover view-model logic" → bUnit component tests. `Web.E2E` Playwright is unaffected as a framework; only selectors change.
- **NFR10 (portability)** — improves. The `web` image loses its Node build stage entirely; it becomes a .NET publish stage producing static WASM assets served by the same nginx.
- **CI (`ci.yml`)** — remove `ng lint`, `ng test`, `ng build`; the web project builds and tests as part of `dotnet build` / `dotnet test`. Net simplification.
- **Compose topology** — unchanged. `db`, `migrate`, `api`, `web` (nginx), `receiver`. Blazor WASM is static files behind the same nginx with the same `API_UPSTREAM` templating and the same 200s proxy timeouts.
- **Structural Seed** — `web/actionledger-web/` (Angular tree) → `src/ActionLedger.Web/` (Blazor project) with `Features/`, `Shared/`, `Core/`. `openapi.json` moves with it.

### 2.4 What does not change

Domain, Application, Infrastructure, Api, all seven test projects, all 7 ADRs, the database schema, the webhook design, the Evaluation Gate, the fixture catalog, the migration bundle, the compose service list, and every functional requirement's *behaviour*. The three HTML mockups remain valid visual reference — they are plain HTML and CSS.

---

## 3. Recommended Approach

**Selected: Option 1 — Direct Adjustment.**

| Option | Verdict | Reasoning |
|---|---|---|
| 1. Direct adjustment | **Viable — selected** | Modify existing stories in place. No epic restructuring. Effort: Medium (document editing only). Risk: Medium |
| 2. Rollback | **Not applicable** | Nothing to roll back — no UI code exists. 1.1 and 1.2 are backend and pipeline |
| 3. MVP review | **Not required** | Every functional requirement survives unchanged. The MVP is untouched; only its implementation technology moves |

**Effort:** Medium — roughly 2 to 3 hours of documentation work across six artifacts. No code is discarded.

**Risk:** Medium, concentrated in one place. MudBlazor implements Material Design but is not a drop-in for Angular Material 3's token system; `DESIGN.md` currently inherits its entire colour ramp from the prebuilt `azure-blue` MD3 theme via `material.<token>` references. Those references need remapping onto a MudBlazor palette, and the mapping is judgment work rather than search-and-replace. The four semantic provenance families — the actual design idea — are framework-independent and carry over intact.

**Timeline impact:** Net roughly neutral, plausibly positive. Two to three hours of replanning against the removal of an entire toolchain the developer would otherwise be learning under deadline.

---

## 4. Detailed Change Proposals

### 4.1 `ARCHITECTURE-SPINE.md`

**AD-13** — OLD: "The web build generates its client with `ng-openapi-gen` (MIT) into `src/app/core/api/` (git-ignored) in the npm `prebuild` script, so a contract change fails the Angular build by type error and the `web` image has no .NET stage."
NEW: "The web build generates its client with `NSwag.MSBuild` (MIT) into `src/ActionLedger.Web/Core/Api/` (git-ignored) as a pre-build target, so a contract change fails the Blazor build by compile error. The `web` image publishes the WASM assets and serves them from nginx, so it needs no Node stage."
*Rationale:* preserves the contract-breaks-the-build guarantee, which is the point of AD-13.

**AD-14** — retitle "Frontend is MVVM with feature folders and no HTTP in components" → "Frontend is component-per-route with feature folders and no HTTP in components". Replace the Angular file conventions and the ESLint rule with Blazor equivalents and an `Architecture.Tests` rule asserting `HttpClient` is referenced only from `Core/` and `Features/*/Data/`.
*Rationale:* the discipline is what matters; enforcing it in the build is stronger than a lint rule.

**Stack table** — remove five Angular/Node rows, add the four rows in §2.3.

**Structural Seed** — replace the `web/actionledger-web/` tree with `src/ActionLedger.Web/`.

**Consistency Conventions** — "Angular files `<name>.<container|component|service|store>.ts`" → Blazor naming; Pipeline row drops `ng lint`/`ng test`/`ng build`.

### 4.2 `ARCHITECTURE.md`

Update the frontend narrative sections and the `docs/frontend-architecture.md` deliverable description to describe the Blazor component model.

### 4.3 `prd.md`

- **FR27** — "the Angular typed client is generated from the committed `openapi.json`" → "the Blazor typed client is generated from the committed `openapi.json`".
- **NFR8** — "Angular unit tests cover view-model logic" → "bUnit component tests cover component logic".
- **NFR9** — keep the licence allowlist; the excluded-package list is unaffected.
- **NFR10** — "no host-installed .NET or Node needed" → "no host-installed .NET needed" (Node is gone entirely).

### 4.4 `epics.md`

- **Story 1.5** — retitle "Angular scaffold with Material theme, tokens, and the generated API client" → "Blazor WebAssembly scaffold with MudBlazor theme, tokens, and the generated API client". Rewrite acceptance criteria: `dotnet new blazorwasm`, MudBlazor registration, CSS custom properties, NSwag pre-build target, `Architecture.Tests` HTTP rule.
- **Story 1.6** — replace `core/auth/session.store.ts` and `core/users/users.store.ts` with Blazor scoped services; `mat-progress-bar` → `MudProgressLinear`; snackbar → `ISnackbar`; keep every state-pattern behaviour verbatim.
- **Stories 2.2, 2.6, 3.4, 3.5, 4.3, 4.4, 4.5, 7.2** — re-express component references (`mat-table` → `MudTable`, `mat-expansion-panel` → `MudExpansionPanels`, `mat-chip` → `MudChip`, date picker → `MudDatePicker`, dialogs → `IDialogService`). **All behavioural acceptance criteria are unchanged.**
- **Story 6.4** — Playwright test unchanged in intent; selectors re-expressed.
- **Story 6.6** — `docs/frontend-architecture.md` content changes; deliverable list otherwise unchanged.
- **Additional Requirements block** — replace the `ng new` line with the Blazor equivalent; drop the TypeScript pin.

### 4.5 `DESIGN.md`

- **Front matter and Brand & Style** — base theme becomes a MudBlazor palette rather than the Angular Material prebuilt `azure-blue`.
- **Colors** — resolve every `material.<token>` reference to an explicit MudBlazor palette entry or a literal hex value. The four semantic provenance families keep their hex values unchanged.
- **Components** — `mat-chip`/`mat-icon` composition → `MudChip`/`MudIcon`. `ProvenanceChip`, `LowConfidenceBadge`, `OverdueIndicator`, `StatusChip`, `ReviewStateChip`, `PendingCounter`, `ProposalCard`, `AuditEntry` all keep their specified behaviour, content, and accessibility requirements.
- **Do's and Don'ts** — "no Material component is restyled" → "no MudBlazor component is restyled".

### 4.6 `EXPERIENCE.md`

- **Component Patterns** and **State Patterns** — re-express component names; keep every behaviour, string, and accessibility requirement verbatim.
- **Voice and Tone**, **Provenance Language**, **Accessibility Floor**, **Key Flows UJ-1 to UJ-4** — **no change**. These are framework-independent and are the substance of the document.

---

## 5. Implementation Handoff

**Scope: Moderate** — backlog reorganization across six artifacts, no fundamental replan. Epic structure, story count, and every functional requirement survive.

| Step | Work | Blocking? |
|---|---|---|
| 1 | `ARCHITECTURE-SPINE.md` + `ARCHITECTURE.md` | **Yes — blocks Story 1.5** |
| 2 | `epics.md` — 10 stories plus Additional Requirements | **Yes — blocks Story 1.5** |
| 3 | `prd.md` — FR27, NFR8, NFR10 | No |
| 4 | `DESIGN.md` — token remap and component vocabulary | Blocks Story 1.6, not 1.5 |
| 5 | `EXPERIENCE.md` — component and state patterns | Blocks Story 1.6, not 1.5 |
| 6 | Re-run `bmad-sprint-planning` to refresh story keys after retitling | Before Story 1.5 |

**Story 1.3 continues uninterrupted.** It is database, migrations, and seeded users — no UI surface.

**Success criteria.**

1. No Angular, Node, npm, TypeScript, or `ng-openapi-gen` reference remains in any planning artifact.
2. Every behavioural acceptance criterion in the 10 UI stories is preserved — only component vocabulary changes.
3. `DESIGN.md`'s four semantic provenance families keep their hex values.
4. AD-13's contract-breaks-the-build guarantee still holds under NSwag.
5. `sprint-status.yaml` regenerated; story count still 35.
6. All 7 ADRs untouched.

**Handoff:** Developer agent, direct implementation of the artifact edits above, then resume the build at Story 1.4.
