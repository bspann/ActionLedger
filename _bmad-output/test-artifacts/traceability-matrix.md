---
stepsCompleted:
  [
    'step-01-load-context',
    'step-02-discover-tests',
    'step-03-map-criteria',
    'step-04-analyze-gaps',
    'step-05-gate-decision',
  ]
lastStep: 'step-05-gate-decision'
lastSaved: '2026-09-21'
coverageBasis: 'acceptance_criteria'
oracleConfidence: 'high'
oracleResolutionMode: 'formal_requirements'
oracleSources:
  [
    '_bmad-output/planning-artifacts/epics.md#story-14',
    '_bmad-output/implementation-artifacts/spec-1-4-sign-in-and-receive-a-jwt-list-users.md',
    'web/actionledger-web/openapi.json',
  ]
externalPointerStatus: 'not_used'
collectionStatus: 'COLLECTED'
collectionMode: 'contract_static'
sourceSha: 'b970415ce75039b709bfc25bdbec4be39a881ee5'
gateType: 'story'
decisionMode: 'deterministic'
gateStatus: 'PASS'
---

# Traceability Matrix & Gate Decision — Story 1.4: Sign in and receive a JWT; list users

**Target:** story 1.4 · **Commit:** `b970415` · **Evaluator:** Murat (Master Test Architect)
**Suite state after remediation:** `dotnet test ActionLedger.sln` → **119 passed, 0 failed, 0 skipped, 0 warnings**
(117 at first trace; +2 from the guard added to close GAP-1, below)

**Oracle:** the three Given/When/Then blocks in `epics.md` Story 1.4, decomposed into six criteria
(each block's `And` clause states a separately testable obligation). Confidence **high** — formal,
committed acceptance criteria, cross-checked against the spec's I/O matrix and the published contract.

---

## PHASE 1: REQUIREMENTS TRACEABILITY

### Coverage Summary

| Metric | Value |
|--------|-------|
| Criteria traced | 6 |
| FULL | 6 |
| PARTIAL | 0 |
| NONE | 0 |
| Overall coverage (FULL+PARTIAL) | 100% |
| **P0 coverage (FULL only)** | **100% (5/5)** |
| P1 coverage (FULL only) | 100% (1/1) |
| Unique test cases accepted as evidence | 51 across 8 files |

### Detailed Mapping

#### AC-1: Valid credentials return an HS256 JWT with `sub`/`name`/`role` and an 8-hour expiry signed with `Jwt:Key` (P0)

**Coverage: FULL**

| Test | Level |
|------|-------|
| `tests/Application.Tests/Auth/SignInHandlerTests.cs:27` — `A_valid_credential_returns_a_token_and_the_callers_own_summary` | unit |
| `tests/Application.Tests/Auth/SignInHandlerTests.cs:103` — `A_username_is_matched_as_stored_so_casing_and_padding_do_not_matter` (3 cases) | unit |
| `tests/Api.Tests/AuthEndpointTests.cs:41` — `A_seeded_user_signs_in_and_the_token_is_one_the_running_host_accepts` | api |
| `tests/Api.Tests/AuthEndpointTests.cs:60` — `The_issued_token_is_hs256_with_sub_name_and_role_no_audience_and_eight_hours` | api |
| `tests/Api.Tests/AuthEndpointTests.cs:101` — casing/padding theory (4 cases) | api |

**Justification:** every clause of the criterion is established. Algorithm, the three claims, and the
8-hour lifetime are asserted directly on a decoded token; "signed with `Jwt:Key`" is established
behaviourally — the token is replayed against the running host on `GET /api/v1/users`, which only
accepts a signature over the configured key. Runs against a real `postgres:18-alpine`.

#### AC-2: Invalid credentials return 401 `unauthorized` with a message that does not say which part was wrong (P0)

**Coverage: FULL**

| Test | Level |
|------|-------|
| `tests/Application.Tests/Auth/SignInHandlerTests.cs:47` — `A_wrong_password_is_refused` | unit |
| `tests/Application.Tests/Auth/SignInHandlerTests.cs:57` — `An_unknown_username_is_refused` | unit |
| `tests/Application.Tests/Auth/SignInHandlerTests.cs:67` — `The_system_identity_is_never_signed_in_even_with_the_right_password` | unit |
| `tests/Application.Tests/Auth/SignInHandlerTests.cs:81` — `Every_refusal_is_the_same_answer_so_the_controller_has_nothing_to_leak` | unit |
| `tests/Application.Tests/Auth/SignInHandlerTests.cs:113` — `A_refused_sign_in_never_asks_for_a_token` | unit |
| `tests/Api.Tests/AuthEndpointTests.cs:125` — `A_wrong_password_an_unknown_username_and_the_system_identity_are_one_401` | api |
| `tests/Api.Tests/AuthEndpointTests.cs:157` — `A_malformed_credential_is_a_400_validation_not_a_401` (3 cases) | api |

**Justification:** all three refusal reasons are proven byte-identical at the wire (correlation id
excluded, which is the one field allowed to differ), and the 400/401 boundary is held separately so a
malformed body cannot be mistaken for a wrong credential. Negative-path coverage is complete.

#### AC-3: A valid JWT on `GET /api/v1/users` returns `UserSummaryDto { id, displayName, role }` for every non-system User (P1)

**Coverage: FULL**

| Test | Level |
|------|-------|
| `tests/Application.Tests/Users/UsersQueriesTests.cs:19` — `The_system_identity_is_not_in_the_roster` | unit |
| `tests/Application.Tests/Users/UsersQueriesTests.cs:37` — `The_roster_is_ordered_by_display_name` | unit |
| `tests/Application.Tests/Users/UsersQueriesTests.cs:55` — `Two_people_sharing_a_display_name_still_page_in_a_stable_order` | unit |
| `tests/Application.Tests/Users/UsersQueriesTests.cs:84` — `A_window_outside_the_bounds_is_clamped_rather_than_refused` | unit |
| `tests/Application.Tests/Users/UsersQueriesTests.cs:101` — `Total_counts_every_matching_row_not_the_length_of_the_page` | unit |
| `tests/Application.Tests/Users/UsersQueriesTests.cs:119` — `A_page_past_the_end_is_empty_and_still_reports_the_total` | unit |
| `tests/Application.Tests/Users/UsersQueriesTests.cs:134` — `A_page_far_past_the_end_is_empty_rather_than_an_overflowed_offset` | unit |
| `tests/Api.Tests/AuthEndpointTests.cs:200` — `The_roster_is_every_seeded_human_ordered_by_display_name_and_no_system_identity` | api |
| `tests/Api.Tests/AuthEndpointTests.cs:223` — `The_roster_clamps_the_paging_window_and_counts_every_matching_row` | api |
| `tests/Infrastructure.Tests/ReadSeamTests.cs:20` — `A_read_through_the_seam_leaves_the_change_tracker_empty` | other (infra integration) |
| `tests/Api.Tests/OpenApiContractTests.cs:26` — `Paged_result_component_matches_the_application_type` | api |
| `tests/Api.Tests/OpenApiContractTests.cs:85` — `Enums_are_published_as_the_strings_they_are_serialized_as` | api |

**Justification:** the `IsSystem` exclusion, the DTO's three members, deterministic ordering, and the
paging envelope are each established at unit level and confirmed end to end against a real database.
The integer-overflow edge on the offset is covered, and the wire shape of `role` is pinned both in the
document and on the raw body.

#### AC-4: `ICurrentUser` reads the `sub` claim, and handlers never accept an actor id from a request body (P0)

**Coverage: FULL** — *was PARTIAL; closed by the guard below*

| Test | Level | Establishes |
|------|-------|-------------|
| `tests/Api.Tests/CurrentUserTests.cs:25` — `The_current_user_is_the_sub_name_and_role_of_a_token_this_api_issued` | api | clause 1 |
| `tests/Api.Tests/CurrentUserTests.cs:47` — `The_container_resolves_a_fresh_actor_per_request_not_one_shared_between_them` | api | clause 1 |
| `tests/Api.Tests/CurrentUserTests.cs:83` — `An_unauthenticated_request_has_no_current_user_rather_than_an_invented_one` | api | clause 1 |
| `tests/Api.Tests/CurrentUserTests.cs:94` — `A_request_with_no_http_context_at_all_has_no_current_user` | api | clause 1 |
| `tests/Api.Tests/CurrentUserTests.cs:101` — `An_authenticated_principal_missing_the_subject_claim_is_refused` | api | clause 1 |
| `tests/Api.Tests/CurrentUserTests.cs:119` — `A_claim_this_api_never_issues_is_refused_rather_than_coerced` (3 cases) | api | clause 1 |
| `tests/Architecture.Tests/ActorIntegrityTests.cs:69` — `A_handler_command_never_carries_the_actor_that_sent_it` | unit | **clause 2** |
| `tests/Architecture.Tests/ActorIntegrityTests.cs:92` — `A_query_never_takes_the_actor_as_a_parameter` | unit | **clause 2** |

**Justification:** clause 1 — "`ICurrentUser` reads the `sub` claim" — is established thoroughly,
including the registration lifetime, the unauthenticated path, and coercion of malformed claims.

Clause 2 — "handlers never accept an actor id from a request body" — **had no test at any level at
first trace (GAP-1).** The invariant held in the shipped code, but nothing would have failed if a
later handler added an `ActorId`/`UserId` member, and story 1.4 is precisely the story that
establishes the handler seam every later story copies.

`tests/Architecture.Tests/ActorIntegrityTests.cs` now holds it. The rule reflects over every type a
`*Handler.HandleAsync` accepts and every public method on a `*Queries` class, and refuses an actor
identity member by exact name. It bans the actor and nothing else: a command naming a *target* user
(`OwnerUserId`, `AssigneeUserId`) passes, which matters because Epic 3 resolves an owner on approval
and that owner is not the caller. Both tests assert the enumeration was non-empty, so a renamed
handler suffix turns the rule red rather than making it pass on nothing.

#### AC-5: Every documented operation except `auth/login` and the health routes returns 401 without a token (P0)

**Coverage: FULL**

| Test | Level |
|------|-------|
| `tests/Api.Tests/AuthDisciplineTests.cs:29` — `Every_documented_operation_but_login_and_health_refuses_an_anonymous_caller` | api |
| `tests/Api.Tests/AuthDisciplineTests.cs:64` — `Sign_in_is_published_without_a_security_requirement` | api |
| `tests/Api.Tests/AuthDisciplineTests.cs:77` — `The_roster_is_published_as_requiring_the_bearer_scheme` | api |
| `tests/Api.Tests/AuthEndpointTests.cs:189` — `The_roster_refuses_an_anonymous_caller` | api |
| `tests/Api.Tests/ProblemDetailsTests.cs:76` — `No_credential_on_a_protected_route_is_a_401_unauthorized` | api |
| `tests/Api.Tests/ProblemDetailsTests.cs:87` — `An_invalid_token_is_a_401_unauthorized` | api |
| `tests/Api.Tests/OpenApiContractTests.cs:70` — `Bearer_scheme_is_published_for_the_operations_story_1_4_adds` | api |

**Justification:** the walk enumerates from the generated document and then asserts a real 401 over
HTTP, which is what makes it non-vacuous — a controller missing `[Authorize]` would be both unsecured
and documented as unsecured, so a document-only assertion would pass. The walk asserts it covered at
least one operation, so it cannot pass on an empty enumeration.

#### AC-6: A `Lead`-only test action returns 403 for an ActionOfficer (P0)

**Coverage: FULL**

| Test | Level |
|------|-------|
| `tests/Api.Tests/ProblemDetailsTests.cs:99` — `An_authenticated_user_in_the_wrong_role_is_a_403_forbidden` | api |
| `tests/Api.Tests/ProblemDetailsTests.cs:110` — `An_authenticated_user_in_the_right_role_is_allowed_through` | api |

**Justification:** driven against `AuthProbeController`'s `[Authorize(Roles = "Lead")]` action, which
is exactly the "Lead-only test action" the criterion names. The positive counterpart is asserted too,
so the 403 cannot come from a route that refuses everyone.

---

### Gap Analysis

#### Critical Gaps (BLOCKER) — ✅ ALL CLOSED

**GAP-1 — AC-4 clause 2: no guard that handlers never accept an actor id from a request body (P0) — ✅ CLOSED**

- **Status at first trace:** PARTIAL coverage on a P0 criterion → Gate Rule 1 failed.
- **Status now:** FULL. Closed in this run by `tests/Architecture.Tests/ActorIntegrityTests.cs`.
- **Current exposure:** none. Both shipped handlers take no actor id, and story 1.4 ships no write path.
- **Latent risk:** the actor seam is the pattern Epics 2–4 copy for every attributed write. An added
  `ActorId` on a command would let a caller attribute a write to another user, and no test would go red.
- **Remediation applied:** `tests/Architecture.Tests/ActorIntegrityTests.cs`, two tests, both
  mutation-verified in this run:

  | Mutation introduced | Result |
  |---------------------|--------|
  | `ActorId` added to `SignInCommand` | `A_handler_command_never_carries_the_actor_that_sent_it` → **red** |
  | `ActorId` added as a `UsersQueries.ListAsync` parameter | `A_query_never_takes_the_actor_as_a_parameter` → **red** |
  | that parameter renamed to `OwnerUserId` (a target, not the actor) | **green** — no false positive |

  All mutations reverted; `git status src/` clean. Full suite green at 119.

#### High / Medium / Low Priority Gaps

None. No criterion is at NONE, and no P1/P2/P3 criterion is below FULL.

---

### Coverage Heuristics Findings

| Heuristic | Status | Notes |
|-----------|--------|-------|
| Endpoint coverage | ✅ complete | Both operations the story adds (`POST /api/v1/auth/login`, `GET /api/v1/users`) have direct API tests. No documented operation is untested — `AuthDisciplineTests` walks the contract. |
| Auth/authz negative paths | ✅ complete | Wrong password, unknown username, system identity, anonymous, invalid token, wrong role, missing `sub`, malformed `role` all covered. |
| Error paths | ✅ complete | 400 validation (blank username, blank password, overlong password), 401, 403, plus the paging-overflow edge. |
| UI journey / UI state | n/a | Story 1.4 ships no UI. |

**One heuristic note, not a gap:** there is no test for a token signed with a different key, an
expired token, or a token from another issuer. `ProblemDetailsTests:87` covers the class
(`An_invalid_token_is_a_401_unauthorized`), and the story's review pass rejected the finer split as
already-covered. Recorded here for the reader, not counted against coverage.

---

### Quality Assessment

**Tests passing quality gates:** all 49 accepted tests.

Observations supporting the assessment:

- **Non-vacuity is engineered, not assumed.** The contract walk asserts it covered ≥1 operation; the
  architecture rules count hand-written types so deleting the last consumer turns them red.
- **No credential literals** (NFR5). Passwords are generated per run via `TestApi.NewPassword()`.
- **No literal roster.** Tests read demo users from `DemoDataSeeder`, so renaming a seeded user
  cannot turn an assertion into a false pass.
- **Real dependencies where the criterion needs them.** `postgres:18-alpine` via Testcontainers for
  the endpoint and read-seam suites, rather than in-memory substitutes.
- **Mutation-verified.** The story's own verification table records seven guards broken, confirmed
  red, and reverted. This trace did not re-run those mutations; it accepts the recorded evidence and
  notes it as the story's, not this trace's, verification.

**Tests with issues:** none found.

**Rejected evidence** (tests naming a criterion their assertions do not establish): none.

### Duplicate Coverage Analysis

**Acceptable overlap (defense in depth):** the login and roster behaviours are asserted at unit level
against fakes and again end to end against a real database. This is intentional and correct — the unit
tests pin branch logic, the API tests pin the wire contract and the token's acceptance by the host.

**Unacceptable duplication:** none.

### Coverage by Test Level

| Level | Test cases | Criteria covered |
|-------|-----------|------------------|
| unit | 18 | 4 (AC-1, AC-2, AC-3, AC-4) |
| api | 32 | 6 |
| component | 0 | 0 |
| e2e | 0 | 0 |
| live | 0 | 0 |
| other (infra integration) | 1 | 1 (AC-3) |

### Traceability Recommendations

#### Immediate Actions (Before PR Merge)

1. ~~**Close GAP-1**~~ — ✅ **done in this run.** `tests/Architecture.Tests/ActorIntegrityTests.cs`
   added and mutation-verified; gate moved FAIL → PASS.

#### Short-term Actions (This Milestone)

2. **DW-1 (already on the deferred ledger)** — sign-in has no rate limiting, lockout, or audit log
   line. See the NFR assessment for the security rating this drives.

#### Long-term Actions (Backlog)

3. When Epic 3 introduces the first real `Lead`-only production route, extend AC-6's evidence from the
   test-assembly probe to that route.

---

## PHASE 2: QUALITY GATE DECISION

### Evidence Summary

**Test execution (this trace, commit `b970415`):**

| Command | Result |
|---------|--------|
| `dotnet build ActionLedger.sln` | Build succeeded, **0 warnings**, 0 errors |
| `dotnet test ActionLedger.sln` | **119 passed**, 0 failed, 0 skipped (117 pre-remediation) |
| `--export-openapi` with no database and `Jwt__Key`/`Jwt__Issuer`/`Database__ConnectionString` unset | exit 0, contract written |
| regenerated contract vs. committed contract | byte-identical |

**Flakiness:** not observed. Container-backed suites completed deterministically
(Api.Tests 15.1s, Infrastructure.Tests 7.0s). No retries configured, no skipped tests.

**Mutation evidence (this run):** three mutations introduced against the new guard, two confirmed red,
one confirmed green as a false-positive check, all reverted. See GAP-1 above.

### Decision Criteria Evaluation

| Criterion | Required | Actual | Status |
|-----------|----------|--------|--------|
| P0 coverage | 100% | **100%** | ✅ PASS |
| P1 coverage | ≥90% target, ≥80% minimum | 100% | ✅ PASS |
| Overall coverage | ≥80% | 100% | ✅ PASS |
| All tests passing | yes | 117/117 | ✅ PASS |
| Zero build warnings | yes | 0 | ✅ PASS |

### Gate Decision: ✅ **PASS**

**Rules applied:** Rule 4 — P1 coverage ≥ 90% and overall ≥ 80% with P0 at 100% → PASS.
(P0 100%, P1 100%, overall 100%.)

**History, kept deliberately.** The first trace of this commit returned **FAIL** under Rule 1: AC-4
was P0 at PARTIAL (4/5 = 80%) because its second clause — "handlers never accept an actor id from a
request body" — had no test at any level. That was the only gap in the story. It was a guard gap, not
a defect: the invariant held in the shipped code, but nothing defended it for the later stories that
copy this seam.

GAP-1 was closed in this run and the gate re-derived to PASS. The FAIL is recorded rather than erased
because the gap is the useful finding here — story 1.4's observable behaviour was correct and
unusually well verified throughout (119 green tests, zero warnings, a real-database end-to-end path,
and now nine mutation-verified guards).

**Waivers:** no waiver register (`gate-waivers.md`) is present. None filed, none applied.
