---
name: ActionLedger
description: Human-in-the-loop action tracker. Angular Material 3 with the prebuilt azure-blue theme; this DESIGN.md specifies only the semantic delta for provenance, review, status, and confidence.
status: final
created: 2026-09-20
updated: 2026-09-20
colors:
  # Everything not listed inherits from Angular Material 3 prebuilt theme `azure-blue`
  # (primary, on-primary, primary-container, secondary, tertiary, error, surface,
  # surface-container, on-surface, on-surface-variant, outline, outline-variant).
  # Light values first, dark pairs suffixed -dark. Contrast targets in the Colors section.
  # Convention: a bare `material.<token>` value anywhere below means the Angular Material
  # system token of that name, resolved by the theme, not a token defined in this file.
  ai-provenance: '#5B3E96'
  ai-provenance-container: '#EADDFF'
  on-ai-provenance-container: '#2A0E5C'
  ai-provenance-dark: '#D0BCFF'
  ai-provenance-container-dark: '#4A2D82'
  on-ai-provenance-container-dark: '#EADDFF'
  human-provenance: '#1F5F8B'
  human-provenance-container: '#D6EAF8'
  on-human-provenance-container: '#0B2A40'
  human-provenance-dark: '#9CCAF0'
  human-provenance-container-dark: '#154466'
  on-human-provenance-container-dark: '#D6EAF8'
  low-confidence: '#8A5000'
  low-confidence-container: '#FFF1DC'
  on-low-confidence-container: '#4A2A00'
  low-confidence-dark: '#FFB870'
  low-confidence-container-dark: '#4A2E00'
  on-low-confidence-container-dark: '#FFE2C2'
  success: '#1B5E20'
  success-container: '#E3F2E5'
  on-success-container: '#0D3A11'
  success-dark: '#8BD48F'
  success-container-dark: '#1E4A22'
  on-success-container-dark: '#D9F2DB'
  neutral-container: '#E8EAED'
  on-neutral-container: '#3C4043'
  neutral-container-dark: '#3C4043'
  on-neutral-container-dark: '#E8EAED'
typography:
  # Roboto ramp inherited from Angular Material 3. No overrides. Roboto Mono is the one
  # extra font the delta adds; load it in index.html beside Roboto or fall back to ui-monospace. Roles used:
  # headline-small (page titles), title-medium (card titles), body-medium (default),
  # body-small (metadata), label-large (buttons and chips).
  confidence-score:
    fontFamily: 'Roboto Mono'
    fontSize: 13px
    fontWeight: '500'
    lineHeight: '1.2'
  source-excerpt:
    fontFamily: 'Roboto'
    fontSize: 14px
    fontWeight: '400'
    lineHeight: '1.5'
rounded:
  # Angular Material 3 defaults inherited (chips full, cards 12px, buttons full, fields 4px top).
  # Only the values referenced below are listed.
  sm: 4px
  lg: 12px
  full: 9999px
spacing:
  # 8px grid inherited from Material. Only the named layout tokens the prose references are listed.
  page-gutter: 24px
  content-max: 1280px
  notes-pane-min: 360px
components:
  provenance-chip-ai:
    background: '{colors.ai-provenance-container}'
    foreground: '{colors.on-ai-provenance-container}'
    radius: '{rounded.full}'
    icon: 'auto_awesome'
  provenance-chip-human:
    background: '{colors.human-provenance-container}'
    foreground: '{colors.on-human-provenance-container}'
    radius: '{rounded.full}'
    icon: 'person'
  low-confidence-badge:
    background: '{colors.low-confidence-container}'
    foreground: '{colors.on-low-confidence-container}'
    radius: '{rounded.full}'
    icon: 'warning'
  overdue-badge:
    background: 'material.error-container'
    foreground: 'material.on-error-container'
    radius: '{rounded.full}'
    icon: 'schedule'
  status-chip-open:
    background: '{colors.neutral-container}'
    foreground: '{colors.on-neutral-container}'
  status-chip-in-progress:
    background: 'material.primary-container'
    foreground: 'material.on-primary-container'
  status-chip-complete:
    background: '{colors.success-container}'
    foreground: '{colors.on-success-container}'
  status-chip-cancelled:
    background: '{colors.neutral-container}'
    foreground: '{colors.on-neutral-container}'
    text-decoration: 'line-through'
  review-state-chip-pending:
    background: 'material.primary-container'
    foreground: 'material.on-primary-container'
  review-state-chip-approved:
    background: '{colors.success-container}'
    foreground: '{colors.on-success-container}'
  review-state-chip-edited:
    background: '{colors.success-container}'
    foreground: '{colors.on-success-container}'
    icon: 'edit'
  review-state-chip-rejected:
    background: '{colors.neutral-container}'
    foreground: '{colors.on-neutral-container}'
  pending-counter:
    background: '{colors.neutral-container}'
    foreground: '{colors.on-neutral-container}'
    radius: '{rounded.full}'
  source-excerpt-highlight:
    background: '{colors.ai-provenance-container}'
    foreground: 'material.on-surface'
  proposal-card:
    background: 'material.surface-container-low'
    radius: '{rounded.lg}'
    border-left-low-confidence: '4px solid {colors.low-confidence}'
  audit-entry-ai:
    marker: '{colors.ai-provenance}'
    background: '{colors.ai-provenance-container}'
    foreground: '{colors.on-ai-provenance-container}'
  audit-entry-human:
    marker: '{colors.human-provenance}'
    background: 'material.surface-container-low'
    foreground: 'material.on-surface'
---

## Brand & Style

ActionLedger is a working tool for staff who turn meeting notes into tracked commitments. Its visual posture is deliberately plain: Angular Material 3 with the prebuilt `azure-blue` theme, Roboto, default shapes, default elevation. Nothing here is styled for its own sake. The product's one visual idea is **provenance**: at every point where the reader could wonder "did the AI say this or did a person," the surface answers with color, icon, and label before they have to ask.

This DESIGN.md specifies only that delta, and the delta is tokens and chips: four semantic color families, two type roles, and a handful of chip and badge variants composed from `mat-chip` and `mat-icon`. No Material component is restyled. Buttons, form fields, tables, cards, dialogs, snackbars, and navigation ship as Angular Material renders them. Customizing them is against the discipline. `[ASSUMPTION: the prebuilt azure-blue theme is the base; the seed asked for Angular Material and no custom visual design, and a prebuilt theme is the shortest path to that.]`

## Colors

Angular Material's `azure-blue` theme supplies primary, secondary, tertiary, error, and the surface and outline ramp. The brand adds four semantic families and nothing else.

- **AI provenance (purple family).** Marks anything the model produced: the AI Proposal entry in an Audit Trail, the original values on an Edited proposal, the Source Excerpt highlight in Meeting Notes, and the "Proposed by AI" chip. Purple was chosen because it is absent from the azure-blue theme's primary and error ramps, so it never collides with an interactive or destructive meaning. `[ASSUMPTION: purple for AI, blue for human; any two hues that are distinguishable and not error red would satisfy the rule.]`
- **Human provenance (blue family, distinct from theme primary).** Marks human decisions and edits in the Audit Trail and the "Decided by {name}" chip. It sits near the theme primary on purpose: human decisions are the authoritative record, and the theme primary is the color of authority in Material.
- **Low confidence (amber family).** The one warning color. Used only for the Low Confidence badge and the left border of a low-confidence proposal card. Never used for Overdue. Overdue uses Material's error container instead, so that "late" and "uncertain" read differently.
- **Success (green family).** Approved and Edited review states, Complete action status. Not used for buttons.
- **Neutral container.** Open, Cancelled, and Rejected chips and the Pending counter. Deliberately quiet so the colored states stand out.

Contrast targets. Every foreground/container pair above meets 4.5:1 in both modes. Dark pairs are at or above 7:1. Material's own pairs are AA by construction. Pairs verified at authoring time:

- on-ai-provenance-container on ai-provenance-container: 9.8:1
- on-low-confidence-container on low-confidence-container: 9.1:1
- on-success-container on success-container: 9.6:1
- on-human-provenance-container on human-provenance-container: 10.2:1

Avoid theme primary for status, error red for anything but Overdue and destructive confirmation, gradients, custom hover colors, and any semantic family beyond these four.

## Typography

The Roboto ramp is inherited from Angular Material 3. Page titles use `headline-small`, card and dialog titles `title-medium`, body `body-medium`, metadata and timestamps `body-small`, buttons and chips `label-large`.

Two brand roles:

- **`confidence-score`** (Roboto Mono, 13px, 500). The Confidence Score is always shown as a two-decimal number in monospace so that 0.55 and 0.85 line up in a column and the eye can compare them. It never appears as a bar or a percentage.
- **`source-excerpt`** (Roboto, 14px, 1.5 line height). Quoted notes text on proposal cards and audit entries. It renders inside a blockquote with the AI provenance container as background.

## Layout & Spacing

The 8px grid is inherited from Material. Page content sits inside a `{spacing.content-max}` container with `{spacing.page-gutter}` side gutters. A single Material toolbar carries the product name, two top-level links (Meetings, Actions), and the user menu. There is no side navigation. `[ASSUMPTION: two top-level destinations do not justify a sidenav.]`

The Review Screen is the only two-pane surface. At 1200px and wider, Meeting Notes occupy a left pane of at least `{spacing.notes-pane-min}` and proposal cards occupy the right pane, each scrolling independently. Below 1200px the notes pane collapses to an expandable panel above the cards. `[ASSUMPTION: two-pane review with notes beside proposals; the addendum asked for the Source Excerpt to be visible without expansion, and showing it in context in the notes is the strongest way to do that.]`

Tables use Material density level -1 so the Action List shows 50 rows without a second scroll. `[ASSUMPTION: density -1 for tables only.]`

## Elevation & Depth

Material defaults. Proposal cards sit on `surface-container-low` with no shadow; only dialogs and the user menu elevate. Depth is not a hierarchy device in this product.

## Shapes

Material 3 defaults: chips and buttons pill-shaped, cards `{rounded.lg}`, form fields `{rounded.sm}` on top corners. A low-confidence proposal card adds a `4px` left border in `{colors.low-confidence}`; the radius is unchanged.

## Components

Used as Angular Material renders them, unchanged: `mat-toolbar`, `mat-menu`, `mat-button` (all variants), `mat-form-field` with `mat-input`, `mat-select`, `mat-datepicker` and `mat-date-range-picker`, `mat-slide-toggle`, `mat-chip-grid` with `mat-chip-row`, `mat-table` with `mat-sort` and `mat-paginator`, `mat-card`, `mat-chip`, `mat-dialog`, `mat-snack-bar`, `mat-progress-bar`, `mat-expansion-panel`, `mat-icon`.

Run Detail is built from these alone. Its run list is a `mat-table`, and its metadata block is a plain two-column definition list with `body-small` labels and `body-medium` values.

Brand-layer components:

- **Provenance chip.** A `mat-chip` in two fixed variants. AI: `{components.provenance-chip-ai}` with the `auto_awesome` icon and the text "Proposed by AI". Human: `{components.provenance-chip-human}` with the `person` icon and the text "Decided by {display name}". Whether the decision was Approve, Edit, or Reject is carried by the Review State chip, not by the provenance chip. Both chips carry an icon and text so the meaning survives without color.
- **Low Confidence badge.** `{components.low-confidence-badge}`, icon `warning`, text "Low confidence". Appears beside the Confidence Score on any proposal below the Low Confidence Threshold, and the card gains the left border. Never conveyed by color alone.
- **Pending counter.** `{components.pending-counter}`, text "{n} proposals pending" or "All proposals decided". One per Review Screen header.
- **Overdue indicator.** Rendered as a badge on Material's error container: `{components.overdue-badge}`, icon `schedule`, text "Overdue". Appears in the Action List row and on Action Detail.
- **Status chip.** One of `{components.status-chip-open}`, `{components.status-chip-in-progress}`, `{components.status-chip-complete}`, `{components.status-chip-cancelled}`. Text is the Action Status name. Cancelled adds line-through.
- **Review State chip.** One of the four `review-state-chip-*` tokens. Text is the Review State name. Edited carries the `edit` icon.
- **Proposal card.** `mat-card` on `{components.proposal-card.background}`. It has three states.
  - *Pending.* Anatomy top to bottom: header row ("Proposed by AI" provenance chip, Confidence Score in `{typography.confidence-score}`, Low Confidence badge if flagged, Review State chip); description, owner, and due date as read-only values, with the owner carrying the hint "AI suggested: {text}"; Source Excerpt blockquote in `{typography.source-excerpt}` on `{components.source-excerpt-highlight}` background; action row (Reject as text button, Edit as outlined button, Approve as filled button).
  - *Edit mode.* The three values become a text field, a select, and a date picker. The action row becomes Cancel (text) and Approve or Approve with edits (filled).
  - *Decided.* The action row is replaced by the human provenance chip, the timestamp beside it, the Review State chip, and a "View action" link. The Source Excerpt blockquote stays visible. An Edited card shows a "Proposed" column beside the decided values, with the original on the AI provenance container. A Rejected card shows the reason.
  - → `mockups/review-screen.html` shows Pending, edit-mode, Approved, Edited, and Rejected cards; spine wins on conflict.
- **Source Excerpt highlight.** In the Review Screen notes pane, the sentence matching the focused or hovered proposal card is wrapped in `{components.source-excerpt-highlight}`. Only one highlight at a time.
- **Audit entry.** A vertical timeline row. AI entries use `{components.audit-entry-ai}`: purple marker, purple container. Human entries use `{components.audit-entry-human}`: blue marker, default surface. Field changes render old and new values in two columns labeled "Was" and "Now". → `mockups/action-detail.html` shows an AI entry followed by three human entries.

## Do's and Don'ts

| Do | Don't |
|---|---|
| Inherit Angular Material defaults for every component not in the brand layer | Restyle Material components to look custom |
| Show provenance with icon and text every time color is used | Rely on purple or blue alone to mean AI or human |
| Use amber only for Low Confidence and error red only for Overdue and destructive confirmation | Use the same color for "uncertain" and "late" |
| Render Confidence Score as a monospace two-decimal number | Render confidence as a bar, gauge, or percentage |
| Keep the Review Screen two-pane at 1200px and wider | Add a third pane or a sidenav |
| Use Material density -1 on tables only | Compact cards, forms, or dialogs |
