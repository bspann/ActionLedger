# Epic 3 Context: Decide what becomes tracked work

<!-- Generated from planning artifacts. Regenerate with compile-epic-context if planning docs change. -->

## Goal

An Action Officer reviews each AI proposal from an Extraction Run on the Review Screen, with the notes shown beside the proposals and the source sentence highlighted, and approves it, edits and approves it, or rejects it. Only a human decision creates a Tracked Action. Every decision records who made it, is written to the audit log, and is safe when two people decide at once. Every approval writes its outbox row in the same transaction, so no approval can be lost before webhook delivery. This is the core trust model of the product: nothing the AI says becomes a record without a person approving it.

## Stories

- Story 3.1: Decision domain model with ordered revisions and the Tracked Action root
- Story 3.2: Decision endpoint with owner resolution and concurrency safety
- Story 3.3: Transactional outbox row on every approval
- Story 3.4: Review Screen layout with proposal cards and source highlighting
- Story 3.5: Make decisions on the Review Screen

## Requirements & Constraints

- **Review Screen:** proposals appear in the order the AI returned them. Each one shows description, suggested owner, suggested due date, Confidence Score, Source Excerpt, Review State and the API-computed `isLowConfidence` flag. Pending proposals offer Approve, Edit and Reject. Decided proposals show the decision, who decided, and when. A Pending count is always shown.
- **Approve:** the proposal becomes Approved, a Tracked Action is created with status Open, and a ReviewDecision revision is written. Deciding a proposal that is not Pending returns 409.
- **Edit and approve:** the proposal becomes Edited and the Tracked Action gets the edited values. The original proposal values never change. Revisions are written in order: ReviewDecision first, then one FieldEdit per changed field, all with one shared timestamp. If nothing changed (keeping the pre-selected owner counts as no change), it is treated as a plain Approve.
- **Reject:** takes an optional reason and creates no Tracked Action. The ReviewDecision revision records the reason, the user and the timestamp, and all three appear on the Review Screen and on Run Detail.
- **Low confidence:** proposals below the threshold (configurable, default 0.70) are flagged with an icon, a text label and a left border, never by color alone. They are never blocked. The client never hardcodes the threshold.
- **Owner resolution:** the suggested owner is free text on the proposal. The API pre-selects a user whose display name matches it, ignoring case. The Tracked Action stores only a nullable User reference, where null means Unassigned.
- **Tracked Action creation:** only Approve or Edit-and-Approve creates one. It links to exactly one proposal, run and Meeting, and raises the domain event that feeds the outbox. No endpoint creates one directly, and the OpenAPI document must have no `POST /tracked-actions`.
- **Outbox:** each active webhook subscription whose event types include `action.approved` gets one Pending outbox message, written in the same transaction as the approval. All messages for one event share its event id. The payload holds the event id, event type, timestamp, Tracked Action, proposal and the decision with display names. If the transaction fails after the outbox write, neither the Tracked Action nor the outbox row may exist; a Testcontainers test must prove this.
- **Auth:** decisions require role ActionOfficer or Lead. The acting user always comes from the JWT, never from the request body.
- **Performance:** a Review Screen with 50 proposals renders within 2 seconds on compose.
- **Accessibility:** the whole Review Screen works from the keyboard and every control has a name. A manual keyboard pass and a browser axe scan are recorded in the PR checklist.

## Technical Decisions

- **Single mutation path:** `ProposedAction.Decide(kind, edits, actor, now)` is the only way Review State changes. On a non-Pending proposal it throws `DomainRuleException`, which maps to 409. It returns a `DecisionResult` that carries the new `TrackedAction` (for Approved or Edited) and the revisions it produced. It raises `TrackedActionCreated(trackedActionId, proposedActionId, kind)` on the Tracked Action root through `AggregateRoot.Raise`. Setters are private.
- **Decision copy on the proposal:** `ProposedAction` also keeps `ReviewState`, `DecidedByUserId`, `DecidedAt` and `RejectionReason` for fast reads. The ReviewDecision revision is the source of truth, and an Infrastructure test asserts the two agree after each decision kind.
- **Revisions:** `ActionRevision` is its own append-only root with no navigation from any aggregate. `Sequence` increases strictly for each (TargetType, TargetId). The ReviewDecision revision targets the proposal. The FieldEdit revisions target the new Tracked Action. There is no port for updating or deleting revisions.
- **Handler:** `DecideProposalHandler` takes the actor from `ICurrentUser` and the time from `IClock`, never from the command. It works out Approved or Edited itself; the client never decides this. It adds the Tracked Action through `IActionRepository.Add` and the revisions through `IActionRevisionRepository.AddRange`, then calls `IUnitOfWork.CommitAsync` exactly once. Application tests assert both adds happen before the commit.
- **Owner rule:** `OwnerResolver.Match` is a pure function in Application/Review. It is called in two places only: in `ProposedActionReadModel`, to set `suggestedOwnerUserId` and `isLowConfidence` for both Run Detail and the Review Screen, and in the handler's change test (`sent ownerUserId != Match(...)?.Id`, plus diffs on description and due date). The handler never assigns an owner from the match. The owner is exactly the `ownerUserId` the client sends.
- **Endpoint:** `POST /api/v1/proposed-actions/{id}/decision` with body `{ ownerUserId, dueDate, description, reason }`. `reason` applies to Reject only. Errors are ProblemDetails.
- **Concurrency:** `ProposedAction` and `TrackedAction` use the PostgreSQL `xmin` concurrency token. There is a unique index on `tracked_action(proposed_action_id)`. Concurrency exceptions and unique-constraint violations become `ConcurrencyConflictException`, which returns 409 `conflict`. An Api test sends two decisions on the same proposal at once and asserts exactly one Tracked Action and one 409.
- **Outbox pipeline:** runs inside `AppDbContext.SaveChangesAsync`, with `IClock` injected into the context. It collects events from Added or Modified aggregate roots and calls `IWebhookPayloadBuilder.BuildAsync`, which may run one query against the same context. It serializes with the API's JSON options, writes the rows, and clears events after the commit. The wire shape `WebhookEventDto`, the `WebhookEventTypes` map and the builder all live in `Application/Webhooks`. Domain events carry only ids and the decision kind.
- **New entities:** `WebhookSubscription` and `OutboxMessage` are roots. Event types are stored as `text[]`, the payload as `jsonb`. `OutboxMessage` gets an `xmin` concurrency token and an index on `(state, next_attempt_at)`. `SaveChangesAsync(suppressOutbox: true)` exists, and an Architecture test asserts it is referenced only from `Infrastructure/Seed`.
- **Migrations:** Story 3.1 adds `tracked_actions`. Story 3.3 adds the subscription and outbox tables. Keys are UUIDv7 generated in the Domain with `ValueGeneratedNever`, repositories only add new rows, enums are stored as strings, and table names are snake_case.
- **DTO fields the UI depends on:** `ProposedActionDto` must carry `isLowConfidence`, `suggestedOwnerUserId`, `reviewState`, `decidedByUserId`, `decidedByDisplayName`, `decidedAt`, `rejectionReason` and `trackedActionId`. `RunSummaryDto` carries `pendingCount`. After any API change, regenerate the committed `openapi.json` so the snapshot test passes and the web client builds.
- **Frontend:** the code goes in the `Features/Review` folder. There is one routable container page, and its children are presentational components. HTTP calls happen only in `Features/Review/Data/ReviewService.cs`; an Architecture test enforces this. The owner roster comes from `Core/Users/UserDirectory`. bUnit tests in `tests/Web.Tests` cover the page and its data service.
- **Tests by project:** Domain.Tests covers every Review State transition and the revision order. Application.Tests uses the handler with fakes. Infrastructure.Tests runs against Testcontainers and covers the NFR3 outbox test, the decision-copy agreement check, and the inactive and non-matching subscription cases. Api.Tests covers the concurrent-decision case and asserts there is no `POST /tracked-actions`.

## UX & Interaction Patterns

- **Route:** `/meetings/:id/runs/:runId/review`. At 1200px and wider there are two panes: notes on the left (at least 360px wide, `white-space: pre-wrap`) and cards on the right, each scrolling on its own. From 1024px to 1199px the notes collapse into a `MudExpansionPanels` above the cards. Below 1024px is unsupported. Story 2.6's navigation after a run now targets this route.
- **Pending card:** shows a "Proposed by AI" `ProvenanceChip` and the Confidence Score in Roboto Mono. When `isLowConfidence` is set it adds the `LowConfidenceBadge` and a 4px left border. Description, owner and due date are read-only values. The hint "AI suggested: {text}" is always visible. Null values read "Unassigned" / "AI suggested: none" and "No due date proposed". The Source Excerpt appears as a blockquote on the AI provenance container. The buttons are Reject (text), Edit (outlined) and Approve (filled).
- **Edit mode:** description becomes a `MudTextField`, owner a `MudSelect` of users plus Unassigned (pre-selected from `suggestedOwnerUserId`), and due date a `MudDatePicker` with a clear button. The buttons are Cancel (text), which restores the proposed values, and a primary button labelled "Approve with edits" when any field differs from the proposal and "Approve" otherwise.
- **Confirmation:** Reject opens an `IDialogService` dialog with an optional reason and a "Reject" confirm button. Approve does not ask for confirmation.
- **Decided card:** stays in place and keeps its Source Excerpt. It shows "Decided by {name}" with the timestamp beside the chip, not inside it, plus a `ReviewStateChip`. A rejected card shows "Reason: {text}" or "No reason given". An edited card shows a "Proposed" column beside the decided values. Approved and edited cards show a "View action" link to `/actions/:id`, which lands in Epic 4 and shows Not found until then.
- **Source highlight:** focusing or hovering a card, or clicking its blockquote, highlights the matching sentence in the notes pane, scrolls it into view, and sets `aria-describedby`. Only one highlight shows at a time.
- **PendingCounter:** reads "{n} proposals pending", or "All proposals decided" with a "View actions" link to `/actions?meetingId=<id>`. It sits in an `aria-live="polite"` region.
- **Zero proposals:** the screen shows "The AI found no actions in these notes." with "Run again" and "Back to meeting".
- **States:** the write button is disabled while a request is in flight. A 409 shows the snackbar "Already changed. Reloading." and refreshes the run, with no Retry. A 403 shows "Your role does not allow this." Other write failures show a snackbar with Retry and keep the form values. Dates display as `YYYY-MM-DD` and timestamps as `YYYY-MM-DD HH:mm UTC`, never as relative time. Button verbs are exactly Approve, Edit and Reject.

## Cross-Story Dependencies

- 3.1 must land first: 3.2 and 3.3 build on `Decide`, `DecisionResult`, `TrackedActionCreated` and the `tracked_actions` table.
- 3.3 hooks the event raised by 3.1 and committed by 3.2's handler. It builds on Epic 2's run, proposal and AI Proposal revision model.
- 3.4 builds on Epic 2's `ProposedActionReadModel` and run DTOs and on Epic 1's shell, state patterns, `SessionState` and `UserDirectory`. 3.5 depends on 3.2's endpoint and 3.4's card components.
- Downstream: Epic 4 adds status transitions and edits to `TrackedAction`, plus the Action List and Action Detail routes the links point to. Epic 5's dispatcher consumes the outbox rows from 3.3. Epic 6's seeder uses `Decide` with `suppressOutbox: true`. The Playwright test in 6.4 exercises this whole flow.
