---
title: 'Story 2.4 — Extraction seam, output validation, and the Fake provider'
type: 'feature'
created: '2026-09-22'
baseline_revision: '1f3930093159c83c6a1c9c46f2aae0d147ba264a'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-2-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-2-3-fixture-catalog-and-prompt-v1-saturday-evening.md'
  - '{project-root}/_bmad-output/planning-artifacts/prds/prd-ActionLedger-2026-09-19/addendum.md'
warnings: ['oversized']
deferred:
  - summary: >-
      `Ai:CallTimeoutSeconds` accepts up to 600, so two provider calls can spend 1,200 seconds
      against the 180-second run ceiling NFR-1 states and the extractor's own comment claims to
      enforce.
    evidence: |-
      `src/ActionLedger.Api/Configuration/AiOptions.cs` carries `[Range(1, 600)]` on
      `CallTimeoutSeconds`, and `ChatClientActionExtractor` bounds each call by that value with no
      whole-run deadline. The `[Range]` predates this story, and at the shipped default of 90 two
      calls are 180 seconds exactly, so nothing is wrong today. It becomes reachable when Story 2.7
      wires a provider that can actually spend the budget. What would settle it: either narrow the
      option's range to what two calls may spend inside 180 seconds, or give the retry loop a
      whole-run deadline in addition to the per-call one.
    location: >-
      src/ActionLedger.Api/Configuration/AiOptions.cs
    severity: low
  - summary: >-
      Any `OperationCanceledException` the provider raises for its own reasons is persisted and
      shown to a human as a budget timeout, because the timeout token is scoped inside `CallAsync`.
    evidence: |-
      `ChatClientActionExtractor.ExtractAsync` catches `OperationCanceledException` unfiltered after
      the caller-cancellation case and writes "The provider did not answer within
      Ai:CallTimeoutSeconds". The `CancellationTokenSource` that would distinguish a budget expiry
      lives in `CallAsync` and is disposed before the catch runs. Unreachable today: the Fake throws
      nothing, and it is the only registered provider. It arrives with Story 2.7's HTTP clients,
      whose internal timeouts surface as `TaskCanceledException`. What would settle it: catch inside
      `CallAsync`, or surface the timeout token so the two causes are distinguishable.
    location: >-
      src/ActionLedger.Infrastructure/Ai/ChatClientActionExtractor.cs
    severity: low
  - summary: >-
      A provider exception's `Message` is interpolated verbatim into the persisted failure reason,
      which the class doc three lines above promises will name what went wrong rather than what the
      call carried.
    evidence: |-
      `ChatClientActionExtractor` builds the reason as
      `$"...: {exception.GetType().Name}: {exception.Message}"`. The extractor controls its own
      strings but not an SDK's, and HTTP client exceptions can carry a request URI or a response
      excerpt. Nothing reaches that string today because the Fake throws nothing. What would settle
      it: when Story 2.7 lands, decide whether to truncate or allowlist what is taken from an
      exception before it is persisted and rendered.
    location: >-
      src/ActionLedger.Infrastructure/Ai/ChatClientActionExtractor.cs
    severity: low
  - summary: >-
      `PromptCatalog`'s numeric version ordering is never exercised with more than one version, so
      replacing it with a string sort would leave every test green until a `v10` lands beside a `v9`.
    evidence: |-
      Only `prompts/extract-actions.v1.md` is embedded, so `Versions` is a one-element list and
      `Current == Versions[^1]` holds trivially. `PromptCatalog` reads this assembly's own manifest
      resources through the static `EmbeddedContent`, so closing this needs either a seam for the
      resource source or a second embedded prompt file. What would settle it: add the assertion when
      a second prompt revision exists, which is Story 7.1's territory.
    location: >-
      src/ActionLedger.Infrastructure/Ai/PromptCatalog.cs
    severity: low
---

<intent-contract>

## Intent

**Problem:** The repository has a fixture catalog and a versioned prompt (Story 2.3) and an `Ai`
configuration section that validates its own shape, but there is no code behind either. Nothing can
turn meeting notes into proposals, so Story 2.5 (persist a run), Story 2.6 (run it from the UI),
Story 2.7 (swap in a real provider) and Story 6.2 (the Evaluation Gate) are all blocked, and the
provider-swap claim the whole demo rests on is unexercised.

**Approach:** Build the seam once. `ActionLedger.Application/Ai` gets the provider-neutral contract —
request, result, strictly-deserialized output, the committed JSON schema, the validator, the one text
normalizer and the one excerpt verifier. `ActionLedger.Infrastructure/Ai` gets the single extractor
on `Microsoft.Extensions.AI.IChatClient`, the embedded prompt and fixture catalogs, the deterministic
`FakeChatClient`, and a keyed chat-client factory registry so adding a provider is one class plus one
registration line. No endpoint, no persistence, no Domain change — those are Stories 2.5 and 2.6.

## Boundaries & Constraints

**Always:**

- **`ExtractAsync` never throws for a provider or validation failure.** It returns
  `ExtractionResult.Succeeded(...)` or `ExtractionResult.Failed(reason, metrics)` (AD-11). A
  cancellation raised by the *caller's* token still propagates — that is an aborted request, not a
  failed run.
- **Validation is strict and layered, in this order.** (1) `System.Text.Json` deserialization into
  `ExtractionOutput` with required members and `JsonUnmappedMemberHandling.Disallow`. (2)
  `ExtractionOutputValidator` for lengths, ranges and date format. (3) *Only then* excerpt
  verification, which is a per-proposal filter and never fails the run (FR5).
- **One retry, then Failed.** An invalid response, a provider exception, or a per-call timeout is a
  failed attempt: call once more, and if that also fails return `Failed` with a reason naming what
  went wrong. At most two calls, so the run stays inside the 180-second ceiling (NFR-1). The per-call
  budget is `Ai:CallTimeoutSeconds` (90), handed in through settings, never re-read from config.
- **Both kept and dropped proposals travel in the result**, each drop paired with a warning carrying
  the dropped excerpt text, so the Evaluation Gate never re-filters (AD-11, FR5).
- **One normalizer, one verifier.** `TextNormalization.Normalize` and `ExcerptVerifier` are pure
  static code in namespace `ActionLedger.Application.Ai` and are the only implementations of either.
  Normalization is FR38's: lowercase, strip punctuation, collapse whitespace.
- **The fixture body rule is the README's, byte for byte.** The notes body is everything after the
  line containing the closing `---`, that line's terminating newline consumed, the remainder kept
  exactly — no trim, no re-wrap, no newline translation
  (`fixtures/extraction/README.md`, `FixtureCatalogTests.SplitFrontMatter`).
- **Infrastructure reads no configuration.** It takes an `AiSettings` record of already-validated
  values from the composition root, exactly as `SeedSettings` does
  (`src/ActionLedger.Infrastructure/Seed/SeedSettings.cs:19`).
- **Tokens are `int`, never null; zero for Fake** (FR6). So are `StartedAt` and `DurationMs`, which
  the extractor measures because it is the only code that sees both ends of the call.
- **Adding a provider is one factory class plus one DI registration** (FR7). The chat-client factory
  is resolved by the `Ai:Provider` string through keyed DI; the Fake's factory is the only one this
  story registers.
- **Startup fails loudly, before anything serves.** A configured `Ai:PromptVersion` absent from the
  embedded prompt catalog, or an `Ai:Provider` with no registered factory, fails the host at start
  with a message naming the version or the provider (AD-6, AD-16).

**Never:**

- Do not let Application see an AI SDK. `DependencyRuleTests` Rule 3 bans the package id and the
  namespace `Microsoft.Extensions.AI` and `OpenAI` from Domain and Application, at the project file
  *and* the assembly. `IActionExtractor` and every type it names are provider-neutral.
- Do not add the OpenAI SDK, `Microsoft.Extensions.AI.OpenAI`, or any Azure package. Real providers
  are Story 2.7, whose whole proof point is that its diff touches only `Infrastructure/Ai/Providers`,
  DI registration and configuration.
- Do not add a JSON-schema validation package. `JsonSchema.Net` is licence-banned
  (`Directory.Packages.props:3-7`); the validator is hand-rolled.
- Do not add an `Ai__*` configuration key. `ComposeTopologyTests.SpineConfigKeys` is a closed list
  (`tests/Architecture.Tests/ComposeTopologyTests.cs:45-64`) and a new key breaks it. Every key this
  story needs already exists in `appsettings.json` and `.env.example`.
- Do not add a file to `prompts/` or `fixtures/extraction/`, and do not edit one.
  `PromptFileTests` allows only `extract-actions.v<N>.md` there, and `FixtureCatalogTests` pins the
  catalog's every count. Story 2.3's content is contract.
- Do not name any new type `*Normaliz*` outside `ActionLedger.Application.Ai`
  (`DependencyRuleTests.cs:187-195`), and do not add a second normalizer to any test assembly.
- Do not touch `_bmad-output/implementation-artifacts/sprint-status.yaml`, `src/ActionLedger.Web/`,
  `src/ActionLedger.Web/openapi.json`, `src/ActionLedger.Domain/`, or any controller. This story adds
  no route: `GET /api/v1/ai/provider` is Story 2.6's.
- Do not persist anything. No migration, no `DbContext` change, no repository. Story 2.5 owns the run
  aggregate.
- Do not log the notes text or any secret (NFR-4). The `ExtractionRunCompleted` log event belongs to
  Story 2.5, which owns the completed run.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Known fixture notes, Fake | Notes byte-equal to a catalog case's body | `Succeeded` with exactly that case's expected actions, in file order, tokens 0, within 1 second | No error expected |
| Unknown notes, Fake | Free text with modal verbs | Up to five proposals, one per modal-verb sentence in order, excerpt = that sentence, owner = first capitalized name in it or `""`, due date `null`, confidence 0.85 for the first four and 0.55 for the fifth | No error expected |
| Unknown notes, no modal verb | Free text with no `will`/`should`/`needs to`/`must` | `Succeeded` with zero proposals and no warnings | No error expected |
| Valid response | Model returns schema-valid JSON, every excerpt present in the notes | `Succeeded`, all proposals kept, no warnings, one provider call | No error expected |
| Unverifiable excerpt | Schema-valid JSON, one excerpt absent from the notes after normalization | `Succeeded`; that one proposal is in `Dropped`, the rest kept, one warning carrying the dropped excerpt | Never fails the run |
| Invalid then valid | First response unmapped-member / missing member / out-of-range; second valid | `Succeeded` from the second response; tokens summed over both calls | Two calls, no throw |
| Invalid twice | Both responses invalid | `Failed` with a reason naming the validation failure, zero kept, zero dropped | Two calls, no throw |
| Provider throws | `IChatClient` throws on call one, succeeds on call two | `Succeeded` from the second call | The throw is a failed attempt, not an escape |
| Per-call timeout | A call exceeds `Ai:CallTimeoutSeconds` | That attempt fails; after two, `Failed` with a timeout reason | Run stays under 180 s |
| Caller cancels | The caller's `CancellationToken` is cancelled mid-call | `OperationCanceledException` propagates | Deliberately *not* converted to `Failed` |
| Unknown provider at startup | `Ai:Provider=LocalOpenAI` with no factory registered | Host fails to start, message names the provider | Startup, not first extraction |
| Missing prompt version at startup | `Ai:PromptVersion` names a version with no embedded file | Host fails to start, message names the version and lists what is embedded | Startup, not first extraction |

</intent-contract>

## Code Map

Read these before writing anything.

**The decisions this story implements (quote them in doc comments):**

- `_bmad-output/planning-artifacts/architecture/architecture-ActionLedger-2026-09-20/ARCHITECTURE-SPINE.md:120`
  (AD-11) — the whole seam in one paragraph: the two ports, the never-throws rule, the one extractor,
  factories keyed by `Ai:Provider`, `ChatOptions.ResponseFormat` from the committed schema with
  `strict = true` through `ChatOptions.AdditionalProperties`, the wire-schema subset claim, the
  `JsonSchemaExporter` parity test, the one normalizer and one verifier, and the Fake keyed by
  SHA-256 of the normalized notes.
- `…/ARCHITECTURE-SPINE.md:90` (AD-6) — the metadata set, and the embedding line to copy verbatim:
  `<EmbeddedResource Include="../../prompts/*.md" LogicalName="prompts/%(Filename)%(Extension)" />`.
  `Current` is `Ai:PromptVersion` when set, else the highest `N`, validated to exist at startup.
  `SchemaVersion` is the schema file's own top-level `version`.
- `…/ARCHITECTURE-SPINE.md:180` (AD-21) — `fixtures/extraction/` is embedded in
  `ActionLedger.Infrastructure` "the same way as prompts", and is the Fake's answer table keyed by
  SHA-256 of the normalized notes text.
- `_bmad-output/planning-artifacts/epics.md:431-453` — Story 2.4's three acceptance criteria verbatim.
- `_bmad-output/planning-artifacts/prds/prd-ActionLedger-2026-09-19/prd.md:87` — the Fake's unknown-notes
  heuristic, spelled out: one proposal per sentence containing `will`, `should`, `needs to` or `must`,
  up to five; that sentence as the excerpt; the first capitalized name in it as owner; no due date;
  0.85 for the first four and 0.55 for the fifth.
- `…/prd.md:154, :158` (FR-5) — over-length is a validation failure, never truncation; validation is
  all-or-nothing per response; excerpt verification is a separate post-validation filter using the
  FR-38 normalization (lowercase, strip punctuation, collapse whitespace).
- `_bmad-output/planning-artifacts/prds/prd-ActionLedger-2026-09-19/addendum.md:55-76` — **the
  authoritative schema**, as literal JSON. Copy it exactly; the only addition is the top-level
  `version` AD-6 requires. `suggestedOwner` is a plain `"string"`; only `suggestedDueDate` is
  `["string","null"]`.

**Where the new code goes, and the shapes it must copy:**

- `src/ActionLedger.Application/Abstractions/` — **every port in this repo lives here**, one file per
  interface, async methods ending `Async` with `CancellationToken cancellationToken = default` last
  (`IMeetingRepository.cs:10-21`, `IReadDb.cs:28-31`). A companion value type may share the port's
  file (`IAccessTokenIssuer.cs:14-23` declares `AccessToken` beside it). `IActionExtractor` and
  `IAiProviderInfo` belong here; the value types they name belong in `Application/Ai/`.
- `src/ActionLedger.Application/ApplicationRegistration.cs:17-31` — read it, then leave it alone.
  Everything this story adds to Application is either a port (registered by the ring that implements
  it) or static, so this file does not change. Its doc comment claims every story adds its handler
  here; there is no handler in this story.
- `src/ActionLedger.Application/Meetings/SaveMeetingNotesHandler.cs:24-49` and `MeetingDtos.cs:50` —
  the house style for the new types: file-scoped namespace, `sealed record` for values, positional
  records for outbound shapes, `IReadOnlyList<T>` for collections, explicit types over `var`,
  collection expressions, an XML `<summary>` on every public type naming the AD or FR it serves.
- `src/ActionLedger.Domain/Meetings/MeetingNotes.cs:80-81` — the repo's one SHA-256 idiom,
  `Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))`. It is `private`, so the
  catalog key repeats the idiom rather than calling it; `MeetingNotes.Sha256` is the hash of the
  **raw** text and is a different value from the catalog key, which hashes the **normalized** text.
- `src/ActionLedger.Infrastructure/InfrastructureRegistration.cs:16-21, :65-71` — the registration
  pattern to copy exactly: an extension method taking `Func<IServiceProvider, TSettings>` rather than
  a value, because the Api's `ValidateOnStart` options are only resolvable once the provider is built.
  `AddActionLedgerSeeding` is the template, hosted service and all.
- `src/ActionLedger.Infrastructure/Seed/SeedSettings.cs:19` — `public sealed record SeedSettings(bool
  Enabled, string DefaultPassword);` and its doc comment stating why Infrastructure reads no
  configuration. `AiSettings` is its sibling.
- `src/ActionLedger.Infrastructure/Seed/DemoDataSeeder.cs:31-34, :69-78` — a hosted service taking its
  settings record by primary constructor and re-asserting its invariant with `InvalidOperationException`
  rather than defaulting. The startup check copies this.
- `src/ActionLedger.Infrastructure/InfrastructureAssemblyMarker.cs` — the assembly anchor for
  `GetManifestResourceStream`. **There is no embedded resource and no manifest-resource reader
  anywhere in the repository yet**; this story introduces both.
- `src/ActionLedger.Api/Program.cs:35-51` — the insertion point. `AddApiOptions` (`:35`) is already
  `Bind` + `ValidateDataAnnotations` + `ValidateOnStart`. Put `AddActionLedgerAi` between
  `AddActionLedgerPersistence` (`:41-42`) and `AddActionLedgerSeeding` (`:46-51`) so the AI startup
  check runs before the seeder — hosted services start in registration order, and `:44-45` is the
  precedent for documenting that order in a comment.
- `src/ActionLedger.Api/Configuration/AiOptions.cs` — **already written and already validated.**
  `SectionName`, `FakeProvider`/`LocalOpenAIProvider`/`AzureOpenAIProvider` constants, `Provider`
  regex-checked against the three names, `PromptVersion` required, `CallTimeoutSeconds` 1-600,
  `LowConfidenceThreshold` 0-1, and the two sub-sections. Its `<remarks>` (`:10-15`) names this story
  as the one that adds the AI ring. Project `IOptions<AiOptions>` into `AiSettings`; do not extend it.
- `src/ActionLedger.Api/Configuration/ApiOptionsRegistration.cs:45-79` — `AiOptionsValidator` already
  requires the LocalOpenAI and AzureOpenAI sub-sections when those providers are selected, and
  requires nothing for Fake. Nothing to add.

**Content this story consumes (read-only — Story 2.3 owns every byte):**

- `prompts/extract-actions.v1.md` — the system prompt. Its `## Input` section already declares the
  contract for the user message: "You are given two values: `meetingDate` … `notes`".
- `fixtures/extraction/*.md` + `*.expected.json` + `roster.json` + `README.md` — 14 cases, 31 expected
  actions. `README.md` documents the front-matter keys, the body rule, and the excerpt rule; it must
  be **excluded** from the embedded glob or the loader will try to parse it as a case.
- `.gitattributes:6-7` — `fixtures/**` and `prompts/**` are pinned `text eol=lf` precisely so this
  story's hashing and substring search are stable on a Windows checkout. Nothing to change.

**Read-only evidence — rules the new code can trip:**

- `tests/Architecture.Tests/DependencyRuleTests.cs:44-49` — Application's package allowlist is
  `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`,
  `System.Text.Json`. `System.Text.Json` and `JsonSchemaExporter` ship in the `net10.0` shared
  framework, so **no new `PackageReference` on Application** — adding one would also need a
  `PackageVersion` and is unnecessary.
- `…/DependencyRuleTests.cs:52-59, :122-148` — the banned prefixes, checked both as package ids and as
  namespaces, against Domain and Application.
- `…/DependencyRuleTests.cs:187-195` — Rule 5, the `Normaliz` name rule. `HaveNameMatching("Normaliz")`
  is a regex over the type name across all four assemblies; `ResideInNamespace` is a prefix match on
  `ActionLedger.Application.Ai`.
- `tests/Architecture.Tests/ComposeTopologyTests.cs:45-64` — `SpineConfigKeys`, the closed key list.
  `:105-108` — `Ai__Provider` must stay `Fake` in `.env.example`.
- `tests/Architecture.Tests/PromptFileTests.cs:70-82, :150-165` — the prompt filename must agree with
  `.env.example`'s `Ai__PromptVersion`, and `prompts/` may hold nothing but `extract-actions.v<N>.md`.
  Its class doc (`:12-14`) predicts this story's startup failure message.
- `tests/Architecture.Tests/FixtureCatalogTests.cs:1035-1052` — `SplitFrontMatter`, the body rule the
  production loader must reproduce exactly: `raw[match.Length..]` against
  `@"\A---\r?\n(?<front>.*?)^---[ \t]*\r?\n"` with `Singleline | Multiline`. `:1054-1084` — `Scalar`,
  `Sequence`, `Unquote` for front matter. `:1164-1230` — `roster.json` is read as
  `people[] { displayName, role, aliases }` into an `OrdinalIgnoreCase` set. `:1` — precedent for a
  test assembly taking a production dependency rather than retyping a constant.
- `tests/Architecture.Tests/ProjectFile.cs:96-114` — `RepositoryRoot`, but it is `internal`: only
  `Architecture.Tests` can use it. `Application.Tests` and `Infrastructure.Tests` must assert against
  embedded resources, not disk paths.
- `tests/Infrastructure.Tests/InfrastructureRingTests.cs` — the no-Docker test shape: a plain
  `public sealed class XTests` with no `[Collection]`, flat in the project folder (**this project has
  no subfolders**). `ActionLedger.Infrastructure.csproj:21-23` already has
  `<InternalsVisibleTo Include="Infrastructure.Tests" />`, so new Ai types may be `internal`.
- `tests/Application.Tests/Meetings/MeetingsTests.cs:298-376` — hand-written `private sealed class
  Fake…` nested under a `// --- Fakes ---` banner. **No mocking library exists in this repo.**
  `tests/Application.Tests/Auth/SignInHandlerTests.cs:112-124` — the representative test shape;
  async tests always pass `TestContext.Current.CancellationToken`.
- `Directory.Build.props:5-13` — `TreatWarningsAsErrors=true`. Any warning fails the build; nullable
  warnings are the usual cause in new code. `GenerateDocumentationFile=false`, so doc comments are
  convention, not a gate — but the convention is dense and load-bearing here.
- `Directory.Packages.props:3-7` — the licence gate and the banned list. `Microsoft.Extensions.AI` is
  MIT; add it in a new labelled `ItemGroup` matching the file's house style.

**Tooling (verified on this machine at `1f39300`):**

- `dotnet` 10.0.401 on `PATH` at `~/.dotnet/dotnet`, matching `global.json`.
- `nuget.org` is reachable and `Microsoft.Extensions.AI` `10.10.0` exists there; the local package
  cache does **not** hold it, so the first restore needs the network.
- Baseline at `1f39300`: `dotnet test ActionLedger.sln` → **765 passed, 0 failed, 0 skipped**, eight
  assemblies. That is the number to beat.

## Tasks & Acceptance

**Execution:**

- `Directory.Packages.props` -- add a new labelled `ItemGroup` with
  `<PackageVersion Include="Microsoft.Extensions.AI" Version="10.10.0" />` and a comment naming the
  story and the MIT licence -- the spine's Stack table pins this version, and NFR9 requires the
  licence note. Add nothing else: the OpenAI SDK is Story 2.7's.
- `src/ActionLedger.Application/Ai/extract-actions.schema.json` -- commit the authoritative schema
  from `addendum.md:55-76` byte-for-byte, with one addition: a top-level `"version": "1"` member,
  first in the document -- AD-6 makes this member the `SchemaVersion` every run records.
- `src/ActionLedger.Application/ActionLedger.Application.csproj` -- add an `<ItemGroup>` with
  `<EmbeddedResource Include="Ai/extract-actions.schema.json" />` and a comment saying why (one copy
  of the schema, read from the assembly by the extractor, no path configuration and no copy step) --
  add no `PackageReference`: the allowlist and Rule 3 both forbid it and nothing here needs one.
- `src/ActionLedger.Application/Ai/ExtractionSchema.cs` -- expose the committed schema to the ring
  above: `Version` (the top-level `version` member) and `WireJson` (the same document with that member
  removed, which is what a provider's structured-output request is built from) -- `version` is not a
  JSON Schema keyword, and a strict-mode server may reject an unknown top-level member. Read the
  resource once, statically.
- `src/ActionLedger.Application/Ai/ExtractionOutput.cs` -- the strict wire shape: `ExtractionOutput`
  with a required `Actions` collection and `ExtractedAction` with all five members required, both
  carrying `[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]` -- `SuggestedDueDate`
  is `string?`, **not** `DateOnly?`, so the validator and not the deserializer decides what a
  malformed date means, and so the exported schema types it `["string","null"]` as the committed file
  does. Expose the single `JsonSerializerOptions` the seam deserializes with (camelCase, disallow
  unmapped, no trailing commas, no comments) as a static on this type — there is no shared options
  object anywhere in Application, and a second one would be a second contract.
- `src/ActionLedger.Application/Ai/ExtractionOutputValidator.cs` -- hand-rolled, static, returns the
  first failure as a reason string or null -- `description` 1-500, `suggestedOwner` 0-100 and never
  null, `suggestedDueDate` null or exactly `YYYY-MM-DD` (`DateOnly.TryParseExact`, invariant),
  `confidence` within `[0, 1]` inclusive, `sourceExcerpt` 1-1000. Over-length is a failure, never a
  truncation (FR-5). The reason names the member and the index of the offending action.
- `src/ActionLedger.Application/Ai/TextNormalization.cs` -- FR38's normalization, and the only one in
  the solution: lowercase invariant, drop every `char.IsPunctuation` or `char.IsSymbol`, collapse every
  run of `char.IsWhiteSpace` to a single space, trim -- Rule 5 requires namespace
  `ActionLedger.Application.Ai`. Document the order, because stripping before collapsing is what keeps
  `"P. Ram"` two tokens.
- `src/ActionLedger.Application/Ai/ExcerptVerifier.cs` -- `IsSubstring(excerpt, notes)`: normalize both
  through `TextNormalization` and test ordinal `Contains`; an excerpt that normalizes to empty is not
  verified -- this is the single implementation AD-11 demands, used by the extractor now and by the
  Evaluation Gate in Story 6.2.
- `src/ActionLedger.Application/Ai/ExtractionRequest.cs` -- `sealed record ExtractionRequest(string
  Notes, DateOnly MeetingDate)` -- the prompt's `## Input` names exactly these two values, and PRD
  FR-4 says no other Meeting field is sent.
- `src/ActionLedger.Application/Ai/ExtractionResult.cs` -- the result and its parts: `ExtractionMetrics`
  (provider, model, prompt version, schema version, `StartedAt`, `DurationMs`, `InputTokens`,
  `OutputTokens` — all non-null, `int` tokens), `ExtractedProposal` (the five validated members),
  `DroppedProposal` (the proposal plus the warning text), and `ExtractionResult` with static
  `Succeeded(kept, dropped, metrics, warnings)` and `Failed(reason, metrics)` -- AD-11 names the two
  factories; carrying kept and dropped together is what stops Story 6.2 re-filtering.
- `src/ActionLedger.Application/Abstractions/IActionExtractor.cs` -- the port:
  `Task<ExtractionResult> ExtractAsync(ExtractionRequest request, CancellationToken cancellationToken
  = default)` -- every port in this repo lives in `Abstractions/`; the doc comment states the
  never-throws rule so no implementer has to rediscover it.
- `src/ActionLedger.Application/Abstractions/IAiProviderInfo.cs` -- `Provider` and `Model`, read-only --
  AD-11 assigns it to Application; Story 2.6 serves it from `GET /api/v1/ai/provider`.
- `src/ActionLedger.Infrastructure/ActionLedger.Infrastructure.csproj` -- add
  `<PackageReference Include="Microsoft.Extensions.AI" />` and two `<EmbeddedResource>` items: the
  prompts glob exactly as AD-6 writes it, and `../../fixtures/extraction/*` with
  `LogicalName="fixtures/extraction/%(Filename)%(Extension)"` and `Exclude` on `README.md` --
  AD-21 says the catalog embeds "the same way as prompts"; README is documentation, not a case, and
  the loader must never see it.
- `src/ActionLedger.Infrastructure/Ai/AiSettings.cs` -- `sealed record AiSettings(string Provider,
  string? PromptVersion, int CallTimeoutSeconds)` with a doc comment copying `SeedSettings`' reasoning
  -- `PromptVersion` is nullable because AD-6 says `Current` falls back to the highest `N` when it is
  not set, even though `AiOptions` currently requires it.
- `src/ActionLedger.Infrastructure/Ai/EmbeddedContent.cs` -- one internal static reader over
  `typeof(InfrastructureAssemblyMarker).Assembly`: read a resource by logical name as UTF-8, and list
  the names under a prefix -- two consumers, one reader, so a naming mistake surfaces in one place.
- `src/ActionLedger.Infrastructure/Ai/IPromptCatalog.cs` + `PromptCatalog.cs` -- `Get(version)`,
  `Current`, and the available versions, over `prompts/extract-actions.v<N>.md` resource names --
  AD-6 names these members. `Current` is the configured version when set, else the highest `N` by
  numeric order; `Get` on a missing version throws with a message naming the version and listing what
  is embedded. The interface lives beside the class in Infrastructure, not in Application: no
  Application code consumes it, and Rule 2's allowlist makes an unused Application port dead weight.
- `src/ActionLedger.Infrastructure/Ai/FixtureCatalog.cs` -- load the embedded catalog once: for each
  `<stem>.md`, split front matter with the README's rule, keep the body byte-for-byte, pair it with
  `<stem>.expected.json`, and index by `Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8
  .GetBytes(TextNormalization.Normalize(body))))` -- AD-21 keys the Fake by the hash of the
  **normalized** notes, which is what lets a pasted copy with different wrapping still match. Expose
  the raw expected JSON per case, not a re-serialized object, so the Fake returns the committed bytes.
- `src/ActionLedger.Infrastructure/Ai/FakeChatClient.cs` -- an `IChatClient` that reads the last user
  message, takes `notes` out of it, and answers from the catalog by normalized hash; on a miss applies
  the `prd.md:87` heuristic -- zero-usage token counts, no I/O, no delay. Split the notes into
  sentences, keep those containing `will`, `should`, `needs to` or `must` (whole word, case-insensitive),
  take the first five in order, excerpt = the sentence verbatim, owner = the first capitalized word
  sequence in it that is not the sentence's first word, else `""`, due date `null`, confidence 0.85
  for the first four and 0.55 for the fifth.
- `src/ActionLedger.Infrastructure/Ai/Providers/IChatClientFactory.cs` +
  `Providers/FakeChatClientFactory.cs` -- the factory seam and its only implementation for this story
  -- FR7's proof point is that Story 2.7 adds two files to this folder and two registration lines,
  and touches nothing else.
- `src/ActionLedger.Infrastructure/Ai/ChatClientActionExtractor.cs` -- the single `IActionExtractor`:
  build `ChatOptions` with `ResponseFormat` from `ExtractionSchema.WireJson` and strict mode through
  `AdditionalProperties` (AD-11), send the `PromptCatalog` text as the system message and a JSON object
  `{"meetingDate": "...", "notes": "..."}` as the user message (the prompt's own `## Input` contract),
  time the call against `Ai:CallTimeoutSeconds` with a linked token, deserialize strictly, validate,
  then verify excerpts -- retry once on any failed attempt, accumulate tokens across attempts, and
  return `Failed` rather than throwing. Distinguish the caller's cancellation from the per-call
  timeout and let the caller's cancellation propagate. **Confirm the exact strict-mode key against the
  installed `Microsoft.Extensions.AI` public surface before writing it**; only the Fake exercises this
  path until Story 2.7, so a wrong key would otherwise surface three stories away.
- `src/ActionLedger.Infrastructure/Ai/AiProviderInfo.cs` -- `IAiProviderInfo` over `AiSettings` and the
  active factory: `Provider` is the configured name, `Model` is what the factory reports (`"fixture-catalog"`
  for the Fake) -- Run Detail renders both, so the Fake needs a real, stable model string.
- `src/ActionLedger.Infrastructure/Ai/AiStartupCheck.cs` -- an `IHostedService` that resolves
  `PromptCatalog.Current` and the factory for the configured provider, and throws with a message
  naming the missing version or the unregistered provider -- AD-16's fail-fast, in the shape
  `DemoDataSeeder` already uses. It never touches the network; the reachability probe is Story 2.7's.
- `src/ActionLedger.Infrastructure/InfrastructureRegistration.cs` -- add
  `AddActionLedgerAi(this IServiceCollection services, Func<IServiceProvider, AiSettings> settings)`
  registering the settings, the prompt and fixture catalogs, the keyed Fake factory, the resolved
  `IChatClient`, `IActionExtractor`, `IAiProviderInfo`, and the startup check -- copy
  `AddActionLedgerSeeding`'s accessor pattern and document, as it does, why the order matters.
- `src/ActionLedger.Api/Program.cs` -- call `AddActionLedgerAi` between the persistence and seeding
  registrations, projecting `IOptions<AiOptions>` into `AiSettings` -- a comment naming AD-11 and AD-16
  and saying the AI check is registered before the seeder so a bad provider fails before any data is
  written, matching the existing comment at `:44-45`.
- `tests/Application.Tests/Ai/ExtractionContractTests.cs` -- cover the Application half and the I/O
  matrix rows it owns: strict deserialization (missing member, unmapped member, `null` owner, wrong
  type), every validator boundary on both sides, `TextNormalization`'s exact rule, `ExcerptVerifier`
  against a hard-wrapped body, and the `JsonSchemaExporter` parity assertion -- mirror the production
  folder, hand-written fakes only, `TestContext.Current.CancellationToken` on every async test.
- `tests/Infrastructure.Tests/PromptCatalogTests.cs` -- resolution and failure: `Current` honours the
  configured version, falls back to the highest `N` when unset, and `Get` on an unknown version throws
  a message naming it -- flat in the project, no `[Collection]`, no Docker.
- `tests/Infrastructure.Tests/FakeProviderTests.cs` -- the catalog and the Fake: all 14 cases load,
  each answers with its own committed expected JSON, every expected excerpt passes the production
  `ExcerptVerifier` against the production `FixtureCatalog` body, and the unknown-notes heuristic
  produces the PRD's shape -- this is the test that would have caught a loader that split front matter
  differently from the answer files (deferred entry DW-15).
- `tests/Infrastructure.Tests/ChatClientActionExtractorTests.cs` -- the retry and filter matrix against
  a hand-written scripted `IChatClient`: valid, invalid-then-valid, invalid-twice, throw-then-valid,
  timeout, unverifiable excerpt, and caller cancellation -- the epic asks for this in
  `Application.Tests`, but `Application.Tests` references only Application and AD-18 places a test with
  its ring; see Design Notes.
- `tests/Architecture.Tests/AiSeamTests.cs` -- pin the seam's shape: exactly one type in the
  Infrastructure assembly implements `IActionExtractor`; every file in `prompts/` and every catalog
  file except `README.md` is embedded under its AD-6/AD-21 logical name with bytes identical to the
  file on disk; `README.md` is **not** embedded; `extract-actions.schema.json` is embedded in the
  Application assembly and identical to the file on disk; and the schema's `version` matches what
  `ExtractionSchema.Version` reports -- a silently dropped resource is otherwise invisible until a
  container runs.

**Acceptance Criteria:**

- Given `ExtractionRequest`, `ExtractionResult`, `ExtractionOutput`, `extract-actions.schema.json`,
  `ExtractionOutputValidator`, `TextNormalization` and `ExcerptVerifier`, when the Application assembly
  is inspected, then all of them reside in namespace `ActionLedger.Application.Ai`, the two ports
  reside in `ActionLedger.Application.Abstractions`, and `DependencyRuleTests` Rules 2, 3 and 5 stay
  green — Application declares no new package and depends on no AI namespace.
- Given a schema-valid response whose every `sourceExcerpt` appears in the notes, when it is
  extracted, then the result is `Succeeded`, every proposal is kept in the order the model returned
  them, `Dropped` and `Warnings` are empty, and the provider was called exactly once.
- Given a schema-valid response with one `sourceExcerpt` that is absent from the notes after
  normalization, when it is extracted, then the run still succeeds, that one proposal appears in
  `Dropped` and not in `Kept`, and exactly one warning carries the dropped excerpt's text verbatim.
- Given a response that fails strict deserialization or the validator, when it is extracted, then the
  provider is called a second time; a valid second response succeeds, and a second failure returns
  `ExtractionResult.Failed` with a reason naming the failure, zero kept and zero dropped, and nothing
  throws.
- Given a provider that throws or exceeds `Ai:CallTimeoutSeconds` on the first call, when it is
  extracted, then that attempt counts as failed, at most one more call is made, and the result is
  `Failed` with a reason rather than a propagated exception — while a cancellation of the **caller's**
  token propagates as `OperationCanceledException`.
- Given `ExtractionOutput`, when `JsonSchemaExporter` exports it, then the exported property names,
  `required` set and types match the committed `extract-actions.schema.json` — in particular
  `suggestedOwner` is `"string"` and `suggestedDueDate` is `["string","null"]`.
- Given `ExtractionMetrics` on any completed run, when it is read, then `Provider`, `Model`,
  `PromptVersion`, `SchemaVersion`, `StartedAt`, `DurationMs`, `InputTokens` and `OutputTokens` are all
  populated and non-null, `SchemaVersion` equals the schema file's top-level `version`, and both token
  counts are `0` for the Fake provider.
- Given notes byte-equal to any of the 14 catalog cases' bodies, when the Fake provider extracts them,
  then the result is that case's committed `expected.json` — same count, same order, same five members
  per action — every proposal is kept with no warnings, and the call completes within one second.
- Given notes matching no case, when the Fake provider extracts them, then it returns one proposal per
  sentence containing `will`, `should`, `needs to` or `must`, at most five, in sentence order, each
  quoting its sentence verbatim as `sourceExcerpt` with `suggestedDueDate` `null`, and confidences
  `0.85` for the first four and `0.55` for the fifth.
- Given every one of the 31 expected actions in the catalog, when its `sourceExcerpt` is checked with
  the production `ExcerptVerifier` against the production `FixtureCatalog`'s body for that case, then
  every one verifies — the loader and the answer files agree on where the notes body begins.
- Given `prompts/extract-actions.v1.md` and the 30 catalog files, when the built Infrastructure
  assembly's manifest resources are listed, then each appears exactly once as
  `prompts/extract-actions.v1.md` or `fixtures/extraction/<name>`, each resource's bytes equal the
  file on disk, and `fixtures/extraction/README.md` is absent.
- Given `Ai:PromptVersion` set to a version with no embedded prompt, or `Ai:Provider` set to a name
  with no registered factory, when the host starts, then it fails with a message naming the missing
  version or the unregistered provider, and it does not serve a request.
- Given `git status --porcelain`, when the work is complete, then nothing under
  `src/ActionLedger.Web/`, `src/ActionLedger.Domain/`, `prompts/`, `fixtures/`, or
  `_bmad-output/implementation-artifacts/sprint-status.yaml` has changed, and `.env.example` and the
  `Ai` section of `appsettings.json` are byte-identical to `1f39300`.
- Given `dotnet build ActionLedger.sln -c Release` and `dotnet test ActionLedger.sln`, when they run,
  then the build is 0 warnings and 0 errors and every one of the eight assemblies is green with 0
  skipped and a total above the 765 recorded at `1f39300`.

## Spec Change Log

### 2026-09-22 — two mutation checks were not falsifiable as written

- **Trigger:** running the `## Verification` mutation checks after implementation. Two of the ten
  named a test that stays green under the mutation they describe.
- **Amended:** the fixture-body check now names the body-is-byte-for-byte test, and the prompt check
  now removes the `<EmbeddedResource>` item rather than editing the file's bytes. Both amended
  mutations were run and both go red. No acceptance criterion and nothing inside
  `<intent-contract>` changed.
- **Known-bad state avoided:** a mutation check that cannot fail reads as coverage and is not. The
  prompt one in particular would have certified the embedded-bytes assertion on a rebuild that
  re-embeds the very edit it was meant to detect, leaving a dropped `<EmbeddedResource>` — the
  failure that only shows up when the api image runs — looking already proven.
- **KEEP:** the eight other mutations all went red on a named test and are recorded in the Auto Run
  Result. Keep the byte-comparison half of `AiSeamTests`: the README mutation proves the
  presence-and-naming half is live, and the comparison is what makes a future transformed or
  mis-linked resource visible.

## Review Triage Log

### 2026-09-22 — Review pass
- verdicts: 44 findings — high 2, medium 11, low 24, false 7, maybe-false 0
- findings:
  - `[medium]` `[patch]` The schema's length and range bounds live in three copies and the parity test compares only names, required and type — patched: the seven bounds are now `public const` on `ExtractionOutputValidator` and nowhere else, `FakeChatClient` reads them, and `ExtractionContractTests` compares each keyword against `ExtractionSchema.CommittedJsonText` in both directions. `maxLength: 500` → `800` in the committed file now reddens.
  - `[false]` `[reject]` The committed schema permits additional properties while the deserializer forbids them — refuted: the file is byte-identical to the published schema at `addendum.md:55-76`, which the spec requires copied exactly, and AD-11 assigns `additionalProperties: false` to the adapter's pre-send transform, which is what makes the wire schema a strict subset of the committed file.
  - `[low]` `[reject]` Nothing caps the number of actions a response may contain — notes are capped at 50,000 characters upstream and a provider's own output limit bounds the array; no unbounded response was demonstrated, and a count ceiling adds a branch and a new failure mode for a state nothing reaches.
  - `[low]` `[defer]` `Ai:CallTimeoutSeconds` accepts up to 600, so two calls can spend 1,200 seconds against NFR-1's 180-second ceiling — the `[Range(1, 600)]` predates this story in `AiOptions`, and at the shipped default of 90 the ceiling holds exactly. Deferred: it becomes real when Story 2.7 wires a provider that can actually spend the budget.
  - `[low]` `[defer]` Any `OperationCanceledException` is reported as a budget timeout because the timeout token is scoped inside `CallAsync` — no SDK on the current path throws one spontaneously (the Fake throws nothing), so the mislabel is unreachable today. Deferred: it arrives with Story 2.7's HTTP clients, whose internal timeouts surface as `TaskCanceledException`.
  - `[low]` `[reject]` `response` is declared nullable and then dereferenced — `IChatClient.GetResponseAsync` returns a non-nullable `Task<ChatResponse>`, the compiler's flow analysis proves the variable non-null at the dereference (no CS8602 under `TreatWarningsAsErrors`), and no implementation returning null was demonstrated.
  - `[low]` `[defer]` A provider exception's `Message` is interpolated verbatim into the persisted failure reason, which could carry a URI or response excerpt — the Fake throws nothing, so nothing reaches that string today. Deferred with the Azure and LM Studio clients it would apply to.
  - `[low]` `[reject]` `Warnings` duplicates `Dropped` and the factory accepts them independently — the `Succeeded(kept, dropped, metrics, warnings)` signature is the spec's own Execution bullet, `Verify` is the only construction path and derives both collections in one loop, and the desync is hypothetical.
  - `[low]` `[reject]` `ExtractionResult` and friends are records whose equality compares list fields by reference — real, but no caller or test relies on whole-result equality (`The_fake_is_deterministic` asserts on `Kept`), and the fix means converting the types or hand-writing equality, which is more than a direct correction.
  - `[false]` `[reject]` `ExtractionResult.Succeeded`'s parameter order does not match the private constructor it forwards to — refuted: the forwarding call is positionally correct, and `metrics` and `warnings` are `ExtractionMetrics` and `IReadOnlyList<string>`, so the transposition the finding warns about is a compile error, not a silent bug.
  - `[low]` `[patch]` The heuristic owner returns an article phrase as a person — verified: `"The IT team must patch the server."` yielded owner `The IT`. Patched: `OwnerIn` now rejects a capitalized run opening with a determiner and skips the whole run, so that sentence yields `""` while `"Dana Whitfield will …"` still yields `Dana Whitfield`. Three determiner tests added.
  - `[low]` `[patch]` `TextNormalization`'s remark claims stripping-before-collapsing avoids joining tokens across punctuation, which is exactly what it does to `end-of-month` — patched: the remark now states FR-38's literal drop rule and both of its outcomes. No behaviour change.
  - `[medium]` `[patch]` The front-matter splitter exists twice and nothing asserts the two agree, which is deferred entry DW-15's cause rather than its symptom — patched: `FixtureCatalogTests` now asserts per case that its own split body is byte-equal to the production `FixtureCatalog.Notes`, plus a case-set equality test. A production-side `.Trim()` now reddens 14 cases where it previously left that file green.
  - `[low]` `[patch]` `AiStartupCheck` and `InfrastructureRegistration.ActiveFactory` give different unregistered-provider messages although a comment claims they read the same — patched: both now go through one `ChatClientFactories.Resolve`, so the message including the registered-provider list is identical at both sites.
  - `[false]` `[reject]` The extractor has no `ILogger`, so a failed run logs nothing — refuted: the spec's Never list assigns the `ExtractionRunCompleted` log event to Story 2.5, which owns the completed run; this story deliberately logs nothing per run.
  - `[high]` `[patch]` A `null` element inside the actions array escapes `ExtractAsync` as a `NullReferenceException` — verified empirically: `{"actions":[null]}` deserializes (nullable annotations do not reject null collection elements) and the validator dereferences it outside any try, breaking the port's never-throws contract so Story 2.5 would 500 instead of persisting a Failed run. Patched: a null element is now a validation failure naming its index, so the attempt retries and a second returns `Failed`. Tests added in both suites.
  - `[low]` `[reject]` A null `ChatResponse` produces a `NullReferenceException` — same refutation as the nullable-`response` finding: the return type is non-nullable and no implementation returning null was demonstrated.
  - `[low]` `[patch]` Each token count is clamped per attempt and then summed with unchecked `int` addition, so two saturating attempts can wrap negative — patched: the accumulator is `long` and clamps once at the end.
  - `[low]` `[reject]` No ceiling on the actions array — grouped with the `maxItems` finding above and rejected on the same evidence.
  - `[low]` `[reject]` `string.Length` counts UTF-16 units where the schema's `maxLength` counts code points — a description near 500 characters built from astral characters was not demonstrated, and switching five guards to rune counting would also put the validator out of step with `MeetingNotes.TextMaxLength`, which counts the same way.
  - `[low]` `[reject]` Format characters and combining marks pass the punctuation and symbol filter — FR-38 names punctuation and whitespace only, both sides of the verifier apply the identical rule, and adding Unicode categories changes the published normalization that Story 6.2's scorer must match.
  - `[low]` `[patch]` `int.Parse` on a prompt version's digits throws `OverflowException` while building the catalog, with nothing naming the file — patched: `int.TryParse`, verified by temporarily adding `extract-actions.v99999999999.md`.
  - `[low]` `[reject]` Two files whose versions parse alike, such as `v1` and `v01`, make `Current` arbitrary — the keys are the literal version strings so both are retained; producing the collision requires committing a second file that `PromptFileTests` would have to allow, and the guard is a branch over a state nothing reaches.
  - `[low]` `[patch]` A non-string `version` in the committed schema throws inside a static initializer, losing the crafted AD-6 message — patched: `JsonValue.TryGetValue`, so the message naming the missing member is what surfaces.
  - `[low]` `[patch]` The heuristic's description truncation cuts at a UTF-16 index and can leave a lone surrogate — patched: the cut is now on a rune boundary, verified with an emoji straddling index 500.
  - `[low]` `[patch]` The catalog's pairing rule runs in one direction, so an answer file with no notes file is silently absent — patched: an orphaned `<stem>.expected.json` now throws naming the stem, verified with a temporary file.
  - `[high]` `[patch]` Claim check: "a second failure returns `Failed` … and nothing throws" is false for `{"actions":[null]}` — same root cause as the null-element finding above and patched with it.
  - `[medium]` `[patch]` Claim check: "one proposal per modal-verb sentence, at most five, in order" — a sentence over 1000 characters is skipped, so a later sentence takes its slot. Grouped with the sentence-segmentation finding and patched with it; a ~1200-character sentence now has a test.
  - `[medium]` `[patch]` Claim check: the strict-mode key's recorded provenance names `Microsoft.Extensions.AI.OpenAI` 10.10.0 — verified false: no project references that package and the only copy on this machine is `10.2.0-preview.1.26063.2`. Patched: the remark now states exactly what was read and where, says plainly that no project references the package, and tells Story 2.7 to re-confirm against the version it pins.
  - `[medium]` `[patch]` The `AddActionLedgerAi` call in `Program.cs` has no test — deleting it left every existing test green, and the registration-order claim was asserted nowhere. Patched: `tests/Api.Tests/AiRegistrationTests.cs` boots the real host and asserts seam resolution, refusal to start on `Ai:PromptVersion=v99`, and the order against the seeder. Deleting the block reddens all three; moving it after the seeder reddens the ordering test alone.
  - `[medium]` `[patch]` The Fake's "never emits output its own validator would reject" invariant is unverified at both length guards — patched: `FakeProviderTests` now drives ~600-character and ~1200-character modal-verb sentences and asserts the run still succeeds.
  - `[medium]` `[patch]` `StartedAt` and `DurationMs` are asserted only as `!= default` and `>= 0`, with every test on the wall clock — patched: a hand-written `SteppingTimeProvider` now pins exact values for a one-call, a retried and a failed run. A literal `0` duration, or recording `StartedAt` at the end, each redden three tests.
  - `[low]` `[defer]` `PromptCatalog`'s numeric version ordering is never exercised with more than one version — filed by the gap layer as defer: closing it needs either a seam for the resource source or a second embedded prompt, and neither is worth adding before a second prompt revision exists.
  - `[medium]` `[patch]` `SentenceSpan` matches across newlines, so an unpunctuated bullet list becomes one proposal quoting the whole block, and a block over 1000 characters yields none — verified empirically. Patched: a line break now ends a sentence, so that list yields one proposal per bullet. Bullet-list and bare-line-break tests added.
  - `[low]` `[reject]` `Clamp`'s negative and over-`int.MaxValue` branches are unpinned — filed by the gap layer as explicitly not a gap, on the grounds that no realistic provider produces those inputs; the saturation half is now covered by the token-overflow patch.
  - `[false]` `[reject]` Most of the intent's operative text lives in `_bmad-output/`, which the diff cannot confirm or refute — refuted: the intent forbids writing `sprint-status.yaml` and the diff does not touch it; the spec frontmatter is this run's own bookkeeping, outside the reviewed diff by design.
  - `[medium]` `[patch]` The startup AC is about the host starting, but the test invokes `StartAsync` on a hand-built container — grouped with the composition-root gap and patched with it.
  - `[medium]` `[patch]` The strict-mode key's provenance cites a package that is not installed — grouped with the claim-check finding above and patched with it.
  - `[false]` `[reject]` The epic asks for three response paths in `Application.Tests`; the diff puts them in `Infrastructure.Tests` — refuted: `Application.Tests` references only `ActionLedger.Application`, AD-18 places a test with its ring, and the spec's Design Notes resolved this before implementation.
  - `[medium]` `[patch]` The injected `TimeProvider` is justified by a need no test uses — grouped with the timing-metrics gap and patched with it.
  - `[low]` `[patch]` The owner rule narrows across three surfaces and the single-word leading name is described but not exercised — grouped with the determiner finding and patched with it; the single-word case now has a test and remains a documented trade-off.
  - `[false]` `[reject]` The committed document keeps `minLength` and `format`, so "strict subset" is unexercised — refuted: AD-11 states that the adapter demotes those keywords before sending, which is precisely what makes the wire document a subset; the committed file is required to match the published schema.
  - `[low]` `[reject]` Exporter parity compares types per leaf rather than over the whole document — the root and array-element `["object","null"]` is an exporter artifact of reference-type nullability, not drift from the committed file; the AC names property names, the required set and types, all of which are compared, and the test discloses the exclusion in its own remarks.
  - `[false]` `[reject]` The command-level acceptance criteria carry no evidence inside the patch — refuted: those criteria are observations at a command surface, and the commands were run in this session; their outcomes are recorded under Auto Run Result.

### 2026-09-22 — Review pass (follow-up)
- verdicts: 37 findings — high 0, medium 5, low 27, false 5, maybe-false 0
- findings:
  - `[false]` `[reject]` `sprint-status.yaml` is modified although the intent forbids it, an AC asserts it is untouched, and the prior triage row rejected a finding by claiming it is untouched — refuted: `git show --stat c2bff6d -- _bmad-output/implementation-artifacts/sprint-status.yaml` is empty, so the story's own commit does not touch the file. The `backlog` → `done` hunk is an uncommitted working-tree edit the orchestrator made after the story committed, and this run's invocation states the board is orchestrator-owned and must be neither written nor reverted. The prior row was accurate about the diff it reviewed.
  - `[false]` `[reject]` The "strict subset" wire schema is never produced and the only test pins the absence of the transform — refuted: AD-11 assigns the pre-send transform (all properties required, `additionalProperties` false, unsupported keywords demoted) to `Microsoft.Extensions.AI`'s provider adapter, not to this code. `ExtractionSchema.BuildWireJson` removing only `version` is precisely what AD-11 asks of this ring, and `The_wire_schema_drops_version_and_changes_nothing_else` pins that contract rather than its absence. Story 2.7 owns confirming the adapter's behaviour against the version it pins, as the `StrictSchemaKey` remark already records.
  - `[low]` `[reject]` Nothing asserts the committed schema still matches `addendum.md:55-76` — verified the byte-identity holds today (the committed file is the published block plus the `version` member AD-6 requires), so no drift exists. Closing it means a new Architecture test that parses a BMAD planning document, which is new machinery for a drift nobody has demonstrated and which `AiSeamTests`' embedded-vs-disk comparison already half-guards.
  - `[low]` `[reject]` `ExtractAsync` can throw before it enters any `try`, because `_prompts.Current` and `_prompts.Get(...)` run above the retry loop — real as written, but `AiStartupCheck` resolves both at startup, so in any composed host the state is unreachable; the only construction that reaches it is a hand-built catalog in a test. Folding the lookup into the `Failed` path needs a fallback `promptVersion` for `Metrics`, which is more than a direct correction.
  - `[low]` `[patch]` The caller's token is never observed by the extractor itself, so `A_token_cancelled_before_the_call_propagates` certifies the fake rather than the extractor — patched: `ExtractAsync` now calls `cancellationToken.ThrowIfCancellationRequested()` at the top of each attempt, so the never-throws contract's one documented exception is the extractor's own promise rather than a property borrowed from the client.
  - `[low]` `[patch]` `"yyyy-MM-dd"` is written as a literal in `Verify`'s `DateOnly.ParseExact` beside the `ExtractionOutputValidator.DueDateFormat` const that just validated the same value, and `Verify` is called outside every `try` — patched: the call now reads the const, so widening the accepted date format can no longer put a `FormatException` through a port documented never to throw.
  - `[low]` `[patch]` `ExtractionOutput.SerializerOptions` is documented as deserialize-only but is also the serializer for the extractor's user-message envelope and the Fake's answer — patched: the summary and a new remarks paragraph now state that both directions use it, which of its settings are inert outbound, and that a change made for deserialization changes what the seam sends.
  - `[medium]` `[patch]` The Fake's owner heuristic names the object of the sentence: `"Dana will email Marcus Chen."` yielded `Marcus Chen`, because a rejected single-word leading name only skipped that word and the scan continued — patched: a rejected leading word now ends the search, so the sentence yields `""`. The remarks gained a paragraph saying why naming the person the work is owed *to* is worse than naming nobody. Reverting the `return` to a `continue` reddens the new test.
  - `[low]` `[patch]` The determiner guard covered ten words and trimmed punctuation on one side only, so `"Every Facilities lead must sign off."`, `"All Vendor Partners should quote."`, `"His Team will follow up."` and any bracketed run still yielded an article phrase as an owner — patched: the set now covers `Every/Each/All/Any/No/Some/Both/Either/Neither/His/Her/My/Your` alongside the articles, and the word is trimmed of quotes and brackets on both sides before the lookup. Four cases added to the determiner theory.
  - `[low]` `[reject]` The 0.55 confidence only appears when unknown notes carry five or more modal-verb sentences — verified, and that is exactly what the intent specifies ("0.85 for the first four and 0.55 for the fifth"). The code matches its contract; changing it to make the low-confidence flag easier to demo would be a deviation, and the demo's fixture selection is not this story's surface.
  - `[low]` `[reject]` `ChatClientFactories.Registered` constructs every registered factory to compose a failure message — verified as written, but today the only factory is the Fake's, which is trivially constructible, so no bad outcome occurs. It becomes real when Story 2.7 registers factories that can fail in their constructors; closing it now means a parallel registry of provider names, which is new surface for a state nothing reaches.
  - `[low]` `[reject]` `The_committed_schema_declares_no_bound_the_validator_ignores` checks only that each leaf's keyword *names* are in an allowlist, so `"format": "email"` on `suggestedOwner` would leave it green — real, but the committed schema is contractually frozen to `addendum.md`, so the keyword set cannot change without a deliberate spec amendment. Replacing the flat allowlist with a per-member keyword map is a test rewrite, not a direct correction.
  - `[low]` `[reject]` `AddActionLedgerAi` is not idempotent: a second call adds a second keyed factory and a second hosted service — verified, and it matches every other registration extension in this repo (`AddActionLedgerSeeding`, `AddActionLedgerPersistence`). A composition root calls each once; no caller calls it twice. `TryAdd` everywhere plus a hosted-service guard is complexity for an unreached state.
  - `[low]` `[reject]` The schema resource is the only embedded file with no explicit `LogicalName`, so its name depends on MSBuild mangling and `RootNamespace` — verified the inconsistency with the prompts and fixtures globs. `The_schema_is_embedded_and_declares_its_own_version` fails loudly and immediately on any drift, so the coupling cannot ship silently; changing a working resource name to gain a guard that already exists is not worth the churn.
  - `[low]` `[reject]` Three of `FixtureCatalog`'s four throw sites and `PromptCatalog`'s negative surface are untestable without editing the repository, because both read a static assembly reference — real. The `PromptCatalog` half is already deferred as DW-19; the `FixtureCatalog` half was manually exercised for two of three guards in the prior pass. Closing it means adding an internal resource-source seam to both types, which is new surface rather than a direct correction.
  - `[low]` `[reject]` The embedding acceptance criterion says 30 catalog files where the test correctly asserts 29 — the fix is an edit to this build's spec, which this review does not make.
  - `[low]` `[reject]` A null `ChatResponse` produces a `NullReferenceException` — carried: same claim and location as the prior pass's row; `IChatClient.GetResponseAsync` returns a non-nullable `Task<ChatResponse>` and no implementation returning null was demonstrated. Code reads as that row describes.
  - `[low]` `[reject]` Nothing caps the number of actions a response may contain — carried: same claim and location as the prior pass's two rows on `maxItems`; notes are capped upstream and no unbounded response was demonstrated.
  - `[low]` `[patch]` A full stop inside a decimal, an abbreviation or an email address ended a sentence, so `"Dana will order 2.5 tons."` produced the truncated excerpt `"Dana will order 2."` — which still verified, because it is a genuine substring of the notes, and so reached the Review Screen looking like a real citation. Patched: a terminator ends a sentence only when whitespace, a closing bracket or quote, or the end of the line follows it. Two theory cases added. The first formulation of this fix dropped the text *before* an interior stop and lost a bracketed sentence entirely; both were caught by the suite and by a new bracketed determiner case before the fix was accepted.
  - `[low]` `[defer]` `Ai:CallTimeoutSeconds` accepts up to 600, so two calls can spend 1,200 seconds against NFR-1's 180-second ceiling — carried: DW-16 already records this, and the code reads as that row describes. Not re-deferred.
  - `[low]` `[reject]` Format and control characters (U+200B, U+00AD, U+0000) pass the punctuation and symbol filter — carried: same claim and location as the prior pass's row; FR-38 names punctuation and whitespace only, both sides of the verifier apply the identical rule, and widening it changes the published normalization Story 6.2's scorer must match.
  - `[low]` `[reject]` A bare CR or U+2028/U+2029 line break makes a whole block one sentence — verified the regex is `\r?\n`, but `.gitattributes` pins `fixtures/**` and `prompts/**` to LF and pasted browser input uses LF or CRLF; no reachable source of bare-CR notes was demonstrated, and widening the split is a guard on an undemonstrated state.
  - `[low]` `[reject]` Two prompt files whose versions parse alike (`v1` and `v01`) make `Current` arbitrary — carried: same claim and location as the prior pass's row; the keys are the literal version strings, and producing the collision needs a second file `PromptFileTests` would have to allow.
  - `[false]` `[reject]` Claim check: the AC "nothing under `sprint-status.yaml` has changed" is violated by this same diff — refuted on the same evidence as the first row: the story's commit does not touch the file; the hunk is the orchestrator's uncommitted bookkeeping.
  - `[medium]` `[patch]` `Ai:Provider` and `Ai:CallTimeoutSeconds` are mapped at the composition root with nothing asserting either value arrives — filed pre-verified by the gap layer and confirmed: replacing the accessor body with `new AiSettings("Fake", ai.PromptVersion, 90)` left the whole suite green. Patched: `AiRegistrationTests` gained `The_composition_root_carries_the_configured_ai_values_into_the_ring` (boots the host with `Ai:CallTimeoutSeconds=7` and resolves `AiSettings`) and `A_provider_with_no_registered_factory_stops_the_host_before_it_serves` (`Ai:Provider=AzureOpenAI` with a complete sub-section, so only `AiStartupCheck` can refuse). The mutation reddens both.
  - `[medium]` `[patch]` The Fake's owner-length bound is the only thing keeping it from emitting output its own validator rejects, and no test exercises it — patched: `A_capitalized_run_past_the_owner_bound_leaves_the_owner_empty` drives a 115-character capitalized run that no determiner opens. Removing the bound reddens it. The first version of this test opened with `ALL`, which the widened determiner set now skips before the bound is reached, so the mutation stayed green; the input was corrected and the mutation re-run.
  - `[low]` `[patch]` The rune-boundary cut in the Fake's description shortener is unverified, because its only test uses pure ASCII — patched: `A_description_cut_at_the_bound_never_ends_in_a_lone_surrogate` puts an emoji's high surrogate at index 499. Reducing the cut to `sentence[..cut]` reddens it.
  - `[low]` `[defer]` `PromptCatalog`'s "highest embedded version" fallback is asserted against its own implementation — carried: DW-19 already records this, and the code reads as that row describes. Not re-deferred.
  - `[medium]` `[patch]` The unknown-provider AC names the host, but the diff exercises it on a hand-built `ServiceCollection` — grouped with the composition-root gap and patched with it by the new host-level `AzureOpenAI` refusal test.
  - `[medium]` `[patch]` The per-call budget's expectation lives at a configuration key while its tests live at a constructor argument — grouped with the composition-root gap and patched with it by the `Ai:CallTimeoutSeconds=7` assertion.
  - `[low]` `[defer]` NFR-1's 180-second ceiling is enforced as arithmetic over the default rather than as a whole-run deadline — carried: DW-16 already records this. Not re-deferred.
  - `[low]` `[patch]` No test covers the single-word leading name the `OwnerIn` remarks name as the rule's cost, although the prior triage log claims one was added — verified absent. Patched: `A_single_word_leading_name_yields_no_owner_and_ends_the_search` covers both `"Marcus needs to call the vendor."` and the object-naming case the scan-continuation patch fixed.
  - `[false]` `[reject]` The strict-mode key is asserted by reading back the literal the production code wrote, with nothing binding it to the adapter package — refuted as a defect: no project references `Microsoft.Extensions.AI.OpenAI`, the `StrictSchemaKey` remarks say exactly that and hand re-confirmation to Story 2.7, and the prior pass patched that provenance. A test cannot bind to a package the solution does not reference.
  - `[low]` `[defer]` A provider exception's `Message` is interpolated verbatim into the persisted failure reason — carried: DW-18 already records this, and the code reads as that row describes. Not re-deferred.
  - `[false]` `[reject]` `sprint-status.yaml` is forbidden by the Never list and changed by the diff — refuted on the same evidence as the first row.
  - `[low]` `[reject]` `FixtureCatalogTests` was strengthened beyond what the intent asked — verified, and it is not a defect: the added per-case body comparison closes Story 2.3's DW-15 at a stronger surface. Nothing to fix.
  - `[low]` `[reject]` New surface the intent does not mention: `TryAddSingleton(TimeProvider.System)`, `IAiProviderInfo`/`AiProviderInfo`, and a `public` `FixtureCatalog` — verified all three. `IAiProviderInfo` is named in the spec's own Execution list; the `TimeProvider` registration is `TryAdd` and documented against AD-15; `FixtureCatalog` is public because `Architecture.Tests` must construct it and `InternalsVisibleTo` covers only `Infrastructure.Tests`. No named harm.


## Design Notes

**Why the extractor's tests live in `Infrastructure.Tests` and not `Application.Tests`.** The epic's
second acceptance criterion says "`Application.Tests` covers valid, invalid-then-valid, and
invalid-twice responses", but it places `ChatClientActionExtractor` in Infrastructure in the same
sentence, and `Application.Tests.csproj` references only `ActionLedger.Application`. AD-18 puts a test
with its ring. The criterion's intent — those three response paths are covered by a unit test with no
model server — is met in `Infrastructure.Tests`, which already carries a no-Docker precedent in
`InfrastructureRingTests`. The Application half of the same criterion (strict deserialization, the
validator, the exporter parity assertion) does live in `Application.Tests`, where it belongs.

**Why `suggestedDueDate` is a `string?` on the wire type.** Binding it to `DateOnly?` would make
`System.Text.Json` reject `"2026-13-40"` before `ExtractionOutputValidator` ever sees it, turning a
date-format failure into a deserialization failure with a worse message — and `JsonSchemaExporter`
would emit a `format`-annotated `string`, not the `["string","null"]` the committed file declares, so
the parity test would fail for a reason that is not a real drift. The validator owns date format.

**Why the schema carries `version` but the wire request does not.** AD-6 makes `SchemaVersion` the
schema file's own top-level `version`, which is the only way a run can record which schema validated
it. `version` is not a JSON Schema keyword, and AD-11 requires the wire schema to be a strict subset
of the committed file, so `ExtractionSchema.WireJson` is the same document with that one member
removed. One file, two views, no second copy of the schema anywhere.

**Why provider selection is keyed DI.** FR7 is measured by the size of Story 2.7's diff. With a keyed
`IChatClientFactory`, that diff is two classes in `Infrastructure/Ai/Providers/` and two
`AddKeyedSingleton` lines — and an unregistered provider is a startup failure naming the provider
rather than a null reference at the first extraction. A `switch` in the registration method would
work identically today and would have to be edited by Story 2.7, which is exactly the edit the proof
point claims is unnecessary.

**Why the Fake keys on the normalized hash, not the raw text.** AD-21 says so, and the reason shows up
in the demo: an operator who pastes a case's notes out of the repository picks up different wrapping
and trailing whitespace than the file holds. The raw hash would miss; the normalized hash matches.
`MeetingNotes.Sha256` is a different value — it hashes the raw text and exists to prove a run read the
notes it claims to have read — and the two must not be conflated.

**Why `ExcerptVerifier` normalizes both sides.** FR-5 and FR-38 both say the substring check uses the
FR-38 normalization. The catalog's excerpts are already exact ordinal substrings, which is strictly
stronger, so every fixture passes either way; normalizing is what makes a real model's minor
re-punctuation survive instead of silently costing recall.

**Golden example — the settings hand-off, mirroring `AddActionLedgerSeeding`:**

```csharp
// AD-11, AD-16 — the AI ring takes validated values, never configuration, and its startup check
// is registered before the seeder so a bad provider fails before any row is written.
builder.Services.AddActionLedgerAi(services =>
{
    AiOptions ai = services.GetRequiredService<IOptions<AiOptions>>().Value;

    return new AiSettings(ai.Provider, ai.PromptVersion, ai.CallTimeoutSeconds);
});
```

## Verification

**Commands:**

- `dotnet build ActionLedger.sln` -- expected: 0 errors, 0 warnings (`TreatWarningsAsErrors` is on).
- `dotnet build ActionLedger.sln -c Release` -- expected: the same; this is `ci.yml`'s shape.
- `dotnet test ActionLedger.sln` -- expected: eight assemblies green, 0 skipped, total above the 765
  recorded at `1f39300`.
- `git status --porcelain` -- expected: changes confined to `Directory.Packages.props`,
  `src/ActionLedger.Application/`, `src/ActionLedger.Infrastructure/`, `src/ActionLedger.Api/Program.cs`,
  `tests/Application.Tests/`, `tests/Infrastructure.Tests/`, `tests/Architecture.Tests/AiSeamTests.cs`,
  and this spec. Nothing under `prompts/`, `fixtures/`, `src/ActionLedger.Web/` or
  `src/ActionLedger.Domain/`.
- `git diff --stat 1f39300 -- .env.example src/ActionLedger.Api/appsettings.json` -- expected: empty.

**Mutation checks — introduce each, confirm the named test goes red, then revert:**

- Trim the fixture body in `FixtureCatalog` → the body-is-byte-for-byte test. (Not the
  excerpt-verifies-through-production test: every committed excerpt is an interior sentence, so a
  trimmed body still contains all 31 of them, and the Fake's key moves consistently on both sides.)
- Drop the `Exclude` on `README.md` from the embedded glob → the catalog-loads-14-cases test.
- Remove the prompts `<EmbeddedResource>` item from `ActionLedger.Infrastructure.csproj` → the
  prompt-is-embedded tests. (Editing the file's bytes instead cannot go red: the rebuild re-embeds
  the edited file, so disk and resource agree again. What the byte comparison guards is a dropped or
  mis-named item, which this mutation and the README one exercise.)
- Make `ExtractionOutput` tolerate an unmapped member → the strict-deserialization test.
- Widen `confidence` to accept `1.5` → the validator boundary test.
- Return `Failed` on an unverifiable excerpt instead of dropping it → the drop-not-fail test.
- Retry twice instead of once → the invalid-twice test's call count.
- Register a second `IActionExtractor` implementation → `AiSeamTests`' one-extractor rule.
- Rename `TextNormalization` to `TextNormalizer` and move it to `Infrastructure/Ai` →
  `DependencyRuleTests` Rule 5.
- Remove the top-level `version` from the schema → the `SchemaVersion` test.

**Manual checks:**

- Read the `Microsoft.Extensions.AI` 10.10.0 public surface for the strict-structured-output key
  before writing `ChatOptions.AdditionalProperties`, and record in a code comment what the key is and
  where it was confirmed — only the Fake exercises that request path until Story 2.7, so no test in
  this story can catch a wrong key.



## Auto Run Result

Status: done (follow-up review pass)

**Summary of implemented change.** No new feature work. This pass re-reviewed the shipped Story 2.4
seam against its frozen intent with four independent layers, triaged 37 findings, and applied ten
patches: six corrections to the extractor, the Fake and the Application wire type, and four tests
that close verification gaps the previous pass left open. The seam's contract, its public surface
and its acceptance criteria are unchanged.

**Files changed.**

- `src/ActionLedger.Infrastructure/Ai/ChatClientActionExtractor.cs` — the caller's token is now
  observed by the extractor itself at the top of each attempt, and `Verify` parses a due date with
  `ExtractionOutputValidator.DueDateFormat` rather than a second copy of the literal.
- `src/ActionLedger.Infrastructure/Ai/FakeChatClient.cs` — a rejected single-word leading name ends
  the owner search instead of handing the slot to the sentence's object; the determiner vocabulary
  covers quantifiers and possessives and is matched after trimming quotes and brackets; a full stop
  inside a decimal, abbreviation or email address no longer ends a sentence.
- `src/ActionLedger.Application/Ai/ExtractionOutput.cs` — `SerializerOptions`' documentation now
  states that the seam both reads and writes with it, and which settings are inert outbound.
- `tests/Api.Tests/AiRegistrationTests.cs` — two host-level tests: the composition root carries the
  configured `Ai` values into the ring, and a provider with no registered factory stops the host.
- `tests/Infrastructure.Tests/FakeProviderTests.cs` — four tests: the single-word leading name, a
  capitalized run past the owner bound, the rune-boundary cut, and an interior full stop; plus four
  determiner cases.

**Review findings breakdown.** 37 findings — 0 high, 5 medium, 27 low, 5 false, 0 maybe-false.
Ten entries patched (3 medium, 7 low at entry verdict). Four entries carried from the previous
pass's defers (DW-16 twice, DW-18, DW-19) and re-logged rather than re-deferred; no new deferral was
added, and no existing ledger entry was reopened or edited. Twenty-three findings rejected, each
with its refutation or its reason recorded in the triage-log entry above.

**Follow-up review recommendation: false.** This was a follow-up pass, so the bar is a patched
`high`; none of the ten patched entries was graded above `medium`, which means the work has
converged. Patched counts by verdict: high 0, medium 3, low 7.

**Verification performed.**

- `dotnet build ActionLedger.sln` — 0 warnings, 0 errors.
- `dotnet build ActionLedger.sln -c Release` — 0 warnings, 0 errors.
- `dotnet test ActionLedger.sln` — 969 passed, 0 failed, 0 skipped, eight assemblies. The commit
  under review recorded 957; this pass adds 12.
- `git status --porcelain` — changes confined to the four source and test files above plus this
  spec. Nothing under `prompts/`, `fixtures/`, `src/ActionLedger.Web/` or `src/ActionLedger.Domain/`.
- `git diff --stat 1f39300 -- .env.example src/ActionLedger.Api/appsettings.json` — empty.
- Mutation checks on all four new tests, each introduced, confirmed red against the named test, and
  reverted: hard-wiring `new AiSettings("Fake", ai.PromptVersion, 90)` in `Program.cs` reddens both
  new `AiRegistrationTests`; restoring the `continue` in `OwnerIn` reddens the single-word-name
  theory; `sentence[..cut]` reddens the surrogate test; dropping the owner-length bound reddens the
  owner-bound test.

**Residual risks.**

- The first owner-bound test and the first sentence-regex fix were both wrong in ways the suite
  caught: the test opened with a word the same pass had just added to the determiner set, and the
  regex dropped text ahead of an interior full stop. Both were corrected and re-verified by
  mutation, but they are evidence that the Fake's heuristic has more coupled rules than its size
  suggests, and that a future edit to any one of them should be mutation-checked rather than
  assumed.
- `ChatClientFactories.Registered` still constructs every registered factory to compose its
  failure message. Harmless while the Fake is the only factory; Story 2.7 should decide whether a
  provider whose constructor can fail belongs on that path.
- DW-16, DW-18 and DW-19 remain open and unchanged; all three become reachable with Story 2.7's
  real providers, which is where they were filed to be settled.
