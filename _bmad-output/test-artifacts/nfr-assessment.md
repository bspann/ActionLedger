---
stepsCompleted:
  [
    'step-01-load-context',
    'step-02-define-thresholds',
    'step-03-gather-evidence',
    'step-04-evaluate-and-score',
    'step-05-generate-report',
  ]
lastStep: 'step-05-generate-report'
lastSaved: '2026-09-21'
target: 'story 1.4 — Sign in and receive a JWT; list users'
sourceSha: 'b970415ce75039b709bfc25bdbec4be39a881ee5'
evaluator: 'Murat (Master Test Architect)'
overallStatus: 'CONCERNS'  # security/performance/reliability; maintainability PASS after GAP-1 closed
---

# NFR Evidence Audit — Story 1.4: Sign in and receive a JWT; list users

**Commit:** `b970415` · **Date:** 2026-09-21 · **Suite:** 119 passed, 0 failed, 0 skipped, 0 warnings (117 at first audit; +2 closing GAP-1)

**Scope note.** This is an evidence audit, not a re-test. Each finding cites evidence that exists in
the repository at this commit or was observed during this run. Where a threshold was never defined,
the finding is CONCERNS by the standing rule — an absent measurement is not evidence the target was met.

## Gate YAML

```yaml
audited_domains:
  security: CONCERNS
  performance: CONCERNS
  reliability: CONCERNS
  maintainability: PASS
overall: CONCERNS
```

## Thresholds Resolved (Step 2)

| NFR | Applies to 1.4 | Threshold | Resolved |
|-----|----------------|-----------|----------|
| NFR-1 Extraction latency | no | — | N/A |
| NFR-2 UI responsiveness | no (story ships no UI) | — | N/A |
| NFR-3 Outbox reliability | no | — | N/A |
| NFR-4 Observability | partly | structured log line w/ correlation id on the events the PRD names | DEFINED (for runs/webhooks); **UNKNOWN for authentication events** |
| NFR-5 Security baseline | yes | writes require valid JWT; secrets from env/user-secrets; CodeQL + Dependabot + secret scanning on; no committed secret | DEFINED |
| NFR-7 Architecture enforcement | yes | NetArchTest fails the build on an outward or banned reference | DEFINED |
| NFR-8 Test discipline | yes | API auth covered; repositories against PostgreSQL in Testcontainers | DEFINED |
| NFR-9 Licensing | yes | MIT / Apache-2.0 / BSD only | DEFINED |
| Auth endpoint latency | yes | — | **UNKNOWN** (no target stated anywhere) |

---

## Security Assessment — **CONCERNS**

| ID | Finding | Status |
|----|---------|--------|
| S-1 | Every documented operation but sign-in and the health routes refuses an anonymous caller | PASS |
| S-2 | Secrets come from configuration only; no credential literal anywhere in the repo | PASS |
| S-3 | CodeQL and Dependabot are configured in the repository | PASS |
| S-4 | The three sign-in refusal reasons are indistinguishable | PASS |
| S-5 | Password length is capped before any hashing work | PASS |
| S-6 | **No rate limiting, lockout, or audit line on `POST /api/v1/auth/login`** | **CONCERNS** |

**S-1 — PASS.** `AuthDisciplineTests.cs:29` enumerates every operation in the generated contract,
skips only `auth/login` and `ApiRoutes.UnversionedPaths`, issues the real request without a token, and
asserts 401 — then asserts the walk covered at least one operation, so it cannot pass vacuously.
`ProblemDetailsTests.cs:99` holds the 403 for an authenticated caller in the wrong role. Mutation
evidence: removing `[Authorize]` from `UsersController` turns three named tests red (recorded in the
story's verification table).

**S-2 — PASS.** `JwtOptions` is bound from configuration with `[MinLength(32)]` on the key; test
passwords are generated per run via `TestApi.NewPassword()` rather than written down. A repo-wide grep
for a credential literal across `src`, `tests`, `.env.example`, and `appsettings*.json` returned
nothing (story verification table). The one fixed string in the tree is a Testcontainers fixture
value, not a product credential.

**S-3 — PASS, with one item not verifiable from the tree.** `.github/workflows/codeql.yml` and
`.github/dependabot.yml` are both present. GitHub **secret scanning** is a repository setting, not a
file — this audit cannot confirm it from the working tree. Confirm it once in the repo settings; it is
the one element of NFR-5 with no in-repo evidence.

**S-4 — PASS.** Wrong password, unknown username, and the system identity produce a byte-identical 401
body (correlation id excluded, which is the one field allowed to differ). Held at both levels:
`SignInHandlerTests.cs:81` and `AuthEndpointTests.cs:125`. Mutation evidence: making the 401 detail
username-specific turns `AuthEndpointTests.cs:125` red.

**S-5 — PASS.** `SignInCommand.PasswordMaxLength` is 256 and is enforced by model validation, so an
anonymous caller cannot hand PBKDF2 a multi-megabyte body for a username the demo publishes. Guarded
by `AuthEndpointTests.cs:157` (`OverlongPassword` case).

**S-6 — CONCERNS.** `AuthController` and `SignInHandler` contain no throttling and emit **no log call
of any kind** — confirmed this run: neither file references `ILogger`. No middleware supplies either.
So a brute force against `POST /api/v1/auth/login` is both unlimited and invisible, and the three demo
usernames are published in `.env.example`, so there is nothing to guess but the password.

*Why CONCERNS and not FAIL:* NFR-5's stated threshold does not include throttling, no acceptance
criterion in `epics.md` or FR-23 requires it, PBKDF2's work factor makes each attempt costly, and the
item is already on the deferred ledger as **DW-1** with that reasoning recorded. It does not block the
demo.

*This must become FAIL before any deployment beyond the demo.* The remediation is a hardening story:
rate limiting or lockout on the login route, plus one structured log line per refused credential
carrying the correlation id — a refused authentication is the canonical event NFR-4's correlation-id
constraint exists for.

---

## Performance Assessment — **CONCERNS**

| ID | Finding | Status |
|----|---------|--------|
| P-1 | No latency target exists for the auth or roster endpoints | CONCERNS |
| P-2 | Roster paging is bounded and the offset overflow is closed | PASS |
| P-3 | No measured baseline for sign-in cost (PBKDF2) | CONCERNS |

**P-1 — CONCERNS (UNKNOWN threshold).** NFR-1 covers extraction and NFR-2 covers the Action List and
Review Screen. Neither covers `POST /api/v1/auth/login` or `GET /api/v1/users`, and no other document
states a target. Per the standing rule, an undefined threshold cannot be scored PASS. No performance
problem is implied — there is simply nothing to measure against. Define a target when the compose
environment lands (story 1.7), where it can actually be measured.

**P-2 — PASS.** `Paging.Normalize` clamps page size to 200, and the review pass closed a real defect
here: `(page - 1) * pageSize` overflowed `int`, so `?page=2000000000&pageSize=200` sent PostgreSQL a
negative OFFSET and 500'd. Offset is now computed as `long` and compared against the total before
narrowing, with a past-the-end page short-circuiting the query entirely. Guarded by
`UsersQueriesTests.cs:134`. Reads go through `IReadDb` with `AsNoTracking`, asserted against a real
database at `ReadSeamTests.cs:20`.

**P-3 — CONCERNS (no baseline).** Sign-in cost is dominated by PBKDF2, which is deliberate. No
baseline has been recorded, so there is no way to notice a regression or a misconfigured work factor.
Cheap follow-up: record the observed login latency once when the compose environment exists.

---

## Reliability Assessment — **CONCERNS**

| ID | Finding | Status |
|----|---------|--------|
| R-1 | Suite is deterministic: 117 passed, 0 failed, 0 skipped, no retries | PASS |
| R-2 | Contract export survives with no database and no JWT configuration | PASS |
| R-3 | Failures are structured ProblemDetails that leak nothing | PASS |
| R-4 | **A refused sign-in emits no log line** | **CONCERNS** |

**R-1 — PASS.** Verified this run at `b970415`: build succeeded with 0 warnings; `dotnet test
ActionLedger.sln` reported 117 passed, 0 failed, 0 skipped. Container-backed suites completed
deterministically (Api.Tests 15.1s, Infrastructure.Tests 7.0s). No test is skipped, no retry policy is
configured, and nothing is marked flaky.

**R-2 — PASS.** Verified this run: `--export-openapi` with `Database__ConnectionString`, `Jwt__Key`,
and `Jwt__Issuer` all unset and no database reachable exited 0 and wrote the contract, and the
regenerated file was byte-identical to the committed one. This is the story-1.3 export trap surviving
two new controllers — a genuine reliability property, since CI regenerates the contract.

**R-3 — PASS.** `ApiExceptionHandler` maps only the three domain exception types; anything else is a
bare 500 that says nothing about itself (`ProblemDetailsTests.cs:132`), an unmapped path is a
ProblemDetails 404 rather than an HTML error page (`:121`), and every problem carries the correlation
id of the request that produced it (`:148`). Middleware order is pinned so 401/403 bodies are not
silently emptied.

**R-4 — CONCERNS.** The observability half of DW-1 (see S-6). Correlation ids reach *responses*, but a
refused credential produces no log record at all, so there is no signal to alert on and no trail after
the fact. Rated here as well as under Security because it is an operability defect independent of the
brute-force exposure: even a single legitimate user locked out leaves nothing to diagnose from.

---

## Maintainability Assessment — **PASS**

| ID | Finding | Status |
|----|---------|--------|
| M-1 | Ring rules are enforced by tests that cannot pass vacuously (NFR-7) | PASS |
| M-2 | Test discipline matches NFR-8 for this story's surface | PASS |
| M-3 | Licensing is centrally pinned to permissive licences (NFR-9) | PASS |
| M-4 | Zero-warning build enforced repo-wide | PASS |
| M-5 | ~~The actor seam has one invariant held only by inspection~~ — closed in this run | PASS |

**M-1 — PASS.** `DependencyRuleTests` checks every rule twice — over assembly types *and* over a raw
`.csproj` scan — so even an unused `PackageReference` fails. `PasswordVerificationResult` stops in
Infrastructure behind `IPasswordVerifier`, `Microsoft.IdentityModel` stays in Api behind
`IAccessTokenIssuer`, and `IReadDb` carries its own materialization so EF's async operators never
reach Application. This is NFR-7 met in full.

**M-2 — PASS.** NFR-8 asks for API auth coverage and repositories against PostgreSQL in Testcontainers;
both are present (`AuthEndpointTests`, `ReadSeamTests`, `ReadinessTests`). Test naming, the
`/// <summary>` naming the AD each class defends, and `TestContext.Current.CancellationToken` on
awaited calls are applied consistently across the new files.

**M-3 — PASS.** `Directory.Packages.props` has both `ManagePackageVersionsCentrally` and
`CentralPackageTransitivePinningEnabled`, so a package without a `PackageVersion` fails restore
outright. The one package this story added (`Microsoft.Extensions.DependencyInjection.Abstractions`)
is MIT.

**M-4 — PASS.** `TreatWarningsAsErrors`, `Nullable=enable`, and `EnforceCodeStyleInBuild` are set
repo-wide including tests; the build at this commit produced 0 warnings.

**M-5 — PASS (was CONCERNS).** This was **GAP-1** from the traceability matrix. AC-4's second clause
— "handlers never accept an actor id from a request body" — held in the shipped code but was guarded
by nothing, so an added `ActorId` on a future command would have passed every test in the suite.
Closed in this run by `tests/Architecture.Tests/ActorIntegrityTests.cs`, mutation-verified red on an
`ActorId` added to `SignInCommand` and on one added to `UsersQueries.ListAsync`, and verified not to
false-positive on a target-user member (`OwnerUserId`). The actor seam now carries the same class of
guard as every other invariant this story introduces.

---

## Summary & Recommended Actions

Story 1.4 is in good shape. Maintainability is PASS. The other three domains score CONCERNS on items
that are either explicitly deferred with sound reasoning (DW-1) or are thresholds nobody has defined
yet — not on defects. Nothing in this audit contradicts the story's `done` status for demo purposes.

**Before merge:**

1. ~~**M-5 / GAP-1**~~ — ✅ **done in this run.** The `Architecture.Tests` rule is in place and
   mutation-verified; the quality gate moved FAIL → PASS.

**Before the 2026-09-23 demo:**

2. **S-3** — confirm GitHub secret scanning is enabled on the repository. One settings check; it is
   the only part of NFR-5 with no in-repo evidence.

**Before any non-demo deployment (hardening story):**

3. **S-6 / R-4 (DW-1)** — rate limiting or lockout on `POST /api/v1/auth/login`, plus one structured
   log line per refused credential carrying the correlation id. Already on the deferred ledger;
   this audit raises its urgency to *blocking for production*, not for the demo.

**When the compose environment exists (story 1.7):**

4. **P-1 / P-3** — define and record a latency target and a sign-in baseline for the auth endpoints.
