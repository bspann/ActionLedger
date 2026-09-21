# Review: Version currency and reality check

- Reviewer lens: every committed decision web-researched or reality-checked, not asserted from training data.
- Artifacts: ARCHITECTURE-SPINE.md (Stack table, AD-11, AD-17, Consistency Conventions), ARCHITECTURE.md, .memlog.md
- Date: 2026-09-20

## Verdict

The memlog-backed rows hold up, but the three rows the memlog admits it did not verify are wrong or under-specified, and one of them (TypeScript 5.9) would break the Angular 22 scaffold on day one; fix those, drop the stale Azure.AI.OpenAI row, and tighten the AD-11 schema-equivalence claim, then the spine is current.

## Coverage

| | Count |
| --- | --- |
| Stack rows | 24 |
| Covered by memlog and independently re-verified today | 18 |
| Covered by memlog, not independently re-fetched (consistent with runtime) | 1 (Microsoft.AspNetCore.OpenApi) |
| Not covered by memlog, verified in this review | 5 (Serilog, TypeScript, Node, nginx, Ollama) |
| Rows needing a correction | 5 (TypeScript, Serilog, Node, Ollama, Azure.AI.OpenAI) |

## Verification table

| Item | Spine says | Verified | Source | Action needed |
| --- | --- | --- | --- | --- |
| .NET SDK (LTS) | 10.0.12 | Yes. Runtime 10.0.12, SDK 10.0.401, released 2026-09-08, LTS to 2028-11-14 | https://dotnet.microsoft.com/en-us/download/dotnet/10.0 ; https://endoflife.date/dotnet | Optionally state SDK 10.0.401 alongside runtime 10.0.12 |
| ASP.NET Core Web API | 10.0.12 | Yes. ASP.NET Core Runtime 10.0.12 in the same drop | https://dotnet.microsoft.com/en-us/download/dotnet/10.0 | None |
| Microsoft.AspNetCore.OpenApi | 10.0.12 | Memlog only. Ships in lockstep with the runtime; not re-fetched | .memlog.md (nuget.org) | None; low risk |
| Swashbuckle.AspNetCore.SwaggerUI | 10.2.3 | Yes. 10.2.3, 2026-06-22, MIT | https://www.nuget.org/packages/Swashbuckle.AspNetCore.SwaggerUI | None |
| Microsoft.EntityFrameworkCore | 10.0.12 | Yes. 10.0.12, 2026-09-08 | https://www.nuget.org/packages/Microsoft.EntityFrameworkCore | None |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 | Yes. 10.0.3, 2026-07-10, depends EF Core >=10.0.4 <11 | https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL | None (11.0.0-rc.1 exists; stay on 10.0.3) |
| PostgreSQL (postgres:18-alpine) | 18.6 | Yes. Tag `18-alpine` = 18.6 on Alpine 3.24; 19 is beta3 | https://hub.docker.com/_/postgres | None |
| Microsoft.Extensions.AI | 10.10.0 | Yes. 10.10.0, 2026-09-09 | https://www.nuget.org/packages/Microsoft.Extensions.AI | None |
| Microsoft.Extensions.AI.OpenAI | 10.10.0 | Yes. 10.10.0, 2026-09-09, depends OpenAI >=2.13.0; description says "OpenAI-compatible endpoints" | https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI | None |
| OpenAI .NET SDK, custom `Endpoint` | 2.14.0 | Yes. 2.14.0, 2026-09-15, MIT. README documents `OpenAIClientOptions.Endpoint` for OpenAI-compatible APIs | https://www.nuget.org/packages/OpenAI ; https://github.com/openai/openai-dotnet/blob/main/README.md | None |
| Azure.AI.OpenAI (optional) | 2.1.0 | Stale. 2.1.0 is from 2024-12-06 and pins OpenAI >=2.1.0; latest is 2.9.0-beta.1 (2026-03-13). Microsoft's current .NET guidance for Azure OpenAI uses the `OpenAI` package against the `/openai/v1/` endpoint and does not reference Azure.AI.OpenAI at all | https://www.nuget.org/packages/Azure.AI.OpenAI ; https://learn.microsoft.com/en-us/azure/foundry/openai/supported-languages ; https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/structured-outputs | Drop the row. Make the AzureOpenAI factory = OpenAI SDK + `Endpoint = https://<resource>.openai.azure.com/openai/v1/` + api-key or `BearerTokenPolicy`. Resolves the memlog open question |
| Serilog.AspNetCore | 9.0.0 | No. Latest stable is 10.0.0, 2025-11-28, Apache-2.0 | https://www.nuget.org/packages/Serilog.AspNetCore | Change to 10.0.0 |
| xunit.v3 | 4.0.1 | Yes. 4.0.1, 2026-09-12, Apache-2.0 | https://www.nuget.org/packages/xunit.v3 | None |
| Testcontainers.PostgreSql | 4.15.0 | Yes. 4.15.0, 2026-09-06, MIT | https://www.nuget.org/packages/Testcontainers.PostgreSql | None |
| NetArchTest.eNhancedEdition | 1.4.5 | Yes. 1.4.5, 2025-06-04, MIT, not deprecated; net10 compatible | https://www.nuget.org/packages/NetArchTest.eNhancedEdition | None |
| Microsoft.Playwright | 1.62.0 | Yes. 1.62.0, 2026-08-11 | https://www.nuget.org/packages/Microsoft.Playwright | None |
| Angular | 22.1.7, zoneless and standalone by default | Yes. npm latest 22.1.7; engines `^22.22.3 || ^24.15.0 || >=26.0.0`. CLI application schema: `zoneless` default true, `standalone` default true; angular.dev: "Zoneless is the default in Angular v21+" | https://registry.npmjs.org/@angular/core/latest ; https://github.com/angular/angular-cli/blob/main/packages/schematics/angular/application/schema.json ; https://angular.dev/guide/zoneless ; https://angular.dev/cli/new | None |
| Angular Material | 22.1.7 | Yes. 22.1.7, peer `@angular/core ^22.0.0 || ^23.0.0` | https://registry.npmjs.org/@angular/material/latest | None |
| TypeScript (strict) | 5.9 | No. Angular 22.0.x requires TypeScript `>=6.0.0 <6.1.0`. Highest 6.0.x is 6.0.3. npm `latest` is 7.0.2, which Angular 22 does not accept | https://angular.dev/reference/versions ; https://unpkg.com/typescript@6/package.json ; https://registry.npmjs.org/-/package/typescript/dist-tags | Change to 6.0.3 and pin `~6.0.3`; never `typescript@latest` |
| Node (LTS) | 22 | Partly. Node 22 is Maintenance LTS (security only until 2027-04-30); Angular 22 needs >=22.22.3. Node 24 is Active LTS (24.21.0), enters maintenance 2026-10-20; Node 26 becomes LTS in October 2026 | https://endoflife.date/nodejs ; https://nodejs.org/en/blog/release/v26.0.0 ; https://angular.dev/reference/versions | Change to Node 24 LTS (24.21.0). If 22 stays, write 22.23.2 and note the >=22.22.3 floor |
| nginx (nginx:stable-alpine) | stable | Yes. `stable-alpine` = 1.30.5; mainline 1.31.6 | https://hub.docker.com/_/nginx | Write 1.30.5 in the table; consider pinning `nginx:1.30-alpine` |
| LM Studio | 0.4.25, json_schema response_format | Yes. 0.4.25 released 2026-09-19. Structured output docs show `response_format: { type: "json_schema", json_schema: { name, strict, schema } }` on `/v1/chat/completions`; caveat: models under ~7B may not honor it | https://lmstudio.ai/changelog/lmstudio/lmstudio-v0.4.25 ; https://lmstudio.ai/docs/app/api/structured-output | Add the model-size caveat to DEMO.md or the eval notes |
| Ollama (alternate, OpenAI `/v1`) | current | Version filled: v0.34.2 stable, 2026-09-15 (v0.34.3-rc1 pre-release). Structured-outputs page states "Structured outputs work through the OpenAI-compatible API via `response_format`"; the compatibility page lists only "JSON mode" | https://github.com/ollama/ollama/releases ; https://docs.ollama.com/capabilities/structured-outputs ; https://docs.ollama.com/api/openai-compatibility | Write 0.34.2; add a one-request smoke check of json_schema at scaffold time |
| azure/container-apps-deploy-action | v2 | Yes. v2 is the newest major tag | https://github.com/Azure/container-apps-deploy-action/releases | None |

### Fit claims

| Claim | Verified | Source | Action needed |
| --- | --- | --- | --- |
| (a) Angular 22 supported Node versions; `ng new` defaults zoneless and standalone | Yes. Node `^22.22.3 || ^24.15.0 || ^26.0.0`; TS `>=6.0.0 <6.1.0`; CLI schema defaults `zoneless: true`, `standalone: true` | angular.dev/reference/versions ; angular-cli application/schema.json | Fix TypeScript and Node rows (above) |
| (b) `OpenAIClientOptions.Endpoint` on OpenAI 2.14 works with M.E.AI.OpenAI 10.10 `AsIChatClient()` | Yes. `AsIChatClient` wraps any `ChatClient`; the package targets "OpenAI-compatible endpoints"; M.E.AI.OpenAI 10.10.0 requires OpenAI >=2.13.0, so 2.14.0 is in range | nuget Microsoft.Extensions.AI.OpenAI ; openai-dotnet README ; learn.microsoft.com OpenAIClientExtensions.AsIChatClient | None |
| (c) `ChatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema(...)` is sent as `response_format` json_schema | Yes. `OpenAIChatClient.ToOpenAIChatResponseFormat` maps `ChatResponseFormatJson` to `OpenAI.Chat.ChatResponseFormat.CreateJsonSchemaFormat(name, schema, description, strict)`. Two details: the schema is first run through `StrictSchemaTransformCache` (RequireAllProperties, DisallowAdditionalProperties, ConvertBooleanSchemas, MoveDefaultKeywordToDescription, and unsupported keywords such as `pattern`, `minimum`, `maxLength` moved into `description`); and `strict` is read from `ChatOptions.AdditionalProperties["strict"]`, null when unset | https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIChatClient.cs ; https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIClientExtensions.cs | Say in AD-11 that the wire schema is the transformed schema, and set `AdditionalProperties["strict"] = true` explicitly |
| JsonSchemaExporter exists in .NET 10 and what it expresses | Yes. Introduced in .NET 9, present in .NET 10 (`System.Text.Json.Schema`). Emits `type`, `properties`, `required` (from constructor/required members), nullability as `["type","null"]`, `additionalProperties: false` when `UnmappedMemberHandling.Disallow`, `enum` arrays for string enums, `pattern` only for numbers-as-strings, `default`, `description` via `TransformSchemaNode`. It does not consult data annotations and never emits `minLength`, `maxLength`, `minimum`, `maximum`, or `format` | https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/extract-schema ; https://learn.microsoft.com/en-us/dotnet/api/system.text.json.schema.jsonschemaexporter?view=net-10.0 ; https://github.com/dotnet/runtime/blob/main/src/libraries/System.Text.Json/src/System/Text/Json/Schema/JsonSchemaExporter.cs | Define "equivalent" in AD-11 (see M-1) |
| ARCHITECTURE.md: "response_format json_schema, which LM Studio, Ollama, and Azure OpenAI all accept" | LM Studio: yes. Azure OpenAI: yes on Chat Completions with the v1 API; strict mode rejects `minLength`, `maxLength`, `pattern`, `format`, `minimum`, `maximum`, `minItems`, `maxItems` and requires all fields required plus `additionalProperties: false`. Ollama: documented in one sentence only | lmstudio.ai structured-output ; learn.microsoft.com structured-outputs ; docs.ollama.com structured-outputs | Keep; add the Azure keyword restrictions to AD-11 and smoke-test Ollama |
| ARCHITECTURE.md / memlog: containers reach LM Studio at `host.docker.internal:1234` | Docker Desktop resolves the name automatically; on Linux engines it needs `extra_hosts: ["host.docker.internal:host-gateway"]` | https://docs.docker.com/reference/cli/docker/container/run/#add-host | Add the `extra_hosts` line to the compose spec in AD-17 so Linux CI and ACA-adjacent Linux hosts behave the same |

## Findings by severity

### High

- **H-1 TypeScript 5.9 breaks Angular 22.** Angular 22.0.x requires TypeScript >=6.0.0 <6.1.0. A 5.9 pin fails `ng new` / `ng build` peer checks. Change the Stack row to TypeScript 6.0.3 (`~6.0.3`) and note that npm `latest` is 7.0.2, which Angular 22 rejects, so `typescript@latest` must never be used. Sources: angular.dev/reference/versions; unpkg typescript@6.
- **H-2 Serilog.AspNetCore 9.0.0 is one major behind.** Current stable is 10.0.0 (2025-11-28, Apache-2.0), which is the line built for .NET 10. Change the row to 10.0.0. Source: nuget.org/packages/Serilog.AspNetCore.

### Medium

- **M-1 AD-11's "JsonSchemaExporter output is equivalent to the committed schema file" is under-specified and, as written, likely false.** The exporter cannot emit `minLength`, `maxLength`, `minimum`, `maximum`, or `format` (date), and it emits nullable types as `["string","null"]` unless `TreatNullObliviousAsNonNullable` is set. If the committed `extract-actions.schema.json` carries the length, range, and date-format constraints that `ExtractionOutputValidator` enforces, the test will never pass. Fix: keep the committed file to the structural subset (type, properties, required, enum, additionalProperties), or have the test compare after stripping constraint keywords; use `TreatNullObliviousAsNonNullable = true` and `UnmappedMemberHandling.Disallow` in the exporter options; state that the validator, not the schema, owns lengths, ranges, and the `YYYY-MM-DD` check. This also matches Azure OpenAI strict mode, which rejects those keywords outright.
- **M-2 The wire schema is not the committed file.** M.E.AI.OpenAI transforms the schema (all properties required, `additionalProperties: false`, unsupported keywords moved to `description`) before sending it, and `strict` is only set when `ChatOptions.AdditionalProperties["strict"]` is true. AD-11 should say the transformed schema goes on the wire and that the extractor sets `strict = true` explicitly, so the provider behaviour is deterministic across LM Studio, Ollama, and Azure.
- **M-3 Azure.AI.OpenAI 2.1.0 is stale and no longer Microsoft's documented path.** The stable package is 21 months old, pins OpenAI >=2.1.0, and Microsoft's current .NET samples for Azure OpenAI use the `OpenAI` package with `Endpoint = .../openai/v1/`. The memlog already lists the pairing with M.E.AI.OpenAI 10.10.0 as unverified. Remove the row, make the AzureOpenAI factory the OpenAI SDK plus Endpoint plus api-key or `BearerTokenPolicy`, and close the memlog question. Config keys `Ai:AzureOpenAI:Endpoint`, `Ai:AzureOpenAI:Deployment`, `Ai:AzureOpenAI:ApiKey` still fit (deployment name is the `model`).
- **M-4 Node 22 is Maintenance LTS.** Node 24 is the Active LTS (24.21.0) and Angular 22 accepts `^22.22.3 || ^24.15.0 || ^26.0.0`. Recommend Node 24 for the web build image and CI. Note the calendar: Node 24 moves to maintenance on 2026-10-20 and Node 26 becomes LTS the same month, so the row should carry an exact version and a revisit date.

### Low

- **L-1 Ollama row says "current".** Fill in v0.34.2 (2026-09-15). Ollama's structured-outputs doc confirms `response_format` works on the OpenAI-compatible API, but the compatibility matrix only lists "JSON mode"; add a one-request json_schema smoke check when the LocalOpenAI factory is first pointed at Ollama.
- **L-2 nginx row says "stable".** `nginx:stable-alpine` is 1.30.5 today; write the number and consider `nginx:1.30-alpine` so a stable-branch bump does not silently change the image.
- **L-3 `host.docker.internal` is Docker Desktop behaviour.** On a Linux engine (GitHub-hosted runners for eval.yml with `LOCAL_AI_BASE_URL`, or any Linux dev box) it needs `extra_hosts: ["host.docker.internal:host-gateway"]` on the `api` service. Add it to the AD-17 compose description.
- **L-4 LM Studio structured output has a model-size caveat.** LM Studio documents that models under about 7B parameters may not honor json_schema; the retry-once policy covers occasional misses, but DEMO.md should name a model that is known to work with the Golden Set.
- **L-5 Microsoft.AspNetCore.OpenApi 10.0.12 not independently re-fetched.** It ships with the shared framework at the runtime version, so the memlog value is almost certainly right; noting for completeness.

### Confirmed with no action

.NET 10.0.12 (LTS), EF Core 10.0.12, Npgsql EF 10.0.3, postgres:18-alpine = 18.6, Microsoft.Extensions.AI and .OpenAI 10.10.0, OpenAI 2.14.0 with `OpenAIClientOptions.Endpoint`, Swashbuckle.AspNetCore.SwaggerUI 10.2.3 (MIT), xunit.v3 4.0.1, Testcontainers.PostgreSql 4.15.0 (MIT), NetArchTest.eNhancedEdition 1.4.5 (MIT, not deprecated), Microsoft.Playwright 1.62.0, Angular and Material 22.1.7 with zoneless and standalone defaults, LM Studio 0.4.25 with json_schema, azure/container-apps-deploy-action v2, JsonSchemaExporter present in .NET 10, `ForJsonSchema` mapped to `response_format` json_schema by the OpenAI adapter, Azure OpenAI structured outputs on Chat Completions v1.
