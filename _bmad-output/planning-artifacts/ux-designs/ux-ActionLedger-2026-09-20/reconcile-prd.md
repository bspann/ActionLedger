# Reconciliation: PRD and addendum vs DESIGN.md and EXPERIENCE.md

Date: 2026-09-20. Inputs: `prd.md` (Glossary, §4.1 to §4.6, NFR-6, UJ-1 to UJ-4) and `addendum.md` ("UX inputs for Sally", "Demo click path", "Seed data storyline"). Targets: `DESIGN.md`, `EXPERIENCE.md`. Severity: gap (PRD requires, spine silent) / contradiction (spine says otherwise) / weakened (spine covers it less strictly) / drift (vocabulary or storyline mismatch) / addition (spine adds something the PRD did not ask for; flagged only where it needs API support or touches a non-goal).

## High priority

- **contradiction** — PRD FR-10 ("Pending proposals show Approve, Edit, and Reject controls") and addendum UX inputs ("the Approve, Edit, and Reject controls"). Spine: `EXPERIENCE.md` Component Patterns, Proposal card, vs `DESIGN.md` Components, Proposal card. EXPERIENCE makes description, owner, and due date always editable and never says what an "Edit" control does; DESIGN specifies an Edit outlined button in the action row. The two spines disagree on whether fields are inline-editable or gated by Edit. Fix: pick one (recommend Edit toggles the card into edit mode, then the primary button becomes "Approve with edits") and state it in both files.

- **contradiction** — PRD FR-13 ("reason ... visible on the Review Screen and Run Detail") and EXPERIENCE Interaction Primitives ("Banned: hover-only affordances"). Spine: `EXPERIENCE.md` Provenance Language, Run Detail row ("Review State chip per proposal with decider name on hover"). Decider name is hover-only and the rejection reason is not shown on Run Detail at all. Fix: Run Detail proposal rows show Review State, decider display name, timestamp, and rejection reason as visible text.

- **gap** — PRD FR-13 rejection reason visible on the Review Screen. Spine: `EXPERIENCE.md` Proposal card ("After any decision the card collapses its controls and shows the decision chip"). A Rejected card never displays the reason entered in the Reject dialog. Fix: a Rejected card shows "Reason: {text}" under the decision chip, or "No reason given".

- **contradiction** — PRD FR-17 (Complete and Cancelled are terminal; transitions out return 409) and EXPERIENCE Interaction Primitives ("Destructive or irreversible decisions (Reject, Cancelled) confirm in a dialog"). Spine: `EXPERIENCE.md` Status control ("Changing status applies immediately with a snackbar ... Undo ... performs the reverse transition when allowed"). Cancelled is said to confirm in a dialog and also to apply immediately; Complete is irreversible yet neither confirms nor can be undone. Fix: Complete and Cancelled open a confirm dialog ("This cannot be reopened"); Undo is offered only for Open to In Progress and In Progress to Open.

- **weakened** — PRD FR-17 ("Only a Lead may transition to Cancelled ... so the demo can show authorization working") and FR-24 (403 test uses the Cancelled transition). Spine: `EXPERIENCE.md` Status control ("Cancelled is listed only for a Lead"). Hiding the option means the Role gate is invisible in the UI and the 403 state pattern can never be triggered from Action Detail. Fix: show Cancelled to Action Officers as a disabled option with the caption "Lead only", so the authorization boundary is demonstrable on screen.

- **contradiction** — Addendum Demo click path step 3 ("Filter to Marcus as Owner, show the Overdue indicator") and Seed data storyline (Meeting 1 has the Overdue action). Spine: `EXPERIENCE.md` UJ-2 step 4 ("Action Detail shows Owner Dana Whitfield, status Open, due 2026-09-12"). The spine's overdue action is owned by Dana, so filtering to Marcus as Owner would not show it. Fix: make the seeded Overdue action's Owner Marcus Bell in UJ-2 (and tell the seed author), or change the demo path filter.

- **contradiction** — PRD FR-18 (filters are Owner, Action Status, due date range) and FR-26 (same filters as API query parameters). Spine: `EXPERIENCE.md` Pending counter ("View actions link to the Action List filtered to this Meeting"), UJ-1 step 7, UJ-4 step 1 ("Action List, filtered to the office move Meeting"), Filter bar (no Meeting filter). The spine depends on a Meeting filter that neither the filter bar nor the PRD API defines. Fix: either add a `meetingId` query filter to the Filter bar and ask the PM to add it to FR-18/FR-26, or replace the link with "Back to meeting" and reach actions from Meeting Detail.

- **gap** — PRD FR-2 (409 on second notes save), FR-11 (409 approving a non-Pending proposal), FR-17 and FR-20 (409 on terminal actions). Spine: `EXPERIENCE.md` State Patterns, "Save failure or network error" ("Snackbar with the problem detail title and a Retry action"). Retry is the wrong affordance for 409; the request can never succeed. Fix: add a 409 state: snackbar "Already changed by someone else. Reloading." and refresh the record, no Retry.

- **weakened** — PRD FR-10 (every proposal shows its suggested owner) and FR-15 ("The Proposed Action keeps the original free-text suggestion"). Spine: `EXPERIENCE.md` Proposal card (suggested text shown as a hint "when unmatched" only). When the name matches, the AI's raw suggested owner text disappears behind the pre-selected User, so the reader cannot see what the AI actually wrote. Fix: always show "AI suggested: {text}" as the owner field hint, matched or not.

## Medium priority

- **gap** — PRD FR-12 ("clearing the picker ... is a change"; Owner may be null). Spine: `EXPERIENCE.md` Proposal card (owner is a `mat-select` of Users). No way to clear a pre-selected owner or clear the due date picker is specified. Fix: owner select includes an "Unassigned" option; due date field has a clear button.

- **weakened** — PRD FR-12 ("An edit that changes nothing is treated as Approve"). Spine: `EXPERIENCE.md` Proposal card ("Editing any field switches the primary button label ... to Approve with edits"). Does not say the label reverts to "Approve" when a value is changed back to the AI's value, so a touched-but-unchanged card would be sent as Edit. Fix: label is "Approve with edits" only while at least one field differs from the proposed value (with the FR-15 pre-selection counting as the proposed owner).

- **gap** — PRD FR-10 ("Proposals are listed in the order the AI returned them") and FR-9. Spine: missing. Neither the Review Screen nor Run Detail states card or row order. Fix: add "cards and rows in AI return order; decided cards stay in place" to the Proposal card and Run Detail patterns.

- **contradiction** — EXPERIENCE Interaction Primitives ("irreversible decisions confirm in a dialog") vs PRD FR-2 (Meeting Notes immutable once saved). Spine: `EXPERIENCE.md` Notes paste area ("Save notes" with no confirmation). Saving notes is irreversible but is the one irreversible write without a confirm or warning. Fix: show the caption "Notes cannot be changed after saving" beside the button, or confirm in a dialog.

- **contradiction** — EXPERIENCE Interaction Primitives ("Banned: hover-only affordances"). Spine: `EXPERIENCE.md` Run extraction button ("Disabled with tooltip 'Add notes first'"). A tooltip on a disabled button is hover-only, and Material does not raise tooltips on disabled buttons. Fix: replace with a visible caption under the button.

- **gap** — Addendum UX inputs, front-end layering rule (templates only render and bind; components expose signals and commands; no HTTP in components; feature folders `meetings`, `review`, `actions`, `audit`, `auth`). Spine: `EXPERIENCE.md` Foundation mentions signals but never restates the rule or maps surfaces to feature folders. Fix: add a "Surface to feature folder" column in the IA table and one sentence on the layering rule with the `docs/frontend-architecture.md` deliverable.

- **drift** — PRD UJ-4 ("the edited action from UJ-1", the office move meeting) vs Addendum Demo click path step 4 ("the edited action from Meeting 3") and Seed data storyline (Meeting 3 is the audit showcase with P. Ram, 0.55). Spine: `EXPERIENCE.md` UJ-4 step 1 says "filtered to the office move Meeting" but the values in step 3 (P. Ram, 0.55, 2026-10-10 to 2026-10-03) are Meeting 3's. Fix: state which Meeting UJ-4 opens (recommend Meeting 3 to match the demo path) and flag the UJ-1/UJ-4 mismatch to the PRD polish agent.

- **drift** — PRD UJ-1 ("edits the fifth to fix the owner and due date") vs Seed data storyline (P. Ram and the wrong date belong to Meeting 3). Spine: `EXPERIENCE.md` UJ-1 step 6 reuses "AI suggested: P. Ram" inside the office move meeting. Harmless for a live run, but it makes UJ-1 and the Meeting 3 seed read as the same action. Fix: use a different unmatched name in UJ-1 or note that the live run and the seed differ.

- **drift** — Glossary "Overdue indicator". Spine: `DESIGN.md` Components and `EXPERIENCE.md` throughout use "Overdue badge". Fix: keep "Overdue indicator" as the term and say once that it is rendered as a badge.

- **drift** — DESIGN vs EXPERIENCE on the human provenance chip. `DESIGN.md` Provenance chip: "Decided by {name}" or "Edited by {name}". `EXPERIENCE.md` Provenance Language and UJ-4: always "Decided by {name}", with Edited shown by the Review State chip. Fix: drop "Edited by" from DESIGN; the Review Decision name lives on the Review State chip.

- **gap** — PRD FR-21 (AI Proposal revision carries description, suggested owner, suggested due date, Confidence Score, Source Excerpt) and FR-22. Spine: `EXPERIENCE.md` Audit Trail timeline ("The AI Proposal entry shows Confidence Score, Source Excerpt, and suggested owner text"). Suggested due date and proposed description are omitted from the entry anatomy, though UJ-2 relies on the suggested due date. Fix: list all five proposed fields in the AI Proposal entry.

- **weakened** — PRD FR-20 caption for terminal actions. Spine: `EXPERIENCE.md` Edit fields ("Completed actions cannot be edited"). Wrong for Cancelled. Fix: "{Status} actions cannot be edited".

- **addition** — Spine: `EXPERIENCE.md` Extraction in progress ("Extracting with {AI Provider} · {model}") and UJ-1 step 3. The UI needs the active provider and model before the run returns, and no PRD endpoint exposes configuration. Fix: either request a read-only `GET /api/v1/config/provider` from the architect or make the caption "Extracting..." and show provider and model on the run row afterwards.

- **addition** — Spine: `EXPERIENCE.md` Filter bar (Owner select includes "Unassigned"). Filtering on a null Owner needs an explicit API convention (for example `ownerId=none`) that FR-26 does not state. Fix: keep it and note the API need for the architect.

## Low priority

- **gap** — PRD FR-1 (title 1 to 200 characters, date required, attendees each 1 to 100). Spine: `EXPERIENCE.md` UJ-1 step 1 (dialog with Title, Date, Attendees). No validation messages or limits. Fix: add "Title required, max 200" and per-chip max 100 to the New meeting dialog pattern.

- **gap** — PRD FR-2 and FR-3 (saved text is byte-for-byte; "shows the notes as pasted"). Spine: `EXPERIENCE.md` Notes paste area ("becomes read-only text"). Whitespace preservation is not stated. Fix: render saved notes and the Review Screen notes pane with `white-space: pre-wrap`.

- **weakened** — PRD FR-3 (sort by Meeting date descending then creation time descending). Spine: `EXPERIENCE.md` Meeting table ("Sort by Date descending default"). Secondary sort omitted; "No paging below 50 Meetings" leaves the above-50 case undefined. Fix: add the tie-break and "paginator appears at 50".

- **gap** — PRD FR-14 (threshold configurable). Spine: `EXPERIENCE.md` Low confidence proposal state does not say where the flag comes from. Fix: the API returns the threshold or an `isLowConfidence` flag; the client never hardcodes 0.70.

- **gap** — PRD FR-19 ("Today" evaluated in UTC on the server clock). Spine: `EXPERIENCE.md` Overdue state. Fix: the Overdue indicator renders from the API's `isOverdue` flag, never from a client-side date compare.

- **gap** — PRD Audit Trail as evidence; FR-19 uses UTC. Spine: `EXPERIENCE.md` Voice and Tone ("Timestamps as 2026-09-21 14:03"). Time zone of displayed timestamps is not stated. Fix: display in UTC with a "UTC" suffix, or state local time with the zone.

- **drift** — PRD FR-9 (Failed run shows "the failure reason") and EXPERIENCE State Patterns ("failure reason verbatim"). Spine: `EXPERIENCE.md` Voice table and UJ-1 failure give a fixed string "Extraction failed. The AI returned invalid output twice." Fix: mark the Voice string as an example of a server-supplied reason; timeouts (NFR-1) and unreachable providers have other reasons.

- **drift** — Spine: `EXPERIENCE.md` IA table (Action Detail reached from "Review Screen 'View action' after approval") vs Proposal card (decided card shows only the decision chip). No per-card "View action" link is specified. Fix: add a "View action" link on Approved and Edited cards, or remove it from the IA table.

- **drift** — Glossary and FR-23 use "log in" and the screen is "Login". Spine: `EXPERIENCE.md` Login form ("Sign in", "Sign-in failed"), Toolbar ("Sign out"). Fix: acceptable, but say once that the Login screen's button reads "Sign in".

- **drift** — Addendum UX inputs ("Angular Material components, no custom visual design"). Spine: `DESIGN.md` adds four semantic color families, a Roboto Mono role (an extra font to load), custom chip and badge components, and density -1. Framed as a semantic delta, which is defensible, but it is custom theming. Fix: keep, and state in DESIGN that the delta is tokens and chips only, no restyled Material components, so the constraint is visibly honored.

- **drift** — Addendum Demo click path step 2 (approve two, edit one, reject one of five) vs PRD SM-1 ("no Proposed Action left Pending"). Spine: `EXPERIENCE.md` "All proposals decided" state is never reached in the scripted demo. Not a spine defect; pass to the addendum polish agent to make step 2 decide all five.

- **gap** — PRD FR-6 (token counts are zero for the Fake Provider, not null). Spine: `EXPERIENCE.md` Run metadata block. Fix: render "0", never blank, for token counts.

- **gap** — PRD NFR-1 (a run may take up to 180 seconds before Failed). Spine: `EXPERIENCE.md` Extraction in progress (indeterminate progress bar). No expectation setting for a long synchronous wait. Fix: caption "This can take up to a minute with a local model".

- **drift** — DESIGN Proposal card header (Confidence Score, Low Confidence badge, Review State chip) vs EXPERIENCE Provenance Language (Pending card "Header reads 'Proposed by AI'"). Fix: DESIGN header row adds the "Proposed by AI" chip so both files agree.

## Confirmed covered (no finding)

Seven screens present and routed; FR-14 flag is icon plus text plus border; NFR-6 items all present in Accessibility Floor; FR-15 pre-selection on case-insensitive match; FR-18 columns, default sort, paging, Overdue-only filter; FR-22 oldest-first timeline with Was/Now; FR-23 no-hint 401 message and in-memory token; Review Decision verbs used as button labels; no spine addition contradicts a PRD non-goal (no notifications, no mobile work, no prompt editing, no AI writes to Tracked Actions).
