### DW-1: Sign-in has no rate limiting, lockout, or audit log line, so a brute force against POST /api/v1/auth/login is both unlimited and unobservable.
origin: spec-deferred 005dce3a444c
location: src/ActionLedger.Api/Controllers/AuthController.cs
source_spec: `spec-1-4-sign-in-and-receive-a-jwt-list-users.md`
severity: medium
reason: AuthController and SignInHandler contain no throttling and emit no log entry on a refused credential, and no middleware supplies either. The epic's Observability constraint asks for a correlation id on every line, and a failed authentication is the canonical event needing one. Not caused by a defect in this story's code: no acceptance criterion in epics.md or PRD FR-23 requires throttling, and adding it is new product surface rather than a smallest fix. Worth a hardening story before anything beyond the demo.
status: open
