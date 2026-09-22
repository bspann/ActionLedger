# PR checklist: Story 3.5, Make decisions on the Review Screen

This file mirrors the checklist in `.github/pull_request_template.md`.

## Checklist

- [ ] **Issue link**: add it when the pull request is opened.
- [x] **Tests added**: bUnit coverage is in `tests/Web.Tests`.
  - `ReviewServiceTests` covers approve, reject, the wire date and the failure mapping.
  - `ProposalCardTests` covers edit mode, the label diff, Cancel, the blank-description disable, `WritesDisabled` and focus return.
  - `ReviewPageTests` covers every row of the I/O matrix.
  - `LayoutTests` covers the main landmark.
  - `VoiceAndFormatsTests` pins the new copy.
  - `dotnet test ActionLedger.sln` passes: 1690 tests, 0 failed.
- [x] **License review**: no dependency was added or bumped.
- [x] **axe pass**: axe reported no violations on the Review Screen in any of the states listed below. Details follow.

## axe scan

- **Date:** 2026-09-22
- **Browser:** HeadlessChrome/145.0.7632.6 (Playwright chromium headless shell), viewport 1280x900
- **axe-core:** 4.10.2, loaded from cdnjs and run as `axe.run(document)` against the review route
- **Stack:** compose, `Ai__Provider=Fake`, signed in as `dana` (Action Officer). Meetings, notes and runs were created through the API from the fixtures `badge-printer-replacement`, `break-room-refresh` and `equipment-inventory-kickoff`.

| State | Violations |
|---|---|
| At rest (two Pending cards) | 0 |
| Edit mode (one card editing) | 0 |
| After an approve with edits (Edited + Pending) | 0 |
| Reject dialog open | 0 |
| After decisions (Rejected, low-confidence card; "All proposals decided") | 0 |

The first scan at rest found three violations. All of them were fixed in this change:

- `landmark-one-main` and `region`: the shell had no `<main>`. `MainLayout` now wraps the body in `<main>`.
- `link-in-text-block`: the meeting link in the meta line relied on color alone, at a 1.67:1 contrast with the text around it. It is now underlined.

## Keyboard-only pass

This was done with the same browser, stack and date as the axe scan, using only Tab, Shift+Tab, Enter, the arrow keys, Escape and typed text. The focused element was read after each key.

- **Reachable:** focus reaches each of these with Tab, in order:
  - the meeting link and Back to meeting;
  - each card (`article`, named by its description);
  - Reject, Edit and Approve;
  - on decided cards, View action;
  - on the counter, View actions.
- **Edit:** Enter on Edit swaps in the fields and moves focus to the Description field (label "Description").
  - Tab reaches Owner (label "Owner"). Enter opens the listbox, which lists Unassigned first and then the roster. ArrowUp and Enter chose an owner, and the label changed to "Approve with edits".
  - Tab reaches Due date (label "Due date"). Typing `2026-10-01` set the date, and select-all plus Backspace cleared it. The picker's clear (x) icon is not in the tab order. Clearing works by editing the typed text.
  - Tab reaches Cancel and then the primary button.
  - Choosing the original owner again turned the label back to "Approve".
  - Enter on Cancel restored the proposed values and moved focus to the card. The next Edit started from the proposed values.
  - Enter on "Approve with edits" produced an Edited card with a Proposed column.
    - The Pending counter read "1 proposal pending", inside its `aria-live="polite"` region.
    - Focus moved to the decided card.
- **Reject:** Enter on Reject opens "Reject this proposal?" with focus in "Reason (optional)".
  - Escape dismisses it. No POST was sent, the buttons were enabled again, and focus returned to the card.
  - The reason was typed, then Tab reached Cancel and Tab reached Reject. Enter on Reject produced a Rejected card reading "Reason: not an action", with the reason sent trimmed. The counter read "All proposals decided", followed by View actions, and focus returned to the card.
- **Names:** every control has a visible label or text: the buttons, the three edit fields, the dialog's field and its two buttons, and the links.
- **Focus indicator:**
  - Cards show a 2px solid outline.
  - Buttons and links now show a 2px outline on `:focus-visible`. Before this change, MudBlazor's focused and unfocused buttons had identical computed styles. The fix is in `app.css`; `tokens.css` was not edited.
  - The outlined text fields show MudBlazor's focused border.
- **Color alone:**
  - Low confidence shows an icon, the text "Low confidence" and a left border.
  - Review states are chips with text.
  - The meta link is underlined.

## Not verified

- **Compose as committed:** the api container does not start with the repository's own `src/ActionLedger.Api/Dockerfile`. The error is `Ai:PromptVersion 'v1' has no embedded prompt file. Embedded prompt versions: []`. That Dockerfile copies `src/` but not `prompts/` or `fixtures/`, which `ActionLedger.Infrastructure.csproj` embeds from `../../`. These checks ran with a compose override in which the api build copies both folders as well. No repository file was changed for this, and the web image was built from this branch unchanged. This defect predates Story 3.5 and needs its own fix.
- A 403 and a 409 were not produced against the live stack. bUnit covers both paths.
- A screen reader was not used. Live-region announcements were checked by attribute, not by listening to them.
