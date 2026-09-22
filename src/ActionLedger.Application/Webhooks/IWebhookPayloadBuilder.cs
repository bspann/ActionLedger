using ActionLedger.Application.Abstractions;

namespace ActionLedger.Application.Webhooks;

/// <summary>
/// AD-8 — builds the <see cref="WebhookEventDto"/> for one event, inside the commit that raised it.
/// </summary>
/// <remarks>
/// <paramref name="reads"/> in <see cref="BuildAsync"/> is passed rather than injected: the builder
/// is resolved by <c>AppDbContext</c>, and the read seam depends on that context, so injecting it
/// would be a dependency cycle.
/// </remarks>
public interface IWebhookPayloadBuilder
{
    /// <summary>The payload for <paramref name="source"/>, with one read for the already-committed facts.</summary>
    /// <param name="source">The event and the tracked entities it concerns.</param>
    /// <param name="reads">The read seam over the committing context.</param>
    /// <param name="cancellationToken">The commit's cancellation token.</param>
    Task<WebhookEventDto> BuildAsync(WebhookEventSource source, IReadDb reads, CancellationToken cancellationToken = default);
}
