---
id: ADR-006
title: Owner is free text on the proposal and a User reference on the Tracked Action
status: Accepted
date: 2026-09-20
spine: AD-9
---

# ADR-006: Owner is free text on the proposal and a User reference on the Tracked Action

## Context

The AI reads names out of notes. Those names may be nicknames or initials, or may name people who are not users. A Tracked Action needs a real Owner for filtering and accountability (PRD FR-15, FR-18). The AI must never write a foreign key (PRD §4B Safety).

## Decision

`ProposedAction.SuggestedOwner` is a string exactly as the model wrote it. `TrackedAction.OwnerUserId` is a nullable foreign key to `User`, set only from the `ownerUserId` the client sends on Decide; null means Unassigned. `OwnerResolver.Match`, a case-insensitive display-name match, is called in exactly two places: the proposal read model, to pre-select the picker on Run Detail and the Review Screen, and `DecideProposalHandler`, only for the FR-12 change test that derives Approved versus Edited. The handler never assigns an owner from the match (spine AD-9).

## Alternatives considered

- **Resolve to a User at extraction time.** Rejected. It lets the model's guess become a foreign key without a human seeing it, and a wrong match would look authoritative.
- **Make Owner required on approval.** Rejected. A real action may have no known owner yet; forcing a choice invites wrong data. Unassigned is an honest state and the Action List filters on it.

## Consequences

- The audit trail shows "Suggested owner: P. Ram" beside "Owner: Priya Ramaswamy", which is the demo's provenance proof.
- The eval scorer compares free text against a roster with aliases, matching what the model actually produced.
- Unassigned Tracked Actions are valid and visible.
