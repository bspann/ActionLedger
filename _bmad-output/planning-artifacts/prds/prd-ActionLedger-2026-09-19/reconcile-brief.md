---
title: "Reconciliation: Product Brief vs PRD"
input: brief.md and addendum.md (briefs/brief-ActionLedger-2026-09-19)
targets: prd.md and addendum.md (prds/prd-ActionLedger-2026-09-19)
created: 2026-09-19
---

# Reconciliation: Product Brief vs PRD

Severity key: **gap** = brief idea absent from PRD. **contradiction** = PRD says something different. **weakened** = present but diluted, demoted, or partial. **drift** = same concept, different word.

Ordered roughly by importance within each group.

## Framing and positioning

- **gap** — "No technical moat" honesty. Brief §What Makes This Different: "There is no technical moat here, and the brief does not claim one. The advantage is a coherent, explainable design that a reviewer can walk through end to end." PRD: missing. Fix: add one sentence to PRD §1 Vision stating the differentiator is the trust model and the explainable design, not the extraction, and that no moat is claimed.
- **weakened** — "The differentiator is the trust model, not the extraction." Brief §What Makes This Different, first sentence. PRD §1 states the thesis (AI output is never authoritative) but never names it as the differentiator or contrasts it with extraction quality. Fix: open the third paragraph of §1 with that sentence verbatim.
- **gap** — Long-term vision. Brief §Vision: capture layer in front of any tracker; notes from meetings, chat, or transcription; audit trail travels with the action into Planner or Jira via the same webhook contract; prompt versions promoted like code; model replaceable without downstream noticing. PRD §1 covers only the v1 thesis. Fix: add a short "Beyond v1" paragraph to PRD §1, and note that FR-30's payload (proposal plus decision in the webhook) is what lets the audit trail travel.
- **gap** — Competitive framing. Brief §The Problem: "Existing tools either skip the capture step or trust the model's output as-is." PRD: missing. Fix: one sentence in §1 or §2.1 naming the two failure modes the product sits between.
- **gap** — Stated purpose for the panel. Brief §Executive Summary: purpose is to show "a full delivery methodology, a defensible data model, working ALM and CI/CD, and a credible pattern for putting AI into a workflow a government customer could trust." PRD §0 says the panel reads it "as evidence of method" only. Fix: list the four things the panel must be able to walk in §0 or §2.
- **gap** — Build constraint. Brief §Scope hard constraints: "Scope must fit three build days for one developer working with Claude Code." PRD §4B Time has the freeze date and the cut-features rule but not the one-developer-with-Claude-Code constraint. Fix: add the sentence to §4B Time.

## Users

- **gap** — Interview panel as a secondary user. Brief §Who This Serves lists "Interview panel (secondary)" with success = can walk methodology, data model, ALM, CI/CD, AI usage, with artifacts living in the repo. PRD §2 has no panel entry and no journey; only §0 and the addendum artifact-location note touch it. Fix: add a UJ-5 "The panel walks the repo" journey or a §2.4 secondary-audience entry with the artifacts-in-repo success condition.
- **weakened** — Action Officer success criterion. Brief §Who This Serves: "no action leaves the review screen without a human decision attached to it." PRD FR-10 shows a pending count and SM-C3 discourages a high approve rate, but nothing measures decision completeness. Fix: add a success metric (for example, zero Pending proposals on seeded Meeting 1 and on the demo run) or a consequence on FR-10 that the screen cannot be marked done with Pending proposals.
- **weakened** — "clear view of what the AI got wrong." Brief §Who This Serves, Action Officer. PRD FR-12 keeps original values visible and UJ-4 demonstrates it, but the Review Screen (FR-10) has no requirement to show original versus edited side by side. Fix: add to FR-10 that a non-pending Edited proposal shows original and edited values together.
- **drift** — Lead's view scope. Brief step 5 says "Leads see a filterable list"; PRD FR-18 says "Any User." Superset, not a contradiction. Fix: none, or note in FR-18 that the Lead is the primary audience.

## Success criteria vs success metrics

- **weakened** — "Every story merges through a pull request with green checks" is a primary success criterion in brief §Success Criteria. PRD demotes it to secondary SM-4 and a sentence in §4B Time. Fix: promote SM-4 to primary, since the commit history is the ALM evidence the panel will read.
- **weakened** — Evaluation gate criterion. Brief: the gate "runs in CI with published thresholds and fails the build when extraction quality drops below them." PRD SM-3 measures "passing on main at tag time." Passing and failing-on-regression are different claims. Fix: reword SM-3 to "gate runs in CI, thresholds are published in job output, and at least one recorded run demonstrates a failing verdict on a deliberately degraded prompt."
- **contradiction** — Provider swap claim. Brief: switching "requires only a configuration change and one Infrastructure class, with zero edits to Domain or Application." Brief addendum §Clean Architecture: "a configuration change plus an Infrastructure class." PRD SM-5 and FR-7 say "changing the provider key and restarting is the only change needed." That is true only for the three built-in providers; the brief's claim is about adding a provider. Fix: split FR-7 into (a) switching among built-in providers is configuration only, and (b) adding a provider is one Infrastructure class and a DI registration with zero Domain or Application edits, verified by architecture tests.
- **gap** — Release tag as a success criterion. Brief §Success Criteria: "Release v1.0.0 is tagged with release notes by end of day 2026-09-21." PRD §6.1 lists it in scope but §7 has no metric. Fix: add SM-6 for the tag and release notes, or fold into SM-4.
- **gap** — Architecture document must make the provider swap explicit. Brief addendum §Clean Architecture, demo proof point. PRD: missing. Fix: add a hand-off note to the architect under FR-7 or §4B.

## Build sequence and ALM

- **weakened** — Day-by-day build sequence. Brief addendum §Build sequence gives Saturday, Sunday, Monday contents and the rule "pipeline work comes first, not last." PRD §6.1 references the sequence by pointer and repeats "pipeline first" but does not map FRs to days. Fix: add a three-row table in §6.1 mapping FR groups to the three days (Sat: FR-34 to 36 skeleton, NFR-7, domain model; Sun: FR-1 to 16; Mon: FR-17 to 33, FR-37 to 39, CD, tag).
- **gap** — CD pipeline and release deploy. Brief addendum §ALM and CI/CD: cd.yml pushes images to a registry, runs the EF migration bundle, deploys to Azure Container Apps, and a tagged release triggers a versioned deploy. PRD mentions cd.yml only in Open Question 2. Fix: add NFR-11 "Delivery pipeline" listing ci.yml, eval.yml, and cd.yml behaviours, with the Azure target marked as degradable to a dry run per Open Question 2.
- **gap** — Branch and workflow rules. Brief addendum: GitHub Issues and Project board, every story an issue, trunk-based flow, short-lived branches, conventional commits, PR template with checklist, branch protection on main. PRD covers only issue linking (SM-4) and a PR checklist mention (NFR-9). Fix: fold these into the same NFR-11, or into §4B as an "ALM" guardrail.
- **gap** — Frontend architecture deliverable. Brief addendum §Front end: "Deliverable: /docs/frontend-architecture.md with a diagram of this mapping," plus the MVVM rules (no HTTP in components, feature folders, smart containers). PRD addendum UX inputs mention screens only. Fix: add the deliverable and the "no HTTP calls inside components" rule to the PRD addendum UX section, or to NFR-7 as a front-end layering rule.
- **gap** — Review State transitions as domain logic. Brief addendum §Clean Architecture: "Approval state transitions (Proposed to Approved, Edited, Rejected) are domain logic on the entity, not controller logic." PRD FR-17 states this for Action Status but FR-11 to FR-13 do not state it for Review State. Fix: add a consequence to FR-11 (or a shared line above it) that Review State transitions are enforced on the Proposed Action entity and a non-Pending transition throws.

## AI, audit, and observability

- **weakened** — Per-run logging fields. Brief addendum §AI requirements: "Provider, model, latency, and token counts are logged per run." PRD NFR-4 logs "correlation id, provider, duration, and outcome" and omits model and token counts. Fix: add model name and token counts to NFR-4.
- **weakened** — Rejection as a first-class audit entry. Brief §What Makes This Different: "Every human decision is a first-class audit entry." PRD FR-13 stores the rejection "on the Proposed Action," while FR-21 says every Review State change produces an Action Revision. The two readings can both hold but FR-13 does not say a revision is written. Fix: add to FR-13 "An Action Revision of kind Review Decision records the rejection, reason, User, and timestamp."
- **weakened** — Re-run against immutable notes with a newer prompt is positioned as core in brief §What Makes This Different, but in both documents it is P1 (brief P1, PRD FR-42). Consistent on scope, but PRD FR-4 never says which Prompt Version a re-run uses. Fix: add to FR-4 "A run uses the Prompt Version currently configured; selecting a different version is FR-42."
- **gap** — Eval gate measured "precision or recall" as the regression trigger. Brief §What Makes This Different. PRD FR-39 covers it with added owner and date accuracy. No fix needed; recorded for completeness.

## Glossary and vocabulary drift

- **drift** — Initial Review State name. Brief addendum §Clean Architecture: "Proposed to Approved, Edited, Rejected." PRD Glossary: "Pending, Approved, Edited, Rejected." Fix: keep Pending in the PRD and add a glossary note "called Proposed in the brief addendum," or change the brief addendum to Pending.
- **drift** — "audit entry" (brief) vs "Action Revision" (PRD). The PRD Glossary defines Action Revision as "One audit entry," so this is resolved, but brief §What Makes This Different still says "audit entry" and PRD §1 says "kept as Action Revisions" where the brief says "kept in an audit trail." Fix: none required; optionally add "audit entry" as an alias in the glossary.
- **drift** — "source sentence" vs "Source Excerpt." Brief §The Solution step 2 says "source sentence"; PRD Glossary defines Source Excerpt as "sentence or fragment"; PRD FR-37 and FR-38 use "source sentence" for the Golden Set expected value. Fix: in FR-37 and FR-38 call the expected value "expected Source Excerpt" so the term is single.
- **drift** — "MeetingNotes" (brief addendum entity) vs "Meeting Notes" (PRD glossary). Fix: none; note in glossary that the entity is `MeetingNotes`.
- **drift** — "deterministic fake" (brief) vs "Fake Provider" (PRD). Fix: add "deterministic fake" to the Fake Provider glossary line.
- **drift** — "review and approval flow" (brief addendum build sequence) vs "Review Decision" (PRD). Fix: none.
- **drift** — "webhook receiver page or echo endpoint" (brief addendum) vs "Demo receiver" (PRD FR-33) and "echo" (PRD §5). Fix: pick one label in FR-33 and use it in §5.
- **drift** — "timestamp" on ExtractionRun (brief addendum data model) vs "start time" (PRD glossary) vs "start timestamp" (FR-6). Fix: use "start timestamp" in the glossary.

## PRD-internal inconsistencies surfaced while reconciling

- **contradiction** — PRD §2.2 Non-Users says Webhook Subscriptions "are seeded" with no admin UI, while FR-29 lets a Lead create and edit them through the API. Not a brief conflict, but a reader will trip on it. Fix: change §2.2 to "seeded, and API-managed by a Lead; no UI."

## Confirmed consistent (no action)

- Trust model core statements, immutable notes, separate proposal and tracked records, run metadata, schema validation with one retry, untrusted notes and injection golden case, low-confidence flag, owner resolved on approval, JWT with two roles, OpenAPI and Swagger UI, HMAC webhook via outbox with retry, seed data storyline including three meetings, five-minute compose start, fictional-and-unclassified rule, P1 list, out-of-scope list, code freeze date, all four seed open questions resolved in the PRD addendum.
