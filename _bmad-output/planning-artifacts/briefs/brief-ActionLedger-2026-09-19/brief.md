---
title: "Product Brief: ActionLedger"
status: ready-for-review
created: 2026-09-19
updated: 2026-09-19
---

# Product Brief: ActionLedger

## Executive Summary

ActionLedger turns rough meeting notes into tracked action items without letting the AI have the final word. An action officer pastes notes. The AI proposes actions, each with an owner, due date, confidence score, and the sentence that justifies it. A named person approves, edits, or rejects every proposal. Approved actions become tracked work, exposed through a documented REST API and pushed to other systems by signed webhook. Every step, from the AI's original proposal through each human edit, is kept in an audit trail.

The product exists because staff organizations run on meetings, and the actions agreed in them get lost between someone's notes and whatever tracking system the team uses. Existing tools either skip the capture step or trust the model's output as-is. ActionLedger's design principle is that AI output is never authoritative: nothing the model produces becomes a tracked action until a human signs off, and the record always shows what the AI proposed versus what the human decided.

This first version is a small, production-shaped application built in roughly three developer days for a technical interview on 2026-09-23. It is a clean-room personal project. Its purpose is to show a full delivery methodology, a defensible data model, working ALM and CI/CD, and a credible pattern for putting AI into a workflow that a government customer could trust.

## The Problem

A staff meeting ends. Three people took notes, each captured a different subset of the commitments made, and nobody owns the step of turning those notes into tracked work. Days later, an action is discovered to have no owner, or two owners, or a due date nobody remembers agreeing to. The lead cannot see what is overdue because half the actions were never entered anywhere.

Teams cope today by re-reading notes after the meeting and typing actions into a tracker by hand, or by skipping the tracker and relying on memory and email. Both fail quietly. The cost is missed commitments, duplicated follow-up, and no way to reconstruct who decided what when a dispute surfaces.

Handing the notes to an AI and accepting its list of actions solves the effort problem but creates a trust problem. In a government setting, an unreviewed, unexplained model output cannot be the system of record.

## The Solution

ActionLedger keeps the human in the loop by design.

1. The action officer creates a meeting and pastes the raw notes. The notes are stored immutably.
2. An extraction run asks the AI for proposed actions. Each proposal carries a description, a suggested owner, a suggested due date, a confidence score, and the source sentence from the notes that justifies it. Output is validated against a schema and rejected if malformed.
3. On the review screen, the officer approves, edits then approves, or rejects each proposal. Low-confidence proposals are visually flagged.
4. Approved proposals become tracked actions with a status lifecycle. Each tracked action keeps its link back to the proposal and the run that produced it.
5. Leads see a filterable list of tracked actions with an overdue indicator. Anyone can open the audit trail for an action and see the AI proposal, every human change, who made it, and when.
6. Integrators consume the REST API, documented with OpenAPI and Swagger UI. When an action is approved, they receive an HMAC-signed webhook, delivered through an outbox with retry.

The AI provider is swappable by configuration between Azure OpenAI, local Ollama, and a deterministic fake, so the demo works offline and tests are repeatable.

## What Makes This Different

The differentiator is the trust model, not the extraction. Proposals and tracked actions are separate records. Raw notes are immutable, and extraction can be re-run against them with a newer prompt. Every run records the provider, model, and prompt version. Every human decision is a first-class audit entry. An evaluation gate in CI measures extraction quality against a golden set, so a prompt change that degrades precision or recall fails the build.

There is no technical moat here, and the brief does not claim one. The advantage is a coherent, explainable design that a reviewer can walk through end to end and that treats the model as a proposer rather than a decider.

## Who This Serves

- **Action officer.** Runs the meeting follow-up. Needs to turn notes into tracked actions in minutes, with a clear view of what the AI got wrong. Success: no action leaves the review screen without a human decision attached to it.
- **Lead.** Owns the outcomes. Needs to see all actions by owner, status, and due date, and to spot what is overdue. Success: one screen answers "what is slipping and who owns it."
- **Integrator.** Another system, not a person. Needs a stable, documented API and a reliable signed webhook. Success: an approved action shows up in the downstream system without polling.
- **Interview panel (secondary).** Needs to walk the methodology, data model, ALM and CI/CD, and AI usage, with the artifacts that produced them living in the repo.

## Success Criteria

- The end-to-end path works in the demo: paste notes, extract, review, approve, see the tracked action, open its audit trail, watch the signed webhook arrive.
- `docker compose up` from a clean clone yields a seeded, working app in under five minutes on macOS and Windows.
- Every story merges through a pull request with green checks, so the commit history itself tells the ALM story.
- The AI evaluation gate runs in CI with published thresholds and fails the build when extraction quality drops below them.
- Switching the AI provider between Azure OpenAI, Ollama, and the fake requires only a configuration change and one Infrastructure class, with zero edits to Domain or Application. Architecture tests enforce the dependency rule.
- Release `v1.0.0` is tagged with release notes by end of day 2026-09-21.

## Scope

**In (P0).** Meeting creation with immutable notes. AI extraction with structured, schema-validated output and confidence scores. Review screen with approve, edit, reject, and low-confidence flagging. Tracked actions with status lifecycle. Filterable action list with overdue indicator. Audit trail per action. Seeded users with two roles and JWT login. REST API with OpenAPI and Swagger UI. HMAC-signed webhook via outbox with retry. Seed data so every screen is populated on first run.

**In only if P0 is green (P1).** AI-drafted follow-up email per meeting, reviewed before export. Dashboard tiles. Re-run extraction with a newer prompt version and compare results side by side.

**Out, roadmap only.** Real integrations with Teams, Planner, or Outlook. Audio transcription. Multi-tenancy. SSO or CAC authentication. Notifications. Mobile layout polish.

**Hard constraints.** Code freeze end of day 2026-09-21. Scope must fit three build days for one developer working with Claude Code. When in doubt, cut features, never quality gates. All sample content is obviously fictional and unclassified.

## Vision

If the pattern holds, ActionLedger becomes the capture layer in front of whatever tracker an organization already uses. Notes arrive from meetings, chat, or transcription. The AI proposes, humans decide, and the audit trail travels with the action into Planner, Jira, or a ticketing system through the same webhook contract. Prompt versions are evaluated and promoted like code. The model can be upgraded or replaced without anyone downstream noticing, because the system of record was always the human decision, never the model output.
