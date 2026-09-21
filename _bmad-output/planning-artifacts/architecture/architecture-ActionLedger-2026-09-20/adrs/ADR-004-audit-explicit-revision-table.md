---
id: ADR-004
title: Audit is an explicit revision table
status: Accepted
date: 2026-09-20
spine: AD-7
---

# ADR-004: Audit is an explicit revision table

## Context

The Audit Trail must show the AI proposal, every human decision and edit, who made it, and when (PRD FR-21, FR-22, UJ-4). It must be attributable to a named user and must distinguish kinds of change (AI Proposal, Review Decision, Status Change, Field Edit).

## Decision

`ActionRevision` is its own append-only persistence root, with no navigation from any aggregate. Each row holds target type and id, a `Sequence` that is strictly increasing per target, kind, field, old value, new value, actor user id (null for the AI), and timestamp. Rows are created only inside the aggregate methods that change state, so the actor and kind are known at the moment of change. Each method receives `now` once and stamps every revision it produces with that same instant. The methods return their revisions, and the handler adds them through `IActionRevisionRepository` in the same commit as the aggregate. No port exposes update or delete for revisions (spine AD-7).

## Alternatives considered

- **PostgreSQL temporal tables or EF Core interceptors capturing before and after images.** Rejected. They record that a row changed, not who decided or why. Attribution would have to be bolted on, and the AI Proposal entry, which is not a change to an existing row, does not fit the model.
- **Event sourcing the aggregates.** Rejected for a three-day build. Rebuilding state from events, snapshotting, and projections add a great deal of machinery for one screen. The revision table gives the same read without changing how the aggregates persist.

## Consequences

- The Audit Trail is a single query over revisions for a proposal and its tracked action, ordered by timestamp then `Sequence`.
- Every write path that changes Review State or Action Status must produce its revisions, which is why AD-3 restricts those changes to aggregate methods and why the handler must add what they return.
- The AI Proposal is a revision row with a JSON new-value, so the trail has one uniform shape.
