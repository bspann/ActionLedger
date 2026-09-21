---
title: 'Story 1.1 — Green pipeline on an empty Clean Architecture solution'
type: 'chore'
created: '2026-09-20'
status: 'review'
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
- [x] `git init` + `.gitignore` -- initialize the repo on `main`, standard VisualStudio + Node ignores, plus `web/actionledger-web/src/app/core/api/` -- every later story merges through a PR here
- [x] `global.json` -- pin SDK `10.0.401` with `rollForward: disable`; install it first -- clean clone and CI must agree on the toolchain
- [x] `Directory.Build.props` -- `net10.0`, `nullable enable`, `ImplicitUsings enable`, `TreatWarningsAsErrors`, `LangVersion latest` -- one place sets the compiler contract
- [x] `Directory.Packages.props` -- central package management with the spine's exact versions -- version drift is a licensing and reproducibility risk
- [x] `ActionLedger.sln` + `src/ActionLedger.{Domain,Application,Infrastructure,Api}` -- four rings, project references inward only, Domain with zero PackageReferences -- AD-1
- [x] `tests/{Domain,Application,Infrastructure,Api}.Tests`, `tests/Architecture.Tests`, `tests/Eval`, `tests/Web.E2E` -- seven xunit.v3 4.0.1 projects, each with one passing placeholder so `dotnet test` is meaningful -- AD-18
- [x] `tests/Architecture.Tests/DependencyRuleTests.cs` -- NetArchTest.eNhancedEdition 1.4.5 asserting all six AD-1 rules against the real assemblies -- the dependency rule is enforced by the build, not by discipline
- [x] `.github/workflows/ci.yml` -- restore, build, `dotnet test` across all projects, on PR and `main` -- the required check
- [x] `.github/workflows/codeql.yml` + `.github/dependabot.yml` -- C# (and later npm) scanning and update PRs -- NFR5
- [x] `.github/pull_request_template.md` -- checklist: issue link, tests added, license review, axe pass for UI stories -- the spine's Branching row
- [x] `.github/workflows/require-linked-issue.yml` -- required check asserting the PR body links an issue -- branch protection cannot require issue linkage natively
- [x] `tools/seed-project-board.sh` -- write the `gh` script that creates the Project board and one issue per story parsed from `epics.md`, idempotent on re-run; **write it only, do not run it** -- AC 4
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

**All seven test projects run under Microsoft.Testing.Platform, not VSTest.** xunit.v3 4.0.1 on the .NET 10 SDK refuses to run under the VSTest path (`Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later`). The opt-in is `"test": { "runner": "Microsoft.Testing.Platform" }` in `global.json`. Consequence: `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` are deliberately absent — MTP mode requires that no test project use VSTest, and xunit.v3 test projects are self-executing MTP applications. `Directory.Packages.props` therefore pins two packages, not four.

**`Api` is a web project with an empty host.** `src/ActionLedger.Api` uses `Microsoft.NET.Sdk.Web` and a three-line `Program.cs` that builds and runs a host with no routes. Two reasons: AD-1's rule that Domain and Application never reference ASP.NET Core is only meaningful asserted against a graph where ASP.NET Core actually exists, and Story 1.2 then extends the project rather than converting it. No controllers, no auth, no OpenAPI — those are 1.2.

**Each ring carries an assembly marker.** `DomainAssemblyMarker` and its three siblings are empty public classes. They give the architecture tests and the ring test projects a compile-time handle on the real compiled assembly, and they keep NetArchTest's type queries non-vacuous while the rings hold no code.

**AD-1 is asserted two ways, and it has to be.** Roslyn prunes unused assembly references out of the metadata it emits, so a `PackageReference` added to Domain but not yet called is invisible to every assembly-level check — including NetArchTest. The I/O matrix row "Domain takes a package | Any PackageReference in Domain | Architecture test fails" therefore cannot be satisfied by NetArchTest alone. `DependencyRuleTests` pairs each NetArchTest rule with a scan of the project files (`ProjectFile.cs`), which also folds in any `Directory.Build.props` between the repository root and the ring, so a package smuggled into a shared props file still counts against the ring that carries it.

**The story is on a `chore/` branch, not on `main`.** `main` holds one bootstrap commit (`.gitignore` plus the planning artifacts); everything else is on `chore/1-1-green-pipeline`. That gives the landing step a real pull request with real content and real checks, per the Branching convention, rather than a pull request with nothing in it. If the orchestrator would rather have it all on `main`, it is a fast-forward merge.

**The Microsoft.Testing.Platform packages are pinned even though nothing references them directly.** `xunit.v3` 4.0.1 asks for `Microsoft.Testing.Platform >= 2.4.0`, not an exact version, and 2.4.1 is already published. `dotnet test` on the .NET 10 SDK drives the test application over MTP, and a host/platform mismatch there surfaces as a run reporting *zero tests* rather than as a restore error — a failure that looks like a broken test project but is really package drift. `Directory.Packages.props` pins the four MTP packages at 2.4.0 under central transitive pinning; the pin was verified to bind by moving it to 2.4.1 and watching restore follow.

**`dotnet test` mode is chosen by `global.json` discovery, which is cwd-sensitive.** Run from inside the repository, `dotnet test` uses MTP and passes. Run from outside it (`cd ~ && dotnet test /path/to/ActionLedger.sln`), `global.json` is never found, `dotnet test` falls back to VSTest, and the run fails loudly with `Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later`. `ci.yml` runs from the checkout root, so CI always gets MTP. Worth knowing before debugging a local run that behaves differently from CI.

**The BMAD skill mirrors are git-ignored.** `.agents/` and `.claude/skills/` are two identical 13 MB installer-generated copies of the skill library, and `_bmad/config.user.toml` holds per-person install answers. `_bmad/` config, `_bmad-output/` artifacts, and `docs/` are tracked — the board script reads `epics.md` from `_bmad-output/` at landing time.

## Spec Change Log

- **Testing packages.** The spec's Execution list implies the usual xunit test-project triple. Only `xunit.v3` 4.0.1 is present; `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` are omitted because MTP mode rejects a solution where any test project uses VSTest. Both omitted packages are outside the spine's Stack table, so no pinned version was dropped.
- **`Api` project type.** Not specified either way in the spec. Implemented as `Microsoft.NET.Sdk.Web` with an empty host rather than a class library.
- **`ProjectFile.cs` alongside `DependencyRuleTests.cs`.** The spec names one file for the architecture rules. The project-file reader is a second file in the same project, because the I/O matrix demands a check NetArchTest cannot perform.
- **Branch layout.** The spec's landing list says "push `main`" and "open the pull request"; the work is staged on `chore/1-1-green-pipeline` so both are possible.

## Review Triage Log

## Design Notes

**Toolchain location.** SDK 10.0.401 is installed at `~/.dotnet`, not the root-owned `/usr/local/share/dotnet` (which holds only 10.0.1xx and 8.0.x). `global.json` pins `10.0.401` with `rollForward: disable`, so every `dotnet` invocation in this repo must run with `export PATH="$HOME/.dotnet:$PATH"` and `export DOTNET_ROOT="$HOME/.dotnet"` first, or it will fail to resolve the pinned SDK. `ci.yml` uses `actions/setup-dotnet` with `dotnet-version: 10.0.401` and needs no such handling.

**Implementation is local-only.** `git init`, commits, every file, and the board script are in scope; creating the GitHub repository, pushing, enabling scanning, applying branch protection, and running the board script are not. Those are outward-facing and irreversible, so they happen at the landing step under human supervision. Write the script; do not execute it.

**Architecture tests assert, they do not self-test.** Writing a fixture that genuinely violates AD-1 would mean committing a project that breaks the build. The rules are therefore asserted against the real compiled assemblies — zero violations today, red the moment someone adds the forbidden reference. That is what "fails when" means operationally and it is how the rule earns its keep from story 1.2 onward.

**Placeholder tests are deliberate.** Each of the seven projects gets one trivial passing test so `dotnet test` exercises the whole matrix from day one and a later story adding real tests changes only content, not wiring. `Web.E2E` and `Infrastructure.Tests` must not touch Playwright browsers or Docker yet — those arrive with 1.7 and 1.3 respectively, and CI has to stay green tonight.

**Central Package Management** (`Directory.Packages.props`) rather than per-project versions: eleven projects sharing pinned versions is exactly the case it exists for, and it makes the license allowlist auditable in one file.

## Verification

**Run on 2026-09-20 against the working tree, SDK 10.0.401 at `~/.dotnet`.**

| Command | Expected | Result |
|---------|----------|--------|
| `dotnet --version` | `10.0.401` | `10.0.401` |
| `dotnet build ActionLedger.sln` | succeeds, zero warnings | Build succeeded, 0 Warning(s), 0 Error(s) |
| `dotnet test ActionLedger.sln` | all seven test projects pass | Passed — 7 projects, 19 tests, 0 failed |
| `dotnet test tests/Architecture.Tests` | every AD-1 rule reports zero violations | 13 tests, 0 failed |
| clean clone, `dotnet build` then `dotnet test` | both succeed | verified in a fresh `git clone` of the branch |
| wipe every `bin`/`obj`, then `dotnet restore` + `dotnet build` + `dotnet test ActionLedger.sln` | all seven pass | Passed — total 19, failed 0, exit code 0 |

**The AD-1 rules were verified red, then reverted.** Each violation was introduced, observed to fail, and removed:

| Violation introduced | Test that failed |
|---------------------|------------------|
| `PackageReference` on Domain | `Rule1_domain_declares_no_package_and_no_project_reference`, `Rule3_inner_rings_declare_no_persistence_web_or_ai_package(Domain)` |
| Domain type using `Npgsql.NpgsqlConnection` | `Rule3_inner_ring_types_never_touch_persistence_web_or_ai`, naming the offending type |
| Type named `TempTextNormalizer` in Domain | `Rule5_normalization_types_live_only_in_application_ai` |
| `PackageReference` on Application outside the allowlist | `Rule2_application_takes_no_package_outside_the_allowlist`, `Rule3_inner_rings_declare_no_persistence_web_or_ai_package(Application)` |
| Extra `ProjectReference` on Infrastructure | `Rule6_infrastructure_references_application_and_domain_only` |

Rule 4 (nothing references Api) and the inward `ProjectReference` forms of rules 1, 2, and 6 cannot be violated in a real project file without creating a reference cycle, which MSBuild rejects before the tests run. They are belt-and-braces over a rule the build already enforces, and they go red for a *type-level* violation, which the build does not catch.

**Not verified here — these run at landing, with a human present:**

- `gh pr checks` — `ci.yml` and `require-linked-issue` both green
- `gh api repos/{owner}/ActionLedger/branches/main/protection --jq '.required_status_checks.contexts'` — lists both required checks
- `gh issue list --limit 100 --json number --jq 'length'` — 35
- `tools/seed-project-board.sh` — syntax-checked (`bash -n`) and its `epics.md` parse verified to yield exactly 35 stories with the right epic on each; the `gh` calls themselves are unexercised until the repository exists.
