---
title: 'Story 2.2 — Meeting List, New meeting dialog, and notes paste area'
type: 'feature'
created: '2026-09-22'
baseline_revision: '96c714942a5517a02c4b1238581b4630896308ca'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-2-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-2-1-meeting-and-immutable-notes-api.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-ActionLedger-2026-09-20/EXPERIENCE.md'
warnings: ['oversized']
deferred:
  - summary: >-
      MeetingsService's DI registration in Program.cs is never executed by any test, so a
      registration that was removed or given the wrong lifetime would not fail the build.
    evidence: |-
      Nothing in the suite runs `Program.cs`; every Web.Tests fixture registers the service into
      its own bUnit container instead. Not caused by this story: `AuthService` has carried the
      identical gap since Story 1.6, and `ApiClientRegistrationTests` covers
      `AddActionLedgerApiClient` only. Settling it means giving the composition root a shape a
      test can resolve against, which is a change to `Program.cs` and to how every feature
      registers itself rather than to this story's code.
    location: >-
      src/ActionLedger.Web/Program.cs:25
    severity: low
  - summary: >-
      The generated DateFormatConverter parses and writes meeting dates with the browser's
      culture, so a non-Gregorian calendar reads and sends the wrong year or fails outright.
    evidence: |-
      Verified by probe under CurrentCulture, against the real converter's two lines
      (DateTimeOffset.Parse(dateTime) and value.ToString("yyyy-MM-dd"), both without a format
      provider):

        th-TH  the server's "2026-09-21" parses to 1483-09-21 and MeetingsService maps it to
               DateOnly(1483, 9, 21); an outbound 2026-10-03 is written as "2569-10-03".
        ar-SA  DateTimeOffset.Parse("2026-09-21") throws FormatException: "String '2026-09-21'
               was not recognized as a valid DateTime."

      The FormatException is raised inside JSON deserialization, so it is not an ApiException,
      an HttpRequestException, or an OperationCanceledException — the three arms
      MeetingsService.CallAsync catches. It escapes the seam as an unhandled component
      exception and takes the Meeting List and Meeting Detail to the Blazor error UI.

      Not caused by this story: the converter is generated code from Story 2.1's contract and
      is off-limits here, and the app sets no culture policy at all (no
      DefaultThreadCurrentCulture anywhere in src/). The seam cannot fix it either — the
      converter parses before MeetingsService sees the value and formats after it hands one
      over. The fix is a composition-root globalization decision (for example pinning
      InvariantCulture in Program.cs) that applies to every feature, not just Meetings.

      That the repo already tests Formats under ar-SA and de-DE is what makes this reachable
      rather than theoretical: those cultures are ones the project has decided to care about.
    location: >-
      src/ActionLedger.Web/Core/Api/ActionLedgerApiClient.g.cs:1769 (consumed at
      src/ActionLedger.Web/Features/Meetings/Data/MeetingsService.cs:162,169)
    severity: high
  - summary: >-
      The title link's @onclick:preventDefault cannot be observed by bUnit, so removing it
      degrades every title click to a full page reload with the suite still green.
    evidence: |-
      MeetingListPageTests.Clicking_the_title_link_navigates_exactly_once asserts
      Assert.Single(Navigation.History), which covers the handler and stopPropagation — remove
      either and the count goes to zero or two. Nothing is sensitive to preventDefault, because
      BunitNavigationManager never follows an anchor's default action.

      Delete the attribute and both link tests pass unchanged. In a browser the click falls
      through to the href as a document navigation, which reboots the WebAssembly runtime; the
      spec's own Design Notes record that SessionState holds the token in memory only, so that
      reload signs the user out.

      Only a real browser can observe a default action. tests/Web.E2E is still the wiring
      placeholder AD-18 reserves for Playwright — its single test asserts an assembly name — so
      this belongs with that suite rather than with this story.
    location: >-
      src/ActionLedger.Web/Features/Meetings/MeetingListPage.razor:78
    severity: medium
---

<intent-contract>

## Intent

**Problem:** Story 2.1 published the four Meeting operations in the committed contract, but the web
app calls none of them: `/meetings` still lands on `NotFoundNotice`, `StubApiClient`'s Meeting
methods throw "Story 2.2 is what makes the web app call them", and there is no screen anywhere that
can create a Meeting or paste its notes. Every later Epic 2 and Epic 3 screen hangs off Meeting
Detail, so nothing downstream has a surface to attach to.

**Approach:** Add the `Meetings` feature on the Epic 1 web rails (AD-14 seam, `Voice`, `Formats`,
`LoadFailure`, `NotFoundNotice`, `SessionState`): one `MeetingsService` data service wrapping the
generated client, a `MeetingListPage` at `/meetings` with the `MudTable` and the `New meeting`
dialog, and a `MeetingDetailPage` at `/meetings/{id}` carrying the meeting fields and the
write-once notes paste area with its confirm dialog. This is also the app's first protected route,
so the route-level auth check EXPERIENCE.md describes lands here.

## Boundaries & Constraints

**Always:**

- AD-14 seam: HTTP and every `ActionLedger.Web.Core.Api` type stop inside
  `Features/Meetings/Data/`. Pages, dialogs and tests above that folder see only web-owned records
  and `ApiFailure`. `WebStructureTests.Http_is_confined_to_core_and_the_per_feature_data_folders`
  is the build-enforced version of this.
- AD-14 naming: every `@page` component is `<Noun>Page` and sits directly in
  `ActionLedger.Web.Features.Meetings` — not in `.Data`, not in `Shared/`.
  `WebStructureTests.Every_routable_component_is_a_page_under_a_feature_folder` fails otherwise.
- UX-DR20: every user-visible string is a `Voice` constant, and every new constant gets a row in
  `VoiceAndFormatsTests.VoiceConstants` — `Every_voice_constant_is_pinned` compares the pinned set
  against the class by reflection, so an unpinned constant fails the build. No exclamation marks,
  no emoji, ASCII plus U+2019 only.
- Dates render only through `Formats.Date` / `Formats.Instant`. No relative time anywhere.
- Immutability is structural in the UI, not conditional: once `notes` is non-null the detail page
  renders text and never a textarea, a Save control, or any other edit affordance.
- Route-level auth check: both pages redirect an unauthenticated visitor to `/login` on
  initialise, capturing the attempted route via `SessionState.CaptureAttemptedRoute` first, and
  render nothing in that pass (a navigation is not an unmount — `LoginPage` already shows the
  shape).
- Global response behaviour stays in `SessionMessageHandler`: the progress bar, the 401 redirect,
  and the 403/409 snackbars are already wired for every request and must not be re-implemented per
  page.
- Writes disable their button while in flight (`MudButton Disabled`), so a double click cannot
  issue a second request.
- Every field has a visible label; the notes textarea and its live count are associated through
  `aria-describedby`; dialogs are `MudDialog` opened through `IDialogService` so MudBlazor's own
  focus trap and focus return apply.

**Never:**

- No change to `src/ActionLedger.Api`, `src/ActionLedger.Application`, `src/ActionLedger.Domain`,
  `src/ActionLedger.Infrastructure`, or `src/ActionLedger.Web/openapi.json`. This story consumes
  the Story 2.1 contract unchanged, so `OpenApiSnapshotTest` must stay green without an export.
- No Run extraction button, no run list, no provider/model caption, no Review Screen link. Those
  are Stories 2.5–2.6 and would need endpoints that do not exist.
- No notes edit, replace, or delete affordance, and no client-side "are you sure you want to
  change" path — there is no such operation in the contract.
- No new package. bUnit + xunit.v3 + MudBlazor 9.10.0 are what is available (NFR8, NFR9).
- No sixth folder under `src/ActionLedger.Web/Features/`
  (`WebStructureTests.No_feature_folder_outside_the_five_ad14_names_exists`).
- No client-side date arithmetic, no client-side re-sort of the page the server returned, and no
  client-side recomputation of `runCount` / `trackedActionCount` (they are `0` until 2.5/3.1).
- No `sprint-status.yaml` edit.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| List loads | signed in, navigate to `/meetings` | `MudTable` of Title, Date, Runs, Tracked Actions in the server's order, page size 50, paginator rendered, "New meeting" button in the header | No error expected |
| List is empty | server returns `total: 0` | "No meetings yet." and a primary "New meeting" button; no table rows | No error expected |
| List load fails | `GET /api/v1/meetings` answers 500 or the transport fails | `LoadFailure` notice reading `Couldn’t load. {problem title}` with Retry; Retry re-requests the same page | `ApiFailure` from the service; the table body stays empty |
| Open a meeting | row click, or Enter on the row's title link | navigates to `/meetings/{id}` | No error expected |
| Page 2 | paginator next, 50+ meetings | requests `page: 2, pageSize: 50` (MudBlazor's `TableState.Page` is 0-based; the API is 1-based) | a failed page shows the same `LoadFailure` notice |
| Create a meeting | dialog with Title, Date, ≥0 attendees, press Create | `POST /api/v1/meetings`, dialog closes, navigates to `/meetings/{new id}` | No error expected |
| Create with missing fields | blank Title, or no Date | validation message under the offending field; no request is sent | client-side, before the call |
| Create with an over-long value | 201-char Title, or a 101-char attendee entry | validation message under that field; the attendee is not added as a chip | client-side |
| Create is refused | duplicate title+date → 409, or 400 | dialog stays open, Title/Date/attendees retained, the problem title renders as an inline error | `ApiFailure` rendered inline; the global handler's own 409 snackbar is left alone |
| Detail without notes | `notes` is null | meeting fields, plus the paste area: multiline field, live `{n} / 50,000` count, caption "Notes cannot be changed after saving", "Save notes" disabled while the text is empty | No error expected |
| Save notes | non-empty text, press "Save notes", confirm | confirm dialog repeats "Notes cannot be changed after saving"; on confirm `PUT /api/v1/meetings/{id}/notes`; the area becomes read-only `pre-wrap` text with `Saved {ts} UTC, immutable` | No error expected |
| Save notes, cancelled | confirm dialog dismissed | no request is sent; the textarea and its text are untouched | No error expected |
| Save notes fails | 500 or transport failure | snackbar carrying the problem title with a Retry action; the typed text is retained and the area is still editable | `ApiFailure`; nothing is marked saved |
| Save notes conflicts | 409 (notes already attached) | the page re-reads the meeting and renders the stored notes read-only | the global handler raises "Already changed. Reloading."; the page performs the reload |
| Detail with notes | `notes` is non-null | read-only `white-space: pre-wrap` text plus the immutable caption; no textarea, no Save control anywhere in the markup | No error expected |
| Detail, unknown id | `GET /api/v1/meetings/{id}` answers 404 | `NotFoundNotice` with its link to `/meetings` | 404 is distinguished from other failures by `ApiFailure.StatusCode` |
| Detail load fails | 500 or transport failure | `LoadFailure` notice with Retry, which re-reads the meeting | `ApiFailure` |
| Unauthenticated visitor | no session, navigate to `/meetings/{id}` | redirected to `/login`; after signing in, restored to `/meetings/{id}` | route-level check, before any request |

</intent-contract>

## Code Map

Read these before writing anything. Every one is a pattern this story copies rather than invents.

**Copy the shape from (Story 1.6's web rails):**

- `src/ActionLedger.Web/Features/Auth/Data/AuthService.cs` — the AD-14 data-service exemplar and
  the exact four `catch` arms every call needs: `ApiException`, `HttpRequestException`, and
  `OperationCanceledException when (!cancellationToken.IsCancellationRequested)`. Nothing generated
  escapes the file.
- `src/ActionLedger.Web/Core/Auth/SignInOutcome.cs` — the outcome-record shape
  (`Succeeded`/`Failed` factories, nullable `Value`/`Failure`) that `MeetingsService` mirrors, and
  the web-owned-record convention (`SignedInUser`).
- `src/ActionLedger.Web/Features/Auth/LoginPage.razor` — the routable-component shape: `h1` for
  `FocusOnNavigate`, `@if` around the whole body so a redirecting page renders nothing, `inFlight`
  guard on the submit, `ApiFailure? failure` rendered through `<LoadFailure>`, and the
  `OnInitialized` route-level auth check (this story inverts it: redirect when *not* signed in).
- `src/ActionLedger.Web/Core/Errors/ApiFailures.cs` — `From(Exception)` already covers all four
  shapes including a 404/409 arriving as a bare `ApiException` with the problem document sitting
  undeserialised in `Response`. Call it; do not branch on exception type in the service.
- `src/ActionLedger.Web/Core/Errors/ApiFailure.cs` — `StatusCode` is `0` for a transport failure,
  which is how the detail page tells a 404 from "no response".
- `src/ActionLedger.Web/Shared/LoadFailure.razor:12` — `LoadFailure.RetryId` is the stable id the
  tests click. Reuse the component; do not retype the sentence.
- `src/ActionLedger.Web/Shared/NotFoundNotice.razor` — already defaults to `/meetings` +
  `Voice.Meetings`, so the detail page's 404 branch renders `<NotFoundNotice />` bare.
- `src/ActionLedger.Web/Core/Voice/Voice.cs` — the constant + XML-doc convention, and
  `LoadFailurePrefix` as the precedent for a sentence declared as a prefix/suffix pair.
- `src/ActionLedger.Web/Core/Formatting/Formats.cs` — `Instant` already emits
  `2026-09-21 14:03 UTC`, so the immutable caption is `Voice.NotesSavedPrefix + Formats.Instant(…)
  + Voice.NotesSavedSuffix`. `Date` takes a `DateOnly`.
- `src/ActionLedger.Web/Core/Auth/SessionState.cs:79` — `CaptureAttemptedRoute` / `TakeAttemptedRoute`
  are already built and already exercised by the 401 path; the guard added here is their first
  deliberate caller.
- `src/ActionLedger.Web/Core/Auth/SessionMessageHandler.cs:105` — how the current route is read
  (`"/" + navigation.ToBaseRelativePath(navigation.Uri)`) and why `/login` and `/` are excluded.
  Copy the expression, not the exclusion (the Meetings routes are never either).
- `src/ActionLedger.Web/Program.cs:25` — where a per-feature data service is registered
  (`AddScoped<AuthService>()`); add `MeetingsService` beside it.

**Contract shapes (generated, seen only inside `Features/Meetings/Data/`):**

- `src/ActionLedger.Web/openapi.json` — read, never edited. The four operations are
  `CreateMeetingAsync(CreateMeetingCommand)`, `ListMeetingsAsync(int? page, int? pageSize)`,
  `GetMeetingAsync(Guid id)`, `SaveMeetingNotesAsync(Guid id, SaveMeetingNotesCommand)`; each also
  has a `CancellationToken` overload. Signatures are visible verbatim in `StubApiClient`.
- `src/ActionLedger.Web/Core/Api/ActionLedgerApiClient.g.cs:1296,1366,1440` (git-ignored, generated
  at build) — `CreateMeetingCommand.MeetingDate`, `MeetingSummaryDto.MeetingDate` and
  `MeetingDetailDto.MeetingDate` are **`DateTimeOffset`** carrying a `DateFormatConverter`, not
  `DateOnly`. Map at the seam: `DateOnly.FromDateTime(dto.MeetingDate.Date)` inbound, and
  `new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)` outbound.
  `MeetingDetailDto.Attendees` is `ICollection<string>`; `MeetingDetailDto.Notes` is a nullable
  `MeetingNotesDto` with `Id`, `Text`, `Sha256`, `SavedAt`.

**MudBlazor 9.10.0 surface this story leans on** (`~/.nuget/packages/mudblazor/9.10.0/lib/net10.0/MudBlazor.xml`):

- `MudTable<T>.ServerData` — `TableState` in (`Page` is **0-based**, `PageSize`), `TableData<T>`
  out (`Items`, `TotalItems`), plus a `CancellationToken` to forward. `MudTable<T>.ReloadServerData()`
  is what Retry calls. `MudTableBase.RowsPerPage` is the page size; `MudTablePager` goes in
  `PagerContent`.
- `MudTable<T>.OnRowClick` — `EventCallback<TableRowClickEventArgs<T>>`, the mouse half of "row
  click opens detail".
- `MudChipSet<string>` with `AllClosable` and `OnClose`, holding `MudChip<string>` children — the
  attendee chip input. `MudChip<T>.Text`/`.Value`.
- `IDialogService` and `MudDialog`; `MudDialogProvider` is already rendered by
  `MainLayout.razor:24`, and `AddMudServices()` already registers the service.

**Read-only evidence (tests that will judge the change):**

- `tests/Architecture.Tests/WebStructureTests.cs:49,96,185` — the HTTP-seam rule, the
  `<Noun>Page`-in-a-feature-folder rule, and the five-folder rule. All three see this story.
- `tests/Web.Tests/VoiceAndFormatsTests.cs:22` — `VoiceConstants` must gain a row per new `Voice`
  constant, or `Every_voice_constant_is_pinned` goes red. Only `const string` fields are
  enumerated (`Declared()` at `:125`).
- `tests/Web.Tests/StubApiClient.cs:100-115` — the four Meeting methods currently throw
  `NoMeetingScreenYet()`. They must become recording stubs; `SignInGate` at `:64` is the pattern
  for holding a call open, and `Problem`/`Invalid`/`Bare`/`BareWithBody` are the failure factories.
- `tests/Web.Tests/LoginPageTests.cs` — the bUnit harness this story copies verbatim:
  `BunitContext`, `Services.AddMudServices()`, `JSInterop.Mode = JSRuntimeMode.Loose`, singletons
  for the stub client and `SessionState`, and `(BunitNavigationManager)Services.GetRequiredService<NavigationManager>()`
  for `Navigation.History`.
- `tests/Web.Tests/ShellTests.cs:38` — the same harness at layout level, for reference.
- `src/ActionLedger.Web/App.razor:2` — the comment claiming "/meetings and /actions land on
  NotFound until Stories 2.2 and 4.3 add them" is half-satisfied by this story and must be amended.
- `_bmad-output/planning-artifacts/ux-designs/ux-ActionLedger-2026-09-20/EXPERIENCE.md:86-88,112-113`
  — the Meeting table, New meeting dialog, and Notes paste area rows, verbatim, plus the empty-state
  copy.

**Tooling:**

- The SDK pinned by `global.json` (10.0.401) lives at `~/.dotnet` and is not on `PATH`; prefix
  commands with `export PATH="$HOME/.dotnet:$PATH"`.
- `dotnet test ActionLedger.sln` from the repository root runs all eight assemblies (the Story 2.1
  review pass confirmed this; the "zero tests ran" report from its implementation session did not
  reproduce).

## Tasks & Acceptance

**Execution:**

- `src/ActionLedger.Web/Core/Voice/Voice.cs` -- add the constants this story renders, each with an
  XML doc naming its EXPERIENCE.md row: `NewMeeting` ("New meeting"), `NoMeetings`
  ("No meetings yet."), `Title` ("Title"), `Date` ("Date"), `Runs` ("Runs"), `TrackedActions`
  ("Tracked Actions"), `Attendees` ("Attendees"), `Create` ("Create"), `Cancel` ("Cancel"),
  `Notes` ("Notes"), `SaveNotes` ("Save notes"), `NotesImmutable`
  ("Notes cannot be changed after saving"), `NotesSavedPrefix` ("Saved "), `NotesSavedSuffix`
  (", immutable"). -- UX-DR20: one vocabulary, pinned in one test.
- `src/ActionLedger.Web/Features/Meetings/Data/MeetingsService.cs` -- add the AD-14 seam for this
  feature: the web-owned records (`MeetingListItem`, `MeetingPage`, `MeetingDetail`,
  `MeetingNotes`), a generic `MeetingOutcome<T>` with `Succeeded`/`Failed` factories mirroring
  `SignInOutcome`, and four methods — `ListAsync(page, pageSize, ct)`, `CreateAsync(title, date,
  attendees, ct)`, `GetAsync(id, ct)`, `SaveNotesAsync(id, text, ct)` — each wrapping the generated
  call in the same four catch arms as `AuthService` and mapping through `ApiFailures.From`. Convert
  `DateTimeOffset` ⇄ `DateOnly` here and nowhere else.
  -- AD-14: this file is the only one in the feature that may name a generated type.
- `src/ActionLedger.Web/Features/Meetings/MeetingListPage.razor` -- add `@page "/meetings"`: the
  route-level auth check, an `h1` reading `Voice.Meetings`, the "New meeting" button that opens the
  dialog through `IDialogService` and navigates to `/meetings/{id}` on a non-null result, and a
  `MudTable<MeetingListItem>` driven by `ServerData` with `RowsPerPage="50"` and a `MudTablePager`.
  Columns Title, Date (`Formats.Date`), Runs, Tracked Actions; the Title cell holds an
  `<a href="/meetings/{id}">` so a keyboard user reaches and opens the row with Enter, and
  `OnRowClick` handles the mouse. `LoadFailure` above the table when the last load failed, with
  Retry calling `ReloadServerData()`; `Voice.NoMeetings` plus a primary "New meeting" button when
  the load succeeded with `Total == 0`. -- UX-DR12 and EXPERIENCE.md's Meeting table row.
- `src/ActionLedger.Web/Features/Meetings/NewMeetingDialog.razor` -- add the `MudDialog`: Title
  (`MudTextField`, required, max 200), Date (`MudDatePicker`, required), and an attendee chip input
  — a `MudTextField` whose Enter adds the trimmed value to a `MudChipSet<string>` when it is 1–100
  characters and rejects it inline otherwise, with `AllClosable` + `OnClose` removing a chip.
  Validation messages render under the offending field and `Create` sends nothing while any field
  is invalid. `Create` disables itself while in flight, calls `MeetingsService.CreateAsync`, and
  closes with the new id; on failure the dialog stays open with values retained and renders
  `ApiFailure.Title` inline. `Cancel` closes with no result.
  -- UX-DR12's New meeting dialog, and the "form values are retained" state pattern.
- `src/ActionLedger.Web/Shared/ConfirmDialog.razor` -- add the presentational confirm dialog:
  `Message` and `ConfirmLabel` parameters, a Cancel that closes with no result and a Confirm that
  closes with `true`, with stable ids on both buttons. It lives in `Shared/` because Epic 3's
  Reject, Complete, and Cancelled confirmations are the same dialog.
  -- "Irreversible writes confirm in a dialog" is a global interaction primitive, not a notes rule.
- `src/ActionLedger.Web/Features/Meetings/MeetingDetailPage.razor` -- add
  `@page "/meetings/{Id:guid}"`: the route-level auth check, the load with its three outcomes
  (loaded / `NotFoundNotice` on `StatusCode == 404` / `LoadFailure` + Retry otherwise), an `h1` of
  the meeting title, the fields block (Date via `Formats.Date`, attendees as chips or a dash when
  empty), and the notes region. Notes region without notes: a multiline `MudTextField` with a live
  `{n} / 50,000` count in an element the field names through `aria-describedby`, the caption
  `Voice.NotesImmutable`, and a `Save notes` button disabled while the text is empty or a save is
  in flight, which opens `ConfirmDialog` carrying the same sentence and only then calls
  `SaveNotesAsync`. On success it swaps to the read-only branch; on a 409 it re-reads the meeting;
  on any other failure it raises a snackbar carrying `ApiFailure.Title` with a Retry action and
  keeps the typed text. Notes region with notes: a `<pre>`-style block with
  `white-space: pre-wrap` and the caption
  `Voice.NotesSavedPrefix + Formats.Instant(savedAt) + Voice.NotesSavedSuffix`, and no textarea,
  Save control, or edit affordance rendered at all. -- UX-DR13's notes area and ADR-002 made
  visible.
- `src/ActionLedger.Web/wwwroot/css/app.css` -- add the one `white-space: pre-wrap` class the
  read-only notes block uses. Layout/placement only; `tokens.css` stays untouched because
  `ThemeAndTokenTests` pins all forty of its values.
  -- the same reason the progress-bar rule lives here rather than in the token file.
- `src/ActionLedger.Web/Program.cs` -- register `MeetingsService` as scoped beside `AuthService`.
  -- each feature registers its own data service; nothing is discovered by reflection.
- `src/ActionLedger.Web/App.razor` -- amend the comment that says `/meetings` lands on `NotFound`
  until Story 2.2. -- the comment is the only place that claim is written down.
- `tests/Web.Tests/StubApiClient.cs` -- replace the four `NoMeetingScreenYet()` throws with
  recording stubs: call counts, last arguments, settable results, settable `Throws` exceptions, and
  a gate task on `SaveMeetingNotesAsync` and `CreateMeetingAsync` so "disabled while in flight" is
  assertable. Leave the two health operations throwing.
  -- the repository takes no mocking package (NFR8, NFR9).
- `tests/Web.Tests/MeetingsServiceTests.cs` -- cover the seam: each of the four calls maps its DTO
  into the web-owned record (including `DateTimeOffset` → `DateOnly` both ways and a null `notes`),
  each failure shape (`ApiException<ProblemDetails>`, `ApiException<ValidationProblemDetails>`, a
  bare `ApiException` carrying an undeserialised problem body, `HttpRequestException`, and a
  `TaskCanceledException` the caller did not ask for) becomes an `ApiFailure` with the right
  `StatusCode` and `Title`, and a cancellation the caller *did* request still propagates.
  -- the matrix's error column, asserted where it is decided.
- `tests/Web.Tests/MeetingListPageTests.cs` -- bUnit: the table renders the four columns in the
  server's order with `Formats.Date` dates; the pager requests page 2 as `page: 2, pageSize: 50`;
  a zero-total load renders `Voice.NoMeetings` and a New meeting button and no rows; a failed load
  renders `LoadFailure` and Retry re-requests; a row click and the title link both navigate to
  `/meetings/{id}`; an unauthenticated render redirects to `/login`, captures the attempted route,
  and emits no table.
  -- the AC's list row, plus the route-level check.
- `tests/Web.Tests/NewMeetingDialogTests.cs` -- bUnit: a blank Title and a missing Date each show a
  message under their own field and send nothing; a 201-character Title and a 101-character
  attendee are refused inline; added attendees round-trip as chips and a closed chip is removed;
  Create sends exactly one `CreateMeetingCommand` carrying the trimmed title, the picked date, and
  the chip list; the button cannot fire twice while the call is outstanding; a 409 keeps the dialog
  open with values retained and renders the problem title.
  -- the AC's dialog row.
- `tests/Web.Tests/MeetingDetailPageTests.cs` -- bUnit: a meeting without notes shows the textarea,
  the live count against 50,000, `Voice.NotesImmutable`, and a `Save notes` button that is disabled
  while empty; pressing it opens a confirm dialog repeating that sentence and cancelling sends
  nothing; confirming sends exactly one `SaveMeetingNotesCommand` with the text byte-for-byte
  (leading whitespace and CRLF survive) and the area becomes the read-only caption block; a meeting
  that arrives with notes renders no `textarea` and no Save control anywhere in the markup; a 404
  renders `NotFoundNotice`; another failure renders `LoadFailure` with a working Retry; an
  unauthenticated render redirects to `/login`.
  -- the AC's notes row and ADR-002's shape claim, asserted on the rendered markup.
- `tests/Web.Tests/VoiceAndFormatsTests.cs` -- add one `VoiceConstants` row per new constant, with
  the literal EXPERIENCE.md writes. -- `Every_voice_constant_is_pinned` is the reason this file is
  edited rather than the reason it fails.

**Acceptance Criteria:**

- Given a signed-in Action Officer on `/meetings`, when the list loads, then a `MudTable` shows
  Title, Date, Runs, and Tracked Actions in the order the server returned, the page size is 50 with
  a paginator, and both a row click and Enter on the row's title reach `/meetings/{id}`.
- Given the New meeting dialog, when Create is pressed with a valid Title, Date, and attendee
  chips, then exactly one `POST /api/v1/meetings` is sent carrying those values and the app lands
  on the new meeting's detail page; when any field is invalid, a message appears under that field
  and no request is sent.
- Given a Meeting whose notes have never been attached, when the Action Officer types notes and
  presses "Save notes" and confirms in the dialog, then one `PUT /api/v1/meetings/{id}/notes` is
  sent with the text unchanged and the area is replaced by read-only `pre-wrap` text captioned
  `Saved {timestamp} UTC, immutable`.
- Given a Meeting that already has notes, when the detail page renders, then the markup contains no
  `textarea`, no Save control, and no other edit affordance for the notes.
- Given an unauthenticated visitor, when they navigate to `/meetings` or `/meetings/{id}`, then
  they are sent to `/login`, the attempted route is captured, and signing in returns them to it.
- Given the solution after this change, when `dotnet test ActionLedger.sln` runs, then every
  assembly is green — `WebStructureTests`, `VoiceAndFormatsTests`, `ThemeAndTokenTests`, and
  `OpenApiSnapshotTest` included — without any of them being weakened, and `src/ActionLedger.Web/openapi.json`
  and the four server rings are unchanged in `git diff`.

## Spec Change Log

## Review Triage Log

### 2026-09-22 — Review pass

- verdicts: 34 findings — high 0, medium 14, low 18, false 2, maybe-false 0
- findings:
  - `[medium]` `[patch]` `[blind-hunter]` The Save notes button never renders disabled while the PUT is outstanding — Confirmed: `SaveNotesAsync` sets `saving = true` only after `await Dialogs.ShowAsync`, and `ComponentBase.HandleEventAsync` renders once before the handler's first await and once after it completes, so `Disabled` is only ever evaluated with `saving == false`. `NewMeetingDialogTests:160` proves the pattern works when the flag is set before the first await, which is exactly the difference. Patched with the entry below.
  - `[medium]` `[patch]` `[edge-case]` Two fast Save notes clicks both pass the `saving` guard and open two stacked confirm dialogs — Confirmed, same root cause: the guard at the top of `SaveNotesAsync` reads a flag that is not set until after the dialog resolves, so the second click gets through and a second `PUT` follows the second confirm. EXPERIENCE.md bans modal stacks deeper than one. Patched: `saving` is now set before the dialog opens, cleared on dismissal, and rendered.
  - `[medium]` `[patch]` `[edge-case]` Claim check — the spec's "writes disable their button while in flight" is unsatisfied on the notes path — Confirmed, and the same defect as the two rows above. Patched with them.
  - `[medium]` `[patch]` `[blind-hunter]` `StubApiClient.SaveMeetingNotesGate` is declared, awaited, and never set by any test — Confirmed: the only two occurrences in `tests/` are its declaration and the `await` inside the stub. Patched: the gate now drives the in-flight test that was missing.
  - `[medium]` `[patch]` `[edge-case]` Claim check — the gate the spec asked for on `SaveMeetingNotesAsync` is unreached code — Confirmed, same defect as the row above. Patched with it.
  - `[medium]` `[patch]` `[verification-gap]` The notes save's in-flight guard has no test, so removing it stays green — Pre-verified by the layer and re-checked: dropping `|| saving` and `saving ||` leaves all 14 `MeetingDetailPageTests` passing because none holds a save open. Patched: a gated test asserts `disabled` mid-save and `SaveMeetingNotesCalls == 1` after a second click.
  - `[medium]` `[patch]` `[blind-hunter]` The failed-save snackbar's Retry cannot update the page — Confirmed: `options.OnClick = _ => SaveNotesAsync()` runs on MudBlazor's snackbar, outside the component's event pipeline, with no `InvokeAsync`/`StateHasChanged`, so a Retry that succeeds sets `detail` and never re-renders — the user keeps looking at a textarea over notes that are now immutable. Patched: the callback is dispatched through `InvokeAsync` and renders.
  - `[medium]` `[patch]` `[edge-case]` Snackbar Retry succeeds with no render queued — Confirmed, same defect as the row above. Patched with it.
  - `[medium]` `[patch]` `[verification-gap]` The snackbar's Retry is asserted as text, never as behaviour — Pre-verified: deleting `options.OnClick` leaves both assertions in `MeetingDetailPageTests:266` passing, since they only search the rendered markup for `Voice.Retry`. The repo's own standard for a retry affordance is the two `LoadFailure` tests, which click and assert the call count rises. Patched: the test now clicks the snackbar action and asserts a second save goes out.
  - `[medium]` `[patch]` `[blind-hunter]` Clicking a row's title link navigates twice — Confirmed: the anchor sits inside `<MudTd>` while `OnRowClick` is on the `<tr>`, so the click reaches `OpenRow` by bubbling and the anchor's own navigation runs too — two history entries for one URL, so one Back press leaves the user where they started. No test clicks the anchor: `:158` reads `href` only and `:172` clicks the `<tr>`. Patched: `@onclick:stopPropagation` on the anchor plus a test that clicks it.
  - `[medium]` `[patch]` `[blind-hunter]` `NewMeetingDialog.Validate()` never recomputes `attendeeError`, so the dialog can latch shut — Confirmed: after a 101-character attendee is refused and the field is then cleared, `CreateAsync` skips `AddAttendee` (empty draft), the stale error survives, `Validate` returns false, and Create silently does nothing under a message attached to an empty field. Patched: `Validate` clears the error when the draft is empty.
  - `[medium]` `[patch]` `[edge-case]` Over-long attendee refused, then field cleared, makes Create a permanent no-op — Confirmed, same defect as the row above. Patched with it.
  - `[medium]` `[patch]` `[verification-gap]` The `attendeeError is null` clause in `Validate` is guarded but never tested — Pre-verified: `NewMeetingDialogTests:88` only presses Enter and never clicks Create with an over-long draft still in the field. Patched: a case asserting `CreateMeetingCalls == 0` with the message under the field.
  - `[low]` `[patch]` `[edge-case]` A cancelled `ServerData` load escapes unhandled — Half confirmed, and this triage's own first verdict of `medium` was wrong. The escape is real: `MudTable.InvokeServerLoadFunc` calls `CancelToken()` on the prior request and awaits `ServerData` with no try/catch, while `MeetingsService.CallAsync`'s arm is filtered `when (!cancellationToken.IsCancellationRequested)` and declines the cancelled case. The claimed consequence is not: an async method letting an `OperationCanceledException` propagate completes its task in the `Canceled` state, and `Renderer.GetErrorHandledTask` (verified against the ASP.NET Core 10 source) skips the error boundary for exactly that state — `if (!taskToHandle.IsCanceled)`. Every route into this page arrives on a lifecycle or event task, so `#blazor-error-ui` is never reached and no reload is forced. Regraded `low`. The arm was kept as defence rather than reverted, because the guarantee it leans on belongs to the renderer and not to this page, and its comment was rewritten — it previously asserted the false consequence.
  - `[medium]` `[patch]` `[verification-gap]` The `/meetings/{Id:guid}` route template is never exercised through the router — Pre-verified: all 14 detail tests render the component directly with `Id` passed as a parameter, so the template is never parsed; renaming it leaves every row click, title link, and post-create navigation landing on `NotFoundNotice` with the suite green. Patched: a routed `ShellTests` case beside the `/meetings` one.
  - `[low]` `[patch]` `[blind-hunter]` Runs and Tracked Actions could be swapped and every test would still pass — Confirmed: `MeetingListPageTests:270-271` and `MeetingsServiceTests:69-70` pin both counts at `0`, so swapping the two arguments in `ToListItem` or the two `<MudTd>` bindings is undetectable. The "they are 0 until 2.5/3.1" rule is about what the server sends, not about fixture data. Patched: distinct non-zero fixture values, asserted.
  - `[low]` `[patch]` `[verification-gap]` The Runs / Tracked Actions mapping is pinned only at 0 — Pre-verified, same defect as the row above. Patched with it.
  - `[low]` `[patch]` `[blind-hunter]` `.al-notes` wraps at break opportunities only — Confirmed: `white-space: pre-wrap` with no `overflow-wrap`, so a pasted URL or path with no spaces overflows the container horizontally rather than wrapping. Patched: one `overflow-wrap: anywhere` declaration.
  - `[low]` `[patch]` `[blind-hunter]` `MeetingDetail.CreatedAt` is mapped and then never asserted — Confirmed: the service populates it and neither Get test checks it, so a wrong mapping ships unnoticed. Patched with a one-line assertion in each of the two existing tests rather than by deleting the field.
  - `[low]` `[patch]` `[blind-hunter]` `FailureId`'s doc claims an `aria-describedby` the markup never makes, and the notes field's `aria-describedby` omits the immutability sentence — Confirmed on both counts: no element references `FailureId`, and `aria-describedby` names only `NotesCountId`, so the one sentence that makes the write irreversible is the one thing not announced with the field. Patched: the attribute added in both places.
  - `[low]` `[patch]` `[blind-hunter]` The empty state's New meeting button is never clicked and shares its accessible name with the header's — Confirmed: `A_zero_total_renders_the_empty_state_and_no_rows` reads its text and stops, and both buttons present the identical name "New meeting" to a screen reader. Patched: the empty-state button is described by the "No meetings yet." text and the test now clicks it.
  - `[low]` `[patch]` `[edge-case]` Cancel is clickable while a create is outstanding — Confirmed: `Cancel` carries no `Disabled`, so pressing it mid-request cancels the dialog while the meeting is still created server-side, and the list neither navigates nor refreshes. Patched: `Disabled="@inFlight"` on Cancel.
  - `[low]` `[patch]` `[edge-case]` New meeting can be double-clicked into two stacked dialogs — Confirmed: `OpenNewMeetingAsync` has no guard and the button is never disabled, so two clicks open two dialogs, each able to create a meeting. EXPERIENCE.md bans modal stacks deeper than one. Patched: the button is disabled while a dialog is open.
  - `[low]` `[reject]` `[blind-hunter]` A paste over 50,000 characters is silently truncated into an immutable record — Real but rejected as low: 50,000 characters is roughly 8,000 words against a fixture catalog built for ~2,000-word notes, and the count reads `50,000 / 50,000` at the cap, which is itself a signal. The fix needs a new over-limit branch, a new `Voice` constant, and a test — more than a direct correction of what is there.
  - `[low]` `[reject]` `[blind-hunter]` Neither page passes a `CancellationToken` to `MeetingsService` — Real but rejected as low: navigating away mid-request lands a field assignment on a component Blazor has already disposed, and Blazor drops the resulting render, so no user-visible harm was demonstrated. The fix adds a `CancellationTokenSource`, `IDisposable` on two pages, and disposal wiring.
  - `[low]` `[reject]` `[blind-hunter]` `MeetingDetailPage` never reloads when `Id` changes — Real but rejected as low: nothing in the app links one meeting detail to another, and editing the address bar reloads the WebAssembly host, so the component is never reused across two `/meetings/{id}` routes today. The fix adds a `loadedId` field and an `OnParametersSetAsync` branch guarding a state not shown reachable.
  - `[low]` `[reject]` `[edge-case]` In-app navigation between two `/meetings/{id}` routes reuses the component — Same defect as the row above, rejected for the same reason.
  - `[low]` `[reject]` `[edge-case]` A draft longer than `NotesMaxLength` reaches the server as a 400 — Rejected: `MudTextField.MaxLength` emits the HTML `maxlength` attribute (the literal is present in `MudBlazor.dll`), which browsers enforce on paste as well as typing, so `draft` cannot exceed the limit through the UI. The proposed guard defends a state unreachable from a browser.
  - `[low]` `[reject]` `[edge-case]` `items` or `attendees` arriving as JSON null throws inside the seam — Rejected: both are `required` in the committed contract and the server always populates them. Adding `?? []` guards a contract violation with no demonstrated path, and the rule would have to spread to every collection the seam maps.
  - `[low]` `[reject]` `[edge-case]` Claim check — `MudDatePicker`'s `DateFormat` is a second place a date's shape is decided — Real but rejected as low: `Formats.Date` is pinned ordinally by two tests so it cannot drift silently, and `DateFormat` governs what the picker parses and shows in its own input, not the rendering of a stored date. A shared constant would add public surface for an inert duplication.
  - `[low]` `[reject]` `[verification-gap]` Other findings — the snackbar's Retry re-opens the confirm dialog before resending — Rejected: EXPERIENCE.md makes confirmation a property of the irreversible write itself, not of the first attempt at it, so re-confirming a retried Save notes is the specified interaction rather than a defect.
  - `[low]` `[defer]` `[verification-gap]` `MeetingsService`'s DI registration is never executed by any test — Real and pre-existing in shape: nothing runs `Program.cs` and every fixture registers the service itself. `AuthService` has carried the identical gap since Story 1.6, so closing it means reshaping the composition root rather than editing this story. Deferred.
  - `[false]` `[reject]` `[blind-hunter]` Duplicate attendee names remove the wrong chip — Refuted: `attendees` holds strings compared by value, and both chips carry identical `Text` and `Value`, so removing the first equal element leaves a list identical to the one that removing the second would produce. No observable difference and no data the user did not ask to remove.
  - `[false]` `[reject]` `[edge-case]` `List.Remove` drops the first match, so the wrong chip disappears — Refuted on the same grounds as the row above.

Intent-alignment auditor: descriptive only, no actionable finding. It reports the diff implements the reading under which the orchestration protocol's conditional clause is dormant — Story 2.2 has no acceptance criterion requiring a human action outside the repo, so `operator_actions` is correctly absent, `sprint-status.yaml` is untouched, and `blocked` is unused. Its secondary observations about expectations stated at one surface and checked at another duplicate the snackbar-Retry and keyboard-Enter findings already logged above.

### 2026-09-22 — Review pass (follow-up)

- verdicts: 29 findings — high 1, medium 7, low 17, false 4, maybe-false 0
- layers reported 28 findings (blind-hunter 13, edge-case 10, verification-gap 5, intent-alignment 0);
  blind-hunter's label-association finding carried two separate claims — the missing
  `aria-labelledby` and `Voice.NoValue` being a bare dash — so each has its own row below.
- findings:
  - `[medium]` `[patch]` `[blind-hunter]` Meeting Detail renders no `h1` while the meeting is loading — Confirmed, and worse than filed: the heading sat inside the `detail is not null` branch, so the route also had no `h1` in the load-failure state, permanently, because `LoadFailure` brings none of its own. `App.razor`'s own comment states the contract ("every routable component … has to render one"), and `FocusOnNavigate` looks exactly once, after the first render following the navigation — which lands while the GET is in flight. `MeetingDetailPageTests:405` only asserts an `h1` after a retry has succeeded, so neither hole was pinned. Patched: the heading is hoisted out of the load branches and reads `detail?.Title ?? Voice.Meeting`, with a gated test for the loading state and one for the failure state.
  - `[medium]` `[patch]` `[edge-case]` No `h1` outside the branch chain when `GetAsync` fails or is in flight — Confirmed, same defect as the row above. Patched with it.
  - `[medium]` `[patch]` `[blind-hunter]` The inbound date conversion is only ever tested at zero offset — Confirmed, and it guards more than it looked like. `DateFormatConverter.Read` is `DateTimeOffset.Parse` with no format provider, so a bare `"2026-09-21"` arrives carrying the browser's own offset, not UTC — verified by probe: `2026-09-21T00:00:00-04:00` under `en-US`. `ToDate`'s `value.Date` is therefore correct, while `value.UtcDateTime.Date` would render `2026-09-20` for every user east of UTC. Every fixture in `MeetingsServiceTests`, `StubApiClient` and both page test files is `TimeSpan.Zero`, where the two agree, so that regression ships green. Patched: a `+09:00` case asserting the day the server wrote. Mutation-checked — the test goes red under `value.UtcDateTime.Date`.
  - `[medium]` `[patch]` `[edge-case]` A whitespace-only draft can be saved into the one irreversible write — Confirmed: `Disabled="@(draft.Length == 0 || saving)"` counts spaces, and the server's `MinimumLength = 1` counts them too, so three spaces pass both ends and attach blank notes to the meeting for good. Patched: both guards now test `draft.Trim().Length`, while the payload still goes byte-for-byte untrimmed — this gates the button, it does not tidy what ADR-002 hashes.
  - `[medium]` `[patch]` `[verification-gap]` The 50,000-character cap rests entirely on an unasserted `MaxLength` attribute — Pre-verified and re-checked: no test in the suite reads any element's `maxlength`, and neither the Save guard nor `SaveNotesAsync` looks at the upper bound. Remove the attribute and everything stays green while a 60,000-character paste reaches the server as a guaranteed 400. Patched: a test asserting the textarea renders `maxlength="50000"`. Mutation-checked.
  - `[medium]` `[patch]` `[verification-gap]` `The_read_only_notes_block_preserves_the_whitespace_it_was_given` asserts a class name, not whitespace preservation — Pre-verified: bUnit applies no stylesheet, so the test observes only the class attribute, and deleting the `.al-notes` rule leaves it and both `TextContent` tests green while the browser collapses every newline the server hashed. The repo already pins the sibling class (`ShellTests:317-326` for `.al-progress`). Patched: `.al-notes {` pinned beside it. Mutation-checked by renaming the rule.
  - `[medium]` `[defer]` `[verification-gap]` `@onclick:preventDefault` on the title link is unobservable in bUnit — Pre-verified: `BunitNavigationManager` never follows an anchor's default action, so deleting the attribute leaves both link tests passing while a real browser turns every title click into a document navigation that reboots the runtime and, per this spec's own Design Notes, signs the user out. Deferred to the Playwright suite AD-18 reserves; `tests/Web.E2E` is still the placeholder.
  - `[high]` `[defer]` `[edge-case]` The generated date converter is culture-sensitive, so a non-Gregorian calendar corrupts or breaks every meeting date — Confirmed by probe, and the most serious finding of the pass. Under `th-TH` the server's `"2026-09-21"` parses to `1483-09-21` and an outbound `2026-10-03` is written as `"2569-10-03"`; under `ar-SA` the parse throws `FormatException`, which is none of the three arms `CallAsync` catches, so it escapes the seam and takes both Meeting screens to the Blazor error UI. Deferred rather than patched: the two offending lines are generated code this story may not touch, the seam cannot intercept either direction, and the fix is a composition-root globalization policy affecting every feature. That the repo already tests `Formats` under `ar-SA` and `de-DE` is what makes it reachable rather than theoretical.
  - `[low]` `[patch]` `[blind-hunter]` A successful notes save announces nothing and drops focus — Confirmed: the read-only branch removes the Save button in the same render, and that button is what MudBlazor returns focus to when the confirm dialog closes (EXPERIENCE.md:139, "Focus returns to the triggering control after a dialog closes"), so focus falls to `<body>` with nothing said. Every other outcome of this write speaks — failure raises a snackbar, 409 raises the global sentence. Patched with a success snackbar, which is the house pattern (EXPERIENCE.md:98) and rides the provider already mounted in `MainLayout` — the one live region reliably announced for a region that has only just appeared. Mutation-checked.
  - `[low]` `[patch]` `[edge-case]` The failure snackbar's Retry still fires after a later save has attached notes — Confirmed: the snackbar outlives the failure, `draft` is never cleared, and the guard read only `saving || draft.Length == 0`, so a stale Retry opened a confirm dialog over the read-only region and sent a second PUT at a write the contract allows once, which the server then refused with a 409. Patched: `detail?.Notes is not null` is now the third guard, with a test driving the real sequence — fail, succeed, then press the stale Retry. Mutation-checked.
  - `[low]` `[patch]` `[blind-hunter]` The detail page's captions are not associated with their values — Confirmed: "Date" and "Attendees" are plain `MudText` paragraphs above their values, whose ids existed only for the tests, so a screen reader reads four unrelated lines. Patched: each caption gets an id and each value an `aria-labelledby`. Mutation-checked.
  - `[low]` `[patch]` `[blind-hunter]` `OnAttendeeKeyDown`'s comment asserts a guard the code does not have — Confirmed: it claimed MudBlazor's Enter-to-submit "would otherwise send the form with the draft unadded", but there is no `preventDefault` anywhere on the handler; it is harmless only because `MudDialog` renders no `form`. Patched: the comment now says why no modifier is needed and what a later story wrapping this in a form has to add.
  - `[low]` `[patch]` `[blind-hunter]` `NewMeetingDialog` claims `inFlight` without the explicit render both pages document as necessary — Confirmed: `MeetingDetailPage.SaveNotesAsync` and `MeetingListPage.OpenNewMeetingAsync` each set their flag and call `StateHasChanged()` with a paragraph on why the event pipeline is not enough for a non-event caller; `CreateAsync` relied on being reached from a rendered `OnClick`. Patched for consistency — it is the same three lines in all three writes now.
  - `[low]` `[patch]` `[verification-gap]` The three contract limits are re-declared as literals with nothing tying them to the contract — Pre-verified: the dialog tests build their fixtures from `MeetingsService.*MaxLength` itself, so they move with any drift instead of catching it, and `MeetingsServiceTests` never mentions the constants. Patched: one test reading the three `maxLength` values out of the committed `openapi.json` as data. Mutation-checked by nudging `TitleMaxLength` to 201.
  - `[low]` `[defer]` `[verification-gap]` `MeetingsService`'s DI registration is never executed by any test — carried: logged and deferred on the previous pass, and `Program.cs:25` still reads exactly as that row describes. The layer filed it `patch` this time, but the route does not change on a re-report and the entry is not deferred a second time.
  - `[low]` `[reject]` `[blind-hunter]` `CreatedAt` and `Sha256` are mapped and pinned by tests but never rendered — Real but rejected as low: the I/O matrix enumerates what Meeting Detail shows and neither appears in it, so surfacing them would add UI the intent does not ask for, and deleting the fields would undo the assertions the previous pass deliberately added. No named harm beyond mapped data sitting unused.
  - `[low]` `[reject]` `[blind-hunter]` The attendee error goes stale while the user corrects the field — Real but rejected as low: the message clears on the next Enter or on Create, so it is wrong only mid-keystroke, and a 101-character attendee name is not an everyday entry. The fix replaces the two-way binding with an explicit `ValueChanged` handler, which is more than a direct correction.
  - `[low]` `[reject]` `[blind-hunter]` `Title` and the attendee field get no `MaxLength` and no counter — Rejected: the intent settles this. Its matrix asks for "a validation message under that field" for a 201-character title and a 101-character attendee, which is exactly what `Validate` and `AddAttendee` do; capping at the input would be a different interaction from the one specified.
  - `[low]` `[reject]` `[blind-hunter]` Root-absolute URLs ignore a non-root `<base href>` — Real in shape but rejected as low: `wwwroot/index.html:8` is `<base href="/" />`, so no sub-path deployment was shown to exist, and the pattern is pre-existing across `LoginPage` and `Toolbar`. The fix would route every URL in the app through a base-aware helper.
  - `[low]` `[reject]` `[blind-hunter]` `Voice.NoValue` is a bare dash, announced as "hyphen" or skipped — Real but rejected as low: it is pinned UX copy used as the empty-attendees placeholder, and changing what it says is a copy decision rather than a review fix. The `aria-labelledby` patch above at least makes it announce as "Attendees, -" rather than as a stray line.
  - `[low]` `[reject]` `[edge-case]` `MeetingDetailPage` does not reload when `Id` changes while it stays mounted — carried: rejected on the previous pass as low on the same grounds, and nothing in the app links one meeting detail to another today.
  - `[low]` `[reject]` `[edge-case]` `items` or `attendees` arriving as JSON null throws inside the seam — carried: rejected on the previous pass; both are `required` in the committed contract and no path to a violation was shown.
  - `[low]` `[reject]` `[edge-case]` A 409 from Create raises the global "Already changed. Reloading." beside the dialog's inline failure, promising a reload that never happens — Real incoherence, but the intent settles it: "the global handler's own 409 snackbar is left alone", and the proposed fix scopes `SessionMessageHandler`'s Conflict arm per call, which is precisely the per-page re-implementation the intent forbids.
  - `[low]` `[reject]` `[edge-case]` The empty attendee draft is cleared silently rather than refused inline — Verified here rather than as filed: the layer's report was truncated in delivery mid-finding, so this row is triaged from its location, trigger and the code itself. `AddAttendee` does clear an empty draft silently, and the intent's matrix covers only the over-long entry, not the empty one. Rejected as low: a message under an empty field would fire on every stray Enter, which is what the existing comment says.
  - `[low]` `[reject]` `[blind-hunter]` A paste over 50,000 characters is silently truncated into an immutable record — carried: rejected on the previous pass. The separate verification gap it sits beside — that nothing asserted the cap at all — was patched this pass.
  - `[false]` `[reject]` `[blind-hunter]` There is no in-app route back to the Meeting List from Meeting Detail — Refuted: `Shared/Toolbar.razor:8` links the product name to `/meetings` and `:15` is a `MudNavLink Href="/meetings" Match="NavLinkMatch.Prefix"` that even marks itself active on the detail route. `MainLayout` renders the toolbar for every signed-in session. The layer searched `Layout/` and the component lives in `Shared/`.
  - `[false]` `[reject]` `[edge-case]` `NoRecordsContent` renders before the first `ServerData` call returns, so the empty state claims the list is empty while page one loads — Refuted by probe. With the proposed `loaded` guard removed and a gate holding `ListMeetingsAsync` open, the rendered markup contains neither `Voice.NoMeetings` nor the empty-state button while the request is outstanding (`ListMeetingsCalls == 1`): `MudTable` suppresses its own `NoRecordsContent` for the duration. The guard was written, found to be unobservable in either direction, and reverted.
  - `[false]` `[reject]` `[edge-case]` Retry clicked after navigating away throws `ObjectDisposedException` — Refuted on the stated consequence: `ComponentBase.InvokeAsync` dispatches on the renderer's dispatcher, which outlives the component, and `StateHasChanged` on a disposed component is dropped by the renderer rather than thrown. The adjacent real behaviour — a confirm dialog opening over an unrelated page — needs `IDisposable` plus disposal wiring on the page, which the previous pass already rejected as low for an undemonstrated harm.
  - `[false]` `[reject]` `[blind-hunter]` Duplicate attendee names remove the wrong chip — carried: refuted on the previous pass; the list holds strings compared by value, so removing either equal element leaves an identical list.

Intent-alignment auditor: descriptive only, no actionable finding, and no loopback. It reports the diff resolving all seven of its ambiguities consistently toward the generated-client surface (R1b, R2a, R3a, R4a, R5b, R6b, R7a) and documenting each choice in-source, with every `Never` constraint honoured. Its central observation is that the I/O matrix is written in transport vocabulary while every test lives at `IActionLedgerApiClient` and the DOM, so `SessionMessageHandler` — a `DelegatingHandler` below the stubbed interface — is bypassed, leaving the handler's half of three composed matrix rows unexercised. It also verified that `WebStructureTests` runs `Types.InAssembly(WebAssembly)` and therefore cannot see the test assembly, which makes the literal reading of AD-14's "and tests" clause unenforceable by the test the intent cites. Recorded rather than actioned: an auditor that is descriptive by mandate does not file defects, and the surfaces it names are ones the intent chose.

## Design Notes

**Why the list uses `ServerData` rather than a client-side page.** The API is paged and returns a
`total` counted across all pages. Loading page 1 into `MudTable.Items` would give MudBlazor a
paginator over 50 rows it already has, so "next page" would show nothing while the server holds
more. `ServerData` is the one shape where the paginator and the API agree. The 0-based
`TableState.Page` → 1-based API conversion is the single place that can be off by one, so it gets
its own test.

**Why the title cell is an anchor.** EXPERIENCE.md asks for "row click opens detail" *and* "rows
are also focusable and open on Enter". `MudTable` renders its own `<tr>` and exposes no attribute
splat to put `tabindex` and `@onkeydown` on it. A real `<a href>` inside the first cell is
focusable, opens on Enter, and announces itself as a link, while `OnRowClick` keeps the whole row
clickable for the mouse. Faking the same thing with a `tabindex` on a `<tr>` would be more code and
less accessible.

The anchor carries its own click handler with both `@onclick:preventDefault` and
`@onclick:stopPropagation`, and all three parts are load-bearing. Left plain, a click on the link
is handled twice — once by the row and once by Blazor's document-level anchor listener — which
leaves two history entries for one URL, so Back returns the user to the page they are already on.
`stopPropagation` silences the row, but it also keeps the event from reaching that listener, and
the browser would then follow the `href` as a full page load: in a WebAssembly host that reloads
the app and `SessionState` holds the token in memory only, so it would sign the user out.
`preventDefault` stops that, leaving the handler as the single navigation. The cost is that ctrl
or cmd click no longer opens a row in a new tab, because `@onclick:preventDefault` is a
render-time flag rather than a per-event decision; middle click is unaffected, since it raises
`auxclick`. Neither the epic nor EXPERIENCE.md asks for modifier-click, and both surfaces they do
ask for — mouse click and Enter — work.

**Why the confirm dialog is in `Shared/` and takes its sentence as a parameter.** The notes confirm
repeats "Notes cannot be changed after saving" verbatim, which reads like a notes-specific dialog.
It is not: Reject, Complete, and Cancelled confirm the same way in Epics 3 and 4. Parameterising
the message now means those stories add a call, not a second dialog — and keeps the sentence itself
a single `Voice` constant used in both the caption and the dialog.

**Why the failure treatments differ between the dialog and the notes save.** EXPERIENCE.md's "Save
failure or network error" row asks for a snackbar with the problem title and a Retry action, with
form values retained. On the detail page there is nothing else on screen to carry the failure, so
that is exactly what happens. Inside the dialog the retry affordance already exists — the Create
button, over the values the user still sees — so the failure renders inline and the dialog stays
open. Both satisfy "values are retained"; putting a snackbar behind an open modal would put the
message where the focus trap cannot reach it.

**The read-only branch is structural.** The detail page branches on `detail.Notes is null` around
the *whole* notes region rather than toggling a `ReadOnly` flag on one `MudTextField`. A flag is a
condition a later story can invert by accident; a branch means the edit markup does not exist in
the rendered tree at all, which is what
`MeetingDetailPageTests` asserts by searching the markup for `textarea`.

## Verification

**Commands** (prefix each with `export PATH="$HOME/.dotnet:$PATH"`):

- `dotnet build ActionLedger.sln` -- expected: 0 errors, 0 warnings (`TreatWarningsAsErrors` is on).
- `dotnet build ActionLedger.sln -c Release` -- expected: the same, since this is `ci.yml`'s shape.
- `dotnet test ActionLedger.sln` -- expected: every assembly green, 0 skipped, with a higher total
  than the 479 the Story 2.1 review pass recorded.
- `dotnet test tests/Architecture.Tests` -- expected: `WebStructureTests` green without being
  edited, which is what says the seam and the page-naming rule survived.
- `git diff --stat` -- expected: nothing under `src/ActionLedger.Api`, `src/ActionLedger.Application`,
  `src/ActionLedger.Domain`, `src/ActionLedger.Infrastructure`, `src/ActionLedger.Web/openapi.json`,
  or `_bmad-output/implementation-artifacts/sprint-status.yaml`.

**Mutation checks — introduce each, confirm the named test goes red, then revert:**

- Delete the `notes is null` branch guard so the textarea always renders → the "no textarea when
  notes exist" test in `MeetingDetailPageTests`.
- Send `state.Page` straight through to the API → the "page 2 requests page 2" test in
  `MeetingListPageTests`.
- Remove the confirm dialog and save directly → the "cancelling the confirm sends nothing" test.
- Remove the route-level auth check from either page → that page's unauthenticated-redirect test.
- Trim the notes text at the seam → the byte-for-byte save test in `MeetingDetailPageTests`.

**Manual checks:**

- Read the rendered `MeetingDetailPage` markup for a meeting with notes and confirm no `textarea`,
  no `Save notes`, and no other edit affordance appears — the assertion the test automates, checked
  once by eye because it is the story's one irreversible rule.


## Auto Run Result

Status: done

### Implemented change

A follow-up review pass over the Story 2.2 Meetings feature — the pass the previous run asked
for by setting `followup_review_recommended: true`. No new feature work: the code under review is
the one already committed at `02daaa2`, and this pass hardened it. Eleven entries were patched,
three deferred, and the rest rejected on their refutations. No `intent_gap` and no `bad_spec`, so
there was no loopback and `review_loop_iteration` stayed at 0.

### Files changed

- `src/ActionLedger.Web/Features/Meetings/MeetingDetailPage.razor` — the `h1` is hoisted out of
  the load branches so the route has a heading while the GET is in flight and when it fails; the
  two field captions carry ids and their values `aria-labelledby`; the Save guard tests
  `draft.Trim().Length` and also refuses once notes are attached; a success snackbar announces the
  write whose own button has just left the tree.
- `src/ActionLedger.Web/Features/Meetings/NewMeetingDialog.razor` — `inFlight` is claimed with an
  explicit render, matching both pages; the `OnAttendeeKeyDown` comment now describes what the
  code actually does.
- `src/ActionLedger.Web/Core/Voice/Voice.cs` — `Meeting` (the loading heading) and `NotesSaved`
  (the success announcement), both pinned.
- `tests/Web.Tests/MeetingDetailPageTests.cs` — six new cases: heading while loading, heading on
  failure, caption association, the `maxlength` cap, the whitespace-only draft, the success
  announcement, and the stale-retry guard.
- `tests/Web.Tests/MeetingsServiceTests.cs` — a `+09:00` date case, and a case reading the three
  contract limits out of the committed `openapi.json`.
- `tests/Web.Tests/ShellTests.cs` — `.al-notes {` pinned in `app.css` beside `.al-progress`.
- `tests/Web.Tests/StubApiClient.cs` — a `GetMeetingGate`, so the detail page's loading state is
  observable at all.
- `tests/Web.Tests/VoiceAndFormatsTests.cs` — the two new pinned rows.

`MeetingListPage.razor` and `MeetingListPageTests.cs` are deliberately unchanged: a guard was
written for the empty-state finding, found to be unobservable in either direction, and reverted.

### Review findings

29 rows from 28 findings across four layers — high 1, medium 7, low 17, false 4, maybe-false 0.

**Patched (11 entries — 5 medium, 6 low).** The heading hole was the broadest: Meeting Detail had
no `h1` at all while loading *or* after a failed load, so `FocusOnNavigate` dropped focus on every
navigation into a meeting and a failed load left the route permanently headless. The date-offset
gap was the sharpest: `DateFormatConverter` hands the seam a browser-local offset, so `ToDate`'s
`value.Date` is load-bearing and a change to `UtcDateTime.Date` would have shifted the date a day
earlier for every user east of UTC, with the whole suite green. A whitespace-only draft could be
written into the one irreversible endpoint in the app. The 50,000-character cap and the
`.al-notes` whitespace rule could each be deleted with everything still passing. The rest were
smaller: the stale snackbar Retry firing after notes were attached, the silent success, the
unassociated captions, the inconsistent `inFlight` render, a comment asserting a guard that was
not there, and the three contract limits now read from `openapi.json` rather than retyped.

**Deferred (3).** One carried from the previous pass unchanged (the `Program.cs` DI registration
no test executes). Two new: `@onclick:preventDefault` cannot be observed without a browser and
belongs with the Playwright suite AD-18 reserves; and the generated date converter's
culture-sensitivity, which is the one high-severity finding of the pass — see the residual risk
below.

**Rejected (14, with reasons in the triage log).** Four on refutation: the missing route back to
the list (the toolbar has one), the empty state during loading (MudTable suppresses it itself,
verified by probe), the disposed-component exception (Blazor drops the render rather than
throwing), and the duplicate-chip claim carried from last pass. The rest were lows whose fix
exceeded a direct correction, or claims the intent itself settles — input capping, the global 409
snackbar, and the unrendered `CreatedAt`/`Sha256`.

**Follow-up review recommendation: false.** This was a follow-up pass, where the bar is a patched
`high`. None was patched: the single `high` is deferred, not fixed, and patch volume is explicitly
not grounds. The work has converged.

Patched counts by verdict: high 0, medium 5, low 6.

### Verification performed

All commands from `## Verification`, on the patched tree:

- `dotnet build ActionLedger.sln` — 0 errors, 0 warnings.
- `dotnet build ActionLedger.sln -c Release` — 0 errors, 0 warnings.
- `dotnet test ActionLedger.sln` — 600 passed, 0 failed, 0 skipped (587 before this pass, 479 at
  Story 2.1).
- `dotnet test tests/Architecture.Tests` — 95 passed; `WebStructureTests` green without being
  edited.
- `git diff --stat` against `96c7149` — nothing under `src/ActionLedger.Api`,
  `src/ActionLedger.Application`, `src/ActionLedger.Domain`, `src/ActionLedger.Infrastructure`, or
  `src/ActionLedger.Web/openapi.json`.

Every patch was mutation-checked individually — the fix was reverted, the new test confirmed red,
and the fix restored: the heading (both states), the caption association, the `maxlength` cap, the
whitespace gate, the success announcement, the stale-retry guard, `ToDate` under
`value.UtcDateTime.Date`, `TitleMaxLength` nudged to 201, and the `.al-notes` rule renamed. The
spec's own structural mutation was re-run after the detail page's branches were restructured:
forcing the notes branch open still turns
`A_meeting_that_arrives_with_notes_renders_no_edit_affordance_at_all` red.

The culture finding was established by probe rather than by argument: a temporary test set
`CurrentCulture` and exercised the converter's own two lines, then was removed.

### Residual risks

- **The deferred culture defect is live.** Under a non-Gregorian browser calendar the Meetings
  feature is broken today, not theoretically: `th-TH` reads the server's `2026-09-21` as
  `1483-09-21` and sends `2569-10-03` for an October 2026 date, and `ar-SA` throws a
  `FormatException` that escapes `MeetingsService.CallAsync` and takes both screens to the Blazor
  error UI. Nothing in this story's blast radius can fix it, and no test in the suite would catch
  a regression in it. If the demo is driven from a machine in a non-Gregorian locale, Meetings
  will not work.
- **`@onclick:preventDefault` is unguarded until the Playwright suite exists.** Removing it is
  invisible to the entire suite and turns every title click into a full page reload, which signs
  the user out.
- **The `SessionMessageHandler` half of three matrix rows remains unexercised**, as the
  intent-alignment auditor describes: stubbing `IActionLedgerApiClient` sits above the
  `DelegatingHandler`, so the 401 redirect, the 403/409 snackbars and the progress bar are never
  driven by these tests. That is a consequence of the seam the intent chose, not a defect in it.
- **The composition root is still unverified end to end** (the carried deferral): deleting
  `Program.cs:25` leaves the suite green and the app broken at `/meetings`.
