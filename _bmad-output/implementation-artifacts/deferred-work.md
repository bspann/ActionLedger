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
