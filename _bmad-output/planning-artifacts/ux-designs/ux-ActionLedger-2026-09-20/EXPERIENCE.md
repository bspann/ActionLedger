---
name: ActionLedger
status: final
sources:
  - _bmad-output/planning-artifacts/prds/prd-ActionLedger-2026-09-19/prd.md
  - _bmad-output/planning-artifacts/prds/prd-ActionLedger-2026-09-19/addendum.md
  - docs/bmad-seed-prompt.md
created: 2026-09-20
updated: 2026-09-20
---

# ActionLedger — Experience Spine

## Foundation

Desktop web, single surface, Angular current stable with standalone components and signals, Angular Material 3. `DESIGN.md` is the visual identity reference and names the four semantic color families the product adds to the prebuilt theme. This spine specifies behavior only. Vocabulary is the PRD Glossary, used verbatim.

The PRD addendum's front-end layering rule (no HTTP calls in components, typed clients as the model) is architecture, not experience. It appears here only as the Feature folder column below. `docs/frontend-architecture.md` will diagram the mapping at tag time.

Wireframe level. Layout and behavior are decided here. Visual polish beyond Material defaults is a non-goal.

## Information Architecture

Two top-level destinations in the toolbar, one auth surface, and detail surfaces reached by row click. Every PRD screen name is mirrored.

| Surface | Route | Feature folder | Reached from | Purpose | PRD |
|---|---|---|---|---|---|
| Login | `/login` | `auth` | Unauthenticated redirect | Username and password, JWT into memory | FR-23 |
| Meeting List | `/meetings` | `meetings` | Toolbar "Meetings", app open | Meetings newest first with run and action counts; "New meeting" button | FR-3 |
| Meeting Detail | `/meetings/:id` | `meetings` | Meeting List row | Fields, Meeting Notes (paste once), Extraction Runs list, "Run extraction" | FR-1, FR-2, FR-3, FR-4 |
| Run Detail | `/meetings/:id/runs/:runId` | `review` | Meeting Detail run row | Run metadata, warnings, failure reason, Proposed Actions with Review States and decisions, "Review" and "Run again" | FR-6, FR-9 |
| Review Screen | `/meetings/:id/runs/:runId/review` | `review` | Run Detail "Review", Meeting Detail "Review" on the latest Succeeded run | Two-pane: Meeting Notes beside proposal cards; Approve, Edit, Reject | FR-10 to FR-15 |
| Action List | `/actions` | `actions` | Toolbar "Actions", "View actions" links | Filterable, sortable, paged table of Tracked Actions with the Overdue indicator | FR-18, FR-19 |
| Action Detail | `/actions/:id` | `actions`, `audit` | Action List row, "View action" on a decided card | Fields, status control, edit, and the Audit Trail | FR-17, FR-20, FR-22 |

Closure: every stated need lands on a surface. Paste notes and extract on Meeting Detail, review on Review Screen, see overdue on Action List, prove provenance on Action Detail, log in on Login. Every surface is reached by a Key Flow below. The webhook receiver and Swagger UI are separate pages outside the Angular app and are not designed here.

→ Composition references, each linked again at the section it illustrates: `mockups/review-screen.html` (Review Screen, six cards in five states), `mockups/action-list.html` (Action List with filters and Overdue indicator), `mockups/action-detail.html` (Action Detail with Audit Trail). Spine wins on conflict.

## Voice and Tone

Microcopy. Plain, declarative, no exclamation marks, no emoji. The AI is referred to as "AI", never "assistant" or "we". The Login screen's button reads "Sign in" and the user menu reads "Sign out". The PRD's "log in" is the same act.

| Do | Don't |
|---|---|
| "Proposed by AI" | "AI suggestion ✨" |
| "Decided by Dana Whitfield" | "Approved!" |
| "Low confidence" | "AI isn't sure about this one" |
| "3 proposals pending" | "You have 3 items left to review" |
| "Extraction failed. {server-supplied reason}", for example "The AI returned invalid output twice." | "Something went wrong" |
| "No actions match these filters." | "Nothing here yet!" |
| "Run extraction" | "Let AI find your actions" |
| Button verbs are the Review Decision names: Approve, Edit, Reject | "Accept", "Modify", "Dismiss" |

Dates render as `2026-10-03`. Timestamps as `2026-09-21 14:03 UTC`, always UTC with the suffix, because the Audit Trail is evidence and the server evaluates Overdue in UTC. Relative time ("2 hours ago") is not used anywhere.

## Provenance Language

Product-specific section. The PRD thesis is that the record always shows what the AI proposed versus what a human decided. Every surface answers "who said this" with the same three devices from `DESIGN.md`: a provenance chip (icon plus text), a provenance color family, and a named person or "AI" in the copy.

| Surface | AI provenance shown as | Human provenance shown as |
|---|---|---|
| Review Screen, Pending card | "Proposed by AI" chip in the header; Source Excerpt blockquote on `{colors.ai-provenance-container}`; owner hint "AI suggested: {text}" always visible | None yet |
| Review Screen, decided card | Original values in a "Proposed" column on `{colors.ai-provenance-container}` when Edited | Human provenance chip "Decided by {name}" with the timestamp beside it replaces the action row. The Review State chip says Approved, Edited, or Rejected. A Rejected card shows "Reason: {text}" or "No reason given" |
| Run Detail | AI Provider, model, Prompt Version in the metadata block | Per proposal row: Review State chip, decider display name, timestamp, and rejection reason, all as visible text |
| Action Detail header | "Created from AI proposal, run {Prompt Version}" link to Run Detail | Owner, status, and last-changed-by name |
| Audit Trail | First entry marked `{components.audit-entry-ai}` with all five proposed fields | Every later entry marked `{components.audit-entry-human}` with the User's display name |
| Action List | Not shown. The list is the committed record. | Owner column |

Rule: wherever a human-decided value could appear, an AI-suggested value in that position must carry the AI provenance treatment.

## Component Patterns

Behavioral. Visual specs live in `DESIGN.md.Components` or in Angular Material defaults.

| Component | Use | Behavioral rules |
|---|---|---|
| Provenance chip | Review Screen, Run Detail, Action Detail, Audit entry | Two variants, AI and human, per `DESIGN.md`. AI text is always "Proposed by AI". Human text is "Decided by {display name}"; the timestamp sits beside the chip, never inside it. Static, not clickable. In an `aria-live="polite"` region on the Review Screen. |
| Low Confidence badge | Proposal card, Audit entry, Run proposal rows | Shown when the API's `isLowConfidence` flag is true. Icon plus text. Static. Never blocks a Review Decision. |
| Overdue indicator | Action table, Action Detail | Shown when the API's `isOverdue` flag is true. Icon plus text. Static. Never shown on Complete or Cancelled. |
| Status chip | Action table, Action Detail, Meeting Detail action counts | Text is the Action Status name. Static. Changing status is the Status control's job. |
| Review State chip | Proposal card, Run proposal rows | Text is the Review State name. Static. Edited carries the edit icon. |
| Toolbar | Global | Product name links to Meeting List. "Meetings" and "Actions" links show active state. User menu shows display name and Role, one item: "Sign out". |
| Meeting table | Meeting List | Columns: Title, Date, Runs, Tracked Actions. Row click opens Meeting Detail. Sort by Meeting date descending, then creation time descending. Paginator appears at 50 rows. |
| New meeting dialog | Meeting List | Title (required, max 200), Date (required), Attendees as chip input (each 1 to 100 characters). Validation messages under each field. "Create" opens Meeting Detail. |
| Notes paste area | Meeting Detail | A `mat-form-field` textarea with a 50,000 character limit and a live count. Caption beside the button: "Notes cannot be changed after saving". "Save notes" is enabled only when the textarea is non-empty and opens a confirm dialog with the same sentence. After save the textarea becomes read-only text rendered with `white-space: pre-wrap` and a caption "Saved {timestamp}, immutable". No edit affordance ever appears. |
| Run extraction button | Meeting Detail | When there are no Meeting Notes, the button is disabled with a visible caption "Add notes first" beneath it. On click it disables itself and shows an indeterminate `mat-progress-bar` with the caption "Extracting with {AI Provider} · {model}. This can take up to a minute with a local model." On success it navigates to the Review Screen for the new run. On failure it stays on Meeting Detail and shows the failed run in the list with its reason. The active AI Provider and model come from a read-only API endpoint (PRD FR-7). |
| Run list | Meeting Detail | A `mat-table`. Rows: started timestamp, Prompt Version, AI Provider and model, outcome, count of Proposed Actions, Pending count. Succeeded rows have "Review"; Failed rows have "Details". |
| Run metadata block | Run Detail | Plain two-column definition list, not a table: AI Provider, model, Prompt Version, started, duration, input tokens, output tokens (rendered as "0" for the Fake Provider, never blank), outcome, failure reason, warnings. Warnings expand to show each dropped Source Excerpt. |
| Run proposal rows | Run Detail | One row per Proposed Action in AI return order: description, Confidence Score, Review State chip, decider display name, decision timestamp, rejection reason if any. "Review" button when any row is Pending. |
| Proposal card | Review Screen | See `DESIGN.md.Components`. Cards appear in AI return order and stay in place after a decision. A Pending card shows description, owner, and due date as read-only values with a "Proposed by AI" chip, the Source Excerpt blockquote, and three controls: Reject (text), Edit (outlined), Approve (filled). A null suggested due date reads "No due date proposed"; an empty suggested owner reads "Unassigned" with the hint "AI suggested: none". **Edit** switches the card into edit mode: description becomes a text field, owner becomes a `mat-select` of Users plus "Unassigned", due date becomes a `mat-datepicker` with a clear button. The owner select is pre-selected on a case-insensitive display-name match, and the hint "AI suggested: {text}" is always visible whether or not it matched. In edit mode the primary button reads "Approve with edits" while at least one field differs from the proposed value (the pre-selected owner counts as the proposed value) and reverts to "Approve" when nothing differs. A "Cancel" text button leaves edit mode and restores the proposed values. **Reject** opens a small dialog with an optional reason field and a "Reject" confirm. After any decision the card keeps its place and its Source Excerpt blockquote. The controls are replaced by the human provenance chip, the timestamp, and the Review State chip. A Rejected card shows "Reason: {text}" or "No reason given". An Edited card shows a "Proposed" column beside the decided values. An Approved or Edited card shows a "View action" link. → `mockups/review-screen.html`. |
| Source Excerpt highlight | Review Screen | Focusing or hovering a card highlights its Source Excerpt in the notes pane and scrolls the pane to it. Clicking the blockquote does the same. One highlight at a time. The notes pane renders with `white-space: pre-wrap`. |
| Pending counter | Review Screen header | Per `DESIGN.md`. Reads "{n} proposals pending" and updates after every decision. At zero it reads "All proposals decided" with a "View actions" link to the Action List filtered to this Meeting. |
| Action table | Action List | Columns: Description, Owner, Due date, Status, Meeting, Overdue indicator. Default sort is Due date ascending, nulls last. Sortable on Due date and Status. Paged at 50. Row click opens Action Detail. The Overdue indicator renders from the API's `isOverdue` flag, never from a client-side date compare. → `mockups/action-list.html`. |
| Filter bar | Action List | Owner `mat-select` (all Users plus "Unassigned"), Status multi-select, Due date range picker, Meeting `mat-select`, "Overdue only" toggle. Filters combine with AND, apply on change, and are reflected in the URL query string so a filtered view can be linked. "Clear filters" appears when any filter is set. |
| Status control | Action Detail | `mat-select` listing every transition allowed from the current Action Status (FR-17). For an Action Officer, Cancelled is listed but disabled with the caption "Lead only", so the Role boundary is visible on screen. Open to In Progress and In Progress to Open apply on selection with a snackbar "Status set to {status}" and an "Undo" action that performs the reverse. Complete and Cancelled open a confirm dialog "Set to {status}? This cannot be reopened." and offer no Undo. Once Complete or Cancelled, the control is disabled. |
| Edit fields | Action Detail | Description, Owner, Due date editable inline with "Save" and "Cancel". Disabled on Complete and Cancelled with caption "{Status} actions cannot be edited". |
| Audit entry | Action Detail, Audit Trail | Entries listed oldest first in a vertical timeline. Entry anatomy: marker, kind label, actor ("AI" or display name), timestamp, and for field changes a two-column "Was / Now". The AI Proposal entry shows all five proposed fields: description, suggested owner text, suggested due date, Confidence Score, and Source Excerpt, plus the Low Confidence badge when it applied. Never collapsed; the whole history is always visible. → `mockups/action-detail.html`. |
| Login form | Login | Username, password, "Sign in". On 401 shows "Sign-in failed. Check your username and password." under the button without saying which was wrong. |

## State Patterns

| State | Surface | Treatment |
|---|---|---|
| Cold load | Every table and detail | `mat-progress-bar` under the toolbar; the body is empty until data arrives. No skeleton rows. |
| Load failure | Any table or detail | Progress bar stops; body shows "Couldn't load. {problem detail title}" with a "Retry" button. Filters and the toolbar stay usable. |
| Not found | Any detail route | "Not found." with a link to the parent list. |
| No Meetings | Meeting List | "No meetings yet." with a primary "New meeting" button. Seed data means this is rare. |
| Meeting without notes | Meeting Detail | Notes paste area is the dominant element; runs list shows "No extraction runs. Add notes, then run extraction." |
| Extraction in progress | Meeting Detail | Button disabled, progress bar, caption from the Run extraction button row. Nothing else on the page is blocked. |
| Extraction Failed | Meeting Detail, Run Detail | Failed run row and Run Detail show the server-supplied failure reason verbatim and a "Run again" button. |
| Run with warnings | Run Detail | Warnings count in the metadata block; expansion shows each dropped Source Excerpt. |
| Zero Proposed Actions | Review Screen | "The AI found no actions in these notes." with "Run again" and "Back to meeting". |
| All proposals decided | Review Screen | Pending counter reads "All proposals decided"; cards remain visible in decided state; "View actions" link. |
| Low confidence proposal | Review Screen | Card gains the Low Confidence badge and left border, driven by the API's `isLowConfidence` flag; the client never hardcodes the threshold. Nothing is blocked. |
| Unmatched suggested owner | Review Screen | Owner value reads "Unassigned" with the hint "AI suggested: {text}". Approve is allowed and produces an Unassigned Tracked Action. |
| No actions match filters | Action List | "No actions match these filters." with "Clear filters". |
| Overdue | Action List, Action Detail | Overdue indicator beside the due date. Never shown for Complete or Cancelled. |
| Terminal status | Action Detail | Status control and edit fields disabled with captions. Audit Trail still fully visible. |
| Session expired or 401 | Global | Redirect to Login with a snackbar "Session expired. Sign in again." The attempted route is restored after sign-in. |
| 403 | Global | Snackbar "Your role does not allow this." The control that triggered it stays disabled until reload. |
| 409 conflict | Any write | Snackbar "Already changed. Reloading." and the record refreshes. No Retry, because the request can never succeed. |
| Save failure or network error | Any write | Snackbar with the problem detail title and a "Retry" action. Form values are retained. |
| Offline | Global | Not handled specially. The API is local in the demo. |

## Interaction Primitives

Mouse-first with full keyboard operability. No custom shortcuts. `[ASSUMPTION: a three-day build does not budget for a shortcut layer; Material's native keyboard support is the floor.]`

- Row click opens detail. Rows are also focusable and open on Enter.
- Writes apply on button click, never on blur, except the reversible status transitions on Action Detail, which apply on selection with an Undo snackbar.
- Irreversible writes confirm in a dialog: Save notes, Reject, Complete, Cancelled. Approve does not confirm. The Audit Trail is the safety net.
- Buttons that start a write disable until the response returns, so a double click cannot produce a 409 on the user's own action.
- Filters apply immediately and live in the URL.
- Escape closes the topmost dialog. Tab order follows reading order. Focus returns to the triggering control after a dialog closes.
- Banned: infinite scroll, drag and drop, hover-only affordances (including tooltips on disabled controls), auto-save, toasts that require dismissal, modal stacks deeper than one.

## Accessibility Floor

Behavioral. Visual contrast lives in `DESIGN.md`. Target from PRD NFR-6: Review Screen and Action List keyboard operable, every control named, low-confidence flag not color alone, manual keyboard pass and browser axe scan recorded in the PR checklist.

- Every provenance, status, Overdue, and Low Confidence indicator carries icon plus text. Color is never the only signal.
- Every icon button has an `aria-label`. Every form field has a visible label.
- The Pending counter and the human provenance chips are in an `aria-live="polite"` region so a screen reader hears "2 proposals pending" after a decision.
- Highlighting a Source Excerpt in the notes pane also sets `aria-describedby` on the card to the excerpt element.
- Tables use `mat-table` native semantics with sortable headers announced.
- Focus rings are Material defaults, visible on `surface` at AA.
- Dialogs trap focus and return it.

## Responsive & Platform

Desktop web. Supported at 1024px and wider. Layout is checked at 1280px.

| Width | Behavior |
|---|---|
| 1200px and wider | Review Screen is two-pane, notes left, proposals right, independent scroll. |
| 1024 to 1199px | Notes pane becomes a `mat-expansion-panel` above the proposal cards, collapsed by default, expanded when a Source Excerpt is clicked. |
| Below 1024px | Not supported in v1. Mobile layout polish is a PRD non-goal. The app renders single column without further design. |

## Key Flows

Protagonists and journey names mirror the PRD. UJ-1 is a live run on fresh notes. UJ-2 and UJ-4 open seeded Meetings from the PRD addendum storyline.

### UJ-1. Dana turns Tuesday's staff meeting into tracked work

Dana Whitfield, Action Officer, already signed in, has notes from a new office move follow-up meeting.

1. Meeting List. Dana clicks "New meeting". In the dialog she enters Title, Date, and Attendees, and clicks "Create". Meeting Detail opens.
2. Meeting Detail. Dana pastes the notes into the paste area, sees the character count and the caption "Notes cannot be changed after saving", clicks "Save notes", and confirms. The area becomes read-only with the immutable caption.
3. Dana clicks "Run extraction". The progress bar appears with the caption "Extracting with LocalOpenAI · {model}. This can take up to a minute with a local model." The page is not blocked.
4. The run succeeds and the app navigates to the Review Screen. The header reads "6 proposals pending". Notes fill the left pane; six proposal cards fill the right, each with a "Proposed by AI" chip. Hovering the first card highlights its Source Excerpt on the left.
5. Dana clicks "Approve" on four cards. Each keeps its place and collapses to "Decided by Dana Whitfield · {timestamp}" with an Approved chip and a "View action" link. The counter counts down.
6. On the fifth card the owner reads "Unassigned" with the hint "AI suggested: Priya R." and the due date is wrong. Dana clicks "Edit", picks Priya Ramaswamy, corrects the date, and the primary button now reads "Approve with edits". She clicks it. The card shows a "Proposed" column with the AI's values beside her values and an Edited chip.
7. **Climax.** On the sixth card she clicks "Reject", types "discussion item, not an action", confirms. The card shows a Rejected chip and "Reason: discussion item, not an action". The header reads "All proposals decided" with a "View actions" link. Dana clicks it and sees five Tracked Actions filtered to this Meeting, each with an Owner or "Unassigned" and status Open. Nothing the AI said became a record without her hand on it.

Failure: the AI returns invalid output twice. Step 4 does not happen. Meeting Detail shows the run as Failed with "Extraction failed. The AI returned invalid output twice." and a "Run again" button. Dana clicks it.

### UJ-2. Marcus finds what is slipping

Marcus Bell, Lead, Friday afternoon, looking at the seeded office move Meeting's actions.

1. Login. Marcus signs in and lands on Meeting List. He clicks "Actions" in the toolbar.
2. Action List. He sets the Status filter to Open and In Progress. The URL updates. The table is already sorted by due date ascending.
3. Rows carry the Overdue indicator. He toggles "Overdue only" to see just those.
4. He clicks the first row. Action Detail shows Owner Dana Whitfield, status Open, due 2026-09-12. He sets the status to In Progress and the snackbar offers Undo. Because he is a Lead, his Status control lists Cancelled enabled. Dana's would show it disabled with "Lead only".
5. **Climax.** He scrolls to the Audit Trail. The first entry, purple, reads "Proposed by AI · confidence 0.82" with the Source Excerpt and a suggested due date of 2026-09-26. The second entry, blue, reads "Decided by Dana Whitfield" with "Due date: Was 2026-09-26 · Now 2026-09-12". The slip is Dana's decision, not the model's. He knows who to ask.

Failure: filters return nothing. "No actions match these filters." with "Clear filters".

### UJ-3. The office tracker receives an approved action

The Integrator is a system, not a person. This flow has no Angular surface. The Angular contribution is only step 2.

1. Dana approves a proposal on the Review Screen.
2. The decided card shows "Decided by Dana Whitfield". Nothing about the webhook is shown in the UI. `[ASSUMPTION: delivery state is visible through the API and the demo receiver page, not in the Angular app, to keep P0 scope.]`
3. The demo receiver page, outside this spine, shows the signed event arriving.

Failure: the receiver is down. Retries and the Dead state are visible only through the API and the receiver page. Nothing changes in the Angular app.

### UJ-4. Dana proves the AI got it wrong and the human got it right

During the demo, Dana opens the Audit Trail for the seeded edited action in the equipment inventory Meeting (Meeting 3 in the PRD addendum storyline). The PRD names the edited action from UJ-1. The live-run action would show the same shape.

1. Action List, Meeting filter set to the equipment inventory Meeting. Dana clicks the edited action.
2. Action Detail header reads "Created from AI proposal, run extract-actions.v1" with a link to Run Detail.
3. **Climax.** The Audit Trail shows, oldest first: the purple AI Proposal entry with the proposed description, "Suggested owner: P. Ram", suggested due date 2026-10-10, confidence 0.55, the Low Confidence badge, and the Source Excerpt; then the blue "Decided by Dana Whitfield" entry marked Edited; then two field entries, "Owner: Was P. Ram · Now Priya Ramaswamy" and "Due date: Was 2026-10-10 · Now 2026-10-03", each with Dana's name and the same timestamp. The panel sees the proposal, the decision, and the person, in that order.

Failure: none in this flow. If the action had been approved as-is, the trail would show only the AI Proposal and the Decided entry.

## Open Questions

None blocking. Two `[ASSUMPTION]` tags in this spine (no shortcut layer; webhook state not in the Angular app) and five in `DESIGN.md` (prebuilt azure-blue theme, purple/blue provenance hues, two-pane review, no sidenav, density -1 on tables) are defaults. The architect and story author may keep or reverse them without changing any FR.

Needs from the API, passed to the architect and recorded in the PRD: a read-only endpoint exposing the active AI Provider and model (FR-7); an `isLowConfidence` flag on Proposed Actions (FR-14); an `isOverdue` flag on Tracked Actions (FR-19); a Meeting filter and an "Unassigned" owner filter on the Tracked Action list (FR-18, FR-26).
