---
id: ADR-003
title: Prompt Version and model are stored on every run
status: Accepted
date: 2026-09-20
spine: AD-6
---

# ADR-003: Prompt Version and model are stored on every run

## Context

The Evaluation Gate compares extraction quality across prompt and model changes (PRD FR-37 to FR-39). Run Detail must show what produced a set of proposals (FR-6, FR-9). Without both facts on the row, a regression cannot be attributed and a demo run cannot be reproduced.

## Decision

`ExtractionRun` requires `Provider`, `Model`, `PromptVersion`, and `SchemaVersion` plus timing and token metrics. Prompts are versioned files at `/prompts/extract-actions.v{N}.md`, embedded in `ActionLedger.Infrastructure` and read through `IPromptCatalog`. The version is the number in the filename. The active version is `Ai:PromptVersion` when set, else the highest number, and it is validated to exist at startup. The eval scorer names its report by provider, model, prompt version, and date (spine AD-6, AD-19).

## Alternatives considered

- **Prompt text inline in code, version implied by git.** Rejected. A run row cannot cite a commit, and a prompt change would ship as a code change without a visible version bump.
- **Store the full prompt text on every run.** Rejected for v1. It bloats the table and the version identifier already resolves to the exact file in the repository. Revisit if prompts become editable at runtime, which is a PRD non-goal.

## Consequences

- Every Run Detail can say "LocalOpenAI · {model} · extract-actions.v1" and the eval report can be matched to it.
- A prompt change is a pull request that touches `/prompts`, which is the path filter that triggers the Evaluation Gate.
- Adding `SchemaVersion` means an output-schema change is as traceable as a prompt change.
