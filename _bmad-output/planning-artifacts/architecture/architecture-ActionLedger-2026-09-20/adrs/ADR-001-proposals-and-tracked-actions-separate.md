---
id: ADR-001
title: Proposed Actions and Tracked Actions are separate tables
status: Accepted
date: 2026-09-20
spine: AD-4
---

# ADR-001: Proposed Actions and Tracked Actions are separate tables

## Context

The AI proposes actions and a human decides which become tracked work. The PRD's thesis is that the record must always show what the AI proposed versus what the human decided (PRD §1, FR-4, FR-16). The data model has to make that distinction structural, not conventional.

## Decision

`ProposedAction` and `TrackedAction` are separate entities and tables. A `ProposedAction` belongs to an `ExtractionRun`, holds the AI's values and a Review State, and is never edited except to record the decision. A `TrackedAction` is created only by `ProposedAction.Decide` and holds a required foreign key back to its proposal. No API endpoint creates a `TrackedAction` directly (spine AD-4).

## Alternatives considered

- **One table with a status flag** (`Proposed`, `Approved`, `Rejected`, `Open`, ...). Rejected. The AI's values and the human's values would share columns. An edit on approval would then overwrite the proposal unless every field were duplicated as `proposed_*` and `current_*`. Rejected proposals would sit in the same table as live work, so every list query would need a filter. The audit trail would have to reconstruct "what the AI said" from revision rows rather than read it.
- **Proposals as a JSON column on the run.** Rejected. Proposals need their own identity for review decisions, per-row revisions, and the eval scorer.

## Consequences

- Two tables and a one-to-optional-one relationship mean slightly more mapping code.
- Every Tracked Action can always show its originating proposal and run without joins through the audit table.
- Repeated extraction runs against the same notes produce independent proposal sets that never collide with tracked work (see ADR-002).
- The provenance UI (purple for AI, blue for human) maps directly onto the two tables.
