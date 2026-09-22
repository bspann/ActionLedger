using ActionLedger.Domain.Common;

namespace ActionLedger.Domain.Webhooks;

/// <summary>
/// AD-8 — a receiver that asked to be told about some event types. The outbox pipeline writes one
/// <see cref="OutboxMessage"/> per active subscription whose <see cref="EventTypes"/> name the event.
/// </summary>
/// <remarks>
/// Nothing mutates a subscription yet. Story 5.2 seeds the demo's one, and there is no endpoint that
/// manages them.
/// </remarks>
public sealed class WebhookSubscription : AggregateRoot
{
    /// <summary>The longest url the <c>url</c> column holds.</summary>
    public const int UrlMaxLength = 2048;

    /// <summary>The longest shared secret the <c>secret</c> column holds.</summary>
    public const int SecretMaxLength = 256;

    /// <summary>The longest single event type name, such as <c>action.approved</c>.</summary>
    public const int EventTypeMaxLength = 64;

    private WebhookSubscription(string url, string secret, IReadOnlyList<string> eventTypes, bool isActive)
    {
        Url = url;
        Secret = secret;
        EventTypes = eventTypes;
        IsActive = isActive;
    }

    /// <summary>Rehydration constructor for EF Core.</summary>
    private WebhookSubscription()
    {
        Url = string.Empty;
        Secret = string.Empty;
        EventTypes = [];
    }

    /// <summary>Where deliveries are POSTed. An absolute http or https URI.</summary>
    public string Url { get; private set; }

    /// <summary>The HMAC key Epic 5 signs deliveries with. Never part of a payload.</summary>
    public string Secret { get; private set; }

    /// <summary>Whether deliveries are enqueued for this receiver at all.</summary>
    public bool IsActive { get; private set; }

    /// <summary>The event type names this receiver wants, such as <c>action.approved</c>. Stored as <c>text[]</c>.</summary>
    public IReadOnlyList<string> EventTypes { get; private set; }

    /// <summary>Creates a subscription.</summary>
    /// <param name="url">The absolute http or https URI deliveries go to.</param>
    /// <param name="secret">The shared signing secret.</param>
    /// <param name="eventTypes">At least one event type name; each trimmed.</param>
    /// <param name="isActive">Whether deliveries are enqueued for it.</param>
    /// <exception cref="DomainRuleException">
    /// The url is not an absolute http or https URI, the secret is blank, or the event type list is
    /// empty or holds a blank entry — or any of them is too long.
    /// </exception>
    public static WebhookSubscription Create(string url, string secret, IEnumerable<string> eventTypes, bool isActive) =>
        new(RequireUrl(url), RequireSecret(secret), RequireEventTypes(eventTypes), isActive);

    private static string RequireUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            throw new DomainRuleException("A webhook subscription's url must be an absolute http or https URI.");
        }

        string trimmed = url.Trim();

        return trimmed.Length > UrlMaxLength
            ? throw new DomainRuleException($"A webhook subscription's url cannot exceed {UrlMaxLength} characters.")
            : trimmed;
    }

    private static string RequireSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new DomainRuleException("A webhook subscription's secret is required.");
        }

        return secret.Length > SecretMaxLength
            ? throw new DomainRuleException($"A webhook subscription's secret cannot exceed {SecretMaxLength} characters.")
            : secret;
    }

    private static IReadOnlyList<string> RequireEventTypes(IEnumerable<string> eventTypes)
    {
        List<string> types = [.. (eventTypes ?? []).Select(RequireEventType)];

        return types.Count == 0
            ? throw new DomainRuleException("A webhook subscription must name at least one event type.")
            : types;
    }

    private static string RequireEventType(string eventType)
    {
        string trimmed = (eventType ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw new DomainRuleException("A webhook subscription's event type cannot be blank.");
        }

        return trimmed.Length > EventTypeMaxLength
            ? throw new DomainRuleException($"A webhook subscription's event type cannot exceed {EventTypeMaxLength} characters.")
            : trimmed;
    }
}
