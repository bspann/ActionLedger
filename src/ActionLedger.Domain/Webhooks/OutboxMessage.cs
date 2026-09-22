using ActionLedger.Domain.Common;

namespace ActionLedger.Domain.Webhooks;

/// <summary>
/// AD-8 / ADR-005 — one delivery owed to one subscription for one event. Written in the same
/// transaction as the change that raised the event (NFR-3), so an approval can never commit without
/// the record Epic 5's dispatcher delivers from.
/// </summary>
/// <remarks>
/// <para>
/// Every message for one event shares its <see cref="EventId"/>, which is the receiver's
/// idempotency key; each message has its own <see cref="AggregateRoot.Id"/>.
/// </para>
/// <para>
/// Only <see cref="Enqueue"/> exists here. Story 5.1 adds <c>MarkDelivered</c> and
/// <c>RecordFailure</c>, which is also why this root carries an <c>xmin</c> token: two dispatchers
/// may race one row.
/// </para>
/// </remarks>
public sealed class OutboxMessage : AggregateRoot
{
    private OutboxMessage(Guid subscriptionId, string eventType, Guid eventId, string payload, DateTimeOffset now)
    {
        SubscriptionId = subscriptionId;
        EventType = eventType;
        EventId = eventId;
        Payload = payload;
        State = OutboxState.Pending;
        AttemptCount = 0;
        NextAttemptAt = now;
        CreatedAt = now;
    }

    /// <summary>Rehydration constructor for EF Core.</summary>
    private OutboxMessage()
    {
        EventType = string.Empty;
        Payload = string.Empty;
    }

    /// <summary>The <see cref="WebhookSubscription"/> this delivery is for.</summary>
    public Guid SubscriptionId { get; private set; }

    /// <summary>The event type name, such as <c>action.approved</c>.</summary>
    public string EventType { get; private set; }

    /// <summary>The event's id, shared by every message the event produced. Stable across retries.</summary>
    public Guid EventId { get; private set; }

    /// <summary>
    /// The serialized request body. Stored as <c>jsonb</c> (AD-10), which keeps its meaning but not
    /// its bytes: PostgreSQL normalizes key order and whitespace on read. The dispatcher sends, and
    /// signs, the JSON it reads back, so the signature always covers the bytes actually delivered.
    /// </summary>
    public string Payload { get; private set; }

    /// <summary>Where the delivery stands.</summary>
    public OutboxState State { get; private set; }

    /// <summary>How many delivery attempts have been made.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>The earliest instant the dispatcher may try again.</summary>
    public DateTimeOffset NextAttemptAt { get; private set; }

    /// <summary>The HTTP status the last attempt got, or <c>null</c> when there was none.</summary>
    public int? LastStatusCode { get; private set; }

    /// <summary>What went wrong on the last attempt, or <c>null</c>.</summary>
    public string? LastError { get; private set; }

    /// <summary>The instant the message was written, from <c>IClock</c> (AD-15).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A Pending message with no attempts, due now.</summary>
    /// <param name="subscriptionId">The subscription it is owed to.</param>
    /// <param name="eventType">The event type name.</param>
    /// <param name="eventId">The event's id, shared by every message for the event.</param>
    /// <param name="payload">The serialized body.</param>
    /// <param name="now">The commit's instant, from <c>IClock</c>.</param>
    public static OutboxMessage Enqueue(Guid subscriptionId, string eventType, Guid eventId, string payload, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        return new OutboxMessage(subscriptionId, eventType, eventId, payload, now.ToUniversalTime());
    }
}
