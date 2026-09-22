# Epic 2 Context: Capture meeting notes and get AI proposals

<!-- Generated from planning artifacts. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Epic 2 delivers the capture-and-extract half of the product: an Action Officer creates a Meeting, pastes its notes exactly once, presses "Run extraction", and gets back validated AI proposals carrying a confidence score, a verbatim source excerpt, and complete run metadata. Everything flows through a single provider seam, so the same code path serves a deterministic Fake provider (used for all of Sunday's work, for CI, and as the demo fallback), a local model on the developer's Mac via LM Studio or Ollama, and Azure OpenAI — swapped by one configuration key and a restart, with no Domain or Application edits. The epic also lands the fixture catalog and the versioned prompt, which are the single shared source of truth for the Fake provider's answers, the demo seed data, and the Evaluation Gate. It matters because the provider-swap proof point and the reproducibility of every run are the technical claims the whole demo rests on.

## Stories

- Story 2.1: Meeting and immutable notes API
- Story 2.2: Meeting List, New meeting dialog, and notes paste area
- Story 2.3: Fixture catalog and prompt v1
- Story 2.4: Extraction seam, output validation, and the Fake provider
- Story 2.5: Extraction Run and proposals persisted with AI Proposal revisions
- Story 2.6: Run extraction from Meeting Detail and inspect Run Detail
- Story 2.7: Real providers through the same seam: LM Studio, Ollama, and Azure OpenAI

## Requirements & Constraints

- **Notes are immutable.** Notes attach to a Meeting exactly once, are stored byte-for-byte, and have no update or delete path anywhere — a second save is a conflict. A test must prove no endpoint mutates them. To change notes, a user creates a new Meeting.
- **Runs are repeatable and reproducible.** A Meeting may have any number of runs. Every run records provider, model, prompt version, schema version, the notes identity and a hash of the text it read, start time, duration, input and output tokens (zero for Fake, never null), outcome, failure reason, and warnings.
- **A failed extraction is not an HTTP error.** Invalid model output is retried once, then the run is persisted as Failed with a reason and zero proposals. The create-run endpoint returns the same success status for Succeeded and Failed alike, and the UI surfaces the server-supplied reason verbatim with a "Run again" affordance.
- **Validation is strict and layered.** Output must deserialize strictly against the committed extraction schema (required members, no unmapped members) and pass explicit length, range, and date-format checks. Excerpt verification is a separate post-validation filter: a proposal whose source excerpt cannot be found in the notes is dropped with a recorded warning rather than failing the run. Both kept and dropped proposals travel in the result so the evaluation scorer never has to re-filter.
- **Prompt injection defense.** The prompt instructs the model to treat notes as data and ignore embedded instructions. One fixture case carries an injected instruction that the Evaluation Gate later hard-fails on.
- **Provider selection is configuration only.** One key chooses among LocalOpenAI, AzureOpenAI, and Fake. Missing credentials or an unreachable local endpoint must fail the host at startup with a message naming the problem. A read-only endpoint reports the active provider and model. Adding a provider must be one Infrastructure factory class plus one DI registration.
- **Latency budget.** Per-call timeout 90s, run ceiling 180s (two calls). Fake answers within 1s; a local model run on ~2,000 words should finish inside 60s. There is no client-side timeout on the run endpoint, and the reverse proxy allows 200s on the API path.
- **Fixture catalog shape.** 12 to 15 fictional Pinecrest Regional Office cases covering plain actions with owner and date, no owner, no due date, relative dates, non-action discussion items, a duplicate mention, and one injection; each case pairs a front-matter note file with an expected-output document valid against the schema, plus a roster with aliases. Exactly three are marked as seed cases and must contain the proposals the demo storyline needs. All content is obviously fictional and unclassified.
- **Observability.** One structured log event per completed run carrying correlation id, provider, model, prompt version, duration, tokens, and outcome. Never log notes text or secrets.
- **Attribution and roles.** Creation takes the actor from the JWT, never from the request. Reads need any authenticated user; writes need ActionOfficer or Lead.

## Technical Decisions

- **The seam.** Application owns the extraction port and a provider-info port; the port never throws for provider or validation failure — it returns an explicit succeeded-or-failed result. Infrastructure has exactly one extractor implementation built on the Microsoft.Extensions.AI chat-client abstraction; providers are chat-client factories selected by configuration. Local and Azure providers are both the OpenAI SDK differing only in endpoint, so no Azure-specific SDK enters the tree. Architecture tests fail the build if Application references any AI SDK.
- **Structured output.** The request sets a response format built from the committed schema with strict mode on. The adapter transforms the schema on the wire (all properties required, no additional properties, unsupported keywords demoted to descriptions), so the wire schema is a strict subset of the committed file. A unit test asserts schema-exporter parity on property names, required set, and types.
- **One normalizer, one verifier.** Text normalization (lowercase, strip punctuation, collapse whitespace) and excerpt verification live as pure static code in Application and are the only implementations; an architecture rule fails the build on any second normalizer.
- **Prompts and schema versioning.** Prompt files are versioned by filename, embedded as assembly resources so every consumer (API, tests, eval) reads them without path configuration. The active version is the configured one, else the highest, validated to exist at startup. Schema version comes from the schema file's own top-level version property.
- **Fake provider.** Answers come from the embedded fixture catalog keyed by the hash of the normalized notes text; unknown notes fall back to a modal-verb sentence heuristic with fixed confidences. The catalog is the single source for Fake answers, seed data, and the Golden Set — there is no separate golden directory.
- **Persistence.** The run aggregate is started and given its proposals through aggregate methods; one AI-proposal audit revision is written per proposal (JSON new-value, null actor) inside the aggregate, and the whole run commits once. The revision root is separate with a strictly increasing per-target sequence and a single shared timestamp per aggregate call. Migrations add the meeting, notes, run, proposal, and revision tables with the documented unique constraints and concurrency tokens; migrations ship only through the bundle.
- **Derived values are server-side.** The low-confidence flag (threshold configurable, default 0.70) and the suggested-owner match (case-insensitive display-name match, else null) are computed once in the read model and returned on the DTO. The web never recomputes them.
- **Handler and controller shape.** One handler per write use case, reads as per-feature query classes; controllers validate HTTP shape, call one handler or query, and map. One unit-of-work commit per use case.
- **Web structure.** Blazor WebAssembly feature folders with one routable page component per route; HTTP lives only in per-feature data services wrapping the generated client, enforced by an architecture test. Page components and their data services get bUnit tests.

## UX & Interaction Patterns

- **Meeting List.** A table of Title, Date, Runs, Tracked Actions sorted by meeting date descending; row click or Enter opens detail; paginator at 50 rows. "New meeting" opens a dialog with required title and date and an attendee chip input, with validation messages under each field; creating navigates to Meeting Detail.
- **Notes paste area.** Multiline field with a live character count against the limit, the caption "Notes cannot be changed after saving", and a Save action that confirms in a dialog repeating that sentence. After save the notes render as read-only pre-wrapped text with a "Saved {timestamp} UTC, immutable" caption and no edit affordance, ever.
- **Run extraction button.** Disabled with the visible caption "Add notes first" when there are no notes. On click it disables itself and shows an indeterminate progress bar with the caption naming the active provider and model and warning a local model can take up to a minute; nothing else on the page is blocked. On failure the page stays put and the failed run appears in the run list with its reason.
- **Run list and Run Detail.** The run list shows started, prompt version, provider and model, outcome, proposal count, and pending count. Run Detail uses a two-column definition list (not a table) for the full metadata set, renders tokens as "0" rather than blank, and expands warnings to the individual dropped excerpts. Proposal rows appear in AI return order with a review-state chip, decider name, timestamp, and rejection reason as visible text.
- **Empty and failure states.** A meeting with no runs reads "No extraction runs. Add notes, then run extraction." A failed run shows the server's reason verbatim plus "Run again".
- **Shared conventions inherited from Epic 1.** Progress bar during loads, retry on load failure, snackbar patterns for 403/409/write failures, buttons disabled while a write is in flight, dates as `YYYY-MM-DD` and instants as `YYYY-MM-DD HH:mm UTC` with no relative time, declarative microcopy with no exclamation marks or emoji, the model always called "AI", every indicator pairing an icon with text, and a recorded keyboard pass and axe scan for UI stories.

## Cross-Story Dependencies

- 2.3 is content, not code, and is scheduled first (Saturday evening) because 2.4's Fake provider, Epic 6's seeder, and the Evaluation Gate all read the same catalog. Getting the seed cases wrong here breaks the demo storyline later.
- 2.1 must land before 2.2 (UI needs the endpoints) and before 2.5 (a run needs notes to read).
- 2.4 is the gate for 2.5: the seam, schema, validator, excerpt verifier, and Fake provider must exist before a run can be persisted.
- 2.5 must land before 2.6, which additionally adds the provider-info endpoint the progress caption reads from.
- 2.6's success navigation deliberately targets Run Detail; Epic 3's Review Screen story retargets it. Review-state chips, decider names, and rejection reasons render empty until Epic 3 writes decisions.
- 2.7 is scheduled Monday morning and must touch only provider factories, DI registration, and configuration — that narrow diff is itself the proof point being demonstrated. All of Sunday runs on Fake so no story blocks on a model server.
- Depends on Epic 1 for: the versioned API surface and committed contract that the generated web client is built from, JWT auth and the current-user port, the migration bundle, the MudBlazor shell and global state patterns, and the compose host-gateway alias that lets the API reach a model server on the developer's machine.
- Feeds Epic 3 (proposals, the low-confidence flag, and the pre-selected owner are what the Review Screen decides on), Epic 5 (run metadata flows into webhook payloads), and Epic 6 (seeded demo meetings and the Evaluation Gate both read this epic's catalog, prompt, and seam).
