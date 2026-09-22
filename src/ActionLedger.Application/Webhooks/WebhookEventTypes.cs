using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Common;

namespace ActionLedger.Application.Webhooks;

/// <summary>
/// AD-8 — which domain events are webhook events, and under what name. The outbox pipeline asks
/// <see cref="For"/> about every event it drains; a <c>null</c> answer means no outbox row.
/// </summary>
public static class WebhookEventTypes
{
    /// <summary>A human approved a proposal, as it stood or edited, and a Tracked Action exists.</summary>
    public const string ActionApproved = "action.approved";

    /// <summary>The webhook event type for <paramref name="domainEvent"/>, or <c>null</c> when it has none.</summary>
    public static string? For(DomainEvent domainEvent) => domainEvent switch
    {
        TrackedActionCreated => ActionApproved,
        _ => null,
    };
}
