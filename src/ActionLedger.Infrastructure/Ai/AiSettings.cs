namespace ActionLedger.Infrastructure.Ai;

/// <summary>
/// What the AI ring needs from configuration, handed to it by the composition root.
/// </summary>
/// <remarks>
/// <para>
/// The keys themselves (<c>Ai:Provider</c>, <c>Ai:PromptVersion</c>, <c>Ai:CallTimeoutSeconds</c>)
/// are bound and validated by <c>AiOptions</c> in the Api, which is where AD-16's fail-fast lives.
/// This record is the validated values arriving here — Infrastructure reads no configuration of
/// its own, so there is one definition of each key and no second place a default could hide. It is
/// <c>SeedSettings</c>' sibling and exists for the same reason.
/// </para>
/// <para>
/// No secret crosses this record. A base URL, an endpoint or an api key belongs to the provider
/// factory Story 2.7 adds, not to the seam.
/// </para>
/// </remarks>
/// <param name="Provider">
/// AD-11 — which <c>IChatClientFactory</c> the keyed registry resolves. One of <c>Fake</c>,
/// <c>LocalOpenAI</c>, <c>AzureOpenAI</c>; only <c>Fake</c> has a factory until Story 2.7.
/// </param>
/// <param name="PromptVersion">
/// AD-6 — the prompt revision to ask the catalog for, for example <c>v1</c>. Nullable although
/// <c>AiOptions</c> currently requires it, because AD-6 defines <c>Current</c> as the configured
/// version <em>when set, else the highest</em>, and the catalog is the type that implements that
/// rule.
/// </param>
/// <param name="CallTimeoutSeconds">
/// NFR-1 — seconds allowed for one provider call. A run makes at most two, which is what keeps it
/// inside the 180-second ceiling. Handed in here once; never re-read from configuration.
/// </param>
public sealed record AiSettings(string Provider, string? PromptVersion, int CallTimeoutSeconds);
