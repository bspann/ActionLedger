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
