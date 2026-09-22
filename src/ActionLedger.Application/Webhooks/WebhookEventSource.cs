using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Common;
using ActionLedger.Domain.Extraction;

namespace ActionLedger.Application.Webhooks;

/// <summary>
/// What the outbox pipeline hands <see cref="IWebhookPayloadBuilder"/>: the event and the two
/// tracked entities it concerns, whose values exist only in memory until the commit lands.
/// </summary>
/// <param name="Event">The raised event.</param>
/// <param name="TrackedAction">The Tracked Action the event was raised on, not yet saved.</param>
/// <param name="ProposedAction">The decided proposal, carrying its not-yet-saved decision copy.</param>
public sealed record WebhookEventSource(DomainEvent Event, TrackedAction TrackedAction, ProposedAction ProposedAction);
