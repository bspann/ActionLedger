namespace ActionLedger.Application.Ai;

/// <summary>
/// FR-7 / AD-11 — what <c>GET /api/v1/ai/provider</c> answers with: the provider that is actually
/// wired up and the model it answers with. Meeting Detail names both in its in-flight caption.
/// </summary>
/// <remarks>
/// Nothing else crosses here. An endpoint, a key, or a base URL is configuration the caller has no
/// use for, and publishing one would be a leak (NFR-4).
/// </remarks>
/// <param name="Provider">The configured provider name: <c>Fake</c>, <c>LocalOpenAI</c> or <c>AzureOpenAI</c>.</param>
/// <param name="Model">The model the active provider reports; <c>fixture-catalog</c> for the Fake.</param>
public sealed record AiProviderDto(string Provider, string Model);
