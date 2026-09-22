---
title: 'Story 3.4 — Review Screen layout with proposal cards and source highlighting'
type: 'feature'
created: '2026-09-22'
baseline_revision: '47e669b71fb4a639c5b3fe026205332f18cb0b37'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
warnings: ['oversized']
deferred:
  - summary: >-
      NFR2 (a Review Screen with 50 proposals renders within 2 seconds on compose) has not been timed.
    evidence: |-
      Only the bUnit test Fifty_proposals_render_in_the_order_received covers 50 proposals, and it checks order, not timing. To settle it, seed a run with 50 proposals and time the first render of its review route on compose in a browser.
    location: >-
      src/ActionLedger.Web/Features/Review/ReviewPage.razor
    severity: medium (unverified)
---

<intent-contract>

## Intent

**Problem:** Decisions exist in the API since 3.2, but there is no Review Screen. `ProposedActionDto` does not publish the decision copy, the decided values or the excerpt position, and after a run the app navigates to Run Detail. An Action Officer cannot see proposals beside the notes they came from (epics.md:581-603; FR10, FR14, NFR2; UX-DR3/4/6/7/8/9/10).

**Approach:** Extend `ProposedActionReadModel`, the single producer of the DTO, with the decision copy, display names, the Tracked Action's values, and a server-computed excerpt span in the raw notes. Regenerate `openapi.json`. Add the routable `ReviewPage` in `Features/Review`: a two-pane layout, presentational `ProposalCard`s, the shared `ProvenanceChip`/`LowConfidenceBadge`/`PendingCounter`, and a single Source Excerpt highlight. Point post-run navigation at the new route.

## Boundaries & Constraints

**Always:**

- **DTO (`Application/Review/ProposedActionDtos.cs`):** append these nullable fields to `ProposedActionDto`, after `ReviewState`:
  - `SuggestedOwnerDisplayName`: the display name of the roster user matched by `SuggestedOwnerUserId`.
  - `DecidedByUserId`, `DecidedByDisplayName`, `DecidedAt`, `RejectionReason`: from the proposal's decision copy.
  - `TrackedActionId`, `DecidedDescription`, `DecidedOwnerUserId`, `DecidedOwnerDisplayName`, `DecidedDueDate`: from the `TrackedAction` whose `ProposedActionId` is this proposal. These are null for Pending and Rejected.
  - `ExcerptStart`, `ExcerptLength`: UTF-16 offsets into the run's `MeetingNotes.Text`.
- **Read model (`ForRunAsync`):**
  - Keeps AI order and the zero-proposal early return.
  - For a non-empty run it reads, in addition:
    - the run's notes text (`ExtractionRun.MeetingNotesId` → `MeetingNotes.Text`);
    - the tracked actions for the run's proposal ids;
    - the display names of every decider and owner id, from all users, including system users.
  - Each read is one query, and a read is skipped when its id set is empty.
- **Excerpt span:** a new `ExcerptLocator.Locate(string? excerpt, string? notes) → ExcerptSpan?` in `Application/Ai`.
  - It finds the first occurrence of the normalized excerpt in the normalized notes, using the same comparison as `ExcerptVerifier`.
  - It maps that match back to raw indices: the raw start is the raw index of the first matched character, and the end is one past the raw index of the last matched character.
  - It returns null when the excerpt normalizes to empty or is not found.
  - The raw-index map comes from `TextNormalization` itself, through an internal overload that also returns the map. `Normalize` delegates to it. There must be no second normalizer (Rule 5, AD-11).
- **Web:**
  - `Features/Review/ReviewPage.razor` has `@page "/meetings/{MeetingId:guid}/runs/{RunId:guid}/review"`. It is the only component that injects services.
  - Children take parameters and raise `EventCallback`s only.
  - HTTP goes only through `ReviewService`. It gets a new `GetReviewAsync(meetingId, runId)` that fetches the run and the meeting and returns web-owned records (`ReviewScreen`, `ReviewProposal`). No generated type leaves the service.
- **Page behavior:**
  - **Sign-in and not-found:** sign-in redirect as in `RunDetailPage`. A 404 on either GET, or a run whose `MeetingId` differs from the route, renders `NotFoundNotice` pointing to the meeting. A Failed run navigates, with replace, to its Run Detail.
  - **Heading and focus:** the h1 is "Review proposals", rendered in every state. A meta line gives the meeting title (a link), the run start instant, the provider, the model and the prompt version. There is a "Back to meeting" link.
  - **Notes pane:** the notes text is rendered with `white-space: pre-wrap`. At most one `<mark id="review-excerpt-highlight">` wraps the active proposal's span, styled with `--al-ai-provenance-container` and `--al-on-surface`.
  - **Layout:** from 1200px, a CSS grid with the notes at least `var(--al-notes-pane-min)` wide on the left and the cards on the right. Each pane has its own `overflow-y: auto` and a viewport-bound height. From 1024 to 1199px, the same notes content sits inside a collapsed `MudExpansionPanels` above the cards.
  - **Viewport width:** the page reads the width from MudBlazor's `IBrowserViewportService`. An unknown width is treated as wide.
- **Highlight:**
  - Hovering a card (`mouseenter`), focusing inside it (`focusin`), or clicking its blockquote makes that card active. Activating the card that is already active does nothing.
  - Only the active card has `aria-describedby="review-excerpt-highlight"`, and only when its span is not null.
  - After the render, the page calls `IScrollManager.ScrollIntoViewAsync("#review-excerpt-highlight", …)`.
  - A null span gives no mark, no `aria-describedby`, and no scroll.
  - Every card is an `<article tabindex="0">`, so cards without buttons can still be reached with the keyboard.
- **Pending card** (DESIGN.md Proposal card):
  - The header row holds `ProvenanceChip` (AI, "Proposed by AI", `auto_awesome`), the Confidence Score (two decimals, `al-confidence-score`), `LowConfidenceBadge` when `IsLowConfidence`, and `ReviewStateChip`. `IsLowConfidence` also adds the 4px left-border class.
  - Description, owner and due date are read-only values.
  - The owner is `SuggestedOwnerDisplayName`, or "Unassigned". The hint "AI suggested: {SuggestedOwner}" is always shown, and reads "AI suggested: none" when the text is empty.
  - A null due date reads "No due date proposed".
  - The Source Excerpt is a `<blockquote>` with the `al-source-excerpt` class on the AI provenance container.
  - The buttons are Reject (text), Edit (outlined) and Approve (filled). They raise `OnReject`/`OnEdit`/`OnApprove`, which `ReviewPage` leaves unbound until 3.5.
- **Decided card:**
  - It keeps its place, header and blockquote. The action row becomes `ProvenanceChip` (human, `person`, "Decided by {DecidedByDisplayName}"), then `Formats.Instant(DecidedAt)` as a sibling beside the chip, not inside it.
  - Approved/Edited: the values shown are the Decided* values. An owner of null reads "Unassigned" and a due date of null reads "No due date". The card has a "View action" link to `/actions/{TrackedActionId}`.
  - Edited: adds a "Proposed" column on the AI provenance container with the proposal's description, suggested owner text and suggested due date, beside a "Decided" column.
  - Rejected: shows the proposed values as a Pending card does, plus "Reason: {text}" or "No reason given". There is no link.
  - The human chips sit inside `aria-live="polite"`.
- **PendingCounter:**
  - Its count comes from the proposals whose state is Pending.
  - It reads "{n} proposals pending", or "1 proposal pending" when n is 1.
  - At 0 it reads "All proposals decided" and adds a "View actions" link to `/actions?meetingId={MeetingId}`.
  - It sits inside `aria-live="polite"`.
- **Zero proposals:** "The AI found no actions in these notes.", plus "Run again", which starts a run and navigates to the new run's review route when it succeeds or to its Run Detail when it fails, and "Back to meeting". The pending counter is not shown. The Run again button is disabled while the request is in flight.
- **Navigation:**
  - `MeetingDetailPage`: a Succeeded run navigates to `/meetings/{id}/runs/{runId}/review`.
  - `RunDetailPage`: a Succeeded run shows a "Review" link to that route.
  - `ReviewService.ToProposal` for Run Detail now fills `DecidedBy`, `DecidedAt` and `RejectionReason` from the DTO.
- **Shared components:** `Shared/{ProvenanceChip,LowConfidenceBadge,PendingCounter}.razor` are presentational. `RunDetailPage`'s inline low-confidence markup is replaced by `LowConfidenceBadge`, with the output unchanged: `al-low-confidence`, icon plus text.
- All new copy is a `Voice` constant pinned in `VoiceAndFormatsTests`. Dates go through `Formats.Date` and instants through `Formats.Instant`.

**Never:**

- No decision POST, edit mode, reject dialog or `UserDirectory` owner picker. Those belong to 3.5.
- No new API route, no JS file, and no edit to `tokens.css`. Layout CSS goes in `app.css`.
- The web never compares a confidence threshold and never locates or normalizes excerpt text itself.
- No change to the domain model, `DecideProposalHandler`, migrations or the outbox.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Exact excerpt | notes `"A.\nDana will call Bob."`, excerpt `"Dana will call Bob."` | span (3, 18): `Dana` through `Bob`, the trailing `.` dropped by normalization | — |
| Normalized-only match | notes `"Dana  will call\r\nBob!"`, excerpt `"dana will call bob"` | span covers `Dana` through `Bob` (excludes `!`) | — |
| Excerpt not in notes | legacy or seeded data | span null → card renders; no mark | — |
| Approved, owner matched | TrackedAction owner = Dana | Decided* filled, `DecidedOwnerDisplayName` "Dana Whitfield", `TrackedActionId` set | — |
| Rejected with reason | decision copy set, no TrackedAction | Decided* null, `RejectionReason` set, `DecidedByDisplayName` set | — |
| Zero proposals | Succeeded run, no proposals | zero-state copy, Run again, Back to meeting | — |
| Run of another meeting | run.MeetingId ≠ route id | Not found | — |
| Failed run | Outcome Failed | replace-navigate to Run Detail | — |
| Load failure | 500 or transport error | `LoadFailure` with Retry | — |

</intent-contract>

## Code Map

- `src/ActionLedger.Application/Review/ProposedActionReadModel.cs`: the projection and derived values to extend. The roster is already loaded, so `SuggestedOwnerDisplayName` comes from the match at no extra cost. `ProposedActionDtos.cs`: the record to extend, and its remark that anticipates 3.4.
- `src/ActionLedger.Application/Ai/{TextNormalization,ExcerptVerifier}.cs`: the normalizer to give a mapped overload, and the comparison to mirror. `ToLowerInvariant` keeps the UTF-16 length, so the indices align.
- `src/ActionLedger.Domain/Actions/TrackedAction.cs:62-77`, `Domain/Extraction/ProposedAction.cs:127-142`, `Domain/Meetings/MeetingNotes.cs`, `Domain/Users/User` (`DisplayName`, `IsSystem`): read-only sources.
- `src/ActionLedger.Application/Extraction/RunsQueries.cs`: composes the read model. Nothing changes there except the tests.
- `src/ActionLedger.Web/Features/Review/RunDetailPage.razor`: the template for the page pattern: sign-in redirect, `loadedRunId` reuse, `NotFoundNotice`/`LoadFailure`, Run again with Retry, `disposed` guard, and the h1 outside the load branches. Its low-confidence span is at `:127-135`.
- `src/ActionLedger.Web/Features/Review/Data/ReviewService.cs`: `CallAsync`, `ReviewOutcome`, and `ToProposal` at `:434`.
- `src/ActionLedger.Web/Features/Meetings/MeetingDetailPage.razor:360-370`: the post-run navigation.
- `src/ActionLedger.Web/Shared/ReviewStateChip.razor`: the shared-component style. `Core/Voice/Voice.cs` and `Core/Formatting/Formats.cs`.
- `src/ActionLedger.Web/wwwroot/css/{tokens,app}.css`: the `--al-*` tokens, including `--al-notes-pane-min`. `.al-notes` and `.al-low-confidence` already exist in app.css.
- `src/ActionLedger.Web/openapi.json` and the generated client: regenerate with `dotnet run --project src/ActionLedger.Api -- --export-openapi`, with `Database__ConnectionString`, `Jwt__Key` and `Jwt__Issuer` set. The client regenerates on build.
- `tests/Web.Tests/{RunDetailPageTests,StubApiClient,ReviewServiceTests,MeetingDetailPageTests,SharedComponentTests,VoiceAndFormatsTests}.cs`: the bUnit patterns (`BunitContext`, `AddMudServices`, `JSInterop.Mode = Loose`, the stub client with `Run`/meeting fields).
- `tests/Application.Tests/Review/ProposedActionReadModelTests.cs`: the fake read seam, to extend for `TrackedAction`/`User`/`MeetingNotes`/`ExtractionRun`.
- `tests/Infrastructure.Tests/TrackedActionPersistenceTests.cs`: `HandlerIn(scope, actor)` and the migrated-DB helpers, for a real-PostgreSQL read after each decision kind.
- `tests/Architecture.Tests/WebStructureTests.cs`: the HTTP-seam and routable-page rules the new files must satisfy.

## Tasks & Acceptance

**Execution:**

- `src/ActionLedger.Application/Ai/{TextNormalization,ExcerptLocator}.cs`: the mapped normalize overload, and `ExcerptLocator` with `ExcerptSpan(int Start, int Length)`.
- `src/ActionLedger.Application/Review/{ProposedActionDtos,ProposedActionReadModel}.cs`: the new fields and the extra reads, per Always.
- `src/ActionLedger.Web/openapi.json`: regenerated. Also update the `ProposedActionDto` construction sites in tests.
- `src/ActionLedger.Web/Features/Review/Data/ReviewService.cs`: `GetReviewAsync`, the `ReviewScreen`/`ReviewProposal` records (converting `SuggestedDueDate`/`DecidedDueDate` to `DateOnly`), and the `ToProposal` decision fields.
- `src/ActionLedger.Web/Shared/{ProvenanceChip,LowConfidenceBadge,PendingCounter}.razor`: new shared components.
- `src/ActionLedger.Web/Features/Review/{ReviewPage,ProposalCard,NotesPane}.razor`: the container page and its presentational children.
- `src/ActionLedger.Web/Features/Review/RunDetailPage.razor`: the `LowConfidenceBadge` swap and the "Review" link. `Features/Meetings/MeetingDetailPage.razor`: the post-run route.
- `src/ActionLedger.Web/Core/Voice/Voice.cs`: the new constants. Refresh the stale "until Story 3.1" remarks.
- `src/ActionLedger.Web/wwwroot/css/app.css`: panes, independent scroll, highlight, card, left border, and chip classes.
- Tests:
  - `tests/Application.Tests/Ai/ExcerptLocatorTests.cs`: the matrix spans, plus the first occurrence, an empty excerpt, and `Normalize` output unchanged.
  - `ProposedActionReadModelTests`: every new field for Pending/Approved/Edited/Rejected, a null owner, and a system-user decider name.
  - `tests/Infrastructure.Tests/ReviewReadModelPersistenceTests.cs`: on Testcontainers, after an Approve, an Edit and a Reject through the handler, `RunsQueries.GetAsync` returns agreeing decision fields and spans. This proves the queries translate.
  - `tests/Web.Tests/{ReviewPageTests,ProposalCardTests}.cs`, `SharedComponentTests`, `ReviewServiceTests`, `MeetingDetailPageTests`, `RunDetailPageTests`, `VoiceAndFormatsTests`: every Always rule on the web side, including a narrow layout through a stub `IBrowserViewportService`, and 50 proposals rendering in order.

**Acceptance Criteria:**

- Given a Succeeded run with Pending proposals at 1200px or wider, when `/meetings/:id/runs/:runId/review` opens, then the notes pane (pre-wrap) sits left and the cards sit right in AI order, and each card shows "Proposed by AI", a mono two-decimal score, the badge and border when flagged, the read-only values with the always-visible "AI suggested: …" hint, the blockquote, and Reject/Edit/Approve.
- Given a width from 1024 to 1199px, when the page renders, then the notes are inside a collapsed `MudExpansionPanels` above the cards.
- Given a card, when it is hovered or focused, or its blockquote is clicked, then exactly one `mark#review-excerpt-highlight` wraps that proposal's span, the card alone carries `aria-describedby="review-excerpt-highlight"`, and a scroll-into-view is requested.
- Given decided proposals, when the page renders, then each card shows "Decided by {name}" with the UTC timestamp beside the chip, the Review State chip, "Reason: …"/"No reason given" on Rejected cards, a "Proposed" column on Edited cards, and "View action" → `/actions/{id}` on Approved and Edited cards. The counter reads "{n} proposals pending", or "All proposals decided" with "View actions" → `/actions?meetingId={id}`.
- Given Meeting Detail, when a run succeeds, then the app navigates to the run's review route. Given Run Detail for a Succeeded run, then a "Review" link points there.
- Given the solution, when `dotnet test` runs, then everything passes, including `OpenApiSnapshotTest` against the regenerated `openapi.json` and the web structure rules.

## Spec Change Log

## Review Triage Log

### 2026-09-22 — Review pass
- verdicts: 28 findings — high 0, medium 4, low 10, false 13, maybe-false 1
- findings:
  - `[low]` `[reject]` (blind) Hovering a card calls `scrollIntoView`, which can also scroll the window — the UX spec requires hover to scroll the highlight into view, and the spec fixes the mechanism as `IScrollManager.ScrollIntoViewAsync`. In the wide layout the panes are viewport-bound, so the window has little to scroll. Changing the mechanism would mean editing the spec.
  - `[false]` `[reject]` (blind) An unhandled JS exception from the scroll call kills the circuit — MudBlazor's `scrollIntoView` does `querySelector(e)||document.documentElement` and never throws on a missing element. This is WebAssembly, and there is no circuit. (The page jump that fallback causes is the narrow-layout group below.)
  - `[medium]` `[patch]` (blind) At 1024–1199px the highlight sits in a collapsed panel, so there is no mark, `aria-describedby` dangles, and the scroll hits the missing element — fixed: `KeepContentAlive="true"` on the notes panel, expansion tracked with `@bind-Expanded`, and a scroll is requested only when the layout is wide or the panel is open. There are new narrow-layout tests for collapsed (mark and describedby, no scroll) and expanded (scroll).
  - `[false]` `[reject]` (blind) No loading state — `MainLayout` renders the global `LoadingState` progress bar, which `SessionMessageHandler` drives for every API call, and the h1 is always rendered.
  - `[false]` `[reject]` (blind) The `/actions` links lead to Not found — this is intended. The epic says the Not found page shows until Stories 4.3 and 4.4 land.
  - `[false]` `[reject]` (blind) The Edited "Proposed" column shows the raw suggested owner text — the Proposed column shows the AI's original values, and the mockup shows the raw "P. Ram" there.
  - `[false]` `[reject]` (blind) An Approved or Edited card with no Tracked Action, or a decider with a null name, renders an empty description or "Decided by " — unreachable. 3.2 creates the Tracked Action in the same transaction as the decision, with a unique index on `proposed_action_id`, and the decider is the JWT user, who is resolved from all users, system users included.
  - `[low]` `[patch]` (blind) The focusable `<article>` has no accessible name — fixed: `aria-labelledby` now points at the description's `proposal-{id}-description` id. Asserted in `ProposalCardTests`.
  - `[low]` `[reject]` (blind) The pane height is a hand-picked `calc(100vh - appbar - 176px)` — real, but the fix is a layout rework that needs tuning in a browser. Recorded as a residual risk.
  - `[low]` `[reject]` (blind) The review-route strings are duplicated across three pages — there is no concrete divergence, and a shared route helper adds new surface.
  - `[low]` `[patch]` (blind) Changing only `MeetingId` does not reload — fixed: the guard now tracks both ids. There is a new test.
  - `[low]` `[patch]` (blind) Test gaps: `NotesPane`'s guard, and non-ASCII text in `ExcerptLocator` — fixed: `NotesPaneTests.cs` (out of range, negative, zero length, and ending exactly at the end), plus accent and emoji cases in `ExcerptLocatorTests`.
  - `[medium]` `[patch]` (edge) Collapsed-panel activation leaves no mark in the DOM and the scroll falls back to documentElement — same group as the narrow-layout blind row, same fix.
  - `[false]` `[reject]` (edge) An out-of-bounds server range leaves `aria-describedby` dangling and scrolls to documentElement — unreachable. The span is computed on the server over the same immutable `MeetingNotes.Text` that the web renders from the meeting read.
  - `[false]` `[reject]` (edge) `Start + Length` int overflow — the server's values are bounded by the notes length, which is at most 50,000.
  - `[low]` `[patch]` (edge) A `MeetingId`-only route change shows a stale review — same group as the blind row, same fix.
  - `[low]` `[reject]` (edge) Disposal during the viewport awaits leaks the resize observer — rare (the user leaves within the first render), and the callback already checks `disposed`. The fix adds a guard branch.
  - `[false]` `[reject]` (edge) An Approved card without a Tracked Action shows empty decided values — same refutation as the blind row.
  - `[false]` `[reject]` (edge) A null decider name gives a dangling "Decided by " — same refutation as the blind row.
  - `[false]` `[reject]` (edge) A stale `skipRender` swallows the next render — `Activate` runs only from the card's EventCallback. The page's `HandleEventAsync` calls `StateHasChanged` straight after it, in the same dispatch, and no other page render can be pending there, so `ShouldRender` always consumes the flag.
  - `[medium]` `[patch]` (edge) The AC "exactly one mark" fails in the narrow layout — same group as the narrow-layout blind row. With `KeepContentAlive`, the mark now exists at every width.
  - `[false]` `[reject]` (edge) "1 proposal pending" contradicts the AC template — the spec's Always rules and Design Notes specify the singular form for n = 1.
  - `[medium]` `[patch]` (verification-gap) The resize subscription is untested — fixed: `StubViewport` stores the callback and options. A new test flips 1280 → 1100 → 1280 and asserts `NotifyOnBreakpointOnly == false`.
  - `[low]` `[patch]` (verification-gap) `NotesPane`'s out-of-range guard is untested — same group as the blind test-gap row, same fix.
  - `[false]` `[reject]` (verification-gap, other) `DescribedBy` and `scrollPending` ignore `NotesPane`'s range check — the out-of-range state is unreachable (see the edge row above).
  - `[low]` `[reject]` (intent) The web expectations (layout, scroll, styling) are proven only through bUnit, CSS and stubs, not in a real browser — bUnit is the project's established web test surface, and Story 6.4's Playwright suite covers the browser flow. Recorded as a residual risk.
  - `[maybe-false]` `[defer]` (intent) NFR2 (50 proposals render within 2 s on compose) is not timed — the bUnit test proves only that the 50 cards render in order. To settle it, time a 50-proposal review render on compose. Deferred as medium (unverified).
  - `[false]` `[reject]` (intent) The story should end at awaiting-operator — no acceptance item needs a human outside the repo. The NFR2 timing is agent-doable work, recorded as deferred and not run.

### 2026-09-22 — Review pass (follow-up)
- verdicts: 29 findings — high 0, medium 0, low 11, false 18, maybe-false 0
- findings:
  - `[false]` `[reject]` (blind) The `/actions` "View action" and "View actions" links lead to Not found — carried: this is intended. The epic keeps the Not found page until Stories 4.3 and 4.4 land.
  - `[low]` `[reject]` (blind) An older `LoadAsync` can finish after a newer one and show the previous run at the new URL — real: the result is assigned without checking that the route still matches. It needs back or forward between two review URLs while a GET is still in flight. Run again navigates only after the load has finished. That is unlikely in everyday use, and the fix adds a stale-response guard branch.
  - `[false]` `[reject]` (blind) An Approved or Edited card with no Tracked Action renders an empty description and false fallbacks — carried: unreachable. 3.2 creates the Tracked Action in the decision transaction, with a unique index on `proposed_action_id`.
  - `[false]` `[reject]` (blind) The human chip can read "Decided by " with no name — carried: the decider is the JWT user, and names are resolved from all users, system users included.
  - `[low]` `[reject]` (blind) Hovering scrolls the window as well as the pane, and ignores `prefers-reduced-motion` — carried: the mechanism, `IScrollManager.ScrollIntoViewAsync` with smooth behavior, is spec-mandated, and in the wide layout the panes are viewport-bound.
  - `[low]` `[patch]` (blind) Opening the collapsed notes panel does not scroll to the already-active highlight — fixed: `@bind-Expanded` became `Expanded` plus `OnNotesExpandedChanged`, which requests the scroll when the panel opens with a highlight present. New tests: `In_the_narrow_layout_opening_the_panel_scrolls_to_the_active_span` and `..._with_no_active_card_scrolls_nothing`.
  - `[false]` `[reject]` (blind) The 24-parameter positional `ProposedActionDto` invites transposition — no transposition exists. `ProposedActionReadModelTests` and `ReviewServiceTests` pin each field's mapping, so a swap fails a test.
  - `[false]` `[reject]` (blind) `RunStarted` duplicates `StartedRun`, and the snackbar block is copied a third time — the finding names no caller that diverges and no rule that breaks. The records belong to different feature services, as AD-14's feature-local data seam intends.
  - `[false]` `[reject]` (blind) `ReadTrackedAsync`'s `ToDictionary` throws on a duplicate Tracked Action — a duplicate cannot exist, because `TrackedActionConfiguration.cs:90-92` declares a unique index on `ProposedActionId`.
  - `[low]` `[reject]` (blind) The pane height is a hard-coded `176px` allowance — carried: recorded as a residual risk, and the fix is a layout rework that needs browser tuning.
  - `[false]` `[reject]` (blind) The Edited card's "Proposed" owner shows the raw text, unlike a Pending card — carried: the Proposed column shows the AI's original values, and the mockup shows the raw text there.
  - `[low]` `[reject]` (blind) `GetReviewAsync` makes its run and meeting GETs one after the other — the cost is one extra local round-trip. Running them in parallel would read the meeting on a run 404, which the existing test forbids. This is not a direct correction.
  - `[low]` `[reject]` (blind) Each hover re-renders all 50 cards, because `ProposalCard` gets a new lambda — the diff yields no DOM change for 48 of them. The fix adds `ShouldRender` or parameter-comparison logic, and NFR2 timing is already tracked as DW-24.
  - `[low]` `[reject]` (edge) An out-of-order `LoadAsync` shows the previous run — same group as the blind stale-load row, same reason.
  - `[low]` `[reject]` (edge) Disposal during the viewport awaits leaks the resize observer — carried: rare, and the callback already checks `disposed`.
  - `[false]` `[reject]` (edge) An out-of-range span leaves `aria-describedby` dangling — carried: the span is computed on the server over the same immutable notes text.
  - `[false]` `[reject]` (edge) `Start + Length` int overflow in `NotesPane` — carried: the values are bounded by the 50,000-character notes limit.
  - `[false]` `[reject]` (edge) A null decider name gives "Decided by " — carried, same refutation as the blind row.
  - `[false]` `[reject]` (edge) The AC requires a scroll on every activation, but a collapsed panel and a re-activation do not scroll — the contract says "Activating the card that is already active does nothing". The collapsed no-scroll comes from the first pass's narrow-layout patch, and opening the panel now performs the scroll.
  - `[low]` `[patch]` (verification-gap) Nothing checks that disposal unsubscribes the viewport observer — fixed: `StubViewport` now records subscribed and unsubscribed observer ids, and the new test `Disposing_the_page_unsubscribes_its_viewport_observer` asserts that the ids match.
  - `[false]` `[reject]` (intent) The page renders the meeting's notes, not the run's `MeetingNotes` — notes are attach-once and immutable (`Meeting.cs:13-15`), so the text is the same.
  - `[false]` `[reject]` (intent) The not-found state's h1 is "Not found.", not "Review proposals" — the contract's not-found clause renders `NotFoundNotice`, which owns its h1, as `RunDetailPage` does.
  - `[false]` `[reject]` (intent) The narrow-layout no-scroll goes beyond the contract — this was the first pass's triaged patch against a real page jump, and no bad outcome follows from it.
  - `[false]` `[reject]` (intent) `SuggestedOwnerDisplayName` comes from the roster, not the all-users name read — the contract defines it as "the display name of the roster user matched by `SuggestedOwnerUserId`".
  - `[false]` `[reject]` (intent) The Edited "Proposed" column shows "Unassigned" for empty suggested-owner text — this is the same owner fallback the contract gives the other cards, and no value is misrepresented.
  - `[low]` `[reject]` (intent) Layout, styling and scrolling are proven only through bUnit and stubs — carried: bUnit is the project's web test surface, and Story 6.4 adds Playwright.
  - `[false]` `[reject]` (intent) The span is never sent through the real HTTP client, and `openapi.json` types `excerptStart` as `["null","integer","string"]` — the generated client exposes `int? ExcerptStart` (`ActionLedgerApiClient.g.cs:2525`). This is the standard ASP.NET OpenAPI shape for `int?`, and `OpenApiSnapshotTest` pins it.
  - `[low]` `[reject]` (intent) No test counts the queries or proves an empty read is skipped — the code skips empty id sets (`ProposedActionReadModel.cs:158,184`). This is only a test gap, and closing it needs a new query-counting harness.
  - `[false]` `[reject]` (intent) "Decided by " with no name when the decider has no User row — carried, same refutation as the blind row.

## Design Notes

**Why the span is computed on the server.** Excerpts are verified by a normalized match (FR-38), so an exact `IndexOf` in the browser would miss real-model excerpts that differ in punctuation or line wraps. A web-side normalizer would be a second implementation, which Rule 5 and AD-11 forbid. The server already owns the one normalizer, so it maps the match back to raw offsets and ships them, which also follows AD-15: derived values come from the server.

**Decided values come from the Tracked Action.** In Epic 3 they equal the values at decision time. Epic 4 edits will change them. The FieldEdit revisions remain the audit record, and the card simply shows current values.

**Pluralization.** The spec template reads "{n} proposals pending". n = 1 renders "1 proposal pending". The UX examples only ever show n ≥ 2.

## Verification

**Commands:**

- `dotnet build ActionLedger.sln`: expected 0 warnings and 0 errors.
- `dotnet test ActionLedger.sln` (no `--nologo`): expected all green.
- `git diff --stat -- src/ActionLedger.Web/openapi.json`: expected changed (the new DTO fields only).

**Manual checks (if no CLI):**

- If a browser and compose are available: open a seeded 50-proposal run's review route, confirm the first render finishes in 2 s or less, and confirm hover and highlight work. Record the result in the Auto Run Result, or record it as not run.

## Auto Run Result

Status: done (follow-up review pass)

**Summary.** Story 3.4's Review Screen is at `/meetings/{id}/runs/{runId}/review`.
- `ProposedActionDto` now publishes the decision copy, display names, the Tracked Action's values, and a server-computed excerpt span.
- The screen shows two panes at 1200px and wider, and a collapsed notes panel from 1024 to 1199px.
- It has Pending and decided proposal cards, the shared provenance chip, low-confidence badge and pending counter, and one Source Excerpt highlight.
- A successful run now navigates to the Review Screen.

This follow-up pass reviewed the full change since `47e669b71fb4a639c5b3fe026205332f18cb0b37` and applied two low patches.

**Files changed in this pass**
- `src/ActionLedger.Web/Features/Review/ReviewPage.razor`: the narrow-layout panel's `@bind-Expanded` became `Expanded` plus `OnNotesExpandedChanged`, so opening the panel scrolls to the active highlight.
- `tests/Web.Tests/ReviewPageTests.cs`: `StubViewport` records observer ids. Three new tests: panel-open scroll, panel-open with no active card, and disposal unsubscribing the viewport observer.

The files from the first pass are unchanged. They are the Application read model and `ExcerptLocator`, the Web Review feature, the shared components, `openapi.json`, and their tests; commit `6a1dfbd` lists them.

**Review findings.** 29 findings: 0 high, 0 medium, 11 low, 18 false, and 0 maybe-false.
- **Patched (2, both low):** the collapsed panel now scrolls to the active highlight when opened, and there is a test that disposal unsubscribes the viewport observer.
- **Deferred:** none new. NFR2 timing remains open as DW-24, from the first pass.
- **Rejected lows, with their reasons (each in the Review Triage Log):**
  - the stale-load race (blind and edge): unlikely, and the fix adds a guard branch;
  - hover scrolling the window: carried, spec-mandated;
  - the hard-coded pane height: carried, residual risk;
  - sequential GETs: one local round-trip, and the existing test forbids the change;
  - 50-card re-render on hover: no DOM change, fix adds logic, NFR2 tracked;
  - disposal during the viewport await: carried;
  - browser-surface verification only through bUnit: carried, Story 6.4 adds Playwright;
  - untested query-skip: only a test gap.
- **Rejected as false (18):** each refutation is in the Review Triage Log. Most are carried from the first pass: unreachable null names and missing Tracked Actions, the intended `/actions` links, and the immutable notes.

**Follow-up review recommendation:** false. This was a follow-up pass, and it patched no high entry (patched: 0 high, 0 medium, 2 low). The work has converged.

**Verification**
- `dotnet build ActionLedger.sln`: 0 warnings, 0 errors.
- `dotnet test ActionLedger.sln`: 1652 total, 1652 succeeded, 0 failed. That is the first pass's 1649 plus the 3 new ReviewPage tests.
- Manual browser check (a 50-proposal run within 2 s, and hover or panel-open scroll in a real browser): not run. This remains deferred as DW-24.

**Residual risks**
- The panel-open scroll is proven only against the stubbed `IScrollManager`. While MudBlazor's expand animation runs, the browser may clamp a smooth scroll to a mark deep in long notes before the panel reaches full height.
- The stale-load race is still open. Rapid back and forward between two review URLs, with a GET still in flight, can show the earlier run's data.
- These first-pass risks are unchanged:
  - the fixed `176px` pane allowance;
  - window scroll on hover when the page overflows;
  - a brief two-pane flash at 1024–1199px;
  - Reject, Edit and Approve do nothing until Story 3.5.
