---
title: "PRD Addendum: ActionLedger"
status: final
created: 2026-09-19
updated: 2026-09-20
---

# PRD Addendum: ActionLedger

This addendum holds the technical depth and rationale that support the PRD but belong in the architecture document, UX documents, or stories rather than in the requirements themselves. The brief addendum at `_bmad-output/planning-artifacts/briefs/brief-ActionLedger-2026-09-19/addendum.md` carries the locked stack, layering rules, ADR list, and build sequence. This file does not repeat that material.

## Resolutions of the seed's open questions

### 1. Evaluation thresholds for a small Golden Set

Decision in FR-39: recall at least 0.80, precision at least 0.75, owner accuracy at least 0.85, due date accuracy at least 0.80, plus a hard fail on the prompt-injection case.

Reasoning. Twelve to fifteen notes with four to six actions each give roughly 50 to 70 expected actions. One missed action moves recall by about 1.5 to 2 points. A threshold of 0.80 tolerates 10 to 14 misses across the set, enough to absorb genuinely ambiguous cases without hiding a regression that drops several. Precision is set lower than recall because over-extraction is cheaper for this product: a human rejects a bad proposal in one click, while a missed action is invisible. Owner accuracy is set higher because the roster is small and names appear verbatim in notes, so a miss there signals a prompt problem. Due date accuracy is set at 0.80 because relative-date resolution is the hardest part of the task.

Alternatives rejected. Per-case averaging was rejected because a single tiny case would carry the same weight as a large one. Thresholds above 0.90 were rejected as dishonest for a set this small. One bad case would fail the gate for reasons unrelated to the change under test.

Revisit rule. After the first real baseline run, record the scores here. Move a threshold only with a written rationale and never to make a specific prompt change pass.

Provider decision (2026-09-20). The baseline and the demo run on LM Studio on the demo Mac through the LocalOpenAI provider, because the interviewing organization has a group pursuing local models and the demo should speak to that path. LM Studio and Ollama both serve the OpenAI chat completions API, so one Infrastructure class handles either by base URL and model name. Pick a model that fits the Mac and returns JSON reliably, enable LM Studio's local server and its structured-output support, and record the server and model name on every Extraction Run and in the committed report under `/tests/Eval/reports/`. From inside docker compose the Mac's LM Studio is reached at `http://host.docker.internal:1234/v1`. Small local models are the reason the thresholds are not set higher: if the baseline lands below a threshold, the first move is to try a stronger local model or tighten the prompt, not to lower the threshold.

### 2. Matching rule

Decision in FR-38: deterministic two-signal fuzzy match, greedy one-to-one.

Reasoning. Signal (a), Source Excerpt overlap, is the primary signal because the extractor is required to cite the notes. If the cited sentence and the expected sentence share at least half of their tokens, the two refer to the same commitment even if the descriptions are phrased differently. Signal (b), description token-set similarity at 0.60, catches cases where the model cites a neighboring sentence but describes the right action.

Alternatives rejected. Exact matching was rejected because any paraphrase would fail and the gate would measure wording rather than extraction. Model-graded matching was rejected because the judge would share failure modes with the system under test, add cost and non-determinism to CI, and make a failing gate hard to explain to the panel. A deterministic scorer can be shown, read, and unit tested.

Implementation note for the story author. Token-set similarity is the count of shared tokens divided by the size of the smaller token set. Greedy assignment sorts all candidate pairs by combined similarity in descending order, then takes each pair in turn whose members are both still unassigned.

### 3. Auth mechanism

Decision in FR-23 through FR-25: seeded Users with hashed passwords, username and password login, 8-hour JWT with `sub`, `name`, and `role` claims, no refresh tokens, token held in memory in the browser.

Reasoning. The audit trail needs a real User id on every write. That requires authentication, not a role header. Symmetric-key JWT with ASP.NET Core's built-in bearer handling is a few dozen lines and needs no external identity provider. Password hashing uses the framework's PasswordHasher so no custom crypto is written. Full ASP.NET Identity was rejected because it brings registration, lockout, and schema that v1 does not use and would have to explain away.

What changes for production. Replace the login endpoint with OpenID Connect against an enterprise identity provider or CAC-backed PKI. The `ICurrentUser` abstraction and the audit columns do not change.

### 4. Outbox worker placement

Recommendation for the architect: run the worker as a hosted `BackgroundService` inside the API process for v1.

Reasoning. This means one fewer container, one fewer image in CD, and a demo that can show the worker log alongside the request log. The worker polls the Outbox table on a short interval, claims a batch with `FOR UPDATE SKIP LOCKED`, and delivers. Correctness does not depend on placement because the outbox write is transactional with the Tracked Action.

Scale path. Move the same class to a separate worker project and container when delivery volume or isolation matters. The ADR on the outbox pattern should record this as the intended evolution.

## Extraction schema

The extractor validates its output against the JSON schema below. The Prompt Version and this schema version are recorded on every Extraction Run.

```json
{
  "type": "object",
  "required": ["actions"],
  "properties": {
    "actions": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["description", "suggestedOwner", "suggestedDueDate", "confidence", "sourceExcerpt"],
        "properties": {
          "description": { "type": "string", "minLength": 1, "maxLength": 500 },
          "suggestedOwner": { "type": "string", "maxLength": 100 },
          "suggestedDueDate": { "type": ["string", "null"], "format": "date" },
          "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
          "sourceExcerpt": { "type": "string", "minLength": 1, "maxLength": 1000 }
        }
      }
    }
  }
}
```

## Webhook payload and signature

Delivery format, headers, and body:

```
POST {subscription.url}
Content-Type: application/json
X-ActionLedger-Event: action.approved
X-ActionLedger-Delivery: 7f3c...   (event id, stable across retries)
X-ActionLedger-Timestamp: 2026-09-21T14:03:22Z
X-ActionLedger-Signature: sha256=<hex of HMAC-SHA256(secret, timestamp + "." + rawBody)>

{
  "eventId": "7f3c...",
  "eventType": "action.approved",
  "occurredAt": "2026-09-21T14:03:22Z",
  "trackedAction": { "id": "...", "description": "...", "ownerId": "...", "ownerName": "...", "dueDate": "2026-10-03", "status": "Open", "meetingId": "...", "meetingTitle": "..." },
  "proposedAction": { "id": "...", "description": "...", "suggestedOwner": "...", "suggestedDueDate": "...", "confidence": 0.82, "sourceExcerpt": "..." },
  "reviewDecision": { "kind": "Edited", "byUserId": "...", "byUserName": "...", "at": "2026-09-21T14:03:21Z" }
}
```

Receivers verify a delivery by recomputing the HMAC with the shared secret and comparing it to the signature header in constant time. Including the timestamp in the signed string lets a receiver reject stale deliveries.

## Seed data storyline

Organization: Pinecrest Regional Office, a fictional branch of the fictional Northwind Cooperative. Nothing in the seed data resembles a government structure. Roster: Dana Whitfield (ActionOfficer), Priya Ramaswamy (ActionOfficer), Marcus Bell (Lead), and a system User "Seed".

- **Meeting 1, "Office move planning", fully reviewed.** Six Proposed Actions: four approved as-is, one edited, one rejected. Tracked Actions in statuses Open, In Progress, and Complete. The edited one is owned by Dana Whitfield, status Open: the AI proposed due date 2026-09-26 and Dana set it to 2026-09-12 on approval, so it is Overdue and its Audit Trail shows a human-changed due date (UJ-2).
- **Meeting 2, "Q4 training event", awaiting review.** One Succeeded Extraction Run with five Pending Proposed Actions, one of them below the Low Confidence Threshold.
- **Meeting 3, "Equipment inventory kickoff", audit showcase.** One Proposed Action with Confidence Score 0.55 whose suggested owner was "P. Ram" and whose suggested due date was wrong. It was edited on approval to owner Priya Ramaswamy and the correct date, so the Audit Trail shows the AI proposal, two field edits, and the Review Decision by Dana.

All seeded Extraction Runs are attributed to the Fake Provider with Prompt Version `extract-actions.v1`.

## Golden Set composition

The set holds twelve to fifteen cases. Each case is a markdown file with front matter for the meeting date, plus a sibling JSON file of expected actions. Suggested spread:

| Case type | Count | Purpose |
|---|---|---|
| Plain actions with owner and date | 4 | Baseline recall and precision |
| Actions with no owner | 2 | Owner accuracy denominators, empty-owner handling |
| Actions with no due date | 2 | Null date handling |
| Relative dates ("by next Friday", "end of month") | 2 | Due date resolution against meeting date |
| Discussion items that are not actions | 2 | Precision, over-extraction |
| One action mentioned twice | 1 | Deduplication |
| Prompt injection inside the notes | 1 | FR-8 hard fail |

The roster file lists canonical names and aliases ("Priya", "P. Ramaswamy", "Priya R.") for owner normalization.

## Demo click path (input to DEMO.md)

1. `docker compose up`, open the web app, log in as Dana.
2. Open Meeting 2, show the Review Screen with the flagged low-confidence proposal. Approve three, edit one, reject one, so no proposal is left Pending.
3. Open the Action List. Filter to Dana as Owner, show the Overdue indicator.
4. Open the Audit Trail for the edited action from Meeting 3.
5. Open the webhook receiver page and show the signed event that arrived from step 2.
6. Open Swagger UI, show the OpenAPI document and the signature documentation.
7. Show the provider configuration key, the Infrastructure folder, and the architecture test that guards the layering.
8. Fallback: if the network fails, every step above works with the Fake Provider.

## UX inputs for Sally

- Screens: Login, Meeting List, Meeting Detail (notes plus runs), Run Detail, Review Screen, Action List, Action Detail with Audit Trail.
- The Review Screen is the hero. Each proposal is a card or row. It shows the Source Excerpt without expansion, the Confidence Score as a number with a flag when it is low, an owner picker pre-filled when the name matches a User, a date picker, and the Approve, Edit, and Reject controls.
- The Audit Trail is a vertical timeline, oldest first, with old and new values side by side per field edit.
- Wireframe level only. Angular Material components, no custom visual design.
- Front-end layering rule for every screen: templates only render and bind events. Component classes expose signals and commands and make no HTTP calls. Typed API clients generated from the OpenAPI document are the model. Feature folders are `meetings`, `review`, `actions`, `audit`, and `auth`. Deliverable at tag time: `docs/frontend-architecture.md` with a diagram of this mapping.

## Artifact location

The seed requires BMAD artifacts under `/docs`. Copy `prd.md` and this addendum into `docs/` before code freeze, alongside the brief, architecture, ADRs, and stories.
