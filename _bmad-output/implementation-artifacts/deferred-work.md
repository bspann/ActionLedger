### DW-1: Sign-in has no rate limiting, lockout, or audit log line, so a brute force against POST /api/v1/auth/login is both unlimited and unobservable.
origin: spec-deferred 005dce3a444c
location: src/ActionLedger.Api/Controllers/AuthController.cs
source_spec: `spec-1-4-sign-in-and-receive-a-jwt-list-users.md`
severity: medium
reason: AuthController and SignInHandler contain no throttling and emit no log entry on a refused credential, and no middleware supplies either. The epic's Observability constraint asks for a correlation id on every line, and a failed authentication is the canonical event needing one. Not caused by a defect in this story's code: no acceptance criterion in epics.md or PRD FR-23 requires throttling, and adding it is new product surface rather than a smallest fix. Worth a hardening story before anything beyond the demo.
status: open

### DW-2: The shell has never been exercised in a browser; every assertion about it is a bUnit render or a direct API-chain drive.
origin: spec-deferred aa0b66850fb7
location: src/ActionLedger.Web/Layout/MainLayout.razor
source_spec: `spec-1-6-login-screen-session-shell-and-global-state-patterns.md`
severity: medium
reason: The Api configures no CORS and this story forbids editing src/ActionLedger.Api, so a dev-server page on another origin cannot reach it. The single-origin nginx topology that makes a real browser run possible is Story 1.7. The API chain itself was driven live against a seeded Postgres, and ShellTests/LoginPageTests render the real components, so what is unverified is specifically the browser: MudBlazor's rendered app bar, the fixed progress-bar offset in app.css, focus order, and the keyboard pass AC2 asks for. What would settle it: run the Story 1.7 compose stack and sign in through the browser.
status: open

### DW-3: SessionState.ExpiresAt is stored but never consulted, so an expired token is only discovered by round-tripping a request.
origin: spec-deferred c729227d44a2
location: src/ActionLedger.Web/Core/Auth/SessionState.cs
source_spec: `spec-1-6-login-screen-session-shell-and-global-state-patterns.md`
severity: low
reason: SignIn captures the server's expiresAt and nothing reads it. That is the specified design — the matrix routes expiry through the handler's 401 branch — but it means a session that expired while the tab sat idle looks signed in until the next request, and the first thing the user does after an 8-hour gap fails and bounces them to login. The field is a hook with no consumer. What would settle whether it matters: decide in a later UI story whether the shell should pre-empt expiry rather than react to it.
status: open

### DW-4: A hanging roster call strands a successfully signed-in user on the login form for up to 100 seconds.
origin: review-followup-2026-09-21
location: src/ActionLedger.Web/Features/Auth/LoginPage.razor
source_spec: `spec-1-6-login-screen-session-shell-and-global-state-patterns.md`
severity: medium
reason: SubmitAsync awaits Directory.EnsureLoadedAsync() before NavigateTo(TakeAttemptedRoute() ?? HomeRoute), and ApiClientRegistration constructs its HttpClient without setting Timeout, so the .NET default of 100 seconds applies. The session is already signed in when the await starts, so the user is authenticated but still looking at a disabled Sign in button and a progress bar. The comment above the await claims "awaiting it cannot block the landing" — true of a failure, false of latency, because a swallowed exception still has to arrive before the next statement runs. UserDirectory already anticipates this exact call timing out, so the failure mode was foreseen and its latency consequence was not. Not a defect in the story's acceptance criteria: none of them constrain post-sign-in latency, and nothing renders the roster until Epic 3's owner picker. Uncovered by tests — LoginPageTests gates SignIn (`:153`) but never ListUsers. What would settle it: choose between (a) not awaiting the roster load at all, which matches the spec's own wording "kicks the roster load" and is safe because UserDirectory swallows its failures and leaves IsLoaded false for a later retry, or (b) bounding it with an explicit HttpClient.Timeout or a CancellationTokenSource. (a) is the smaller change.
status: open

### DW-5: Every `<response code="...">` and `<param>` doc comment on a controller action exports as a bare HTTP reason phrase, so the prose that reads as contract documentation is dead.
origin: spec-deferred da69baf07498
location: src/ActionLedger.Api/Controllers/MeetingsController.cs
source_spec: `spec-2-1-meeting-and-immutable-notes-api.md`
severity: low
reason: The exported document gives every response `"description": "Created"`, `"Bad Request"`, `"Not Found"`, `"Conflict"` and emits the `{id}` path parameter with no description; only `[EndpointSummary]` and `[EndpointDescription]` survive the export. Pre-existing rather than caused by this story: `POST /api/v1/auth/login`, `GET /api/v1/users`, `/health`, and `/health/ready` all read the same way, from Stories 1.2 and 1.4. This story roughly triples the volume of that dead prose, which is what made it visible. Settling it means either wiring XML response and parameter documentation into the OpenAPI export or dropping the comments; both are repo-wide decisions, not this story's.
status: open

### DW-6: List paging counts and windows in two separate statements, so a concurrent insert can make a row repeat on two pages or be skipped, despite the total-order claim.
origin: spec-deferred 783ed050413c
location: src/ActionLedger.Application/Meetings/MeetingsQueries.cs
source_spec: `spec-2-1-meeting-and-immutable-notes-api.md`
severity: low
reason: `MeetingsQueries.ListAsync` calls `readDb.CountAsync` and then materializes the windowed query; nothing holds a snapshot across the two. The order is total, so paging is stable against a static table, but not against a concurrent writer. Pre-existing: `UsersQueries` `ListAsync` has the identical shape from Story 1.4, and this story's Code Map directed that it be copied. Settling it means a repeatable-read transaction around both statements or keyset paging, applied to every list endpoint at once rather than to this one.
status: open

### DW-7: A note containing U+0000 satisfies the published contract and both length guards, but PostgreSQL's character types cannot store a NUL byte, so the save may surface as a 500.
origin: spec-deferred 8b1db048ee95
location: src/ActionLedger.Domain/Meetings/MeetingNotes.cs
source_spec: `spec-2-1-meeting-and-immutable-notes-api.md`
reason: `System.Text.Json` deserializes `"\u0000"` into a string containing NUL. `[StringLength(50_000, MinimumLength = 1)]` counts it as one character and `MeetingNotes.RequireText` guards length only, so nothing between the request body and `INSERT` refuses it — while `text` is `character varying(50000)`, and PostgreSQL text types reject NUL with SQLSTATE 22021. The intent says any 1–50,000-character text is accepted and stored byte-for-byte, which is not satisfiable for that one character, so the choice between refusing it with a 400 and transforming it is a product decision rather than a coding one. Not reproduced here: it needs a real database, and the existing round-trip tests cover ASCII whitespace and line endings only. What would settle it: a single `MeetingPersistenceTests` case attaching `"a\u0000b"` against the containerized PostgreSQL — if it throws, decide between a 400 and normalization; if it stores, the intent already holds and only the test is missing.
status: open

### DW-8: MeetingsService's DI registration in Program.cs is never executed by any test, so a registration that was removed or given the wrong lifetime would not fail the build.
origin: spec-deferred 4849e7a099bf
location: src/ActionLedger.Web/Program.cs:25
source_spec: `spec-2-2-meeting-list-new-meeting-dialog-and-notes-paste-area.md`
severity: low
reason: Nothing in the suite runs `Program.cs`; every Web.Tests fixture registers the service into its own bUnit container instead. Not caused by this story: `AuthService` has carried the identical gap since Story 1.6, and `ApiClientRegistrationTests` covers `AddActionLedgerApiClient` only. Settling it means giving the composition root a shape a test can resolve against, which is a change to `Program.cs` and to how every feature registers itself rather than to this story's code.
status: open

### DW-9: The generated DateFormatConverter parses and writes meeting dates with the browser's culture, so a non-Gregorian calendar reads and sends the wrong year or fails outright.
origin: spec-deferred f37743469584
location: src/ActionLedger.Web/Core/Api/ActionLedgerApiClient.g.cs:1769 (consumed at src/ActionLedger.Web/Features/Meetings/Data/MeetingsService.cs:162,169)
source_spec: `spec-2-2-meeting-list-new-meeting-dialog-and-notes-paste-area.md`
severity: high
reason: Verified by probe under CurrentCulture, against the real converter's two lines (DateTimeOffset.Parse(dateTime) and value.ToString("yyyy-MM-dd"), both without a format provider): th-TH the server's "2026-09-21" parses to 1483-09-21 and MeetingsService maps it to DateOnly(1483, 9, 21); an outbound 2026-10-03 is written as "2569-10-03". ar-SA DateTimeOffset.Parse("2026-09-21") throws FormatException: "String '2026-09-21' was not recognized as a valid DateTime." The FormatException is raised inside JSON deserialization, so it is not an ApiException, an HttpRequestException, or an OperationCanceledException — the three arms MeetingsService.CallAsync catches. It escapes the seam as an unhandled component exception and takes the Meeting List and Meeting Detail to the Blazor error UI. Not caused by this story: the converter is generated code from Story 2.1's contract and is off-limits here, and the app sets no culture policy at all (no DefaultThreadCurrentCulture anywhere in src/). The seam
status: open

### DW-10: The title link's @onclick:preventDefault cannot be observed by bUnit, so removing it degrades every title click to a full page reload with the suite still green.
origin: spec-deferred 047e16aa7955
location: src/ActionLedger.Web/Features/Meetings/MeetingListPage.razor:78
source_spec: `spec-2-2-meeting-list-new-meeting-dialog-and-notes-paste-area.md`
severity: medium
reason: MeetingListPageTests.Clicking_the_title_link_navigates_exactly_once asserts Assert.Single(Navigation.History), which covers the handler and stopPropagation — remove either and the count goes to zero or two. Nothing is sensitive to preventDefault, because BunitNavigationManager never follows an anchor's default action. Delete the attribute and both link tests pass unchanged. In a browser the click falls through to the href as a document navigation, which reboots the WebAssembly runtime; the spec's own Design Notes record that SessionState holds the token in memory only, so that reload signs the user out. Only a real browser can observe a default action. tests/Web.E2E is still the wiring placeholder AD-18 reserves for Playwright — its single test asserts an assembly name — so this belongs with that suite rather than with this story.
status: open

### DW-11: The fixture catalog holds 31 expected actions, not the 50 to 70 the PRD addendum's threshold reasoning assumes, so one missed action moves recall about 3.2 points rather than the 1.5 to 2 the 0.80
origin: spec-deferred 3677d1db7002
location: fixtures/extraction/ (all fourteen .expected.json files)
source_spec: `spec-2-3-fixture-catalog-and-prompt-v1-saturday-evening.md`
severity: medium
reason: Counted across the fourteen answer files: 6+5+3+2+2+2+2+2+1+2+0+0+2+2 = 31. The addendum at prds/prd-ActionLedger-2026-09-19/addendum.md:18 reasons "Twelve to fifteen notes with four to six actions each give roughly 50 to 70 expected actions. One missed action moves recall by about 1.5 to 2 points. A threshold of 0.80 tolerates 10 to 14 misses across the set." At 31 actions that same threshold tolerates 6 misses, so the gate is materially tighter than the published rationale describes. Not caused by a defect in this story: the per-case counts follow the epic's category spread (4/2/2/2/2/1/1) and the three seed cases follow the PRD storyline exactly, and both are pinned by FixtureCatalogTests. Raising the count means enriching cases with more commitments, which changes the demo's seeded meetings and the Gate's ground truth together. What would settle it: Story 6.2 authors thresholds.json against this catalog. Either restate the addendum's rationale for 31 actions, or enrich the non-seed
status: open

### DW-12: The DW-11 entry in the deferred-work ledger is truncated mid-sentence in both its heading and its reason, losing the metric it is measured against and the action it proposes.
origin: spec-deferred df12f27141c2
location: _bmad-output/implementation-artifacts/deferred-work.md:80,85
source_spec: `spec-2-3-fixture-catalog-and-prompt-v1-saturday-evening.md`
severity: medium
reason: deferred-work.md:80 ends "...rather than the 1.5 to 2 the 0.80" with no noun and no period, where this spec's own deferred block reads "...the 0.80 recall threshold was derived from". The reason at :85 ends "...or enrich the non-seed", where the spec continues "enrich the non-seed cases before the first baseline run. The addendum's own 'Revisit rule' already requires a written rationale for any threshold move." Both truncations were verified by reading the two files side by side. DW-11 is the entry Story 6.2 picks up when it authors thresholds.json, so as filed it loses both halves of what makes it actionable. Not repaired here: the deferred-work ledger is the orchestrator's to own, and this run was instructed not to modify, re-open or rewrite existing ledger entries. Repairing DW-11's two lines from this spec's frontmatter is a mechanical copy the orchestrator can make.
status: open

### DW-13: Every notes body uses one sentence shape, so the Golden Set measures a narrow slice of the formats the paste area accepts and will not discriminate between providers.
origin: spec-deferred 38fe2d6113f6
location: fixtures/extraction/ (all fourteen .md files)
source_spec: `spec-2-3-fixture-catalog-and-prompt-v1-saturday-evening.md`
severity: medium
reason: All fourteen bodies are context paragraph, then one commitment per line, then a closing paragraph, and every extractable sentence is "<Display Name> will <verb> ... by YYYY-MM-DD." or its explicit no-owner/no-date variant. Nothing exercises bullet lists, speaker-prefixed transcript text, an owner named mid-sentence or by pronoun, a commitment spanning two sentences, a table, or noisy pasted text. Not caused by a defect in this story: the per-case counts and categories follow the epic's spread (4/2/2/2/2/1/1) and the three seed cases follow the PRD storyline exactly. What would settle it: the same Story 6.2 threshold conversation that DW-11 opens. Format diversity and action volume are two halves of one question about what the Gate measures, and both change the demo's seeded meetings and the ground truth together.
status: open

### DW-14: No fixture exercises an extracted owner that resolves to nobody, though AD-9's OwnerResolver must handle exactly that case.
origin: spec-deferred 53e6c178fb88
location: fixtures/extraction/ (all fourteen .expected.json files)
source_spec: `spec-2-3-fixture-catalog-and-prompt-v1-saturday-evening.md`
severity: medium
reason: Every_suggested_owner_resolves_through_the_roster and Every_suggested_owner_attended_its_own_meeting together require every non-empty owner to be a roster display name or alias who was in the room, so a vendor, a visitor or a misspelling never appears. roster.json carries ten aliases of which one (P. Ram) is used anywhere in the catalog, and no case uses the "plainly invented names" in attendees that this spec explicitly permits. Not caused by a defect in this story: the catalog's categories come from the epic's spread, which has no unresolvable-owner category. What would settle it: Story 6.2 decides how the Gate scores an owner that matches no User. Adding such a case before that decision would pin ground truth the scorer has no rule for.
status: open

### DW-15: The catalog's front-matter split and trigram tokenizer are pinned only in the test assembly, so Stories 2.4 and 6.2 could implement either differently without any test noticing.
origin: spec-deferred 3838c5240316
location: tests/Architecture.Tests/FixtureCatalogTests.cs (SplitFrontMatter, Trigrams)
source_spec: `spec-2-3-fixture-catalog-and-prompt-v1-saturday-evening.md`
reason: FixtureCatalogTests.SplitFrontMatter uses the regex \A---\r?\n(?<front>.*?)^---[ \t]*\r?\n and Trigrams splits on whitespace keeping punctuation, compared OrdinalIgnoreCase. Story 2.4's ExcerptVerifier and Story 6.2's injection scorer will each implement their own; nothing binds them to these, and only a sentence of prose in fixtures/extraction/README.md describes the intended split. Unverified because both consumers are unwritten: if 2.4 trims the body differently, or 6.2 strips punctuation before tokenizing, the catalog can satisfy every assertion here and still behave differently at runtime. No near-miss exists in the current content — every excerpt is an interior single-line sentence and no legitimate action is close to a shared trigram — so this is coupling rather than a live failure today. What would settle it: when 2.4 and 6.2 land, assert their loaders against this catalog rather than re-deriving the rules, or lift the split and tokenizer into one shared place both read.
status: open

### DW-16: `Ai:CallTimeoutSeconds` accepts up to 600, so two provider calls can spend 1,200 seconds against the 180-second run ceiling NFR-1 states and the extractor's own comment claims to enforce.
origin: spec-deferred 72a7d241326b
location: src/ActionLedger.Api/Configuration/AiOptions.cs
source_spec: `spec-2-4-extraction-seam-output-validation-and-the-fake-provider.md`
severity: low
reason: `src/ActionLedger.Api/Configuration/AiOptions.cs` carries `[Range(1, 600)]` on `CallTimeoutSeconds`, and `ChatClientActionExtractor` bounds each call by that value with no whole-run deadline. The `[Range]` predates this story, and at the shipped default of 90 two calls are 180 seconds exactly, so nothing is wrong today. It becomes reachable when Story 2.7 wires a provider that can actually spend the budget. What would settle it: either narrow the option's range to what two calls may spend inside 180 seconds, or give the retry loop a whole-run deadline in addition to the per-call one.
resolution: Story 2.7 (`spec-2-7-real-providers-through-the-same-seam-lm-studio-ollama-and-az.md`) narrowed `AiOptions.CallTimeoutSeconds` to `[Range(1, 90)]`, so two calls always fit inside 180 s; `StartupValidationTests` pins 91 as a startup failure.
status: resolved

### DW-17: Any `OperationCanceledException` the provider raises for its own reasons is persisted and shown to a human as a budget timeout, because the timeout token is scoped inside `CallAsync`.
origin: spec-deferred 324c1dee3420
location: src/ActionLedger.Infrastructure/Ai/ChatClientActionExtractor.cs
source_spec: `spec-2-4-extraction-seam-output-validation-and-the-fake-provider.md`
severity: low
reason: `ChatClientActionExtractor.ExtractAsync` catches `OperationCanceledException` unfiltered after the caller-cancellation case and writes "The provider did not answer within Ai:CallTimeoutSeconds". The `CancellationTokenSource` that would distinguish a budget expiry lives in `CallAsync` and is disposed before the catch runs. Unreachable today: the Fake throws nothing, and it is the only registered provider. It arrives with Story 2.7's HTTP clients, whose internal timeouts surface as `TaskCanceledException`. What would settle it: catch inside `CallAsync`, or surface the timeout token so the two causes are distinguishable.
note: Story 2.7 (`spec-2-7-real-providers-through-the-same-seam-lm-studio-ollama-and-az.md`) closed the SDK-timeout path by construction: both real factories set `NetworkTimeout` to `Ai:CallTimeoutSeconds + 10 s` and `ClientRetryPolicy(maxRetries: 0)`, so the extractor's budget token always fires first and an SDK-internal timeout cannot be the cancellation that reaches the catch. The extractor is unchanged, so a provider that raises `OperationCanceledException` for some other reason would still be mislabelled.
status: open

### DW-18: A provider exception's `Message` is interpolated verbatim into the persisted failure reason, which the class doc three lines above promises will name what went wrong rather than what the call carried.
origin: spec-deferred 3cd28c9be1ba
location: src/ActionLedger.Infrastructure/Ai/ChatClientActionExtractor.cs
source_spec: `spec-2-4-extraction-seam-output-validation-and-the-fake-provider.md`
severity: low
reason: `ChatClientActionExtractor` builds the reason as `$"...: {exception.GetType().Name}: {exception.Message}"`. The extractor controls its own strings but not an SDK's, and HTTP client exceptions can carry a request URI or a response excerpt. Nothing reaches that string today because the Fake throws nothing. What would settle it: when Story 2.7 lands, decide whether to truncate or allowlist what is taken from an exception before it is persisted and rendered.
note: Story 2.7 (`spec-2-7-real-providers-through-the-same-seam-lm-studio-ollama-and-az.md`) wired the real OpenAI SDK. Its exception messages carry no credential (the api key travels in the Authorization header, never in the message), but a `ClientResultException` message can include the response body excerpt, so the truncate-or-allowlist decision is still open.
status: open

### DW-19: `PromptCatalog`'s numeric version ordering is never exercised with more than one version, so replacing it with a string sort would leave every test green until a `v10` lands beside a `v9`.
origin: spec-deferred dca494d0d35f
location: src/ActionLedger.Infrastructure/Ai/PromptCatalog.cs
source_spec: `spec-2-4-extraction-seam-output-validation-and-the-fake-provider.md`
severity: low
reason: Only `prompts/extract-actions.v1.md` is embedded, so `Versions` is a one-element list and `Current == Versions[^1]` holds trivially. `PromptCatalog` reads this assembly's own manifest resources through the static `EmbeddedContent`, so closing this needs either a seam for the resource source or a second embedded prompt file. What would settle it: add the assertion when a second prompt revision exists, which is Story 7.1's territory.
status: open

### DW-20: `ExtractionResult.Failed` does not refuse a blank reason, and Story 2.5 is the first code to turn one into a 409 on the path AD-11 requires to answer 201.
origin: spec-deferred e91bd47ec986
location: src/ActionLedger.Application/Ai/ExtractionResult.cs:130
source_spec: `spec-2-5-extraction-run-and-proposals-persisted-with-ai-proposal-revi.md`
reason: `src/ActionLedger.Application/Ai/ExtractionResult.cs:130` builds `Failed(reason, metrics)` with no guard on `reason`. `ExtractionRun.Start` refuses a Failed run whose reason is blank (`src/ActionLedger.Domain/Extraction/ExtractionRun.cs`, `RequireReasonMatchesOutcome`), so a blank reason becomes a `DomainRuleException` and the controller answers 409 instead of the 201-with-Outcome-Failed that AD-11 fixes. Unreachable today: every reason `ChatClientActionExtractor` builds is a non-empty interpolation, and the Fake is the only registered provider. `ExtractionResult` is Story 2.4's file, so the missing guard predates this story; 2.5 is only the first consumer. What would settle it: when Story 2.7 wires a provider whose exception message can be empty, decide whether `Failed` rejects a blank reason or the aggregate substitutes a placeholder rather than throwing.
note: Story 2.7 (`spec-2-7-real-providers-through-the-same-seam-lm-studio-ollama-and-az.md`) wired the real OpenAI SDK; the extractor's reason is always a non-empty interpolation (`attempt N of 2: TypeName: ...`) even when the SDK's message is empty, and the SDK's messages carry no credential. The missing guard on `ExtractionResult.Failed` remains.
status: open

### DW-21: `AiOptions`' two provider model names carry no length bound mirroring `ExtractionRunMetadata.ModelMaxLength`, so an over-long operator-supplied model turns every run into a 409 with no row.
origin: spec-deferred 4c0253d549b8
location: src/ActionLedger.Api/Configuration/AiOptions.cs
source_spec: `spec-2-5-extraction-run-and-proposals-persisted-with-ai-proposal-revi.md`
severity: medium
reason: `AiOptions.LocalOpenAI.Model` and `AiOptions.AzureOpenAI.Model` are free strings with no `[StringLength]`, while `ExtractionRunMetadata.Validated()` throws `DomainRuleException` for a blank or over-200-character model — and `ApiExceptionHandler` maps that to 409, after the provider call, with no `ExtractionRun` persisted. That is the outcome AD-11 exists to prevent, and it would fail on every run rather than once. Unreachable today: the Fake is the only registered provider and supplies `fixture-catalog`, its own constant, so nothing operator-supplied reaches the guard. `AiOptions` is Story 2.4's file, so the missing bound predates this story; 2.5 is only the first code that turns it into a status code. What would settle it: when Story 2.7 wires LM Studio, Ollama and Azure OpenAI, decide whether the options bind with `[StringLength]` tied to the Domain constants and fail at startup (with the constant-agreement test `ProposedActionShapeTests` already models for the validator bounds), or
resolution: Story 2.7 (`spec-2-7-real-providers-through-the-same-seam-lm-studio-ollama-and-az.md`) made `AiOptionsValidator` refuse an active provider's model longer than `ExtractionRunMetadata.ModelMaxLength` at startup, naming the key; `StartupValidationTests` pins a 201-character model.
status: resolved
