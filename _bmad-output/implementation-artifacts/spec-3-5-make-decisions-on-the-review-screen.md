---
title: 'Story 3.5 — Make decisions on the Review Screen'
type: 'feature'
created: '2026-09-22'
baseline_revision: '28897ab4810582c85f8f676bcf87f88010bbb699'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
warnings: ['oversized']
deferred:
  - summary: >-
      Compose as committed does not start, because the api image lacks the embedded prompts and fixtures.
    evidence: |-
      src/ActionLedger.Api/Dockerfile copies src/ but not prompts/ or fixtures/, which ActionLedger.Infrastructure.csproj embeds from ../../. The api container crashes on boot with "Ai:PromptVersion 'v1' has no embedded prompt file. Embedded prompt versions: []". The 3.5 browser checks ran on a scratchpad compose override that adds `COPY prompts/ prompts/` and `COPY fixtures/ fixtures/` after `COPY src/ src/`. The defect predates this story and blocks the compose demo.
    location: >-
      src/ActionLedger.Api/Dockerfile
    severity: high
---

<intent-contract>

## Intent

**Problem:** The Review Screen from 3.4 shows Pending cards with Reject, Edit and Approve buttons, but nothing is bound to them. An Action Officer cannot approve, edit and approve, or reject a proposal from the UI, even though the decision endpoint has existed since 3.2 (epics.md:605-629; FR11, FR12, FR13, FR15 UI; NFR6, NFR8; UX-DR8, UX-DR19, UX-DR21).

**Approach:** Add approve and reject calls to `ReviewService`. Give `ProposalCard` a card-local edit mode with the owner picker from `UserDirectory`. Add a `RejectDialog` opened through `IDialogService`. `ReviewPage` sends the decision, refreshes the run from the server, and handles the in-flight, 409, 403 and failure states. Then do a real-browser keyboard pass and axe scan on compose and record the results.

## Boundaries & Constraints

**Always:**

- **Service (`Features/Review/Data/ReviewService.cs`):**
  - `ApproveAsync(Guid proposalId, ProposalValues values)` sends `Decision = Approve` with the values.
  - `RejectAsync(Guid proposalId, string? reason)` sends `Decision = Reject`. It sends the reason trimmed, and sends `null` when the reason is blank.
  - Both go through `CallAsync` and return `ReviewOutcome<ReviewState>`, the state the server recorded.
  - `ProposalValues(string Description, Guid? OwnerUserId, DateOnly? DueDate)` is web-owned. `DateOnly` converts to the generated `DateTimeOffset` at midnight with offset zero, so the day on the wire is the day chosen.
  - No generated type leaves the file.
- **Plain Approve** (not in edit mode) sends the proposal's own `Description`, `SuggestedOwnerUserId` and `SuggestedDueDate`, unchanged. The server then records Approved (`DecideProposalHandler.ApprovalAsync` compares them ordinally, against the pre-selected owner).
- **Edit mode (`ProposalCard`)** is card-local UI state. The card still injects nothing.
  - Edit swaps the read-only values for:
    - a `MudTextField` for the description, labelled `Voice.Description`, with `MaxLength` 500;
    - a `MudSelect<Guid?>` labelled `Voice.Owner`, listing "Unassigned" (`null`) and then the `Owners` roster in the order given, pre-selected from `SuggestedOwnerUserId`. The "AI suggested: {text}" hint stays visible below it;
    - a `MudDatePicker` labelled `Voice.DueDate`, with `Clearable`, `Editable` and the date format `yyyy-MM-dd`.
  - The action row becomes Cancel (text) and a filled primary button. The primary reads `Voice.ApproveWithEdits` ("Approve with edits") while any field differs from the proposed values, and `Voice.Approve` otherwise. The description is compared ordinally, the owner against `SuggestedOwnerUserId`, and the due date against `SuggestedDueDate`.
  - The primary button is disabled while the description is blank.
  - Cancel leaves edit mode and restores the proposed values. The next Edit starts from them again.
  - The primary raises `OnApprove` with the edited values.
  - A card that is no longer Pending never renders edit mode.
- **Card parameters:**
  - `Owners` (`IReadOnlyList<DirectoryUser>`).
  - `WritesDisabled` (`bool`) disables every button in the action row: Reject, Edit, Approve, Cancel and the edit-mode primary.
  - `OnApprove` is `EventCallback<ProposalValues>`, and `OnReject` is `EventCallback`.
  - `OnEdit` is removed, because edit mode is the card's own state.
- **`Features/Review/RejectDialog.razor`** is a presentational `MudDialog`.
  - It has a multi-line `MudTextField` labelled `Voice.RejectReasonLabel` ("Reason (optional)") with `MaxLength` 500.
  - Its buttons are Cancel (`RejectDialog.CancelId`) and a filled `Voice.Reject` confirm (`RejectDialog.ConfirmId`).
  - The confirm closes with `DialogResult.Ok(reason)`. Cancel cancels.
  - `ReviewPage` opens it with `IDialogService.ShowAsync<RejectDialog>(Voice.RejectDialogTitle)`. The title is "Reject this proposal?".
- **`ReviewPage` decision flow:**
  - It injects `UserDirectory` and `IDialogService` in addition to what it already injects. It calls `UserDirectory.EnsureLoadedAsync()` when it loads, and passes `Users` to every card.
  - **In flight:** a `deciding` flag is claimed before the dialog opens or the POST is sent, with `StateHasChanged` straight after. While it is set, every card gets `WritesDisabled`, and a second decision returns at once. It is released in a `finally`.
  - **Success:** the page refreshes the run through `GetReviewAsync`. The decided card re-renders per 3.4 in the same place, and `PendingCounter` recounts inside its existing `aria-live` region. `activeId` and the notes panel state are kept.
  - **409:** the global handler already shows "Already changed. Reloading.". The page performs the refresh and offers no Retry.
  - **403:** the global handler already shows the role snackbar. The page adds that proposal's id to a `forbidden` set, so its card stays `WritesDisabled` until the page is reloaded. There is no Retry.
  - **401:** nothing more. The global handler redirects.
  - **Any other failure:** a snackbar with `problem.Title` (Severity.Error) and a Retry action. The Retry resends the same decision, with the same values or reason, through `InvokeAsync` and without reopening the dialog. The edit-mode values stay in the card.
  - **A failed refresh** (after a success or a 409) keeps the current screen and shows a snackbar with its title and a Retry that repeats the refresh.
- All new copy is a `Voice` constant pinned in `VoiceAndFormatsTests`.

**Never:**

- No API, Application, Domain or `openapi.json` change. The client never chooses between Approved and Edited, and never sends an actor or a time.
- No confirmation for Approve. No optimistic local patch of a decided card: the refresh is the source of the decided values.
- No new JS file and no `tokens.css` edit. No HTTP outside `ReviewService`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Plain approve | Pending card, click Approve | POST Approve with the proposed values → refresh → Approved card, counter −1 | — |
| Edit, change nothing | Edit, then primary | label "Approve", sends the proposed values | — |
| Edit a field back | Change the owner, then change it back | label returns to "Approve" | — |
| Approve with edits | Owner changed, date cleared | label "Approve with edits"; POST has the new owner and `dueDate` null → Edited card | — |
| Reject with reason | Dialog, "  dup  ", Reject | POST Reject with reason "dup" → Rejected card, "Reason: dup" | — |
| Reject dismissed | Dialog, Cancel | no POST; buttons enabled again | — |
| Conflict | POST → 409 | refresh; no Retry | global snackbar |
| Forbidden | POST → 403 | that card's buttons stay disabled | global snackbar |
| Server/transport error | POST → 500 or throws | card keeps its edit values; snackbar title + Retry resends | Retry |

</intent-contract>

## Code Map

- `src/ActionLedger.Web/Features/Review/ReviewPage.razor`: the container.
  - Follow `RunAgainAsync`/`RetryRunAgainAsync` for the claim-before-await flag, the snackbar Retry shape, and the `disposed` guard.
  - `Cards` (the render fragment near the end) is where the card callbacks bind.
  - `LoadAsync` is the full load. The refresh is a lighter sibling that keeps the screen when it fails.
- `src/ActionLedger.Web/Features/Review/ProposalCard.razor`: `OnApprove`/`OnEdit`/`OnReject` are declared but unbound. The Pending branch is the `default:` case of the `switch`, and the action row is the last `@if`.
- `src/ActionLedger.Web/Features/Review/Data/ReviewService.cs`: `CallAsync`, `ReviewOutcome<T>`, `ToReviewState`, and `ToDate`, the inverse of the new conversion. The generated `DecideProposedActionAsync(Guid, DecideProposalCommand, CancellationToken)` returns `ProposalDecisionDto` (`ReviewState`, `TrackedActionId`, `DecidedByUserId`, `DecidedAt`). It has no display names, which is why the page refreshes. `DecideProposalCommand.DueDate` is a `DateTimeOffset?` with `DateFormatConverter`.
- `src/ActionLedger.Application/Review/DecideProposalHandler.cs` (read-only): the server-side Approved/Edited diff. The description is required (1–500 characters, not blank) on approve, and a reason on approve is a 400.
- `src/ActionLedger.Web/Features/Meetings/MeetingDetailPage.razor:440-520`: the reference `IDialogService` + `ConfirmDialog` + 409 reload + Retry flow. `src/ActionLedger.Web/Shared/ConfirmDialog.razor` is the dialog shape (stable button ids, `IMudDialogInstance`).
- `src/ActionLedger.Web/Core/Auth/SessionMessageHandler.cs`: raises the 401, 403 (`Voice.RoleNotAllowed`) and 409 (`Voice.AlreadyChanged`) snackbars globally. The page must not add them again.
- `src/ActionLedger.Web/Core/Users/UserDirectory.cs`: `EnsureLoadedAsync`, `Users` and `DirectoryUser(Id, DisplayName, Role)`. A failed load leaves the roster empty, so only Unassigned is offered.
- `src/ActionLedger.Web/Core/Voice/Voice.cs`: `Approve`/`Edit`/`Reject`/`Cancel`/`Description`/`Owner`/`DueDate`/`Retry` already exist.
- `_bmad-output/planning-artifacts/ux-designs/ux-ActionLedger-2026-09-20/EXPERIENCE.md:93,125-127` and `mockups/review-screen.html:236-267`: edit mode, the 403, 409 and failure rows.
- `tests/Web.Tests/{ReviewPageTests,ProposalCardTests,ReviewServiceTests,StubApiClient,MeetingDetailPageTests}.cs`:
  - the stub's `DecideProposedActionAsync` currently throws `NotExercised`;
  - `MeetingDetailPageTests` has the `Render<MudDialogProvider>()` pattern;
  - MudSelect and MudDatePicker need `MudPopoverProvider` rendered.
- `tests/Architecture.Tests/WebStructureTests.cs`: the HTTP-seam and one-routable-page rules.
- `.github/pull_request_template.md`: the "axe pass" checklist item the accessibility record mirrors.

## Tasks & Acceptance

**Execution:**

- `src/ActionLedger.Web/Features/Review/Data/ReviewService.cs`: add `ApproveAsync`, `RejectAsync`, the `ProposalValues` record and the `DateOnly` → wire conversion.
- `src/ActionLedger.Web/Features/Review/ProposalCard.razor`: edit mode, the diff-driven label, `Owners`, `WritesDisabled`, the typed `OnApprove`, and remove `OnEdit`.
- `src/ActionLedger.Web/Features/Review/RejectDialog.razor`: the new dialog.
- `src/ActionLedger.Web/Features/Review/ReviewPage.razor`: the decision flow, the refresh, `forbidden`, the roster load, and binding the callbacks.
- `src/ActionLedger.Web/Core/Voice/Voice.cs`: `ApproveWithEdits`, `RejectDialogTitle`, `RejectReasonLabel`. Update the card remarks that say "Story 3.5 binds it".
- `src/ActionLedger.Web/wwwroot/css/app.css`: edit-mode field layout, only if MudBlazor's defaults do not suffice.
- Tests:
  - `ReviewServiceTests`: the command sent for approve and reject (trimmed reason, blank → null, the wire date), the outcome state, and the failure mapping.
  - `ProposalCardTests`: the edit-mode controls and pre-selection, the label diff in both directions, Cancel restoring the values, the blank-description disable, `WritesDisabled`, and the `OnApprove` payloads.
  - `ReviewPageTests`: every matrix row, including the counter update, the disabled state during flight (with a stub that holds the POST on a `TaskCompletionSource`), the refresh after success and after 409, the 403 lock, and Retry resending.
  - `RejectDialog` tests: in `ReviewPageTests` or their own file.
  - `VoiceAndFormatsTests`: the new copy.
  - Extend `StubApiClient` with a decision recorder and scripted outcomes.
- `_bmad-output/implementation-artifacts/pr-checklist-3-5-make-decisions-on-the-review-screen.md`: the manual keyboard pass and axe scan results, in the PR template's checklist shape (see Verification).

**Acceptance Criteria:**

- Given a Pending card, when Edit is clicked, then the description is a `MudTextField`, the owner a `MudSelect` of Unassigned plus the `UserDirectory` roster, pre-selected from `suggestedOwnerUserId`, and the due date a clearable `MudDatePicker`, with Cancel and a primary whose label is "Approve with edits" exactly while a value differs. Cancel restores the proposed values.
- Given Reject is clicked, when the dialog opens through `IDialogService`, then it offers an optional reason and a Reject confirm.
- Given an approve, an approve with edits, or a reject, when the response returns, then the card shows its decided state per 3.4, the Pending counter in its `aria-live` region shows the new count, the write buttons were disabled while the request was in flight, and a 409 leads to "Already changed. Reloading." and a refreshed run.
- Given the Review Screen on compose, when a keyboard-only pass and a browser axe scan run, then every control is reachable and named, no indicator relies on color alone, and the results are recorded in the PR checklist file. The page and its data service have bUnit tests in `tests/Web.Tests`.
- Given the solution, when `dotnet build` and `dotnet test` run, then there are 0 warnings and every test passes, including `WebStructureTests` and `OpenApiSnapshotTest` (unchanged).

## Spec Change Log

## Review Triage Log

### 2026-09-22 — Review pass
- verdicts: 43 findings — high 2, medium 2, low 26, false 13, maybe-false 0
- findings:
  - `[low]` `[reject]` (blind) A failed refresh after a successful decision leaves the card Pending and enabled, so a second decision gets a 409 — real, but it needs a failed GET right after a successful POST. The 409 then refreshes the card to its decided state. The fix adds a "decided, not yet shown" lock set.
  - `[low]` `[reject]` (blind) A stale snackbar Retry resends the values captured at failure after the draft was edited — it needs a failure, then an edit, then choosing the snackbar Retry over the card's own primary button. The refresh then shows the recorded values, so nothing is hidden. The fix needs a draft channel from the card to the page.
  - `[low]` `[patch]` (blind) The edit-mode fields stay editable while a write is in flight or the card is 403-locked — fixed: `Disabled="@WritesDisabled"` on the text field, select and date picker, asserted in the existing WritesDisabled card test.
  - `[low]` `[reject]` (blind) A 400 gets a Retry that fails the same way — unreachable from the UI. A blank description disables the primary, the owner choices come from the server roster, and AI descriptions are length-validated at extraction.
  - `[low]` `[reject]` (blind) Focus is lost after a 403 or after a successful snackbar Retry — edge paths (a role refusal, and a snackbar-driven retry). The fix adds focus branches.
  - `[medium]` `[patch]` (blind) `RejectDialog`'s 150 ms delayed focus can run after the dialog closed — confirmed: Blazor's focus on a removed element throws `JSException` out of `OnAfterRenderAsync`, which raises the WASM error UI. Fixed: a `closed` flag set in Confirm, Cancel and `Dispose` (Escape disposes), plus a `catch (JSException)`.
  - `[low]` `[reject]` (blind) The reject dialog does not name the proposal — the user opened it from that card, and the dialog returns focus there. Adding the name needs a new parameter and copy.
  - `[false]` `[reject]` (blind) The roster load delays the screen — `UserDirectory` loads at sign-in, and `EnsureLoadedAsync` returns at once when `IsLoaded`.
  - `[low]` `[reject]` (blind) With a failed roster, the pre-selected owner cannot be picked again once changed — it needs a failed roster load, which is rare. The fix adds a synthetic select item. The display fallback is now tested (see the verification-gap row).
  - `[false]` `[reject]` (blind) Whitespace-only edits create an Edited record — this is by design. The spec requires the same ordinal comparison `DecideProposalHandler` uses, so the label and the server always agree.
  - `[low]` `[reject]` (blind) An invalid typed date and the untabbable clear icon — MudDatePicker marks an unparseable date as an error and keeps the previous value. Deleting the typed text clears the date from the keyboard. Guarding it adds parse-error state.
  - `[low]` `[reject]` (blind) The app-wide focus ring may be hard to see on dark surfaces — it replaces no visible focus at all, which is strictly better. No surface is named where it is worse than before.
  - `[false]` `[reject]` (blind) The `<main>` wrapper could duplicate a page's own main — the only `<main>` in the web project is `MainLayout.razor:46`, and `MainLayout` is the only layout.
  - `[false]` `[reject]` (blind) The two-column edit row is squeezed at narrow widths — below 1024px is unsupported, and at 1024px and wider the cards column gives each field enough width.
  - `[high]` `[defer]` (blind) The spec was not updated for the extra scope, and the Dockerfile defect is not recorded — the Dockerfile defect is real, pre-existing and blocks compose. It is deferred as high. The spec-edit part is rejected: its fix is to edit this build's spec.
  - `[false]` `[reject]` (blind) The global 409 and 403 snackbars are untested on this endpoint — `SessionMessageHandler` handles every response the generated client makes, whatever the endpoint. `SessionMessageHandlerTests` pins the snackbars, and its registration on the client is covered by `ApiClientRegistrationTests`.
  - `[low]` `[reject]` (blind) The Escape path is untested — Escape is MudBlazor's own `CloseOnEscapeKey`, and it leads to the same Canceled result the Cancel test covers. It was exercised in the live keyboard pass.
  - `[low]` `[reject]` (edge) Retry sends the old captured values after the draft changed — same as the blind row, same reason.
  - `[low]` `[reject]` (edge) A 400 or 422 gets a futile Retry — same as the blind row, same reason.
  - `[low]` `[reject]` (edge) A route change during the POST shows a snackbar with a dead Retry on the new route — rare. The snackbar is accurate, the Retry is refused harmlessly by `TryClaimDecision`, and a stale id in `forbidden` never matches the new run.
  - `[false]` `[reject]` (edge) A refresh GET answering 403 or 409 gives duplicate snackbars — the run read is open to every signed-in role and never answers 409.
  - `[medium]` `[patch]` (edge) `RejectDialog` focuses after it closed — same group as the blind row, same fix.
  - `[low]` `[reject]` (edge) The suggested owner cannot be re-picked when it is missing from the roster — same as the blind row.
  - `[low]` `[reject]` (edge) An unparseable typed date sends the previous value — same as the blind row.
  - `[low]` `[patch]` (edge) The edit fields stay editable during flight — same as the blind row, same fix.
  - `[low]` `[reject]` (edge) `decisionRaised` survives a 403, so a later refresh could pull focus to that card — it needs a 403 and then the same card decided elsewhere. The fix adds a branch.
  - `[low]` `[reject]` (edge) A route change during `EnsureLoadedAsync` makes the load read the new ids — the roster is normally already loaded, so the await completes at once. This is the same stale-load race 3.4 triaged and rejected.
  - `[false]` `[reject]` (edge) The edit row on mobile — same refutation as the blind row: below 1024px is unsupported.
  - `[low]` `[reject]` (edge) The checklist says every control is reachable, but the date picker's clear icon is not — the clear function works from the keyboard by deleting the typed date, which satisfies keyboard operability. MudBlazor's adornment is `tabindex=-1` by design. The checklist records this honestly.
  - `[low]` `[patch]` (verification-gap) The 401 branches of the decision and the refresh are untested — added `An_unauthorized_decision_adds_no_snackbar_and_no_refresh` and `An_unauthorized_refresh_after_a_decision_offers_no_retry`.
  - `[low]` `[patch]` (verification-gap) Nothing proves a stale Retry on a decided card sends nothing — added `A_stale_retry_on_a_card_decided_since_sends_nothing`.
  - `[low]` `[patch]` (verification-gap) The owner select's fallback name with an empty roster is untested — added `A_pre_selected_owner_missing_from_the_roster_shows_the_servers_display_name`.
  - `[low]` `[reject]` (verification-gap) The refresh's route-change guard is untested — the window is narrow, and the test needs a new gate on the run read in the stub.
  - `[false]` `[reject]` (verification-gap, other) `forbidden.Clear()` on a route change is untested — the layer filed this for completeness only, with no bad outcome.
  - `[false]` `[reject]` (intent) The manual keyboard pass should be a human's, which would make this awaiting-operator — the pass and the scan are in-product browser checks within agent reach, and they were performed. The awaiting-operator clause covers actions outside the repo (a domain, DNS, keys, a vendor console). Stories 1.6 and 1.7 set this precedent.
  - `[high]` `[defer]` (intent) The checks ran on a compose override, not on the committed stack — the root cause is the pre-existing Dockerfile defect, in the same group as the blind high row. The Review Screen code under test is identical either way.
  - `[false]` `[reject]` (intent) The results are recorded in a file, not in a PR — this workflow commits on the branch without opening a PR. The checklist file mirrors `.github/pull_request_template.md` so it can be pasted into the PR when one is opened, and opening a PR is not a human-only action.
  - `[false]` `[reject]` (intent) The 409 message is tested only in isolation — same refutation as the blind global-snackbar row.
  - `[low]` `[reject]` (intent) The aria-live announcement is checked by markup, not by listening — bUnit cannot hear, and the region markup is what produces the announcement. A screen-reader pass is recorded as not done.
  - `[low]` `[reject]` (intent) The clear icon versus "every control reachable" — same as the edge claim row.
  - `[low]` `[reject]` (intent) The global CSS, layout and link changes go beyond the Review Screen — each fixes an axe or keyboard failure found in the pass the AC requires. The `<main>` change is tested, and the focus ring only adds an outline where none showed.
  - `[false]` `[reject]` (intent) The awaiting-operator clause was not used — nothing a human must do outside the repo is owed (see the first intent row).
  - `[false]` `[reject]` (intent) `ReviewServiceTests` are not bUnit — they live in `tests/Web.Tests`, the bUnit project, alongside the bUnit page tests. The data service has no markup to render.

## Design Notes

**Refresh, not patch.** `ProposalDecisionDto` has no display names and no Tracked Action values, and the web must not reconstruct derived values (AD-15). One `GetReviewAsync` after the decision brings the whole decided card from the server. The same refresh serves the 409 path, so there is a single way to show a decided card.

**One decision at a time.** A single page-level `deciding` flag disables writes on every card, rather than tracking a set of in-flight proposals. The refresh replaces the whole screen, so two overlapping decisions would race each other's refreshes. The dialog is modal anyway.

**Card-local edit state.** The edit values are view state, not data, so the card owns them. That keeps the page free of per-card dictionaries. `@key="proposal.Id"` keeps the values across the page's re-renders and refreshes, which is what "form values are retained" on a failure needs.

## Verification

**Commands:**

- `dotnet build ActionLedger.sln`: expected 0 warnings and 0 errors.
- `dotnet test ActionLedger.sln` (no `--nologo`): expected all green.
- `git diff --stat -- src/ActionLedger.Web/openapi.json src/ActionLedger.Api src/ActionLedger.Application src/ActionLedger.Domain`: expected empty.

**Manual checks (agent-run in a real browser):**

- Bring up compose (`docker compose up`), sign in as an Action Officer, create a meeting, attach notes, and run an extraction with the fake provider to reach a review route.
- Using the keyboard only: Tab to each card, then Edit, the text field, the owner select (open it and choose), the date picker (type a date and clear it), Cancel, "Approve with edits", and Reject → the dialog → the reason → the confirm. Confirm that every control is reachable, has a visible or accessible name, and shows a focus indicator.
- Inject axe-core (cdnjs `axe-core`) and run `axe.run()` on the review route: at rest, in edit mode, with the reject dialog open, and after decisions. Record the violations (the target is none), the browser and the date in the PR checklist file.
- If compose or a browser cannot run, record the checks as not run, with the reason. Do not claim them.

## Auto Run Result

Status: done

**Summary.** The Review Screen now makes decisions.
- A Pending card approves as is, enters an edit mode, or opens a Reject dialog with an optional reason.
- In edit mode the description is a text field, the owner a select over the `UserDirectory` roster plus Unassigned (pre-selected from the server's match), and the due date a clearable, typeable date picker. The primary reads "Approve with edits" exactly while a value differs, and Cancel restores the proposed values.
- `ReviewPage` allows one decision in flight at a time, with every card's controls disabled meanwhile. After a success or a 409 it re-reads the run, so the decided card and the Pending counter come from the server. A 403 locks that card. Other failures show a snackbar whose Retry resends the same decision.
- The keyboard pass and the axe scan were run in a real browser, and their results are recorded.

**Files changed:**
- `src/ActionLedger.Web/Features/Review/Data/ReviewService.cs`: `ApproveAsync`, `RejectAsync`, `ProposalValues`, and the date conversion for the wire.
- `src/ActionLedger.Web/Features/Review/ProposalCard.razor`: edit mode, the diff-driven label, `Owners`, `WritesDisabled`, the typed `OnApprove`, and focus handling.
- `src/ActionLedger.Web/Features/Review/RejectDialog.razor`: new. The optional reason, Cancel and Reject.
- `src/ActionLedger.Web/Features/Review/ReviewPage.razor`: the decision flow, the refresh, the 401, 403 and 409 handling, Retry, and the roster load.
- `src/ActionLedger.Web/Core/Voice/Voice.cs`: `ApproveWithEdits`, `RejectDialogTitle`, `RejectReasonLabel`.
- `src/ActionLedger.Web/Layout/MainLayout.razor`: the `<main>` landmark (axe).
- `src/ActionLedger.Web/wwwroot/css/app.css`: the edit-row layout and the keyboard focus ring (keyboard pass).
- `tests/Web.Tests/{ReviewPageTests,ProposalCardTests,ReviewServiceTests,StubApiClient,LayoutTests,VoiceAndFormatsTests}.cs`: tests for every matrix row, the 401 path, the stale Retry, and the roster fallback.
- `_bmad-output/implementation-artifacts/pr-checklist-3-5-make-decisions-on-the-review-screen.md`: the keyboard pass and axe scan record, in the PR template's checklist shape.

**Review findings:** 43 in total — high 2, medium 2, low 26, false 13.
- **Patched:**
  - 1 medium entry: the RejectDialog delayed focus after close.
  - 3 low entries: the edit fields now disable with the buttons, plus new tests for the 401 path, the stale Retry, and the empty-roster owner name.
- **Deferred:** 1 high entry, the committed api Dockerfile. It omits `prompts/` and `fixtures/`, so compose does not start. This is pre-existing, and it blocks the compose demo.
- **Rejected:** every other finding, each with its reason in the Review Triage Log above.

**Follow-up review recommended:** false. One medium entry and three low entries were patched, with no high patch.

**Verification:**
- `dotnet build ActionLedger.sln`: 0 warnings, 0 errors.
- `dotnet test ActionLedger.sln`: 1694 of 1694 passed.
- There are no changes under Api, Application, Domain or `openapi.json`.
- Browser checks: headless Chrome 145 with axe-core 4.10.2, on compose with the scratchpad Dockerfile override, signed in as dana. axe found 0 violations at rest, in edit mode, with the reject dialog open, and after decisions. In the keyboard-only pass, every control was reachable and named.

**Residual risks:**
- A live 403 and a live 409 were not produced; bUnit covers both. No screen-reader pass was done.
- The date picker's clear icon is not in the tab order. Deleting the typed date clears it instead.
- The reject dialog's reason-field focus relies on a 150 ms timing workaround.
- Four test meetings, "(3.5 check a–d)", remain in the local compose database volume.
