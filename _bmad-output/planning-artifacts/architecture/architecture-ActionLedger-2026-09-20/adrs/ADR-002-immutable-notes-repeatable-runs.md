---
id: ADR-002
title: Meeting Notes are immutable and extraction runs are repeatable
status: Accepted
date: 2026-09-20
spine: AD-5
---

# ADR-002: Meeting Notes are immutable and extraction runs are repeatable

## Context

Extraction quality will change as prompts and models change. The demo and the P1 comparison feature need to re-run extraction on the same input and compare results (PRD FR-2, FR-4, FR-42). An audit reader must be able to trust that the Source Excerpt on a proposal came from the text that was actually extracted.

## Decision

`MeetingNotes` is written once and has no update method or endpoint; a second save returns 409. Each `ExtractionRun` records the `MeetingNotesId` and a SHA-256 of the text it read. Any number of runs may exist per Meeting, each with its own proposals (spine AD-5).

## Alternatives considered

- **Editable notes with a version history.** Rejected. It adds a versioning model for a document nobody needs to edit in v1, and every run would need to pin a version anyway. Immutability gives the same guarantee with no extra tables.
- **One run per Meeting, overwritten on re-run.** Rejected. It destroys the comparison the PRD asks for and orphans the proposals that tracked actions link to.

## Consequences

- To change notes, a user creates a new Meeting. That is acceptable for meeting minutes and is documented in the UX ("Notes cannot be changed after saving").
- Runs are cheap to add and safe to compare; FR-42 needs no new storage.
- The hash gives a tamper check for free and lets the eval scorer confirm which text a report was produced from.
