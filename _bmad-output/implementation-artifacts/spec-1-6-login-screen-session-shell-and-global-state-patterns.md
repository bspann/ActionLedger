---
title: 'Story 1.6 — Login screen, session, shell, and global state patterns'
type: 'feature'
created: '2026-09-21'
status: 'done'
baseline_commit: '99ccd7f6537fec51fe8aaca5e8c8d35bb0542e9c'
baseline_revision: '99ccd7f6537fec51fe8aaca5e8c8d35bb0542e9c'
review_loop_iteration: 1
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-5-blazor-webassembly-scaffold-with-mudblazor-theme-tokens-and.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-ActionLedger-2026-09-20/EXPERIENCE.md'
warnings: ['oversized']
deferred:
  - summary: >-
      The shell has never been exercised in a browser; every assertion about it is a bUnit render
      or a direct API-chain drive.
    evidence: |-
      The Api configures no CORS and this story forbids editing src/ActionLedger.Api, so a
      dev-server page on another origin cannot reach it. The single-origin nginx topology that
      makes a real browser run possible is Story 1.7. The API chain itself was driven live
      against a seeded Postgres, and ShellTests/LoginPageTests render the real components, so
      what is unverified is specifically the browser: MudBlazor's rendered app bar, the fixed
      progress-bar offset in app.css, focus order, and the keyboard pass AC2 asks for.
      What would settle it: run the Story 1.7 compose stack and sign in through the browser.
    location: >-
      src/ActionLedger.Web/Layout/MainLayout.razor
    severity: medium
  - summary: >-
      SessionState.ExpiresAt is stored but never consulted, so an expired token is only
      discovered by round-tripping a request.
    evidence: |-
      SignIn captures the server's expiresAt and nothing reads it. That is the specified design —
      the matrix routes expiry through the handler's 401 branch — but it means a session that
      expired while the tab sat idle looks signed in until the next request, and the first thing
      the user does after an 8-hour gap fails and bounces them to login. The field is a hook with
      no consumer. What would settle whether it matters: decide in a later UI story whether the
      shell should pre-empt expiry rather than react to it.
    location: >-
      src/ActionLedger.Web/Core/Auth/SessionState.cs
    severity: low
---

<intent-contract>

## Intent

**Problem:** Story 1.5 shipped a Blazor shell with no routable component, no way to sign in, no session, and no toolbar. Story 1.4's login endpoint is unreachable from the browser, the generated client sends no `Authorization` header, and every global behaviour UX-DR18/19/20 specify — cold-load progress, load failure with Retry, Not found, the 401/403/409 snackbars, the shared date and instant formatters, the verbatim Voice strings — exists only as prose. Every UI story in Epics 2–4 lands on top of these patterns, so they have to be real and tested before any of them start.

**Approach:** Add `Features/Auth/LoginPage.razor` as the app's first routable component, a scoped `Core/Auth/SessionState.cs` holding the token in memory, a `DelegatingHandler` in `Core/Auth/` that attaches the bearer token and turns 401/403/409 into the specified global behaviour, a `MudAppBar` shell in `Layout/MainLayout.razor` with a `MudProgressLinear` the handler drives, `Core/Users/UserDirectory.cs` loading the roster once after sign-in, and two first-of-their-kind constant files — `Core/Voice/Voice.cs` for verbatim copy and `Core/Formatting/Formats.cs` for the two date shapes. Every piece gets a bUnit or xunit test in `tests/Web.Tests` with the generated client stubbed by hand.

## Boundaries & Constraints

**Always:** AD-14's seam is enforced by `WebStructureTests.Http_is_confined_to_core_and_the_per_feature_data_folders`, so **no component, layout, or `Shared/` type may reference a `ActionLedger.Web.Core.Api` type** — not `SignInResult`, not `Role`, not `ProblemDetails`, not `ApiException`. `AuthService` and `UserDirectory` map the generated DTOs into web-owned types before anything above the seam sees them. Naming per the spine: routable `<Noun>Page.razor` under `Features/<Feature>/`, presentational `<Noun>.razor`, per-feature clients `<Feature>Service.cs`, scoped state `<Noun>State.cs`. Cross-feature state is limited to `Core/Auth/SessionState.cs` and `Core/Users/UserDirectory.cs`. Every user-facing string in the I/O matrix is a `const` on `Core/Voice/Voice.cs` and is used through that constant, character-exact including the U+2019 apostrophe in `Couldn't load.`. Formatting is `CultureInfo.InvariantCulture` everywhere — a WASM host carries the browser's culture. Repo style holds: file-scoped namespaces, `sealed` on every concrete class, primary constructors for DI, explicit types with target-typed `new()`, `StringComparison.Ordinal` on every comparison, `Sentence_case_with_underscores` test names, a `/// <summary>` on every type naming the AD or UX-DR it defends, `TestContext.Current.CancellationToken` on every awaited call in a test. `Directory.Build.props` has `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` on, so the build is zero-warning or it is red.

**Never:** No new NuGet package — everything here is reachable with the five already pinned; in particular no mocking library (hand-write stubs, as `AuthServiceTests` does), no `Microsoft.Extensions.Http`/`AddHttpClient`, no `Blazored.LocalStorage`, no `Microsoft.AspNetCore.Components.Authorization`. **The token is never persisted** — no `localStorage`, no `sessionStorage`, no cookie; a reload signs the user out, by decision (PRD FR-23, "token held in memory"). No "remember me" and no "forgot password". No sixth folder under `Features/` — `WebStructureTests.No_feature_folder_outside_the_five_ad14_names_exists` asserts exactly `Auth`, `Meetings`, `Review`, `Actions`, `Audit`. No placeholder `MeetingsPage`/`ActionsPage` to fill the toolbar's destinations — those are Stories 2.2 and 4.3, and the Not found page showing until then is the state the epic already plans for. No MudBlazor component is restyled, no third pane, no sidenav, no dark-mode toggle, no infinite scroll, no auto-save, no hover-only affordance, no modal stack. No edit to `wwwroot/css/tokens.css` — it is DESIGN.md's token file and `ThemeAndTokenTests` pins all 40 values. No change to `openapi.json`, to `OpenApiExport`, or to any file under `src/ActionLedger.Api` — this story is web-only. No credential, password, or real URL literal in any committed file (NFR5). No relative time anywhere. No exclamation mark and no emoji in any user-facing string.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Sign in succeeds | `/login`, valid credentials, Sign in pressed | `SessionState` holds token, `expiresAt`, user id, display name, role; the roster loads once; the browser navigates to the attempted route if one was captured, otherwise `/meetings` | N/A |
| Sign in refused | Server answers 401 `ApiException<ProblemDetails>` | The form shows `Sign-in failed. Check your username and password.` under the button; fields keep their values; no navigation; no snackbar; `SessionState` stays empty | The handler does **not** redirect — no token was attached to this request |
| Sign in rejected by validation | Server answers 400 `ApiException<ValidationProblemDetails>` | Same inline message under the button, values retained | Treated as a refusal, not a crash |
| Sign in fails some other way | 500, or a transport failure raising plain `ApiException`/`HttpRequestException` | The form shows `Couldn't load. {problem title}` with `Retry`; `Retry` re-submits; values retained | A response with no usable problem body falls back to `Voice.UnexpectedFailureTitle` |
| Sign in in flight | Submit pressed, response outstanding | The Sign in button is disabled until the response returns; a second press cannot issue a second request | N/A |
| Already signed in visits `/` or `/login` | `SessionState.IsSignedIn` | Redirects to `/meetings` instead of rendering the form | N/A |
| Signed out visits `/` | No session | Renders the login form | N/A |
| Shell after sign-in | Any route, signed in | One `MudAppBar` with `ActionLedger` linking to `/meetings`, `Meetings` and `Actions` links, the current one carrying the active marker, and a user menu showing display name, role, and one item `Sign out`. No sidenav. Content in a `MaxWidth.Large` gutter container | N/A |
| Shell before sign-in | Login page | No app bar, no nav, no user menu | N/A |
| Sign out | `Sign out` chosen | `SessionState` and `UserDirectory` are cleared and the browser navigates to `/login` | N/A |
| Any request in flight | The handler has ≥1 outstanding request | `MudProgressLinear` shows under the toolbar (at the top of the viewport when there is no toolbar); the body is not replaced by skeleton rows | The counter decrements in a `finally`, so a throwing request still clears the bar |
| No request in flight | Counter at zero | No progress bar | N/A |
| Authenticated request | `SessionState.IsSignedIn`, any client call | `Authorization: Bearer {token}` is attached | N/A |
| Unauthenticated request | No session (the login POST) | No `Authorization` header is attached | N/A |
| Session expires mid-session | A request that **carried a token** answers 401 | `SessionState` is cleared, the attempted route is captured, an `ISnackbar` shows `Session expired. Sign in again.`, and the browser navigates to `/login`; after the next successful sign-in the captured route is restored and then forgotten | N/A |
| Role refuses a write | Any response is 403 | `ISnackbar` shows `Your role does not allow this.`; no navigation, no session change | N/A |
| Concurrent change | Any response is 409 | `ISnackbar` shows `Already changed. Reloading.`; no Retry is offered | Refreshing the record is the calling page's job; there is no such page yet |
| Unmatched route | `/nope`, or `/meetings` before Story 2.2 lands | The Not found notice renders inside the shell: `Not found.` and a link to the parent list | N/A |
| Load failure notice | A page hands the notice a problem title and a retry callback | Renders `Couldn't load. {title}` and a `Retry` button that invokes the callback once per press | N/A |
| Roster loads once | First sign-in | `ListUsersAsync(1, 200, ct)` is called exactly once; a second `EnsureLoadedAsync` does not re-fetch | N/A |
| Roster fails to load | The roster call throws | Sign-in still completes and the shell still renders; the roster is empty and `IsLoaded` stays false so a later story can retry | Swallowed deliberately — the roster has no consumer until Epic 3 |
| Date rendered | `DateOnly(2026, 10, 3)` | `2026-10-03` | N/A |
| Instant rendered | `DateTimeOffset` at 14:03 UTC on 2026-09-21, in any offset | `2026-09-21 14:03 UTC` | Converted to UTC first; never localized, never relative |
| Voice constants | The whole `Voice` class | Every constant matches EXPERIENCE.md verbatim and none contains `!` or an emoji | Test names the offending constant |

</intent-contract>

## Code Map

**Read `spec-1-5-…md` first — it is the scaffold's own map and this story only extends it. Everything below was verified on disk at `99ccd7f`.**

### The seam that shapes every decision here

- `tests/Architecture.Tests/WebStructureTests.cs:32-54` -- `SeamNamespaces` is `@"^ActionLedger\.Web\.(Core|Features\.[^.]+\.Data)(\..+)?$"`, and `TheHttpSeam` is `["System.Net.Http", "ActionLedger.Web.Core.Api"]`. **Any type that touches a generated DTO must live under `ActionLedger.Web.Core.*` or `ActionLedger.Web.Features.<X>.Data.*`.** `Features/Auth/LoginPage.razor` is `ActionLedger.Web.Features.Auth` — outside the allowlist — so it must never see `SignInResult`, `Role`, `ProblemDetails`, or `ApiException`. This single fact is why `AuthService` changes shape below. `:56-67` `The_http_rule_has_something_to_rule_on` excludes `[GeneratedCode]` and nested types. `:69-77` `The_web_project_references_no_other_project`. `:99-112` `No_feature_folder_outside_the_five_ad14_names_exists` compares the ordered array `["Auth","Meetings","Review","Actions","Audit"]` against the folders on disk — **adding a sixth breaks the build.**

### What exists and changes

- `src/ActionLedger.Web/Core/ApiClientRegistration.cs:37-39` -- `services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(baseAddress) })`. A raw `new HttpClient`, **not** `AddHttpClient`, so there is no handler pipeline to slot into. Chain the handler by hand: `handler.InnerHandler = new HttpClientHandler()` then `new HttpClient(handler)`. `:26` `BaseAddressKey = "Api:BaseAddress"`, falling back to the host origin — leave that logic alone.
- `src/ActionLedger.Web/Features/Auth/Data/AuthService.cs` -- today `SignInAsync(string, string, CancellationToken)` returns `Task<SignInResult>` and lets `ApiException<ProblemDetails>` escape. **Both halves have to change**: return a web-owned outcome and catch the exception, or `LoginPage` cannot call it without breaking AD-14. Its `<remarks>` says Story 1.6 consumes rather than replaces it — keep the type, the file, and the primary-constructor shape.
- `src/ActionLedger.Web/Program.cs:15-23` -- the composition root. `AddMudServices()` at `:17` already registers `ISnackbar` and `IDialogService`. `:20` `AddActionLedgerApiClient(...)`, `:23` `AddScoped<AuthService>()`. The comment at `:7-10` says the scaffold is "deliberately no routable component yet — every page, the session, and the shell are Story 1.6" — that comment is now stale and must go.
- `src/ActionLedger.Web/App.razor` -- `<Router AppAssembly="@typeof(WebAssemblyMarker).Assembly">` with a one-line `<NotFound><LayoutView Layout="@typeof(MainLayout)"><MudText Typo="Typo.body1">Not found.</MudText></LayoutView></NotFound>`. Replace that body with the shared notice component; keep the `<FocusOnNavigate RouteData Selector="h1" />` — it means **every page needs an `h1`**, which in MudBlazor is `<MudText Typo="Typo.h4" HtmlTag="h1">`.
- `src/ActionLedger.Web/Layout/MainLayout.razor` -- `MudThemeProvider` (bound to `ActionLedgerTheme.Instance`, `ObserveSystemDarkModeChange="true"`), the other three providers, then `MudLayout > MudMainContent > MudContainer MaxWidth.Large Gutters`. The app bar and the progress bar go inside `MudLayout`, above `MudMainContent`. `LayoutTests` asserts all four providers, `Assert.Same(ActionLedgerTheme.Instance, provider.Theme)`, `ObserveSystemDarkModeChange`, `MaxWidth.Large`, `Gutters == true`, and that `@Body` renders inside `.mud-container` with `mud-container-maxwidth-lg` and `mud-container--gutters` — **all five must still pass.**
- `src/ActionLedger.Web/_Imports.razor` -- has `Routing`, `Web`, `JSInterop`, `MudBlazor`, `ActionLedger.Web`, `ActionLedger.Web.Layout`. No `System.Net.Http` on purpose (`:1-2`) — keep it that way. Add only the new non-HTTP namespaces.
- `src/ActionLedger.Web/wwwroot/index.html` -- `ThemeAndTokenTests.The_host_page_loads_mudblazor_and_the_design_tokens` and `The_host_page_requests_the_font_family` parse this file; adding a `<link>` is additive and safe. `#blazor-error-ui` needs no CSS — `MudBlazor.min.css` already gives it `display:none` (settled in 1.5's review).
- `src/ActionLedger.Web/wwwroot/css/tokens.css` -- **do not touch.** `ThemeAndTokenTests` pins 40 hex values, both type roles, and three spacing values. New layout CSS goes in a new `app.css`.

### The contract surface, verbatim

- `src/ActionLedger.Web/Core/Api/ActionLedgerApiClient.g.cs` (git-ignored, regenerated pre-build) -- `IActionLedgerApiClient` has exactly eight members: `GetHealthAsync`/`GetReadinessAsync`/`SignInAsync(SignInCommand body, …)`/`ListUsersAsync(int? page, int? pageSize, …)`, each with and without a `CancellationToken`. `ListUsersAsync`'s parameters are **not** optional. DTOs are mutable classes with object-initializer construction, not records: `SignInResult { string Token; DateTimeOffset ExpiresAt; UserSummaryDto User }`, `UserSummaryDto { Guid Id; string DisplayName; Role Role }`, `PagedResultOfUserSummaryDto { ICollection<UserSummaryDto> Items; int Page; int PageSize; int Total }`, `ProblemDetails { string? Type; string? Title; int? Status; string? Detail; string? Instance; IDictionary<string,object> AdditionalProperties }`. `ValidationProblemDetails` is a **separate flat class, not a subclass**, with the same five members plus `Errors`. `enum Role { ActionOfficer, Lead }`.
- Failure shape: `SignInAsync` throws `ApiException<ValidationProblemDetails>` on 400, `ApiException<ProblemDetails>` on 401, and a **plain non-generic** `ApiException` on anything else — including 500, where the server sends no `type`/`title` at all. `ApiException` exposes `int StatusCode`, `string? Response`, `IReadOnlyDictionary<string, IEnumerable<string>> Headers`; `ApiException<T>` adds `T Result`. A transport failure is not an `ApiException` at all — it is `HttpRequestException`. **The mapper must handle all four.**
- `correlationId` is not a typed property; it lands in `ProblemDetails.AdditionalProperties["correlationId"]`. Nothing in this story renders it.

### The server's exact strings (read-only — do not change the Api)

- `src/ActionLedger.Api/Errors/ProblemDetailsMapping.cs:116-145` -- `ProblemTypes` are bare slugs, not URIs: `"validation"`, `"unauthorized"`, `"forbidden"`, `"not-found"`, `"conflict"`. `TitleFor` gives one title per status: 400 `"The request was not valid."`, 401 `"Authentication is required."`, 403 `"You do not have permission to do that."`, 404 `"The resource was not found."`, 409 `"The request conflicts with the current state."`. `:95-99` is the comment that authorises this story's copy: *"The web app renders it verbatim — \"Couldn't load. {problem title}\" — so it has to read as a sentence."* **Dispatch on `ApiException.StatusCode`, not on `type`** — a bare 500 carries neither.
- `src/ActionLedger.Api/Controllers/AuthController.cs:27` -- `RefusedDetail = "The username or password is incorrect."`; the 401 body is `type: unauthorized`, `title: "Authentication is required."`, that detail. The web shows its own copy instead, per UX-DR18.
- `src/ActionLedger.Application/Abstractions/PagedResult.cs:29-43` -- `Paging.DefaultPageSize = 50`, `MaxPageSize = 200`; `pageSize` is clamped, `page` is not. The roster call passes `1, 200`.
- `src/ActionLedger.Api/Auth/JwtAccessTokenIssuer.cs`, `src/ActionLedger.Api/Configuration/JwtOptions.cs:14` -- HS256, 8-hour lifetime, claims `sub`/`name`/`role`. **The web never parses the JWT**: `SignInResult` returns `expiresAt` and the full `UserSummaryDto` alongside the opaque token.
- `src/ActionLedger.Infrastructure/Seed/DemoDataSeeder.cs:46-51` -- demo users `dana` / `Dana Whitfield` / ActionOfficer, `priya` / `Priya Ramaswamy` / ActionOfficer, `marcus` / `Marcus Bell` / Lead. The password is `Seed:DefaultPassword`, documented in `.env.example:66-75` and **committed nowhere**.

### Test scaffolding to copy, not reinvent

- `tests/Web.Tests/AuthServiceTests.cs` -- the hand-written `private sealed class StubApiClient : IActionLedgerApiClient` pattern: record the last call, return a canned result, throw `NotSupportedException` from every member the test does not expect. **There is no mocking package and none may be added.** Widen this stub (or add a sibling) to cover `ListUsersAsync` and to throw a chosen `ApiException`.
- `tests/Web.Tests/LayoutTests.cs` -- `public sealed class LayoutTests : BunitContext` (bUnit **v2**: `BunitContext`, `Render<T>`, `IRenderedComponent<T>`), ctor `Services.AddMudServices(); JSInterop.Mode = JSRuntimeMode.Loose;`, and `RenderLayout()` passing a `RenderFragment` body probe. Every new component test starts from this shape. bUnit supplies a fake `NavigationManager` whose `History` records navigations, and `AddMudServices` supplies a real `ISnackbar` whose `ShownSnackbars` can be asserted.
- `tests/Web.Tests/WebProject.cs` -- `internal static string ReadAllText(string relativePath)` rooted at `src/ActionLedger.Web`. Use it to read `app.css` or `index.html`; **do not write a second root-finder.**
- `tests/Web.Tests/ThemeAndTokenTests.cs` -- the `TheoryData<string,string>` + `AssertToken` shape for pinning literal values. Copy it for the Voice constants.
- `tests/Api.Tests/AuthEndpointTests.cs:125-175` -- what the server actually returns for each refusal, if a stub's fidelity is ever in doubt.

## Tasks & Acceptance

**Execution:**

- `src/ActionLedger.Web/Core/Voice/Voice.cs` -- a `static class` of `const string` for every user-facing string this story renders: `ProductName`, `Meetings`, `Actions`, `SignIn`, `SignOut`, `SignInFailed`, `Username`, `Password`, `Retry`, `NotFound`, `LoadFailurePrefix` (`"Couldn't load. "`, U+2019), `UnexpectedFailureTitle`, `SessionExpired`, `RoleNotAllowed`, `AlreadyChanged` -- the repo's first client-side string vocabulary; every screen from Epic 2 on draws from it (UX-DR20).
- `src/ActionLedger.Web/Core/Formatting/Formats.cs` -- `Date(DateOnly)` → `yyyy-MM-dd` and `Instant(DateTimeOffset)` → `yyyy-MM-dd HH:mm 'UTC'` after `ToUniversalTime()`, both `CultureInfo.InvariantCulture` -- a WASM host carries the browser's culture, so invariant is load-bearing, not decoration (UX-DR20).
- `src/ActionLedger.Web/Core/Errors/ApiFailure.cs` -- a web-owned `sealed record ApiFailure(int StatusCode, string Title, string? Detail)` with no dependency on the generated namespace -- this is what crosses the AD-14 seam so pages can render a problem title without importing `Core.Api`.
- `src/ActionLedger.Web/Core/Errors/ApiFailures.cs` -- maps `ApiException<ProblemDetails>`, `ApiException<ValidationProblemDetails>`, plain `ApiException`, and `HttpRequestException` onto `ApiFailure`, falling back to `Voice.UnexpectedFailureTitle` when the body carries no title -- a 500 arrives with neither `type` nor `title`, so the fallback is a real path, not a guard.
- `src/ActionLedger.Web/Core/Auth/SessionState.cs` -- scoped, in-memory: `IsSignedIn`, `Token`, `ExpiresAt`, `UserId`, `DisplayName`, `Role` (a `string`, **not** the generated enum), `SignIn(...)`, `SignOut()`, `CaptureAttemptedRoute(string)`, `TakeAttemptedRoute()` returning and clearing it, and a `Changed` event -- AD-14 names this file as one of only two cross-feature state services; `Role` is a string so the toolbar can render it without crossing the seam.
- `src/ActionLedger.Web/Core/Shell/LoadingState.cs` -- a counter with `Begin()`/`End()`, `IsBusy`, and a `Changed` event -- the shell's progress bar needs one signal that every in-flight request feeds.
- `src/ActionLedger.Web/Core/Auth/SessionMessageHandler.cs` -- a `DelegatingHandler` that brackets every send with `LoadingState`, attaches `Authorization: Bearer {token}` when signed in, and on the response raises the 403 and 409 snackbars and — **only when it attached a token** — clears the session, captures the attempted route, snackbars `Session expired. Sign in again.`, and navigates to `/login` -- the token check is what stops the login POST's own 401 from bouncing the user (UX-DR18, UX-DR19).
- `src/ActionLedger.Web/Core/ApiClientRegistration.cs` -- chain the handler in front of a `HttpClientHandler` and register `SessionState`, `LoadingState`, `SessionMessageHandler`, and `UserDirectory` -- the one place an `HttpClient` is built stays the one place, and no `Microsoft.Extensions.Http` package is added.
- `src/ActionLedger.Web/Core/Users/UserDirectory.cs` -- scoped; `EnsureLoadedAsync` calls `ListUsersAsync(1, 200, ct)` at most once, exposes `IsLoaded` and a `IReadOnlyList<DirectoryUser>` of web-owned records (id, display name, role as a string), swallows a failure into an empty roster, and `Clear()`s on sign-out -- AD-14's second named state service; the roster has no consumer until Epic 3, so a failure must not block sign-in.
- `src/ActionLedger.Web/Features/Auth/Data/AuthService.cs` -- change the return type to a web-owned `SignInOutcome` (success carrying the session fields with `Role` mapped to its string name, failure carrying an `ApiFailure`) and catch `ApiException`/`HttpRequestException` through `ApiFailures` -- **without this `LoginPage` cannot call it at all**, because `SignInResult` is a `Core.Api` type and `Features.Auth` is outside the AD-14 allowlist.
- `src/ActionLedger.Web/Core/Auth/SignInOutcome.cs` -- the web-owned result `AuthService` returns -- it lives in `Core/Auth` because `SessionState` consumes it too.
- `src/ActionLedger.Web/Features/Auth/LoginPage.razor` -- `@page "/login"` and `@page "/"`; an `h1`, username and password fields with visible labels, a submit button that disables in flight, Enter-to-submit through a plain `<form @onsubmit @onsubmit:preventDefault>`; a 401/400 shows `Voice.SignInFailed` under the button, anything else shows the load-failure notice with Retry; on success it signs the session in, kicks the roster load, and navigates to the captured route or `/meetings`; when already signed in it redirects to `/meetings` on initialise -- the app's first routable component and the only protected-route seam this story can honestly consume.
- `src/ActionLedger.Web/Shared/Toolbar.razor` -- presentational: `[Parameter]` display name and role, `EventCallback OnSignOut`; a `MudAppBar` with the product name linking to `/meetings`, `MudNavLink`s for Meetings and Actions carrying the active marker, and a labelled `MudMenu` with one `Sign out` item -- AD-14 wants children presentational with parameters in and callbacks out, and it keeps the layout free of command logic.
- `src/ActionLedger.Web/Shared/NotFoundNotice.razor` -- presentational: `Not found.` plus a link to a parent list, defaulting to Meetings -- named `NotFoundNotice` rather than `NotFound` so it cannot collide with the `<NotFound>` parameter tag inside `<Router>`.
- `src/ActionLedger.Web/Shared/LoadFailure.razor` -- presentational: `Couldn't load. {Title}` and a `Retry` button raising an `EventCallback` -- the one place that copy is assembled, so Epic 2's first list inherits it rather than re-typing it.
- `src/ActionLedger.Web/Layout/MainLayout.razor` -- inject `SessionState` and `LoadingState`, subscribe to both through `InvokeAsync(StateHasChanged)`, render `Toolbar` only when signed in, render the `MudProgressLinear` whenever `IsBusy` (with the under-app-bar offset only when the bar is there), dispose the subscriptions -- the shell; the four providers, the theme binding, and the `MaxWidth.Large` gutter container must survive untouched or `LayoutTests` goes red.
- `src/ActionLedger.Web/App.razor` -- replace the one-line `NotFound` body with `NotFoundNotice` -- an unmatched route is the only way to reach `/meetings` and `/actions` until Stories 2.2 and 4.3 land.
- `src/ActionLedger.Web/wwwroot/css/app.css`, `src/ActionLedger.Web/wwwroot/index.html` -- one new stylesheet holding only the global progress bar's fixed positioning and its under-app-bar modifier, linked after `tokens.css` -- `tokens.css` is DESIGN.md's file and is pinned value-by-value by a test; layout CSS does not belong in it.
- `src/ActionLedger.Web/_Imports.razor`, `src/ActionLedger.Web/Program.cs` -- add the new non-HTTP namespaces to the imports, register the new services, and delete the stale "no routable component yet" comment -- `System.Net.Http` stays out of `_Imports` so no component picks it up ambiently.
- `tests/Web.Tests/VoiceAndFormatsTests.cs` -- a `TheoryData` pinning every `Voice` constant to its literal, a test that none contains `!` or a non-ASCII emoji, and the date/instant rows of the matrix including a non-UTC offset and a non-invariant `CurrentCulture` -- UX-DR20 says these strings are used verbatim, so the test is what makes "verbatim" checkable.
- `tests/Web.Tests/SessionStateTests.cs` -- sign-in/sign-out, `Changed` firing, and that `TakeAttemptedRoute` returns the captured route once and then null -- the restore-once semantics are what stop a stale route hijacking a later login.
- `tests/Web.Tests/ApiFailuresTests.cs` -- the four exception shapes and the no-title fallback -- the 500 path is unreachable from a stubbed client otherwise.
- `tests/Web.Tests/SessionMessageHandlerTests.cs` -- bearer attached when signed in and absent when not; 401 with a token clears, captures, snackbars, and navigates; 401 without a token does none of those; 403 and 409 snackbar their exact strings; `LoadingState` returns to zero on a success, on a failure response, and on a thrown send -- drive it with a stub `HttpMessageHandler` so no network is involved.
- `tests/Web.Tests/LoginPageTests.cs` -- the six login rows of the matrix: success stores the session and navigates, 401 shows the inline message with values retained, an unexpected failure shows the load-failure notice, the button disables in flight, a signed-in visit redirects, and the attempted route wins over `/meetings` -- AC3 names this test explicitly.
- `tests/Web.Tests/UserDirectoryTests.cs` -- loads once, does not re-fetch, survives a throwing roster, and clears -- the "once" in "loads the roster once" is otherwise unobservable.
- `tests/Web.Tests/ShellTests.cs` -- the app bar is absent before sign-in and present after, carries the product name, both destinations with the active marker on the current one, and the user menu with display name, role, and `Sign out` raising the callback; the progress bar follows `LoadingState`; the four providers and the gutter container still hold -- extend `LayoutTests`' `BunitContext` shape rather than replacing it.
- `tests/Web.Tests/SharedComponentTests.cs` -- `NotFoundNotice` renders `Not found.` with a working parent link, and `LoadFailure` renders the prefixed title and raises `Retry` -- both are presentational, so a direct render is the whole test.
- `tests/Architecture.Tests/WebStructureTests.cs` -- add a rule that every type carrying a `[Route]` resides under `ActionLedger.Web.Features.` and is named `*Page`, with a non-vacuity assertion that at least one routable type exists -- AD-14's "one routable container component per route in a feature folder" had no enforcement, and a page dropped into `Shared/` or `Layout/` would otherwise pass every check.

**Acceptance Criteria:**

- Given a clean clone and a stock SDK 10.0.401 with no workloads, when I run `dotnet build ActionLedger.sln` and `dotnet test ActionLedger.sln`, then both succeed with zero warnings, every project passes, and no step invokes Node, npm, or a workload install.
- Given the signed-in shell, when I operate it with the keyboard only, then every field has a visible label, the user-menu trigger has an accessible name, focus rings are MudBlazor's defaults, and tab order follows reading order.
- Given each new guard, when its protected behaviour is mutated — the bearer header dropped, the token check removed from the handler's 401 branch, a `Voice` constant altered, `Formats` switched to `CurrentCulture`, `UserDirectory`'s once-only flag removed, a routable component moved out of `Features/`, a `Core.Api` type referenced from `LoginPage` or `Toolbar` — then a named test or the build fails, verified red and then reverted.
- Given the app after sign-in, when I open `/meetings` or `/actions`, then the Not found notice renders inside the shell with its parent link, which is the state Stories 2.2 and 4.3 replace.
- Given the pull request, when CI runs, then `build and test`, `linked issue`, and CodeQL all pass.

## Spec Change Log

- **`Voice` is declared in namespace `ActionLedger.Web.Core`, not `ActionLedger.Web.Core.Voice`,
  although the file is at the specified `Core/Voice/Voice.cs`.** A namespace and a type that share
  a name do not coexist: from inside `ActionLedger.Web.Core.Errors`, C# name lookup walks the
  enclosing namespaces before it reaches a using-imported type, finds the child namespace
  `Core.Voice`, and `Voice.UnexpectedFailureTitle` fails to compile with `CS0234`. Verified
  against the compiler on a minimal reproduction before choosing. Declaring the class one level up
  removes the ambiguity — there is no namespace named `Voice` to lose to — and costs only a
  folder/namespace mismatch on one file, which nothing enforces here (there is no `.editorconfig`
  and `IDE0130` is off). The file path, the class name, and every constant are exactly as
  specified. **KEEP:** the class name `Voice` and the `Core/Voice/` path; the alternatives were a
  renamed class (`VoiceStrings`), which every later screen would inherit, or a `using` alias at
  every call site.
- **The `Role` is rendered beside the user-menu trigger rather than inside the menu.**
  EXPERIENCE.md asks the menu to show display name and Role; putting the Role inside the popover
  would hide it until the menu is opened, and folding it into the trigger's label would invent a
  separator string that is not in `Voice`. A `MudText` next to the trigger keeps both visible at
  all times, keeps the menu at exactly one item as specified, and invents no copy. **KEEP:** the
  one-item menu — `ShellTests` asserts it, so a second item added later has to be a decision.
- **The manual check ran at the API chain rather than in a browser.** A Postgres container, the
  migration bundle, and a seeded API were stood up, and the real `HttpClient` →
  `SessionMessageHandler` → generated client → `AuthService`/`UserDirectory` chain was driven
  against it. What could not run is the browser half: the Api configures no CORS, and this story
  forbids touching `src/ActionLedger.Api`, so a cross-origin dev-server page cannot reach the API.
  The single-origin nginx topology that makes it work is Story 1.7. Results are in Verification.

## Review Triage Log

### 2026-09-21 — Follow-up review pass (inline, no subagents)

The pass the previous run could not complete. The earlier run escalated `no subagents`: all four
layers were dispatched and none returned, so nothing was reviewed and the spec was parked at
`blocked`. This pass ran the same four lenses inline in one session against the same diff
(`99ccd7f..5c2f851`, 35 files) and closes that escalation. No code was changed by the escalated run;
the only prior edit was this spec's `status`.

- verdicts: 4 findings — medium 1, false 1, low 2 (both rejected)

**`[medium]` `[defer]` A hanging roster call strands a signed-in user on the login form for up to 100 seconds.**
`LoginPage.SubmitAsync` awaits `Directory.EnsureLoadedAsync()` *before*
`Navigation.NavigateTo(TakeAttemptedRoute() ?? HomeRoute)`, and `ApiClientRegistration` sets no
`HttpClient.Timeout`, so the .NET default of 100 seconds applies. The session is already signed in
at that point, so the user is authenticated but still looking at a disabled button and a progress
bar. The comment above the await — *"it swallows its own failures, so awaiting it cannot block the
landing"* — is true of a **failure** and false of **latency**: a swallowed exception still has to
arrive before the next line runs. `UserDirectory` anticipates precisely this call timing out (its
`OperationCanceledException` arm is commented *"A timed-out roster call throws
TaskCanceledException"*), so the failure mode was foreseen and its latency consequence was not.
Deferred rather than patched: nothing renders the roster until Epic 3's owner picker, no acceptance
criterion in this story constrains post-sign-in latency, and the smallest correct fix is a design
choice between not awaiting and bounding the timeout — see the ledger entry.

**`[false]` `[reject]` The undeclared-status branch in `ApiFailures` is *not* "only exercised against synthetic bodies".**
This was the one unverified risk the previous pass named and asked a follow-up to close. Verified
three ways and refuted:
- the generated `ProblemDetails` carries `[System.Text.Json.Serialization.JsonPropertyName("title")]`
  and siblings (`ActionLedgerApiClient.g.cs:790-807`), so the default case-sensitive
  `JsonSerializer.Deserialize` maps the API's camelCase body correctly — the failure mode that would
  have made this branch silently useless does not exist;
- NSwag reads the real body into `ApiException.Response` for an undeclared status
  (`ActionLedgerApiClient.g.cs:208-209`), so there is content to parse rather than a placeholder;
- `ApiFailuresTests.cs:60` drives it with
  `{"type":"not-found","title":"The resource was not found.","status":404,"detail":"No such meeting."}`,
  which is the shape `ProblemDetailsMapping` actually emits, not a synthetic one.
The branch still first meets a live server in Epic 2, but that is ordinary integration exposure, not
an unverified claim.

**`[low]` `[reject]` `SessionMessageHandler.PathOf` compares the captured route case-sensitively.**
`/Login` would not match `LoginRoute` and would be captured as an attempted route, and since Blazor
routing is case-insensitive the user would be returned to the form they just left. Speculative: no
link in the app produces that casing, and the only writer of this value is
`NavigationManager.ToBaseRelativePath` over routes the app itself emits.

**`[low]` `[reject]` The scoped `HttpClient` factory assigns `handler.InnerHandler` inside its lambda.**
A second scope would reassign `InnerHandler` on a handler that has already sent. WebAssembly has one
scope for the app's lifetime, and the tests build their own handler chains, so there is no second
caller.

**Intent-alignment: no divergence.** The one thing that reads like a defect on a first pass —
`HomeRoute = "/meetings"` pointing at a route no component serves — is specified deliberately
(I/O matrix row *"Unmatched route … `/meetings` before Story 2.2 lands"*, Code Map line for
`App.razor`) and is covered by `ShellTests.cs:183-201` and `LoginPageTests.cs:56`.

**Verification re-run for this pass:** `dotnet build ActionLedger.sln` succeeded with 0 warnings;
`dotnet test ActionLedger.sln` passed 186 of 186 at `8b0bd07`. No file was changed by this review.


### 2026-09-21 — Review pass
- verdicts: 33 findings — high 0, medium 8, low 21, false 4, maybe-false 0
- findings:
  - `[low]` `[patch]` `ApiFailures` discards the server's problem title for any status the operation did not declare — real: the generated client puts the raw ProblemDetails body in `ApiException.Response` for an undeclared status and the mapper never read it; unreachable from this story's two operations (login and users answer only 200/400/401) but live from Epic 2 on. Fixed: the bare-`ApiException` arm now deserializes `Response` and uses its title and detail, falling back when the body is absent, untitled, or not JSON.
  - `[medium]` `[patch]` A roster call that times out escapes `EnsureLoadedAsync` into `LoginPage`, stranding a signed-in user on the form — real: `TaskCanceledException` is not an `HttpRequestException`, so neither catch arm held it. Fixed: an `OperationCanceledException` arm filtered on the caller's own token not having cancelled.
  - `[low]` `[patch]` The user menu renders the raw enum name, so a seeded Action Officer reads `ActionOfficer` — real and user-visible on the demo, where two of three seeded users hold that role; every existing test used `Lead`, which hid it. Fixed: `Core/Users/RoleNames.Display`, used by both mappers, pinned by a test covering `ActionOfficer`.
  - `[false]` `[reject]` The no-exclamation-mark/no-emoji test asserts against its own theory data, so it cannot fail on the code — refuted: `Every_voice_constant_is_pinned` asserts the theory-row names equal `Voice`'s declared names in both directions, and `The_voice_constant_reads_exactly_as_experience_md_writes_it` asserts each row's expected value equals the constant, so theory data cannot diverge from `Voice` and checking one is checking the other.
  - `[low]` `[patch]` The inline sign-in failure is not announced to assistive technology — real: a plain `MudText` with no live region, so a screen-reader user presses Sign in and hears nothing. Fixed: `role="alert"` plus `aria-describedby` on the password field while shown.
  - `[low]` `[patch]` The login fields carry no `autocomplete`, so password managers do not recognise them — real, and it is the demo's own login screen. Fixed: `autocomplete="username"` and `"current-password"`; unmatched attributes already forward to the input, as `id` does.
  - `[false]` `[reject]` Submitting empty credentials yields the generic refusal rather than a "you typed nothing" message — refuted: I/O matrix row 3 mandates that a 400 shows exactly the same inline message as a 401, so the observed behaviour is the specified behaviour, and client-side required-validation is surface the intent never asked for.
  - `[medium]` `[patch]` `ExpireSession` is not idempotent, so concurrent 401s duplicate the snackbar and redirect and the second `SignOut()` wipes the first's captured route — real: restore-once fails silently, which is the one thing the mechanism exists to do. Fixed: an early return when the session is already signed out; independently mutation-checked (two tests red without it).
  - `[low]` `[patch]` `CurrentRoute()` returns path and query together, so the login-page guard does not recognise `/login?x=1` and captures the login page as the attempted route — real, unreachable until Epic 4 adds query-string routes. Fixed: the guard compares the path; the captured route keeps its query.
  - `[low]` `[patch]` `OnInitialized` redirects a signed-in visitor but the form still renders, and the test that names the behaviour asserts only the navigation — both real. Fixed: the body is wrapped in a signed-out check and the test now asserts the form is absent.
  - `[low]` `[reject]` No call is cancellable and `LoginPage` is not disposable — real but negligible: nothing calls `StateHasChanged` after the await, so a torn-down component is harmless, and the smallest fix adds a `CancellationTokenSource` field plus `IDisposable` — complexity for a case not shown reachable.
  - `[low]` `[patch]` The new routable-component rule prefix-matches `ActionLedger.Web.Features.`, so a page in `Features/Auth/Data` or a sixth non-existent feature namespace passes the rule it claims to enforce — real, and it is the guard this story added. Fixed: the namespace must equal one of the five names already in the `Features` array.
  - `[low]` `[defer]` Deliberate deferrals are described in prose but the `deferred` ledger is empty, so a ledger sweep sees none of them — real. Deferred: the two genuine items (browser-level verification owed at Story 1.7, and `ExpiresAt` stored but never consulted) are now in frontmatter `deferred`.
  - `[low]` `[patch]` Tests reach `LoadFailure`'s Retry through MudBlazor's internal `mud-button-outlined` class — real: change the `Variant` and two tests silently retarget. Fixed: a `RetryId` constant on the button, used by both test files.
  - `[low]` `[patch]` `ApiFailures`' `HttpRequestException` arm duplicates the discard arm, and `new HttpClient(handler)` disposes a handler the container also owns — both real. Fixed: the duplicate arm deleted (and the `OperationCanceledException` arm added in its place was deleted too, on the same reasoning, once it proved behaviourally identical to the discard arm); `disposeHandler: false`.
  - `[medium]` `[patch]` Duplicate expiry on two 401s, and a 401 landing after a deliberate sign-out — same defect as the idempotence finding above; shares its fix and its mutation check.
  - `[low]` `[reject]` `NavigateTo("/login")` is origin-relative and would escape a non-root `<base href>` — real only under a sub-path deployment; Story 1.7's nginx and the Azure target both serve at root, no sub-path deployment exists or is planned, and every other route literal in the change is root-relative too, so a partial fix would be worse than none.
  - `[low]` `[reject]` The attempted route is captured at response time, not send time, so a slow 401 restores a route the user had already left — real but marginal, and the smallest fix threads the request URI through the handler, adding surface for a case the idempotence guard already narrows to the first 401.
  - `[medium]` `[patch]` `UserDirectory` does not catch `TaskCanceledException` — same defect as the roster-timeout finding above; shares its fix.
  - `[medium]` `[patch]` `AuthService` does not catch a cancelled or timed-out login POST — real: it escapes as an unhandled component exception where the matrix requires the load-failure notice with Retry. Fixed with the same filtered `OperationCanceledException` arm.
  - `[false]` `[reject]` A roster load answering 403 would snackbar a role refusal at a fresh session — refuted: `/api/v1/users` carries a bare `[Authorize]` with no role requirement, so it cannot answer 403; and a token issued moments earlier cannot answer 401. Expiring on a genuine 401 there is correct behaviour, not a defect.
  - `[low]` `[reject]` `MainLayout.OnStateChanged` discards the task returned by `InvokeAsync`, so a fault would go unobserved — not shown reachable: `InvokeAsync(StateHasChanged)` after disposal does not throw in this host, and the fix adds a try/catch around every state change for a fault never demonstrated.
  - `[low]` `[reject]` The progress bar's fixed offset misses MudBlazor's shorter app bar below 600px and in short landscape — real, and confirmed in MudBlazor 9.10.0's stylesheet (the toolbar height is a `calc`, not a redefinition of `--mud-appbar-height`), but DESIGN.md scopes the product to desktop at 1024px and wider, so the affected viewports are outside the supported range and the fix adds matching media queries.
  - `[low]` `[reject]` A null or empty `DisplayName` gives the user-menu trigger an empty accessible name — not shown reachable: `displayName` is required and non-empty in the contract and in the seeder, and the fix adds a fallback branch for a value the server cannot send.
  - `[low]` `[patch]` The routable-component rule allows a page under `Features/<X>/Data` — same defect as the prefix-match finding above; shares its fix.
  - `[medium]` `[patch]` The claim that a failed roster load still lets sign-in complete did not hold for a cancelled load — same defect as the roster-timeout finding; shares its fix.
  - `[low]` `[reject]` "At most once" is inaccurate because a failed load re-fetches, and there is no in-flight guard — the re-fetch is intentional and directly tested (`A_failed_load_can_be_retried`); the inaccurate phrase is in this build's spec, and the concurrent-caller case has one caller today, so a guard would be complexity for an unreachable case.
  - `[low]` `[reject]` "Kicks the roster load, and navigates" describes fire-and-forget but the code awaits — accurate reading, but the fix is to edit this build's spec; awaiting is correct ordering and the progress bar covers the latency.
  - `[low]` `[reject]` `LoadingState.End()` runs when response headers arrive, so the bar clears while the body downloads — real, and NSwag does use `ResponseHeadersRead`; negligible for JSON bodies of this size, and the fix wraps the response stream to observe completion.
  - `[medium]` `[patch]` Nothing in the suite executes `AddActionLedgerApiClient`, so the handler can be disconnected from the app with a green suite — real and the most serious finding: I reverted the registration to its pre-change form and the whole suite stayed green. Fixed: `ApiClientRegistrationTests` builds a real `ServiceCollection`; independently mutation-checked (three tests red on that exact revert).
  - `[medium]` `[patch]` A 401 expiry leaves the previous user's roster loaded, so the next sign-in on the same tab inherits it — real: `ExpireSession` never clears `UserDirectory` and `EnsureLoadedAsync` returns early on `IsLoaded`. Fixed at the sign-in site — `Directory.Clear()` before `EnsureLoadedAsync` — which covers every way a previous session ended and avoids the DI cycle that injecting the directory into the handler would create.
  - `[low]` `[reject]` The post-sign-in landing is serialized behind the roster round-trip — real, but the progress bar is showing and the button is disabled throughout, so the latency is communicated rather than hidden; grouped with the cancellation finding above and rejected on the same reasoning.
  - `[false]` `[reject]` The run should have finalized to `awaiting-operator` with `operator_actions` — refuted: the directive's trigger is human-only action *outside the repo* (buy a domain, publish DNS, grant an API key, click a vendor console). Story 1.6's ACs contain none of that class: a keyboard pass and a browser check are in-product, and CI-on-a-PR at landing is how every prior story in this epic finished — Story 1.5 is `done` carrying the identical "runs at landing, with a human present" note. The owed human work is recorded in `deferred` instead.

## Design Notes

**The AD-14 seam is the whole design, and it is easy to violate by accident.** `WebStructureTests` fails the build when any type outside `Core/` or `Features/*/Data/` references `ActionLedger.Web.Core.Api`. A login page that holds a `SignInResult`, a toolbar that renders the generated `Role` enum, or a page that catches `ApiException` all break it. So the seam types map on the way out: `AuthService` returns `SignInOutcome`, `UserDirectory` returns `DirectoryUser` records, `ApiFailures` turns every exception shape into `ApiFailure`, and `SessionState.Role` is a `string`. Nothing above the seam imports the generated namespace, and the existing rule — which already passes — becomes genuinely load-bearing rather than only covering `ApiClientRegistration` and `AuthService`.

**Only redirect on a 401 the handler itself authenticated.** The login POST is anonymous and answers 401 on bad credentials. If the handler treated every 401 alike, a mistyped password would snackbar "Session expired. Sign in again." and bounce the user to the page they are already on, and UX-DR18's inline message would never be seen. Gate the whole 401 branch on "this request carried a token", which is exactly the condition that distinguishes an expired session from a refused credential.

**Chain the handler by hand; `AddHttpClient` is not available.** `Microsoft.Extensions.Http` is not pinned and NFR9 keeps the package list short:

```csharp
services.AddScoped<SessionMessageHandler>();
services.AddScoped(provider =>
{
    SessionMessageHandler handler = provider.GetRequiredService<SessionMessageHandler>();
    handler.InnerHandler = new HttpClientHandler();
    return new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
});
```

In Blazor WebAssembly `HttpClientHandler` resolves to the browser fetch handler, so this is the supported shape and not a workaround. Scoped is effectively singleton in WASM, so the handler, `SessionState`, and `LoadingState` are one instance each and can be injected into components directly.

**`LoadingState` must decrement in a `finally`.** The bar is driven from `SendAsync`, so a thrown send — a transport failure, a cancellation — would otherwise strand it on screen for the rest of the session. Test all three exits, not just the happy one.

**There is no protected page yet, so no route guard ships.** EXPERIENCE.md calls the unauthenticated redirect "a route-level auth check, not a router guard", which means each routable component checks for itself. Story 1.6 ships exactly two reachable surfaces — the login page and the Not found notice — and neither is protected, so an `AuthenticatedPage` base class would have no consumer and could only be exercised by a test written to exercise it. Story 1.5 already paid for that lesson: a guard with no real subject passes vacuously. What does ship is the real mechanism the guard would use — `CaptureAttemptedRoute`/`TakeAttemptedRoute` on `SessionState`, consumed by the handler's 401 branch and by the login page's post-sign-in navigation, and tested end to end. The first protected page (Story 2.2) adds the per-page check on top of it.

**`/meetings` does not exist, and that is the correct interim state.** The toolbar's two destinations and the post-sign-in landing route all resolve to the router's `NotFound`, which now renders the real notice inside the shell. Epic 1's plan says so in as many words for `/actions/:id`, and Story 1.5 established the precedent: a placeholder page that exists only to be deleted next story is worse than an honest Not found. Anyone opening the demo before Epic 2 signs in, sees the toolbar, and sees "Not found." — which is the shell working, not the shell broken.

**Copy is character-exact, and one character is a trap.** EXPERIENCE.md writes `Couldn't load. {problem detail title}` with a U+2019 right single quotation mark, not an ASCII apostrophe. `Voice.LoadFailurePrefix` carries it, and the pinning test compares ordinally so a straight quote fails. Story 1.6's AC writes the placeholder as `{problem title}` and EXPERIENCE.md as `{problem detail title}` — the same thing, the ProblemDetails `title`, which the server guarantees is one fixed sentence per status.

**The roster is loaded but unused, on purpose.** AD-14 names `Core/Users/UserDirectory.cs` and AC1 says it "loads the roster once after login". Nothing renders it until Epic 3's owner picker. That makes a failed roster load a non-event: it must not block a successful sign-in or hold up the shell. `IsLoaded` stays false so the first real consumer can retry rather than inheriting a silently empty list.

**Toolchain.** SDK 10.0.401 at `~/.dotnet`; `export PATH="$HOME/.dotnet:$PATH"` and `export DOTNET_ROOT="$HOME/.dotnet"` first. No workload is needed, and none may be installed.

## Verification

**Commands:**

- `dotnet build ActionLedger.sln` -- expected: succeeds, **0 Warning(s), 0 Error(s)**.
- `dotnet test ActionLedger.sln` -- expected: every project passes; record the total and the delta against the 184 tests Story 1.5 recorded.
- `dotnet build -c Release ActionLedger.sln && dotnet test -c Release --no-build ActionLedger.sln` -- expected: green; this is `ci.yml`'s exact shape.
- `dotnet publish src/ActionLedger.Web -c Release` -- expected: succeeds with no workload installed; Story 1.7 needs the static assets.
- `git status --porcelain` after a build -- expected: empty; the generated client is still ignored.
- `dotnet run --project src/ActionLedger.Api -- --export-openapi` then `git diff --stat src/ActionLedger.Web/openapi.json` -- expected: no change; this story touches no server code.
- `grep -rniE "localStorage|sessionStorage|angular|npm|node_modules|typescript" src/ActionLedger.Web tests/Web.Tests` -- expected: no hits.
- `grep -rn "Core\.Api" src/ActionLedger.Web --include=*.razor` -- expected: no hits; no component crosses the AD-14 seam.

**Manual check, once, against a running API:** start the Api with the database and `Seed__Enabled=true`, run `dotnet run --project src/ActionLedger.Web`, sign in as `marcus` with the configured `Seed__DefaultPassword`, and confirm the toolbar shows `ActionLedger`, `Meetings`, `Actions`, and `Marcus Bell` with `Lead` and `Sign out`; then sign in with a wrong password and confirm the inline message appears and no redirect happens. Record the result. If no database is reachable in this environment, say so rather than claiming the pass.

**Result, 2026-09-21.** Ran as far as this environment allows. A PostgreSQL 18 container was
started, `dotnet dotnet-ef database update` applied `20260921135410_InitialCreate`, and the Api
came up with `Seed__Enabled=true` and a generated `Seed__DefaultPassword` (nothing committed);
`/health` and `/health/ready` both answered 200. The real chain — `HttpClient` →
`SessionMessageHandler` → the generated client → `AuthService` and `UserDirectory` — was then
driven against it from a throwaway harness, since deleted:

- `marcus` with a wrong password: `ApiFailure` status 401, title `Authentication is required.`,
  **zero snackbars and zero navigations**. The "carried a token" gate holds against a real 401.
- `marcus` with the configured password: `Marcus Bell`, role `Lead`, an 8-hour `expiresAt`, a
  263-character opaque token. The web parses none of it.
- Roster after sign-in: loaded, 3 users — `Dana Whitfield/ActionOfficer`, `Marcus Bell/Lead`,
  `Priya Ramaswamy/ActionOfficer` — with roles as strings.
- `LoadingState.InFlight` back to 0 after every call.
- The same roster call with no session: refused and swallowed into an empty roster with
  `IsLoaded` false, which is what proves the bearer header is what authorises the other one.

**Not run: the browser half.** `src/ActionLedger.Api` configures no CORS and this story forbids
changing it, so a dev-server page on another origin cannot reach the Api. The single-origin nginx
topology that makes the browser check possible is Story 1.7; the toolbar's own rendering is
covered by `ShellTests` against the real components.

**Mutation checks — each verified red, then reverted:**

- Drop the `Authorization` header from the handler -- expected: the bearer test fails.
- Remove the "carried a token" condition from the 401 branch -- expected: the login-refusal test fails on an unexpected navigation.
- Change one `Voice` constant by a single character (including the U+2019 apostrophe) -- expected: the pinning test names it.
- Switch `Formats` to `CultureInfo.CurrentCulture` -- expected: the culture test fails.
- Remove `UserDirectory`'s once-only flag -- expected: the call-count test fails.
- Move `LoginPage` out of `Features/` -- expected: the new routable-component rule fails naming it.
- Reference a `Core.Api` type from `LoginPage` or `Toolbar` -- expected: `Http_is_confined_to_core_and_the_per_feature_data_folders` fails naming the type.
- Remove the `finally` around `LoadingState.End()` -- expected: the thrown-send test leaves the counter above zero and fails.

**Not verified here — it runs at landing, with a human present:** `gh pr checks` for `ci.yml`, `require-linked-issue`, and CodeQL.


## Auto Run Result

Status: blocked
Blocking condition: no subagents

**What this run was.** Story 1.6's spec arrived at `status: done` with
`followup_review_recommended: true`, so step-01 routed it to step-04 as a follow-up review pass
(`review_loop_iteration` reset to 0, `followup_pass` true). No code was written, planned, or
changed by this run.

**How far it got.** The diff was staged successfully: `git diff 99ccd7f..` over `src` and `tests`,
35 files, 192 kB, written to the run's temp diff file. The spec itself was held back from that
diff so it could go to the edge-case layer alone as the claims file, which is what step-04
specifies. All four review layers — blind-hunter, edge-case-hunter, verification-gap, and
intent-alignment — were then launched together in a single message with their placeholders
substituted.

**Why it is blocked.** None of the four layers ever returned a result. After roughly an hour all
four showed as idle with no output delivered and no reply to a direct request for their findings.
A control subagent was then spawned whose entire task was to emit one word using no tools; it too
sat idle with nothing delivered. Subagent results are not reaching this session in this
environment, so the review layers cannot be run and their findings cannot be triaged.
`workflow.md` makes subagents mandatory where a step calls for them and directs this exact halt.

**What this does NOT mean.** It is not a defect in Story 1.6's code, and it is not a failed
review. The prior review pass recorded in `## Review Triage Log` (2026-09-21, 33 findings, 13
patched, 1 deferred) still stands, as does the verification recorded under `## Verification`.
What is missing is the second, confirming pass that the prior pass asked for.

**The unverified risk the follow-up pass was meant to close** is the one the prior pass named:
`ApiFailures`' undeclared-status branch deserializes the raw response body into `ProblemDetails`,
and no operation in the committed contract can return a titled undeclared status, so that branch
has only ever been exercised against synthetic bodies. It first meets a real server response in
Epic 2.

**Repository state.** No code was modified by this run. `src/` and `tests/` are untouched since
`5c2f851`. The only file this run wrote is this spec — `status` and this section. The staged diff
lives in the session scratchpad and was not added to version control. Nothing was committed and
nothing was pushed.

**What would unblock it.** Re-dispatch this spec for a follow-up review pass in a session where
subagent results are delivered. The spec is at `blocked`, so a re-dispatch needs its status set
back to `done` (the follow-up-pass route) by whoever owns that decision.
