using System.Text.Json;
using ActionLedger.Application.Webhooks;
using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Extraction;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ActionLedger.Api.Tests;

/// <summary>
/// AD-8 — the outbox payload is serialized "with the API's JSON options". The Application ring
/// cannot see the host's options, so <see cref="WebhookJson"/> restates them; this pins the
/// restatement to the configured host, so an integrator never reads JSON shaped differently from
/// what the api answers with.
/// </summary>
public sealed class WebhookJsonTests
{
    [Fact]
    public async Task A_webhook_event_serializes_exactly_as_the_host_would_serialize_it()
    {
        await using TestApi api = new();

        JsonSerializerOptions host = api.Services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;

        WebhookEventDto dto = new(
            Guid.CreateVersion7(),
            WebhookEventTypes.ActionApproved,
            new DateTimeOffset(2026, 9, 21, 14, 3, 22, TimeSpan.Zero),
            new WebhookTrackedAction(
                Guid.CreateVersion7(), "Order the scanners & cables.", Guid.CreateVersion7(), "Dana Whitfield",
                new DateOnly(2026, 10, 3), ActionStatus.Open, Guid.CreateVersion7(), "Weekly sync"),
            new WebhookProposedAction(
                Guid.CreateVersion7(), "Order the scanners.", "Dana", null, 0.82, "Dana will order the scanners."),
            new WebhookReviewDecision(
                DecisionKind.Edited, Guid.CreateVersion7(), null, new DateTimeOffset(2026, 9, 21, 14, 3, 21, TimeSpan.Zero)));

        string expected = JsonSerializer.Serialize(dto, host);
        string actual = JsonSerializer.Serialize(dto, WebhookJson.SerializerOptions);

        Assert.Equal(expected, actual);

        // And the shape is the one the addendum publishes: camelCase names, enums as their names.
        Assert.Contains("\"trackedAction\":", actual, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"Open\"", actual, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"Edited\"", actual, StringComparison.Ordinal);
    }
}
