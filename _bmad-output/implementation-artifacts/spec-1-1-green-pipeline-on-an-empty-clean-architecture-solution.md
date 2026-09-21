---
title: 'Story 1.1 — Green pipeline on an empty Clean Architecture solution'
type: 'chore'
created: '2026-09-20'
status: 'in-progress'
route: 'dispatch'
baseline_commit: 'NO_VCS' # no git repository existed at baseline
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** ActionLedger has complete planning artifacts but no repository, no solution, and no pipeline. With a code freeze on 2026-09-21 there is no room to retrofit quality gates later, and every subsequent story assumes it is merging into a protected branch with architecture rules already enforced.

**Approach:** Create the GitHub repository and an empty four-ring .NET solution with its seven test projects, implement the AD-1 dependency rules as executable architecture tests, and land CI, CodeQL, Dependabot, the PR template, branch protection, and the seeded Project board — all green on the first pull request, with no feature code.

## Boundaries & Constraints

**Always:** Four src rings `ActionLedger.Domain|Application|Infrastructure|Api` with dependencies pointing inward, and seven test projects `Domain.Tests`, `Application.Tests`, `Infrastructure.Tests`, `Api.Tests`, `Architecture.Tests`, `Eval`, `Web.E2E`, all on xunit.v3 4.0.1. Directory layout matches the spine's Structural Seed. Package versions come from the spine's Stack table — no drift, no `latest`. Only MIT, Apache-2.0, or BSD dependencies; FluentAssertions 8+, MediatR 13+, AutoMapper 15+, MassTransit 9, and JsonSchema.Net are excluded. Conventional commits; the work lands through a pull request that links its issue.

**Never:** No feature code, no entities, no controllers, no EF DbContext, no Angular app — those are stories 1.2 to 1.7. No secret committed. No Domain or Application reference to EF Core, ASP.NET Core, Npgsql, OpenAI, or Microsoft.Extensions.AI. No test that requires a network, a container, or a browser at this stage.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Clean clone builds | Pinned SDK, no cache | `dotnet build` succeeds; `dotnet test` runs all 7 projects green | N/A |
| Dependency rules hold | Real compiled assemblies | Every AD-1 rule asserts zero violations | Test fails naming the offending type |
| Domain takes a package | Any PackageReference in Domain | Architecture test fails | Build red before merge |
| Application reaches outside allowlist | Reference beyond Domain + the three allowed packages | Architecture test fails | Build red before merge |
| Anything references Api | Project reference to Api | Architecture test fails | Build red before merge |
| Normalizer placed wrong | Type named `*Normaliz*` outside `Application/Ai` | Architecture test fails | Build red before merge |
| PR without linked issue | PR body has no issue link | Required check fails | Merge blocked by branch protection |

## Decisions

- **Repository:** `bspann/ActionLedger`, **public**. CodeQL, secret scanning, and push protection are free on public repositories, so NFR5 is met as written. All demo content is fictional.
- **SDK:** install .NET SDK **10.0.401** and pin it exactly in `global.json` (`rollForward: disable`); `ci.yml` pins the same version. Local and CI compile on one feature band.
- **Board:** one issue per **all 35 stories** in `epics.md`, including the three conditional Epic 7 stories.
- **Spec scope:** kept whole — board seeding ships in this story rather than a follow-up.

</frozen-after-approval>

## Code Map

Greenfield — nothing to reuse, nothing to avoid breaking. Authoritative sources for this story:

- `_bmad-output/planning-artifacts/architecture/architecture-ActionLedger-2026-09-20/ARCHITECTURE-SPINE.md` -- AD-1 rule text (line 61), AD-18 test placement (line 165), Consistency Conventions table (line 182+) for Pipeline/Branching/Licensing rows, Stack table (line 201+) for every pinned version, Structural Seed (line 234+) for the exact directory tree
- `_bmad-output/planning-artifacts/epics.md` -- Story 1.1 ACs at line 196; the 35 story headings the board script consumes
- `_bmad-output/implementation-artifacts/epic-1-context.md` -- compiled epic context; the pipeline and licensing constraints in short form
- `_bmad-output/implementation-artifacts/sprint-status.yaml` -- `1-1-green-pipeline-on-an-empty-clean-architecture-solution`, to flip on completion
- `docs/bmad-seed-prompt.md` -- existing file; leave in place, it ships under `docs/` at tag time

## Tasks & Acceptance

**Execution:**
- [ ] `git init` + `.gitignore` -- initialize the repo on `main`, standard VisualStudio + Node ignores, plus `web/actionledger-web/src/app/core/api/` -- every later story merges through a PR here
- [ ] `global.json` -- pin SDK `10.0.401` with `rollForward: disable`; install it first -- clean clone and CI must agree on the toolchain
- [ ] `Directory.Build.props` -- `net10.0`, `nullable enable`, `ImplicitUsings enable`, `TreatWarningsAsErrors`, `LangVersion latest` -- one place sets the compiler contract
- [ ] `Directory.Packages.props` -- central package management with the spine's exact versions -- version drift is a licensing and reproducibility risk
- [ ] `ActionLedger.sln` + `src/ActionLedger.{Domain,Application,Infrastructure,Api}` -- four rings, project references inward only, Domain with zero PackageReferences -- AD-1
- [ ] `tests/{Domain,Application,Infrastructure,Api}.Tests`, `tests/Architecture.Tests`, `tests/Eval`, `tests/Web.E2E` -- seven xunit.v3 4.0.1 projects, each with one passing placeholder so `dotnet test` is meaningful -- AD-18
- [ ] `tests/Architecture.Tests/DependencyRuleTests.cs` -- NetArchTest.eNhancedEdition 1.4.5 asserting all six AD-1 rules against the real assemblies -- the dependency rule is enforced by the build, not by discipline
- [ ] `.github/workflows/ci.yml` -- restore, build, `dotnet test` across all projects, on PR and `main` -- the required check
- [ ] `.github/workflows/codeql.yml` + `.github/dependabot.yml` -- C# (and later npm) scanning and update PRs -- NFR5
- [ ] `.github/pull_request_template.md` -- checklist: issue link, tests added, license review, axe pass for UI stories -- the spine's Branching row
- [ ] `.github/workflows/require-linked-issue.yml` -- required check asserting the PR body links an issue -- branch protection cannot require issue linkage natively
- [ ] `tools/seed-project-board.sh` -- write the `gh` script that creates the Project board and one issue per story parsed from `epics.md`, idempotent on re-run; **write it only, do not run it** -- AC 4
**Landing (NOT part of implementation — the orchestrator runs these after review, with the human present):**
- [ ] create `bspann/ActionLedger` public on GitHub, push `main` -- AC 3
- [ ] enable secret scanning and push protection -- NFR5
- [ ] apply branch protection requiring `ci.yml` and the linked-issue check -- AC 3
- [ ] run `tools/seed-project-board.sh` to create the board and 35 issues -- AC 4
- [ ] open the pull request for this story, linking its issue -- AC 2

**Acceptance Criteria:**
- Given a clean clone on the pinned SDK, when I run `dotnet build` then `dotnet test`, then both succeed and all seven test projects report passing.
- Given the first pull request, when CI runs, then restore, build, and every test project report green and the required checks pass.
- Given branch protection on `main`, when a pull request has no linked issue, then merge is blocked.
- Given the seeded board, when I open it, then one issue exists per story from `epics.md` and this pull request links its own issue.
- Given `git log`, when I read it, then every commit follows conventional commit format.

## Implementation Notes

## Spec Change Log

## Review Triage Log

## Design Notes

**Toolchain location.** SDK 10.0.401 is installed at `~/.dotnet`, not the root-owned `/usr/local/share/dotnet` (which holds only 10.0.1xx and 8.0.x). `global.json` pins `10.0.401` with `rollForward: disable`, so every `dotnet` invocation in this repo must run with `export PATH="$HOME/.dotnet:$PATH"` and `export DOTNET_ROOT="$HOME/.dotnet"` first, or it will fail to resolve the pinned SDK. `ci.yml` uses `actions/setup-dotnet` with `dotnet-version: 10.0.401` and needs no such handling.

**Implementation is local-only.** `git init`, commits, every file, and the board script are in scope; creating the GitHub repository, pushing, enabling scanning, applying branch protection, and running the board script are not. Those are outward-facing and irreversible, so they happen at the landing step under human supervision. Write the script; do not execute it.

**Architecture tests assert, they do not self-test.** Writing a fixture that genuinely violates AD-1 would mean committing a project that breaks the build. The rules are therefore asserted against the real compiled assemblies — zero violations today, red the moment someone adds the forbidden reference. That is what "fails when" means operationally and it is how the rule earns its keep from story 1.2 onward.

**Placeholder tests are deliberate.** Each of the seven projects gets one trivial passing test so `dotnet test` exercises the whole matrix from day one and a later story adding real tests changes only content, not wiring. `Web.E2E` and `Infrastructure.Tests` must not touch Playwright browsers or Docker yet — those arrive with 1.7 and 1.3 respectively, and CI has to stay green tonight.

**Central Package Management** (`Directory.Packages.props`) rather than per-project versions: eleven projects sharing pinned versions is exactly the case it exists for, and it makes the license allowlist auditable in one file.

## Verification

**Commands:**
- `export PATH="$HOME/.dotnet:$PATH" && export DOTNET_ROOT="$HOME/.dotnet"` -- expected: `dotnet --version` prints `10.0.401`
- `dotnet build ActionLedger.sln` -- expected: succeeds, zero warnings (warnings are errors)
- `dotnet test ActionLedger.sln` -- expected: all seven test projects pass
- `dotnet test tests/Architecture.Tests` -- expected: every AD-1 rule reports zero violations
- `gh pr checks` -- expected: `ci.yml` and the linked-issue check both green
- `gh api repos/{owner}/ActionLedger/branches/main/protection --jq '.required_status_checks.contexts'` -- expected: lists both required checks
- `gh issue list --limit 100 --json number --jq 'length'` -- expected: 35
