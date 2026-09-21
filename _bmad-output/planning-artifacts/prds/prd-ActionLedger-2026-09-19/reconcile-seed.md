# Reconciliation: PRD and Addendum against the Seed Prompt

Input: `docs/bmad-seed-prompt.md` (locked seed). Targets: `prd.md` and `addendum.md` in this folder. The brief addendum at `briefs/brief-ActionLedger-2026-09-19/addendum.md` was checked only to confirm where the PRD legitimately delegates.

Severity key: **gap** = seed item absent from PRD and addendum; **contradiction** = PRD conflicts with seed; **weakened** = present but softer or narrower than the seed; **addition** = PRD adds something the seed did not ask for or forbids.

## Findings

### Section 4 P0 and P1

- **gap** (medium) — Seed §4 P0 #10 "Seed data so every screen is populated on first run." PRD FR-34, FR-35 seed Users, Meetings, Runs, Proposals, Tracked Actions, and one Subscription, but no seeded Outbox Messages or delivery attempts, so the webhook receiver page and the outbox API are empty until the first live approval. Fix: add to FR-35 one seeded Outbox Message in Delivered state (and optionally one Dead) so Run Detail, outbox API, and receiver page are populated on first run, or explicitly state the receiver is empty by design.
- **weakened** (low) — Seed §4 "Out of scope (describe as roadmap only)". PRD §5 lists them as Non-Goals with no roadmap framing. Fix: retitle §5 or add one line "Roadmap candidates, not v1" so the panel sees them as deferred, not rejected.
- **addition** (low) — Seed P0 has no requirement to edit a Tracked Action's description, Owner, or due date after creation. PRD FR-20 adds it. It is defensible (seed §4 P0 #6 audit trail shows "each human change") but it is a new write path, UI, and revision kind on a three-day budget. Fix: keep FR-20 but tag it as the first P0 item to cut if Monday slips, or fold it into FR-17's screen.
- **addition** (low) — Seed §8 lists `WebhookSubscription` as seeded data. PRD FR-29 adds Lead-only create and edit via API (tagged ASSUMPTION). Fix: keep as read-only seeded in P0; move create/edit to P1 or the roadmap.
- **addition** (low) — PRD NFR-6 adds an automated axe scan target on two screens. The seed lists no accessibility gate. Reasonable, but it is a new quality gate to wire. Fix: keep as a manual check in the PR checklist unless the Playwright smoke test can host it for free.

### Section 5 Stack and Section 6 Architecture

- **none** — No re-decision of the stack found in PRD or addendum. Mentions of ASP.NET Core bearer auth, `PasswordHasher`, `FOR UPDATE SKIP LOCKED`, Testcontainers, NetArchTest, and Playwright are all consistent with the locked stack.
- **weakened** (medium) — Seed §6 "Approval state transitions (Proposed to Approved, Edited, Rejected) are domain logic on the entity, not controller logic." PRD FR-17 states the entity rule only for Tracked Action status. FR-11 to FR-13 give the 409 behaviour but never say the Review State transition is enforced on the Proposed Action entity. Fix: add to FR-11 (or a shared consequence in §4.3) "Review State transitions are enforced in the Proposed Action entity; a controller cannot bypass them." Also add it to NFR-7 or NFR-8 as a domain test.
- **gap** (low) — Seed §5 "Migrations ship as an EF migration bundle in the pipeline." PRD never mentions the migration bundle. FR-36 says compose "applies migrations" without saying how. Covered in the brief addendum, but the PRD's own delegation sentence in §0 does not name it. Fix: add "via the EF migration bundle" to FR-36 and to the pipeline NFR suggested below.
- **gap** (low) — Seed §7 `/docs/frontend-architecture.md` deliverable and the MVVM mapping. Absent from PRD and PRD addendum. Present in the brief addendum. Fix: list it in PRD §6.1 In Scope as a required deliverable at tag time, since the seed says it will be asked for.

### Section 9 AI requirements

- **gap** (medium) — Seed §9 "Prompts live as versioned files in the repo (`/prompts/extract-actions.v1.md`)." The PRD records the Prompt Version on runs (FR-6) and filters on `/prompts` (FR-39) but never states the requirement that prompt files live at that path with that naming. Fix: add a consequence to FR-6 or a new FR "Prompt files live under `/prompts/` named `extract-actions.v<N>.md`; the Prompt Version on a run is that file's version."
- **addition / internal tension** (medium) — Seed §9 "Invalid output is rejected and retried once, then surfaced as a failed run. Never silently accepted." PRD FR-5 adds a rule that drops a single Proposed Action whose `sourceExcerpt` is not a substring of the notes and records a warning, then says "No path stores partially validated output." Dropping individual items is partial acceptance of a schema-valid response. It is not silent, but it also changes eval recall for the same output. Fix: either (a) reword "No path stores partially validated output" to "No path stores schema-invalid output; excerpt verification is a post-validation filter recorded as a warning", or (b) treat an unverifiable excerpt as a validation failure that triggers the single retry. Pick one and make the scorer aware of it.
- **weakened** (low) — Seed §9 "Log provider, model, latency, and token counts per run." PRD NFR-4 log line lists correlation id, provider, duration, outcome; model and token counts are stored (FR-6) but not required in the log line. Fix: add model name and input/output token counts to the NFR-4 structured log line.
- **ok** — Schema validation, single retry, failed run, untrusted notes with a golden injection case, 12 to 15 cases, precision/recall/owner/date scoring, path-filtered eval job with manual dispatch and repository secret, fake provider for tests/smoke/fallback, run detail screen: all present (FR-5, FR-8, FR-9, FR-37 to FR-39, NFR-8, FR-36).

### Section 10 ALM and CI/CD

- **gap** (medium) — Seed §10 defines `ci.yml`, `cd.yml` (registry push, migration bundle, Azure Container Apps, tagged release = versioned deploy), GitHub Project board, every story becomes an issue, trunk-based flow, conventional commits, PR template, branch protection. The PRD has no requirement for any of these. Only SM-4 (PRs link to issues, green checks), NFR-5 (CodeQL, Dependabot, secret scanning), §6.1 (pipeline first, `v1.0.0` tag) and Open Question 2 (ACA) touch it. PRD §0 delegates "technology choices, layering rules, and the ADR list" to the brief, not ALM. The brief addendum does carry the seed's ALM text, so this is a delegation gap, not a loss. Fix: add NFR-11 "Delivery pipeline" with testable consequences (ci.yml stages, eval.yml path filter, cd.yml on merge and on tag, migration bundle step, branch protection on `main`, PR template with dependency review checkbox, Project board with one issue per story), or add one sentence to §0 delegating ALM to the brief addendum by name.
- **gap** (low) — Seed §10 "A tagged release triggers a versioned deploy." Not in PRD. Fix: include in the NFR-11 above and in §6.1 next to the `v1.0.0` tag.
- **ok** — CodeQL, Dependabot, secret scanning, `.env.example` only (NFR-5, FR-36); `v1.0.0` tag and release notes (§6.1); every story merges through a PR with green checks (§4B).

### Section 11 Demo readiness

- **gap** (low) — Seed §11 "A `DEMO.md` script with the click path and the fallback plan." PRD FR-34 references `DEMO.md` for passwords and the addendum supplies the click path, but no FR requires `DEMO.md` to exist with both the click path and the fake-provider fallback. Fix: add FR-36 consequence or a new FR "`DEMO.md` at repo root holds the seeded credentials, the numbered click path, and the fake-provider fallback steps."
- **ok** — Five-minute cold start (FR-36, SM-2), three seeded meetings matching the seed's three states (FR-35, addendum storyline), webhook receiver in compose with a seeded subscription (FR-33).

### Section 12 Sample data rule

- **weakened** (low) — Seed §12 "an invented generic organization doing mundane work ... nothing resembling actual government or employer work." Addendum names the org "Pinecrest Field Office, a fictional regional office of a fictional agency." "Field Office" and "agency" are government-shaped labels. The work (office move, training, inventory) is mundane and fine. Fix: rename to a generic organization ("Pinecrest Regional Office of the fictional Northwind Cooperative") or drop the word "agency" so nothing in the seed data resembles a government structure.
- **ok** — Fictional-and-unclassified rule is restated in FR-35, FR-37, and §4B Privacy. Roster names are invented.

### Section 14 open questions

- **ok** — All four are resolved in the PRD addendum with rationale and rejected alternatives: thresholds (FR-39, addendum §1), matching rule (FR-38, addendum §2), auth (FR-23 to FR-25, addendum §3), outbox worker placement (addendum §4, recommendation to the architect). The addendum correctly leaves the placement as a recommendation for Winston rather than a PRD decision.

### Section 1 constraints and Section 13 outputs

- **weakened** (low) — Seed §1 "Every BMAD artifact ... lives in the repo under `/docs`." PRD and addendum live under `_bmad-output/` with a note to copy into `docs/` before freeze. Fix: make the copy a Monday story or a `v1.0.0` tag checklist item so it cannot be forgotten, and state the final path in PRD §0.
- **ok** — Code freeze date, demo date, three-day scope, "cut features never quality gates", clean-room rule, P0/P1 split, acceptance criteria per feature, evaluation thresholds, story sequence by reference to the brief addendum.

### Other observations (not seed violations)

- **weakened** (low) — Seed §3 gives Action Officer and Lead distinct jobs. PRD FR-24 grants both roles identical write permissions, so the only 403 path is Lead-only subscription management (FR-29), which is itself a P0 addition flagged above. If FR-29 create/edit is cut, no endpoint distinguishes the roles and the JWT `role` claim has no observable effect. Fix: give Lead one distinct P0 capability the demo can show (for example, only a Lead may set Cancelled), or keep FR-29's Lead-only management explicitly for that reason.
- **ok** — Owner as free text resolved on approval (FR-15) matches seed §8 ADR 6. ExtractionRun fields (FR-6) match seed §8. ActionRevision fields (FR-21) match seed §8.

## Summary counts

| Severity | Count |
|---|---|
| gap | 7 |
| contradiction | 0 |
| weakened | 7 |
| addition | 4 (one with internal tension) |
