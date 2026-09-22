---
title: 'Story 2.7 — Real providers through the same seam: LM Studio, Ollama, and Azure OpenAI'
type: 'feature'
created: '2026-09-22'
baseline_revision: '05cad7f20abb141130db3499ba21b785db0919fb'
status: 'awaiting-operator'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-2-context.md'
warnings: ['oversized']
deferred: []
operator_actions:
  - "Download an instruct chat model in LM Studio 0.4.25 (7B or larger, so it honours json_schema response_format), load it, and start the local server on port 1234 (`lms server start`)."
  - "Run `curl -s http://localhost:1234/v1/models`, then set `Ai__Provider=LocalOpenAI`, `Ai__LocalOpenAI__BaseUrl=http://host.docker.internal:1234/v1`, and `Ai__LocalOpenAI__Model=<the id exactly as /models lists it>` in `.env`, and restart the stack with `docker compose up -d --build`."
  - "Confirm the api starts and logs `AI provider verified. Provider LocalOpenAI`, then sign in, open a meeting whose notes are a fixture case (for example fixtures/extraction/office-move-planning.md), press Run extraction, and confirm the run is Succeeded with proposals on Run Detail inside 60 seconds."
  - "Optionally, repeat the same check with Ollama 0.34.2 (`ollama pull <model>`, `Ai__LocalOpenAI__BaseUrl=http://host.docker.internal:11434/v1`, and the model id with its `:tag`), and, if Azure OpenAI is in scope, set `Ai__AzureOpenAI__Endpoint=https://<resource>.openai.azure.com/openai/v1/`, `Ai__AzureOpenAI__Model`, and `Ai__AzureOpenAI__ApiKey` from the Azure portal and repeat the run with `Ai__Provider=AzureOpenAI`."
  - "Set `Ai__Provider=Fake` again in `.env` before the demo freeze, unless the demo is meant to run on the local model."
---

<intent-contract>

## Intent

**Problem:** The AI seam from Story 2.4 has only the Fake provider registered. `Ai:Provider=LocalOpenAI`
or `AzureOpenAI` fails the host with "no registered IChatClientFactory". So the demo cannot run on a
local model, and the provider-swap proof point (FR-7) is unproven (epics.md:499-516).

**Approach:** Add two factories, `LocalOpenAIChatClientFactory` and `AzureOpenAIChatClientFactory`,
beside the Fake in `Infrastructure/Ai/Providers`. Both use the OpenAI SDK 2.14.0 with
`OpenAIClientOptions.Endpoint` and `Microsoft.Extensions.AI.OpenAI` 10.10.0's `AsIChatClient()`. Add a
provider startup probe in the same folder (`GET {BaseUrl}/models` for LocalOpenAI, credential presence
for AzureOpenAI). Register both factories, their settings, and the probe in `AddActionLedgerAi`, pass
the validated sub-sections from `Program.cs`, and tighten `AiOptions` so a configured value can no
longer break the 180-second ceiling or the model-name bound.

## Boundaries & Constraints

**Always:**

- **The narrow diff is the proof point.** In `src/`, only these may change:
  `src/ActionLedger.Infrastructure/Ai/Providers/**`, `InfrastructureRegistration.cs`, `Program.cs`,
  `src/ActionLedger.Api/Configuration/{AiOptions.cs,ApiOptionsRegistration.cs}`,
  `ActionLedger.Infrastructure.csproj` and `Directory.Packages.props` (package references),
  and `.env.example`. `ChatClientActionExtractor`, `AiStartupCheck`, `AiSettings`, `AiProviderInfo`,
  Application, Domain, Web, and every migration stay byte-identical.
- The two factories differ only in endpoint, model, and credential. LocalOpenAI sends a fixed
  placeholder api key, because LM Studio and Ollama ignore it and the SDK requires one. Azure sends
  `Ai:AzureOpenAI:ApiKey` as the SDK's bearer credential against `https://<resource>.openai.azure.com/openai/v1/`.
- Both factories set `RetryPolicy = new ClientRetryPolicy(maxRetries: 0)`, so one extractor attempt
  is exactly one HTTP call. Both also set `NetworkTimeout` to `Ai:CallTimeoutSeconds + 10 s`, so the
  extractor's per-call budget always fires before the SDK's own timeout.
- Factory constructors never throw and never touch the network. `ChatClientFactories.Registered`
  builds every registered factory to compose its error message, so a factory must be constructible
  when its sub-section is empty (Fake configured).
- Provider-specific settings records live in `Providers/`. `AiSettings` stays secret-free. The
  Azure settings record's `ToString()` must not print the api key.
- The startup probe is a hosted service registered immediately after `AiStartupCheck` and before
  `AddActionLedgerSeeding`'s seeder. It resolves the active factory through
  `ChatClientFactories.Resolve` and awaits a new `IChatClientFactory.VerifyAsync(CancellationToken)`.
  The Fake's implementation is a no-op. A failure throws `InvalidOperationException` whose message
  names `Ai:Provider`, the failing key or URL, and what to do.
- The LocalOpenAI probe uses a fixed 10-second timeout. It fails when `/models` is unreachable or
  returns non-2xx. It also fails when the response lists model ids and `Ai:LocalOpenAI:Model` is not
  among them; the message lists up to 20 of the available ids. The probe must not log or throw the
  api key.
- `AiOptions.CallTimeoutSeconds` is narrowed to `[Range(1, 90)]`, because two calls must fit inside
  180 s (settles DW-16). `AiOptionsValidator` requires, for the active provider only:
  `BaseUrl`/`Endpoint` is an absolute `http`/`https` URI; the Azure endpoint is `https`; each `Model`
  is at most `ExtractionRunMetadata.ModelMaxLength` characters (settles DW-21).

**Never:**

- No `Azure.AI.OpenAI` package, and no Azure-specific SDK of any kind.
- No edit to the extractor, prompts, fixtures, schema, `appsettings.json`, or `docker-compose.yml`.
  The default stays `Ai:Provider=Fake`.
- No real network call in the default test run. Tests use a loopback `HttpListener` stub or a closed
  port, and there are no skipped tests.
- No secret committed. `.env.example` keeps placeholders only.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Local run, happy | LocalOpenAI → stub server answering `/models` (lists the model) and `/chat/completions` with a fixture case's expected output | Host starts. Extractor returns Succeeded with the fixture's proposals. Request body carries `model`, `response_format.type = json_schema`, and `strict: true` | No error expected |
| Local, unreachable | BaseUrl points at a closed loopback port | Host fails to start. `InvalidOperationException` names `LocalOpenAI` and the `/models` URL | Nothing is seeded |
| Local, model missing | `/models` lists `other-model` only | Host fails to start. Message names the configured model and lists `other-model` | — |
| Local, `/models` non-2xx | Stub answers 500 | Host fails to start, and the message includes the status | — |
| Local, slow model | Stub delays past `CallTimeoutSeconds=1` | Run Failed with the extractor's timeout reason after two attempts, well under 2 × (1 + 10) s. Stub sees exactly two chat calls | No SDK retries |
| Azure, complete | Endpoint, model, and key set | Host starts with no network. Provider info reports `AzureOpenAI` and the model. A stub-backed call sends `Authorization: Bearer <key>` | — |
| Azure, missing value | Any one of Endpoint, Model, or ApiKey blank | Host fails with an `OptionsValidationException` naming that key | — |
| Bad URI / long model / timeout > 90 | e.g. `BaseUrl=not-a-url`, 201-char model, `CallTimeoutSeconds=91` | `OptionsValidationException` naming the key | Only the active provider's section is checked |

</intent-contract>

## Code Map

**Seam (read-only unless listed in Always):**

- `src/ActionLedger.Infrastructure/Ai/Providers/IChatClientFactory.cs` -- `IChatClientFactory { Provider; Model; Create() }` and `ChatClientFactories.Resolve/NotRegistered/Registered`. `Registered` constructs every keyed factory (`GetKeyedServices(AnyKey)`). Add `Task VerifyAsync(CancellationToken)` here.
- `src/ActionLedger.Infrastructure/Ai/Providers/FakeChatClientFactory.cs` -- template for the new factories (`const ProviderName`, XML-doc style). Gains a no-op `VerifyAsync`; its "Story 2.7 adds..." remark becomes past tense.
- `src/ActionLedger.Infrastructure/InfrastructureRegistration.cs:104-145` -- `AddActionLedgerAi(services, Func<IServiceProvider, AiSettings>)`. `:124` is the keyed Fake line that the new siblings sit beside. `:143` registers `AiStartupCheck`; the probe registers directly after it. The remarks at `:88-92` name this story.
- `src/ActionLedger.Infrastructure/Ai/ChatClientActionExtractor.cs:53, :107-115, :120, :223-225` -- READ-ONLY. It sends `ChatResponseFormat.ForJsonSchema` with `AdditionalProperties["strict"]=true` and bounds each call with a linked `CancellationTokenSource(CallTimeoutSeconds)`. The remark at `:35-52` asks this story to re-confirm the `"strict"` key against the pinned adapter. Confirm it with a wire-level test (the stub captures `strict: true`), not by editing the extractor.
- `src/ActionLedger.Infrastructure/Ai/AiStartupCheck.cs` -- READ-ONLY. Its hosted-service shape and message style are the model for the probe.
- `src/ActionLedger.Infrastructure/ActionLedger.Infrastructure.csproj` -- add `<PackageReference Include="OpenAI" />` and `Microsoft.Extensions.AI.OpenAI` under the existing AD-11 comment.
- `Directory.Packages.props:83-90` -- the "AI seam" ItemGroup. Add `OpenAI` 2.14.0 and `Microsoft.Extensions.AI.OpenAI` 10.10.0 (spine Stack table :215-216) and update the comment.

**Composition root and configuration:**

- `src/ActionLedger.Api/Program.cs:45-54` -- the `AddActionLedgerAi` accessor. Pass `AiOptions.LocalOpenAI` and `AiOptions.AzureOpenAI` into the new settings records.
- `src/ActionLedger.Api/Configuration/AiOptions.cs` -- `[Range(1, 600)]` on `CallTimeoutSeconds` becomes `[Range(1, 90)]`. Update the stale remark that says the probe "arrives with the extraction work in Epic 2".
- `src/ActionLedger.Api/Configuration/ApiOptionsRegistration.cs:45-79` -- `AiOptionsValidator`. Add URI and model-length checks beside `RequireKey`. The Api can see `ActionLedger.Domain.Extraction.ExtractionRunMetadata.ModelMaxLength` (200) transitively. Update the stale remark at `:39-43`.
- `.env.example:20-34` -- document the 1-90 range, the Ollama base URL `http://host.docker.internal:11434/v1`, and that LocalOpenAI must list the model at `/models`. `ComposeTopologyTests` pins the key set and `Ai__Provider=Fake`, so keep every key and value.

**Tests to update or add:**

- `tests/Infrastructure.Tests/AiStartupCheckTests.cs:26-94` -- the `Provider(...)` helper calls `AddActionLedgerAi(_ => settings)` and must pass the new accessors. `Assert.Single(GetServices<IHostedService>())` becomes two services. The unregistered-provider theory (`LocalOpenAI`/`AzureOpenAI`) switches to a name with no factory (for example `Nonexistent`). Then assert that `LocalOpenAI`, `AzureOpenAI`, and `Fake` are all listed as registered.
- New `tests/Infrastructure.Tests/OpenAIProviderTests.cs` -- `HttpListener` loopback stub (free port via `TcpListener(IPAddress.Loopback, 0)`) covering the I/O matrix rows at factory, probe, and extractor level. For the happy path, use a real fixture case from `FixtureCatalog` and put its `expected.json` answer in the chat completion `choices[0].message.content`.
- `tests/Api.Tests/AiRegistrationTests.cs:84-106` -- `A_provider_with_no_registered_factory_stops_the_host...` is no longer reachable through the host. Replace it with (a) Azure complete → host starts and `IAiProviderInfo` reports `AzureOpenAI` plus the model, and (b) LocalOpenAI at a closed port → host start throws naming `LocalOpenAI` and `/models`. Extend `The_ai_startup_check_runs_before_the_seeder` so the probe also precedes the seeder.
- `tests/Api.Tests/StartupValidationTests.cs:50-59` -- the existing missing-key test pattern. Add theory rows for Azure missing Endpoint/Model/ApiKey, bad URI, http Azure endpoint, 201-char model, and `CallTimeoutSeconds=91`.
- `tests/Architecture.Tests/DependencyRuleTests.cs:52-59, :119-148` -- already forbids `OpenAI` and `Microsoft.Extensions.AI` in Domain and Application. It must stay green, and must fail if the package leaked.

## Tasks & Acceptance

**Execution:**

- `Directory.Packages.props`, `src/ActionLedger.Infrastructure/ActionLedger.Infrastructure.csproj` -- pin and reference `OpenAI` 2.14.0 and `Microsoft.Extensions.AI.OpenAI` 10.10.0 -- the spine's Stack pins; the one AI SDK stays in this ring.
- `src/ActionLedger.Infrastructure/Ai/Providers/IChatClientFactory.cs`, `FakeChatClientFactory.cs` -- add `VerifyAsync`, with a no-op for the Fake -- AD-16's provider probe needs a per-provider hook.
- `src/ActionLedger.Infrastructure/Ai/Providers/LocalOpenAISettings.cs`, `AzureOpenAISettings.cs` -- settings records: BaseUrl+Model, and Endpoint+Model+ApiKey with a redacting `ToString` -- validated values handed in, with no configuration read in Infrastructure.
- `src/ActionLedger.Infrastructure/Ai/Providers/LocalOpenAIChatClientFactory.cs` -- `ProviderName = "LocalOpenAI"`. `Create()` builds `ChatClient(model, placeholder key, options{Endpoint, NetworkTimeout, RetryPolicy 0}).AsIChatClient()`. `VerifyAsync` uses the SDK's `OpenAIModelClient.GetModelsAsync` against the same endpoint under a 10 s token, and maps failures to `InvalidOperationException` -- FR-7, AD-16.
- `src/ActionLedger.Infrastructure/Ai/Providers/AzureOpenAIChatClientFactory.cs` -- `ProviderName = "AzureOpenAI"`. Same `Create()` with the real key. `VerifyAsync` re-asserts presence and an absolute https endpoint without network -- AD-16 "credential presence".
- `src/ActionLedger.Infrastructure/Ai/Providers/ProviderStartupProbe.cs` -- a hosted service that resolves the active factory and awaits `VerifyAsync`, then logs provider, model, and endpoint host (never the key) -- keeps `AiStartupCheck` untouched.
- `src/ActionLedger.Infrastructure/InfrastructureRegistration.cs` -- `AddActionLedgerAi` gains `Func<IServiceProvider, LocalOpenAISettings>` and `Func<IServiceProvider, AzureOpenAISettings>` parameters, two `AddKeyedSingleton` lines, and `AddHostedService<ProviderStartupProbe>()` after `AiStartupCheck`. Refresh the remarks -- FR-7's "one class + one line".
- `src/ActionLedger.Api/Program.cs` -- pass the two sub-sections -- composition root.
- `src/ActionLedger.Api/Configuration/AiOptions.cs`, `ApiOptionsRegistration.cs` -- the `Range(1, 90)`, URI, https, and model-length rules; refresh the stale remarks -- DW-16, DW-21, AD-16.
- `.env.example` -- comments only: the Ollama URL, the 1-90 range, and the `/models` listing requirement.
- Tests as listed in the Code Map -- cover every I/O matrix row.
- `_bmad-output/implementation-artifacts/deferred-work.md` -- set DW-16 and DW-21 to `status: resolved` with a one-line pointer to this story. DW-17's SDK-timeout path is closed by construction (`NetworkTimeout` > budget, retries 0); record that in DW-17's entry. Leave DW-18 and DW-20 open with a note that the real SDK's messages carry no credential (the key travels in a header).

**Acceptance Criteria:**

- Given `Ai:Provider=LocalOpenAI` with a BaseUrl and a model that the server lists, when the api starts and a run executes, then extraction goes through the unchanged `ChatClientActionExtractor`, and each call is bounded by `Ai:CallTimeoutSeconds` (at most 90, so a run cannot exceed 180 s).
- Given the same configuration against a real LM Studio on the developer's Mac, when a run executes on a fixture case, then it returns proposals. This is an operator check: no model is downloaded on this machine and no server runs in CI.
- Given `Ai:Provider=LocalOpenAI`, when `GET {BaseUrl}/models` is unreachable, then the host fails to start with a message naming the provider and URL. Given `AzureOpenAI` with any of endpoint, model, or api key missing, then the host fails the same way, naming the key.
- Given the finished change, when `git diff --stat <baseline> -- src` is read, then only the files named in Always appear. `Architecture.Tests` stays green, proving that Application and Domain reference no AI SDK.

## Spec Change Log

## Review Triage Log

### 2026-09-22 — Review pass
- verdicts: 33 findings — high 0, medium 0, low 24, false 9, maybe-false 0
- findings:
  - `[low]` `[reject]` Blind: at `CallTimeoutSeconds=90`, two timed-out attempts are 180 s plus prompt build and parse time, so a worst-case run is a few milliseconds over 180 s — real but negligible. The epic AC fixes 90 as the value, and nginx allows 200 s. Narrowing below 90 would contradict the AC.
  - `[low]` `[reject]` Blind: a classic Azure endpoint with no `/openai/v1/` passes the checks and 404s at the first run — real. But `.env.example` documents the v1 path, and the failure surfaces as a Failed run with its HTTP status. A path rule would also refuse legitimate gateway URLs (it adds a guard).
  - `[low]` `[reject]` Blind: the Azure check is presence only, and the log says "verified" — AD-16 specifies credential presence for Azure. A network check contradicts the spine, and the log line accurately reports the provider check passed.
  - `[low]` `[reject]` Blind: Azure `Create()` accepts http, and `Require` runs twice — unreachable. `AiOptionsValidator` and `VerifyAsync` both refuse http, and nothing builds the Azure factory outside `AddActionLedgerAi` today. The fix would restructure the bearer test.
  - `[low]` `[reject]` Blind: failure messages echo the configured URL, which could carry userinfo or a query secret — LM Studio, Ollama and Azure v1 take no credential in the URL (Azure's key travels in its own setting and a header). Sanitizing adds code for a state no documented config reaches.
  - `[low]` `[reject]` Blind: the Ollama probe needs an exact id (`llama3.1:latest`, not `llama3.1`) — real. But the refusal lists the server's ids, and `.env.example` already says "copy its id exactly as /models lists it". Tag matching adds a branch.
  - `[low]` `[reject]` Blind: an empty `/models` list passes and is logged as verified — the spec chose this deliberately (lazy-loading servers). A warning would need a logger in the factory (adds surface).
  - `[low]` `[patch]` Blind: zero-retry is proven only on the probe, never on the chat path — added a `ChatStatus` stub option and a test: `/chat/completions` answers 500 → Failed "attempt 2 of 2" after exactly two chat requests.
  - `[low]` `[patch]` Blind: nothing pins the SDK `NetworkTimeout` margin that DW-17's closure depends on (grouped with the verification-gap finding below) — added a theory (1 s and 90 s budgets) asserting `ChatNetworkTimeout == budget + margin`, margin > 0, and that `OpenAIWire.Options` carries the timeout.
  - `[low]` `[reject]` Blind: LocalOpenAI `Create()` does not re-check that the model is present — `AiOptionsValidator` requires it at every host start. A blank model only arises in hand-built containers, so this guards an undemonstrated state.
  - `[low]` `[reject]` Blind: the free-port helper is duplicated and has a bind-close-reuse race — the window is milliseconds on loopback, and no flake was observed across three full runs. A shared helper or retry-bind is more than a direct fix.
  - `[low]` `[reject]` Blind: "90" is repeated in the `[Range]` message and the docs — an attribute argument cannot interpolate a `const int` into a const string, so the fix is not a direct correction. The value is pinned by `A_call_timeout_of_ninety_seconds_starts` and the 91 row.
  - `[low]` `[patch]` Blind: the csproj comment claimed Architecture.Tests guards every leak, but Rule 3 covers only Domain and Application — the comment now says "if either reaches Domain or Application".
  - `[false]` `[reject]` Blind: `VerifyAsync` has no default implementation — every implementer (all three factories) implements it. The spec asks for the Fake's explicit no-op, and a required member forces each future provider to decide its probe.
  - `[false]` `[reject]` Blind: host-level "no registered factory" coverage was removed — at the host, `AiOptions`' regex admits only the three names and all three are registered, so that path cannot be reached there. It stays covered in the container test (`Nonexistent`).
  - `[low]` `[reject]` Edge: Ollama `:latest` tag matching — same as the Blind finding above; the message lists the ids and `.env.example` says to copy them exactly.
  - `[low]` `[reject]` Edge: model names with surrounding whitespace from an env file — the probe then refuses with the listed ids, which shows the mismatch. A trim rule adds a guard for an undemonstrated state.
  - `[low]` `[reject]` Edge: Azure endpoint without `/openai/v1/` — same as the Blind finding above.
  - `[low]` `[reject]` Edge: a local server with authentication enabled cannot be given a key — LM Studio and Ollama run without auth by default, and the intent names their defaults. An `Ai:LocalOpenAI:ApiKey` key is new configuration surface.
  - `[low]` `[reject]` Edge: userinfo or query in URLs is echoed in messages — same as the Blind finding above.
  - `[low]` `[reject]` Edge: the free-port race in tests — same as the Blind finding above.
  - `[low]` `[reject]` Edge (claim): "a run cannot exceed 180 s" — same as the Blind finding above; milliseconds of overhead, and the value is fixed by the AC.
  - `[low]` `[patch]` Verification gap: the SDK timeout margin is never checked — the theory above was added (grouped with the Blind timeout-margin finding).
  - `[low]` `[patch]` Verification gap: the probe's ProbeTimeout branch and its shutdown rethrow have no tests — added a `ModelsDelay` stub option. Two tests were added: a 60 s hang fails with "did not answer within 10 seconds" in under 15 s; caller cancellation 300 ms into a hang surfaces `OperationCanceledException`, not a provider failure.
  - `[low]` `[patch]` Verification gap: the 200-character model bound was tested only from above — added `A_model_name_at_the_length_bound_starts` (Azure, `ModelMaxLength` chars, `/health` 200).
  - `[false]` `[reject]` Intent: the awaiting-operator close-out is absent from the diff — the spec file was excluded from the review diff by design, and the close-out is the Finalize step of this run, done below.
  - `[low]` `[patch]` Intent: no host-level test shows a successful LocalOpenAI run through the real composition root — added `A_local_provider_run_through_the_host_succeeds`. It starts TestApi with `Ai:Provider=LocalOpenAI` against a loopback stub, the probe passes, and the host's DI-resolved `IActionExtractor` returns Succeeded with the fixture's proposals.
  - `[false]` `[reject]` Intent: Azure's missing-value failure uses a different exception type from the local probe — the epic's "fails the same way" means the host refuses to start with a clear message naming the problem. Both do: `OptionsValidationException` naming the key, and `InvalidOperationException` naming the URL.
  - `[false]` `[reject]` Intent: model-not-listed and `/models` 500 are tested only at the factory — the host-level unreachable test proves the probe is wired into the host, and the variants are branches of that same `VerifyAsync` call.
  - `[false]` `[reject]` Intent: nothing measures a whole run against 180 s — it follows from the extractor's two-attempt loop under the per-call budget, now bounded at 90 and pinned. The two-call count is tested; overhead is covered by the reject above.
  - `[false]` `[reject]` Intent: `DependencyRuleTests` was not modified — it already forbids `OpenAI` and `Microsoft.Extensions.AI` in Domain and Application, and it ran green in the 1,351-test pass.
  - `[false]` `[reject]` Intent: the files touched differ under the epic's shorter wording — `Program.cs` and `ApiOptionsRegistration.cs` are the composition root and configuration, which the epic's "DI registration and configuration" covers.
  - `[false]` `[reject]` Intent: Ollama is documentation only — Ollama is the same OpenAI-compatible path selected by BaseUrl (spine AD-11). The factory is provider-agnostic and the stub tests exercise that wire format.

## Design Notes

**Why a separate probe hosted service instead of a line in `AiStartupCheck`.** The epic AC says the
diff touches only `Infrastructure/Ai/providers`, DI registration, and configuration. The probe lives
in `Providers/` and is registered in `AddActionLedgerAi`, so `AiStartupCheck` stays byte-identical
and the proof point holds literally.

**Why retries are 0 and `NetworkTimeout` is above the budget.** The extractor owns retry-once and the
per-call budget (AD-11). With the SDK defaults (3 retries, 100 s), one "attempt" could hide four HTTP
calls. An `OperationCanceledException` from the SDK's own timeout would also be mislabelled
(DW-17). With both overridden, the budget token is the only thing that cancels.

**Why validate URIs in `AiOptionsValidator`, not only in the factories.** `ValidateOnStart` runs before
any hosted service. A malformed BaseUrl then reads the same way as every other configuration error,
as `OptionsValidationException` naming the key. The Azure factory's own presence check is defence in
depth, for containers built without the Api (tests, the Eval harness).

## Verification

**Commands:**

- `dotnet build ActionLedger.sln -c Release` -- expected: 0 warnings, 0 errors.
- `dotnet test ActionLedger.sln` (Docker running) -- expected: all assemblies green, 0 skipped.
- `git diff --stat 05cad7f20abb141130db3499ba21b785db0919fb -- src` -- expected: only the `src/` paths named in Always.
- `git diff 05cad7f20abb141130db3499ba21b785db0919fb -- src/ActionLedger.Application src/ActionLedger.Domain src/ActionLedger.Web src/ActionLedger.Infrastructure/Ai/ChatClientActionExtractor.cs src/ActionLedger.Infrastructure/Ai/AiStartupCheck.cs prompts fixtures docker-compose.yml src/ActionLedger.Api/appsettings.json` -- expected: empty.

**Manual checks:**

- Confirm that the pinned `Microsoft.Extensions.AI.OpenAI` 10.10.0 honours `AdditionalProperties["strict"]`. The stub test's captured request must show `"strict":true` under `response_format.json_schema`.

## Auto Run Result

Status: awaiting-operator

**Summary of implemented change.** `Ai:Provider=LocalOpenAI` (LM Studio or Ollama) and
`Ai:Provider=AzureOpenAI` now work through the unchanged `ChatClientActionExtractor`. Both are
built on the OpenAI SDK 2.14.0 with `OpenAIClientOptions.Endpoint` and
`Microsoft.Extensions.AI.OpenAI` 10.10.0's `AsIChatClient()`; no Azure SDK is used.

- **Startup probe.** A new `ProviderStartupProbe` hosted service runs after `AiStartupCheck` and
  before the seeder. It refuses to start the host when `GET {BaseUrl}/models` is unreachable,
  answers non-2xx, or does not list the configured model. For Azure it checks that the endpoint,
  model and key are present and that the endpoint is https.
- **Configuration.** `Ai:CallTimeoutSeconds` is bounded to 1–90, so two calls fit inside the
  180-second run ceiling (DW-16). The validator now requires, for the active provider only, an
  absolute http/https URL (https for Azure) and a model name of at most 200 characters (DW-21).
- **SDK behaviour.** SDK retries are off, and the SDK's own timeout sits 10 s above the per-call
  budget, so the extractor's retry-once and budget are the only ones in play (DW-17's SDK path).
- **Wire check.** The `"strict"` key was re-confirmed against the pinned adapter: the captured
  request carries `response_format.json_schema.strict: true`.
- **Diff scope.** The `src/` diff touches only `Infrastructure/Ai/Providers`, the DI registration,
  the composition root, and configuration.

**Files changed.**

- `Directory.Packages.props`, `src/ActionLedger.Infrastructure/ActionLedger.Infrastructure.csproj` — pin and reference OpenAI 2.14.0 and M.E.AI.OpenAI 10.10.0.
- `src/ActionLedger.Infrastructure/Ai/Providers/IChatClientFactory.cs` — adds `VerifyAsync`, and `EndpointHost` for the log line.
- `.../Providers/FakeChatClientFactory.cs` — no-op `VerifyAsync`; remarks now in the past tense.
- `.../Providers/LocalOpenAIChatClientFactory.cs` — the LM Studio/Ollama client and the `/models` probe.
- `.../Providers/AzureOpenAIChatClientFactory.cs` — the Azure v1 client, with the key sent as bearer, and its presence probe.
- `.../Providers/LocalOpenAISettings.cs`, `AzureOpenAISettings.cs` — provider settings records; Azure's `ToString` redacts the key.
- `.../Providers/OpenAIWire.cs` — the shared SDK options: 0 retries, NetworkTimeout = budget + 10 s, and URI parsing.
- `.../Providers/ProviderStartupProbe.cs` — the AD-16 provider probe hosted service.
- `src/ActionLedger.Infrastructure/InfrastructureRegistration.cs` — two keyed factory lines, the settings accessors, and the probe registration.
- `src/ActionLedger.Api/Program.cs` — passes the LocalOpenAI and AzureOpenAI sub-sections.
- `src/ActionLedger.Api/Configuration/AiOptions.cs`, `ApiOptionsRegistration.cs` — `Range(1, 90)`, URL, https and model-length rules.
- `.env.example` — comments only: the Ollama URL, the 1–90 range, and the `/models` requirement.
- `_bmad-output/implementation-artifacts/deferred-work.md` — DW-16 and DW-21 resolved; notes added to DW-17, DW-18 and DW-20.
- `tests/Infrastructure.Tests/OpenAIProviderTests.cs` (new), `AiStartupCheckTests.cs`, `tests/Api.Tests/AiRegistrationTests.cs`, `StartupValidationTests.cs` — loopback-stub coverage of every I/O-matrix row, plus the review additions.

**Review findings breakdown.** 33 findings: high 0, medium 0, low 24, false 9, maybe-false 0.

- **Patched: 6 entries (7 rows), all low.**
  - A chat-path zero-retry test.
  - A timeout-margin theory.
  - Probe-timeout and shutdown-cancellation tests.
  - A 200-character model boundary test.
  - A host-level LocalOpenAI happy-path test.
  - A csproj comment correction.
- **Deferred: none.**
- **Rejected: 26.**
  - 17 lows, each with its reason in the triage log: the 180 s overhead is milliseconds and the AC fixes 90; the Azure v1 path rule; the Azure presence-only wording; Azure http in `Create`; URL userinfo in messages; Ollama tag matching; the empty model list; the Local `Create` model re-check; the test port race; the duplicated "90" literal; model whitespace; auth-enabled local servers — plus 5 duplicate rows across layers.
  - 9 false: detailed in the triage log.

**Follow-up review recommendation.** `false`. No high was patched and no medium was patched
(patched counts: high 0, medium 0, low 6).

**Verification performed.**

- `dotnet build ActionLedger.sln -c Release`: 0 warnings, 0 errors.
- `dotnet test ActionLedger.sln` with Docker running: 1,351 total, 1,351 passed, 0 failed, 0 skipped.
- `git diff --stat 05cad7f -- src`: 13 files, all inside the allowed paths.
- The guard diff over Application, Domain, Web, the extractor, `AiStartupCheck`, `AiSettings`, `AiProviderInfo`, prompts, fixtures, `docker-compose.yml` and `appsettings.json` is empty.
- The strict-key manual check passed through the wire-level stub test.

**Why awaiting-operator.** AC 2 ("a real run on a fixture case returns proposals" against LM
Studio on the developer's Mac) needs a human. LM Studio is installed here, but its only
downloaded model is an 84 MB embedding model (`text-embedding-nomic-embed-text-v1.5`), and its
server was not running. Downloading a multi-gigabyte chat model is the operator's call. An Azure
run likewise needs a real resource key. Everything else is done, and the steps owed are listed
under `operator_actions`.

**Residual risks.**

- Small local models (under about 7B) may not honour `json_schema` strict output; the run then
  fails cleanly after two attempts.
- An Ollama model id must include its `:tag` exactly as `/models` lists it.
- An Azure endpoint missing the `/openai/v1/` path passes startup and fails at the first run with
  HTTP 404.
- DW-18 and DW-20 remain open: SDK error text can quote part of a response body in a persisted
  failure reason, but never a credential.
- The `ChatClientActionExtractor.cs:35-52` remark still says no project references the adapter
  package. It is stale; it was left untouched because the proof point requires that file to stay
  byte-identical.

