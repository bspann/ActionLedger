---
id: ADR-007
title: PostgreSQL, and what changes for SQL Server or Azure SQL
status: Accepted
date: 2026-09-20
spine: AD-10
---

# ADR-007: PostgreSQL, and what changes for SQL Server or Azure SQL

## Context

The seed fixes PostgreSQL. The customer environment may run SQL Server or Azure SQL, so the panel will ask what the switch costs (seed §8, PRD §4.4).

## Decision

PostgreSQL through EF Core with `Npgsql`. All data access is EF Core LINQ except two isolated raw statements, each behind a repository interface: `OutboxRepository.ClaimBatchAsync` for `FOR UPDATE SKIP LOCKED` and `SeedRepository.AcquireLockAsync` for the advisory lock. Ids are UUIDv7 in `uuid` columns, instants are `timestamptz`, dates are `date`, small lists are `jsonb` or text arrays, enums are strings, and the concurrency token is `xmin`. Each mapping sits behind an EF value converter (spine AD-10).

## Alternatives considered

- **SQL Server from the start.** Rejected. The seed locks PostgreSQL. Testcontainers PostgreSQL is lighter in CI, and Azure Database for PostgreSQL is a first-class target for Container Apps.
- **A database-agnostic lowest common denominator.** Rejected. It would give up `SKIP LOCKED` and `jsonb` for no v1 benefit. Portability is achieved by isolation, not by abstinence.

## What changes to target SQL Server or Azure SQL

1. Swap `Npgsql.EntityFrameworkCore.PostgreSQL` for `Microsoft.EntityFrameworkCore.SqlServer` in Infrastructure and regenerate migrations.
2. Replace the one raw claim statement with `WITH (UPDLOCK, READPAST)` semantics.
3. Map `jsonb` and array columns to `nvarchar(max)` JSON with value converters.
4. Swap the Testcontainers module to `Testcontainers.MsSql`.
5. Replace the `xmin` concurrency token with `rowversion` and the advisory-lock statement in `SeedRepository` with `sp_getapplock`.

Domain, Application, Api, and the web app do not change. `Architecture.Tests` (AD-1) keeps every provider package out of those projects, so each of these points can touch Infrastructure only.

## Consequences

- One Infrastructure project change plus a migration regeneration is the honest cost of the switch.
- The naming convention (snake_case tables) is applied in code, so it survives the provider change.
