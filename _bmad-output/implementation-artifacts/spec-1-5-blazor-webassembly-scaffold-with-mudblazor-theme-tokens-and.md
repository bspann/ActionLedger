---
title: 'Story 1.5 — Blazor WebAssembly scaffold with MudBlazor theme, tokens, and the generated API client'
type: 'feature'
created: '2026-09-21'
status: 'done'
route: 'dispatch'
baseline_commit: 'e4f99d44ec2a79b0641f92b62cfebcabca4736e1'
baseline_revision: 'e4f99d44ec2a79b0641f92b62cfebcabca4736e1'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-ActionLedger-2026-09-20/DESIGN.md'
warnings: ['oversized']
deferred:
  - summary: >-
      Roboto and Roboto Mono load from the Google Fonts CDN, so a demo run with no internet
      falls back to system faces.
    evidence: |-
      wwwroot/index.html reaches fonts.googleapis.com for both families. DESIGN.md explicitly
      sanctions this ("load it in wwwroot/index.html beside Roboto or fall back to ui-monospace")
      and tokens.css ends the confidence-score stack in ui-monospace/monospace, so two-decimal
      scores still align in a column and nothing breaks — only the typeface changes. It is
      recorded because the deliverable is a live demo on 2026-09-23 and the venue's connectivity
      is unknown, and because NFR10 asks for a clone-and-run experience. Vendoring the woff2
      files into wwwroot is new surface rather than a smallest fix, and no acceptance criterion
      asks for it.
    location: >-
      src/ActionLedger.Web/wwwroot/index.html
    severity: low
  - summary: >-
      The generated API client is added to Compile by a target rather than by the project's item
      glob, so an IDE's design-time build may not surface it to IntelliSense.
    evidence: |-
      ActionLedger.Web.csproj removes Core/Api/ActionLedgerApiClient.g.cs from the glob (it does
      not exist on a clean clone, before evaluation) and IncludeGeneratedApiClient adds it during
      execution. `dotnet build`, `dotnet test`, and `dotnet publish` were all verified working, so
      the command line is unaffected; what is untested is whether Rider or Visual Studio picks up
      a dynamically added Compile item during CompileDesignTime. If it does not, IActionLedgerApiClient
      shows as unresolved in the editor while the build succeeds — visible and confusing during a
      live walkthrough. What would settle it: open ActionLedger.sln in Rider or Visual Studio from
      a clean clone and check whether AuthService.cs resolves IActionLedgerApiClient before and
      after a build.
    location: >-
      src/ActionLedger.Web/ActionLedger.Web.csproj
    severity: medium (unverified)
---

<intent-contract>

## Intent

**Problem:** There is no web project. The contract is committed under the deleted Angular tree at `web/actionledger-web/openapi.json`, nothing generates a client from it, AD-14's "no HTTP in components" rule has no enforcement now that the ESLint rule it named is gone, and DESIGN.md's four semantic colour families exist only as prose. Story 1.6 has no shell to build in and Story 1.7 has nothing to publish into the `web` image.

**Approach:** Create `src/ActionLedger.Web` on Blazor WebAssembly with MudBlazor, move the committed contract beside it, generate the typed client from that file in a pre-build target into a git-ignored folder, express DESIGN.md's token delta as CSS custom properties with light and dark pairs, and replace the lost lint rule with an `Architecture.Tests` rule that fails the build when HTTP escapes `Core/` or `Features/*/Data/`. Add `tests/Web.Tests` on bUnit so component tests ride the existing `dotnet test`.

## Boundaries & Constraints

**Always:** The committed contract lives at `src/ActionLedger.Web/openapi.json`; `OpenApiExport.ContractRelativePath` is the single source of that path and both `--export-openapi` and `OpenApiSnapshotTest` read it (AD-13). The typed client is generated at build time into `src/ActionLedger.Web/Core/Api/` and is git-ignored — never committed (AD-13). Feature folders are `Auth`, `Meetings`, `Review`, `Actions`, `Audit` under `Features/`; HTTP happens only in `Core/` and `Features/*/Data/` (AD-14). Routable components are `<Noun>Page.razor`, presentational are `<Noun>.razor`, per-feature clients are `<Feature>Service.cs`, scoped state is `<Noun>State.cs`. Every colour, type role, and spacing value comes verbatim from DESIGN.md's front matter. New packages need a `PackageVersion` in `Directory.Packages.props` and must be MIT, Apache-2.0, or BSD (NFR9); versions come from the spine's Stack table. Test method names are `Sentence_case_with_underscores`, assertions are xunit built-ins, every test class carries a `/// <summary>` naming the AD or DESIGN.md rule it defends, and every awaited call takes `TestContext.Current.CancellationToken`. Both new projects join `ActionLedger.sln` so `dotnet build`/`dotnet test` reach them with no `ci.yml` step change.

**Never:** No Node, npm, TypeScript, Angular, or `ng-openapi-gen` anywhere — not in a package, a workflow, a Dockerfile, or an ignore rule (sprint change 2026-09-21). No `dotnet workload install` and no AOT or trimming opt-in: a stock SDK 10.0.401 must build and publish this project. No MudBlazor component is restyled — buttons, fields, tables, cards, dialogs, snackbars, and navigation ship as rendered (DESIGN.md). No `ProjectReference` from `ActionLedger.Web` to any project under `src/` — the committed document is the boundary, not a shared assembly (AD-13, AD-1 Rule 4). No routable page components, login screen, session state, user directory, delegating handler, app bar, or global loading/error behaviour — all of that is Story 1.6; this story ships the router that will host them and nothing routable to put in it. No `Dockerfile` or `nginx.conf.template` — Story 1.7. No brand chips or badges — the screens that use them land in Epics 3 and 4. No credential or real URL literal in any committed file (NFR5). No hand-edit of a generated file.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Client generated on a clean clone | No `Core/Api/` on disk, `dotnet build` | The pre-build target writes the client and the project compiles | A generator failure fails the build with NSwag's output |
| Contract re-exported | `openapi.json` newer than the generated file | The target reruns before compile; the client matches the new contract | N/A |
| Contract unchanged | Second build, nothing touched | The target is skipped — inputs/outputs are up to date | N/A |
| Contract change not re-exported | Web code calls an operation absent from the committed contract | `dotnet build` fails with a compile error naming the missing member | N/A |
| Generated client never committed | `git status` after a build | `Core/Api/` is ignored; the tree is clean | N/A |
| HTTP outside the seam | A type outside `Core/` or `Features/*/Data/` depends on `System.Net.Http` or the generated client namespace | `Architecture.Tests` fails naming the offending type | N/A |
| HTTP inside the seam | The registration in `Core/` and `Features/Auth/Data/` | The rule passes | N/A |
| Rule cannot pass vacuously | No type in the web assembly touches HTTP at all | The rule fails rather than passing on an empty set | N/A |
| Feature folders present | `Features/` on disk | All five of `Auth`, `Meetings`, `Review`, `Actions`, `Audit` exist, each with a `Data/` folder | Test fails naming the missing folder |
| Export follows the move | `--export-openapi`, no database, `Database__ConnectionString`/`Jwt__Key`/`Jwt__Issuer` unset | Writes `src/ActionLedger.Web/openapi.json`, exit 0 | Exit 1 with the path on a write failure |
| Snapshot follows the move | `OpenApiSnapshotTest` | Compares against `src/ActionLedger.Web/openapi.json` | Failure message names the new path and the re-export command |
| Old contract path gone | `web/` on disk | The directory no longer exists and nothing references it | N/A |
| Semantic tokens, light | `wwwroot/css/tokens.css` default scope | ai-provenance, human-provenance, low-confidence, success, and neutral-container families with their on-colours, at DESIGN.md's hex | Test fails naming the missing or wrong property |
| Semantic tokens, dark | The dark scope of the same file | Every family's `-dark` pair at DESIGN.md's hex | Same |
| Base container tokens | Same file, both scopes | primary-container, error-container, and surface-container-low with their on-colours, both modes | Same |
| Type and spacing roles | Same file | `confidence-score` (Roboto Mono, 13px, 500, 1.2) and `source-excerpt` (Roboto, 14px, 400, 1.5); content-max 1280px, page-gutter 24px, notes-pane-min 360px | Same |
| Monospace survives an offline load | Roboto Mono unavailable | The confidence-score role falls back to `ui-monospace` rather than a proportional face | N/A |
| Fonts requested | `wwwroot/index.html` | Both Roboto and Roboto Mono are loaded | Test fails naming the missing family |
| Theme registered | The app renders | `AddMudServices` is called and `MudThemeProvider` is given the app's own `MudTheme`, following the OS light/dark preference | N/A |
| Content width | The layout renders | Content sits in a 1280px container with 24px side gutters | N/A |
| Solution membership | `dotnet build ActionLedger.sln` then `dotnet test ActionLedger.sln` | Both new projects build and `Web.Tests` runs, with zero warnings and no Node step | N/A |

</intent-contract>

## Code Map

**Read before writing anything. Most of the contract plumbing already exists and only its path changes.**

- `src/ActionLedger.Api/OpenApi/OpenApiExport.cs:29` -- `public const string ContractRelativePath = "web/actionledger-web/openapi.json"`. **This one constant is the whole move.** `:123` `CommittedContractPath()` joins it onto the directory holding `ActionLedger.sln`; `:119` and `:8` repeat the old path in doc comments and must follow. Nothing else in `src/` names the path.
- `tests/Api.Tests/OpenApiSnapshotTest.cs:26,30,42` -- reads `OpenApiExport.CommittedContractPath()` and prints `ContractRelativePath` in both failure messages, so it follows the constant with no edit. `:8` the class summary says "Story 1.5 generates the Angular client" — correct it.
- `web/actionledger-web/openapi.json` -- the only file under `web/`. 12,267 bytes, OpenAPI 3.1.1, paths `/health`, `/health/ready`, `/api/v1/auth/login`, `/api/v1/users`; components `SignInCommand`, `SignInResult`, `UserSummaryDto`, `PagedResultOfUserSummaryDto`, `ProblemDetails`, `Role`. Move it with `git mv` and delete `web/`.
- `.gitignore:100` -- `web/actionledger-web/src/app/core/api/` is the Angular generated-client rule; replace with `src/ActionLedger.Web/Core/Api/`, and fix the `:99` comment above it. The `# Node / Angular` block at `:89-97` and the `+ Node` in the `:1` header comment are dead — remove them.
- `tests/Architecture.Tests/DependencyRuleTests.cs` -- six AD-1 rules, each asserted twice (assembly via NetArchTest, project file via `ProjectFile`). Copy the shape: `private static readonly Assembly XAssembly = typeof(XAssemblyMarker).Assembly`, `Types.InAssembly(...)...GetResult()`, then `AssertNoViolations(result, "<rule prose>")` at `:226`. `:156-167` `Rule4_no_src_project_references_api` calls `ProjectFile.LoadAllSrcProjects()` at `:160`, which enumerates **every** `*.csproj` under `src/` recursively — the new web project is picked up automatically and must not reference Api.
- `tests/Architecture.Tests/ProjectFile.cs:96-110` -- `RepositoryRoot`/`FindRepositoryRoot()` walk up from `AppContext.BaseDirectory` looking for `ActionLedger.sln`. Reuse this to locate `src/ActionLedger.Web` from a test; do not invent a second root-finder.
- `tests/Architecture.Tests/Architecture.Tests.csproj` -- `Microsoft.NET.Sdk`, `OutputType Exe`, `IsTestProject`, `xunit.v3` + `NetArchTest.eNhancedEdition`, and a `ProjectReference` per src ring. Add the web project here.
- `src/ActionLedger.Api/ApiAssemblyMarker.cs` -- the marker pattern each ring uses to anchor its assembly for NetArchTest. The web project needs its own.
- `Directory.Build.props` -- applies to every project: `net10.0`, `Nullable enable`, `ImplicitUsings enable`, **`TreatWarningsAsErrors true`**, `EnforceCodeStyleInBuild true`. The generated client compiles clean under all of it *only* with System.Text.Json (see Design Notes).
- `Directory.Packages.props:9-10` -- `ManagePackageVersionsCentrally` and `CentralPackageTransitivePinningEnabled` are both on, so every new package needs a `PackageVersion` and `PackageReference` elements carry no `Version`. Add a `Web (Story 1.5)` `ItemGroup` in the existing commented style.
- `ActionLedger.sln` -- classic format, solution folders `src` `{827E0CD3-…}` and `tests` `{0AB3BF05-…}`, `NestedProjects` maps each project into one. Use `dotnet sln add --solution-folder` so the nesting is written for you.
- `.github/workflows/ci.yml:41-49` -- `dotnet restore/build/test ActionLedger.sln`. **No step change is needed**; the two stale comments at `:4` ("Angular lint/test/build … join this workflow in Stories 1.5 and 1.7") and `:47` ("all seven test projects") are wrong once this lands.
- `tests/Api.Tests/Api.Tests.csproj`, `tests/Web.E2E/Web.E2E.csproj` -- the test-project shape to copy: `OutputType Exe`, `IsTestProject`, `xunit.v3`, no explicit runner wiring (`global.json` sets `"test": { "runner": "Microsoft.Testing.Platform" }`). There are no global `Xunit` usings — every test file writes `using Xunit;`.
- `_bmad-output/planning-artifacts/ux-designs/ux-ActionLedger-2026-09-20/DESIGN.md` -- front matter `colors:`, `typography:`, `spacing:` are the verbatim source for every token value. The exact set this story must emit is reproduced in Design Notes.
- `_bmad-output/planning-artifacts/architecture/architecture-ActionLedger-2026-09-20/ARCHITECTURE-SPINE.md:128` (AD-13), `:134` (AD-14), `:200-232` (Stack table) -- the rules and the pinned versions.

## Tasks & Acceptance

**Execution:**

- `Directory.Packages.props` -- add a `Web (Story 1.5)` group: `Microsoft.AspNetCore.Components.WebAssembly` 10.0.12, `Microsoft.AspNetCore.Components.WebAssembly.DevServer` 10.0.12, `MudBlazor` 9.10.0, `NSwag.MSBuild` 14.7.1, `bunit` 2.11.3 -- all MIT, all from the Stack table; comment why each is here, as the existing groups do.
- `src/ActionLedger.Web/ActionLedger.Web.csproj` -- `Microsoft.NET.Sdk.BlazorWebAssembly`, `RootNamespace` `ActionLedger.Web`, the five package references with `PrivateAssets="all"` on DevServer and NSwag.MSBuild, **no `ProjectReference`**, and a `BeforeTargets="BeforeCompile"` `Exec` running `$(NSwagExe_Net100) openapi2csclient` over `openapi.json` into `Core/Api/` with `Inputs`/`Outputs` so an unchanged contract skips it -- the exact switches are in Design Notes and were verified against the committed contract.
- `src/ActionLedger.Web/openapi.json` -- `git mv` from `web/actionledger-web/`, then remove the now-empty `web/` tree -- the contract moves with the project (AD-13, sprint change §Structural Seed).
- `src/ActionLedger.Api/OpenApi/OpenApiExport.cs` -- repoint `ContractRelativePath` to `src/ActionLedger.Web/openapi.json` and update the two doc comments -- one constant carries the export command and the snapshot test with it.
- `tests/Api.Tests/OpenApiSnapshotTest.cs` -- correct the class summary from "the Angular client" to the generated C# client -- the comment is the only thing that knows about Angular.
- `.gitignore` -- replace the Angular generated-client rule with `src/ActionLedger.Web/Core/Api/`, drop the `# Node / Angular` block and the `Node` mention in the header -- no Node toolchain remains anywhere.
- `src/ActionLedger.Web/WebAssemblyMarker.cs` -- the assembly anchor, mirroring `ApiAssemblyMarker` -- `Architecture.Tests` needs a type to reach the assembly by.
- `src/ActionLedger.Web/Program.cs` -- `WebAssemblyHostBuilder`, root components, `AddMudServices()`, and the API client registration -- the composition root Story 1.6 extends.
- `src/ActionLedger.Web/Core/Api/ApiClientRegistration.cs` -- the one place an `HttpClient` is constructed and handed to the generated client, with the base address read from configuration and defaulting to the host's own origin -- AD-14's seam; `Core/` is inside the allowlist.
- `src/ActionLedger.Web/Core/Theme/ActionLedgerTheme.cs` -- the app's own `MudTheme` instance -- one named place for the palette; see Design Notes for why it overrides nothing.
- `src/ActionLedger.Web/wwwroot/css/tokens.css` -- every DESIGN.md token as a CSS custom property, light in the default scope and dark under `prefers-color-scheme: dark`, plus the `confidence-score` and `source-excerpt` classes -- the design delta in its entirety.
- `src/ActionLedger.Web/wwwroot/index.html` -- load Roboto and Roboto Mono, MudBlazor's stylesheet and script, and `tokens.css` -- DESIGN.md requires Roboto Mono beside Roboto.
- `src/ActionLedger.Web/App.razor`, `Layout/MainLayout.razor`, `_Imports.razor` -- the router with a one-line `NotFound` fragment, MudBlazor's provider set, `MudThemeProvider` bound to `ActionLedgerTheme` and following the OS preference, and the 1280px content container -- the shell Story 1.6 fills in.
- `src/ActionLedger.Web/Features/{Auth,Meetings,Review,Actions,Audit}/Data/.gitkeep` -- the five feature folders AD-14 names, each with its `Data/` seam -- git does not track empty directories.
- `src/ActionLedger.Web/Features/Auth/Data/AuthService.cs` -- a thin wrapper over the generated client's login operation -- without one real consumer the generated client is dead code and the compile-error guard is vacuous; see Design Notes.
- `tests/Web.Tests/Web.Tests.csproj` -- `Microsoft.NET.Sdk.Razor`, `OutputType Exe`, `IsTestProject`, `xunit.v3` + `bunit`, `ProjectReference` to the web project -- bUnit tests need the Razor SDK.
- `tests/Web.Tests/ThemeAndTokenTests.cs` -- the token rows of the matrix, by parsing `tokens.css` and `index.html` from the web project located through the repository root -- DESIGN.md's hex values are the expected values.
- `tests/Web.Tests/LayoutTests.cs` -- bUnit renders the layout and asserts the MudBlazor providers are present, the theme is the app's own, and content sits in the 1280px gutter container -- the theme-wiring and content-width rows.
- `tests/Web.Tests/AuthServiceTests.cs` -- `AuthService` against a hand-written stub of the generated client interface -- proves the seam is usable with the client mocked (NFR8); there is no mocking package and the repo writes fakes by hand.
- `tests/Architecture.Tests/WebStructureTests.cs` -- AD-14: HTTP and the generated client confined to `Core/` and `Features/*/Data/`, a non-vacuity assertion, and the five feature folders on disk -- the build-enforced replacement for the ESLint rule the sprint change removed.
- `tests/Architecture.Tests/Architecture.Tests.csproj` -- add the web `ProjectReference` -- the rule needs the assembly.
- `ActionLedger.sln` -- add both projects into the `src` and `tests` solution folders -- `ci.yml` reaches them through the solution and needs no edit.
- `.github/workflows/ci.yml` -- correct the two stale comments about Angular steps and the test-project count -- no step changes.

**Acceptance Criteria:**

- Given a clean clone with no `Core/Api/` and a stock SDK 10.0.401 with no workloads installed, when I run `dotnet build ActionLedger.sln`, then it succeeds with zero warnings and the typed client has been generated.
- Given a build has just run, when I check version control, then nothing under `Core/Api/` is tracked or pending and the working tree is clean.
- Given `dotnet test ActionLedger.sln`, when it completes, then every project passes including `Web.Tests`, and no step anywhere in the build invokes Node, npm, or a workload install.
- Given each new guard, when its protected behaviour is mutated — an operation removed from the committed contract while a caller remains, a type outside the seam given an `HttpClient`, the non-vacuity assertion's subject removed, a feature folder deleted, a token's hex changed, Roboto Mono dropped from `index.html` — then a named test or the compiler fails, verified red and then reverted.
- Given the pull request, when CI runs, then `build and test`, `linked issue`, and CodeQL all pass.

## Spec Change Log

- **`tokens.css` redefines the same property names in the dark scope instead of emitting a
  parallel `-dark` set.** The spec's matrix row asked for "every family's `-dark` pair"; the
  implementation declares `--al-ai-provenance-container` once in `:root` and again inside
  `@media (prefers-color-scheme: dark)`. That is the same information and a better interface —
  a consumer writes `var(--al-ai-provenance-container)` once and is correct in both modes,
  instead of choosing a name and being wrong in one. Every value still comes verbatim from
  DESIGN.md; all 40 were re-checked against its front matter programmatically. Recorded rather
  than corrected, because the acceptance criterion is that the light and dark values exist and
  match DESIGN.md, and they do. **KEEP:** the single-name-two-scopes shape, and `TokenCss`'s
  brace-matching split of the file into the two scopes the tests assert over.
- **The vacuity guard had to be strengthened during verification.** As first written,
  `The_http_rule_has_something_to_rule_on` asserted that *some* type sits in the HTTP seam. The
  generated client lives in `Core/Api` and is built on `System.Net.Http`, so it satisfies that
  on its own and the guard could never fail — deleting every hand-written consumer left it
  green, which the mutation check caught. It now counts only types without
  `[GeneratedCode]`, so it goes red exactly when the last real consumer is removed. **KEEP:**
  the mutation check that found this; a vacuity guard that is itself vacuous is worse than none.


## Review Triage Log

### 2026-09-21 — Review pass
- verdicts: 8 findings — high 0, medium 1, low 3, false 2, maybe-false 2
- findings:
  - `[false]` `[reject]` `#blazor-error-ui` is emitted in `index.html` with no stylesheet defining it, so the "An unhandled error has occurred" bar would show on every page — refuted: the shipped `MudBlazor.min.css` styles `#blazor-error-ui` with `display:none` plus the full fixed error-bar treatment, so omitting the template's `app.css` is correct here rather than a gap.
  - `[false]` `[reject]` The DESIGN.md hex values may have drifted in transcription between the document, `tokens.css`, and the test's `TheoryData` — refuted: all 20 families across both modes were parsed out of DESIGN.md's front matter and compared to `tokens.css`, and the test's 40 declared pairs compared to `tokens.css` independently; every value matches exactly, as do both type roles and all three spacing values.
  - `[medium]` `[patch]` Nothing enforced "no `ProjectReference` from `ActionLedger.Web`", the invariant the whole story exists to create — `Rule4_no_src_project_references_api` only catches a reference to Api, so adding one to `ActionLedger.Application` to share the server's DTOs would compile and pass every test while removing the contract boundary. Fixed: `The_web_project_references_no_other_project` asserts `ProjectFile.Load("ActionLedger.Web").ProjectReferences` is empty; verified red against an injected Application reference.
  - `[low]` `[patch]` The "generated client is never committed" matrix row had no automated cover — only a manual `git status`. Removing the ignore rule breaks nothing that any test observes; the 50 KB generated file simply starts appearing in diffs. Fixed: `The_generated_client_folder_is_ignored_by_version_control` reads `.gitignore`; verified red with the rule removed.
  - `[low]` `[patch]` `Inputs="openapi.json"` alone meant a bump of the pinned NSwag version left the previously generated client in place until the contract happened to change. Fixed: the project file is an input too. This first regressed the "unchanged contract skips the generator" row — NSwag leaves the file untouched when the text is identical, so the output never became newer than the new input and the target reran on every build — and was then fixed properly with a `Touch` on the generated file. Re-verified: build 1 regenerates, builds 2 and 3 skip.
  - `[low]` `[reject]` `Features/Auth/Data/.gitkeep` is redundant now that `AuthService.cs` sits beside it — not worth removing: the `.gitkeep` is what keeps the folder present if `AuthService` is ever moved or renamed, and `The_feature_folder_exists_with_its_data_seam` would otherwise start failing for a reason that has nothing to do with AD-14.
  - `[maybe-false]` `[defer]` Fonts load from the Google Fonts CDN, so an offline demo loses Roboto and Roboto Mono — deferred at low: DESIGN.md sanctions the CDN link and the fallback chain ends in `ui-monospace`/`monospace`, so the confidence column still aligns; what would settle it is running the compose demo with the network off.
  - `[maybe-false]` `[defer]` The generated client is added to `Compile` by a target rather than by the item glob, so an IDE design-time build may not surface it to IntelliSense — deferred at medium-if-true; every command-line path (`build`, `test`, `publish`, clean-clone) was verified working, and what would settle it is opening the solution in Rider or Visual Studio from a clean clone.

**Note on how this pass ran.** The four review layers were launched together as the workflow requires and all four executed, but none of their results were delivered back to this session — the same subagent result-delivery failure that hit every subagent in this run, including the planning recon agents and the implementation agent. The findings above are therefore this session's own review of the staged diff, verified against the code rather than taken on report. That is a narrower pass than four independent layers, and it is recorded here rather than presented as one.


## Design Notes

**The generator switches are not negotiable, and one of them is load-bearing.** Verified against the committed contract on 2026-09-21 with NSwag 14.7.1 (NJsonSchema 11.6.1), which reads OpenAPI 3.1.1 without complaint:

```
$(NSwagExe_Net100) openapi2csclient /input:openapi.json /classname:ActionLedgerApiClient
  /namespace:ActionLedger.Web.Core.Api /output:Core/Api/ActionLedgerApiClient.g.cs
  /JsonLibrary:SystemTextJson /GenerateNullableReferenceTypes:true
  /GenerateClientInterfaces:true /UseBaseUrl:false
```

`/JsonLibrary:SystemTextJson` is the load-bearing one: NSwag defaults to Newtonsoft.Json, and the default output does not compile here — it emits `Newtonsoft.Json` attributes against a package the project does not have and should not take. With System.Text.Json the generated file compiles clean under `Nullable enable` and `TreatWarningsAsErrors` (it carries its own `#pragma` block). `/GenerateClientInterfaces:true` is what makes `AuthServiceTests` able to stub the client without a mocking package. `/UseBaseUrl:false` leaves addressing to the `HttpClient`, which is what Story 1.7's nginx upstream needs. Generation produces `SignInAsync`, `ListUsersAsync`, `GetHealthAsync`, `GetReadinessAsync` and a typed `PagedResultOfUserSummaryDto` from the committed `operationId`s.

**Why `AuthService` exists in a scaffold story.** The acceptance criterion "a contract change that has not been re-exported fails `dotnet build` with a compile error" is only true if something compiles against the generated client. With no consumer, the client is unreferenced code and the guard passes vacuously forever. One thin wrapper over the login operation makes the guard real — verified: removing `/api/v1/auth/login` from the committed contract turns the build red with `CS1061 … does not contain a definition for 'SignInAsync'`. It is also the `Features/<Feature>/Data/<Feature>Service.cs` exemplar AD-14 names, and Story 1.6 consumes it rather than replacing it. Everything else about signing in — the screen, `SessionState`, the delegating handler, the 401 redirect — stays in 1.6.

**The architecture rule, verified red and green.** `Types.InAssembly(WebAssembly).That().HaveDependencyOnAny("System.Net.Http", "ActionLedger.Web.Core.Api").Should().ResideInNamespaceMatching(<allowlist regex>)` passes on a compliant tree and fails naming the type when an offender is added outside the seam. Folder layout and namespace agree because `RootNamespace` is `ActionLedger.Web`, so `Core/Api` is `ActionLedger.Web.Core.Api` and `Features/Auth/Data` is `ActionLedger.Web.Features.Auth.Data`; the allowlist matches `ActionLedger.Web.Core` and `ActionLedger.Web.Features.<anything>.Data` and their descendants. **The vacuity trap is real and must be closed:** `That().HaveDependencyOnAny(...)` over a tree where nothing touches HTTP yields an empty set, and NetArchTest reports success. Assert separately that the matched set is non-empty, the way `AuthDisciplineTests` asserts its walk covered at least one operation.

**Dark mode keys off the OS, because MudBlazor 9.10.0 gives no class to key off.** `MudThemeProvider` swaps the values of its own `--mud-palette-*` properties when `IsDarkMode` flips; it adds no `dark` class to the document, so a token file cannot follow it by selector. `ObserveSystemDarkModeChange` makes the provider follow `prefers-color-scheme`, and putting the dark token pairs under the same media query puts both layers on one signal with no plumbing. If a later story adds an explicit toggle, it adds an attribute hook then — do not build one now.

**1280px and 24px come from MudBlazor, not from an override.** `MudContainer` with `MaxWidth.Large` and gutters emits `mud-container-maxwidth-lg` (max-width 1280px at ≥1280px) and `mud-container--gutters` (24px padding at ≥600px) — checked against the shipped `MudBlazor.min.css` for 9.10.0, and exactly DESIGN.md's `content-max` and `page-gutter`. So the container is stock MudBlazor and nothing is restyled. `tokens.css` still declares `--al-content-max`, `--al-page-gutter`, and `--al-notes-pane-min` because DESIGN.md lists them and the Review screen's two-pane layout will reference them; the token test pins the values so the two cannot silently diverge.

**The app has no route yet, and that is the correct interim state.** Story 1.6 owns every routable
component, so after this story the assembly contains no `@page`. The `Router` still ships, because
the router *is* part of the scaffold and `MainLayout` has nothing to host `@Body` for without it;
its `NotFound` fragment is one line of text that Story 1.6 replaces with the real Not found page.
Do not invent a placeholder home page to fill the gap — a page that exists only to be deleted next
story is worse than an empty router.

**`ActionLedgerTheme` deliberately overrides nothing.** DESIGN.md says the base palette is the MudTheme defaults and that the six container-tier values it pins are there only because MudBlazor has no container tier — they are the Material 3 azure-blue values, so the rendered result is unchanged. The whole design delta is therefore tokens and chips, not a palette. `ActionLedgerTheme` exists so there is one named seam to change when a later story needs one, and so `MudThemeProvider` is given an app-owned theme rather than an implicit default. A reviewer will ask why it looks empty; this paragraph is the answer.

**Tokens, verbatim from DESIGN.md.** Light value, then the `-dark` pair. The five families are the acceptance criterion; the three base pairs are what the brand layer references.

| Family | Base | Container | On-container | Base dark | Container dark | On-container dark |
|---|---|---|---|---|---|---|
| ai-provenance | `#5B3E96` | `#EADDFF` | `#2A0E5C` | `#D0BCFF` | `#4A2D82` | `#EADDFF` |
| human-provenance | `#1F5F8B` | `#D6EAF8` | `#0B2A40` | `#9CCAF0` | `#154466` | `#D6EAF8` |
| low-confidence | `#8A5000` | `#FFF1DC` | `#4A2A00` | `#FFB870` | `#4A2E00` | `#FFE2C2` |
| success | `#1B5E20` | `#E3F2E5` | `#0D3A11` | `#8BD48F` | `#1E4A22` | `#D9F2DB` |
| neutral-container | — | `#E8EAED` | `#3C4043` | — | `#3C4043` | `#E8EAED` |

Base container tier: `primary-container` `#D3E4FF` on `#001C38`, dark `#00497D` on `#D3E4FF`; `error-container` `#FFDAD6` on `#410002`, dark `#93000A` on `#FFDAD6`; `surface-container-low` `#F3F5F9` with `on-surface` `#1A1C1E`, dark `#1F2124` with `#E2E2E6`.

Type roles: `confidence-score` — Roboto Mono, 13px, weight 500, line-height 1.2, falling back to `ui-monospace`; `source-excerpt` — Roboto, 14px, weight 400, line-height 1.5. Spacing: `content-max` 1280px, `page-gutter` 24px, `notes-pane-min` 360px. `rounded` values are MudTheme `LayoutProperties` defaults and are inherited, not redeclared.

**Toolchain, and two things that would otherwise look like blockers.** SDK 10.0.401 at `~/.dotnet`; run `export PATH="$HOME/.dotnet:$PATH"` and `export DOTNET_ROOT="$HOME/.dotnet"` first. **No workload is needed:** `dotnet build` and `dotnet publish` both succeed on a machine with an empty `dotnet workload list`; publish prints a recommendation to install `wasm-tools`, but it is a message, not a warning, so `TreatWarningsAsErrors` does not trip on it. Do not install a workload to silence it — NFR10 says a clean clone builds with the pinned SDK and nothing else.

## Verification

**Commands:**

- `dotnet build ActionLedger.sln` -- expected: succeeds, **0 Warning(s), 0 Error(s)**.
- `rm -rf src/ActionLedger.Web/Core/Api && dotnet build ActionLedger.sln` -- expected: the generator reruns and the build succeeds; proves a clean clone needs nothing on disk.
- `dotnet build ActionLedger.sln` again, unchanged -- expected: the generation target is skipped as up to date.
- `git status --porcelain` after a build -- expected: empty; the generated client is ignored.
- `dotnet test ActionLedger.sln` -- expected: every project passes, `Web.Tests` among them, zero failures. Record the total and the delta against the 74 tests Story 1.3 recorded plus Story 1.4's additions.
- `dotnet build -c Release ActionLedger.sln && dotnet test -c Release --no-build ActionLedger.sln` -- expected: green; this is `ci.yml`'s exact shape.
- `dotnet publish src/ActionLedger.Web -c Release` -- expected: succeeds with no workload installed; confirms Story 1.7 has static assets to serve.
- `dotnet run --project src/ActionLedger.Api -- --export-openapi` with `Database__ConnectionString`, `Jwt__Key`, and `Jwt__Issuer` unset and no database reachable -- expected: prints `Wrote …/src/ActionLedger.Web/openapi.json`, exit 0; the Story 1.3 export trap must survive the move.
- `git diff --stat` on the contract after that export -- expected: no content change, only the path move.
- `grep -rniE "angular|npm|node_modules|typescript|ng-openapi-gen" src tests .github .gitignore Directory.Packages.props` -- expected: no hits.
- `test ! -d web && echo gone` -- expected: `gone`.

**Mutation checks — each verified red, then reverted:**

- Remove `/api/v1/auth/login` from the committed contract, leaving `AuthService` -- expected: `dotnet build` fails with `CS1061` naming `SignInAsync`.
- Add a type referencing `HttpClient` outside `Core/` and `Features/*/Data/` -- expected: the AD-14 rule fails naming that type.
- Remove the only in-seam HTTP consumer -- expected: the non-vacuity assertion fails rather than the rule passing empty.
- Delete one `Features/*/Data/` folder -- expected: the folder-shape test fails naming it.
- Change one token's hex in `tokens.css` -- expected: the token test fails naming the property and both values.
- Remove Roboto Mono from `index.html` -- expected: the font test fails.
- Point `ContractRelativePath` back at the old path -- expected: `OpenApiSnapshotTest` fails with a message naming the missing file.

**Not verified here — it runs at landing, with a human present:** `gh pr checks` for `ci.yml`, `require-linked-issue`, and CodeQL.

## Auto Run Result

Status: done

**Implemented change.** `src/ActionLedger.Web` now exists: a Blazor WebAssembly project on
MudBlazor 9.10.0 with the committed contract beside it, a typed C# client generated from that
contract by a pre-build NSwag target into a git-ignored folder, DESIGN.md's token delta as CSS
custom properties with light and dark values, and `tests/Web.Tests` on bUnit 2.11.3. AD-14's
"no HTTP in components" rule — which lost its ESLint enforcement in the 2026-09-21 sprint change
— is now a build-failing `Architecture.Tests` rule. The contract moved from the deleted
`web/actionledger-web/` tree to `src/ActionLedger.Web/openapi.json`; the re-export is
byte-identical to what was committed there.

**Files changed.**
- `src/ActionLedger.Web/ActionLedger.Web.csproj` — Blazor WASM project, five MIT packages, and the NSwag pre-build target with incremental inputs/outputs.
- `src/ActionLedger.Web/openapi.json` — the committed contract, moved (content unchanged).
- `src/ActionLedger.Web/Program.cs`, `App.razor`, `Layout/MainLayout.razor`, `_Imports.razor` — composition root, router, MudBlazor providers, theme, 1280px/24px content container.
- `src/ActionLedger.Web/Core/ApiClientRegistration.cs` — the only place an `HttpClient` is constructed.
- `src/ActionLedger.Web/Core/Theme/ActionLedgerTheme.cs` — the app's own `MudTheme`, overriding nothing by design.
- `src/ActionLedger.Web/wwwroot/css/tokens.css`, `wwwroot/index.html` — the design delta and the host page.
- `src/ActionLedger.Web/Features/{Auth,Meetings,Review,Actions,Audit}/Data/` — the five AD-14 feature folders; `Features/Auth/Data/AuthService.cs` is the seam exemplar and the generated client's one real consumer.
- `src/ActionLedger.Web/WebAssemblyMarker.cs` — assembly anchor for the architecture rule and the router.
- `src/ActionLedger.Api/OpenApi/OpenApiExport.cs` — `ContractRelativePath` repointed; the export command and the snapshot test follow it.
- `tests/Architecture.Tests/WebStructureTests.cs` — the AD-14 HTTP rule, its non-vacuity guard, the feature-folder shape, the no-ProjectReference rule, and the ignore-rule check.
- `tests/Architecture.Tests/ProjectFile.cs` — `RepositoryRoot` widened to `internal` so one root-finder serves the assembly.
- `tests/Web.Tests/` — `ThemeAndTokenTests`, `TokenCss`, `LayoutTests`, `AuthServiceTests`, `WebProject`, and the project file.
- `ActionLedger.sln`, `Directory.Packages.props`, `.gitignore` — both projects joined, five package versions pinned, the Angular client's ignore rule replaced and the Node block removed.
- `.github/workflows/ci.yml`, `codeql.yml`, `.github/dependabot.yml` — comment-only corrections; no step changes anywhere, and no Node step exists or is coming.
- `tests/Api.Tests/OpenApiSnapshotTest.cs` — comment corrected from "the Angular client".

**Review findings.** 8 findings: 3 patched (1 medium, 2 low), 2 deferred (both maybe-false), 3
rejected. Patched: the missing no-`ProjectReference` guard (medium); the missing ignore-rule
guard (low); the generator's incremental inputs missing the project file (low). Rejected with
reasons: the `#blazor-error-ui` styling gap (refuted — MudBlazor's own stylesheet supplies
`display:none`); DESIGN.md hex drift (refuted — all 40 token values, both type roles, and all
three spacing values re-checked against DESIGN.md's front matter and matching exactly); the
redundant `Features/Auth/Data/.gitkeep` (keeping it is what stops the folder-shape test failing
for an unrelated reason if `AuthService` moves). Deferred: CDN-loaded fonts on an offline demo
(low), and whether an IDE design-time build surfaces the generated client to IntelliSense
(medium, unverified).

**Follow-up review recommended: false.** One medium entry was patched and two low ones; the
first-pass threshold is a patched `high` or two or more patched `medium`s, and neither was met.
No specific unverified risk in the patched work can be named — each patch was verified red
against an injected violation and green after.

**Verification performed.** SDK 10.0.401 at `~/.dotnet`, `dotnet workload list` empty.
- `dotnet build ActionLedger.sln` after `rm -rf src/ActionLedger.Web/Core/Api` — Build succeeded, **0 Warning(s), 0 Error(s)**; the client was generated from nothing on disk.
- Second and third builds — `Skipping target "GenerateApiClient" because all output files are up-to-date`.
- `git status --porcelain` after a build — no `Core/Api` entry; the generated client is ignored.
- `dotnet test ActionLedger.sln` — **184 passed, 0 failed** across eight test projects (182 before the two review patches added their guards).
- `dotnet build -c Release` + `dotnet test -c Release --no-build` (ci.yml's exact shape) — 184 passed, 0 failed, 0 warnings.
- `dotnet publish src/ActionLedger.Web -c Release` — succeeded with no workload installed; the `wasm-tools` line is a message, not a warning, so `TreatWarningsAsErrors` does not trip.
- `--export-openapi` with `Database__ConnectionString`, `Jwt__Key`, and `Jwt__Issuer` unset and no database — `Wrote …/src/ActionLedger.Web/openapi.json`, exit 0. The Story 1.3 export trap survived the move.
- The re-exported contract diffed against `e4f99d4:web/actionledger-web/openapi.json` — byte-identical.
- `grep -rniE "angular|npm|node_modules|typescript|ng-openapi-gen"` over `src tests .github .gitignore Directory.Packages.props` — no hits. `web/` no longer exists.
- All 20 colour families × 2 modes, both type roles, and all three spacing values parsed out of DESIGN.md and compared to `tokens.css` — exact match; the tests' own 40 declared pairs compared to `tokens.css` independently — exact match.

**Every guard verified red, then reverted.**

| Mutation introduced | What failed |
|---|---|
| `/api/v1/auth/login` removed from the committed contract, `AuthService` left in place | `dotnet build` — *CS1061: 'IActionLedgerApiClient' does not contain a definition for 'SignInAsync'* |
| A type with an `HttpClient` added under `Shared/` | `Http_is_confined_to_core_and_the_per_feature_data_folders` — *Offending types: ActionLedger.Web.Shared.Offender* |
| Every hand-written in-seam consumer deleted | `The_http_rule_has_something_to_rule_on` — *Assert.NotEmpty() Failure: Collection was empty*. This is the mutation that exposed the original guard as vacuous; see the Spec Change Log. |
| `Features/Audit` deleted | `The_feature_folder_exists_with_its_data_seam` and `No_feature_folder_outside_the_five_ad14_names_exists` |
| `--al-ai-provenance-container` changed to `#EADDF0` | `The_light_scope_carries_the_design_document_value` — *is #EADDF0 in the light scope; DESIGN.md gives #EADDFF* |
| `Roboto+Mono` removed from `index.html` | `The_host_page_requests_the_font_family` |
| `ContractRelativePath` pointed back at the old path | `OpenApiSnapshotTest` — *web/actionledger-web/openapi.json is missing. Run: dotnet run …* |
| A `ProjectReference` to `ActionLedger.Application` added to the web project | `The_web_project_references_no_other_project` — *Assert.Empty() Failure: Collection was not empty* |
| `src/ActionLedger.Web/Core/Api/` removed from `.gitignore` | `The_generated_client_folder_is_ignored_by_version_control` |

**Residual risks.**
- The two deferred items above.
- `MudMainContent` reserves top padding for an app bar that does not exist until Story 1.6, so the page currently starts lower than it eventually will. Cosmetic and self-correcting.
- The app has no routable component, so it renders the router's one-line `NotFound`. Deliberate — Story 1.6 owns every page. Anyone opening the app before 1.6 lands sees "Not found."
- `Api:BaseAddress` is read from configuration but no `wwwroot/appsettings.json` exists, so it always falls back to the host origin. That is the compose and Azure case Story 1.7 sets up; the key is a hook, not dead code, but nothing exercises the configured branch yet.

**Not verified here — it runs at landing, with a human present:** `gh pr checks` for `ci.yml`,
`require-linked-issue`, and CodeQL.
