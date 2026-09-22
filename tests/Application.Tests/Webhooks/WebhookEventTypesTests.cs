using ActionLedger.Application.Webhooks;
using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Common;
using ActionLedger.Domain.Extraction;
using Xunit;

namespace ActionLedger.Application.Tests.Webhooks;

/// <summary>
/// AD-8 — the map from domain event to webhook event type. A Tracked Action's creation is
/// <c>action.approved</c>; anything else is no webhook at all, which is how the outbox pipeline
/// knows to write nothing for it.
/// </summary>
public sealed class WebhookEventTypesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 15, 5, 12, TimeSpan.Zero);

    [Theory]
    [InlineData(DecisionKind.Approved)]
    [InlineData(DecisionKind.Edited)]
    public void A_created_tracked_action_is_action_approved(DecisionKind kind)
    {
        TrackedActionCreated created = new(Guid.CreateVersion7(), Guid.CreateVersion7(), kind) { OccurredAt = Now };

        Assert.Equal("action.approved", WebhookEventTypes.For(created));
        Assert.Equal(WebhookEventTypes.ActionApproved, WebhookEventTypes.For(created));
    }

    [Fact]
    public void Any_other_event_has_no_webhook_type() =>
        Assert.Null(WebhookEventTypes.For(new SomethingElseHappened { OccurredAt = Now }));

    private sealed record SomethingElseHappened : DomainEvent;
}
