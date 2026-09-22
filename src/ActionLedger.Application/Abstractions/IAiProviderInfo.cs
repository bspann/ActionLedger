namespace ActionLedger.Application.Abstractions;

/// <summary>
/// AD-11 — which provider is actually wired up, read-only. Story 2.6 serves it from
/// <c>GET /api/v1/ai/provider</c>, and Meeting Detail's progress caption names both values while a
/// run is in flight.
/// </summary>
/// <remarks>
/// The port is in Application rather than in the Api because the values come from the ring that
/// built the chat client: the provider is the configured <c>Ai:Provider</c> and the model is what
/// the active factory reports. Nothing here reads configuration, and nothing here is a secret — an
/// endpoint, a key, or a base URL never crosses this port (NFR-4).
/// </remarks>
public interface IAiProviderInfo
{
    /// <summary>The configured provider name: <c>Fake</c>, <c>LocalOpenAI</c> or <c>AzureOpenAI</c>.</summary>
    string Provider { get; }

    /// <summary>
    /// The model the active provider answers with. Always a real, stable string — Run Detail
    /// renders it, so the Fake reports <c>fixture-catalog</c> rather than a blank.
    /// </summary>
    string Model { get; }
}
