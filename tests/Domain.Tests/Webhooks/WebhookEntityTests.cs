using ActionLedger.Domain.Common;
using ActionLedger.Domain.Webhooks;
using Xunit;

namespace ActionLedger.Domain.Tests.Webhooks;

/// <summary>
/// AD-8 — the two webhook roots Story 3.3 adds: a subscription refuses to exist without a usable
/// url, a secret and at least one event type, and a message is enqueued Pending, unattempted and due.
/// </summary>
public sealed class WebhookEntityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 15, 5, 12, TimeSpan.Zero);

    [Fact]
    public void A_subscription_keeps_what_it_was_created_with()
    {
        WebhookSubscription subscription = WebhookSubscription.Create(
            "https://receiver.example/hooks", "s3cret", [" action.approved "], isActive: true);

        Assert.NotEqual(Guid.Empty, subscription.Id);
        Assert.Equal(7, subscription.Id.Version);
        Assert.Equal("https://receiver.example/hooks", subscription.Url);
        Assert.Equal("s3cret", subscription.Secret);
        Assert.Equal(["action.approved"], subscription.EventTypes);
        Assert.True(subscription.IsActive);
        Assert.Empty(subscription.DomainEvents);
    }

    [Fact]
    public void An_inactive_subscription_can_be_created() =>
        Assert.False(WebhookSubscription.Create("http://localhost:5050/", "s", ["action.approved"], isActive: false).IsActive);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/hooks")]
    [InlineData("receiver.example/hooks")]
    [InlineData("ftp://receiver.example/hooks")]
    [InlineData("file:///etc/passwd")]
    public void A_url_that_is_not_absolute_http_or_https_is_refused(string url) =>
        Assert.Throws<DomainRuleException>(() => WebhookSubscription.Create(url, "s3cret", ["action.approved"], isActive: true));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_secret_is_refused(string secret) =>
        Assert.Throws<DomainRuleException>(() =>
            WebhookSubscription.Create("https://receiver.example/hooks", secret, ["action.approved"], isActive: true));

    [Fact]
    public void An_empty_event_type_list_is_refused() =>
        Assert.Throws<DomainRuleException>(() =>
            WebhookSubscription.Create("https://receiver.example/hooks", "s3cret", [], isActive: true));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_event_type_is_refused(string blank) =>
        Assert.Throws<DomainRuleException>(() =>
            WebhookSubscription.Create("https://receiver.example/hooks", "s3cret", ["action.approved", blank], isActive: true));

    [Fact]
    public void A_message_is_enqueued_pending_unattempted_and_due_now()
    {
        Guid subscriptionId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();

        OutboxMessage message = OutboxMessage.Enqueue(subscriptionId, "action.approved", eventId, "{\"eventId\":\"x\"}", Now);

        Assert.Equal(7, message.Id.Version);
        Assert.NotEqual(eventId, message.Id);
        Assert.Equal(subscriptionId, message.SubscriptionId);
        Assert.Equal("action.approved", message.EventType);
        Assert.Equal(eventId, message.EventId);
        Assert.Equal("{\"eventId\":\"x\"}", message.Payload);
        Assert.Equal(OutboxState.Pending, message.State);
        Assert.Equal(0, message.AttemptCount);
        Assert.Equal(Now, message.NextAttemptAt);
        Assert.Equal(Now, message.CreatedAt);
        Assert.Null(message.LastStatusCode);
        Assert.Null(message.LastError);
    }

    [Fact]
    public void Two_messages_for_one_event_share_its_id_but_not_their_own()
    {
        Guid eventId = Guid.CreateVersion7();

        OutboxMessage first = OutboxMessage.Enqueue(Guid.CreateVersion7(), "action.approved", eventId, "{}", Now);
        OutboxMessage second = OutboxMessage.Enqueue(Guid.CreateVersion7(), "action.approved", eventId, "{}", Now);

        Assert.Equal(first.EventId, second.EventId);
        Assert.NotEqual(first.Id, second.Id);
    }
}
