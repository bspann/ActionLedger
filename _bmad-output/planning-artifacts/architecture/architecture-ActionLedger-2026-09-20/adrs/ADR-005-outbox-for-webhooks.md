---
id: ADR-005
title: Webhooks go through a transactional outbox
status: Accepted
date: 2026-09-20
spine: AD-8
---

# ADR-005: Webhooks go through a transactional outbox

## Context

Approving a proposal must notify integrators by signed webhook (PRD FR-30 to FR-32). A receiver may be slow or down. The approval must never be lost, delayed by the receiver, or committed without its notification, and NFR-3 requires that no approved action lack its outbox message.

## Decision

`ProposedAction.Decide` raises a domain event that carries ids and the decision kind only. Application owns the wire shape, `WebhookEventDto`, and the builder that fills it. `AppDbContext.SaveChangesAsync` calls that builder, writes one `OutboxMessage` per active matching subscription, and commits them in the same transaction as the Tracked Action. A `BackgroundService` in the API process leases rows: it claims a batch with `FOR UPDATE SKIP LOCKED`, advances each row's next attempt time by the lease length, and commits before any HTTP call. It then signs with HMAC-SHA256, posts with a 10-second timeout, and retries on the FR-32 schedule until Delivered or Dead (spine AD-8).

## Alternatives considered

- **Direct HTTP call inside the approval request.** Rejected. The request would block on the receiver, a receiver failure would either fail the approval or lose the event, and a retry would need a queue anyway.
- **A message broker (RabbitMQ, Azure Service Bus).** Rejected for v1. It adds a container and an operational dependency for one event type and one subscriber. The outbox gives at-least-once delivery from the database already in use.
- **A separate worker container from day one.** Deferred. The dispatcher class is the same; hosting it in the API process saves an image and a CD step. The scale path is to move the registration into a worker project.

## Consequences

- Delivery is at-least-once; receivers use the event id header to deduplicate.
- Ordering is best-effort per subscription, so one failing message does not block the rest.
- The demo can show the outbox row, the retry, and the receiver page live.
