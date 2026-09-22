# Fixture catalog — `fixtures/extraction/`

One catalog, three consumers (AD-21). The Fake provider answers from it, the demo seeder builds its
Meetings from it, and the Evaluation Gate scores against it. There is no separate `golden/`
directory, and nothing here may be copied into a second source of truth.

All content is fictional and unclassified: Pinecrest Regional Office, a branch of the fictional
Northwind Cooperative, doing mundane office work. Nothing here refers to a real organisation,
location, programme or person.

## What the folder holds

| File | Count | Purpose |
| --- | --- | --- |
| `<case>.md` | 14 | One meeting's front matter and notes |
| `<case>.expected.json` | 14 | The actions a correct extraction returns for that case |
| `roster.json` | 1 | Canonical names, roles and aliases for owner resolution |
| `README.md` | 1 | This file |

Every `<case>.md` has a sibling `<case>.expected.json` and vice versa. Stems are kebab-case and are
the catalog's case keys.

## `<case>.md`

The file opens with YAML front matter delimited by a line containing exactly `---`, then a closing
line containing exactly `---`, then the notes body.

| Key | Type | Required | Notes |
| --- | --- | --- | --- |
| `title` | string | yes | The Meeting title. `(title, meetingDate)` is unique across the catalog, because AD-20 indexes `meeting(title, meeting_date)`. |
| `meetingDate` | string | yes | `YYYY-MM-DD`. Relative dates in the notes resolve against this value. |
| `attendees` | flow sequence of strings | yes | Non-empty. Plainly invented names are allowed here beyond the roster. |
| `seed` | boolean | yes | `true` on exactly three cases, which supply the demo storyline. |
| `injectionSpan` | string | no | Present on exactly one case. The **verbatim** injected text, which occurs literally in the notes body. |

**The notes body is everything after the line containing the closing `---`, with that line's
terminating newline consumed and the remainder kept byte-for-byte.** No trimming, no re-wrapping, no
newline translation. Story 2.4 hashes this text and Story 2.4's excerpt verifier searches it, so any
consumer that trims it will disagree with the answer files.

## `<case>.expected.json`

An exact `extract-actions.schema.json` document: an object whose only member is `actions`, an array.
Every entry carries all five members explicitly — never omission. An absent owner is the empty
string and an absent due date is `null`; that asymmetry is the published schema's, not ours.

| Member | Type | Constraint |
| --- | --- | --- |
| `description` | string | 1–500 characters |
| `suggestedOwner` | string | ≤ 100 characters, **never `null`** — an unknown owner is the empty string, because the published schema types this member as a plain string (`prd.md:154` "may be empty"; the schema in `addendum.md:68` types only `suggestedDueDate` as nullable). When non-empty it matches a `displayName` or an alias in `roster.json`, case-insensitively. |
| `suggestedDueDate` | string or `null` | `YYYY-MM-DD` |
| `confidence` | number | 0–1 |
| `sourceExcerpt` | string | 1–1000 characters, and an **exact ordinal substring of the sibling `.md`'s notes body** |

An excerpt is copied out of the notes, never retyped. Story 2.4's excerpt verifier drops any
proposal whose excerpt cannot be found, and a dropped proposal is a recall miss the Evaluation Gate
charges against the Fake provider, which must score 1.0.

**The notes bodies are hard-wrapped at about 100 columns, and a hard wrap is a real newline in the
body.** A sentence split across two lines is therefore not the substring an excerpt written on one
line claims to quote. Every sentence an answer file quotes sits on a single line, and any new
sentence a case cites must be put on one line too, however long it runs. The injected span in
`vendor-onboarding-notes.md` is on one line for the same reason.

## `roster.json`

An object with one member, `people`, an array of `{ displayName, role, aliases }`. Story 6.2
resolves owner accuracy through `aliases`, so an owner string that appears in any expected file must
appear here as a `displayName` or an alias. **No usernames and no passwords.** Sign-in credentials
live in `src/ActionLedger.Infrastructure/Seed/DemoDataSeeder.cs` and are not duplicated here.

## The case spread

The counts come from the epic and are pinned by `tests/Architecture.Tests/FixtureCatalogTests.cs`.

| Case type | Count | Cases |
| --- | --- | --- |
| Plain actions with owner and date | 4 | `office-move-planning`, `q4-training-event`, `loading-dock-scheduling`, `badge-printer-replacement` |
| Actions with no owner | 2 | `break-room-refresh`, `visitor-parking-signage` |
| Actions with no due date | 2 | `records-shredding-vendor`, `intern-orientation-packet` |
| Relative dates | 2 | `equipment-inventory-kickoff`, `quarterly-safety-walkthrough` |
| Discussion items that are not actions | 2 | `coffee-service-discussion`, `parking-lot-repaving-debate` |
| One action mentioned twice | 1 | `server-room-air-conditioning` |
| Prompt injection inside the notes | 1 | `vendor-onboarding-notes` |

A relative-date case states its deadline as a phrase; the resolved calendar date appears only in the
answer file, never in the notes.

The duplicate-mention case states one commitment twice. That commitment is **one** expected action,
cited from one of the two sentences: two entries for it would collapse under the Gate's greedy
one-to-one matcher and score as a false positive. The case's answer file holds two actions in all —
the duplicated commitment once, plus one unrelated action.

## The three seed cases

`seed: true` marks the cases Story 6.1 turns into demo Meetings, so their answer files are load
bearing for the storyline, not just for scoring.

| Case | What the storyline needs |
| --- | --- |
| `office-move-planning` | Six actions. One is owned by `Dana Whitfield` with `suggestedDueDate` `2026-09-26`; the seeder edits that date to `2026-09-12` on approval, which is what makes the audit trail show a human-changed due date. |
| `q4-training-event` | Five actions, all left Pending, at least one below the `0.70` low-confidence threshold. One is a real commitment a reviewer may reasonably decline, so the on-stage reject does not poison precision. |
| `equipment-inventory-kickoff` | Exactly one action, `confidence` exactly `0.55`, `suggestedOwner` exactly `P. Ram`. The notes give the deadline as "by the end of next month", which resolves against `meetingDate` `2026-08-31` to `2026-09-30` — the correct resolution, and the answer file records it. A separate sentence says the cooperative's asset report closes on 18 September, so the human, not the model, is the one who corrects the date on approval. The Gate stays honest and the demo still shows a field edit. |

Seed meeting dates sit in late August 2026 because `FixedClock.SeedInstant` is `2026-09-01T00:00:00Z`
and a Meeting the seeder creates then must not be dated in its own future.

## Changing this catalog

Fixture content is contract. `tests/Architecture.Tests/FixtureCatalogTests.cs` fails the build on a
broken excerpt, a fourth seed case, a missing `injectionSpan`, an owner absent from the roster, a
changed case-type count, or a seed case that no longer matches the storyline. Adding or editing a
case means updating that test in the same pull request.
